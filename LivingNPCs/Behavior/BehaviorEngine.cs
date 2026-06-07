using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorEngine
{
    private const string SaveDataKey = "behavior-memory";
    private const int SmallGiftMinFriendshipHearts = 2;
    private const int SmallGiftMinFamiliarity = 15;
    private const int MeaningfulGiftMinFriendshipHearts = 5;
    private const int MeaningfulGiftNoCooldownFriendshipHearts = 8;
    private const int PendingReciprocalGiftExpirationDays = 3;

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
    private readonly DialogueBehaviorInfluenceRuntime dialogueBehaviorInfluences;
    private readonly BehaviorMailService mailService;
    private readonly HelpRequestQuestLogService helpRequestQuestLog;
    private readonly List<PendingBehaviorRequest> pendingRequests = new();
    private readonly List<PendingAmbientRemark> pendingAmbientRemarks = new();
    private readonly List<PendingWalkTogether> pendingWalks = new();
    private readonly List<PendingEscortToLocation> pendingEscorts = new();
    private readonly List<PendingDelayedTravelAction> pendingDelayedTravelActions = new();
    private readonly Queue<string> pendingHudMessages = new();
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
        this.debugCommands = new BehaviorDebugCommandHandler(helper, monitor, config, this.memory, this.ShowFeedback);
        this.mailService = new BehaviorMailService(helper, this.memory, this.random);
        this.helpRequestQuestLog = new HelpRequestQuestLogService(config, this.memory, this.mailService);
        this.dialogueBehaviorInfluences = new DialogueBehaviorInfluenceRuntime(
            config,
            this.memory,
            this.TryStartWalkTogether,
            this.TryApproachPlayer,
            this.TryStepAway,
            this.TryPause,
            this.TryFacePlayer,
            (npc, text) => this.TryShowNpcSpeechBubble(npc, text),
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
        this.pendingAmbientRemarks.Clear();
        this.pendingWalks.Clear();
        this.pendingEscorts.Clear();
        this.pendingDelayedTravelActions.Clear();
        this.pendingHudMessages.Clear();
        this.lastConversationMemoryTimeByNpc.Clear();
        this.nextSpeechBubbleTimeByNpc.Clear();
        this.TryPropagateCommunityImpressions();
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
        this.pendingAmbientRemarks.Clear();
        this.pendingWalks.Clear();
        this.pendingEscorts.Clear();
        this.pendingDelayedTravelActions.Clear();
        this.pendingHudMessages.Clear();
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

        this.TryShowPendingHudMessages();
        this.TryShowPendingAmbientRemarks();
        this.TryStartPendingDelayedTravelActions();
        this.TryUpdatePendingEscorts();
        this.TryUpdatePendingWalks();
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
            this.RewardFulfilledHelpRequests(npc, result.FulfilledHelpRequests);
        }

        if (result.HelpRequestsStored > 0 || result.HelpRequestsUpdated > 0)
        {
            this.helpRequestQuestLog.Sync();
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

    private bool TryShowNpcSpeechBubble(NPC npc, string text, int? cooldownMilliseconds = null)
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

        int durationMilliseconds = GetSpeechBubbleDurationMilliseconds(text);
        npc.showTextAboveHead(text, null, 2, durationMilliseconds, 0);
        this.nextSpeechBubbleTimeByNpc[npc.Name] = now + Math.Max(durationMilliseconds, cooldownMilliseconds ?? durationMilliseconds);
        return true;
    }

    private static int GetSpeechBubbleDurationMilliseconds(string text)
    {
        int visibleLength = (text ?? string.Empty).Trim().Length;
        return System.Math.Clamp(4000 + System.Math.Max(0, visibleLength - 8) * 140, 4000, 10000);
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

            if (!this.TryShowNpcSpeechBubble(npc, remark.Text))
            {
                continue;
            }

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

        if (!this.TryBuildFallbackTravelAction(npc, playerText, npcResponse, out ValleyTalkWorldActionRequest? action)
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
        if (!this.LooksLikeImmediateGiftOffer(npcResponse)
            || this.LooksLikeGiftOfferRejection(npcResponse))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return false;
        }

        bool meaningfulCue = this.LooksLikeMeaningfulGiftOffer(playerText, npcResponse);
        string type = meaningfulCue && this.IsEligibleForMeaningfulGift(npc, state, out _)
            ? "give_meaningful_gift"
            : "give_small_gift";
        if (type == "give_small_gift" && !this.IsEligibleForSmallGift(npc, state))
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

    private bool LooksLikeImmediateGiftOffer(string npcResponse)
    {
        return this.ContainsAny(
            npcResponse,
            "给你",
            "送你",
            "拿着",
            "收下",
            "带给你",
            "留给你",
            "一点心意",
            "小礼物",
            "回礼",
            "谢礼",
            "这个给",
            "这个是给",
            "this is for you",
            "this one's for you",
            "take this",
            "take it",
            "i brought you",
            "i saved this for you",
            "small gift",
            "return the favor",
            "to thank you"
        );
    }

    private bool LooksLikeGiftOfferRejection(string npcResponse)
    {
        return this.ContainsAny(
            npcResponse,
            "不能给你",
            "没法给你",
            "下次再给",
            "以后再给",
            "改天再给",
            "not today",
            "next time",
            "another day",
            "can't give"
        );
    }

    private bool LooksLikeMeaningfulGiftOffer(string playerText, string npcResponse)
    {
        string combined = $"{playerText} {npcResponse}";
        return this.ContainsAny(
            combined,
            "有意义",
            "特别",
            "重要",
            "珍藏",
            "用心",
            "记得你喜欢",
            "special",
            "meaningful",
            "important",
            "saved this",
            "remembered you like"
        );
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

        string currentTarget = BehaviorMemory.NormalizeTravelLocation(action.TargetLocation, string.Empty);
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
                this.TryShowNpcSpeechBubble(npc, "好了，我们走吧。");
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
        if (!this.CanUseWorldAction(npc, "small_gift", requireFriendly: false, out reason, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiSmallGifts
            || state == null
            || this.HasAiGiftToday(state))
        {
            reason = "small gifts are disabled or another AI gift was already used today";
            return false;
        }

        if (!this.IsEligibleForSmallGift(npc, state))
        {
            reason = $"small gifts require at least {SmallGiftMinFriendshipHearts} hearts or familiarity {SmallGiftMinFamiliarity}";
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
        string motive = this.DetermineGiftMotive(action, selection, GiftTier.Small);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            string giftReason = this.BuildWorldActionReason(
                action.Reason,
                this.BuildGiftSelectionReason(
                    $"they tried to give the farmer {gift.DisplayName} after an AI conversation, but the farmer's inventory was full",
                    selection
                )
            );
            if (!this.mailService.ScheduleGiftMail(npc, state, selection, "inventory_full", giftReason, dueInDays: 1))
            {
                reason = "player inventory is full";
                return false;
            }

            state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
            this.ClearGiftOpportunities(state);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledSmallGiftMail",
                giftReason,
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, "they mailed the farmer a small gift after the farmer's inventory was full");
            this.ShowFeedbackAfterDialogue($"LivingNPCs：你的背包满了，{npc.displayName} 会把 {gift.DisplayName} 明天寄给你。");
            return true;
        }

        state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        this.ClearGiftOpportunities(state);
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

        this.ShowFeedbackAfterDialogue(this.BuildGiftHudMessage(npc, gift.DisplayName, motive));
        return true;
    }

    private void RewardFulfilledHelpRequests(
        NPC npc,
        IReadOnlyList<NpcHelpRequestFact> requests,
        bool queueAmbientThanks = true
    )
    {
        foreach (var request in requests.Where(request => request.Status == "Fulfilled"))
        {
            this.RewardFulfilledHelpRequest(npc, request, queueAmbientThanks);
        }
    }

    private void RewardFulfilledHelpRequest(NPC npc, NpcHelpRequestFact request, bool queueAmbientThanks)
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
            if (queueAmbientThanks)
            {
                this.QueueAmbientRemark(npc, "谢谢你，真的帮上忙了。", 0);
            }

            this.ShowFeedback($"LivingNPCs：完成 {npc.displayName} 的求助，额外好感 +{friendshipReward}。");
            this.SpreadCommunityRipple(
                npc,
                "helped",
                $"the farmer helped {npc.displayName} with a personal request",
                importance: 78
            );
        }

        if (!request.RewardMoneyGranted)
        {
            this.GrantOrScheduleHelpRequestMoneyReward(npc, request);
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

        if (this.HasAiGiftToday(state))
        {
            return false;
        }

        GiftSelection selection = this.giftSelector.Choose(npc, state, request.Summary, request.Resolution);
        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            string giftReason = this.BuildGiftSelectionReason(
                $"they tried to give the farmer {gift.DisplayName} after a fulfilled personal help request, but the farmer's inventory was full",
                selection
            );
            if (!this.mailService.ScheduleGiftMail(npc, state, selection, "inventory_full", giftReason, dueInDays: 1))
            {
                return false;
            }

            request.RewardGiftGiven = true;
            state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
            this.ClearGiftOpportunities(state);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledHelpRequestRewardGiftMail",
                giftReason,
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, "they mailed the farmer a help request reward gift after the farmer's inventory was full");
            this.ShowFeedbackAfterDialogue($"LivingNPCs：你的背包满了，{npc.displayName} 会把 {gift.DisplayName} 明天寄给你作为谢礼。");
            return true;
        }

        request.RewardGiftGiven = true;
        state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        this.ClearGiftOpportunities(state);
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

        this.ShowFeedbackAfterDialogue(this.BuildGiftHudMessage(npc, gift.DisplayName, "thanks"));
        return true;
    }

    private void GrantOrScheduleHelpRequestMoneyReward(NPC npc, NpcHelpRequestFact request)
    {
        if (Game1.player == null)
        {
            return;
        }

        int amount = System.Math.Clamp(request.RewardMoney <= 0 ? 200 : request.RewardMoney, 200, 10000);
        request.RewardMoney = amount;
        if (this.mailService.ShouldSendHelpRequestMoneyByMail(request))
        {
            this.mailService.ScheduleHelpRequestMoneyRewardMail(request);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledHelpRequestMoneyReward",
                $"the help request system scheduled a {amount}g mail reward for tomorrow: {request.Summary}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.ShowFeedbackAfterDialogue($"LivingNPCs：系统将在明天通过信件发放 {amount}g 求助奖励。");
            return;
        }

        Game1.player.Money += amount;
        request.RewardMoneyByMail = false;
        request.RewardMoneyMailKey = string.Empty;
        request.RewardMoneyMailTotalDays = -1;
        request.RewardMoneyGranted = true;
        this.memory.RecordNpcWorldAction(
            npc,
            "GrantedHelpRequestMoneyReward",
            $"the help request system granted a {amount}g reward: {request.Summary}",
            this.config.MaxMemoryEntriesPerNpc
        );
        this.ShowFeedbackAfterDialogue($"LivingNPCs：系统发放求助奖励 {amount}g。");
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
        this.ShowFeedbackAfterDialogue($"LivingNPCs：{npc.displayName} 给了你 {amount}g。");
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
        if (!this.CanUseWorldAction(npc, "meaningful_gift", requireFriendly: true, out reason, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiMeaningfulGifts || state == null)
        {
            reason = "meaningful gifts are disabled";
            return false;
        }

        if (this.HasAiGiftToday(state))
        {
            reason = "another AI gift was already used today";
            return false;
        }

        if (!this.IsEligibleForMeaningfulGift(npc, state, out reason))
        {
            return false;
        }

        int daysSinceLastMeaningfulGift = state.LastAiMeaningfulGiftTotalDays < 0
            ? int.MaxValue
            : Game1.Date.TotalDays - state.LastAiMeaningfulGiftTotalDays;
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        bool bypassCooldown = friendshipHearts >= MeaningfulGiftNoCooldownFriendshipHearts;
        if (!bypassCooldown && daysSinceLastMeaningfulGift < this.config.AiMeaningfulGiftCooldownDays)
        {
            reason = "meaningful gift cooldown is active";
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
        string motive = this.DetermineGiftMotive(action, selection, GiftTier.Meaningful);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            string giftReason = this.BuildWorldActionReason(
                action.Reason,
                this.BuildGiftSelectionReason(
                    $"they tried to give the farmer a meaningful {gift.DisplayName}, but the farmer's inventory was full",
                    selection
                )
            );
            if (!this.mailService.ScheduleGiftMail(npc, state, selection, "inventory_full", giftReason, dueInDays: 1))
            {
                reason = "player inventory is full";
                return false;
            }

            state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
            this.ClearGiftOpportunities(state);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledMeaningfulGiftMail",
                giftReason,
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, "they mailed the farmer a meaningful gift after the farmer's inventory was full");
            this.ShowFeedbackAfterDialogue($"LivingNPCs：你的背包满了，{npc.displayName} 会把 {gift.DisplayName} 明天寄给你。");
            return true;
        }

        state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        this.ClearGiftOpportunities(state);
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

        this.ShowFeedbackAfterDialogue(this.BuildGiftHudMessage(npc, gift.DisplayName, motive));
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

        int maxWalkMinutes = System.Math.Max(8, this.config.MaxAiWalkTogetherMinutes);
        int durationMinutes = System.Math.Clamp(
            action.DurationMinutes <= 0 ? 12 : action.DurationMinutes,
            8,
            maxWalkMinutes
        );
        this.StopTravelActionsForNpc(npc, returnToSchedule: true);
        bool originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        bool originalFollowSchedule = npc.followSchedule;
        NpcTravelRuntime.SuppressSchedule(npc);
        this.pendingWalks.Add(new PendingWalkTogether(
            npc.Name,
            Game1.Date.TotalDays,
            npc.currentLocation?.Name ?? string.Empty,
            this.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
            Game1.player.TilePoint,
            null,
            originalIgnoreScheduleToday,
            originalFollowSchedule
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
        this.ClearPendingAmbientRemarksForNpc(npc.Name);
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
            this.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
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

        this.TryShowNpcSpeechBubble(npc, this.BuildEscortStartGreeting(targetLocation));
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
                        this.TryShowNpcSpeechBubble(npc, this.BuildEscortCaughtUpGreeting(escort));
                    }
                }
            }

            if (npc.currentLocation != Game1.currentLocation)
            {
                this.StopEscortToLocation(escort, npc, returnToSchedule: false);
                this.ShowFeedback($"LivingNPCs：{npc.displayName} 没跟上，护送中断了。");
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
                    escort.HintShownForLocation = this.TryShowNpcSpeechBubble(npc, "你离得有点远了，先跟上我。");
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
                        escort.HintShownForLocation = this.TryShowNpcSpeechBubble(npc, this.BuildEscortDirectionHint(nextLocation));
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
                    escort.HintShownForLocation = this.TryShowNpcSpeechBubble(npc, this.BuildEscortExitWaitHint(nextLocation));
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
            BehaviorMemory.NormalizeTravelLocation(GetWarpTargetName(warp), string.Empty),
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
            BehaviorMemory.NormalizeTravelLocation(GetWarpTargetName(warp), string.Empty),
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
                BehaviorMemory.NormalizeTravelLocation(GetWarpTargetName(warp), string.Empty),
                targetLocation,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private bool TryFindWarpTileToward(GameLocation location, string targetLocation, NPC npc, out Point targetTile)
    {
        foreach (var warp in location.warps
            .Where(warp => string.Equals(
                BehaviorMemory.NormalizeTravelLocation(GetWarpTargetName(warp), string.Empty),
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
        this.TryShowNpcSpeechBubble(npc, this.BuildEscortArrivalGreeting(escort));
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
        RestoreNpcScheduleControl(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
        this.TryReturnNpcToCurrentSchedule(npc, allowCrossLocationTeleport: false);
    }

    private void ClearPendingAmbientRemarksForNpc(string npcName)
    {
        this.pendingAmbientRemarks.RemoveAll(remark => string.Equals(remark.NpcName, npcName, StringComparison.OrdinalIgnoreCase));
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
        foreach (var walk in this.pendingWalks.Where(walk => walk.NpcName == npc.Name).ToList())
        {
            this.StopWalkTogether(walk, npc, returnToSchedule);
        }

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
            || !TryReadScheduleDestination(scheduleEntry, out string locationName, out Point scheduledTile, out int scheduledFacingDirection))
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

    private bool HasAiGiftToday(LivingNpcState state)
    {
        return state.LastAiSmallGiftTotalDays == Game1.Date.TotalDays
            || state.LastAiMeaningfulGiftTotalDays == Game1.Date.TotalDays;
    }

    private bool IsEligibleForSmallGift(NPC npc, LivingNpcState state)
    {
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        return friendshipHearts >= SmallGiftMinFriendshipHearts
            || state.Familiarity >= SmallGiftMinFamiliarity;
    }

    private bool IsEligibleForMeaningfulGift(NPC npc, LivingNpcState state, out string reason)
    {
        reason = string.Empty;
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        if (friendshipHearts < MeaningfulGiftMinFriendshipHearts)
        {
            reason = $"meaningful gifts require at least {MeaningfulGiftMinFriendshipHearts} hearts";
            return false;
        }

        return true;
    }

    private void ClearGiftOpportunities(LivingNpcState state)
    {
        state.DailyGiftOpportunityTotalDays = -1;
        state.DailyGiftOpportunityChancePercent = 0;
        state.DailyGiftOpportunityReason = string.Empty;
        state.PendingReciprocalGiftDueTotalDays = -1;
        state.PendingReciprocalGiftSourceGiftName = string.Empty;
        state.PendingReciprocalGiftReason = string.Empty;
    }

    private string DetermineGiftMotive(ValleyTalkWorldActionRequest action, GiftSelection selection, GiftTier tier, string fallback = "daily")
    {
        string reason = action.Reason ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selection.MatchedPlayerPreference))
        {
            return "preference";
        }

        if (this.ContainsAny(reason, "recently gave", "return gift", "reciprocal", "回礼"))
        {
            return "reciprocal";
        }

        if (this.ContainsAny(reason, "thank", "thanks", "谢礼", "感谢", "help request"))
        {
            return "thanks";
        }

        if (tier == GiftTier.Meaningful)
        {
            return "meaningful";
        }

        return fallback;
    }

    private string BuildGiftHudMessage(NPC npc, string itemLabel, string motive)
    {
        return motive switch
        {
            "preference" => $"LivingNPCs：{npc.displayName} 记得你的喜好，送给你了 {itemLabel}。",
            "reciprocal" => $"LivingNPCs：{npc.displayName} 回送给你了 {itemLabel}。",
            "thanks" => $"LivingNPCs：{npc.displayName} 送给你了 {itemLabel} 作为谢礼。",
            "meaningful" => $"LivingNPCs：{npc.displayName} 送给你了一份用心的礼物：{itemLabel}。",
            _ => $"LivingNPCs：{npc.displayName} 送给你了 {itemLabel}。"
        };
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
                this.StopWalkTogether(walk, npc, returnToSchedule: true);
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
                this.StopWalkTogether(walk, npc, returnToSchedule: true);
                continue;
            }

            if (Game1.activeClickableMenu != null)
            {
                continue;
            }

            NpcTravelRuntime.SuppressSchedule(npc);

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 4)
            {
                this.StopWalkTogether(walk, npc, returnToSchedule: true);
                continue;
            }

            if (npc.controller != null && walk.LastAssignedController != null && npc.controller != walk.LastAssignedController)
            {
                this.StopWalkTogether(walk, npc, returnToSchedule: true);
                continue;
            }

            if (npc.controller != null && walk.LastAssignedController == null)
            {
                this.StopWalkTogether(walk, npc, returnToSchedule: true);
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
            this.helpRequestQuestLog.Sync();
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
                && candidate.SpecialFollowUpPlanned
                && candidate.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && candidate.FollowUpShownTotalDays < 0
                && candidate.FulfilledTotalDays >= Game1.Date.TotalDays - 7
            );
            if (request == null)
            {
                continue;
            }

            if (!this.TryShowNpcSpeechBubble(npc, this.BuildHelpRequestFollowUp(request)))
            {
                continue;
            }

            request.FollowUpShownTotalDays = Game1.Date.TotalDays;
            request.FollowUpShownTimeOfDay = Game1.timeOfDay;
        }
    }

    private string BuildHelpRequestFollowUp(NpcHelpRequestFact request)
    {
        if (request.RewardMoneyByMail)
        {
            return request.Type == "question_request"
                ? "上次那件事我后来又想了想。信应该也送到了吧？"
                : "上次你带来的东西很有用，信应该也送到了吧？";
        }

        if (request.RewardMoney >= 1000)
        {
            return request.Type == "question_request"
                ? "上次那件事真的帮我理清了不少。"
                : "上次你带来的东西，真的解了我的急。";
        }

        return request.Type switch
        {
            "question_request" => "上次你说的那些，我后来还想了想。",
            _ => "上次你带来的东西，正好派上了用场。"
        };
    }

    private void StopWalkTogether(PendingWalkTogether walk, NPC? npc, bool returnToSchedule)
    {
        if (npc != null && npc.controller == walk.LastAssignedController)
        {
            npc.controller = null;
            npc.Halt();
        }

        if (npc != null)
        {
            RestoreNpcScheduleControl(npc, walk.OriginalIgnoreScheduleToday, walk.OriginalFollowSchedule);
            if (returnToSchedule)
            {
                this.TryReturnNpcToCurrentSchedule(npc);
            }
        }

        this.pendingWalks.Remove(walk);
    }

    private void TryPrepareDailyGiftOpportunity(NPC npc, LivingNpcState state)
    {
        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || this.HasAiGiftToday(state)
            || state.HighestUnresolvedConflictSeverity >= 30
            || state.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        if (WorldContext.For(npc).FriendshipHearts < MeaningfulGiftMinFriendshipHearts)
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
            || !this.IsEligibleForMeaningfulGift(npc, state, out _))
        {
            return false;
        }

        int daysSinceLastMeaningfulGift = state.LastAiMeaningfulGiftTotalDays < 0
            ? int.MaxValue
            : Game1.Date.TotalDays - state.LastAiMeaningfulGiftTotalDays;
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        bool bypassCooldown = friendshipHearts >= MeaningfulGiftNoCooldownFriendshipHearts;
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
            || !this.IsEligibleForSmallGift(npc, state)
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
        if (delayDays == 0 && this.HasAiGiftToday(state))
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
            string mailReason = this.BuildGiftSelectionReason(
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

                this.ClearGiftOpportunities(state);
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
                    this.RewardFulfilledHelpRequests(npc, changedHelpRequests);
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
            this.TrySpreadConversationSocialRipple(npc, state);
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

        this.RewardFulfilledHelpRequests(npc, changedHelpRequests, queueAmbientThanks: false);
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
            this.QueueAmbientRemark(
                npc,
                fulfilledCount > 0 ? "谢谢你，真的帮上忙了。" : "谢谢你，我先收下这个。",
                0
            );
        }

        if (fulfilledCount == 0)
        {
            this.ShowFeedback($"LivingNPCs：已把 {gift.ItemName} 交给 {npc.displayName}。");
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
        return CommunityImpressionStore.NormalizeKind(kind) switch
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
                .FirstOrDefault(candidate => CommunityPropagationRules.CanRetell(candidate, Game1.Date.TotalDays));
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
                .Take(CommunityPropagationRules.GetDailyRetellingTargetLimit(reaction, impression))
                .ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            var retelling = CommunityPropagationRules.BuildRetelling(impression, reaction, subject.displayName);
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
                        retelling.Summary,
                        source: retelling.Source,
                        visibility: retelling.Visibility,
                        transmissionDepth: retelling.TransmissionDepth,
                        distortionLevel: retelling.DistortionLevel,
                        heardFromNpcName: speaker.Name,
                        circleKey: target.CircleKey,
                        importance: retelling.Importance,
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
                        $"Propagated community impression from {speaker.Name} through {string.Join(", ", targets.Select(target => target.CircleKey).Distinct())}: depth {retelling.TransmissionDepth}, distortion {retelling.DistortionLevel}, recipients {stored}.",
                        LogLevel.Debug
                    );
                }
            }
        }
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
            || this.HasAiGiftToday(state))
        {
            return string.Empty;
        }

        int today = Game1.Date.TotalDays;
        if (state.PendingReciprocalGiftDueTotalDays >= 0
            && state.PendingReciprocalGiftDueTotalDays + PendingReciprocalGiftExpirationDays < today)
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

        if (!location.isTilePassable(tileVector))
        {
            return false;
        }

        return !location.characters.Any(npc => npc != ignoredNpc && npc.TilePoint == tile);
    }

    private void ShowFeedback(string message)
    {
        if (!this.config.ShowHudMessages)
        {
            return;
        }

        Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message));
    }

    private void ShowFeedbackAfterDialogue(string message)
    {
        if (!this.config.ShowHudMessages || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (Game1.activeClickableMenu == null)
        {
            this.ShowFeedback(message);
            return;
        }

        this.pendingHudMessages.Enqueue(message);
    }

    private void TryShowPendingHudMessages()
    {
        if (this.pendingHudMessages.Count == 0 || Game1.activeClickableMenu != null)
        {
            return;
        }

        this.ShowFeedback(this.pendingHudMessages.Dequeue());
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
