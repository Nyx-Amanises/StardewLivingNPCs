using StardewValley;
using System.Collections.Generic;

namespace LivingNPCs.Behavior;

/// <summary>Behavior-side facade for the shared Ridgeside AI policy.</summary>
internal static class RsvAiPolicy
{
    internal static bool IsBlockedNpc(NPC? npc)
        => global::LivingNPCs.RsvAiPolicy.IsBlockedNpc(npc);

    internal static bool IsBlockedNpcName(string? name)
        => global::LivingNPCs.RsvAiPolicy.IsBlockedNpcName(name);

    internal static bool IsBlockedLocationName(string? locationName)
        => global::LivingNPCs.RsvAiPolicy.IsBlockedLocationName(locationName);

    internal static bool IsBlockedContentId(string? contentId)
        => global::LivingNPCs.RsvAiPolicy.IsBlockedContentId(contentId);

    internal static bool IsBlockedDialogueKey(string? dialogueKey)
        => global::LivingNPCs.RsvAiPolicy.IsBlockedDialogueKey(dialogueKey);

    internal static bool ContainsBlockedReference(string? text)
        => global::LivingNPCs.RsvAiPolicy.ContainsBlockedReference(text);

    internal static string RemoveBlockedLines(string? text)
        => global::LivingNPCs.RsvAiPolicy.RemoveBlockedLines(text);

    internal static void RegisterRuntimeNpcAliases(IEnumerable<NPC>? villagers)
        => global::LivingNPCs.RsvAiPolicy.RegisterRuntimeNpcAliases(villagers);

    internal static void RegisterRuntimeNpcAlias(string? internalName, string? displayName)
        => global::LivingNPCs.RsvAiPolicy.RegisterRuntimeNpcAlias(internalName, displayName);

    internal static void RegisterRuntimeLocationAlias(string? internalName, string? displayName)
        => global::LivingNPCs.RsvAiPolicy.RegisterRuntimeLocationAlias(internalName, displayName);

    internal static void RegisterRuntimeContentAliases(string? contentId, params string?[] aliases)
        => global::LivingNPCs.RsvAiPolicy.RegisterRuntimeContentAliases(contentId, aliases);

    internal static void RegisterGameThreadAliases()
        => global::LivingNPCs.RsvAiPolicy.RegisterGameThreadAliases();
}
