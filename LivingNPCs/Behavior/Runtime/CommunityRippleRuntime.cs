using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class CommunityRippleRuntime
{
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly Random random;

    public CommunityRippleRuntime(ModConfig config, IMonitor monitor, BehaviorMemory memory, Random random)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.random = random;
    }

    public void TrySpreadConversationSocialRipple(NPC npc, LivingNpcState state)
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
        this.Spread(
            npc,
            "relationship_trend",
            $"the farmer has been speaking with {npc.displayName} more often lately",
            importance
        );
    }

    public void Spread(NPC subject, string kind, string summary, int importance)
    {
        if (Game1.currentLocation == null)
        {
            return;
        }

        string visibility = GetCommunityRippleVisibility(kind);
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

        if (CanSpreadToCloseCircle(visibility, subjectState))
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
                        importance: Math.Max(40, importance - 14),
                        maxEntriesPerNpc: this.config.MaxMemoryEntriesPerNpc))
                {
                    stored++;
                }
            }
        }

        if (visibility == "Public" && IsPublicRumorHub(Game1.currentLocation.Name))
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
                        importance: Math.Max(28, importance - 24),
                        maxEntriesPerNpc: this.config.MaxMemoryEntriesPerNpc))
                {
                    stored++;
                }
            }
        }

        if (stored > 0 && this.config.Debug)
        {
            this.monitor.Log(I18n.Get("log.community.rippleSpread", new { npc = subject.Name, summary, stored }), LogLevel.Debug);
        }
    }

    public void TryPropagate()
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
                        I18n.Get(
                            "log.community.impressionPropagated",
                            new
                            {
                                npc = speaker.Name,
                                circles = string.Join(", ", targets.Select(target => target.CircleKey).Distinct()),
                                depth = retelling.TransmissionDepth,
                                distortion = retelling.DistortionLevel,
                                recipients = stored
                            }),
                        LogLevel.Debug
                    );
                }
            }
        }
    }

    private static string GetCommunityRippleVisibility(string kind)
    {
        return CommunityImpressionStore.NormalizeKind(kind) switch
        {
            "romantic_attention" => "Private",
            "helped" or "shared_experience" => "Personal",
            _ => "Public"
        };
    }

    private static bool CanSpreadToCloseCircle(string visibility, LivingNpcState? subjectState)
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

    private static bool IsPublicRumorHub(string locationName)
    {
        return locationName is "Town" or "Saloon";
    }
}
