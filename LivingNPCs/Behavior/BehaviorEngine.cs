using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorEngine
{
    private const string SaveDataKey = "behavior-memory";

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly Random random = new();
    private readonly BehaviorMemory memory = new();
    private readonly ValleyTalkPromptBridge valleyTalkBridge;
    private readonly GiftSelector giftSelector;
    private readonly IBehaviorPlanner planner;
    private readonly AiBehaviorClient aiBehaviorClient;
    private readonly BehaviorDebugCommandHandler debugCommands;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;
    private readonly DialogueBehaviorInfluenceRuntime dialogueBehaviorInfluences;
    private readonly DelayedTravelActionRuntime delayedTravelActions;
    private readonly BehaviorMailService mailService;
    private readonly GiftOpportunityService giftOpportunities;
    private readonly GiftActionRuntime giftActions;
    private readonly DirectWorldActionRuntime directWorldActions;
    private readonly CompanionOutingRuntime companionOutings;
    private readonly HelpRequestRewardService helpRequestRewards;
    private readonly HelpRequestQuestLogService helpRequestQuestLog;
    private readonly HelpRequestRuntime helpRequests;
    private readonly ConversationStartRecorder conversationStartRecorder;
    private readonly List<PendingBehaviorRequest> pendingRequests = new();
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly HashSet<string> loggedHandlerExceptions = new();
    private string pendingGiftMailKey = string.Empty;
    private int pendingGiftMailTrackTicks;
    private string activeGiftMailKey = string.Empty;

    public BehaviorEngine(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.valleyTalkBridge = new ValleyTalkPromptBridge(helper, monitor, config);
        this.giftSelector = new GiftSelector(this.random);
        this.planner = new AiBehaviorPlanner(new RuleBasedBehaviorPlanner(config, this.random, this.memory));
        this.aiBehaviorClient = new AiBehaviorClient(config, monitor);
        this.feedback = new BehaviorFeedbackService(config, monitor);
        this.communityRipples = new CommunityRippleRuntime(config, monitor, this.memory, this.random);
        this.mailService = new BehaviorMailService(helper, this.memory, this.random);
        this.debugCommands = new BehaviorDebugCommandHandler(helper, monitor, config, this.memory, this.mailService, this.feedback.Show);
        this.giftOpportunities = new GiftOpportunityService(
            config,
            this.memory,
            this.giftSelector,
            this.mailService,
            this.random
        );
        this.giftActions = new GiftActionRuntime(
            config,
            monitor,
            this.memory,
            this.giftSelector,
            this.mailService,
            this.feedback,
            this.CanUseWorldAction
        );
        this.directWorldActions = new DirectWorldActionRuntime(
            config,
            this.memory,
            this.feedback,
            this.CanUseWorldAction
        );
        this.companionOutings = new CompanionOutingRuntime(
            config,
            monitor,
            this.memory,
            this.feedback,
            this.communityRipples,
            this.CanUseWorldAction,
            this.IsSafeDestinationTile,
            this.TryResolveCurrentScheduleTarget,
            (npc, allowCrossLocationTeleport) => this.TryReturnNpcToCurrentSchedule(npc, allowCrossLocationTeleport),
            (npc, debugMessage) => this.PushInteractionContext(npc, debugMessage)
        );
        this.helpRequestRewards = new HelpRequestRewardService(
            config,
            monitor,
            this.memory,
            this.giftSelector,
            this.mailService,
            this.feedback,
            this.communityRipples
        );
        this.helpRequestQuestLog = new HelpRequestQuestLogService(config, this.memory, this.mailService);
        this.conversationStartRecorder = new ConversationStartRecorder(
            helper,
            monitor,
            config,
            this.memory,
            this.valleyTalkBridge,
            this.feedback,
            this.communityRipples,
            this.giftOpportunities,
            this.helpRequestRewards,
            this.helpRequestQuestLog,
            this.TryFindNpcForInteraction,
            (npc, debugMessage, immediatePromptContext) => this.PushInteractionContext(npc, debugMessage, immediatePromptContext)
        );
        this.delayedTravelActions = new DelayedTravelActionRuntime(
            config,
            monitor,
            this.memory,
            npcName => this.TryFindNpcInCurrentLocation(npcName, out NPC? npc) ? npc : null,
            this.companionOutings.TryStart
        );
        this.helpRequests = new HelpRequestRuntime(
            config,
            this.memory,
            npcName => this.TryFindNpcInCurrentLocation(npcName, out NPC? npc) ? npc : null,
            (npc, text) => this.feedback.TryShowNpcSpeechBubble(npc, text),
            (npc, debugMessage) => this.PushInteractionContext(npc, debugMessage),
            this.helpRequestQuestLog.Sync
        );
        this.dialogueBehaviorInfluences = new DialogueBehaviorInfluenceRuntime(
            config,
            this.memory,
            this.TryApproachPlayer,
            this.TryStepAway,
            this.TryPause,
            this.TryFacePlayer,
            (npc, text) => this.feedback.TryShowNpcSpeechBubble(npc, text),
            this.companionOutings.HasActiveOuting,
            (npc, debugMessage) => this.PushInteractionContext(npc, debugMessage)
        );
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
        this.helper.Events.Display.MenuChanged += this.OnMenuChanged;
        this.helper.Events.Content.AssetRequested += this.OnAssetRequested;
        this.debugCommands.RegisterConsoleCommands();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.SafeRun("save loaded", () =>
        {
            BehaviorMemorySaveData? saveData;
            try
            {
                saveData = this.helper.Data.ReadSaveData<BehaviorMemorySaveData>(SaveDataKey);
            }
            catch (Exception ex)
            {
                this.monitor.Log(
                    $"Could not read LivingNPCs save data; continuing with fresh memory for this save. ({ex.Message})",
                    LogLevel.Warn
                );
                saveData = null;
            }

            this.memory.Load(saveData, this.config.MaxMemoryEntriesPerNpc);
            this.conversationStartRecorder.Clear();
            this.valleyTalkBridge.TryInitialize();
            this.mailService.QueueDueGiftMailsForTomorrow();
            this.helper.GameContent.InvalidateCache("Data/mail");
            this.helpRequestQuestLog.Sync();

            if (this.config.Debug)
            {
                this.monitor.Log("Loaded LivingNPCs behavior memory from the current save.", LogLevel.Debug);
            }
        });
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.SafeRun("saving", () =>
        {
            this.helper.Data.WriteSaveData(SaveDataKey, this.memory.ToSaveData());
            if (this.config.Debug)
            {
                this.monitor.Log("Saved LivingNPCs behavior memory to the current save.", LogLevel.Debug);
            }
        });
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.SafeRun("day started", () =>
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
            this.ClearGiftMailTracking();
            this.companionOutings.Clear();
            this.conversationStartRecorder.Clear();
            this.feedback.Clear();
            this.delayedTravelActions.Clear();
            this.communityRipples.TryPropagate();
            this.mailService.QueueDueGiftMailsForTomorrow();
            this.helper.GameContent.InvalidateCache("Data/mail");
            this.helpRequestQuestLog.Sync();
        });
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // Use NameWithoutLocale so the edit also applies to localized variants such as
        // "Data/mail.zh-CN"; otherwise gift/reward mail entries are never added for non-English
        // players and their letters open to nothing.
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
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
                    this.monitor.Log($"Data/mail edit applied: +{data.Count - before} LivingNPCs entries (total {data.Count}).", LogLevel.Debug);
                }
            });
        });
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.SafeRun("returned to title", () =>
        {
            this.memory.ResetDaily();
            this.valleyTalkBridge.ClearAll();
            this.pendingRequests.Clear();
            this.ClearGiftMailTracking();
            this.companionOutings.Clear();
            this.conversationStartRecorder.Clear();
            this.feedback.Clear();
            this.delayedTravelActions.Clear();
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
                this.debugCommands.ShowNearestNpcMemory();
                return;
            }

            this.TryRememberGiftMailOpening(e);

            if (!this.config.BehaviorHotkey.JustPressed())
            {
                this.conversationStartRecorder.TryRecord(e);
                return;
            }

            this.helper.Input.Suppress(e.Button);

            if (this.TryFindNearestNpc(out NPC? npc) && npc != null)
            {
                this.QueueOrExecute(npc, BehaviorTrigger.Manual, "hotkey");
            }
            else
            {
                this.feedback.Show(I18n.Get("hud.noNearbyNpc"));
                if (this.config.Debug)
                {
                    this.monitor.Log("No nearby NPC found for LivingNPCs behavior hotkey.", LogLevel.Debug);
                }
            }
        });
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

            if (!this.TryFindNearestNpc(out NPC? npc) || npc == null)
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

        this.SafeRun("update tick: pending behavior requests", this.ProcessPendingBehaviorRequests);
        this.SafeRun("update tick: gift mail tracking", this.TryTrackGiftMailOpening);
        this.SafeRun("update tick: HUD messages", () => this.feedback.TryShowPendingHudMessages());
        this.SafeRun("update tick: ambient remarks", () => this.feedback.TryShowPendingAmbientRemarks());
        this.SafeRun("update tick: delayed travel actions", () => this.delayedTravelActions.TryStartPending());
        this.SafeRun("update tick: companion outings", () => this.companionOutings.TryUpdatePending());
        if (e.IsMultipleOf(120))
        {
            this.SafeRun("update tick: dialogue behavior influences", () => this.dialogueBehaviorInfluences.TryApply());
        }
    }

    private void ProcessPendingBehaviorRequests()
    {
        foreach (var request in this.pendingRequests.Where(request => request.Task.IsCompleted).ToList())
        {
            this.pendingRequests.Remove(request);

            if (request.TotalDays != Game1.Date.TotalDays || !this.TryFindNpcInCurrentLocation(request.NpcName, out NPC? npc) || npc == null)
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
                this.monitor.Log($"LivingNPCs recovered from an error during {context}: {ex}", LogLevel.Error);
            }
            else if (this.config.Debug)
            {
                this.monitor.Log($"LivingNPCs error during {context} recurred: {ex.Message}", LogLevel.Trace);
            }
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
            this.feedback.Show(I18n.Get("hud.planningBehavior", new { npc = npc.displayName }));
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
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests,
            this.config.EnableAiDialogueFriendship ? this.config.MaxAiDialogueFriendshipPerNpcPerDay : 0,
            this.config.MaxDialogueBehaviorInfluenceDays
        );

        if (!result.HasEffect)
        {
            if (this.config.EnableAiWorldActions)
            {
                this.TryExecuteConversationActions(npc, result.Actions, playerText, npcResponse);
            }

            return false;
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
        }

        if (this.config.EnableDialogueFollowUps && !string.IsNullOrWhiteSpace(result.AmbientFollowUpText))
        {
            this.feedback.QueueAmbientRemark(npc, result.AmbientFollowUpText, result.AmbientFollowUpDelayMinutes);
        }

        if (this.config.EnableAiWorldActions)
        {
            this.TryExecuteConversationActions(npc, result.Actions, playerText, npcResponse);
        }

        this.PushInteractionContext(
            npc,
            $"Recorded ValleyTalk exchange for {npc.Name}: {result.LongTermMemoriesStored} long-term memories, {result.PlayerPreferencesStored} player preferences, {result.HelpRequestsStored} help requests, {result.HelpRequestsUpdated} help request updates, {result.ConflictsStored} conflicts, {result.BehaviorInfluencesStored} dialogue behavior influences, {result.ConflictsResolved} resolved conflicts, +{result.AppliedFriendshipDelta} extra friendship."
        );
        return true;
    }

    public string GetGiftResponseContext(string npcName, string npcDisplayName, string giftItemId, string giftName, int taste)
    {
        if (!this.config.EnableConversationMemory || !this.config.EnableNpcState)
        {
            return string.Empty;
        }

        if (!this.TryFindNpcInCurrentLocation(npcName, out NPC? npc) || npc == null)
        {
            return string.Empty;
        }

        if (taste is 4 or 6)
        {
            return string.Empty;
        }

        var labels = GiftMemoryDetailsFactory.DescribeTaste(taste);
        var gift = new GiftMemoryDetails(
            giftItemId ?? string.Empty,
            string.IsNullOrWhiteSpace(giftName) ? "a gift" : giftName,
            labels.DebugLabel,
            labels.PromptLabel,
            taste
        );
        LivingNpcState state = this.memory.GetState(npc) ?? this.memory.UpdateStateForGift(npc, gift);
        bool scheduledReciprocalGift = this.giftOpportunities.TryScheduleReciprocalGiftOpportunity(npc, state, gift);
        bool hasReciprocalMail = scheduledReciprocalGift || this.mailService.HasPendingGiftMail(state, "reciprocal");
        return hasReciprocalMail
            ? BuildGiftResponseMailPrompt(npc, gift.ItemName)
            : string.Empty;
    }

    private static string BuildGiftResponseMailPrompt(NPC npc, string giftName)
    {
        string itemLabel = string.IsNullOrWhiteSpace(giftName)
            ? "the farmer's gift"
            : giftName.Trim();
        return string.Join(
            "\n",
            "## LivingNPCs Reciprocal Gift Mail",
            $"- LivingNPCs has scheduled a later mailbox return gift from {npc.displayName} because the farmer gave them {itemLabel}.",
            "- In this immediate gift reaction, the NPC may briefly imply they will send something later if it sounds natural.",
            "- Do not give the farmer an item now, do not include a hidden give_small_gift or give_meaningful_gift action, and do not promise a specific item."
        );
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

            bool executed = action.Type switch
            {
                "give_small_gift" => this.giftActions.TryGiveSmallGift(npc, action, playerText, npcResponse, out _),
                "give_meaningful_gift" => this.giftActions.TryGiveMeaningfulGift(npc, action, playerText, npcResponse, out _),
                "give_money" => this.giftActions.TryGiveMoney(npc, action, out _),
                "water_nearby_crops" => this.directWorldActions.TryWaterNearbyCrops(npc, action, out _),
                "companion_outing" => this.companionOutings.TryStart(npc, action, out _),
                "festival_interaction" => this.directWorldActions.TryFestivalInteraction(npc, action, out _),
                "assist_quest" => this.directWorldActions.TryAssistQuest(npc, action, out _),
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
                    "companion_outing" => "companion outing request rejected",
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
            ConversationActionCueRules.TryCorrectTravelActionTargetFromVisibleDialogue(npc, actions, playerText, npcResponse);
            return actions;
        }

        if (this.TryBuildFallbackGiftAction(npc, playerText, npcResponse, out ValleyTalkWorldActionRequest? giftAction)
            && giftAction != null)
        {
            if (this.config.Debug)
            {
                this.monitor.Log(
                    $"Synthesized fallback AI gift action {giftAction.Type} from visible dialogue.",
                    LogLevel.Debug
                );
            }

            return new[] { giftAction };
        }

        if (!ConversationActionCueRules.TryBuildFallbackTravelAction(npc, playerText, npcResponse, out ValleyTalkWorldActionRequest? action)
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

    private bool TryBuildFallbackGiftAction(
        NPC npc,
        string playerText,
        string npcResponse,
        out ValleyTalkWorldActionRequest? action
    )
    {
        action = null;
        if (!ConversationActionCueRules.LooksLikeImmediateGiftOffer(npcResponse)
            || ConversationActionCueRules.LooksLikeGiftOfferRejection(npcResponse))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return false;
        }

        bool meaningfulCue = ConversationActionCueRules.LooksLikeMeaningfulGiftOffer(playerText, npcResponse);
        string type = meaningfulCue && GiftActionRules.IsEligibleForMeaningfulGift(npc, out _)
            ? "give_meaningful_gift"
            : "give_small_gift";
        if (type == "give_small_gift" && !GiftActionRules.IsEligibleForSmallGift(npc, state))
        {
            return false;
        }

        action = new ValleyTalkWorldActionRequest
        {
            Type = type,
            Reason = "the visible dialogue naturally offered the farmer a gift"
        };

        GiftTier tier = type == "give_meaningful_gift"
            ? GiftTier.Meaningful
            : GiftTier.Small;
        if (this.giftSelector.TryChooseMentioned(npc, npcResponse, tier, out GiftSelection? mentioned)
            && mentioned != null)
        {
            action.ItemId = mentioned.ItemId;
            action.ItemLabel = mentioned.DebugName;
            action.Reason = this.BuildWorldActionReason(
                action.Reason,
                $"visible dialogue named {mentioned.DebugName}"
            );
        }

        return true;
    }

    private bool TryReturnNpcToCurrentSchedule(NPC npc, bool allowCrossLocationTeleport = true)
    {
        return this.TryResumeNpcCurrentSchedule(npc, "LivingNPCs companion outing", allowCrossLocationTeleport);
    }

    private bool TryResumeNpcCurrentSchedule(NPC npc, string context, bool allowCrossLocationTeleport = true)
    {
        if (!this.TryResolveCurrentScheduleTarget(npc, out GameLocation? location, out Point targetTile, out int facingDirection)
            || location == null)
        {
            return false;
        }

        try
        {
            npc.controller = null;
            npc.Halt();

            bool sameLocation = string.Equals(npc.currentLocation?.Name, location.Name, StringComparison.OrdinalIgnoreCase);
            if (sameLocation)
            {
                if (!location.characters.Contains(npc))
                {
                    npc.currentLocation?.characters.Remove(npc);
                    location.characters.Add(npc);
                    npc.currentLocation = location;
                }

                if (Vector2.Distance(npc.Tile, new Vector2(targetTile.X, targetTile.Y)) > 0.75f)
                {
                    npc.controller = new PathFindController(
                        npc,
                        location,
                        targetTile,
                        facingDirection is >= 0 and <= 3 ? facingDirection : 2
                    );
                }
                else if (facingDirection is >= 0 and <= 3)
                {
                    npc.faceDirection(facingDirection);
                }

                if (this.config.Debug)
                {
                    this.monitor.Log($"Resumed {npc.Name}'s schedule from current position after {context}.", LogLevel.Debug);
                }

                return true;
            }

            if (!allowCrossLocationTeleport)
            {
                if (this.config.Debug)
                {
                    this.monitor.Log(
                        $"Left {npc.Name} at the current escort location after {context}; current schedule target is in {location.Name}.",
                        LogLevel.Debug
                    );
                }

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

            if (this.config.Debug)
            {
                this.monitor.Log($"Returned {npc.Name} to schedule location after {context}.", LogLevel.Debug);
            }

            return true;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Could not resume {npc.Name}'s schedule after {context}: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private bool TryResolveCurrentScheduleTarget(NPC npc, out GameLocation? location, out Point targetTile, out int facingDirection)
    {
        location = null;
        targetTile = Point.Zero;
        facingDirection = -1;
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
            || !ScheduleReflectionReader.TryReadScheduleDestination(scheduleEntry, out string locationName, out Point scheduledTile, out int scheduledFacingDirection))
        {
            return false;
        }

        targetTile = scheduledTile;
        facingDirection = scheduledFacingDirection;

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

    private void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private bool TryFindOpenTileNear(GameLocation location, Point center, NPC ignoredNpc, out Point targetTile)
    {
        return BehaviorActionExecutor.TryFindOpenTileNear(location, center, ignoredNpc, out targetTile);
    }

    private void PushInteractionContext(NPC npc, string debugMessage, string immediatePromptContext = "")
    {
        string promptContext = this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests
        );
        string giftOpportunityContext = this.BuildGiftOpportunityPromptContext(npc);
        if (!string.IsNullOrWhiteSpace(giftOpportunityContext))
        {
            promptContext = $"{promptContext}\n{giftOpportunityContext}";
        }

        string helpRequestOpportunityContext = this.BuildHelpRequestOpportunityPromptContext(npc);
        if (!string.IsNullOrWhiteSpace(helpRequestOpportunityContext))
        {
            promptContext = $"{promptContext}\n{helpRequestOpportunityContext}";
        }

        string companionOutingContext = this.companionOutings.BuildPromptContext(npc);
        if (!string.IsNullOrWhiteSpace(companionOutingContext))
        {
            promptContext = $"{promptContext}\n{companionOutingContext}";
        }

        if (!string.IsNullOrWhiteSpace(immediatePromptContext))
        {
            promptContext = $"{promptContext}\n{immediatePromptContext}";
        }

        bool pushedToValleyTalk = this.valleyTalkBridge.PushBehaviorContext(npc, promptContext);

        if (this.config.Debug)
        {
            this.monitor.Log(
                pushedToValleyTalk ? debugMessage : $"{debugMessage} ValleyTalk context was not pushed.",
                LogLevel.Debug
            );
            if (pushedToValleyTalk)
            {
                this.monitor.Log($"Primed ValleyTalk context for {npc.Name}:\n{promptContext}", LogLevel.Trace);
            }
        }
    }

    private string BuildGiftOpportunityPromptContext(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null
            || !this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || state.HighestUnresolvedConflictSeverity >= 30
            || GiftActionRules.HasAiGiftToday(state))
        {
            return string.Empty;
        }

        string cue = string.Empty;
        if (state.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays)
        {
            cue = string.IsNullOrWhiteSpace(state.DailyGiftOpportunityReason)
                ? "The relationship is warm enough that the NPC may offer a small everyday gift today."
                : state.DailyGiftOpportunityReason;
        }

        if (string.IsNullOrWhiteSpace(cue))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            "## LivingNPCs Gift Opportunity",
            $"- Gift cue: {cue}.",
            "- If it fits the visible reply, have the NPC naturally offer a small in-game gift now and include exactly one hidden action with type give_small_gift.",
            $"- Shared small gift IDs: {this.giftSelector.BuildCommonPromptList(GiftTier.Small)}.",
            $"- {npc.displayName}'s personalized small gift IDs: {this.giftSelector.BuildPersonalizedPromptList(npc, GiftTier.Small)}.",
            "- If naming a specific gift, use an itemId from the two lists above and the matching itemLabel. Generic wording such as 'a small thing' is fine when no specific item is named.",
            "- If the moment feels emotionally wrong, crowded, or abrupt, skip the gift rather than forcing it."
        );
    }

    private string BuildHelpRequestOpportunityPromptContext(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null
            || !this.config.EnableHelpRequests
            || state.DailyHelpRequestOpportunityTotalDays != Game1.Date.TotalDays
            || state.HighestUnresolvedConflictSeverity >= 30
            || state.HelpRequests.Any(request => request.Status is "Offered" or "Pending"))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            "## LivingNPCs Help Request Opportunity",
            $"- Today {npc.displayName} is inclined to ask the farmer for one small favor during this conversation.",
            "- If the visible reply allows, naturally bring up needing one concrete item from the help-request fit list, and include exactly one hidden helpRequests entry (item_request) with that itemId — do not leave the favor only in the spoken text.",
            "- Keep it brief and in character; if the moment genuinely does not fit, it is fine to wait for another day rather than forcing it."
        );
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

        Point mailbox = Game1.player.getMailboxPosition();
        Point facingTile = this.GetPlayerFacingTile();
        Point grabTile = new((int)cursor.GrabTile.X, (int)cursor.GrabTile.Y);
        return facingTile == mailbox || grabTile == mailbox;
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
                this.feedback.Show(I18n.Get("hud.behaviorSkipped", new { npc = npc.displayName, behavior = this.DescribeIntent(intent.Type), reason = this.TranslateSkipReason(reason) }));
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
                this.feedback.Show(I18n.Get("hud.behaviorFailed", new { npc = npc.displayName, behavior = this.DescribeIntent(intent.Type) }));
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
            if (pushedToValleyTalk)
            {
                this.monitor.Log($"Pushed behavior context to ValleyTalk for {npc.Name}:\n{promptContext}", LogLevel.Trace);
            }
            else
            {
                this.monitor.Log($"ValleyTalk context was not pushed for {npc.Name}. ValleyTalk may be missing or bridge disabled.", LogLevel.Debug);
            }
        }

        if (source == "hotkey")
        {
            string bridge = I18n.Get(pushedToValleyTalk ? "bridge.pushed" : "bridge.notPushed");
            this.feedback.Show(I18n.Get("hud.behaviorDone", new { npc = npc.displayName, behavior = this.DescribeIntent(intent.Type), bridge }));
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
        return BehaviorActionExecutor.TryFacePlayer(npc, this.config.AllowFacePlayer);
    }

    private bool TryEmote(NPC npc, int emoteId)
    {
        return BehaviorActionExecutor.TryEmote(npc, emoteId, this.config.AllowEmotes, this.config.AllowFacePlayer);
    }

    private bool TryPause(NPC npc)
    {
        return BehaviorActionExecutor.TryPause(npc, this.config.AllowFacePlayer);
    }

    private bool TryLookAround(NPC npc)
    {
        return BehaviorActionExecutor.TryLookAround(npc, this.config.AllowFacePlayer, this.random);
    }

    private bool TryApproachPlayer(NPC npc)
    {
        return BehaviorActionExecutor.TryApproachPlayer(npc, this.config.AllowApproachPlayer, this.config.AllowFacePlayer);
    }

    private bool TryStepAway(NPC npc)
    {
        return BehaviorActionExecutor.TryStepAway(npc, this.config.AllowApproachPlayer, this.config.AllowFacePlayer);
    }

    private int GetDirectionTowardPlayer(NPC npc)
    {
        return BehaviorActionExecutor.GetDirectionTowardPlayer(npc);
    }

    private int GetDirectionTowardPlayerFromTile(Point tile)
    {
        return BehaviorActionExecutor.GetDirectionTowardPlayerFromTile(tile);
    }

    private Point GetPlayerFacingTile()
    {
        return BehaviorActionExecutor.GetPlayerFacingTile();
    }

    private bool TryFindApproachTile(NPC npc, out Point targetTile)
    {
        return BehaviorActionExecutor.TryFindApproachTile(npc, out targetTile);
    }

    private bool TryFindStepAwayTile(NPC npc, out Point targetTile)
    {
        return BehaviorActionExecutor.TryFindStepAwayTile(npc, out targetTile);
    }

    private bool IsSafeDestinationTile(GameLocation location, Point tile, NPC? ignoredNpc = null)
    {
        return BehaviorActionExecutor.IsSafeDestinationTile(location, tile, ignoredNpc);
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
