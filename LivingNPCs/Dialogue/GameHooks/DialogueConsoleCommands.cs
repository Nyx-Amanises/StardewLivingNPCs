using System;
using StardewModdingAPI;

using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// 对话引擎控制台命令（WP12 §4.3.3 + 裁决 3：livingnpcs_ 前缀，不留 valleytalk_ 别名）。
/// livingnpcs_tokens（空/export/reset）为新注册；对话遗忘因与行为系统既有的
/// livingnpcs_forget 同名（SMAPI 禁止重名注册），并入该命令：行为侧清记忆的同时经
/// 挂钩清对话历史，near/NPC 名/all confirm 语义与 §4.3.3 要求一致（撞名裁定见实现记录）。
/// </summary>
internal static class DialogueConsoleCommands
{
    public const string TokensCommandName = "livingnpcs_tokens";

    public static void Register(IModHelper helper)
    {
        helper.ConsoleCommands.Add(
            TokensCommandName,
            Util.GetConsoleString(
                "dialogue.command.tokens.description",
                null,
                "Shows LLM token usage. Usage: livingnpcs_tokens [export|reset]"),
            OnTokensCommand);

        BehaviorDebugCommandHandler.DialogueForgetNpc = ForgetNpc;
        BehaviorDebugCommandHandler.DialogueForgetAll = ForgetAll;
    }

    private static void OnTokensCommand(string command, string[] args)
    {
        var monitor = DialogueServices.Monitor;
        if (monitor == null)
        {
            return;
        }

        try
        {
            string sub = args is { Length: > 0 } ? args[0].ToLowerInvariant() : string.Empty;
            switch (sub)
            {
                case "export":
                {
                    string path = TokenUsageTracker.Instance.ExportCurrentSave();
                    monitor.Log(Util.GetConsoleString(
                        "dialogue.command.tokens.exported",
                        new { path },
                        $"Token usage exported to {path}"), LogLevel.Info);
                    break;
                }

                case "reset":
                {
                    TokenUsageTracker.Instance.ResetCurrentSave();
                    monitor.Log(Util.GetConsoleString(
                        "dialogue.command.tokens.reset",
                        null,
                        "Token usage for the current save has been reset."), LogLevel.Info);
                    break;
                }

                default:
                    monitor.Log(TokenUsageTracker.Instance.BuildConsoleSummary(), LogLevel.Info);
                    break;
            }
        }
        catch (Exception ex)
        {
            monitor.Log($"livingnpcs_tokens failed: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>livingnpcs_forget 挂钩：清指定 NPC 的对话历史（连带上下文与转录，WP10 语义）。</summary>
    internal static void ForgetNpc(string npcName, string displayName)
    {
        var engine = DialogueEngineHost.Instance;
        var monitor = DialogueServices.Monitor;
        if (engine == null || monitor == null || string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        try
        {
            bool cleared = engine.History.Forget(npcName);
            string label = string.IsNullOrWhiteSpace(displayName) ? npcName : displayName;
            monitor.Log(cleared
                ? Util.GetConsoleString(
                    "dialogue.command.forget.npcDone",
                    new { npc = label },
                    $"{label} forgot the AI conversation history.")
                : Util.GetConsoleString(
                    "dialogue.command.forget.npcNoHistory",
                    new { npc = label },
                    $"{label} has no AI conversation history."), LogLevel.Info);
        }
        catch (Exception ex)
        {
            monitor.Log($"Failed to clear AI conversation history for {npcName}: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>livingnpcs_forget all confirm 挂钩：清全部对话历史。</summary>
    internal static void ForgetAll()
    {
        var engine = DialogueEngineHost.Instance;
        var monitor = DialogueServices.Monitor;
        if (engine == null || monitor == null)
        {
            return;
        }

        try
        {
            engine.History.ForgetAll();
            monitor.Log(Util.GetConsoleString(
                "dialogue.command.forget.allDone",
                null,
                "All NPCs forgot their AI conversation history."), LogLevel.Info);
        }
        catch (Exception ex)
        {
            monitor.Log($"Failed to clear all AI conversation histories: {ex.Message}", LogLevel.Error);
        }
    }
}
