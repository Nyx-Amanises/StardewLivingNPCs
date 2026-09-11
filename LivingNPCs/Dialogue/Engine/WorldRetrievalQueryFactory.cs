using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue.Content;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>Builds search signals from captured data, without consulting game state or saved history.</summary>
internal static class WorldRetrievalQueryFactory
{
    public static WorldRetrievalQuery Create(
        GenerationRequest request, IReadOnlyList<ConversationTurn> conversation, string npcDisplayName)
    {
        var snapshot = request.Snapshot;
        string playerText = request.Trigger == GenerationTrigger.Conversation
            ? SafeSignal(request.CurrentPlayerText)
            : string.Empty;
        string recentDialogue = string.Empty;
        if (!string.IsNullOrWhiteSpace(playerText))
        {
            // Use only the immediate context preceding this submitted turn. A missing/currently
            // withheld input must never promote an earlier saved question into a new query.
            int currentIndex = -1;
            for (int index = conversation.Count - 1; index >= 0; index--)
            {
                if (conversation[index].IsPlayerLine
                    && string.Equals(conversation[index].Text, request.CurrentPlayerText, StringComparison.Ordinal))
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex > 0)
            {
                recentDialogue = string.Join("\n", conversation.Take(currentIndex).TakeLast(3)
                    .Select(turn => SafeSignal(turn.Text))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => text.Length > 320 ? text[..320] : text));
            }
        }

        return new WorldRetrievalQuery
        {
            PlayerText = playerText,
            RecentDialogue = recentDialogue,
            NpcName = SafeSignal(request.NpcName),
            NpcDisplayName = SafeSignal(npcDisplayName),
            LocationName = SafeSignal(snapshot.LocationName),
            LocationDisplayName = SafeSignal(snapshot.LocationDisplayName),
            Season = snapshot.SeasonName,
            CurrentDestination = SafeSignal(string.IsNullOrWhiteSpace(snapshot.CurrentTravelDestination)
                ? snapshot.NextScheduleLocation
                : snapshot.CurrentTravelDestination),
            FestivalName = CalendarFestival(snapshot.SeasonName, snapshot.DayOfMonth),
            NearbyNpcNames = snapshot.NearbyNpcNames
                .Where(name => !RsvAiPolicy.IsBlockedNpcName(name)
                    && !RsvAiPolicy.ContainsBlockedReference(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string SafeSignal(string? text) =>
        RsvAiPolicy.ContainsBlockedReference(text ?? string.Empty) || RsvAiPolicy.IsWithheldPlayerMessage(text ?? string.Empty)
            ? string.Empty
            : text ?? string.Empty;

    private static string CalendarFestival(string season, int day) => (season.ToLowerInvariant(), day) switch
    {
        ("spring", 13) => "Egg Festival",
        ("spring", >= 15 and <= 17) => "Desert Festival",
        ("spring", 24) => "Flower Dance",
        ("summer", 11) => "Luau",
        ("summer", 28) => "Dance of the Moonlight Jellies",
        ("fall", 16) => "Stardew Valley Fair",
        ("fall", 27) => "Spirit's Eve",
        ("winter", 8) => "Festival of Ice",
        ("winter", >= 15 and <= 17) => "Night Market",
        ("winter", 25) => "Feast of the Winter Star",
        _ => string.Empty
    };
}
