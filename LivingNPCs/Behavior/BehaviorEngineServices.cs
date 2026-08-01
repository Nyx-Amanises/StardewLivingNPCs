using System;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Composition root for the behavior layer. Every service and runtime is constructed and wired
/// here, in dependency order, so <see cref="BehaviorEngine"/> can stay a pure event orchestrator.
/// Adding a new runtime means adding it here (and only here); cross-service callbacks point at
/// services, never back into the engine — the one exception is <see cref="AfterManualMemoryClear"/>,
/// a late-bound hook for engine-owned cleanup after the debug memory-clear command.
/// </summary>
internal sealed class BehaviorEngineServices
{
    public Random Random { get; } = new();
    public BehaviorMemory Memory { get; } = new();
    public DialogueEngineLink DialogueLink { get; }
    public GiftSelector GiftSelector { get; }
    public IBehaviorPlanner Planner { get; }
    public AiBehaviorClient AiBehaviorClient { get; }
    public BehaviorFeedbackService Feedback { get; }
    public CommunityRippleRuntime CommunityRipples { get; }
    public BehaviorMailService MailService { get; }
    public MemoryImpressionService MemoryImpressions { get; }
    public BehaviorDebugCommandHandler DebugCommands { get; }
    public NpcLocator Locator { get; }
    public NpcScheduleReturnService ScheduleReturn { get; }
    public WorldActionGate WorldActionGate { get; }
    public ValleyTalkContextService ContextService { get; }
    public GiftOpportunityService GiftOpportunities { get; }
    public GiftActionRuntime GiftActions { get; }
    public DirectWorldActionRuntime DirectWorldActions { get; }
    public CompanionOutingRuntime CompanionOutings { get; }
    public HelpRequestRewardService HelpRequestRewards { get; }
    public HelpRequestQuestLogService HelpRequestQuestLog { get; }
    public ConversationStartRecorder ConversationStartRecorder { get; }
    public DelayedTravelActionRuntime DelayedTravelActions { get; }
    public HelpRequestRuntime HelpRequests { get; }
    public DialogueBehaviorInfluenceRuntime DialogueBehaviorInfluences { get; }
    public Multiplayer.SmapiModMessageBus ModMessageBus { get; }
    public Multiplayer.NpcRelationshipViewStore RelationshipViews { get; } = new();
    public Multiplayer.HelpRequestQuestProjectionStore QuestProjections { get; } = new();
    public Multiplayer.MultiplayerSyncService MultiplayerSync { get; }

    /// <summary>Engine-owned cleanup invoked after the debug command wipes behavior memory.</summary>
    public Action? AfterManualMemoryClear { get; set; }

    public BehaviorEngineServices(IModHelper helper, IMonitor monitor, ModConfig config, string modUniqueId = "Yuki.LivingNPCs")
    {
        this.DialogueLink = new DialogueEngineLink(monitor);
        this.GiftSelector = new GiftSelector(this.Random);
        this.Planner = new AiBehaviorPlanner(new RuleBasedBehaviorPlanner(config, this.Random, this.Memory));
        this.AiBehaviorClient = new AiBehaviorClient(config, monitor);
        this.Feedback = new BehaviorFeedbackService(config, monitor);
        this.ModMessageBus = new Multiplayer.SmapiModMessageBus(helper.Multiplayer, modUniqueId);
        this.MultiplayerSync = new Multiplayer.MultiplayerSyncService(
            this.ModMessageBus,
            Multiplayer.GameMultiplayerRuntimeContext.Instance,
            monitor,
            config,
            this.RelationshipViews,
            this.QuestProjections,
            this.Feedback,
            modUniqueId);
        this.CommunityRipples = new CommunityRippleRuntime(config, monitor, this.Memory, this.Random);
        this.MailService = new BehaviorMailService(helper, this.Memory, this.Random, config, this.DialogueLink);
        this.MemoryImpressions = new MemoryImpressionService(config, monitor, this.Memory, this.DialogueLink);
        this.DebugCommands = new BehaviorDebugCommandHandler(
            helper,
            monitor,
            config,
            this.Memory,
            this.MailService,
            this.Feedback.Show,
            () => this.AfterManualMemoryClear?.Invoke()
        );
        this.Locator = new NpcLocator(config, this.Memory);
        this.ScheduleReturn = new NpcScheduleReturnService(monitor, config);
        this.WorldActionGate = new WorldActionGate(config, this.Memory);

        // The context service and the outing runtime reference each other (outing context feeds the
        // prompt; outing events refresh the prompt), so the outing side is captured lazily.
        CompanionOutingRuntime? companionOutings = null;
        this.ContextService = new ValleyTalkContextService(
            monitor,
            config,
            this.Memory,
            this.GiftSelector,
            this.MailService,
            npc => companionOutings?.BuildPromptContext(npc) ?? string.Empty,
            this.RelationshipViews,
            () => this.MultiplayerSync.UseHostRelationshipViews,
            () => this.MultiplayerSync.SuppressCompanionOutingOpportunity
        );

        this.GiftOpportunities = new GiftOpportunityService(
            config,
            this.Memory,
            this.GiftSelector,
            this.MailService,
            this.Random
        );
        this.GiftActions = new GiftActionRuntime(
            config,
            monitor,
            this.Memory,
            this.GiftSelector,
            this.MailService,
            this.Feedback,
            this.WorldActionGate.CanUseWorldAction
        );
        this.DirectWorldActions = new DirectWorldActionRuntime(
            config,
            this.Memory,
            this.Feedback,
            this.WorldActionGate.CanUseWorldAction
        );
        companionOutings = new CompanionOutingRuntime(
            config,
            monitor,
            GameOutingWorldView.Instance,
            this.Memory,
            this.Feedback,
            this.CommunityRipples,
            this.WorldActionGate.CanUseWorldAction,
            BehaviorActionExecutor.IsSafeDestinationTile,
            this.ScheduleReturn.TryResolveCurrentScheduleTarget,
            (npc, allowCrossLocationTeleport) => this.ScheduleReturn.TryReturnToCurrentSchedule(npc, allowCrossLocationTeleport),
            (npc, debugMessage) => this.ContextService.PushInteractionContext(npc, debugMessage)
        );
        this.CompanionOutings = companionOutings;
        this.HelpRequestRewards = new HelpRequestRewardService(
            config,
            monitor,
            this.Memory,
            this.GiftSelector,
            this.MailService,
            this.Feedback,
            this.CommunityRipples
        );
        this.HelpRequestQuestLog = new HelpRequestQuestLogService(config, this.Memory, this.MailService);
        this.ConversationStartRecorder = new ConversationStartRecorder(
            helper,
            monitor,
            config,
            this.Memory,
            this.DialogueLink,
            this.Feedback,
            this.CommunityRipples,
            this.GiftOpportunities,
            this.HelpRequestRewards,
            this.HelpRequestQuestLog,
            this.QuestProjections,
            this.MultiplayerSync,
            this.Locator.TryFindNpcForInteraction,
            (npc, debugMessage, immediatePromptContext) => this.ContextService.PushInteractionContext(npc, debugMessage, immediatePromptContext),
            () => this.MultiplayerSync.SuppressHostLedgerSideEffects
        );
        this.DelayedTravelActions = new DelayedTravelActionRuntime(
            config,
            monitor,
            this.Memory,
            npcName => this.Locator.TryFindNpcInCurrentLocation(npcName, out NPC? npc) ? npc : null,
            this.CompanionOutings.TryStart
        );
        this.HelpRequests = new HelpRequestRuntime(
            config,
            this.Memory,
            npcName => this.Locator.TryFindNpcInCurrentLocation(npcName, out NPC? npc) ? npc : null,
            (npc, text) => this.Feedback.TryShowNpcSpeechBubble(npc, text),
            (npc, debugMessage) => this.ContextService.PushInteractionContext(npc, debugMessage),
            () =>
            {
                this.HelpRequestQuestLog.Sync();
                this.MultiplayerSync.BroadcastQuestProjections();
            }
        );
        this.DialogueBehaviorInfluences = new DialogueBehaviorInfluenceRuntime(
            config,
            this.Memory,
            npc => BehaviorActionExecutor.TryApproachPlayer(npc, config.AllowApproachPlayer, config.AllowFacePlayer),
            npc => BehaviorActionExecutor.TryStepAway(npc, config.AllowApproachPlayer, config.AllowFacePlayer),
            npc => BehaviorActionExecutor.TryPause(npc, config.AllowFacePlayer),
            npc => BehaviorActionExecutor.TryFacePlayer(npc, config.AllowFacePlayer),
            (npc, text) => this.Feedback.TryShowNpcSpeechBubble(npc, text),
            this.CompanionOutings.HasActiveOuting,
            (npc, debugMessage) => this.ContextService.PushInteractionContext(npc, debugMessage)
        );
    }
}
