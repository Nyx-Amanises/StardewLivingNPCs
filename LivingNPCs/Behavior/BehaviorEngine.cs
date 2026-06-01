using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;
using StardewValley.Quests;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorEngine
{
    private const string SaveDataKey = "behavior-memory";
    private const string HelpRequestQuestMarkerKey = "LivingNPCs/HelpRequestQuest";
    private const string HelpRequestQuestIdKey = "LivingNPCs/HelpRequestQuestId";

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly Random random = new();
    private readonly BehaviorMemory memory = new();
    private readonly ValleyTalkPromptBridge valleyTalkBridge;
    private readonly GiftSelector giftSelector;
    private readonly IBehaviorPlanner planner;
    private readonly AiBehaviorClient aiBehaviorClient;
    private readonly List<PendingBehaviorRequest> pendingRequests = new();
    private readonly List<PendingAmbientRemark> pendingAmbientRemarks = new();
    private readonly List<PendingWalkTogether> pendingWalks = new();
    private readonly List<PendingEscortToLocation> pendingEscorts = new();
    private readonly List<PendingDelayedTravelAction> pendingDelayedTravelActions = new();
    private readonly Dictionary<string, int> lastConversationMemoryTimeByNpc = new();
    private readonly Dictionary<string, double> nextSpeechBubbleTimeByNpc = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource cancellationTokenSource = new();

    public BehaviorEngine(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.valleyTalkBridge = new ValleyTalkPromptBridge(helper, monitor, config);
        this.giftSelector = new GiftSelector(this.random);
        this.planner = new AiBehaviorPlanner(new RuleBasedBehaviorPlanner(config, this.random, this.memory));
        this.aiBehaviorClient = new AiBehaviorClient(config, monitor);
    }

    public void RegisterEvents()
    {
        this.helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        this.helper.Events.GameLoop.Saving += this.OnSaving;
        this.helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        this.helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        this.helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        this.helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        this.RegisterConsoleCommands();
    }

    private void RegisterConsoleCommands()
    {
        this.helper.ConsoleCommands.Add(
            "livingnpcs_debug",
            "输出 NPC 当前状态、最近行为选择原因、求助生成适配和记忆召回摘要。用法：livingnpcs_debug [near|NPC名字]",
            this.OnDebugCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_prompt",
            "输出 LivingNPCs 即将注入 ValleyTalk 的完整隐藏上下文。用法：livingnpcs_prompt [near|NPC名字]",
            this.OnPromptCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_export",
            "导出 Markdown 调试报告。用法：livingnpcs_export [near|all|NPC名字]",
            this.OnExportCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_eval",
            "运行一组轻量运行时诊断，检查关键人格化规则是否还在。",
            this.OnEvalCommand
        );
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        var saveData = this.helper.Data.ReadSaveData<BehaviorMemorySaveData>(SaveDataKey);
        this.memory.Load(saveData, this.config.MaxMemoryEntriesPerNpc);
        this.lastConversationMemoryTimeByNpc.Clear();
        this.valleyTalkBridge.TryInitialize();
        this.SyncHelpRequestsToQuestLog();

        if (this.config.Debug)
        {
            this.monitor.Log("Loaded LivingNPCs behavior memory from the current save.", LogLevel.Debug);
        }
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.helper.Data.WriteSaveData(SaveDataKey, this.memory.ToSaveData());
        if (this.config.Debug)
        {
            this.monitor.Log("Saved LivingNPCs behavior memory to the current save.", LogLevel.Debug);
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (this.config.EnableNpcState)
        {
            this.memory.DecayStates(
                this.config.NpcStateDailyDecay,
                this.config.NpcEmotionDailyDecay,
                this.config.NpcConflictDailyDecay
            );
        }

        this.memory.ResetDaily();
        this.valleyTalkBridge.ClearAll();
        this.pendingRequests.Clear();
        this.pendingAmbientRemarks.Clear();
        this.pendingWalks.Clear();
        this.pendingEscorts.Clear();
        this.pendingDelayedTravelActions.Clear();
        this.lastConversationMemoryTimeByNpc.Clear();
        this.nextSpeechBubbleTimeByNpc.Clear();
        this.TryPropagateCommunityImpressions();
        this.SyncHelpRequestsToQuestLog();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.memory.ResetDaily();
        this.valleyTalkBridge.ClearAll();
        this.pendingRequests.Clear();
        this.pendingAmbientRemarks.Clear();
        this.pendingWalks.Clear();
        this.pendingEscorts.Clear();
        this.pendingDelayedTravelActions.Clear();
        this.lastConversationMemoryTimeByNpc.Clear();
        this.nextSpeechBubbleTimeByNpc.Clear();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu != null)
        {
            return;
        }

        if (this.config.InspectMemoryHotkey.JustPressed())
        {
            this.helper.Input.Suppress(e.Button);
            this.ShowNearestNpcMemory();
            return;
        }

        if (!this.config.BehaviorHotkey.JustPressed())
        {
            this.TryRecordConversationStart(e);
            return;
        }

        this.helper.Input.Suppress(e.Button);

        if (this.TryFindNearestNpc(out NPC? npc) && npc != null)
        {
            this.QueueOrExecute(npc, BehaviorTrigger.Manual, "hotkey");
        }
        else
        {
            this.ShowFeedback("LivingNPCs：附近没有可触发的 NPC。");
            if (this.config.Debug)
            {
                this.monitor.Log("No nearby NPC found for LivingNPCs behavior hotkey.", LogLevel.Debug);
            }
        }
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        if (!Context.IsWorldReady)
        {
            return;
        }

        this.TryUpdateCommitmentTimers();
        this.TryUpdateHelpRequestTimers();

        if (!this.config.EnablePassiveBehaviors || Game1.activeClickableMenu != null)
        {
            return;
        }

        if (this.random.Next(100) >= this.config.PassiveBehaviorChancePercent)
        {
            return;
        }

        if (!this.TryFindNearestNpc(out NPC? npc) || npc == null)
        {
            return;
        }

        this.QueueOrExecute(npc, BehaviorTrigger.Passive, "passive");
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
        {
            return;
        }

        foreach (var request in this.pendingRequests.Where(request => request.Task.IsCompleted).ToList())
        {
            this.pendingRequests.Remove(request);

            if (request.TotalDays != Game1.Date.TotalDays || !this.TryFindNpcInCurrentLocation(request.NpcName, out NPC? npc) || npc == null)
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

        this.TryShowPendingAmbientRemarks();
        this.TryStartPendingDelayedTravelActions();
        this.TryUpdatePendingEscorts();
        this.TryUpdatePendingWalks();
        this.TryShowCommitmentMorningReminders();
        this.TryShowHelpRequestFollowUps();
        this.TryShowSharedExperienceFollowUps();
        this.TryTriggerCommitmentArrivals();
        if (e.IsMultipleOf(120))
        {
            this.TryApplyDialogueBehaviorInfluences();
        }
    }

    private bool TryFindNearestNpc(out NPC? nearest)
    {
        nearest = null;
        if (Game1.currentLocation == null || Game1.player == null)
        {
            return false;
        }

        float maxDistance = this.config.MaxInteractionDistanceTiles;
        nearest = Game1.currentLocation.characters
            .Where(this.IsCandidateNpc)
            .Select(npc => new
            {
                Npc = npc,
                Distance = Vector2.Distance(npc.Tile, Game1.player.Tile)
            })
            .Where(pair => pair.Distance <= maxDistance)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Npc)
            .FirstOrDefault();

        return nearest != null;
    }

    private bool IsCandidateNpc(NPC npc)
    {
        return npc.currentLocation == Game1.currentLocation
            && !string.IsNullOrWhiteSpace(npc.Name)
            && this.memory.HasDailyBudget(npc, this.config.MaxBehaviorsPerNpcPerDay);
    }

    private void QueueOrExecute(NPC npc, BehaviorTrigger trigger, string source)
    {
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
            this.monitor.Log($"Queued AI behavior intent request for {npc.Name}.", LogLevel.Debug);
        }

        if (trigger == BehaviorTrigger.Manual)
        {
            this.ShowFeedback($"LivingNPCs：正在为 {npc.displayName} 规划行为...");
        }
    }

    private bool HasPendingRequest(string npcName)
    {
        return this.pendingRequests.Any(request => request.NpcName == npcName);
    }

    private bool TryFindNpcInCurrentLocation(string npcName, out NPC? npc)
    {
        npc = Game1.currentLocation?.characters.FirstOrDefault(candidate => candidate.Name == npcName);
        return npc != null;
    }

    public bool RecordValleyTalkExchange(string npcName, string npcDisplayName, string playerText, string npcResponse, string analysisJson)
    {
        if (!this.config.EnableConversationMemory || string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        if (!this.TryFindNpcInCurrentLocation(npcName, out NPC? npc) || npc == null)
        {
            return false;
        }

        var result = this.memory.RecordValleyTalkExchange(
            npc,
            playerText,
            npcResponse,
            analysisJson,
            this.config.MaxMemoryEntriesPerNpc,
            this.config.EnableCommitments ? this.config.MaxPendingCommitmentsPerNpc : 0,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests,
            this.config.EnableAiDialogueFriendship ? this.config.MaxAiDialogueFriendshipPerNpcPerDay : 0,
            this.config.MaxDialogueBehaviorInfluenceDays
        );

        if (!result.HasEffect)
        {
            return false;
        }

        if (result.AppliedFriendshipDelta > 0)
        {
            Game1.player.changeFriendship(result.AppliedFriendshipDelta, npc);
        }

        if (result.FulfilledHelpRequests.Count > 0)
        {
            this.RewardFulfilledHelpRequests(npc, result.FulfilledHelpRequests);
        }

        if (result.HelpRequestsStored > 0 || result.HelpRequestsUpdated > 0)
        {
            this.SyncHelpRequestsToQuestLog();
        }

        if (this.config.EnableDialogueFollowUps && !string.IsNullOrWhiteSpace(result.AmbientFollowUpText))
        {
            this.QueueAmbientRemark(npc, result.AmbientFollowUpText, result.AmbientFollowUpDelayMinutes);
        }

        if (this.config.EnableAiWorldActions)
        {
            this.TryExecuteConversationActions(npc, result.Actions, playerText, npcResponse);
        }

        this.PushInteractionContext(
            npc,
            $"Recorded ValleyTalk exchange for {npc.Name}: {result.LongTermMemoriesStored} long-term memories, {result.PlayerPreferencesStored} player preferences, {result.CommitmentsStored} commitments, {result.HelpRequestsStored} help requests, {result.HelpRequestsUpdated} help request updates, {result.ConflictsStored} conflicts, {result.BehaviorInfluencesStored} dialogue behavior influences, {result.ConflictsResolved} resolved conflicts, +{result.AppliedFriendshipDelta} extra friendship."
        );
        return true;
    }

    private void QueueAmbientRemark(NPC npc, string text, int delayMinutes)
    {
        this.pendingAmbientRemarks.RemoveAll(remark => remark.NpcName == npc.Name);
        this.pendingAmbientRemarks.Add(new PendingAmbientRemark(
            npc.Name,
            text.Trim(),
            Game1.Date.TotalDays,
            this.AddMinutesToTime(Game1.timeOfDay, System.Math.Clamp(delayMinutes, 0, 120)),
            npc.currentLocation?.Name ?? string.Empty,
            npc.Tile
        ));
    }

    private bool TryShowNpcSpeechBubble(NPC npc, string text, int cooldownMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d;
        if (this.nextSpeechBubbleTimeByNpc.TryGetValue(npc.Name, out double nextAllowed) && now < nextAllowed)
        {
            return false;
        }

        npc.showTextAboveHead(text);
        this.nextSpeechBubbleTimeByNpc[npc.Name] = now + Math.Max(1000, cooldownMilliseconds);
        return true;
    }

    private void TryShowPendingAmbientRemarks()
    {
        if (!this.config.EnableDialogueFollowUps
            || this.pendingAmbientRemarks.Count == 0
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var remark in this.pendingAmbientRemarks.ToList())
        {
            if (remark.TotalDays != Game1.Date.TotalDays)
            {
                this.pendingAmbientRemarks.Remove(remark);
                continue;
            }

            if (Game1.timeOfDay < remark.NotBeforeTimeOfDay)
            {
                continue;
            }

            if (!this.TryFindNpcInCurrentLocation(remark.NpcName, out NPC? npc)
                || npc == null
                || npc.currentLocation?.Name != remark.LocationName
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles
                || npc.Tile == remark.OriginTile)
            {
                continue;
            }

            npc.showTextAboveHead(remark.Text);
            this.pendingAmbientRemarks.Remove(remark);

            if (this.config.Debug)
            {
                this.monitor.Log($"Displayed ambient dialogue follow-up for {npc.Name}: {remark.Text}", LogLevel.Debug);
            }
        }
    }

    private int AddMinutesToTime(int timeOfDay, int minutes)
    {
        int hours = timeOfDay / 100;
        int mins = timeOfDay % 100;
        int totalMinutes = (hours * 60) + mins + minutes;
        return ((totalMinutes / 60) * 100) + (totalMinutes % 60);
    }

    private void TryExecuteConversationActions(
        NPC npc,
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        foreach (var action in this.BuildEffectiveConversationActions(npc, actions, playerText, npcResponse).Take(1))
        {
            if (action.Type is "walk_together" or "escort_to_location")
            {
                action.DelayMinutes = System.Math.Max(action.DelayMinutes, this.DetectPreparationDelayMinutes(npcResponse));
                if (action.DelayMinutes > 0)
                {
                    this.QueueDelayedTravelAction(npc, action);
                    continue;
                }
            }

            bool executed = action.Type switch
            {
                "give_small_gift" => this.TryGiveSmallGift(npc, action, playerText, npcResponse, out _),
                "give_meaningful_gift" => this.TryGiveMeaningfulGift(npc, action, playerText, npcResponse, out _),
                "give_money" => this.TryGiveMoney(npc, action, out _),
                "water_nearby_crops" => this.TryWaterNearbyCrops(npc, action, out _),
                "walk_together" => this.TryStartWalkTogether(npc, action, out _),
                "escort_to_location" => this.TryStartEscortToLocation(npc, action, out _),
                "festival_interaction" => this.TryFestivalInteraction(npc, action, out _),
                "assist_quest" => this.TryAssistQuest(npc, action, out _),
                _ => false
            };

            if (!executed && this.config.Debug)
            {
                string reason = action.Type switch
                {
                    "give_small_gift" => "gift request rejected",
                    "give_meaningful_gift" => "meaningful gift request rejected",
                    "give_money" => "money request rejected",
                    "water_nearby_crops" => "watering request rejected",
                    "walk_together" => "walk request rejected",
                    "escort_to_location" => "escort request rejected",
                    "festival_interaction" => "festival interaction request rejected",
                    "assist_quest" => "quest assist request rejected",
                    _ => "unknown request rejected"
                };
                this.monitor.Log($"Skipped AI world action {action.Type} for {npc.Name}: {reason}.", LogLevel.Debug);
            }
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
            this.TryCorrectTravelActionTargetFromVisibleDialogue(npc, actions, playerText, npcResponse);
            return actions;
        }

        if (!this.TryBuildFallbackTravelAction(npc, playerText, npcResponse, out ValleyTalkWorldActionRequest? action)
            && !this.TryBuildImmediateTravelActionFromRecentCommitment(npc, npcResponse, out action)
            || action == null)
        {
            return actions;
        }

        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Synthesized fallback AI travel action {action.Type} toward {action.TargetLocation} from visible dialogue.",
                LogLevel.Debug
            );
        }

        return new[] { action };
    }

    private void TryCorrectTravelActionTargetFromVisibleDialogue(
        NPC npc,
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        var action = actions.FirstOrDefault(candidate => candidate.Type is "walk_together" or "escort_to_location");
        if (action == null)
        {
            return;
        }

        string visibleTarget = this.TryDetectTravelTargetLocation(npc, $"{playerText} {npcResponse}");
        if (string.IsNullOrWhiteSpace(visibleTarget))
        {
            return;
        }

        string currentTarget = BehaviorMemory.NormalizeCommitmentLocation(action.TargetLocation, string.Empty);
        bool currentTargetIsGeneric = string.IsNullOrWhiteSpace(currentTarget)
            || currentTarget is "Town" or "BusStop";
        if (!currentTargetIsGeneric && string.Equals(currentTarget, visibleTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (currentTargetIsGeneric || visibleTarget == this.ResolveNpcHomeEscortTarget(npc))
        {
            action.Type = "escort_to_location";
            action.TargetLocation = visibleTarget;
            action.Reason = this.BuildWorldActionReason(
                action.Reason,
                $"visible dialogue clarified the destination as {visibleTarget}"
            );
        }
    }

    private bool TryBuildImmediateTravelActionFromRecentCommitment(
        NPC npc,
        string npcResponse,
        out ValleyTalkWorldActionRequest? action
    )
    {
        action = null;
        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return false;
        }

        var commitment = state.Commitments.FirstOrDefault(candidate =>
            candidate.Type == "go_together"
            && candidate.Status == "Pending"
            && candidate.CreatedTotalDays == Game1.Date.TotalDays
            && candidate.CreatedTimeOfDay == Game1.timeOfDay
            && candidate.DueTotalDays == Game1.Date.TotalDays
            && System.Math.Abs(this.ToDayMinutes(candidate.TimeOfDay) - this.ToDayMinutes(Game1.timeOfDay)) <= 20
        );
        if (commitment == null)
        {
            return false;
        }

        action = new ValleyTalkWorldActionRequest
        {
            Type = string.IsNullOrWhiteSpace(commitment.LocationName) ? "walk_together" : "escort_to_location",
            TargetLocation = commitment.LocationName,
            DurationMinutes = string.IsNullOrWhiteSpace(commitment.LocationName) ? 10 : 15,
            DelayMinutes = this.DetectPreparationDelayMinutes(npcResponse),
            Reason = $"a just-made same-time plan to go together: {commitment.Summary}"
        };
        return true;
    }

    private bool TryBuildFallbackTravelAction(
        NPC npc,
        string playerText,
        string npcResponse,
        out ValleyTalkWorldActionRequest? action
    )
    {
        action = null;
        string combinedText = $"{playerText} {npcResponse}";
        if (!this.LooksLikeImmediateTravelInvitation(playerText, npcResponse)
            || this.LooksLikeDeferredOrRejectedTravel(npcResponse))
        {
            return false;
        }

        string targetLocation = this.TryDetectTravelTargetLocation(npc, combinedText);
        action = new ValleyTalkWorldActionRequest
        {
            Type = string.IsNullOrWhiteSpace(targetLocation) ? "walk_together" : "escort_to_location",
            TargetLocation = targetLocation,
            DurationMinutes = string.IsNullOrWhiteSpace(targetLocation) ? 10 : 15,
            DelayMinutes = this.DetectPreparationDelayMinutes(npcResponse),
            Reason = "the visible conversation ended with an immediate shared travel plan"
        };
        return true;
    }

    private bool LooksLikeImmediateTravelInvitation(string playerText, string npcResponse)
    {
        bool farmerInvited = this.ContainsAny(
            playerText,
            "一起去",
            "要不要去",
            "陪我去",
            "去我农场",
            "来我农场",
            "去农场看看",
            "一起走",
            "go with me",
            "come to my farm",
            "visit my farm",
            "walk with me"
        );
        bool npcAccepted = this.ContainsAny(
            npcResponse,
            "一起去",
            "我陪你",
            "那我们",
            "走吧",
            "可以",
            "当然",
            "好啊",
            "好呀",
            "愿意",
            "let's go",
            "i'll go",
            "i can go",
            "sure"
        );
        return farmerInvited && npcAccepted;
    }

    private bool LooksLikeDeferredOrRejectedTravel(string npcResponse)
    {
        if (this.DetectPreparationDelayMinutes(npcResponse) > 0)
        {
            return false;
        }

        return this.ContainsAny(
            npcResponse,
            "下次",
            "改天",
            "晚点",
            "以后",
            "今天不行",
            "不行",
            "不可以",
            "不能",
            "没法",
            "抱歉",
            "later",
            "another time",
            "not now",
            "can't"
        );
    }

    private int DetectPreparationDelayMinutes(string npcResponse)
    {
        return this.ContainsAny(
            npcResponse,
            "等我一下",
            "稍等",
            "一会儿",
            "准备一下",
            "换件衣服",
            "拿件衣服",
            "拿衣服",
            "雨衣",
            "带把伞",
            "拿把伞",
            "wait a moment",
            "get my coat",
            "grab my coat",
            "umbrella"
        )
            ? 10
            : 0;
    }

    private string TryDetectTravelTargetLocation(NPC npc, string text)
    {
        string npcHome = this.ResolveNpcHomeEscortTarget(npc);
        if (!string.IsNullOrWhiteSpace(npcHome)
            && this.ContainsAny(
                text,
                "你家",
                "你的家",
                "你家里",
                "你住的地方",
                "你住处",
                "你房间",
                "她家",
                "他家",
                "家里看看",
                "回你家",
                "去家里",
                "your home",
                "your house",
                "where you live"
            ))
        {
            return npcHome;
        }

        if (this.ContainsAny(text, "潘妮家", "潘妮的家", "潘妮家里", "帕姆家", "帕姆的家", "拖车", "penny's home", "penny's house", "trailer"))
        {
            return "Trailer";
        }

        if (this.ContainsAny(text, "农场", "farm"))
        {
            return "Farm";
        }

        if (this.ContainsAny(text, "海边", "海滩", "beach"))
        {
            return "Beach";
        }

        if (this.ContainsAny(text, "博物馆", "图书馆", "museum", "library"))
        {
            return "ArchaeologyHouse";
        }

        if (this.ContainsAny(text, "森林", "煤矿森林", "forest"))
        {
            return "Forest";
        }

        if (this.ContainsAny(text, "矿井", "矿洞", "矿山", "mine", "mines"))
        {
            return "Mine";
        }

        if (this.ContainsAny(text, "山上", "山地", "mountain"))
        {
            return "Mountain";
        }

        if (this.ContainsAny(text, "酒吧", "沙龙", "saloon"))
        {
            return "Saloon";
        }

        if (this.ContainsAny(text, "医院", "诊所", "clinic", "hospital"))
        {
            return "Hospital";
        }

        if (this.ContainsAny(text, "皮埃尔", "杂货店", "general store", "pierre"))
        {
            return "SeedShop";
        }

        if (this.ContainsAny(text, "巴士站", "bus stop"))
        {
            return "BusStop";
        }

        if (this.ContainsAny(text, "镇上", "鹈鹕镇", "town"))
        {
            return "Town";
        }

        return string.Empty;
    }

    private string ResolveNpcHomeEscortTarget(NPC npc)
    {
        return npc.Name switch
        {
            "Penny" or "Pam" => "Trailer",
            "Alex" or "Evelyn" or "George" => "JoshHouse",
            "Haley" or "Emily" => "HaleyHouse",
            "Sam" or "Jodi" or "Vincent" or "Kent" => "SamHouse",
            "Abigail" or "Pierre" or "Caroline" => "SeedShop",
            "Sebastian" or "Maru" or "Demetrius" or "Robin" => "ScienceHouse",
            "Leah" => "LeahHouse",
            "Marnie" or "Jas" or "Shane" => "AnimalShop",
            "Elliott" => "ElliottHouse",
            "Gus" => "Saloon",
            "Clint" => "Blacksmith",
            "Willy" => "FishShop",
            "Wizard" => "WizardHouse",
            "Linus" => "Tent",
            _ => string.Empty
        };
    }

    private bool ContainsAny(string text, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private void QueueDelayedTravelAction(NPC npc, ValleyTalkWorldActionRequest action)
    {
        int delayMinutes = System.Math.Clamp(action.DelayMinutes, 1, 20);
        this.pendingDelayedTravelActions.RemoveAll(pending => pending.NpcName == npc.Name);
        this.pendingDelayedTravelActions.Add(new PendingDelayedTravelAction(
            npc.Name,
            Game1.Date.TotalDays,
            npc.currentLocation?.Name ?? string.Empty,
            this.AddMinutesToTime(Game1.timeOfDay, delayMinutes),
            action.Type,
            action.TargetLocation,
            action.DurationMinutes,
            action.Reason
        ));

        var state = this.memory.GetState(npc);
        if (state != null)
        {
            this.MarkStateAfterWorldAction(state, "they asked the farmer to wait briefly before leaving together");
        }

        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Queued delayed travel action {action.Type} for {npc.Name} in {delayMinutes} minutes toward {action.TargetLocation}.",
                LogLevel.Debug
            );
        }
    }

    private void TryStartPendingDelayedTravelActions()
    {
        if (this.pendingDelayedTravelActions.Count == 0
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var pending in this.pendingDelayedTravelActions.ToList())
        {
            if (pending.TotalDays != Game1.Date.TotalDays)
            {
                this.pendingDelayedTravelActions.Remove(pending);
                continue;
            }

            if (Game1.timeOfDay < pending.NotBeforeTimeOfDay)
            {
                continue;
            }

            if (!this.TryFindNpcInCurrentLocation(pending.NpcName, out NPC? npc)
                || npc == null
                || npc.currentLocation?.Name != pending.LocationName
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 2)
            {
                this.pendingDelayedTravelActions.Remove(pending);
                continue;
            }

            var action = new ValleyTalkWorldActionRequest
            {
                Type = pending.Type,
                TargetLocation = pending.TargetLocation,
                DurationMinutes = pending.DurationMinutes,
                Reason = pending.Reason
            };
            bool started = pending.Type switch
            {
                "escort_to_location" => this.TryStartEscortToLocation(npc, action, out _),
                _ => this.TryStartWalkTogether(npc, action, out _)
            };
            this.pendingDelayedTravelActions.Remove(pending);

            if (started)
            {
                npc.showTextAboveHead("好了，我们走吧。");
            }
        }
    }

    private bool TryGiveSmallGift(
        NPC npc,
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse,
        out string reason
    )
    {
        reason = string.Empty;
        if (!this.CanUseWorldAction(npc, "small_gift", requireFriendly: false, out reason))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiSmallGifts
            || state == null
            || state.LastAiSmallGiftTotalDays == Game1.Date.TotalDays
            || state.LastAiMeaningfulGiftTotalDays == Game1.Date.TotalDays)
        {
            reason = "small gifts are disabled or another AI gift was already used today";
            return false;
        }

        if (!this.TrySelectGiftForConversationAction(
                action,
                GiftTier.Small,
                npc,
                state,
                playerText,
                npcResponse,
                out GiftSelection selection,
                out reason))
        {
            return false;
        }

        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            reason = "player inventory is full";
            return false;
        }

        state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveSmallGift",
            this.BuildWorldActionReason(
                action.Reason,
                this.BuildGiftSelectionReason(
                    $"they gave the farmer {gift.DisplayName} after an AI conversation",
                    selection
                )
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, "they gave the farmer a small gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Selected AI gift for {npc.Name}: {selection.DebugName} ({selection.ItemId}); {selection.Reason}.",
                LogLevel.Debug
            );
        }

        this.ShowFeedback($"LivingNPCs：{npc.displayName} 给了你 {gift.DisplayName}。");
        return true;
    }

    private void RewardFulfilledHelpRequests(NPC npc, IReadOnlyList<NpcHelpRequestFact> requests)
    {
        foreach (var request in requests.Where(request => request.Status == "Fulfilled"))
        {
            this.RewardFulfilledHelpRequest(npc, request);
        }
    }

    private void RewardFulfilledHelpRequest(NPC npc, NpcHelpRequestFact request)
    {
        if (!request.RewardGranted)
        {
            int minReward = System.Math.Min(this.config.MinHelpRequestFriendshipReward, this.config.MaxHelpRequestFriendshipReward);
            int maxReward = System.Math.Max(this.config.MinHelpRequestFriendshipReward, this.config.MaxHelpRequestFriendshipReward);
            int friendshipReward = System.Math.Clamp(request.RewardFriendship, minReward, maxReward);
            Game1.player.changeFriendship(friendshipReward, npc);
            request.RewardFriendship = friendshipReward;
            request.RewardGranted = true;

            this.memory.RecordNpcWorldAction(
                npc,
                "CompletedHelpRequest",
                $"the farmer completed a personal help request and earned {friendshipReward} friendship: {request.Summary}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.QueueAmbientRemark(npc, "谢谢你，真的帮上忙了。", 0);
            this.ShowFeedback($"LivingNPCs：完成 {npc.displayName} 的求助，额外好感 +{friendshipReward}。");
            this.SpreadCommunityRipple(
                npc,
                "helped",
                $"the farmer helped {npc.displayName} with a personal request",
                importance: 78
            );
        }

        if (!request.RewardGiftGiven)
        {
            this.TryGiveHelpRequestRewardGift(npc, request);
        }
    }

    private bool TryGiveHelpRequestRewardGift(NPC npc, NpcHelpRequestFact request)
    {
        if (!this.config.AllowAiSmallGifts)
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return false;
        }

        GiftSelection selection = this.giftSelector.Choose(npc, state, request.Summary, request.Resolution);
        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            return false;
        }

        request.RewardGiftGiven = true;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveHelpRequestRewardGift",
            this.BuildGiftSelectionReason(
                $"they gave the farmer {gift.DisplayName} after a fulfilled personal help request",
                selection
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, "they thanked the farmer with a small gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Selected help request reward gift for {npc.Name}: {selection.DebugName} ({selection.ItemId}); {selection.Reason}.",
                LogLevel.Debug
            );
        }

        this.ShowFeedback($"LivingNPCs：{npc.displayName} 又送了你 {gift.DisplayName} 作为谢礼。");
        return true;
    }

    private bool TryGiveMoney(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.CanUseWorldAction(npc, "money", requireFriendly: true, out reason))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiMoneyGifts || state == null || state.LastAiMoneyGiftTotalDays == Game1.Date.TotalDays)
        {
            reason = "money gifts are disabled or already used today";
            return false;
        }

        int amount = System.Math.Clamp(action.Amount <= 0 ? 100 : action.Amount, 25, this.config.MaxAiMoneyGiftAmount);
        Game1.player.Money += amount;
        state.LastAiMoneyGiftTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveMoney",
            this.BuildWorldActionReason(action.Reason, $"they gave the farmer {amount}g after an AI conversation"),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, "they gave the farmer some money");
        this.ShowFeedback($"LivingNPCs：{npc.displayName} 给了你 {amount}g。");
        return true;
    }

    private bool TryGiveMeaningfulGift(
        NPC npc,
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse,
        out string reason
    )
    {
        reason = string.Empty;
        if (!this.CanUseWorldAction(npc, "meaningful_gift", requireFriendly: true, out reason))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiMeaningfulGifts || state == null)
        {
            reason = "meaningful gifts are disabled";
            return false;
        }

        if (state.LastAiSmallGiftTotalDays == Game1.Date.TotalDays)
        {
            reason = "another AI gift was already used today";
            return false;
        }

        int daysSinceLastMeaningfulGift = state.LastAiMeaningfulGiftTotalDays < 0
            ? int.MaxValue
            : Game1.Date.TotalDays - state.LastAiMeaningfulGiftTotalDays;
        if (daysSinceLastMeaningfulGift < this.config.AiMeaningfulGiftCooldownDays)
        {
            reason = "meaningful gift cooldown is active";
            return false;
        }

        bool highRelationship = state.InteractionComfortTier is "Trusted" or "Intimate";
        bool recentSpecialEvent = !string.IsNullOrWhiteSpace(state.LastEventContext)
            && state.LastEventTotalDays >= Game1.Date.TotalDays - 1;
        bool meaningfulMemoryCue = this.giftSelector.HasMeaningfulMemoryCue(state, playerText, npcResponse);
        if (!highRelationship && !recentSpecialEvent && !meaningfulMemoryCue)
        {
            reason = "meaningful gifts require a strong relationship, recent event, or important memory cue";
            return false;
        }

        if (!this.TrySelectGiftForConversationAction(
                action,
                GiftTier.Meaningful,
                npc,
                state,
                playerText,
                npcResponse,
                out GiftSelection selection,
                out reason))
        {
            return false;
        }

        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            reason = "player inventory is full";
            return false;
        }

        state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveMeaningfulGift",
            this.BuildWorldActionReason(
                action.Reason,
                this.BuildGiftSelectionReason(
                    $"they gave the farmer a meaningful {gift.DisplayName}",
                    selection
                )
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, "they gave the farmer a meaningful gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Selected meaningful AI gift for {npc.Name}: {selection.DebugName} ({selection.ItemId}); {selection.Reason}.",
                LogLevel.Debug
            );
        }

        this.ShowFeedback($"LivingNPCs：{npc.displayName} 给了你 {gift.DisplayName}。");
        return true;
    }

    private bool TrySelectGiftForConversationAction(
        ValleyTalkWorldActionRequest action,
        GiftTier tier,
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse,
        out GiftSelection selection,
        out string reason
    )
    {
        selection = null!;
        reason = string.Empty;
        if (!string.IsNullOrWhiteSpace(action.ItemId))
        {
            if (this.giftSelector.TryChooseRequested(action.ItemId, tier, out GiftSelection? requestedSelection)
                && requestedSelection != null)
            {
                selection = requestedSelection;
                return true;
            }

            reason = $"requested gift item {action.ItemId} is not in the allowed {tier.ToString().ToLowerInvariant()} gift pool";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(action.ItemLabel))
        {
            reason = $"gift action named {action.ItemLabel}, but did not provide a valid itemId";
            return false;
        }

        if (VisibleDialoguePromisesUnsupportedGift(npcResponse))
        {
            reason = "visible dialogue promised a specific unsupported gift without a valid itemId";
            return false;
        }

        selection = tier == GiftTier.Meaningful
            ? this.giftSelector.ChooseMeaningful(npc, state, playerText, npcResponse)
            : this.giftSelector.Choose(npc, state, playerText, npcResponse);
        return true;
    }

    private bool VisibleDialoguePromisesUnsupportedGift(string npcResponse)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return false;
        }

        string text = npcResponse.ToLowerInvariant();
        return ContainsAny(
            text,
            "书签",
            "bookmark",
            "便签",
            "纸条",
            "手帕",
            "发夹",
            "小卡片"
        );
    }

    private bool TryWaterNearbyCrops(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.CanUseWorldAction(npc, "farm_help", requireFriendly: true, out reason))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiFarmHelp || state == null || state.LastAiFarmHelpTotalDays == Game1.Date.TotalDays)
        {
            reason = "farm help is disabled or already used today";
            return false;
        }

        if (Game1.currentLocation is not Farm farm)
        {
            reason = "the player is not on the farm";
            return false;
        }

        int requestedTiles = action.TileCount <= 0 ? 6 : action.TileCount;
        int maxTiles = System.Math.Clamp(requestedTiles, 1, this.config.MaxAiWateredTilesPerAction);
        var nearbyTiles = farm.terrainFeatures.Pairs
            .Where(pair => pair.Value is HoeDirt dirt && dirt.crop != null && dirt.state.Value != 1)
            .OrderBy(pair => Vector2.Distance(pair.Key, Game1.player.Tile))
            .Take(maxTiles)
            .ToList();

        foreach (var pair in nearbyTiles)
        {
            if (pair.Value is HoeDirt dirt)
            {
                dirt.state.Value = 1;
                dirt.updateNeighbors();
            }
        }

        if (nearbyTiles.Count == 0)
        {
            reason = "there are no nearby unwatered crops";
            return false;
        }

        state.LastAiFarmHelpTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "WateredNearbyCrops",
            this.BuildWorldActionReason(action.Reason, $"they watered {nearbyTiles.Count} nearby crop tiles for the farmer"),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, "they helped the farmer with watering");
        this.ShowFeedback($"LivingNPCs：{npc.displayName} 帮你浇了 {nearbyTiles.Count} 格作物。");
        return true;
    }

    private bool TryStartWalkTogether(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.CanUseWorldAction(npc, "walk_together", requireFriendly: false, out reason, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiWalkTogether || state == null || state.LastAiWalkTogetherTotalDays == Game1.Date.TotalDays)
        {
            reason = "walk together is disabled or already used today";
            return false;
        }

        if (npc.controller != null)
        {
            reason = "the NPC is already moving";
            return false;
        }

        int durationMinutes = System.Math.Clamp(
            action.DurationMinutes <= 0 ? 10 : action.DurationMinutes,
            5,
            this.config.MaxAiWalkTogetherMinutes
        );
        this.pendingWalks.RemoveAll(walk => walk.NpcName == npc.Name);
        this.pendingWalks.Add(new PendingWalkTogether(
            npc.Name,
            Game1.Date.TotalDays,
            npc.currentLocation?.Name ?? string.Empty,
            this.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
            Game1.player.TilePoint,
            null
        ));
        state.LastAiWalkTogetherTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "WalkedTogether",
            this.BuildWorldActionReason(action.Reason, $"they agreed to walk with the farmer for about {durationMinutes} minutes"),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, "they agreed to walk with the farmer");
        this.ShowFeedback($"LivingNPCs：{npc.displayName} 会陪你走一会儿。");
        return true;
    }

    private bool TryStartEscortToLocation(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiEscortToLocation)
        {
            reason = "escort to location is disabled";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.TargetLocation))
        {
            reason = "escort target location is missing";
            return false;
        }

        string targetLocation = BehaviorMemory.NormalizeCommitmentLocation(action.TargetLocation, string.Empty);

        if (!this.CanUseWorldAction(npc, "escort_to_location", requireFriendly: false, out reason, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            reason = "there is no NPC state yet";
            return false;
        }

        if (state.LastAiWalkTogetherTotalDays == Game1.Date.TotalDays)
        {
            reason = "walk or escort action was already used today";
            return false;
        }

        if (!this.IsKnownEscortTarget(targetLocation))
        {
            reason = $"escort target {targetLocation} is not supported yet";
            return false;
        }

        int durationMinutes = System.Math.Clamp(
            action.DurationMinutes <= 0 ? this.config.MaxAiWalkTogetherMinutes : action.DurationMinutes,
            10,
            System.Math.Max(10, this.config.MaxAiWalkTogetherMinutes)
        );
        string targetLabel = this.GetEscortTargetLabel(targetLocation);

        npc.controller = null;
        npc.Halt();
        this.pendingWalks.RemoveAll(walk => walk.NpcName == npc.Name);
        this.pendingEscorts.RemoveAll(escort => escort.NpcName == npc.Name);
        this.pendingEscorts.Add(new PendingEscortToLocation(
            npc.Name,
            Game1.Date.TotalDays,
            targetLocation,
            targetLabel,
            this.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
            Game1.currentLocation?.Name ?? string.Empty,
            Game1.player.TilePoint
        ));

        state.LastAiWalkTogetherTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "StartedEscortToLocation",
            this.BuildWorldActionReason(action.Reason, $"they agreed to guide the farmer toward {targetLabel}"),
            this.config.MaxMemoryEntriesPerNpc
        );
        this.MarkStateAfterWorldAction(state, $"they agreed to guide the farmer toward {targetLabel}");

        if (this.IsEscortTargetReached(Game1.currentLocation, targetLocation))
        {
            this.CompleteEscortToLocation(npc, this.pendingEscorts.First(escort => escort.NpcName == npc.Name));
            return true;
        }

        npc.showTextAboveHead(this.BuildEscortStartGreeting(targetLocation));
        this.ShowFeedback($"LivingNPCs：{npc.displayName} 会带你去{targetLabel}。");
        return true;
    }

    private void TryUpdatePendingEscorts()
    {
        if (this.pendingEscorts.Count == 0)
        {
            return;
        }

        if (Game1.eventUp)
        {
            foreach (var escort in this.pendingEscorts.ToList())
            {
                this.StopEscortToLocation(escort, Game1.getCharacterFromName(escort.NpcName), returnToSchedule: true);
            }

            return;
        }

        foreach (var escort in this.pendingEscorts.ToList())
        {
            NPC? npc = Game1.getCharacterFromName(escort.NpcName);
            if (escort.TotalDays != Game1.Date.TotalDays
                || Game1.timeOfDay >= escort.EndTimeOfDay
                || npc == null
                || Game1.currentLocation == null
                || Game1.player == null)
            {
                this.StopEscortToLocation(escort, npc, returnToSchedule: true);
                continue;
            }

            if (Game1.activeClickableMenu != null)
            {
                continue;
            }

            if (escort.WaitingInNextLocation)
            {
                if (npc.currentLocation != Game1.currentLocation)
                {
                    string npcLocation = BehaviorMemory.NormalizeCommitmentLocation(npc.currentLocation?.Name ?? string.Empty, string.Empty);
                    if (string.Equals(npcLocation, escort.WaitingLocationName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    escort.WaitingInNextLocation = false;
                    escort.WaitingLocationName = string.Empty;
                    escort.WaitingSourceLocationName = string.Empty;
                }
                else
                {
                    escort.WaitingInNextLocation = false;
                    escort.WaitingLocationName = string.Empty;
                    escort.WaitingSourceLocationName = string.Empty;
                    escort.LastLocationName = Game1.currentLocation.Name;
                    escort.HintShownForLocation = false;
                    escort.LastWaypointTile = Point.Zero;
                    escort.LastAssignedController = null;
                    if (!this.IsEscortTargetReached(Game1.currentLocation, escort.TargetLocation))
                    {
                        npc.showTextAboveHead(this.BuildEscortCaughtUpGreeting(escort));
                    }
                }
            }

            if (npc.currentLocation != Game1.currentLocation)
            {
                if (!this.TryMoveNpcNearPlayerForEscort(npc, Game1.currentLocation, out _))
                {
                    this.StopEscortToLocation(escort, npc, returnToSchedule: true);
                    continue;
                }

                if (!string.Equals(escort.LastLocationName, Game1.currentLocation.Name, StringComparison.OrdinalIgnoreCase))
                {
                    npc.showTextAboveHead(this.BuildEscortCaughtUpGreeting(escort));
                    escort.LastLocationName = Game1.currentLocation.Name;
                    escort.HintShownForLocation = false;
                    escort.LastWaypointTile = Point.Zero;
                    escort.LastAssignedController = null;
                }
            }
            else if (!Game1.currentLocation.characters.Contains(npc))
            {
                Game1.currentLocation.characters.Add(npc);
                npc.currentLocation = Game1.currentLocation;
            }

            if (!string.Equals(escort.LastLocationName, Game1.currentLocation.Name, StringComparison.OrdinalIgnoreCase))
            {
                escort.LastLocationName = Game1.currentLocation.Name;
                escort.HintShownForLocation = false;
                escort.LastWaypointTile = Point.Zero;
                escort.LastAssignedController = null;
            }

            if (this.IsEscortTargetReached(Game1.currentLocation, escort.TargetLocation))
            {
                this.CompleteEscortToLocation(npc, escort);
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 8)
            {
                if (!this.TryMoveNpcNearPlayerForEscort(npc, Game1.currentLocation, out _))
                {
                    this.StopEscortToLocation(escort, npc, returnToSchedule: true);
                    continue;
                }
            }

            if (npc.controller != null && escort.LastAssignedController != null && npc.controller != escort.LastAssignedController)
            {
                npc.controller = null;
                npc.Halt();
            }

            if (this.TryFindEscortWaypointTile(npc, escort, out Point waypointTile, out string nextLocation))
            {
                float distanceToWaypoint = Vector2.Distance(npc.Tile, new Vector2(waypointTile.X, waypointTile.Y));
                if (distanceToWaypoint > 1.5f)
                {
                    if (escort.LastWaypointTile != waypointTile || npc.controller == null)
                    {
                        npc.controller = new PathFindController(
                            npc,
                            Game1.currentLocation,
                            waypointTile,
                            this.GetDirectionTowardPlayerFromTile(waypointTile)
                        );
                        escort.LastWaypointTile = waypointTile;
                        escort.LastAssignedController = npc.controller;
                    }

                    if (!escort.HintShownForLocation)
                    {
                        npc.showTextAboveHead(this.BuildEscortDirectionHint(nextLocation));
                        escort.HintShownForLocation = true;
                    }

                    continue;
                }

                npc.controller = null;
                npc.Halt();
                this.TryFacePlayer(npc);
                if (this.IsPlayerCloseEnoughToFollowEscort(npc, waypointTile)
                    && this.TryAdvanceEscortNpcToNextLocation(npc, escort, nextLocation))
                {
                    continue;
                }

                if (!escort.HintShownForLocation)
                {
                    npc.showTextAboveHead(this.BuildEscortExitWaitHint(nextLocation));
                    escort.HintShownForLocation = true;
                }

                continue;
            }

            escort.HintShownForLocation = true;
            this.TryKeepEscortNearPlayer(npc, escort);
        }
    }

    private bool TryKeepEscortNearPlayer(NPC npc, PendingEscortToLocation escort)
    {
        if (Vector2.Distance(npc.Tile, Game1.player.Tile) <= 1.75f)
        {
            this.TryFacePlayer(npc);
            return true;
        }

        if (escort.LastPlayerTile == Game1.player.TilePoint && npc.controller != null)
        {
            return true;
        }

        if (!this.TryFindApproachTile(npc, out Point targetTile))
        {
            return false;
        }

        npc.controller = new PathFindController(
            npc,
            Game1.currentLocation,
            targetTile,
            this.GetDirectionTowardPlayerFromTile(targetTile)
        );
        escort.LastPlayerTile = Game1.player.TilePoint;
        escort.LastAssignedController = npc.controller;
        return true;
    }

    private bool IsPlayerCloseEnoughToFollowEscort(NPC npc, Point waypointTile)
    {
        var waypoint = new Vector2(waypointTile.X, waypointTile.Y);
        return Vector2.Distance(Game1.player.Tile, npc.Tile) <= 3f
            || Vector2.Distance(Game1.player.Tile, waypoint) <= 3.5f;
    }

    private bool TryAdvanceEscortNpcToNextLocation(NPC npc, PendingEscortToLocation escort, string nextLocation)
    {
        GameLocation? source = npc.currentLocation;
        GameLocation? destination = this.ResolveEscortLocation(nextLocation);
        if (source == null || destination == null)
        {
            return false;
        }

        string sourceName = BehaviorMemory.NormalizeCommitmentLocation(source.Name, source.Name);
        string destinationName = BehaviorMemory.NormalizeCommitmentLocation(destination.Name, destination.Name);
        if (string.IsNullOrWhiteSpace(destinationName))
        {
            return false;
        }

        if (!this.TryReadWarpDestinationTile(source, nextLocation, out Point targetTile)
            && !this.TryFindReverseWarpEntryTile(destination, sourceName, npc, out targetTile))
        {
            targetTile = this.GetLocationCenterTile(destination);
        }

        if (!this.IsSafeDestinationTile(destination, targetTile, npc)
            && !this.TryFindOpenTileNear(destination, targetTile, npc, out targetTile)
            && !this.TryFindOpenTileNear(destination, this.GetLocationCenterTile(destination), npc, out targetTile))
        {
            return false;
        }

        try
        {
            npc.controller = null;
            npc.Halt();
            source.characters.Remove(npc);
            if (!destination.characters.Contains(npc))
            {
                destination.characters.Add(npc);
            }

            npc.currentLocation = destination;
            npc.Position = new Vector2(targetTile.X * Game1.tileSize, targetTile.Y * Game1.tileSize);
            npc.faceDirection(2);

            escort.WaitingInNextLocation = true;
            escort.WaitingLocationName = destinationName;
            escort.WaitingSourceLocationName = sourceName;
            escort.LastLocationName = destination.Name;
            escort.LastWaypointTile = Point.Zero;
            escort.LastAssignedController = null;
            escort.HintShownForLocation = false;
            return true;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Could not advance {npc.Name} during LivingNPCs escort: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private bool TryFindEscortWaypointTile(NPC npc, PendingEscortToLocation escort, out Point waypointTile, out string nextLocation)
    {
        waypointTile = Point.Zero;
        nextLocation = string.Empty;
        if (Game1.currentLocation == null
            || !this.TryGetNextEscortLocation(Game1.currentLocation, escort.TargetLocation, out nextLocation))
        {
            return false;
        }

        if (this.TryFindWarpTileToward(Game1.currentLocation, nextLocation, npc, out waypointTile))
        {
            return true;
        }

        return false;
    }

    private GameLocation? ResolveEscortLocation(string locationName)
    {
        try
        {
            return BehaviorMemory.NormalizeCommitmentLocation(locationName, locationName) == "Farm"
                ? Game1.getFarm()
                : Game1.getLocationFromName(locationName);
        }
        catch
        {
            return null;
        }
    }

    private bool TryReadWarpDestinationTile(GameLocation sourceLocation, string targetLocation, out Point targetTile)
    {
        foreach (var warp in sourceLocation.warps.Where(warp => string.Equals(
            BehaviorMemory.NormalizeCommitmentLocation(GetWarpTargetName(warp), string.Empty),
            targetLocation,
            StringComparison.OrdinalIgnoreCase)))
        {
            if (TryReadWarpTargetTile(warp, out targetTile))
            {
                return true;
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private static bool TryReadWarpTargetTile(object warp, out Point targetTile)
    {
        bool hasX = TryReadNumericMember(warp, "TargetX", out int x)
            || TryReadNumericMember(warp, "targetX", out x);
        bool hasY = TryReadNumericMember(warp, "TargetY", out int y)
            || TryReadNumericMember(warp, "targetY", out y);

        if (hasX && hasY)
        {
            targetTile = new Point(x, y);
            return true;
        }

        targetTile = Point.Zero;
        return false;
    }

    private bool TryFindReverseWarpEntryTile(GameLocation destination, string sourceLocationName, NPC npc, out Point targetTile)
    {
        foreach (var warp in destination.warps.Where(warp => string.Equals(
            BehaviorMemory.NormalizeCommitmentLocation(GetWarpTargetName(warp), string.Empty),
            sourceLocationName,
            StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var candidate in this.GetTilesAround(new Point(warp.X, warp.Y), 4))
            {
                if (this.IsSafeDestinationTile(destination, candidate, npc))
                {
                    targetTile = candidate;
                    return true;
                }
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private Point GetLocationCenterTile(GameLocation location)
    {
        try
        {
            var layer = location.map.Layers[0];
            return new Point(layer.LayerWidth / 2, layer.LayerHeight / 2);
        }
        catch
        {
            return new Point(10, 10);
        }
    }

    private bool TryGetNextEscortLocation(GameLocation currentLocation, string targetLocation, out string nextLocation)
    {
        nextLocation = string.Empty;
        string current = BehaviorMemory.NormalizeCommitmentLocation(currentLocation.Name, currentLocation.Name);
        if (string.Equals(current, targetLocation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (this.HasWarpToLocation(currentLocation, targetLocation))
        {
            nextLocation = targetLocation;
            return true;
        }

        string targetHub = this.GetEscortHubLocation(targetLocation);
        if (!string.Equals(current, targetHub, StringComparison.OrdinalIgnoreCase)
            && this.HasWarpToLocation(currentLocation, targetHub))
        {
            nextLocation = targetHub;
            return true;
        }

        nextLocation = current switch
        {
            "Farm" => targetLocation == "Farm" ? string.Empty : "BusStop",
            "BusStop" => targetLocation == "Farm" ? "Farm" : "Town",
            "Town" => this.GetTownEscortExit(targetLocation),
            "Mountain" => targetLocation == "Mine" ? "Mine" : "Town",
            "Mine" => "Mountain",
            "Beach" => "Town",
            "Forest" => targetLocation == "Farm" && this.HasWarpToLocation(currentLocation, "Farm") ? "Farm" : "Town",
            "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
                or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
                or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent" => "Town",
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(nextLocation);
    }

    private string GetTownEscortExit(string targetLocation)
    {
        return targetLocation switch
        {
            "Farm" => "BusStop",
            "BusStop" => "BusStop",
            "Mountain" or "Mine" => "Mountain",
            "Beach" => "Beach",
            "Forest" => "Forest",
            "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
                or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
                or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent" => targetLocation,
            _ => string.Empty
        };
    }

    private string GetEscortHubLocation(string targetLocation)
    {
        return targetLocation switch
        {
            "Mine" => "Mountain",
            "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
                or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
                or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent" => "Town",
            _ => targetLocation
        };
    }

    private bool HasWarpToLocation(GameLocation location, string targetLocation)
    {
        return location.warps.Any(warp =>
            string.Equals(
                BehaviorMemory.NormalizeCommitmentLocation(GetWarpTargetName(warp), string.Empty),
                targetLocation,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private bool TryFindWarpTileToward(GameLocation location, string targetLocation, NPC npc, out Point targetTile)
    {
        foreach (var warp in location.warps
            .Where(warp => string.Equals(
                BehaviorMemory.NormalizeCommitmentLocation(GetWarpTargetName(warp), string.Empty),
                targetLocation,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(warp => Vector2.Distance(new Vector2(warp.X, warp.Y), npc.Tile)))
        {
            foreach (var candidate in this.GetTilesAround(new Point(warp.X, warp.Y), 3)
                .OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), npc.Tile)))
            {
                if (this.IsSafeDestinationTile(location, candidate, npc))
                {
                    targetTile = candidate;
                    return true;
                }
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private bool TryMoveNpcNearPlayerForEscort(NPC npc, GameLocation location, out Point targetTile)
    {
        targetTile = Point.Zero;
        foreach (var candidate in this.GetTilesAround(Game1.player.TilePoint, 4)
            .Where(tile => tile != Game1.player.TilePoint)
            .OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), Game1.player.Tile)))
        {
            if (!this.IsSafeDestinationTile(location, candidate, npc))
            {
                continue;
            }

            npc.controller = null;
            npc.Halt();
            npc.currentLocation?.characters.Remove(npc);
            if (!location.characters.Contains(npc))
            {
                location.characters.Add(npc);
            }

            npc.currentLocation = location;
            npc.Position = new Vector2(candidate.X * Game1.tileSize, candidate.Y * Game1.tileSize);
            npc.faceDirection(this.GetDirectionTowardPlayerFromTile(candidate));
            targetTile = candidate;
            return true;
        }

        return false;
    }

    private bool IsEscortTargetReached(GameLocation? location, string targetLocation)
    {
        if (location == null)
        {
            return false;
        }

        string current = BehaviorMemory.NormalizeCommitmentLocation(location.Name, location.Name);
        return string.Equals(current, targetLocation, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsKnownEscortTarget(string targetLocation)
    {
        return targetLocation is "Farm" or "Town" or "Mountain" or "Mine" or "Beach" or "Forest" or "BusStop"
            or "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
            or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
            or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent";
    }

    private void CompleteEscortToLocation(NPC npc, PendingEscortToLocation escort)
    {
        npc.controller = null;
        npc.Halt();
        npc.showTextAboveHead(this.BuildEscortArrivalGreeting(escort));
        var state = this.memory.GetState(npc);
        if (state != null)
        {
            this.memory.RecordNpcWorldAction(
                npc,
                "CompletedEscortToLocation",
                $"they guided the farmer to {escort.TargetLocationLabel}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, $"they guided the farmer to {escort.TargetLocationLabel}");
        }

        this.SpreadCommunityRipple(
            npc,
            "shared_experience",
            $"the farmer went with {npc.displayName} to {escort.TargetLocationLabel}",
            importance: 58
        );
        this.pendingEscorts.Remove(escort);
        this.ShowFeedback($"LivingNPCs：{npc.displayName} 已带你到{escort.TargetLocationLabel}。");
    }

    private void StopEscortToLocation(PendingEscortToLocation escort, NPC? npc, bool returnToSchedule)
    {
        if (npc != null && npc.controller == escort.LastAssignedController)
        {
            npc.controller = null;
            npc.Halt();
        }

        if (npc != null && returnToSchedule)
        {
            this.TryReturnNpcToCurrentSchedule(npc);
        }

        this.pendingEscorts.Remove(escort);
    }

    private bool TryReturnNpcToCurrentSchedule(NPC npc)
    {
        if (npc.Schedule == null || npc.Schedule.Count == 0)
        {
            return false;
        }

        object? scheduleEntry = npc.Schedule
            .Where(entry => entry.Key <= Game1.timeOfDay)
            .OrderByDescending(entry => entry.Key)
            .Select(entry => entry.Value)
            .FirstOrDefault();
        if (scheduleEntry == null
            || !TryReadScheduleDestination(scheduleEntry, out string locationName, out Point targetTile, out int facingDirection))
        {
            return false;
        }

        GameLocation? location;
        try
        {
            location = Game1.getLocationFromName(locationName);
        }
        catch
        {
            location = null;
        }

        if (location == null)
        {
            return false;
        }

        if (!this.IsSafeDestinationTile(location, targetTile, npc)
            && !this.TryFindOpenTileNear(location, targetTile, npc, out targetTile))
        {
            return false;
        }

        npc.currentLocation?.characters.Remove(npc);
        if (!location.characters.Contains(npc))
        {
            location.characters.Add(npc);
        }

        npc.currentLocation = location;
        npc.Position = new Vector2(targetTile.X * Game1.tileSize, targetTile.Y * Game1.tileSize);
        if (facingDirection is >= 0 and <= 3)
        {
            npc.faceDirection(facingDirection);
        }

        return true;
    }

    private string BuildEscortStartGreeting(string targetLocation)
    {
        return targetLocation switch
        {
            "Mine" => "我带你往矿井走，跟紧一点。",
            "Beach" => "我带你往海边走。",
            "ArchaeologyHouse" => "我带你去图书馆。",
            "Farm" => "我跟你去农场看看。",
            "Trailer" => "我带你去我家那边，跟上我。",
            _ => "我带路，你跟着我。"
        };
    }

    private string BuildEscortCaughtUpGreeting(PendingEscortToLocation escort)
    {
        return $"好，你跟上来了，我们继续去{escort.TargetLocationLabel}。";
    }

    private string BuildEscortDirectionHint(string nextLocation)
    {
        return nextLocation switch
        {
            "Mine" => "矿井入口就在前面。",
            "Mountain" => "先往山上走。",
            "Town" => "先回镇上。",
            "BusStop" => "先往巴士站那边走。",
            "Beach" => "从这边去海边。",
            "Forest" => "从这边去森林。",
            "Farm" => "从这边回农场。",
            "Trailer" => "我家就在镇上这边。",
            _ => "从这边走。"
        };
    }

    private string BuildEscortExitWaitHint(string nextLocation)
    {
        return nextLocation switch
        {
            "Mine" => "我先去矿井入口那边等你。",
            "Mountain" => "我先到山路那边等你。",
            "Town" => "我先到镇上那边等你。",
            "BusStop" => "我先到巴士站那边等你。",
            "Beach" => "我先到海边那边等你。",
            "Forest" => "我先到森林那边等你。",
            "Farm" => "我先到农场那边等你。",
            "Trailer" => "我先到家门口那边等你。",
            _ => "我先过去等你。"
        };
    }

    private string BuildEscortArrivalGreeting(PendingEscortToLocation escort)
    {
        return escort.TargetLocation switch
        {
            "Mine" => "到了，这里就是矿井。小心点。",
            "Beach" => "到了，海边就在这里。",
            "ArchaeologyHouse" => "到了，这里就是图书馆。",
            "Farm" => "到了，你的农场就在这里。",
            "Trailer" => "到了，这里就是我家。",
            _ => $"到了，就是{escort.TargetLocationLabel}。"
        };
    }

    private string GetEscortTargetLabel(string targetLocation)
    {
        return targetLocation switch
        {
            "Farm" => "农场",
            "Town" => "鹈鹕镇",
            "Mountain" => "山上",
            "Mine" => "矿井",
            "Beach" => "海边",
            "Forest" => "煤矿森林",
            "BusStop" => "巴士站",
            "Trailer" => "潘妮和帕姆的家",
            "Saloon" => "星之果实酒吧",
            "SeedShop" => "皮埃尔的杂货店",
            "ArchaeologyHouse" => "博物馆和图书馆",
            "Hospital" => "诊所",
            "JoshHouse" => "亚历克斯家",
            "HaleyHouse" => "海莉和艾米丽家",
            "SamHouse" => "山姆家",
            "ScienceHouse" => "罗宾家",
            "LeahHouse" => "莉亚家",
            "AnimalShop" => "玛妮牧场",
            "ElliottHouse" => "艾利欧特小屋",
            "Blacksmith" => "铁匠铺",
            "FishShop" => "鱼店",
            "WizardHouse" => "法师塔",
            "Tent" => "莱纳斯的帐篷",
            _ => targetLocation
        };
    }

    private bool TryFestivalInteraction(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiFestivalInteractions)
        {
            reason = "festival interactions are disabled";
            return false;
        }

        if (!this.CanUseWorldAction(npc, "festival_interaction", requireFriendly: false, out reason, allowDuringEvents: true))
        {
            return false;
        }

        if (!Game1.eventUp)
        {
            reason = "there is no active event";
            return false;
        }

        var state = this.memory.GetState(npc);
        npc.doEmote(20);
        if (state != null)
        {
            this.memory.RecordNpcWorldAction(
                npc,
                "FestivalInteraction",
                this.BuildWorldActionReason(action.Reason, "they shared a light special interaction during an event scene"),
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, "they shared a small festival interaction");
        }

        return true;
    }

    private bool TryAssistQuest(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiQuestAssists)
        {
            reason = "quest assists are disabled";
            return false;
        }

        if (!this.CanUseWorldAction(npc, "assist_quest", requireFriendly: true, out reason))
        {
            return false;
        }

        if (Game1.player?.questLog == null || Game1.player.questLog.Count == 0)
        {
            reason = "the farmer has no active quest";
            return false;
        }

        var state = this.memory.GetState(npc);
        npc.doEmote(16);
        if (state != null)
        {
            string questCue = string.IsNullOrWhiteSpace(action.QuestHint)
                ? "an active task"
                : action.QuestHint.Trim();
            this.memory.RecordNpcWorldAction(
                npc,
                "AssistedQuest",
                this.BuildWorldActionReason(action.Reason, $"they offered light non-completing help around {questCue}"),
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, "they offered light task help");
        }

        return true;
    }

    private bool CanUseWorldAction(
        NPC npc,
        string actionName,
        bool requireFriendly,
        out string reason,
        bool allowDuringEvents = false,
        bool allowDistantWhenExplicit = false
    )
    {
        reason = string.Empty;
        if (!this.config.EnableAiWorldActions)
        {
            reason = "AI world actions are disabled";
            return false;
        }

        if ((!allowDuringEvents && Game1.eventUp) || Game1.currentLocation == null || npc.currentLocation != Game1.currentLocation)
        {
            reason = "world action cannot run in the current scene";
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            reason = "there is no NPC state yet";
            return false;
        }

        bool atLeastFamiliar = state.InteractionComfortTier is "Familiar" or "Friendly" or "Trusted" or "Intimate";
        bool atLeastFriendly = state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate";
        if (state.HighestUnresolvedConflictSeverity >= 30)
        {
            reason = $"{actionName} is blocked by unresolved conflict";
            return false;
        }

        if (!allowDistantWhenExplicit && (requireFriendly ? !atLeastFriendly : !atLeastFamiliar))
        {
            reason = $"{actionName} requires a closer relationship";
            return false;
        }

        return true;
    }

    private string BuildWorldActionReason(string requestedReason, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedReason)
            ? fallback
            : $"{requestedReason.Trim()}; {fallback}";
    }

    private string BuildGiftSelectionReason(string prefix, GiftSelection selection)
    {
        string rememberedPreference = string.IsNullOrWhiteSpace(selection.MatchedPlayerPreference)
            ? string.Empty
            : $"; remembered farmer preference: {selection.MatchedPlayerPreference}";
        return $"{prefix}; selection basis: {selection.Reason}{rememberedPreference}";
    }

    private void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private void TryUpdatePendingWalks()
    {
        if (this.pendingWalks.Count == 0)
        {
            return;
        }

        if (Game1.eventUp)
        {
            foreach (var walk in this.pendingWalks.ToList())
            {
                this.TryFindNpcInCurrentLocation(walk.NpcName, out NPC? npc);
                this.StopWalkTogether(walk, npc);
            }

            return;
        }

        foreach (var walk in this.pendingWalks.ToList())
        {
            NPC? npc = null;
            if (walk.TotalDays != Game1.Date.TotalDays
                || Game1.timeOfDay >= walk.EndTimeOfDay
                || !this.TryFindNpcInCurrentLocation(walk.NpcName, out npc)
                || npc == null
                || npc.currentLocation?.Name != walk.LocationName)
            {
                this.StopWalkTogether(walk, npc);
                continue;
            }

            if (Game1.activeClickableMenu != null)
            {
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 4)
            {
                this.StopWalkTogether(walk, npc);
                continue;
            }

            if (npc.controller != null && walk.LastAssignedController != null && npc.controller != walk.LastAssignedController)
            {
                this.StopWalkTogether(walk, npc);
                continue;
            }

            if (npc.controller != null && walk.LastAssignedController == null)
            {
                this.StopWalkTogether(walk, npc);
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) <= 1.5f)
            {
                this.TryFacePlayer(npc);
                continue;
            }

            if (walk.LastPlayerTile == Game1.player.TilePoint && npc.controller != null)
            {
                continue;
            }

            if (!this.TryFindApproachTile(npc, out Point targetTile))
            {
                continue;
            }

            npc.controller = new PathFindController(
                npc,
                Game1.currentLocation,
                targetTile,
                this.GetDirectionTowardPlayerFromTile(targetTile)
            );
            walk.LastPlayerTile = Game1.player.TilePoint;
            walk.LastAssignedController = npc.controller;
        }
    }

    private void TryApplyDialogueBehaviorInfluences()
    {
        if (!this.config.EnableDialogueDrivenBehaviors
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null)
            {
                continue;
            }

            foreach (var influence in state.DialogueBehaviorInfluences.Where(influence => influence.Status == "Active").ToList())
            {
                if (influence.ExpiresTotalDays < Game1.Date.TotalDays)
                {
                    influence.Status = "Expired";
                    influence.LastUpdatedTotalDays = Game1.Date.TotalDays;
                    influence.LastUpdatedTimeOfDay = Game1.timeOfDay;
                }
            }

            var activeInfluence = state.ActiveDialogueBehaviorInfluences
                .Where(influence => influence.LastTriggeredTotalDays != Game1.Date.TotalDays)
                .OrderByDescending(influence => influence.Intensity)
                .ThenBy(influence => influence.CreatedTotalDays)
                .FirstOrDefault();
            if (activeInfluence == null || !this.CanTryDialogueBehaviorInfluence(npc, activeInfluence))
            {
                continue;
            }

            if (!this.TryApplyDialogueBehaviorInfluence(npc, state, activeInfluence))
            {
                continue;
            }

            activeInfluence.TriggerCount += 1;
            activeInfluence.LastTriggeredTotalDays = Game1.Date.TotalDays;
            activeInfluence.LastTriggeredTimeOfDay = Game1.timeOfDay;
            activeInfluence.LastUpdatedTotalDays = Game1.Date.TotalDays;
            activeInfluence.LastUpdatedTimeOfDay = Game1.timeOfDay;
            if (activeInfluence.TriggerCount >= activeInfluence.MaxTriggers)
            {
                activeInfluence.Status = "Spent";
            }

            this.memory.RecordNpcWorldAction(
                npc,
                "DialogueDrivenBehavior",
                $"a recent conversation shaped later behavior: {activeInfluence.Type}; {activeInfluence.Summary}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.PushInteractionContext(npc, $"Applied dialogue-driven behavior for {npc.Name}: {activeInfluence.PromptLabel}.");
        }
    }

    private bool CanTryDialogueBehaviorInfluence(NPC npc, DialogueBehaviorInfluenceFact influence)
    {
        float distance = Vector2.Distance(npc.Tile, Game1.player.Tile);
        return influence.Type switch
        {
            "visit_location" => this.IsDialogueInfluenceTargetLocationCurrent(influence)
                && distance <= this.config.MaxInteractionDistanceTiles + 2,
            "offended" or "give_space" => distance <= 2.5f && npc.controller == null,
            "companion_walk" => distance <= this.config.MaxInteractionDistanceTiles && npc.controller == null,
            "stay_near" or "comforted" or "pause_to_talk" => distance <= this.config.MaxInteractionDistanceTiles && npc.controller == null,
            _ => false
        };
    }

    private bool TryApplyDialogueBehaviorInfluence(NPC npc, LivingNpcState state, DialogueBehaviorInfluenceFact influence)
    {
        bool executed = influence.Type switch
        {
            "companion_walk" => this.TryStartWalkTogether(
                npc,
                new ValleyTalkWorldActionRequest
                {
                    Type = "walk_together",
                    DurationMinutes = System.Math.Clamp(8 + (influence.Intensity / 15), 5, this.config.MaxAiWalkTogetherMinutes),
                    Reason = influence.Summary
                },
                out _
            ),
            "visit_location" => this.TryReactAtDialogueInfluenceLocation(npc, influence),
            "comforted" => this.TryApplyComfortedInfluence(npc, state),
            "stay_near" => this.TryApplyStayNearInfluence(npc, state),
            "offended" => this.TryApplyDistanceInfluence(npc, state, "they kept more distance after the conversation"),
            "give_space" => this.TryApplyDistanceInfluence(npc, state, "they gave the farmer a little more room after the conversation"),
            "pause_to_talk" => this.TryApplyPauseInfluence(npc, state),
            _ => false
        };

        if (!executed)
        {
            return false;
        }

        state.LastInteraction = $"a recent conversation shaped their later behavior: {influence.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return true;
    }

    private bool TryApplyComfortedInfluence(NPC npc, LivingNpcState state)
    {
        bool moved = Vector2.Distance(npc.Tile, Game1.player.Tile) > 2.25f && this.TryApproachPlayer(npc);
        if (!moved)
        {
            this.TryFacePlayer(npc);
            if (this.config.AllowEmotes)
            {
                npc.doEmote(20);
            }
        }

        state.Mood = "Comfortable";
        state.CurrentInclination = "OpenToTalk";
        return moved || this.config.AllowFacePlayer;
    }

    private bool TryApplyStayNearInfluence(NPC npc, LivingNpcState state)
    {
        bool moved = Vector2.Distance(npc.Tile, Game1.player.Tile) > 2.25f && this.TryApproachPlayer(npc);
        if (!moved)
        {
            this.TryFacePlayer(npc);
        }

        state.Mood = "Engaged";
        state.CurrentInclination = "OpenToTalk";
        return moved || this.config.AllowFacePlayer;
    }

    private bool TryApplyDistanceInfluence(NPC npc, LivingNpcState state, string lastInteraction)
    {
        bool moved = this.TryStepAway(npc);
        if (!moved)
        {
            return false;
        }

        state.Mood = "Guarded";
        state.CurrentInclination = "GentleBoundary";
        state.LastInteraction = lastInteraction;
        return true;
    }

    private bool TryApplyPauseInfluence(NPC npc, LivingNpcState state)
    {
        if (!this.TryPause(npc))
        {
            return false;
        }

        state.Mood = "Attentive";
        state.CurrentInclination = "Acknowledging";
        return true;
    }

    private bool TryReactAtDialogueInfluenceLocation(NPC npc, DialogueBehaviorInfluenceFact influence)
    {
        if (!this.IsDialogueInfluenceTargetLocationCurrent(influence))
        {
            return false;
        }

        this.TryFacePlayer(npc);
        npc.showTextAboveHead(this.BuildDialogueInfluenceLocationRemark(influence));
        return true;
    }

    private bool IsDialogueInfluenceTargetLocationCurrent(DialogueBehaviorInfluenceFact influence)
    {
        string currentLocation = BehaviorMemory.NormalizeCommitmentLocation(Game1.currentLocation?.Name ?? string.Empty, string.Empty);
        return !string.IsNullOrWhiteSpace(influence.TargetLocation)
            && influence.TargetLocation == currentLocation;
    }

    private string BuildDialogueInfluenceLocationRemark(DialogueBehaviorInfluenceFact influence)
    {
        return influence.TargetLocation switch
        {
            "Beach" => "你提到海边后，我也想来看看。",
            "ArchaeologyHouse" => "你提到这里后，我就想顺路来看看。",
            "Forest" => "刚才聊到森林，我就想来走走。",
            "Mountain" => "你提到山上后，我也有点在意这里。",
            _ => $"你刚才提到{influence.TargetLocationLabel}，我后来还想着这件事。"
        };
    }

    private void TryUpdateCommitmentTimers()
    {
        if (!this.config.EnableCommitments)
        {
            return;
        }

        foreach (var state in this.memory.GetTrackedStates())
        {
            foreach (var commitment in state.Commitments.Where(commitment => commitment.Status is "Pending" or "Waiting"))
            {
                if (this.IsPastCommitmentGraceWindow(commitment))
                {
                    commitment.Status = "Expired";
                    commitment.LastUpdatedTotalDays = Game1.Date.TotalDays;
                    commitment.LastUpdatedTimeOfDay = Game1.timeOfDay;
                    this.memory.UpdateStateForExpiredCommitment(state, commitment);
                    NPC? npc = Game1.getCharacterFromName(state.NpcName);
                    if (npc != null)
                    {
                        this.memory.RecordNpcWorldAction(
                            npc,
                            "ExpiredCommitment",
                            $"the farmer missed an agreed plan: {commitment.Summary}",
                            this.config.MaxMemoryEntriesPerNpc
                        );
                        this.PushInteractionContext(npc, $"Expired commitment for {npc.Name}: {commitment.Summary}.");
                        this.TryReturnNpcToSchedule(npc, commitment);
                    }
                    continue;
                }

                if (this.IsWithinCommitmentWindow(commitment))
                {
                    state.LastInteraction = "waiting around an agreed plan";
                    state.LastUpdatedTotalDays = Game1.Date.TotalDays;
                    state.LastUpdatedTimeOfDay = Game1.timeOfDay;
                }
            }
        }
    }

    private void TryUpdateHelpRequestTimers()
    {
        if (!this.config.EnableHelpRequests)
        {
            return;
        }

        bool changed = false;
        foreach (var state in this.memory.GetTrackedStates())
        {
            foreach (var request in state.HelpRequests.Where(request => request.Status is "Offered" or "Pending"))
            {
                if (request.DueTotalDays >= Game1.Date.TotalDays)
                {
                    continue;
                }

                request.Status = "Expired";
                request.LastUpdatedTotalDays = Game1.Date.TotalDays;
                request.LastUpdatedTimeOfDay = Game1.timeOfDay;
                changed = true;
                this.memory.UpdateStateForExpiredHelpRequest(state, request);
                if (this.TryFindNpcInCurrentLocation(state.NpcName, out NPC? npc) && npc != null)
                {
                    this.memory.RecordNpcWorldAction(
                        npc,
                        "ExpiredHelpRequest",
                        $"a personal help request went unanswered: {request.Summary}",
                        this.config.MaxMemoryEntriesPerNpc
                    );
                    this.PushInteractionContext(npc, $"Expired help request for {npc.Name}: {request.Summary}.");
                }
            }
        }

        if (changed)
        {
            this.SyncHelpRequestsToQuestLog();
        }
    }

    private void TryTriggerCommitmentArrivals()
    {
        if (!this.config.EnableCommitments
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.eventUp)
        {
            return;
        }

        this.TryDispatchPendingCommitmentNpcsToWaitingLocations();

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null)
            {
                continue;
            }

            var commitment = state.Commitments.FirstOrDefault(candidate =>
                candidate.Status is "Pending" or "Waiting"
                && !candidate.ArrivalGreetingShown
                && this.IsCommitmentForCurrentLocation(candidate)
                && this.IsWithinCommitmentWindow(candidate)
            );
            if (commitment == null)
            {
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > 3f)
            {
                if (commitment.Status == "Waiting" && !commitment.WaitingGreetingShown)
                {
                    if (this.TryShowNpcSpeechBubble(npc, this.BuildCommitmentWaitingGreeting(commitment), 5500))
                    {
                        commitment.WaitingGreetingShown = true;
                        commitment.LastMentionedTotalDays = Game1.Date.TotalDays;
                        commitment.LastMentionedTimeOfDay = Game1.timeOfDay;
                    }
                }

                if (npc.controller == null)
                {
                    this.TryApproachPlayer(npc);
                }

                continue;
            }

            this.FulfillCommitment(npc, commitment);
        }
    }

    private void TryDispatchPendingCommitmentNpcsToWaitingLocations()
    {
        if (Game1.player == null || Game1.eventUp)
        {
            return;
        }

        foreach (var state in this.memory.GetTrackedStates())
        {
            var commitment = state.Commitments.FirstOrDefault(candidate =>
                candidate.Status is "Pending" or "Waiting"
                && !candidate.ArrivalGreetingShown
                && this.IsWithinCommitmentWindow(candidate)
            );
            if (commitment == null)
            {
                continue;
            }

            NPC? npc = Game1.getCharacterFromName(state.NpcName);
            GameLocation? targetLocation = this.ResolveCommitmentLocation(commitment);
            if (npc == null || targetLocation == null)
            {
                continue;
            }

            bool playerIsAtTarget = this.IsLocationForCommitment(Game1.currentLocation, commitment);
            if (npc.currentLocation == targetLocation && commitment.Status == "Waiting")
            {
                continue;
            }

            if (this.TryMoveNpcToCommitmentRendezvous(npc, commitment, targetLocation, playerIsAtTarget) && this.config.Debug)
            {
                this.monitor.Log(
                    $"Moved {npc.Name} to {targetLocation.Name} to wait for LivingNPCs commitment: {commitment.Summary}",
                    LogLevel.Debug
                );
            }
        }
    }

    private bool FulfillCommitment(NPC npc, NpcCommitmentFact commitment)
    {
        if (!this.TryShowNpcSpeechBubble(npc, this.BuildCommitmentArrivalGreeting(commitment), 5500))
        {
            return false;
        }

        commitment.ArrivalGreetingShown = true;
        commitment.Status = "Fulfilled";
        commitment.FulfilledTotalDays = Game1.Date.TotalDays;
        commitment.FulfilledTimeOfDay = Game1.timeOfDay;
        commitment.LastUpdatedTotalDays = Game1.Date.TotalDays;
        commitment.LastUpdatedTimeOfDay = Game1.timeOfDay;
        this.memory.UpdateStateForFulfilledCommitment(npc, commitment);
        this.memory.RecordNpcWorldAction(
            npc,
            "FulfilledCommitment",
            $"they fulfilled an agreed plan with the farmer: {commitment.Summary}",
            this.config.MaxMemoryEntriesPerNpc
        );
        this.PushInteractionContext(npc, $"Fulfilled commitment for {npc.Name}: {commitment.Summary}.");
        this.SpreadCommunityRipple(
            npc,
            "shared_experience",
            $"the farmer kept a plan with {npc.displayName}: {commitment.Summary}",
            importance: 72
        );
        return true;
    }

    private bool TryMoveNpcToCommitmentRendezvous(NPC npc, NpcCommitmentFact commitment, GameLocation location, bool preferPlayerTile)
    {
        if (Game1.player == null || !this.TryFindCommitmentArrivalTile(location, commitment, preferPlayerTile, out Point targetTile))
        {
            return false;
        }

        try
        {
            npc.controller = null;
            npc.Halt();
            npc.currentLocation?.characters.Remove(npc);
            if (!location.characters.Contains(npc))
            {
                location.characters.Add(npc);
            }

            npc.currentLocation = location;
            npc.Position = new Vector2(targetTile.X * Game1.tileSize, targetTile.Y * Game1.tileSize);
            if (location == Game1.currentLocation)
            {
                npc.faceDirection(this.GetDirectionTowardPlayerFromTile(targetTile));
            }

            if (commitment.Status != "Waiting")
            {
                commitment.Status = "Waiting";
                commitment.WaitingStartedTotalDays = Game1.Date.TotalDays;
                commitment.WaitingStartedTimeOfDay = Game1.timeOfDay;
                commitment.WaitingLocationName = BehaviorMemory.NormalizeCommitmentLocation(location.Name, commitment.LocationName);
                commitment.WaitingTileX = targetTile.X;
                commitment.WaitingTileY = targetTile.Y;
                commitment.LastUpdatedTotalDays = Game1.Date.TotalDays;
                commitment.LastUpdatedTimeOfDay = Game1.timeOfDay;
                this.memory.RecordNpcWorldAction(
                    npc,
                    "ArrivedForCommitment",
                    $"they came to {commitment.LocationLabel} to wait for an agreed plan: {commitment.Summary}",
                    this.config.MaxMemoryEntriesPerNpc
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Could not move {npc.Name} for LivingNPCs commitment: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private bool TryReturnNpcToSchedule(NPC npc, NpcCommitmentFact commitment)
    {
        if (npc.Schedule == null || npc.Schedule.Count == 0)
        {
            return false;
        }

        object? scheduleEntry = npc.Schedule
            .Where(entry => entry.Key <= Game1.timeOfDay)
            .OrderByDescending(entry => entry.Key)
            .Select(entry => entry.Value)
            .FirstOrDefault();
        if (scheduleEntry == null
            || !TryReadScheduleDestination(scheduleEntry, out string locationName, out Point targetTile, out int facingDirection))
        {
            return false;
        }

        GameLocation? location;
        try
        {
            location = Game1.getLocationFromName(locationName);
        }
        catch
        {
            location = null;
        }

        if (location == null)
        {
            return false;
        }

        if (!this.IsSafeDestinationTile(location, targetTile, npc)
            && !this.TryFindOpenTileNear(location, targetTile, npc, out targetTile))
        {
            return false;
        }

        try
        {
            npc.controller = null;
            npc.Halt();
            npc.currentLocation?.characters.Remove(npc);
            if (!location.characters.Contains(npc))
            {
                location.characters.Add(npc);
            }

            npc.currentLocation = location;
            npc.Position = new Vector2(targetTile.X * Game1.tileSize, targetTile.Y * Game1.tileSize);
            if (facingDirection is >= 0 and <= 3)
            {
                npc.faceDirection(facingDirection);
            }

            if (this.config.Debug)
            {
                this.monitor.Log(
                    $"Returned {npc.Name} to schedule after missed LivingNPCs commitment at {commitment.LocationName}.",
                    LogLevel.Debug
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Could not return {npc.Name} to schedule after LivingNPCs commitment: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private bool TryFindOpenTileNear(GameLocation location, Point center, NPC ignoredNpc, out Point targetTile)
    {
        foreach (var candidate in this.GetTilesAround(center, 5))
        {
            if (this.IsSafeDestinationTile(location, candidate, ignoredNpc))
            {
                targetTile = candidate;
                return true;
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private static bool TryReadScheduleDestination(object scheduleEntry, out string locationName, out Point targetTile, out int facingDirection)
    {
        locationName = ReadScheduleString(scheduleEntry, "targetLocationName");
        facingDirection = ReadScheduleInt(scheduleEntry, "facingDirection", -1);
        targetTile = Point.Zero;
        return !string.IsNullOrWhiteSpace(locationName)
            && TryReadScheduleTile(scheduleEntry, "targetTile", out targetTile);
    }

    private static bool TryReadScheduleTile(object scheduleEntry, string memberName, out Point targetTile)
    {
        object? value = ReadScheduleMember(scheduleEntry, memberName);
        if (value is Point point)
        {
            targetTile = point;
            return true;
        }

        if (value is Vector2 vector)
        {
            targetTile = new Point((int)vector.X, (int)vector.Y);
            return true;
        }

        if (value != null
            && TryReadNumericMember(value, "X", out int x)
            && TryReadNumericMember(value, "Y", out int y))
        {
            targetTile = new Point(x, y);
            return true;
        }

        targetTile = Point.Zero;
        return false;
    }

    private static string ReadScheduleString(object source, string memberName)
    {
        return ReadScheduleMember(source, memberName) as string ?? string.Empty;
    }

    private static int ReadScheduleInt(object source, string memberName, int fallback)
    {
        object? value = ReadScheduleMember(source, memberName);
        if (value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryReadNumericMember(object source, string memberName, out int value)
    {
        object? raw = ReadScheduleMember(source, memberName);
        if (raw == null)
        {
            value = 0;
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static object? ReadScheduleMember(object source, string memberName)
    {
        var type = source.GetType();
        return type.GetField(memberName)?.GetValue(source)
            ?? type.GetProperty(memberName)?.GetValue(source);
    }

    private bool IsCommitmentForCurrentLocation(NpcCommitmentFact commitment)
    {
        return this.IsLocationForCommitment(Game1.currentLocation, commitment);
    }

    private bool IsLocationForCommitment(GameLocation? location, NpcCommitmentFact commitment)
    {
        string currentLocation = BehaviorMemory.NormalizeCommitmentLocation(location?.Name ?? string.Empty, string.Empty);
        return !string.IsNullOrWhiteSpace(currentLocation)
            && string.Equals(commitment.LocationName, currentLocation, StringComparison.OrdinalIgnoreCase);
    }

    private GameLocation? ResolveCommitmentLocation(NpcCommitmentFact commitment)
    {
        try
        {
            return commitment.LocationName switch
            {
                "Farm" => Game1.getFarm(),
                _ => Game1.getLocationFromName(commitment.LocationName)
            };
        }
        catch
        {
            return null;
        }
    }

    private bool TryFindCommitmentArrivalTile(
        GameLocation location,
        NpcCommitmentFact commitment,
        bool preferPlayerTile,
        out Point targetTile)
    {
        targetTile = Point.Zero;
        if (preferPlayerTile && location == Game1.currentLocation)
        {
            var playerTile = Game1.player.TilePoint;
            var playerCandidates = new List<Point>();
            for (int radius = 1; radius <= 3; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        {
                            continue;
                        }

                        playerCandidates.Add(new Point(playerTile.X + dx, playerTile.Y + dy));
                    }
                }
            }

            foreach (var candidate in playerCandidates
                .Distinct()
                .OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), Game1.player.Tile)))
            {
                if (this.IsSafeDestinationTile(location, candidate))
                {
                    targetTile = candidate;
                    return true;
                }
            }
        }

        foreach (var candidate in this.GetCommitmentAnchorCandidates(location, commitment.LocationName))
        {
            if (this.IsSafeDestinationTile(location, candidate))
            {
                targetTile = candidate;
                return true;
            }
        }

        var fallbackCenter = new Point(location.Map.Layers[0].LayerWidth / 2, location.Map.Layers[0].LayerHeight / 2);
        foreach (var candidate in this.GetTilesAround(fallbackCenter, 8))
        {
            if (this.IsSafeDestinationTile(location, candidate))
            {
                targetTile = candidate;
                return true;
            }
        }

        return false;
    }

    private IEnumerable<Point> GetCommitmentAnchorCandidates(GameLocation location, string normalizedLocationName)
    {
        if (normalizedLocationName == "Farm")
        {
            foreach (var warp in location.warps.Where(warp => IsFarmhouseWarp(warp)))
            {
                var warpTile = new Point(warp.X, warp.Y);
                foreach (var tile in this.GetTilesAround(warpTile, 4))
                {
                    yield return tile;
                }
            }
        }

        Point[] preferred = normalizedLocationName switch
        {
            "Farm" =>
            [
                new Point(64, 15),
                new Point(63, 15),
                new Point(65, 15),
                new Point(64, 16),
                new Point(63, 16),
                new Point(65, 16)
            ],
            "Town" =>
            [
                new Point(43, 57),
                new Point(44, 57),
                new Point(42, 57),
                new Point(43, 56)
            ],
            "Beach" =>
            [
                new Point(36, 34),
                new Point(37, 34),
                new Point(35, 34)
            ],
            "Forest" =>
            [
                new Point(34, 49),
                new Point(35, 49),
                new Point(34, 50)
            ],
            "Mountain" =>
            [
                new Point(31, 20),
                new Point(32, 20),
                new Point(31, 21)
            ],
            "BusStop" =>
            [
                new Point(11, 23),
                new Point(12, 23),
                new Point(11, 24)
            ],
            "Saloon" =>
            [
                new Point(10, 18),
                new Point(11, 18),
                new Point(10, 17)
            ],
            "SeedShop" =>
            [
                new Point(6, 17),
                new Point(7, 17),
                new Point(6, 16)
            ],
            "ArchaeologyHouse" =>
            [
                new Point(12, 10),
                new Point(13, 10),
                new Point(12, 11)
            ],
            "Hospital" =>
            [
                new Point(12, 14),
                new Point(13, 14),
                new Point(12, 13)
            ],
            _ => []
        };

        foreach (var tile in preferred)
        {
            yield return tile;
        }

        foreach (var warp in location.warps)
        {
            var warpTile = new Point(warp.X, warp.Y);
            foreach (var tile in this.GetTilesAround(warpTile, 3))
            {
                yield return tile;
            }
        }
    }

    private static bool IsFarmhouseWarp(object warp)
    {
        string targetName = GetWarpTargetName(warp);
        return targetName.Equals("FarmHouse", StringComparison.OrdinalIgnoreCase)
            || targetName.Equals("Farmhouse", StringComparison.OrdinalIgnoreCase)
            || targetName.Equals("Cabin", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWarpTargetName(object warp)
    {
        foreach (string memberName in new[] { "TargetName", "targetName", "TargetLocationName", "targetLocationName" })
        {
            if (ReadScheduleMember(warp, memberName) is string targetName && !string.IsNullOrWhiteSpace(targetName))
            {
                return targetName;
            }
        }

        return string.Empty;
    }

    private IEnumerable<Point> GetTilesAround(Point center, int maxRadius)
    {
        yield return center;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    yield return new Point(center.X + dx, center.Y + dy);
                }
            }
        }
    }

    private string BuildCommitmentArrivalGreeting(NpcCommitmentFact commitment)
    {
        return commitment.Type switch
        {
            "go_together" => "你来了，我们按约定一起走吧。",
            "help_task" => "你来了，我记得答应过要帮你。",
            "celebrate_together" => "你来了，正好一起庆祝。",
            "share_activity" => "你来了，我们按说好的去做点什么吧。",
            _ => "你来了，我记得我们的约定。"
        };
    }

    private string BuildCommitmentWaitingGreeting(NpcCommitmentFact commitment)
    {
        return commitment.Type switch
        {
            "go_together" => "我到了，会在这里等你一会儿。",
            "help_task" => "我到了，等你来我们就开始。",
            "celebrate_together" => "我到了，等你一起庆祝。",
            "share_activity" => "我到了，等你来一起做那件事。",
            _ => "我到了，会在这里等你一会儿。"
        };
    }

    private void TryShowCommitmentMorningReminders()
    {
        if (!this.config.EnableCommitments
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.activeClickableMenu != null
            || Game1.eventUp
            || Game1.timeOfDay < 600
            || Game1.timeOfDay > 1000)
        {
            return;
        }

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null)
            {
                continue;
            }

            var commitment = state.Commitments.FirstOrDefault(candidate =>
                candidate.Status == "Pending"
                && candidate.DueTotalDays == Game1.Date.TotalDays
                && !candidate.MorningReminderShown
                && this.ShouldShowMorningCommitmentReminder(candidate)
            );
            if (commitment == null
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles)
            {
                continue;
            }

            npc.showTextAboveHead(this.BuildCommitmentMorningReminder(commitment));
            commitment.MorningReminderShown = true;
            commitment.LastMentionedTotalDays = Game1.Date.TotalDays;
            commitment.LastMentionedTimeOfDay = Game1.timeOfDay;
        }
    }

    private void TryShowSharedExperienceFollowUps()
    {
        if (!this.config.EnableDialogueFollowUps
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles)
            {
                continue;
            }

            var experience = state.SharedExperiences.FirstOrDefault(candidate =>
                candidate.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && candidate.FollowUpShownTotalDays < 0
                && candidate.CreatedTotalDays >= Game1.Date.TotalDays - 7
            );
            if (experience == null)
            {
                continue;
            }

            npc.showTextAboveHead(this.BuildSharedExperienceFollowUp(experience));
            experience.FollowUpShownTotalDays = Game1.Date.TotalDays;
            experience.FollowUpShownTimeOfDay = Game1.timeOfDay;
        }
    }

    private void TryShowHelpRequestFollowUps()
    {
        if (!this.config.EnableDialogueFollowUps
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles)
            {
                continue;
            }

            var request = state.HelpRequests.FirstOrDefault(candidate =>
                candidate.Status == "Fulfilled"
                && candidate.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && candidate.FollowUpShownTotalDays < 0
                && candidate.FulfilledTotalDays >= Game1.Date.TotalDays - 7
            );
            if (request == null)
            {
                continue;
            }

            npc.showTextAboveHead(this.BuildHelpRequestFollowUp(request));
            request.FollowUpShownTotalDays = Game1.Date.TotalDays;
            request.FollowUpShownTimeOfDay = Game1.timeOfDay;
        }
    }

    private string BuildCommitmentMorningReminder(NpcCommitmentFact commitment)
    {
        return commitment.Type switch
        {
            "celebrate_together" => $"别忘了，我们今天要一起庆祝。",
            "share_activity" => $"别忘了，我们今天还约好一起活动。",
            "go_together" => $"别忘了，我们今天还约好一起去{commitment.LocationLabel}。",
            "help_task" => "别忘了，我们今天还约好要一起处理那件事。",
            _ => $"别忘了，我们今天还约好在{commitment.LocationLabel}见面。"
        };
    }

    private string BuildSharedExperienceFollowUp(SharedExperienceFact experience)
    {
        return experience.Type switch
        {
            "celebrate_together" => "上次一起庆祝，我到现在还记得。",
            "share_activity" => "上次一起做那件事，挺开心的。",
            "go_together" => $"上次一起去{experience.LocationLabel}，感觉不错。",
            "help_task" => "上次一起把事情做完，我很高兴。",
            "help_request" => "上次你愿意帮我，我一直记得。",
            _ => "上次一起度过的时间，我还记得。"
        };
    }

    private string BuildHelpRequestFollowUp(NpcHelpRequestFact request)
    {
        return request.Type switch
        {
            "question_request" => "上次你说的那些，我后来还想了想。",
            _ => "上次你带来的东西，正好派上了用场。"
        };
    }

    private bool ShouldShowMorningCommitmentReminder(NpcCommitmentFact commitment)
    {
        unchecked
        {
            string seed = $"{commitment.Type}:{commitment.Summary}:{commitment.DueTotalDays}:{commitment.LocationName}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return System.Math.Abs(hash % 100) < 45;
        }
    }

    private bool IsWithinCommitmentWindow(NpcCommitmentFact commitment)
    {
        if (commitment.DueTotalDays != Game1.Date.TotalDays)
        {
            return false;
        }

        int now = this.ToDayMinutes(Game1.timeOfDay);
        int due = this.ToDayMinutes(commitment.TimeOfDay);
        return now >= due && now <= due + this.config.CommitmentGraceMinutes;
    }

    private bool IsPastCommitmentGraceWindow(NpcCommitmentFact commitment)
    {
        if (commitment.DueTotalDays < Game1.Date.TotalDays)
        {
            return true;
        }

        if (commitment.DueTotalDays > Game1.Date.TotalDays)
        {
            return false;
        }

        return this.ToDayMinutes(Game1.timeOfDay) > this.ToDayMinutes(commitment.TimeOfDay) + this.config.CommitmentGraceMinutes;
    }

    private int ToDayMinutes(int timeOfDay)
    {
        int hours = timeOfDay / 100;
        int minutes = timeOfDay % 100;
        return (hours * 60) + minutes;
    }

    private void StopWalkTogether(PendingWalkTogether walk, NPC? npc)
    {
        if (npc != null && npc.controller == walk.LastAssignedController)
        {
            npc.controller = null;
            npc.Halt();
        }

        this.pendingWalks.Remove(walk);
    }

    private void TryRecordConversationStart(ButtonPressedEventArgs e)
    {
        if (!this.config.EnableConversationMemory || !e.Button.IsActionButton())
        {
            return;
        }

        if (!this.TryFindNpcForInteraction(e.Cursor, out NPC? npc) || npc == null)
        {
            return;
        }

        SObject? heldGift = Game1.player.ActiveObject;
        int timeMarker = (Game1.Date.TotalDays * 10000) + Game1.timeOfDay;
        if (this.lastConversationMemoryTimeByNpc.TryGetValue(npc.Name, out int lastTimeMarker) && lastTimeMarker == timeMarker)
        {
            return;
        }

        this.lastConversationMemoryTimeByNpc[npc.Name] = timeMarker;
        this.TryRecordObservedRomanticInteraction(npc);
        if (heldGift != null)
        {
            var gift = this.BuildGiftMemoryDetails(npc, heldGift);
            this.memory.RecordGiftOffered(npc, gift, this.config.MaxMemoryEntriesPerNpc);
            if (this.config.EnableNpcState)
            {
                this.memory.UpdateStateForGift(npc, gift);
            }

            if (this.config.EnableHelpRequests)
            {
                IReadOnlyList<NpcHelpRequestFact> changedHelpRequests = this.memory.TryCompleteItemHelpRequests(npc, gift, this.config.MaxMemoryEntriesPerNpc);
                if (changedHelpRequests.Count > 0)
                {
                    this.RewardFulfilledHelpRequests(npc, changedHelpRequests);
                    this.SyncHelpRequestsToQuestLog();
                    int fulfilledCount = changedHelpRequests.Count(request => request.Status == "Fulfilled");
                    int advancedCount = changedHelpRequests.Count - fulfilledCount;
                    this.PushInteractionContext(npc, $"Updated {changedHelpRequests.Count} help request(s) for {npc.Name} through a gifted item: {fulfilledCount} fulfilled, {advancedCount} advanced.");
                }
            }

            this.PushInteractionContext(npc, $"Recorded gift interaction for {npc.Name}: {gift.ItemName} ({gift.TastePromptLabel}).");
            return;
        }

        if (Game1.eventUp)
        {
            string eventContext = this.DescribeCurrentEventContext();
            this.memory.RecordEventInteraction(npc, eventContext, this.config.MaxMemoryEntriesPerNpc);
            if (this.config.EnableNpcState)
            {
                this.memory.UpdateStateForEventInteraction(npc, eventContext);
            }

            this.PushInteractionContext(npc, $"Recorded event interaction for {npc.Name}: {eventContext}.");
            return;
        }

        this.memory.RecordConversationStart(npc, this.config.MaxMemoryEntriesPerNpc);
        if (this.config.EnableNpcState)
        {
            LivingNpcState state = this.memory.UpdateStateForConversationStart(npc);
            this.TrySpreadConversationSocialRipple(npc, state);
        }

        this.PushInteractionContext(npc, $"Recorded conversation start for {npc.Name}.");
        this.MarkCommitmentFollowUpsMentionedAfterPrompt(npc);
    }

    private void TryRecordObservedRomanticInteraction(NPC targetNpc)
    {
        if (Game1.currentLocation == null
            || Game1.player == null
            || !this.IsRomanticallyAttachedToFarmer(targetNpc))
        {
            return;
        }

        foreach (var observer in Game1.currentLocation.characters.Where(candidate =>
                     candidate.Name != targetNpc.Name
                     && !string.IsNullOrWhiteSpace(candidate.Name)
                     && Vector2.Distance(candidate.Tile, Game1.player.Tile) <= 6
                     && this.IsRomanticallyAttachedToFarmer(candidate)))
        {
            var state = this.memory.GetState(observer);
            if (state?.CurrentEmotion == "Jealous"
                && state.LastEmotionUpdatedTotalDays == Game1.Date.TotalDays)
            {
                continue;
            }

            this.memory.UpdateStateForObservedRomanticInteraction(observer, targetNpc);
            this.memory.RecordNpcWorldAction(
                observer,
                "ObservedRomanticInteraction",
                $"they noticed the farmer being close with {targetNpc.displayName}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.PushInteractionContext(observer, $"Observed romantic interaction involving {targetNpc.Name}.");
        }

        this.SpreadCommunityRipple(
            targetNpc,
            "romantic_attention",
            $"the farmer has been giving romantic attention to {targetNpc.displayName}",
            importance: 70
        );
    }

    private void TrySpreadConversationSocialRipple(NPC npc, LivingNpcState state)
    {
        bool relationshipIsNoticeable = state.ConsecutiveConversationDays >= 3
            || state.ConversationsToday >= 2
            || state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate";
        if (!relationshipIsNoticeable)
        {
            return;
        }

        int importance = state.InteractionComfortTier switch
        {
            "Intimate" => 74,
            "Trusted" => 68,
            "Friendly" => 60,
            _ when state.ConsecutiveConversationDays >= 5 => 56,
            _ => 48
        };
        this.SpreadCommunityRipple(
            npc,
            "relationship_trend",
            $"the farmer has been speaking with {npc.displayName} more often lately",
            importance
        );
    }

    private void SpreadCommunityRipple(NPC subject, string kind, string summary, int importance)
    {
        if (Game1.currentLocation == null)
        {
            return;
        }

        string visibility = this.GetCommunityRippleVisibility(kind);
        LivingNpcState? subjectState = this.memory.GetState(subject);
        var directWitnesses = Game1.currentLocation.characters
            .Where(candidate =>
                candidate.Name != subject.Name
                && !string.IsNullOrWhiteSpace(candidate.Name)
                && Vector2.Distance(candidate.Tile, subject.Tile) <= 8f)
            .ToList();

        var directWitnessNames = directWitnesses
            .Select(candidate => candidate.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int stored = 0;

        foreach (var witness in directWitnesses)
        {
            if (this.memory.RecordCommunityImpression(
                    witness,
                    subject,
                    kind,
                    summary,
                    source: "Witnessed",
                    visibility: visibility,
                    transmissionDepth: 0,
                    distortionLevel: 0,
                    heardFromNpcName: string.Empty,
                    circleKey: "direct_witness",
                    importance: importance,
                    maxEntriesPerNpc: this.config.MaxMemoryEntriesPerNpc))
            {
                stored++;
            }
        }

        IEnumerable<string> closeConnections = NpcSocialGraph.GetCloseConnections(subject.Name);
        if (visibility == "Private")
        {
            closeConnections = closeConnections.Take(2);
        }

        if (this.CanSpreadToCloseCircle(visibility, subjectState))
        {
            foreach (string connectionName in closeConnections)
            {
                if (directWitnessNames.Contains(connectionName))
                {
                    continue;
                }

                NPC? connection = Game1.getCharacterFromName(connectionName);
                if (connection == null
                    || string.Equals(connection.Name, subject.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (this.memory.RecordCommunityImpression(
                        connection,
                        subject,
                        kind,
                        summary,
                        source: "CloseCircle",
                        visibility: visibility,
                        transmissionDepth: 1,
                        distortionLevel: 10,
                        heardFromNpcName: subject.Name,
                        circleKey: "close_connections",
                        importance: System.Math.Max(40, importance - 14),
                        maxEntriesPerNpc: this.config.MaxMemoryEntriesPerNpc))
                {
                    stored++;
                }
            }
        }

        if (visibility == "Public" && this.IsPublicRumorHub(Game1.currentLocation.Name))
        {
            foreach (var observer in Game1.currentLocation.characters
                         .Where(candidate =>
                             candidate.Name != subject.Name
                             && !string.IsNullOrWhiteSpace(candidate.Name)
                             && !directWitnessNames.Contains(candidate.Name))
                         .OrderBy(candidate => Vector2.Distance(candidate.Tile, subject.Tile))
                         .Take(4))
            {
                if (this.memory.RecordCommunityImpression(
                        observer,
                        subject,
                        kind,
                        summary,
                        source: "PublicRumor",
                        visibility: visibility,
                        transmissionDepth: 1,
                        distortionLevel: 18,
                        heardFromNpcName: string.Empty,
                        circleKey: "public_hub",
                        importance: System.Math.Max(28, importance - 24),
                        maxEntriesPerNpc: this.config.MaxMemoryEntriesPerNpc))
                {
                    stored++;
                }
            }
        }

        if (stored > 0 && this.config.Debug)
        {
            this.monitor.Log($"Spread community ripple for {subject.Name}: {summary} -> {stored} observer memory record(s).", LogLevel.Debug);
        }
    }

    private string GetCommunityRippleVisibility(string kind)
    {
        return BehaviorMemory.NormalizeCommunityImpressionKind(kind) switch
        {
            "romantic_attention" => "Private",
            "helped" or "shared_experience" => "Personal",
            _ => "Public"
        };
    }

    private bool CanSpreadToCloseCircle(string visibility, LivingNpcState? subjectState)
    {
        return visibility switch
        {
            "Private" => subjectState != null
                && subjectState.RelationshipTrust >= 75
                && subjectState.InteractionComfortTier is "Trusted" or "Intimate",
            "Personal" => subjectState != null
                && (subjectState.RelationshipTrust >= 45
                    || subjectState.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate"),
            _ => true
        };
    }

    private bool IsPublicRumorHub(string locationName)
    {
        return locationName is "Town" or "Saloon";
    }

    private void TryPropagateCommunityImpressions()
    {
        foreach (var speakerState in this.memory.GetTrackedStates())
        {
            NPC? speaker = Game1.getCharacterFromName(speakerState.NpcName);
            if (speaker == null)
            {
                continue;
            }

            CommunityReactionCue reaction = CommunityReactionStyle.For(speaker);
            if (this.random.Next(100) >= reaction.SharePropensity)
            {
                continue;
            }

            CommunityImpressionFact? impression = this.memory
                .GetRetellableCommunityImpressions(speakerState, maxCount: 3)
                .FirstOrDefault(candidate => this.CanRetellCommunityImpression(candidate));
            if (impression == null)
            {
                continue;
            }

            NPC? subject = Game1.getCharacterFromName(impression.SubjectNpcName);
            if (subject == null)
            {
                continue;
            }

            var targets = NpcSocialGraph
                .GetStablePropagationTargets(speaker.Name, impression.Visibility)
                .Where(target => !string.Equals(target.NpcName, speaker.Name, StringComparison.OrdinalIgnoreCase))
                .Where(target => !string.Equals(target.NpcName, subject.Name, StringComparison.OrdinalIgnoreCase))
                .Where(target => impression.Visibility != "Personal" || target.AllowsPersonalNews)
                .OrderBy(_ => this.random.Next())
                .Take(this.GetDailyRetellingTargetLimit(reaction, impression))
                .ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            int depth = System.Math.Min(8, impression.TransmissionDepth + 1);
            int distortion = System.Math.Min(
                100,
                impression.DistortionLevel + this.GetRetellingDistortionGain(reaction, impression)
            );
            string retoldSummary = this.BuildRetoldCommunitySummary(subject, impression, depth, distortion);
            int stored = 0;
            foreach (var target in targets)
            {
                NPC? recipient = Game1.getCharacterFromName(target.NpcName);
                if (recipient == null)
                {
                    continue;
                }

                if (this.memory.RecordCommunityImpression(
                        recipient,
                        subject,
                        impression.Kind,
                        retoldSummary,
                        source: "CloseCircle",
                        visibility: impression.Visibility,
                        transmissionDepth: depth,
                        distortionLevel: distortion,
                        heardFromNpcName: speaker.Name,
                        circleKey: target.CircleKey,
                        importance: System.Math.Max(24, impression.Importance - 8),
                        maxEntriesPerNpc: this.config.MaxMemoryEntriesPerNpc))
                {
                    stored++;
                }
            }

            if (stored > 0)
            {
                this.memory.MarkCommunityImpressionShared(impression);
                if (this.config.Debug)
                {
                    this.monitor.Log(
                        $"Propagated community impression from {speaker.Name} through {string.Join(", ", targets.Select(target => target.CircleKey).Distinct())}: depth {depth}, distortion {distortion}, recipients {stored}.",
                        LogLevel.Debug
                    );
                }
            }
        }
    }

    private bool CanRetellCommunityImpression(CommunityImpressionFact impression)
    {
        if (impression.Visibility == "Private")
        {
            return false;
        }

        if (impression.FreshnessStage == "fading"
            || impression.FreshnessStage == "expired"
            || impression.Confidence < 35
            || impression.TransmissionDepth >= 3)
        {
            return false;
        }

        return impression.LastSharedTotalDays < Game1.Date.TotalDays;
    }

    private int GetDailyRetellingTargetLimit(CommunityReactionCue reaction, CommunityImpressionFact impression)
    {
        if (impression.Visibility == "Personal")
        {
            return 1;
        }

        return reaction.SharePropensity switch
        {
            >= 60 => 2,
            _ => 1
        };
    }

    private int GetRetellingDistortionGain(CommunityReactionCue reaction, CommunityImpressionFact impression)
    {
        int baseGain = impression.Source switch
        {
            "Witnessed" => 8,
            "CloseCircle" => 12,
            _ => 16
        };

        return reaction.Key switch
        {
            "Expressive" => baseGain + 6,
            "Curious" => baseGain + 4,
            "Reserved" => System.Math.Max(4, baseGain - 4),
            "Measured" => System.Math.Max(4, baseGain - 3),
            _ => baseGain
        };
    }

    private string BuildRetoldCommunitySummary(
        NPC subject,
        CommunityImpressionFact impression,
        int depth,
        int distortion)
    {
        if (depth <= 1 && distortion < 20)
        {
            return impression.Summary;
        }

        return impression.Kind switch
        {
            "relationship_trend" when depth >= 3 || distortion >= 35 =>
                $"people have noticed the farmer and {subject.displayName} talking more lately",
            "relationship_trend" =>
                $"the farmer seems to have been spending more time with {subject.displayName} lately",
            "helped" when depth >= 3 || distortion >= 35 =>
                $"someone said the farmer did {subject.displayName} a favor recently",
            "helped" =>
                $"the farmer may have helped {subject.displayName} with something recently",
            "shared_experience" when depth >= 3 || distortion >= 35 =>
                $"the farmer and {subject.displayName} seem to have had some sort of plan together recently",
            "shared_experience" =>
                $"the farmer and {subject.displayName} seem to have spent some time together recently",
            _ =>
                $"there has been a little talk lately involving the farmer and {subject.displayName}"
        };
    }

    private bool IsRomanticallyAttachedToFarmer(NPC npc)
    {
        return Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship friendship)
            && (friendship.IsDating() || friendship.IsEngaged() || friendship.IsMarried());
    }

    private void MarkCommitmentFollowUpsMentionedAfterPrompt(NPC npc)
    {
        if (!this.config.EnableCommitments)
        {
            return;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return;
        }

        foreach (var commitment in state.Commitments.Where(commitment =>
                     commitment.Status == "Expired"
                     && commitment.LastMentionedTotalDays < Game1.Date.TotalDays))
        {
            commitment.LastMentionedTotalDays = Game1.Date.TotalDays;
            commitment.LastMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var commitment in state.Commitments.Where(commitment =>
                     commitment.Status == "Pending"
                     && commitment.DueTotalDays == Game1.Date.TotalDays + 1
                     && commitment.DayBeforeReminderMentionedTotalDays < Game1.Date.TotalDays))
        {
            commitment.DayBeforeReminderMentionedTotalDays = Game1.Date.TotalDays;
            commitment.DayBeforeReminderMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var commitment in state.Commitments.Where(commitment =>
                     commitment.Status == "Fulfilled"
                     && commitment.FulfilledTotalDays >= Game1.Date.TotalDays - 3
                     && commitment.FollowUpMentionedTotalDays < 0))
        {
            commitment.FollowUpMentionedTotalDays = Game1.Date.TotalDays;
            commitment.FollowUpMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var conflict in state.Conflicts.Where(conflict =>
                     conflict.Status == "Resolved"
                     && conflict.ResolvedTotalDays >= Game1.Date.TotalDays - 3
                     && conflict.RecoveryMentionedTotalDays < 0))
        {
            conflict.RecoveryMentionedTotalDays = Game1.Date.TotalDays;
            conflict.RecoveryMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var request in state.HelpRequests.Where(request =>
                     request.Status == "Expired"
                     && request.LastMentionedTotalDays < Game1.Date.TotalDays))
        {
            request.LastMentionedTotalDays = Game1.Date.TotalDays;
            request.LastMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var request in state.HelpRequests.Where(request =>
                     request.Status == "Fulfilled"
                     && request.FulfilledTotalDays >= Game1.Date.TotalDays - 3
                     && request.LastMentionedTotalDays < 0))
        {
            request.LastMentionedTotalDays = Game1.Date.TotalDays;
            request.LastMentionedTimeOfDay = Game1.timeOfDay;
        }
    }

    private void PushInteractionContext(NPC npc, string debugMessage)
    {
        string promptContext = this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests
        );
        bool pushedToValleyTalk = this.valleyTalkBridge.PushBehaviorContext(npc, promptContext);

        if (this.config.Debug)
        {
            this.monitor.Log(
                pushedToValleyTalk
                    ? $"{debugMessage} Primed ValleyTalk context:\n{promptContext}"
                    : $"{debugMessage} ValleyTalk context was not pushed.",
                LogLevel.Debug
            );
        }
    }

    private GiftMemoryDetails BuildGiftMemoryDetails(NPC npc, SObject gift)
    {
        int taste = this.TryGetGiftTaste(npc, gift);
        var labels = this.DescribeGiftTaste(taste);
        string itemName = string.IsNullOrWhiteSpace(gift.DisplayName) ? gift.Name : gift.DisplayName;
        return new GiftMemoryDetails(gift.QualifiedItemId, itemName, labels.DebugLabel, labels.PromptLabel, taste);
    }

    private int TryGetGiftTaste(NPC npc, SObject gift)
    {
        try
        {
            var method = typeof(NPC).GetMethod("getGiftTasteForThisItem", [typeof(SObject)]);
            object? result = method?.Invoke(npc, [gift]);
            return result is int taste ? taste : -1;
        }
        catch
        {
            return -1;
        }
    }

    private GiftTasteLabels DescribeGiftTaste(int taste)
    {
        return taste switch
        {
            0 => new GiftTasteLabels("最爱", "loved gift"),
            2 => new GiftTasteLabels("喜欢", "liked gift"),
            4 => new GiftTasteLabels("不喜欢", "disliked gift"),
            6 => new GiftTasteLabels("讨厌", "hated gift"),
            8 => new GiftTasteLabels("普通", "neutral gift"),
            _ => new GiftTasteLabels("未知喜好", "unknown gift taste")
        };
    }

    private string DescribeCurrentEventContext()
    {
        string location = Game1.currentLocation?.DisplayName ?? Game1.currentLocation?.Name ?? "当前地点";
        return $"event or festival moment at {location} on {Game1.season} {Game1.dayOfMonth}, {Game1.timeOfDay}";
    }

    private bool TryFindNpcForInteraction(ICursorPosition cursor, out NPC? npc)
    {
        npc = null;
        if (Game1.currentLocation == null || Game1.player == null)
        {
            return false;
        }

        var facingTile = this.GetPlayerFacingTile();
        var targetTiles = new[]
        {
            cursor.GrabTile,
            new Vector2(facingTile.X, facingTile.Y)
        };

        const float maxConversationDistanceToPlayer = 2.5f;
        npc = Game1.currentLocation.characters
            .Where(candidate =>
                candidate.currentLocation == Game1.currentLocation
                && !string.IsNullOrWhiteSpace(candidate.Name)
                && !candidate.IsInvisible
                && !candidate.isSleeping.Value
            )
            .Select(candidate => new
            {
                Npc = candidate,
                DistanceToTarget = targetTiles.Min(tile => Vector2.Distance(candidate.Tile, tile)),
                DistanceToPlayer = Vector2.Distance(candidate.Tile, Game1.player.Tile)
            })
            .Where(pair => pair.DistanceToTarget <= 1.5f && pair.DistanceToPlayer <= maxConversationDistanceToPlayer)
            .OrderBy(pair => pair.DistanceToTarget)
            .ThenBy(pair => pair.DistanceToPlayer)
            .Select(pair => pair.Npc)
            .FirstOrDefault();

        return npc != null;
    }

    private bool TryExecute(NPC npc, BehaviorIntent intent, string source)
    {
        if (!this.CanExecute(npc, intent, out string reason))
        {
            if (source == "hotkey")
            {
                this.ShowFeedback($"LivingNPCs：{npc.displayName} 未执行 {this.DescribeIntent(intent.Type)}：{this.TranslateSkipReason(reason)}");
            }

            if (this.config.Debug)
            {
                this.monitor.Log($"Skipped {intent.Type} for {npc.Name}: {reason}", LogLevel.Debug);
            }

            return false;
        }

        bool executed = intent.Type switch
        {
            BehaviorIntentType.FacePlayer => this.TryFacePlayer(npc),
            BehaviorIntentType.Emote => this.TryEmote(npc, intent.EmoteId),
            BehaviorIntentType.ApproachPlayer => this.TryApproachPlayer(npc),
            BehaviorIntentType.Pause => this.TryPause(npc),
            BehaviorIntentType.LookAround => this.TryLookAround(npc),
            BehaviorIntentType.StepAway => this.TryStepAway(npc),
            _ => false
        };

        if (!executed)
        {
            if (source == "hotkey")
            {
                this.ShowFeedback($"LivingNPCs：{npc.displayName} 未能执行 {this.DescribeIntent(intent.Type)}。");
            }

            return false;
        }

        this.memory.Record(npc, intent, this.config.MaxMemoryEntriesPerNpc);
        if (this.config.EnableNpcState)
        {
            this.memory.UpdateStateForBehavior(npc, intent, source);
        }

        string promptContext = this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests
        );
        bool pushedToValleyTalk = this.valleyTalkBridge.PushBehaviorContext(npc, promptContext);

        if (this.config.Debug)
        {
            this.monitor.Log($"Executed {intent.Type} for {npc.Name} from {source}.", LogLevel.Debug);
            this.monitor.Log(
                pushedToValleyTalk
                    ? $"Pushed behavior context to ValleyTalk for {npc.Name}:\n{promptContext}"
                    : $"ValleyTalk context was not pushed for {npc.Name}. ValleyTalk may be missing or bridge disabled.",
                LogLevel.Debug
            );
        }

        if (source == "hotkey")
        {
            string bridge = pushedToValleyTalk ? "已推送 ValleyTalk 上下文" : "未推送 ValleyTalk 上下文";
            this.ShowFeedback($"LivingNPCs：{npc.displayName} 已执行 {this.DescribeIntent(intent.Type)}，{bridge}。");
        }

        return true;
    }

    private bool CanExecute(NPC npc, BehaviorIntent intent, out string reason)
    {
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

    private bool TryFacePlayer(NPC npc)
    {
        if (!this.config.AllowFacePlayer)
        {
            return false;
        }

        npc.faceDirection(this.GetDirectionTowardPlayer(npc));
        return true;
    }

    private bool TryEmote(NPC npc, int emoteId)
    {
        if (!this.config.AllowEmotes)
        {
            return false;
        }

        this.TryFacePlayer(npc);
        npc.doEmote(emoteId);
        return true;
    }

    private bool TryPause(NPC npc)
    {
        if (!this.config.AllowFacePlayer)
        {
            return false;
        }

        npc.controller = null;
        npc.Halt();
        this.TryFacePlayer(npc);
        return true;
    }

    private bool TryLookAround(NPC npc)
    {
        if (!this.config.AllowFacePlayer)
        {
            return false;
        }

        npc.faceDirection(this.random.Next(4));
        return true;
    }

    private bool TryApproachPlayer(NPC npc)
    {
        if (!this.config.AllowApproachPlayer || Game1.currentLocation == null)
        {
            return false;
        }

        if (!this.TryFindApproachTile(npc, out Point targetTile))
        {
            this.TryFacePlayer(npc);
            return false;
        }

        npc.controller = new PathFindController(
            npc,
            Game1.currentLocation,
            targetTile,
            this.GetDirectionTowardPlayerFromTile(targetTile)
        );

        return npc.controller != null;
    }

    private bool TryStepAway(NPC npc)
    {
        if (!this.config.AllowApproachPlayer || Game1.currentLocation == null)
        {
            return false;
        }

        if (!this.TryFindStepAwayTile(npc, out Point targetTile))
        {
            this.TryFacePlayer(npc);
            return false;
        }

        npc.controller = new PathFindController(
            npc,
            Game1.currentLocation,
            targetTile,
            this.GetDirectionTowardPlayerFromTile(targetTile)
        );

        return npc.controller != null;
    }

    private int GetDirectionTowardPlayer(NPC npc)
    {
        Vector2 delta = Game1.player.Tile - npc.Tile;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            return delta.X > 0 ? 1 : 3;
        }

        return delta.Y > 0 ? 2 : 0;
    }

    private int GetDirectionTowardPlayerFromTile(Point tile)
    {
        Vector2 delta = Game1.player.Tile - new Vector2(tile.X, tile.Y);
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            return delta.X > 0 ? 1 : 3;
        }

        return delta.Y > 0 ? 2 : 0;
    }

    private Point GetPlayerFacingTile()
    {
        var tile = Game1.player.TilePoint;
        return Game1.player.FacingDirection switch
        {
            0 => new Point(tile.X, tile.Y - 1),
            1 => new Point(tile.X + 1, tile.Y),
            2 => new Point(tile.X, tile.Y + 1),
            3 => new Point(tile.X - 1, tile.Y),
            _ => tile
        };
    }

    private bool TryFindApproachTile(NPC npc, out Point targetTile)
    {
        targetTile = Point.Zero;
        var playerTile = Game1.player.TilePoint;
        var candidates = new[]
        {
            new Point(playerTile.X, playerTile.Y + 1),
            new Point(playerTile.X + 1, playerTile.Y),
            new Point(playerTile.X - 1, playerTile.Y),
            new Point(playerTile.X, playerTile.Y - 1)
        };

        var location = Game1.currentLocation;
        if (location == null)
        {
            return false;
        }

        foreach (var candidate in candidates.OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), npc.Tile)))
        {
            if (this.IsSafeDestinationTile(location, candidate))
            {
                targetTile = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryFindStepAwayTile(NPC npc, out Point targetTile)
    {
        targetTile = Point.Zero;
        var location = Game1.currentLocation;
        if (location == null)
        {
            return false;
        }

        var npcTile = npc.TilePoint;
        Vector2 away = npc.Tile - Game1.player.Tile;
        int awayX = Math.Abs(away.X) >= Math.Abs(away.Y) ? Math.Sign(away.X) : 0;
        int awayY = Math.Abs(away.Y) > Math.Abs(away.X) ? Math.Sign(away.Y) : 0;
        if (awayX == 0 && awayY == 0)
        {
            awayY = 1;
        }

        var candidates = new[]
        {
            new Point(npcTile.X + awayX, npcTile.Y + awayY),
            new Point(npcTile.X + awayY, npcTile.Y + awayX),
            new Point(npcTile.X - awayY, npcTile.Y - awayX),
            new Point(npcTile.X + awayX + awayY, npcTile.Y + awayY + awayX),
            new Point(npcTile.X + awayX - awayY, npcTile.Y + awayY - awayX)
        };

        foreach (var candidate in candidates
            .Distinct()
            .OrderByDescending(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), Game1.player.Tile))
            .ThenBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), npc.Tile)))
        {
            if (this.IsSafeDestinationTile(location, candidate))
            {
                targetTile = candidate;
                return true;
            }
        }

        return false;
    }

    private bool IsSafeDestinationTile(GameLocation location, Point tile, NPC? ignoredNpc = null)
    {
        if (tile.X < 0 || tile.Y < 0 || tile.X >= location.Map.Layers[0].LayerWidth || tile.Y >= location.Map.Layers[0].LayerHeight)
        {
            return false;
        }

        var tileVector = new Vector2(tile.X, tile.Y);
        if (!location.isTileLocationOpen(tileVector))
        {
            return false;
        }

        return !location.characters.Any(npc => npc != ignoredNpc && npc.TilePoint == tile);
    }

    private void ShowNearestNpcMemory()
    {
        if (!this.TryFindNearestNpcIgnoringDailyBudget(out NPC? npc) || npc == null)
        {
            this.ShowFeedback("LivingNPCs：附近没有可查看记忆的 NPC。");
            return;
        }

        string summary = this.memory.BuildDebugSummary(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState);
        this.monitor.Log(summary, LogLevel.Info);
        this.ShowFeedback($"LivingNPCs：已在 SMAPI 控制台输出 {npc.displayName} 的状态和记忆。");
    }

    private void OnDebugCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("LivingNPCs：请先载入存档后再使用调试命令。", LogLevel.Info);
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        this.monitor.Log(this.memory.BuildDebugSummary(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState), LogLevel.Info);
    }

    private void OnPromptCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("LivingNPCs：请先载入存档后再使用 prompt 调试命令。", LogLevel.Info);
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        string promptContext = this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests
        );
        this.monitor.Log($"LivingNPCs：{npc.displayName} 的 ValleyTalk 上下文预览：\n{promptContext}", LogLevel.Info);
    }

    private void OnExportCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("LivingNPCs：请先载入存档后再导出调试报告。", LogLevel.Info);
            return;
        }

        string target = JoinCommandArgs(args);
        if (string.IsNullOrWhiteSpace(target))
        {
            target = "near";
        }

        string directory = this.GetDebugReportDirectory();
        Directory.CreateDirectory(directory);

        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            string indexPath = Path.Combine(directory, "index.md");
            File.WriteAllText(indexPath, BehaviorDiagnostics.BuildTrackedStateIndex(this.memory.GetTrackedStates()));

            foreach (var state in this.memory.GetTrackedStates())
            {
                string statePath = Path.Combine(directory, $"{SanitizeFileName(state.NpcName)}.state.md");
                File.WriteAllText(statePath, BehaviorDiagnostics.BuildStateOnlyMarkdownReport(state));
            }

            foreach (var currentNpc in Game1.currentLocation?.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)) ?? Enumerable.Empty<NPC>())
            {
                string npcPath = Path.Combine(directory, $"{SanitizeFileName(currentNpc.Name)}.current.md");
                File.WriteAllText(npcPath, BehaviorDiagnostics.BuildNpcMarkdownReport(currentNpc, this.memory, this.config));
            }

            this.monitor.Log($"LivingNPCs：已导出全局调试报告到 {directory}", LogLevel.Info);
            this.ShowFeedback("LivingNPCs：已导出全局调试报告。");
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        string filePath = Path.Combine(directory, $"{SanitizeFileName(npc.Name)}.md");
        File.WriteAllText(filePath, BehaviorDiagnostics.BuildNpcMarkdownReport(npc, this.memory, this.config));
        this.monitor.Log($"LivingNPCs：已导出 {npc.displayName} 的调试报告到 {filePath}", LogLevel.Info);
        this.ShowFeedback($"LivingNPCs：已导出 {npc.displayName} 的调试报告。");
    }

    private void OnEvalCommand(string command, string[] args)
    {
        this.monitor.Log(BehaviorDiagnostics.RunRuntimeEvaluationSuite(), LogLevel.Info);
    }

    private bool TryResolveNpcArgument(string[] args, out NPC? npc, out string error)
    {
        npc = null;
        string query = JoinCommandArgs(args);
        if (string.IsNullOrWhiteSpace(query) || query.Equals("near", StringComparison.OrdinalIgnoreCase))
        {
            if (this.TryFindNearestNpcIgnoringDailyBudget(out npc) && npc != null)
            {
                error = string.Empty;
                return true;
            }

            error = "LivingNPCs：附近没有可调试的 NPC。";
            return false;
        }

        npc = Game1.currentLocation?.characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.displayName, query, StringComparison.OrdinalIgnoreCase));
        if (npc != null)
        {
            error = string.Empty;
            return true;
        }

        error = $"LivingNPCs：当前地点没有找到 NPC“{query}”。可以靠近 NPC 后用 near，或使用当前地图上的 NPC 名字。";
        return false;
    }

    private string GetDebugReportDirectory()
    {
        string saveFolder = string.IsNullOrWhiteSpace(Constants.SaveFolderName)
            ? "NoSave"
            : Constants.SaveFolderName;
        return Path.Combine(this.helper.DirectoryPath, "debug_reports", SanitizeFileName(saveFolder));
    }

    private static string JoinCommandArgs(string[] args)
    {
        return string.Join(" ", args ?? Array.Empty<string>()).Trim();
    }

    private static string SanitizeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return safe;
    }

    private void SyncHelpRequestsToQuestLog()
    {
        if (!Context.IsWorldReady || Game1.player?.questLog == null)
        {
            return;
        }

        var pendingRequests = this.memory.GetTrackedStates()
            .SelectMany(state => state.HelpRequests
                .Where(request => request.Status == "Pending")
                .Select(request => new
                {
                    State = state,
                    Request = request
                }))
            .ToList();
        var pendingQuestIds = pendingRequests
            .Select(pair => pair.Request.QuestLogId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var proxyQuests = Game1.player.questLog
            .OfType<Quest>()
            .Where(quest => quest.modData.TryGetValue(HelpRequestQuestMarkerKey, out string? marker)
                && marker == "true")
            .ToList();

        foreach (var quest in proxyQuests)
        {
            if (!quest.modData.TryGetValue(HelpRequestQuestIdKey, out string? questId)
                || string.IsNullOrWhiteSpace(questId)
                || !pendingQuestIds.Contains(questId))
            {
                Game1.player.questLog.Remove(quest);
            }
        }

        foreach (var pair in pendingRequests)
        {
            Quest? quest = Game1.player.questLog
                .OfType<Quest>()
                .FirstOrDefault(candidate =>
                    candidate.modData.TryGetValue(HelpRequestQuestIdKey, out string? questId)
                    && questId == pair.Request.QuestLogId);
            if (quest == null)
            {
                quest = new Quest();
                quest.accept();
                quest.modData[HelpRequestQuestMarkerKey] = "true";
                quest.modData[HelpRequestQuestIdKey] = pair.Request.QuestLogId;
                Game1.player.questLog.Add(quest);
            }

            this.UpdateHelpRequestQuestText(quest, pair.State, pair.Request);
        }
    }

    private void UpdateHelpRequestQuestText(Quest quest, LivingNpcState state, NpcHelpRequestFact request)
    {
        string npcDisplayName = string.IsNullOrWhiteSpace(request.NpcDisplayName)
            ? state.NpcName
            : request.NpcDisplayName;
        string due = this.BuildHelpRequestDueText(request);
        string stepText = this.BuildHelpRequestStepProgressText(request);
        quest.questTitle = $"求助：{npcDisplayName}";
        quest.questDescription = request.Type == "item_request"
            ? $"{npcDisplayName} 请你帮忙找一件东西。{stepText}{this.BuildHelpRequestDetailText(request)}\n{due}"
            : $"{npcDisplayName} 想就一件事请教你。{stepText}{this.BuildHelpRequestDetailText(request)}\n{due}";
        quest.currentObjective = request.Type == "item_request"
            ? $"{stepText}把 {this.GetHelpRequestItemLabel(request)} 交给 {npcDisplayName}。{due}"
            : $"{stepText}和 {npcDisplayName} 继续聊聊：{this.GetHelpRequestQuestionLabel(request)}。{due}";
    }

    private string BuildHelpRequestStepProgressText(NpcHelpRequestFact request)
    {
        int totalSteps = System.Math.Max(1, request.Steps.Count);
        if (totalSteps <= 1)
        {
            return string.Empty;
        }

        int currentStep = System.Math.Clamp(request.CurrentStepIndex + 1, 1, totalSteps);
        return $"第 {currentStep}/{totalSteps} 步：";
    }

    private string BuildHelpRequestDetailText(NpcHelpRequestFact request)
    {
        return request.Type == "item_request"
            ? $"需要：{this.GetHelpRequestItemLabel(request)}。"
            : $"问题：{this.GetHelpRequestQuestionLabel(request)}。";
    }

    private string GetHelpRequestItemLabel(NpcHelpRequestFact request)
    {
        return string.IsNullOrWhiteSpace(request.RequestedItemLabel)
            ? request.Summary
            : request.RequestedItemLabel;
    }

    private string GetHelpRequestQuestionLabel(NpcHelpRequestFact request)
    {
        return string.IsNullOrWhiteSpace(request.QuestionTopic)
            ? request.Summary
            : request.QuestionTopic;
    }

    private string BuildHelpRequestDueText(NpcHelpRequestFact request)
    {
        int daysRemaining = request.DueTotalDays - Game1.Date.TotalDays;
        return daysRemaining switch
        {
            < 0 => $"已逾期 {-daysRemaining} 天。",
            0 => "今天到期。",
            1 => "明天到期。",
            _ => $"还剩 {daysRemaining} 天。"
        };
    }

    private bool TryFindNearestNpcIgnoringDailyBudget(out NPC? nearest)
    {
        nearest = null;
        if (Game1.currentLocation == null || Game1.player == null)
        {
            return false;
        }

        float maxDistance = this.config.MaxInteractionDistanceTiles;
        nearest = Game1.currentLocation.characters
            .Where(npc => npc.currentLocation == Game1.currentLocation && !string.IsNullOrWhiteSpace(npc.Name))
            .Select(npc => new
            {
                Npc = npc,
                Distance = Vector2.Distance(npc.Tile, Game1.player.Tile)
            })
            .Where(pair => pair.Distance <= maxDistance)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Npc)
            .FirstOrDefault();

        return nearest != null;
    }

    private void ShowFeedback(string message)
    {
        if (!this.config.ShowHudMessages)
        {
            return;
        }

        Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message));
    }

    private string DescribeIntent(BehaviorIntentType intentType)
    {
        return intentType switch
        {
            BehaviorIntentType.FacePlayer => "转向玩家",
            BehaviorIntentType.Emote => "显示表情",
            BehaviorIntentType.ApproachPlayer => "走近玩家",
            BehaviorIntentType.Pause => "停下看向玩家",
            BehaviorIntentType.LookAround => "环顾四周",
            BehaviorIntentType.StepAway => "后退一步",
            _ => intentType.ToString()
        };
    }

    private string TranslateSkipReason(string reason)
    {
        return reason switch
        {
            "an event is active" => "当前正在事件中",
            "the NPC is not in the current location" => "NPC 不在当前地图",
            "daily behavior budget reached" => "今天该 NPC 的触发次数已达上限",
            "facing behavior is disabled" => "转向行为已关闭",
            "emote behavior is disabled" => "表情行为已关闭",
            "approach behavior is disabled" => "走近玩家行为已关闭",
            "movement behavior is disabled" => "移动类行为已关闭",
            "small attention behavior is disabled" => "小型注意力行为已关闭",
            _ => reason
        };
    }
}

internal sealed record GiftTasteLabels(string DebugLabel, string PromptLabel);
internal sealed record PendingAmbientRemark(
    string NpcName,
    string Text,
    int TotalDays,
    int NotBeforeTimeOfDay,
    string LocationName,
    Vector2 OriginTile
);

internal sealed record PendingDelayedTravelAction(
    string NpcName,
    int TotalDays,
    string LocationName,
    int NotBeforeTimeOfDay,
    string Type,
    string TargetLocation,
    int DurationMinutes,
    string Reason
);

internal sealed class PendingEscortToLocation
{
    public PendingEscortToLocation(
        string npcName,
        int totalDays,
        string targetLocation,
        string targetLocationLabel,
        int endTimeOfDay,
        string lastLocationName,
        Point lastPlayerTile
    )
    {
        this.NpcName = npcName;
        this.TotalDays = totalDays;
        this.TargetLocation = targetLocation;
        this.TargetLocationLabel = targetLocationLabel;
        this.EndTimeOfDay = endTimeOfDay;
        this.LastLocationName = lastLocationName;
        this.LastPlayerTile = lastPlayerTile;
    }

    public string NpcName { get; }
    public int TotalDays { get; }
    public string TargetLocation { get; }
    public string TargetLocationLabel { get; }
    public int EndTimeOfDay { get; }
    public string LastLocationName { get; set; }
    public Point LastPlayerTile { get; set; }
    public Point LastWaypointTile { get; set; } = Point.Zero;
    public PathFindController? LastAssignedController { get; set; }
    public bool HintShownForLocation { get; set; }
    public bool WaitingInNextLocation { get; set; }
    public string WaitingLocationName { get; set; } = string.Empty;
    public string WaitingSourceLocationName { get; set; } = string.Empty;
}

internal sealed class PendingWalkTogether
{
    public PendingWalkTogether(
        string npcName,
        int totalDays,
        string locationName,
        int endTimeOfDay,
        Point lastPlayerTile,
        PathFindController? lastAssignedController
    )
    {
        this.NpcName = npcName;
        this.TotalDays = totalDays;
        this.LocationName = locationName;
        this.EndTimeOfDay = endTimeOfDay;
        this.LastPlayerTile = lastPlayerTile;
        this.LastAssignedController = lastAssignedController;
    }

    public string NpcName { get; }
    public int TotalDays { get; }
    public string LocationName { get; }
    public int EndTimeOfDay { get; }
    public Point LastPlayerTile { get; set; }
    public PathFindController? LastAssignedController { get; set; }
}
