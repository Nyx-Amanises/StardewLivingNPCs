using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorDebugCommandHandler
{
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorMailService mailService;
    private readonly Action<string> showFeedback;

    public BehaviorDebugCommandHandler(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        BehaviorMailService mailService,
        Action<string> showFeedback)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.mailService = mailService;
        this.showFeedback = showFeedback;
    }

    public void RegisterConsoleCommands()
    {
        this.helper.ConsoleCommands.Add(
            "livingnpcs_debug",
            "输出 NPC 当前状态、最近行为选择原因、求助生成适配和记忆召回摘要。用法：livingnpcs_debug [near|NPC名字]",
            this.OnDebugCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_prompt",
            "输出 LivingNPCs 即将注入 ValleyTalk 的完整隐藏上下文。用法：livingnpcs_prompt [near|NPC名字]",
            this.OnPromptCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_export",
            "导出 Markdown 调试报告。用法：livingnpcs_export [near|all|NPC名字]",
            this.OnExportCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_eval",
            "运行一组轻量运行时诊断，检查关键人格化规则是否还在。",
            this.OnEvalCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_giftmail",
            "诊断 LivingNPCs 礼物信：追踪状态、邮箱位置、Data/mail 是否有对应条目、生成文本、孤儿死信。",
            this.OnGiftMailCommand
        );
    }

    private void OnGiftMailCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("需要先进入存档再运行。", LogLevel.Info);
            return;
        }

        foreach (string line in this.mailService.DescribeGiftMails())
        {
            this.monitor.Log(line, LogLevel.Info);
        }
    }

    public void ShowNearestNpcMemory()
    {
        if (!this.TryFindNearestNpcIgnoringDailyBudget(out NPC? npc) || npc == null)
        {
            this.showFeedback("LivingNPCs：附近没有可查看记忆的 NPC。");
            return;
        }

        string summary = this.memory.BuildDebugSummary(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState);
        this.monitor.Log(summary, LogLevel.Info);
        this.showFeedback($"LivingNPCs：已在 SMAPI 控制台输出 {npc.displayName} 的状态和记忆。");
    }

    private void OnDebugCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("LivingNPCs：请先载入存档后再使用调试命令。", LogLevel.Info);
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        this.monitor.Log(this.memory.BuildDebugSummary(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState), LogLevel.Info);
    }

    private void OnPromptCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("LivingNPCs：请先载入存档后再使用 prompt 调试命令。", LogLevel.Info);
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        string promptContext = this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests
        );
        this.monitor.Log($"LivingNPCs：{npc.displayName} 的 ValleyTalk 上下文预览：\n{promptContext}", LogLevel.Info);
    }

    private void OnExportCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("LivingNPCs：请先载入存档后再导出调试报告。", LogLevel.Info);
            return;
        }

        string target = JoinCommandArgs(args);
        if (string.IsNullOrWhiteSpace(target))
        {
            target = "near";
        }

        string directory = this.GetDebugReportDirectory();
        Directory.CreateDirectory(directory);

        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            string indexPath = Path.Combine(directory, "index.md");
            File.WriteAllText(indexPath, BehaviorDiagnostics.BuildTrackedStateIndex(this.memory.GetTrackedStates()));

            foreach (var state in this.memory.GetTrackedStates())
            {
                string statePath = Path.Combine(directory, $"{SanitizeFileName(state.NpcName)}.state.md");
                File.WriteAllText(statePath, BehaviorDiagnostics.BuildStateOnlyMarkdownReport(state));
            }

            foreach (var currentNpc in Game1.currentLocation?.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)) ?? Enumerable.Empty<NPC>())
            {
                string npcPath = Path.Combine(directory, $"{SanitizeFileName(currentNpc.Name)}.current.md");
                File.WriteAllText(npcPath, BehaviorDiagnostics.BuildNpcMarkdownReport(currentNpc, this.memory, this.config));
            }

            this.monitor.Log($"LivingNPCs：已导出全局调试报告到 {directory}", LogLevel.Info);
            this.showFeedback("LivingNPCs：已导出全局调试报告。");
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        string filePath = Path.Combine(directory, $"{SanitizeFileName(npc.Name)}.md");
        File.WriteAllText(filePath, BehaviorDiagnostics.BuildNpcMarkdownReport(npc, this.memory, this.config));
        this.monitor.Log($"LivingNPCs：已导出 {npc.displayName} 的调试报告到 {filePath}", LogLevel.Info);
        this.showFeedback($"LivingNPCs：已导出 {npc.displayName} 的调试报告。");
    }

    private void OnEvalCommand(string command, string[] args)
    {
        this.monitor.Log(BehaviorDiagnostics.RunRuntimeEvaluationSuite(), LogLevel.Info);
    }

    private bool TryResolveNpcArgument(string[] args, out NPC? npc, out string error)
    {
        npc = null;
        string query = JoinCommandArgs(args);
        if (string.IsNullOrWhiteSpace(query) || query.Equals("near", StringComparison.OrdinalIgnoreCase))
        {
            if (this.TryFindNearestNpcIgnoringDailyBudget(out npc) && npc != null)
            {
                error = string.Empty;
                return true;
            }

            error = "LivingNPCs：附近没有可调试的 NPC。";
            return false;
        }

        npc = Game1.currentLocation?.characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.displayName, query, StringComparison.OrdinalIgnoreCase));
        if (npc != null)
        {
            error = string.Empty;
            return true;
        }

        error = $"LivingNPCs：当前地点没有找到 NPC“{query}”。可以靠近 NPC 后用 near，或使用当前地图上的 NPC 名字。";
        return false;
    }

    private string GetDebugReportDirectory()
    {
        string saveFolder = string.IsNullOrWhiteSpace(Constants.SaveFolderName)
            ? "NoSave"
            : Constants.SaveFolderName;
        return Path.Combine(this.helper.DirectoryPath, "debug_reports", SanitizeFileName(saveFolder));
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

    private static string JoinCommandArgs(string[] args)
    {
        return string.Join(" ", args ?? Array.Empty<string>()).Trim();
    }

    private static string SanitizeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return safe;
    }
}
