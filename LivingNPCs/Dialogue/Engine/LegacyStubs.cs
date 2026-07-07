using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

internal static class SldConstants
{
    // WP12-TODO: replace with the final dialogue key namespace.
    public const string DialogueKeyPrefix = "LivingNPCs_";
}

internal sealed class Character
{
    public Character(string name, NPC? stardewNpc = null)
    {
        this.Name = string.IsNullOrWhiteSpace(name) ? stardewNpc?.Name ?? string.Empty : name;
        this.StardewNpc = stardewNpc;
    }

    public string Name { get; }
    public NPC? StardewNpc { get; }
    public CharacterBio? Bio { get; init; }
}

internal sealed class CharacterBio
{
    public Dictionary<string, CharacterBioTrait> Traits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CharacterBioTrait
{
    public string Heading { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

internal sealed class DialogueContext
{
    public object? Accept { get; set; }
    public List<ConversationElement> ChatHistory { get; set; } = new();
    public string LivingNpcExtraPrompt { get; set; } = string.Empty;
    public int? Hearts { get; set; }
    public string Location { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public List<string> Weather { get; set; } = new();
    public string CurrentActivity { get; set; } = string.Empty;
    public string NextScheduleLocation { get; set; } = string.Empty;
    public int? MinutesUntilNextSchedule { get; set; }
}

internal sealed class DialogueBuilder
{
    public static DialogueBuilder Instance { get; } = new();

    private DialogueBuilder()
    {
    }

    public bool PatchNpc(NPC npc)
    {
        // WP10-TODO: route through IDialogueEngine once WP10/WP12 own the dialogue entrypoint.
        return false;
    }

    public void ClearContext()
    {
        // WP10-TODO: replace with the new dialogue session/context lifecycle.
    }

    public Character GetCharacter(NPC npc)
    {
        return new Character(npc?.Name ?? string.Empty, npc);
    }

    public Character GetCharacterByName(string npcName)
    {
        return new Character(npcName);
    }
}

internal static class Prompts
{
    public static bool IsLivingNpcThirdPartyOverride(string text)
    {
        // WP10-TODO: move this helper into the new prompt assembly path.
        return !string.IsNullOrWhiteSpace(text)
            && (text.Contains("LivingNPCs", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Active Companion Outing", StringComparison.OrdinalIgnoreCase));
    }
}


internal sealed class AsyncBuilder
{
    public static AsyncBuilder Instance { get; } = new();

    private AsyncBuilder()
    {
    }

    public void CancelActiveGeneration()
    {
        // WP10-TODO: cancellation moves to IDialogueEngine generation tokens.
    }
}

internal static class TextInputManager
{
    public static void RequestTextInput(string prompt, NPC npc)
    {
        // WP12-TODO: wire typed dialogue input to the new game hook entrypoint.
    }
}

