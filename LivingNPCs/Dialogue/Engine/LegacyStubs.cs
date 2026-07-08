using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.GameHooks;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

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

    /// <summary>搬运件补丁的启用判定入口：引擎就绪且该 NPC 启用（主动路径不抽签，WP12 §4.1）。</summary>
    public bool PatchNpc(NPC npc)
    {
        return PatchGuards.AllowInteractiveInput(npc);
    }

    public void ClearContext()
    {
        DialogueEngineHost.Instance?.History.ClearContext();
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
        return PromptAssembler.IsLivingNpcOverride(text);
    }
}


internal sealed class AsyncBuilder
{
    public static AsyncBuilder Instance { get; } = new();

    private AsyncBuilder()
    {
    }

    /// <summary>WP12 的 Esc 挂点；调度器装配后转发（§4.2）。</summary>
    public GenerationScheduler? Scheduler { get; set; }

    public void CancelActiveGeneration()
    {
        this.Scheduler?.CancelActiveGeneration();
    }
}

internal static class TextInputManager
{
    /// <summary>搬运件补丁的输入入口：转发到 WP12 输入请求队列（§4.4）。</summary>
    public static void RequestTextInput(string prompt, NPC npc)
    {
        Ui.TypedInputRequestQueue.Submit(prompt, npc, dialogueKey: string.Empty, conversation: null);
    }
}

