using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Pathfinding;
using SObject = StardewValley.Object;

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
    private readonly GiftActionRuntime giftActions;
    private readonly DirectWorldActionRuntime directWorldActions;
    private readonly WalkTogetherRuntime walkTogether;
    private readonly HelpRequestRewardService helpRequestRewards;
    private readonly HelpRequestQuestLogService helpRequestQuestLog;
    private readonly HelpRequestRuntime helpRequests;
    private readonly List<PendingBehaviorRequest> pendingRequests = new();
    private readonly List<PendingEscortToLocation> pendingEscorts = new();
    private readonly Dictionary<string, int> lastConversationMemoryTimeByNpc = new();
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
        this.feedback = new BehaviorFeedbackService(config, monitor);
        this.communityRipples = new CommunityRippleRuntime(config, monitor, this.memory, this.random);
        this.debugCommands = new BehaviorDebugCommandHandler(helper, monitor, config, this.memory, this.feedback.Show);
        this.mailService = new BehaviorMailService(helper, this.memory, this.random);
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
        this.walkTogether = new WalkTogetherRuntime(
            config,
            this.memory,
            this.feedback,
            this.CanUseWorldAction,
            this.StopTravelActionsForNpc,
            npcName => this.TryFindNpcInCurrentLocation(npcName, out NPC? npc) ? npc : null,
            npc => this.TryFindApproachTile(npc, out Point targetTile) ? targetTile : null,
            this.TryFacePlayer,
            this.GetDirectionTowardPlayerFromTile,
            npc => this.TryReturnNpcToCurrentSchedule(npc)
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
        this.delayedTravelActions = new DelayedTravelActionRuntime(
            config,
            monitor,
            this.memory,
            npcName => this.TryFindNpcInCurrentLocation(npcName, out NPC? npc) ? npc : null,
            this.walkTogether.TryStart,
            this.TryStartEscortToLocation,
            (npc, text) => this.feedback.TryShowNpcSpeechBubble(npc, text)
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
            this.walkTogether.TryStart,
            this.TryApproachPlayer,
            this.TryStepAway,
            this.TryPause,
            this.TryFacePlayer,
            (npc, text) => this.feedback.TryShowNpcSpeechBubble(npc, text),
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
        this.helper.Events.Content.AssetRequested += this.OnAssetRequested;
        this.debugCommands.RegisterConsoleCommands();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        var saveData = this.helper.Data.ReadSaveData<BehaviorMemorySaveData>(SaveDataKey);
        this.memory.Load(saveData, this.config.MaxMemoryEntriesPerNpc);
        this.lastConversationMemoryTimeByNpc.Clear();
        this.valleyTalkBridge.TryInitialize();
        this.mailService.QueueDueGiftMailsForTomorrow();
        this.helper.GameContent.InvalidateCache("Data/mail");
        this.helpRequestQuestLog.Sync();

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
        this.walkTogether.Clear();
        this.pendingEscorts.Clear();
        this.lastConversationMemoryTimeByNpc.Clear();
        this.feedback.Clear();
        this.delayedTravelActions.Clear();
        this.communityRipples.TryPropagate();
        this.mailService.QueueDueGiftMailsForTomorrow();
        this.helper.GameContent.InvalidateCache("Data/mail");
        this.helpRequestQuestLog.Sync();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.Name.IsEquivalentTo("Data/mail"))
        {
            return;
        }

        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, string>().Data;
            this.mailService.ApplyMailData(data);
        });
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.memory.ResetDaily();
        this.valleyTalkBridge.ClearAll();
        this.pendingRequests.Clear();
        this.walkTogether.Clear();
        this.pendingEscorts.Clear();
        this.lastConversationMemoryTimeByNpc.Clear();
        this.feedback.Clear();
        this.delayedTravelActions.Clear();
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
            this.debugCommands.ShowNearestNpcMemory();
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
            this.feedback.Show("LivingNPCs：附近没有可触发的 NPC。");
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

        this.feedback.TryShowPendingHudMessages();
        this.feedback.TryShowPendingAmbientRemarks();
        this.delayedTravelActions.TryStartPending();
        this.TryUpdatePendingEscorts();
        this.walkTogether.TryUpdatePending();
        if (e.IsMultipleOf(120))
        {
            this.dialogueBehaviorInfluences.TryApply();
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
            this.feedback.Show($"LivingNPCs：正在为 {npc.displayName} 规划行为...");
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

        var labels = this.DescribeGiftTaste(taste);
        var gift = new GiftMemoryDetails(
            giftItemId ?? string.Empty,
            string.IsNullOrWhiteSpace(giftName) ? "a gift" : giftName,
            labels.DebugLabel,
            labels.PromptLabel,
            taste
        );
        LivingNpcState state = this.memory.GetState(npc) ?? this.memory.UpdateStateForGift(npc, gift);
        this.TryScheduleReciprocalGiftOpportunity(npc, state, gift);
        return this.BuildGiftOpportunityPromptContext(npc);
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
                action.DelayMinutes = System.Math.Max(action.DelayMinutes, ConversationActionCueRules.DetectPreparationDelayMinutes(npcResponse));
                if (action.DelayMinutes > 0)
                {
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
                "walk_together" => this.walkTogether.TryStart(npc, action, out _),
                "escort_to_location" => this.TryStartEscortToLocation(npc, action, out _),
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
        if (this.giftSelector.TryChooseMentioned(npcResponse, tier, out GiftSelection? mentioned)
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

        string targetLocation = BehaviorMemory.NormalizeTravelLocation(action.TargetLocation, string.Empty);

        if (!this.CanUseWorldAction(npc, "escort_to_location", requireFriendly: false, out reason, allowDistantWhenExplicit: true))
        {
            return false;
        }

        if (IsProtectedEscortScene(npc, out reason))
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

        this.StopTravelActionsForNpc(npc, returnToSchedule: true);
        this.feedback.ClearAmbientRemarksForNpc(npc.Name);
        bool originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        bool originalFollowSchedule = npc.followSchedule;
        NpcTravelRuntime.SuppressSchedule(npc);
        npc.controller = null;
        npc.Halt();
        this.pendingEscorts.Add(new PendingEscortToLocation(
            npc.Name,
            Game1.Date.TotalDays,
            targetLocation,
            targetLabel,
            BehaviorTimeMath.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
            Game1.currentLocation?.Name ?? string.Empty,
            Game1.player.TilePoint,
            originalIgnoreScheduleToday,
            originalFollowSchedule
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

        this.feedback.TryShowNpcSpeechBubble(npc, this.BuildEscortStartGreeting(targetLocation));
        this.feedback.Show($"LivingNPCs：{npc.displayName} 会带你去{targetLabel}。");
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

            NpcTravelRuntime.SuppressSchedule(npc);

            if (escort.WaitingInNextLocation)
            {
                if (npc.currentLocation != Game1.currentLocation)
                {
                    string npcLocation = BehaviorMemory.NormalizeTravelLocation(npc.currentLocation?.Name ?? string.Empty, string.Empty);
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
                        this.feedback.TryShowNpcSpeechBubble(npc, this.BuildEscortCaughtUpGreeting(escort));
                    }
                }
            }

            if (npc.currentLocation != Game1.currentLocation)
            {
                this.StopEscortToLocation(escort, npc, returnToSchedule: false);
                this.feedback.Show($"LivingNPCs：{npc.displayName} 没跟上，护送中断了。");
                continue;
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
                if (!escort.HintShownForLocation)
                {
                    escort.HintShownForLocation = this.feedback.TryShowNpcSpeechBubble(npc, "你离得有点远了，先跟上我。");
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
                        escort.HintShownForLocation = this.feedback.TryShowNpcSpeechBubble(npc, this.BuildEscortDirectionHint(nextLocation));
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
                    escort.HintShownForLocation = this.feedback.TryShowNpcSpeechBubble(npc, this.BuildEscortExitWaitHint(nextLocation));
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

        string sourceName = BehaviorMemory.NormalizeTravelLocation(source.Name, source.Name);
        string destinationName = BehaviorMemory.NormalizeTravelLocation(destination.Name, destination.Name);
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
            NpcTravelRuntime.PlaceInLocation(npc, destination, targetTile, 2);

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
            return BehaviorMemory.NormalizeTravelLocation(locationName, locationName) == "Farm"
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
            BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
            targetLocation,
            StringComparison.OrdinalIgnoreCase)))
        {
            if (ScheduleReflectionReader.TryReadWarpTargetTile(warp, out targetTile))
            {
                return true;
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private bool TryFindReverseWarpEntryTile(GameLocation destination, string sourceLocationName, NPC npc, out Point targetTile)
    {
        foreach (var warp in destination.warps.Where(warp => string.Equals(
            BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
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
        string current = BehaviorMemory.NormalizeTravelLocation(currentLocation.Name, currentLocation.Name);
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
                BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
                targetLocation,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private bool TryFindWarpTileToward(GameLocation location, string targetLocation, NPC npc, out Point targetTile)
    {
        foreach (var warp in location.warps
            .Where(warp => string.Equals(
                BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
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

        string current = BehaviorMemory.NormalizeTravelLocation(location.Name, location.Name);
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
        this.feedback.TryShowNpcSpeechBubble(npc, this.BuildEscortArrivalGreeting(escort));
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

        this.communityRipples.Spread(
            npc,
            "shared_experience",
            $"the farmer went with {npc.displayName} to {escort.TargetLocationLabel}",
            importance: 58
        );
        this.pendingEscorts.Remove(escort);
        this.feedback.Show($"LivingNPCs：{npc.displayName} 已带你到{escort.TargetLocationLabel}。");
        RestoreNpcScheduleControl(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
        this.TryReturnNpcToCurrentSchedule(npc, allowCrossLocationTeleport: false);
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
            RestoreNpcScheduleControl(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
            this.TryReturnNpcToCurrentSchedule(npc);
            var state = this.memory.GetState(npc);
            if (state != null && state.LastAiWalkTogetherTotalDays == Game1.Date.TotalDays)
            {
                state.LastAiWalkTogetherTotalDays = -1;
            }
        }
        else if (npc != null)
        {
            RestoreNpcScheduleControl(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
        }

        this.pendingEscorts.Remove(escort);
    }

    private void StopTravelActionsForNpc(NPC npc, bool returnToSchedule)
    {
        this.walkTogether.StopForNpc(npc, returnToSchedule);

        foreach (var escort in this.pendingEscorts.Where(escort => escort.NpcName == npc.Name).ToList())
        {
            this.StopEscortToLocation(escort, npc, returnToSchedule);
        }
    }

    private static void RestoreNpcScheduleControl(NPC npc, bool ignoreScheduleToday, bool followSchedule)
    {
        NpcTravelRuntime.RestoreSchedule(npc, ignoreScheduleToday, followSchedule);
    }

    private bool TryReturnNpcToCurrentSchedule(NPC npc, bool allowCrossLocationTeleport = true)
    {
        return this.TryResumeNpcCurrentSchedule(npc, "LivingNPCs escort", allowCrossLocationTeleport);
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

    private static bool IsProtectedEscortScene(NPC npc, out string reason)
    {
        if (Game1.eventUp || Game1.currentLocation?.currentEvent != null)
        {
            reason = "escort is blocked during events or festivals";
            return true;
        }

        if (npc.IsInvisible || npc.isSleeping.Value)
        {
            reason = "escort is blocked while the NPC cannot naturally interact";
            return true;
        }

        reason = string.Empty;
        return false;
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

    private void TryPrepareDailyGiftOpportunity(NPC npc, LivingNpcState state)
    {
        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || GiftActionRules.HasAiGiftToday(state)
            || state.HighestUnresolvedConflictSeverity >= 30
            || state.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        if (WorldContext.For(npc).FriendshipHearts < GiftActionRules.MeaningfulGiftMinFriendshipHearts)
        {
            return;
        }

        int minChance = System.Math.Clamp(
            System.Math.Min(this.config.AiDailyGiftChanceMinPercent, this.config.AiDailyGiftChanceMaxPercent),
            0,
            100
        );
        int maxChance = System.Math.Clamp(
            System.Math.Max(this.config.AiDailyGiftChanceMinPercent, this.config.AiDailyGiftChanceMaxPercent),
            0,
            100
        );
        if (state.LastDailyGiftOpportunityRollTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        state.LastDailyGiftOpportunityRollTotalDays = Game1.Date.TotalDays;
        int chance = this.random.Next(minChance, maxChance + 1);
        if (this.random.Next(100) >= chance)
        {
            return;
        }

        state.DailyGiftOpportunityTotalDays = Game1.Date.TotalDays;
        state.DailyGiftOpportunityChancePercent = chance;
        state.DailyGiftOpportunityReason = $"{npc.displayName} is at {WorldContext.For(npc).FriendshipHearts} hearts and may naturally offer a small everyday gift during this conversation";
    }

    private bool ShouldUseMeaningfulReciprocalGift(NPC npc, LivingNpcState state, GiftMemoryDetails gift)
    {
        if (gift.TasteScore != 0
            || !this.config.AllowAiMeaningfulGifts
            || !GiftActionRules.IsEligibleForMeaningfulGift(npc, out _))
        {
            return false;
        }

        int daysSinceLastMeaningfulGift = state.LastAiMeaningfulGiftTotalDays < 0
            ? int.MaxValue
            : Game1.Date.TotalDays - state.LastAiMeaningfulGiftTotalDays;
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        bool bypassCooldown = friendshipHearts >= GiftActionRules.MeaningfulGiftNoCooldownFriendshipHearts;
        if (!bypassCooldown && daysSinceLastMeaningfulGift < this.config.AiMeaningfulGiftCooldownDays)
        {
            return false;
        }

        return this.random.Next(100) < 25;
    }

    private void TryScheduleReciprocalGiftOpportunity(NPC npc, LivingNpcState state, GiftMemoryDetails gift)
    {
        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || !GiftActionRules.IsEligibleForSmallGift(npc, state)
            || state.HighestUnresolvedConflictSeverity >= 30
            || gift.TasteScore is 4 or 6
            || this.mailService.HasPendingGiftMail(state, "reciprocal")
            || state.PendingReciprocalGiftDueTotalDays >= Game1.Date.TotalDays)
        {
            return;
        }

        if (state.PendingReciprocalGiftDueTotalDays >= Game1.Date.TotalDays)
        {
            return;
        }

        int chance = gift.TasteScore switch
        {
            0 => 75,
            2 => 45,
            8 => 10,
            _ => 0
        };
        if (chance <= 0 || this.random.Next(100) >= chance)
        {
            return;
        }

        int delayDays = gift.TasteScore switch
        {
            0 => this.random.Next(0, 3),
            2 => this.random.Next(0, 4),
            _ => this.random.Next(1, 4)
        };
        if (delayDays == 0 && GiftActionRules.HasAiGiftToday(state))
        {
            delayDays = 1;
        }

        if (delayDays > 0)
        {
            GiftTier reciprocalTier = this.ShouldUseMeaningfulReciprocalGift(npc, state, gift)
                ? GiftTier.Meaningful
                : GiftTier.Small;
            GiftSelection selection = reciprocalTier == GiftTier.Meaningful
                ? this.giftSelector.ChooseMeaningful(npc, state, gift.ItemName, gift.TastePromptLabel)
                : this.giftSelector.Choose(npc, state, gift.ItemName, gift.TastePromptLabel);
            string mailReason = GiftActionRules.BuildGiftSelectionReason(
                $"they planned a delayed return gift because the farmer recently gave {npc.displayName} {gift.ItemName}, a {gift.TastePromptLabel}",
                selection
            );
            if (this.mailService.ScheduleGiftMail(npc, state, selection, "reciprocal", mailReason, delayDays, gift.ItemName))
            {
                if (reciprocalTier == GiftTier.Meaningful)
                {
                    state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
                }
                else
                {
                    state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
                }

                GiftActionRules.ClearGiftOpportunities(state);
                this.memory.RecordNpcWorldAction(
                    npc,
                    "ScheduledReciprocalGiftMail",
                    mailReason,
                    this.config.MaxMemoryEntriesPerNpc
                );
            }

            return;
        }

        state.PendingReciprocalGiftDueTotalDays = Game1.Date.TotalDays + delayDays;
        state.PendingReciprocalGiftSourceGiftName = gift.ItemName;
        state.PendingReciprocalGiftReason = $"the farmer recently gave {npc.displayName} {gift.ItemName}, a {gift.TastePromptLabel}; a small return gift would feel reciprocal";
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
            if (this.config.EnableHelpRequests && this.HasPendingItemHelpRequest(npc, gift))
            {
                this.helper.Input.Suppress(e.Button);
                this.DeliverHelpRequestItem(npc, heldGift, gift);
                return;
            }

            this.memory.RecordGiftOffered(npc, gift, this.config.MaxMemoryEntriesPerNpc);
            if (this.config.EnableNpcState)
            {
                LivingNpcState state = this.memory.UpdateStateForGift(npc, gift);
                this.TryScheduleReciprocalGiftOpportunity(npc, state, gift);
            }

            if (this.config.EnableHelpRequests)
            {
                IReadOnlyList<NpcHelpRequestFact> changedHelpRequests = this.memory.TryCompleteItemHelpRequests(npc, gift, this.config.MaxMemoryEntriesPerNpc);
                if (changedHelpRequests.Count > 0)
                {
                    this.helpRequestRewards.RewardFulfilled(npc, changedHelpRequests);
                    this.helpRequestQuestLog.Sync();
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
            this.TryPrepareDailyGiftOpportunity(npc, state);
            this.communityRipples.TrySpreadConversationSocialRipple(npc, state);
        }

        this.PushInteractionContext(npc, $"Recorded conversation start for {npc.Name}.");
        this.MarkConflictFollowUpsMentionedAfterPrompt(npc);
    }

    private bool HasPendingItemHelpRequest(NPC npc, GiftMemoryDetails gift)
    {
        var state = this.memory.GetState(npc);
        return state?.HelpRequests.Any(request =>
            request.Status == "Pending"
            && request.Type == "item_request"
            && string.Equals(request.RequestedItemId, gift.ItemId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private void DeliverHelpRequestItem(NPC npc, SObject heldItem, GiftMemoryDetails gift)
    {
        SObject deliveredItem = ItemRegistry.Create<SObject>(gift.ItemId);
        deliveredItem.Quality = heldItem.Quality;
        Game1.player.reduceActiveItemByOne();

        IReadOnlyList<NpcHelpRequestFact> changedHelpRequests = this.memory.TryCompleteItemHelpRequests(
            npc,
            gift,
            this.config.MaxMemoryEntriesPerNpc
        );
        if (changedHelpRequests.Count == 0)
        {
            if (this.config.Debug)
            {
                this.monitor.Log(
                    $"Suppressed gift interaction for {npc.Name}, but no matching help request was completed for {gift.ItemId}.",
                    LogLevel.Debug
                );
            }

            return;
        }

        this.helpRequestRewards.RewardFulfilled(npc, changedHelpRequests, queueAmbientThanks: false);
        this.helpRequestQuestLog.Sync();

        int fulfilledCount = changedHelpRequests.Count(request => request.Status == "Fulfilled");
        int advancedCount = changedHelpRequests.Count - fulfilledCount;
        this.PushInteractionContext(
            npc,
            $"Delivered {gift.ItemName} for {changedHelpRequests.Count} help request(s): {fulfilledCount} fulfilled, {advancedCount} advanced.",
            this.BuildHelpRequestDeliveryPrompt(npc, gift, changedHelpRequests)
        );

        if (!this.valleyTalkBridge.TryRequestGiftDialogue(npc, deliveredItem, gift.TasteScore))
        {
            this.feedback.QueueAmbientRemark(
                npc,
                fulfilledCount > 0 ? "谢谢你，真的帮上忙了。" : "谢谢你，我先收下这个。",
                0
            );
        }

        if (fulfilledCount == 0)
        {
            this.feedback.Show($"LivingNPCs：已把 {gift.ItemName} 交给 {npc.displayName}。");
        }
    }

    private string BuildHelpRequestDeliveryPrompt(
        NPC npc,
        GiftMemoryDetails gift,
        IReadOnlyList<NpcHelpRequestFact> changedHelpRequests
    )
    {
        var lines = new List<string>
        {
            "## LivingNPCs Immediate Help Request Delivery",
            $"- The farmer just handed {npc.displayName} {gift.ItemName} ({gift.ItemId}) for a LivingNPCs help request.",
            "- This is a task hand-in, not an ordinary daily gift. Acknowledge the requested item even if the farmer has already given a normal gift today.",
            "- Respond now with a natural thank-you or reaction to the completed request/step. Do not mention the game's daily gift limit."
        };

        foreach (var request in changedHelpRequests)
        {
            lines.Add($"- Help request status: {request.Status}; summary: {request.Summary}; resolution: {request.Resolution}");
            if (request.Status == "Fulfilled" && request.RewardGranted)
            {
                lines.Add($"- LivingNPCs already granted the configured friendship reward (+{request.RewardFriendship}).");
            }

            if (request.Status == "Fulfilled" && request.RewardMoneyGranted)
            {
                lines.Add(request.RewardMoneyByMail
                    ? $"- LivingNPCs scheduled a system mail reward of {request.RewardMoney}g for tomorrow."
                    : $"- LivingNPCs already granted a system money reward of {request.RewardMoney}g.");
            }

            if (request.RewardGiftGiven)
            {
                lines.Add("- LivingNPCs also gave the farmer a small item reward; mention it only if it feels natural.");
            }

            if (request.SpecialFollowUpPlanned)
            {
                lines.Add("- A later in-person follow-up may happen; do not promise it as guaranteed.");
            }
        }

        return string.Join("\n", lines);
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

        this.communityRipples.Spread(
            targetNpc,
            "romantic_attention",
            $"the farmer has been giving romantic attention to {targetNpc.displayName}",
            importance: 70
        );
    }

    private bool IsRomanticallyAttachedToFarmer(NPC npc)
    {
        return Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship friendship)
            && (friendship.IsDating() || friendship.IsEngaged() || friendship.IsMarried());
    }

    private void MarkConflictFollowUpsMentionedAfterPrompt(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return;
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

        if (!string.IsNullOrWhiteSpace(immediatePromptContext))
        {
            promptContext = $"{promptContext}\n{immediatePromptContext}";
        }

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

        int today = Game1.Date.TotalDays;
        if (state.PendingReciprocalGiftDueTotalDays >= 0
            && state.PendingReciprocalGiftDueTotalDays + GiftActionRules.PendingReciprocalGiftExpirationDays < today)
        {
            state.PendingReciprocalGiftDueTotalDays = -1;
            state.PendingReciprocalGiftSourceGiftName = string.Empty;
            state.PendingReciprocalGiftReason = string.Empty;
        }

        string cue = string.Empty;
        if (state.PendingReciprocalGiftDueTotalDays >= 0 && state.PendingReciprocalGiftDueTotalDays <= today)
        {
            cue = string.IsNullOrWhiteSpace(state.PendingReciprocalGiftReason)
                ? "The NPC has a natural opportunity to offer a small return gift for a recent gift from the farmer."
                : state.PendingReciprocalGiftReason;
        }
        else if (state.DailyGiftOpportunityTotalDays == today)
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
            "- If naming a specific gift, use an allowed itemId and itemLabel. Generic wording such as 'a small thing' is fine when no specific item is named.",
            "- If the moment feels emotionally wrong, crowded, or abrupt, skip the gift rather than forcing it."
        );
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
                this.feedback.Show($"LivingNPCs：{npc.displayName} 未执行 {this.DescribeIntent(intent.Type)}：{this.TranslateSkipReason(reason)}");
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
                this.feedback.Show($"LivingNPCs：{npc.displayName} 未能执行 {this.DescribeIntent(intent.Type)}。");
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
            this.feedback.Show($"LivingNPCs：{npc.displayName} 已执行 {this.DescribeIntent(intent.Type)}，{bridge}。");
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

        if (!location.isTilePassable(tileVector))
        {
            return false;
        }

        return !location.characters.Any(npc => npc != ignoredNpc && npc.TilePoint == tile);
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
