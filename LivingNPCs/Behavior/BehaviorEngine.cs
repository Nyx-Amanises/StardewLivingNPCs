using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace LivingNPCs.Behavior;

/// <summary>
/// Event orchestrator for the behavior layer: subscribes to SMAPI events, runs the small-behavior
/// pipeline, applies queued ValleyTalk exchanges on the main thread, and exposes the mod API
/// surface. Service construction and wiring live in <see cref="BehaviorEngineServices"/>; prompt
/// assembly lives in <see cref="ValleyTalkContextService"/>.
/// </summary>
internal sealed class BehaviorEngine
{
    /// <summary>An exchange reported by ValleyTalk, waiting to be applied on the main thread.</summary>
    private sealed class PendingValleyTalkExchange
    {
        public string NpcName = string.Empty;
        public string NpcDisplayName = string.Empty;
        public string PlayerText = string.Empty;
        public string NpcResponse = string.Empty;
        public string AnalysisJson = string.Empty;

        /// <summary>true = 主机收到的 farmhand 上报（走远程入账路径，见多人 v1）。</summary>
        public bool IsRemote;

        /// <summary>上报者的 UniqueMultiplayerID（仅 IsRemote 时有意义）。</summary>
        public long ReporterPlayerId = -1;
    }

    private const string SaveDataKey = "behavior-memory";

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorEngineServices services;
    private readonly Random random;
    private readonly BehaviorMemory memory;
    private readonly IBehaviorPlanner planner;
    private readonly AiBehaviorClient aiBehaviorClient;
    private readonly BehaviorDebugCommandHandler debugCommands;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;
    private readonly DialogueBehaviorInfluenceRuntime dialogueBehaviorInfluences;
    private readonly DelayedTravelActionRuntime delayedTravelActions;
    private readonly BehaviorMailService mailService;
    private readonly MemoryImpressionService memoryImpressions;
    private readonly GiftActionRuntime giftActions;
    private readonly DirectWorldActionRuntime directWorldActions;
    private readonly CompanionOutingRuntime companionOutings;
    private readonly HelpRequestRewardService helpRequestRewards;
    private readonly HelpRequestQuestLogService helpRequestQuestLog;
    private readonly HelpRequestRuntime helpRequests;
    private readonly ConversationStartRecorder conversationStartRecorder;
    private readonly NpcLocator locator;
    private readonly ValleyTalkContextService contextService;
    private readonly Multiplayer.MultiplayerSyncService multiplayerSync;
    private readonly List<PendingBehaviorRequest> pendingRequests = new();
    private readonly ConcurrentQueue<PendingValleyTalkExchange> pendingValleyTalkExchanges = new();
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly HashSet<string> loggedHandlerExceptions = new();
    private string pendingGiftMailKey = string.Empty;
    private int pendingGiftMailTrackTicks;
    private string activeGiftMailKey = string.Empty;

    public BehaviorEngine(IModHelper helper, IMonitor monitor, ModConfig config, string modUniqueId = "Yuki.LivingNPCs")
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.services = new BehaviorEngineServices(helper, monitor, config, modUniqueId);
        this.services.AfterManualMemoryClear = this.AfterManualMemoryClear;

        this.random = this.services.Random;
        this.memory = this.services.Memory;
        this.planner = this.services.Planner;
        this.aiBehaviorClient = this.services.AiBehaviorClient;
        this.debugCommands = this.services.DebugCommands;
        this.feedback = this.services.Feedback;
        this.communityRipples = this.services.CommunityRipples;
        this.dialogueBehaviorInfluences = this.services.DialogueBehaviorInfluences;
        this.delayedTravelActions = this.services.DelayedTravelActions;
        this.mailService = this.services.MailService;
        this.memoryImpressions = this.services.MemoryImpressions;
        this.giftActions = this.services.GiftActions;
        this.directWorldActions = this.services.DirectWorldActions;
        this.companionOutings = this.services.CompanionOutings;
        this.helpRequestRewards = this.services.HelpRequestRewards;
        this.helpRequestQuestLog = this.services.HelpRequestQuestLog;
        this.helpRequests = this.services.HelpRequests;
        this.conversationStartRecorder = this.services.ConversationStartRecorder;
        this.locator = this.services.Locator;
        this.contextService = this.services.ContextService;
        this.multiplayerSync = this.services.MultiplayerSync;
        this.multiplayerSync.RemoteExchangeReceived = this.EnqueueRemoteValleyTalkExchange;
        this.multiplayerSync.OpenBookFromMirror = this.OpenMemoryBookFromLocalMemory;
    }

    public void RegisterEvents()
    {
        this.helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        this.helper.Events.GameLoop.Saving += this.OnSaving;
        this.helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        this.helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        this.helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        this.helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        this.helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        this.helper.Events.Display.MenuChanged += this.OnMenuChanged;
        this.helper.Events.Content.AssetRequested += this.OnAssetRequested;
        this.helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        this.helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
        this.helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
        this.debugCommands.RegisterConsoleCommands();
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        this.SafeRun("multiplayer message received", () => this.multiplayerSync.OnModMessageReceived(e));
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        this.SafeRun("peer connected", () => this.multiplayerSync.OnPeerConnected(e));
    }

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        this.SafeRun("peer disconnected", () => this.multiplayerSync.OnPeerDisconnected(e));
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.SafeRun("save loaded", () =>
        {
            BehaviorMemorySaveData? saveData;
            if (!Context.IsMainPlayer)
            {
                // SMAPI 的存档数据只有主机可读写；farmhand 用空的本地视图，避免每次读档告警。
                saveData = null;
            }
            else
            {
                try
                {
                    saveData = this.helper.Data.ReadSaveData<BehaviorMemorySaveData>(SaveDataKey);
                }
                catch (Exception ex)
                {
                    this.monitor.Log(
                        I18n.Get("log.save.readFailed", new { error = ex.Message }),
                        LogLevel.Warn
                    );
                    saveData = null;
                }
            }

            this.memory.Load(saveData, this.config.MaxMemoryEntriesPerNpc);
            RsvAiPolicy.RegisterGameThreadAliases();
            this.conversationStartRecorder.Clear();
            if (Context.IsMainPlayer)
            {
                // 账本类操作（礼物信生成/排程、任务栏投影）只属于主机；farmhand 的记忆是
                // 主机镜像，读档后经多人同步请求全量快照（OnSaveLoaded 下面那行）。
                this.mailService.ResolvePendingGiftMailGenerations();
                this.mailService.QueueDueGiftMailsForTomorrow();
            }

            this.mailService.InvalidateMailCache();
            this.helpRequestQuestLog.Sync();
            this.multiplayerSync.OnSaveLoaded();

            if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.save.memoryLoaded"), LogLevel.Debug);
            }
        });
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.SafeRun("saving", () =>
        {
            if (!Context.IsMainPlayer)
            {
                // 行为记忆归主机存档；farmhand 上 WriteSaveData 必抛（此前每次存档都报错且数据丢失）。
                return;
            }

            this.helper.Data.WriteSaveData(SaveDataKey, this.memory.ToSaveData());
            if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.save.memorySaved"), LogLevel.Debug);
            }
        });
    }


    private void AfterManualMemoryClear()
    {
        if (Context.IsMainPlayer)
        {
            this.helper.Data.WriteSaveData(SaveDataKey, this.memory.ToSaveData());
            this.multiplayerSync.BroadcastFullSnapshot();
        }

        this.contextService.ClearImmediateContexts();
        this.helpRequestQuestLog.Sync();
        this.mailService.InvalidateMailCache();
        this.pendingRequests.Clear();
        this.ClearGiftMailTracking();
    }
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        // Runs while the outing day is still current, before OnDayStarted clears the list:
        // outings interrupted by bedtime get their shared-time memory credited here.
        this.SafeRun("day ending", () => this.companionOutings.FinalizeForDayEnd());
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.SafeRun("day started", () =>
        {
            // Memory-impression generation is independent from AI gift mail. Register localized
            // RSV aliases before either subsystem can send saved text to an LLM.
            RsvAiPolicy.RegisterGameThreadAliases();

            // 心智账本的日结（衰减/涟漪/印象压缩/礼物信）只在主机跑：farmhand 的记忆是主机
            // 镜像，本地日结既无意义又会在下一次快照前造成短暂漂移（分屏副屏同理，由主屏跑）。
            bool ledgerOwner = Context.IsMainPlayer;
            if (ledgerOwner && this.config.EnableNpcState)
            {
                this.memory.DecayStates(
                    this.config.NpcStateDailyDecay,
                    this.config.NpcEmotionDailyDecay,
                    this.config.NpcConflictDailyDecay
                );
            }

            this.memory.ResetDaily();
            this.contextService.ClearImmediateContexts();
            this.pendingRequests.Clear();
            this.ClearGiftMailTracking();
            this.companionOutings.Clear();
            this.conversationStartRecorder.Clear();
            this.feedback.Clear();
            this.delayedTravelActions.Clear();
            if (ledgerOwner)
            {
                this.communityRipples.TryPropagate();
                this.mailService.ResolvePendingGiftMailGenerations();
                this.memoryImpressions.ProcessDayStart();
                this.mailService.QueueDueGiftMailsForTomorrow();
            }

            this.mailService.InvalidateMailCache();
            this.helpRequestQuestLog.Sync();
            if (ledgerOwner)
            {
                // 日结后的状态广播给远程 farmhand（无对端时零开销）。
                this.multiplayerSync.BroadcastFullSnapshot();
            }
        });
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!IsMailAsset(e))
        {
            return;
        }

        e.Edit(asset =>
        {
            this.SafeRun("mail asset edit", () =>
            {
                var data = asset.AsDictionary<string, string>().Data;
                int before = data.Count;
                this.mailService.ApplyMailData(data);
                if (this.config.Debug)
                {
                    this.monitor.Log(I18n.Get("log.mail.assetEditApplied", new { added = data.Count - before, total = data.Count }), LogLevel.Debug);
                }
            });
        });
    }

    private static bool IsMailAsset(AssetRequestedEventArgs e)
    {
        // Use NameWithoutLocale so the edit also applies to localized variants such as
        // "Data/mail.zh-CN"; otherwise gift/reward mail entries are never added for non-English
        // players and their letters open to nothing.
        return e.NameWithoutLocale.IsEquivalentTo("Data/mail")
            || string.Equals(e.NameWithoutLocale.BaseName, "Data/mail", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Name.BaseName, "Data/mail", StringComparison.OrdinalIgnoreCase);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.SafeRun("returned to title", () =>
        {
            this.memory.ResetDaily();
            this.pendingValleyTalkExchanges.Clear();
            this.contextService.ClearImmediateContexts();
            this.pendingRequests.Clear();
            this.ClearGiftMailTracking();
            this.companionOutings.Clear();
            this.conversationStartRecorder.Clear();
            this.feedback.Clear();
            this.delayedTravelActions.Clear();
            this.multiplayerSync.OnReturnedToTitle();
        });
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        this.SafeRun("button pressed", () =>
        {
            if (!Context.IsWorldReady || Game1.activeClickableMenu != null)
            {
                return;
            }

            if (this.config.InspectMemoryHotkey.JustPressed())
            {
                this.helper.Input.Suppress(e.Button);
                this.OpenMemoryBook();
                return;
            }

            this.TryRememberGiftMailOpening(e);

            if (!this.config.BehaviorHotkey.JustPressed())
            {
                this.conversationStartRecorder.TryRecord(e);
                return;
            }

            this.helper.Input.Suppress(e.Button);

            if (this.locator.TryFindNearestNpc(out NPC? npc) && npc != null)
            {
                this.QueueOrExecute(npc, BehaviorTrigger.Manual, "hotkey");
            }
            else
            {
                this.feedback.Show(I18n.Get("hud.noNearbyNpc"));
                if (this.config.Debug)
                {
                    this.monitor.Log(I18n.Get("log.behavior.noNearbyNpcHotkey"), LogLevel.Debug);
                }
            }
        });
    }

    /// <summary>
    /// 打开游戏内记忆手册（原快捷键行为是控制台摘要，现由 livingnpcs_debug 命令承担）。
    /// 远程 farmhand 在主机权威下先向主机请求只读快照，快照到位（或超时降级）后再开书；
    /// 其余场合直接从本地记忆开。还没有任何可展示 NPC 时给 HUD 提示而不是弹一本空书。
    /// </summary>
    private void OpenMemoryBook()
    {
        if (this.multiplayerSync.TryBeginRemoteBookOpen())
        {
            return;
        }

        this.OpenMemoryBookFromLocalMemory();
    }

    /// <summary>从本地记忆（主机真身或 farmhand 镜像）开书；延迟打开时防菜单叠加。</summary>
    private void OpenMemoryBookFromLocalMemory()
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu != null)
        {
            return;
        }

        Ui.MemoryBookMenu? menu = Ui.MemoryBookMenu.TryCreate(this.memory);
        if (menu == null)
        {
            this.feedback.Show(I18n.Get("book.hud.empty"));
            return;
        }

        Game1.activeClickableMenu = menu;
    }

    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        this.SafeRun("menu changed", () =>
        {
            if (e.OldMenu is LetterViewerMenu)
            {
                string mailKey = string.IsNullOrWhiteSpace(this.activeGiftMailKey)
                    ? this.pendingGiftMailKey
                    : this.activeGiftMailKey;
                this.mailService.MarkGiftMailClaimed(mailKey);
                this.activeGiftMailKey = string.Empty;
                this.pendingGiftMailKey = string.Empty;
                this.pendingGiftMailTrackTicks = 0;
            }

            if (e.NewMenu is LetterViewerMenu letter
                && letter.isMail
                && !string.IsNullOrWhiteSpace(this.pendingGiftMailKey))
            {
                this.activeGiftMailKey = this.pendingGiftMailKey;
                this.pendingGiftMailKey = string.Empty;
                this.pendingGiftMailTrackTicks = 0;
                return;
            }

            if (e.NewMenu != null && !string.IsNullOrWhiteSpace(this.pendingGiftMailKey))
            {
                this.pendingGiftMailKey = string.Empty;
                this.pendingGiftMailTrackTicks = 0;
            }
        });
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        this.SafeRun("time changed", () =>
        {
            if (!Context.IsWorldReady)
            {
                return;
            }

            this.helpRequests.UpdateTimers();

            if (!this.config.EnablePassiveBehaviors || Game1.activeClickableMenu != null)
            {
                return;
            }

            if (this.random.Next(100) >= this.config.PassiveBehaviorChancePercent)
            {
                return;
            }

            if (!this.locator.TryFindNearestNpc(out NPC? npc) || npc == null)
            {
                return;
            }

            if (this.companionOutings.HasActiveOuting(npc))
            {
                return;
            }

            this.QueueOrExecute(npc, BehaviorTrigger.Passive, "passive");
        });
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
        {
            return;
        }

        this.SafeRun("update tick: multiplayer sync", () => this.multiplayerSync.OnUpdateTicked());
        this.SafeRun("update tick: valleytalk exchanges", this.ProcessPendingValleyTalkExchanges);
        this.SafeRun("update tick: pending gift verifications", () => this.conversationStartRecorder.ProcessPendingGiftVerifications());
        this.SafeRun("update tick: pending behavior requests", this.ProcessPendingBehaviorRequests);
        this.SafeRun("update tick: gift mail tracking", this.TryTrackGiftMailOpening);
        this.SafeRun("update tick: HUD messages", () => this.feedback.TryShowPendingHudMessages());
        this.SafeRun("update tick: ambient remarks", () => this.feedback.TryShowPendingAmbientRemarks());
        this.SafeRun("update tick: delayed travel actions", () => this.delayedTravelActions.TryStartPending());
        this.SafeRun("update tick: companion outings", () => this.companionOutings.TryUpdatePending());
        if (e.IsMultipleOf(120))
        {
            this.SafeRun("update tick: dialogue behavior influences", () => this.dialogueBehaviorInfluences.TryApply());
            this.SafeRun("update tick: help request follow-ups", () => this.helpRequests.ShowFollowUps());
        }
    }

    private void ProcessPendingBehaviorRequests()
    {
        // Runs every update tick; skip the LINQ allocation entirely in the common empty case.
        if (this.pendingRequests.Count == 0)
        {
            return;
        }

        foreach (var request in this.pendingRequests.Where(request => request.Task.IsCompleted).ToList())
        {
            this.pendingRequests.Remove(request);

            if (request.TotalDays != Game1.Date.TotalDays || !this.locator.TryFindNpcInCurrentLocation(request.NpcName, out NPC? npc) || npc == null)
            {
                continue;
            }

            if (request.Source == "passive" && this.companionOutings.HasActiveOuting(npc))
            {
                continue;
            }

            BehaviorIntent intent = request.FallbackIntent;
            if (request.Task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && request.Task.Result != null)
            {
                intent = request.Task.Result;
            }

            this.TryExecute(npc, intent, request.Source);
        }
    }

    private void SafeRun(string context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            string signature = $"{context}|{ex.GetType().FullName}|{ex.Message}";
            if (this.loggedHandlerExceptions.Add(signature))
            {
                this.monitor.Log(I18n.Get("log.error.recovered", new { context, error = ex }), LogLevel.Error);
            }
            else if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.error.recurred", new { context, error = ex.Message }), LogLevel.Trace);
            }
        }
    }

    private void QueueOrExecute(NPC npc, BehaviorTrigger trigger, string source)
    {
        if (RsvAiPolicy.IsBlockedNpc(npc))
        {
            return;
        }

        var fallbackIntent = this.planner.ChooseIntent(npc, trigger);
        if (!this.aiBehaviorClient.CanUse || this.HasPendingRequest(npc.Name))
        {
            this.TryExecute(npc, fallbackIntent, source);
            return;
        }

        var task = this.aiBehaviorClient.ChooseIntentAsync(npc, trigger, this.cancellationTokenSource.Token);
        this.pendingRequests.Add(new PendingBehaviorRequest(
            npc.Name,
            trigger,
            source,
            Game1.Date.TotalDays,
            fallbackIntent,
            task
        ));

        if (this.config.Debug)
        {
            this.monitor.Log(I18n.Get("log.aiPlanner.behaviorQueued", new { npc = npc.Name }), LogLevel.Debug);
        }

        if (trigger == BehaviorTrigger.Manual)
        {
            this.feedback.Show(I18n.Get("hud.planningBehavior", new { npc = npc.displayName }));
        }
    }

    private bool HasPendingRequest(string npcName)
    {
        return this.pendingRequests.Any(request => request.NpcName == npcName);
    }

    public bool RecordValleyTalkExchange(string npcName, string npcDisplayName, string playerText, string npcResponse, string analysisJson)
    {
        // These checks only touch the immutable arguments and config flags, so they are safe on
        // any thread.
        if (RsvAiPolicy.IsBlockedNpcName(npcName)
            || !this.config.EnableConversationMemory
            || string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        // ValleyTalk calls this from LLM-response continuations on thread-pool threads (desktop
        // has no SynchronizationContext). Everything downstream mutates net-synced game state
        // (friendship, inventory, money, quest log) and the shared behavior memory, so defer to
        // the next UpdateTicked on the main thread. The bridge treats this as fire-and-forget;
        // the return value only means the exchange was queued.
        this.pendingValleyTalkExchanges.Enqueue(new PendingValleyTalkExchange
        {
            NpcName = npcName,
            NpcDisplayName = npcDisplayName,
            PlayerText = playerText,
            NpcResponse = npcResponse ?? string.Empty,
            AnalysisJson = analysisJson ?? string.Empty
        });
        return true;
    }

    private void ProcessPendingValleyTalkExchanges()
    {
        while (this.pendingValleyTalkExchanges.TryDequeue(out PendingValleyTalkExchange? exchange))
        {
            if (exchange.IsRemote)
            {
                // 主机：farmhand 上报的交换走远程入账（唯一写者）。
                this.ApplyRemoteValleyTalkExchange(exchange);
                continue;
            }

            if (this.multiplayerSync.UseHostAuthority
                && this.multiplayerSync.TrySendExchangeReport(
                    exchange.NpcName,
                    exchange.PlayerText,
                    exchange.NpcResponse,
                    exchange.AnalysisJson))
            {
                // farmhand：上报主机统一入账；好感与随境跟话等回执到达后在本地应用。
                continue;
            }

            // 主机本人，或 farmhand 的降级路径（主机未装本 mod / 同步关闭 / 瞬时无主机）：
            // 维持旧行为——本地入账（farmhand 侧不落盘，仅当次会话有效）。
            this.ApplyValleyTalkExchange(exchange);
        }
    }

    /// <summary>主机收到 farmhand 交换上报（多人同步回调，主线程）：过本机配置门后入队统一消费。</summary>
    private void EnqueueRemoteValleyTalkExchange(long reporterPlayerId, Multiplayer.ExchangeReportMessage message)
    {
        if (RsvAiPolicy.IsBlockedNpcName(message.NpcName)
            || !this.config.EnableConversationMemory
            || string.IsNullOrWhiteSpace(message.PlayerText))
        {
            return;
        }

        this.pendingValleyTalkExchanges.Enqueue(new PendingValleyTalkExchange
        {
            NpcName = message.NpcName,
            NpcDisplayName = message.NpcName,
            PlayerText = message.PlayerText,
            NpcResponse = message.NpcResponse ?? string.Empty,
            AnalysisJson = message.AnalysisJson ?? string.Empty,
            IsRemote = true,
            ReporterPlayerId = reporterPlayerId
        });
    }

    /// <summary>
    /// 主机代 farmhand 入账（多人 v1）：只写心智账本——记忆/偏好/冲突/情绪/行为倾向与好感
    /// 增量；世界动作与求助字段在入账前剥除（<see cref="Multiplayer.RemoteExchangePolicy"/>，
    /// 含出游请求，丢弃并记 Trace）。好感增量与随境跟话属于上报玩家，经回执发回其本地应用；
    /// NPC 用全图查找——farmhand 的对话地点通常不是主机所在地点。
    /// </summary>
    private void ApplyRemoteValleyTalkExchange(PendingValleyTalkExchange exchange)
    {
        NPC? npc = Game1.getCharacterFromName(exchange.NpcName);
        if (npc == null || RsvAiPolicy.IsBlockedNpc(npc))
        {
            return;
        }

        string sanitizedAnalysis = Multiplayer.RemoteExchangePolicy.SanitizeAnalysisJson(
            exchange.AnalysisJson,
            out List<string> droppedActionTypes);
        if (droppedActionTypes.Count > 0)
        {
            this.monitor.Log(
                I18n.Get(
                    "log.mp.remoteActionsDropped",
                    new { npc = npc.Name, types = string.Join(", ", droppedActionTypes) }),
                LogLevel.Trace);
        }

        var result = this.memory.RecordValleyTalkExchange(
            npc,
            exchange.PlayerText,
            exchange.NpcResponse,
            sanitizedAnalysis,
            this.config.MaxMemoryEntriesPerNpc,
            0,
            this.config.HelpRequestCooldownDays,
            this.config.EnableAiDialogueFriendship ? this.config.MaxAiDialogueFriendshipPerNpcPerDay : 0,
            this.config.MaxDialogueBehaviorInfluenceDays,
            allowHelpRequestProgress: false
        );

        this.multiplayerSync.SendExchangeAck(
            exchange.ReporterPlayerId,
            new Multiplayer.ExchangeAckMessage
            {
                NpcName = exchange.NpcName,
                FriendshipDelta = result.AppliedFriendshipDelta,
                AmbientText = this.config.EnableDialogueFollowUps ? result.AmbientFollowUpText : string.Empty,
                AmbientDelayMinutes = result.AmbientFollowUpDelayMinutes
            });
        this.multiplayerSync.BroadcastNpcView(exchange.NpcName);

        if (this.config.Debug)
        {
            this.monitor.Log(
                I18n.Get(
                    "log.mp.remoteExchangeApplied",
                    new
                    {
                        player = exchange.ReporterPlayerId,
                        npc = npc.Name,
                        memories = result.LongTermMemoriesStored,
                        preferences = result.PlayerPreferencesStored,
                        conflicts = result.ConflictsStored,
                        influences = result.BehaviorInfluencesStored,
                        friendship = result.AppliedFriendshipDelta
                    }),
                LogLevel.Debug);
        }
    }

    private void ApplyValleyTalkExchange(PendingValleyTalkExchange exchange)
    {
        if (!this.locator.TryFindNpcInCurrentLocation(exchange.NpcName, out NPC? npc) || npc == null)
        {
            return;
        }

        string playerText = exchange.PlayerText;
        string npcResponse = exchange.NpcResponse;
        var result = this.memory.RecordValleyTalkExchange(
            npc,
            playerText,
            npcResponse,
            exchange.AnalysisJson,
            this.config.MaxMemoryEntriesPerNpc,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.EnableAiDialogueFriendship ? this.config.MaxAiDialogueFriendshipPerNpcPerDay : 0,
            this.config.MaxDialogueBehaviorInfluenceDays
        );

        // 主机本人交换入账后同样刷新远程 farmhand 的该 NPC 镜像（无对端时零开销）。
        this.multiplayerSync.BroadcastNpcView(exchange.NpcName);

        if (!result.HasEffect)
        {
            if (this.config.EnableAiWorldActions)
            {
                this.TryExecuteConversationActions(npc, result.Actions, playerText, npcResponse);
            }

            return;
        }

        if (result.AppliedFriendshipDelta > 0)
        {
            Game1.player.changeFriendship(result.AppliedFriendshipDelta, npc);
        }

        if (result.FulfilledHelpRequests.Count > 0)
        {
            this.helpRequestRewards.RewardFulfilled(npc, result.FulfilledHelpRequests);
        }

        if (result.HelpRequestsStored > 0 || result.HelpRequestsUpdated > 0)
        {
            this.helpRequestQuestLog.Sync();
            foreach (NpcHelpRequestFact request in result.ActivatedHelpRequests)
            {
                string item = string.IsNullOrWhiteSpace(request.RequestedItemLabel)
                    ? request.Summary
                    : request.RequestedItemLabel;
                this.feedback.ShowAfterDialogue(
                    I18n.Get(
                        "help.quest.acceptedHud",
                        new
                        {
                            npc = string.IsNullOrWhiteSpace(request.NpcDisplayName)
                                ? npc.displayName
                                : request.NpcDisplayName,
                            item
                        })
                );
            }
        }

        if (this.config.EnableDialogueFollowUps && !string.IsNullOrWhiteSpace(result.AmbientFollowUpText))
        {
            this.feedback.QueueAmbientRemark(npc, result.AmbientFollowUpText, result.AmbientFollowUpDelayMinutes);
        }

        if (this.config.EnableAiWorldActions)
        {
            this.TryExecuteConversationActions(npc, result.Actions, playerText, npcResponse);
        }

        this.contextService.PushInteractionContext(
            npc,
            I18n.Get(
                "log.context.exchangeRecorded",
                new
                {
                    npc = npc.Name,
                    longTerm = result.LongTermMemoriesStored,
                    preferences = result.PlayerPreferencesStored,
                    helpRequests = result.HelpRequestsStored,
                    helpUpdates = result.HelpRequestsUpdated,
                    conflicts = result.ConflictsStored,
                    influences = result.BehaviorInfluencesStored,
                    resolved = result.ConflictsResolved,
                    friendship = result.AppliedFriendshipDelta
                })
        );
    }

    public string GetConversationContext(string npcName, string npcDisplayName)
    {
        if (!this.config.EnableConversationMemory || string.IsNullOrWhiteSpace(npcName))
        {
            return string.Empty;
        }

        if (!this.locator.TryFindNpcInCurrentLocation(npcName, out NPC? npc) || npc == null)
        {
            return string.Empty;
        }

        return this.contextService.BuildPromptContext(npc);
    }

    public string GetGiftResponseContext(string npcName, string npcDisplayName, string giftItemId, string giftName, int taste)
    {
        if (!this.config.EnableConversationMemory || !this.config.EnableNpcState)
        {
            return string.Empty;
        }

        if (!this.locator.TryFindNpcInCurrentLocation(npcName, out NPC? npc) || npc == null)
        {
            return string.Empty;
        }

        return this.contextService.BuildGiftResponseContext(npc, giftItemId, giftName, taste);
    }

    private void TryExecuteConversationActions(
        NPC npc,
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        // A daily gift opportunity authorizes exactly one generated reply, not every later turn in
        // the same conversation. Execute that reply's action first, then consume the opportunity
        // whether the model used it, skipped it, or named an invalid item. Generation failures never
        // reach this method, so a failed request can still retry safely.
        LivingNpcState? giftOpportunityState = this.memory.GetState(npc);
        bool consumeDailyGiftOpportunity = giftOpportunityState?.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays;

        foreach (var action in this.BuildEffectiveConversationActions(npc, actions, playerText, npcResponse).Take(1))
        {
            if (action.Type == "companion_outing")
            {
                action.DelayMinutes = System.Math.Max(action.DelayMinutes, ConversationActionCueRules.DetectPreparationDelayMinutes(npcResponse));
                if (action.DelayMinutes > 0)
                {
                    this.feedback.ClearAmbientRemarksForNpc(npc.Name);
                    this.delayedTravelActions.Queue(npc, action);
                    continue;
                }
            }

            string actionReason = string.Empty;
            bool executed = action.Type switch
            {
                "give_small_gift" => this.giftActions.TryGiveSmallGift(npc, action, playerText, npcResponse, out actionReason),
                "give_meaningful_gift" => this.giftActions.TryGiveMeaningfulGift(npc, action, playerText, npcResponse, out actionReason),
                "give_money" => this.giftActions.TryGiveMoney(npc, action, out actionReason),
                "companion_outing" => this.companionOutings.TryStart(npc, action, out actionReason),
                "festival_interaction" => this.directWorldActions.TryFestivalInteraction(npc, action, out actionReason),
                _ => false
            };

            if (!executed && this.config.Debug)
            {
                if (string.IsNullOrWhiteSpace(actionReason))
                {
                    actionReason = I18n.Get("log.worldAction.rejected");
                }

                this.monitor.Log(I18n.Get("log.worldAction.skipped", new { type = action.Type, npc = npc.Name, reason = actionReason }), LogLevel.Debug);
            }
        }

        if (consumeDailyGiftOpportunity && giftOpportunityState != null)
        {
            GiftActionRules.ClearDailyGiftOpportunity(giftOpportunityState);
        }
    }

    private IReadOnlyList<ValleyTalkWorldActionRequest> BuildEffectiveConversationActions(
        NPC npc,
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        if (actions.Count > 0)
        {
            List<string>? dropReasons = this.config.Debug ? new List<string>() : null;
            var visibleSafeActions = ConversationActionCueRules.FilterActionsContradictedByVisibleDialogue(
                actions,
                playerText,
                npcResponse,
                dropReasons
            );
            if (this.config.Debug && visibleSafeActions.Count < actions.Count)
            {
                string removedTypes = string.Join(
                    ", ",
                    actions.Except(visibleSafeActions).Select(action => action.Type)
                );
                this.monitor.Log(
                    I18n.Get(
                        "log.worldAction.filteredByVisibleDialogue",
                        new { npc = npc.Name, count = actions.Count - visibleSafeActions.Count, types = removedTypes }
                    ),
                    LogLevel.Debug
                );
                foreach (string detail in dropReasons ?? [])
                {
                    this.monitor.Log(
                        I18n.Get("log.worldAction.filteredDetail", new { npc = npc.Name, detail }),
                        LogLevel.Debug
                    );
                }
            }

            ConversationActionCueRules.TryCorrectTravelActionTargetFromVisibleDialogue(
                npc,
                visibleSafeActions,
                playerText,
                npcResponse
            );
            return visibleSafeActions;
        }

        if (!ConversationActionCueRules.TryBuildFallbackTravelAction(npc, playerText, npcResponse, out ValleyTalkWorldActionRequest? action)
            || action == null)
        {
            return actions;
        }

        if (this.config.Debug)
        {
            this.monitor.Log(
                I18n.Get("log.worldAction.fallbackTravelSynthesized", new { type = action.Type, target = action.TargetLocation }),
                LogLevel.Debug
            );
        }

        return new[] { action };
    }

    private void TryRememberGiftMailOpening(ButtonPressedEventArgs e)
    {
        if (!e.Button.IsActionButton()
            || Game1.player == null
            || !this.IsMailboxInteraction(e.Cursor)
            || !this.mailService.TryGetCurrentGiftMailInMailbox(out string mailKey, out _))
        {
            return;
        }

        this.pendingGiftMailKey = mailKey;
        this.pendingGiftMailTrackTicks = 60;
    }

    private void TryTrackGiftMailOpening()
    {
        if (string.IsNullOrWhiteSpace(this.pendingGiftMailKey))
        {
            return;
        }

        if (Game1.activeClickableMenu is LetterViewerMenu letter && letter.isMail)
        {
            this.activeGiftMailKey = this.pendingGiftMailKey;
            this.pendingGiftMailKey = string.Empty;
            this.pendingGiftMailTrackTicks = 0;
            return;
        }

        if (Game1.activeClickableMenu != null || this.pendingGiftMailTrackTicks <= 0)
        {
            this.pendingGiftMailKey = string.Empty;
            this.pendingGiftMailTrackTicks = 0;
            return;
        }

        this.pendingGiftMailTrackTicks--;
    }

    private void ClearGiftMailTracking()
    {
        this.pendingGiftMailKey = string.Empty;
        this.pendingGiftMailTrackTicks = 0;
        this.activeGiftMailKey = string.Empty;
    }

    private bool IsMailboxInteraction(ICursorPosition cursor)
    {
        if (Game1.player == null)
        {
            return false;
        }

        Microsoft.Xna.Framework.Point mailbox = Game1.player.getMailboxPosition();
        Microsoft.Xna.Framework.Point facingTile = BehaviorActionExecutor.GetPlayerFacingTile();
        Microsoft.Xna.Framework.Point grabTile = new((int)cursor.GrabTile.X, (int)cursor.GrabTile.Y);
        return facingTile == mailbox || grabTile == mailbox;
    }

    private bool TryExecute(NPC npc, BehaviorIntent intent, string source)
    {
        if (!this.CanExecute(npc, intent, out string reason))
        {
            if (source == "hotkey")
            {
                this.feedback.Show(I18n.Get("hud.behaviorSkipped", new { npc = npc.displayName, behavior = this.DescribeIntent(intent.Type), reason = this.TranslateSkipReason(reason) }));
            }

            if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.behavior.skipped", new { type = intent.Type, npc = npc.Name, reason }), LogLevel.Debug);
            }

            return false;
        }

        bool executed = intent.Type switch
        {
            BehaviorIntentType.FacePlayer => BehaviorActionExecutor.TryFacePlayer(npc, this.config.AllowFacePlayer),
            BehaviorIntentType.Emote => BehaviorActionExecutor.TryEmote(npc, intent.EmoteId, this.config.AllowEmotes, this.config.AllowFacePlayer),
            BehaviorIntentType.ApproachPlayer => BehaviorActionExecutor.TryApproachPlayer(npc, this.config.AllowApproachPlayer, this.config.AllowFacePlayer),
            BehaviorIntentType.Pause => BehaviorActionExecutor.TryPause(npc, this.config.AllowFacePlayer),
            BehaviorIntentType.LookAround => BehaviorActionExecutor.TryLookAround(npc, this.config.AllowFacePlayer, this.random),
            BehaviorIntentType.StepAway => BehaviorActionExecutor.TryStepAway(npc, this.config.AllowApproachPlayer, this.config.AllowFacePlayer),
            _ => false
        };

        if (!executed)
        {
            if (source == "hotkey")
            {
                this.feedback.Show(I18n.Get("hud.behaviorFailed", new { npc = npc.displayName, behavior = this.DescribeIntent(intent.Type) }));
            }

            return false;
        }

        this.memory.Record(npc, intent, this.config.MaxMemoryEntriesPerNpc);
        if (this.config.EnableNpcState)
        {
            this.memory.UpdateStateForBehavior(npc, intent, source);
        }

        // No push channel anymore (WP16): the dialogue engine pulls the behavior context at
        // generation time, so recording the behavior above is all the priming needed.
        if (this.config.Debug)
        {
            this.monitor.Log(I18n.Get("log.behavior.executed", new { type = intent.Type, npc = npc.Name, source }), LogLevel.Debug);
        }

        if (source == "hotkey")
        {
            this.feedback.Show(I18n.Get("hud.behaviorDone", new { npc = npc.displayName, behavior = this.DescribeIntent(intent.Type) }));
        }

        return true;
    }

    private bool CanExecute(NPC npc, BehaviorIntent intent, out string reason)
    {
        if (RsvAiPolicy.IsBlockedNpc(npc))
        {
            reason = "RSV NPCs are excluded from AI behavior";
            return false;
        }

        if (!Game1.IsMasterGame && RequiresHostSimulation(intent.Type))
        {
            // Mirrors the companion-outing host gate: rejecting here (instead of letting the
            // executor fail silently) also skips the memory entry, the state update, and the
            // daily-budget charge that would otherwise describe a movement that never happened.
            reason = "movement behaviors only run for the host player";
            return false;
        }

        if (Game1.eventUp)
        {
            reason = "an event is active";
            return false;
        }

        if (Game1.currentLocation == null || npc.currentLocation != Game1.currentLocation)
        {
            reason = "the NPC is not in the current location";
            return false;
        }

        if (!this.memory.HasDailyBudget(npc, this.config.MaxBehaviorsPerNpcPerDay))
        {
            reason = "daily behavior budget reached";
            return false;
        }

        if (intent.Type == BehaviorIntentType.FacePlayer && !this.config.AllowFacePlayer)
        {
            reason = "facing behavior is disabled";
            return false;
        }

        if (intent.Type == BehaviorIntentType.Emote && !this.config.AllowEmotes)
        {
            reason = "emote behavior is disabled";
            return false;
        }

        if (intent.Type == BehaviorIntentType.ApproachPlayer && !this.config.AllowApproachPlayer)
        {
            reason = "approach behavior is disabled";
            return false;
        }

        if (intent.Type == BehaviorIntentType.StepAway && !this.config.AllowApproachPlayer)
        {
            reason = "movement behavior is disabled";
            return false;
        }

        if ((intent.Type == BehaviorIntentType.Pause || intent.Type == BehaviorIntentType.LookAround) && !this.config.AllowFacePlayer)
        {
            reason = "small attention behavior is disabled";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether this intent manipulates NPC movement state (PathFindController / Halt) that only
    /// the host simulates. On farmhands these are no-ops — the host keeps driving the NPC — yet
    /// they used to be recorded and charged against the daily budget, leaving "they stepped
    /// closer" style memories for movements that never happened. Purely visual intents (facing,
    /// emotes, glances) render locally and stay allowed on farmhands.
    /// </summary>
    internal static bool RequiresHostSimulation(BehaviorIntentType intentType)
    {
        return intentType is BehaviorIntentType.ApproachPlayer
            or BehaviorIntentType.StepAway
            or BehaviorIntentType.Pause;
    }

    private string DescribeIntent(BehaviorIntentType intentType)
    {
        return intentType switch
        {
            BehaviorIntentType.FacePlayer => I18n.Get("behavior.facePlayer"),
            BehaviorIntentType.Emote => I18n.Get("behavior.emote"),
            BehaviorIntentType.ApproachPlayer => I18n.Get("behavior.approachPlayer"),
            BehaviorIntentType.Pause => I18n.Get("behavior.pause"),
            BehaviorIntentType.LookAround => I18n.Get("behavior.lookAround"),
            BehaviorIntentType.StepAway => I18n.Get("behavior.stepAway"),
            _ => intentType.ToString()
        };
    }

    private string TranslateSkipReason(string reason)
    {
        return reason switch
        {
            "an event is active" => I18n.Get("skip.eventActive"),
            "the NPC is not in the current location" => I18n.Get("skip.notInLocation"),
            "daily behavior budget reached" => I18n.Get("skip.dailyBudget"),
            "facing behavior is disabled" => I18n.Get("skip.facingDisabled"),
            "emote behavior is disabled" => I18n.Get("skip.emoteDisabled"),
            "approach behavior is disabled" => I18n.Get("skip.approachDisabled"),
            "movement behavior is disabled" => I18n.Get("skip.movementDisabled"),
            "small attention behavior is disabled" => I18n.Get("skip.attentionDisabled"),
            _ => reason
        };
    }
}
