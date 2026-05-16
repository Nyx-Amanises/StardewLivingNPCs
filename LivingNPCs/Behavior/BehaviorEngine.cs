using System;
using System.Collections.Generic;
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
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        var saveData = this.helper.Data.ReadSaveData<BehaviorMemorySaveData>(SaveDataKey);
        this.memory.Load(saveData, this.config.MaxMemoryEntriesPerNpc);
        this.lastConversationMemoryTimeByNpc.Clear();
        this.valleyTalkBridge.TryInitialize();

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
        this.lastConversationMemoryTimeByNpc.Clear();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.memory.ResetDaily();
        this.valleyTalkBridge.ClearAll();
        this.pendingRequests.Clear();
        this.pendingAmbientRemarks.Clear();
        this.pendingWalks.Clear();
        this.lastConversationMemoryTimeByNpc.Clear();
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
        this.TryUpdatePendingWalks();
        this.TryShowCommitmentMorningReminders();
        this.TryShowSharedExperienceFollowUps();
        this.TryTriggerCommitmentArrivals();
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
            this.config.EnableAiDialogueFriendship ? this.config.MaxAiDialogueFriendshipPerNpcPerDay : 0
        );

        if (!result.HasEffect)
        {
            return false;
        }

        if (result.AppliedFriendshipDelta > 0)
        {
            Game1.player.changeFriendship(result.AppliedFriendshipDelta, npc);
        }

        if (this.config.EnableDialogueFollowUps && !string.IsNullOrWhiteSpace(result.AmbientFollowUpText))
        {
            this.QueueAmbientRemark(npc, result.AmbientFollowUpText, result.AmbientFollowUpDelayMinutes);
        }

        if (this.config.EnableAiWorldActions && result.Actions.Count > 0)
        {
            this.TryExecuteConversationActions(npc, result.Actions, playerText, npcResponse);
        }

        this.PushInteractionContext(
            npc,
            $"Recorded ValleyTalk exchange for {npc.Name}: {result.LongTermMemoriesStored} long-term memories, {result.PlayerPreferencesStored} player preferences, {result.CommitmentsStored} commitments, {result.ConflictsStored} conflicts, {result.ConflictsResolved} resolved conflicts, +{result.AppliedFriendshipDelta} extra friendship."
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
        foreach (var action in actions.Take(1))
        {
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

        GiftSelection selection = this.giftSelector.Choose(npc, state, playerText, npcResponse);
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

        GiftSelection selection = this.giftSelector.ChooseMeaningful(npc, state, playerText, npcResponse);
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
        if (!this.CanUseWorldAction(npc, "walk_together", requireFriendly: false, out reason))
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

        if (!this.TryStartWalkTogether(npc, action, out reason))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state != null)
        {
            this.memory.RecordNpcWorldAction(
                npc,
                "EscortedTowardLocation",
                this.BuildWorldActionReason(action.Reason, $"they agreed to accompany the farmer toward {targetLocation} within the current safe path limits"),
                this.config.MaxMemoryEntriesPerNpc
            );
            this.MarkStateAfterWorldAction(state, "they agreed to accompany the farmer toward a place");
        }

        this.ShowFeedback($"LivingNPCs：{npc.displayName} 会先陪你往约定地点走一段。");
        return true;
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

    private bool CanUseWorldAction(NPC npc, string actionName, bool requireFriendly, out string reason, bool allowDuringEvents = false)
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

        if (requireFriendly ? !atLeastFriendly : !atLeastFamiliar)
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

    private void TryUpdateCommitmentTimers()
    {
        if (!this.config.EnableCommitments)
        {
            return;
        }

        foreach (var state in this.memory.GetTrackedStates())
        {
            foreach (var commitment in state.Commitments.Where(commitment => commitment.Status == "Pending"))
            {
                if (this.IsPastCommitmentGraceWindow(commitment))
                {
                    commitment.Status = "Expired";
                    commitment.LastUpdatedTotalDays = Game1.Date.TotalDays;
                    commitment.LastUpdatedTimeOfDay = Game1.timeOfDay;
                    this.memory.UpdateStateForExpiredCommitment(state, commitment);
                    if (this.TryFindNpcInCurrentLocation(state.NpcName, out NPC? npc) && npc != null)
                    {
                        this.memory.RecordNpcWorldAction(
                            npc,
                            "ExpiredCommitment",
                            $"the farmer missed an agreed plan: {commitment.Summary}",
                            this.config.MaxMemoryEntriesPerNpc
                        );
                        this.PushInteractionContext(npc, $"Expired commitment for {npc.Name}: {commitment.Summary}.");
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

    private void TryTriggerCommitmentArrivals()
    {
        if (!this.config.EnableCommitments
            || Game1.currentLocation == null
            || Game1.player == null
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

            var commitment = state.Commitments.FirstOrDefault(candidate =>
                candidate.Status == "Pending"
                && !candidate.ArrivalGreetingShown
                && candidate.LocationName == Game1.currentLocation.Name
                && this.IsWithinCommitmentWindow(candidate)
            );
            if (commitment == null)
            {
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > 3f)
            {
                if (npc.controller == null)
                {
                    this.TryApproachPlayer(npc);
                }

                continue;
            }

            npc.showTextAboveHead(this.BuildCommitmentArrivalGreeting(commitment));
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
            _ => "上次一起度过的时间，我还记得。"
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
        if (heldGift != null)
        {
            var gift = this.BuildGiftMemoryDetails(npc, heldGift);
            this.memory.RecordGiftOffered(npc, gift, this.config.MaxMemoryEntriesPerNpc);
            if (this.config.EnableNpcState)
            {
                this.memory.UpdateStateForGift(npc, gift);
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
            this.memory.UpdateStateForConversationStart(npc);
        }

        this.PushInteractionContext(npc, $"Recorded conversation start for {npc.Name}.");
        this.MarkCommitmentFollowUpsMentionedAfterPrompt(npc);
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
    }

    private void PushInteractionContext(NPC npc, string debugMessage)
    {
        string promptContext = this.memory.BuildPromptContext(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState);
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
        return new GiftMemoryDetails(itemName, labels.DebugLabel, labels.PromptLabel, taste);
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

        string promptContext = this.memory.BuildPromptContext(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState);
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

    private bool IsSafeDestinationTile(GameLocation location, Point tile)
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

        return !Game1.currentLocation.characters.Any(npc => npc.TilePoint == tile);
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
