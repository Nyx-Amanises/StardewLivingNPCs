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
    private readonly Action afterMemoryCleared;

    public BehaviorDebugCommandHandler(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        BehaviorMailService mailService,
        Action<string> showFeedback,
        Action afterMemoryCleared)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.mailService = mailService;
        this.showFeedback = showFeedback;
        this.afterMemoryCleared = afterMemoryCleared;
    }

    public void RegisterConsoleCommands()
    {
        this.helper.ConsoleCommands.Add(
            "livingnpcs_debug",
            I18n.Get("command.livingnpcs_debug.description"),
            this.OnDebugCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_prompt",
            I18n.Get("command.livingnpcs_prompt.description"),
            this.OnPromptCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_export",
            I18n.Get("command.livingnpcs_export.description"),
            this.OnExportCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_eval",
            I18n.Get("command.livingnpcs_eval.description"),
            this.OnEvalCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_giftmail",
            I18n.Get("command.livingnpcs_giftmail.description"),
            this.OnGiftMailCommand
        );
        this.helper.ConsoleCommands.Add(
            "livingnpcs_forget",
            I18n.Get("command.livingnpcs_forget.description"),
            this.OnForgetCommand
        );
    }

    private void OnGiftMailCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log(I18n.Get("debug.needSaveLoaded"), LogLevel.Info);
            return;
        }

        foreach (string line in this.mailService.DescribeGiftMails())
        {
            this.monitor.Log(line, LogLevel.Info);
        }
    }

    private void OnForgetCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log(I18n.Get("debug.needSaveLoaded"), LogLevel.Info);
            return;
        }

        string target = JoinCommandArgs(args);
        if (string.IsNullOrWhiteSpace(target) || target.Equals("near", StringComparison.OrdinalIgnoreCase))
        {
            if (!this.TryFindNearestNpcIgnoringDailyBudget(out NPC? nearbyNpc) || nearbyNpc == null)
            {
                this.monitor.Log(I18n.Get("debug.noNearbyForgetNpc"), LogLevel.Info);
                return;
            }

            this.ClearNpcMemory(nearbyNpc.Name, nearbyNpc.displayName);
            return;
        }

        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            this.monitor.Log(I18n.Get("debug.forgetConfirmAll"), LogLevel.Info);
            return;
        }

        if (target.Equals("all confirm", StringComparison.OrdinalIgnoreCase))
        {
            int count = this.memory.ClearAllMemory();
            this.afterMemoryCleared();
            this.monitor.Log(I18n.Get("debug.forgetAllDone", new { count }), LogLevel.Info);
            this.showFeedback(I18n.Get("debug.forgetAllHud", new { count }));
            return;
        }

        if (!this.TryResolveMemoryTarget(target, out string npcName, out string displayName, out string error))
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        this.ClearNpcMemory(npcName, displayName);
    }

    private void ClearNpcMemory(string npcName, string displayName)
    {
        if (!this.memory.ClearNpcMemory(npcName))
        {
            this.monitor.Log(I18n.Get("debug.forgetNpcNoMemory", new { npc = displayName }), LogLevel.Info);
            return;
        }

        this.afterMemoryCleared();
        this.monitor.Log(I18n.Get("debug.forgetNpcDone", new { npc = displayName }), LogLevel.Info);
        this.showFeedback(I18n.Get("debug.forgetNpcHud", new { npc = displayName }));
    }

    private bool TryResolveMemoryTarget(string query, out string npcName, out string displayName, out string error)
    {
        npcName = string.Empty;
        displayName = query;
        error = string.Empty;

        NPC? npc = Game1.currentLocation?.characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.displayName, query, StringComparison.OrdinalIgnoreCase));
        if (npc == null)
        {
            npc = Game1.getCharacterFromName(query);
        }

        if (npc != null)
        {
            npcName = npc.Name;
            displayName = npc.displayName;
            return true;
        }

        LivingNpcState? state = this.memory.GetTrackedStates()
            .FirstOrDefault(candidate => string.Equals(candidate.NpcName, query, StringComparison.OrdinalIgnoreCase));
        if (state != null)
        {
            npcName = state.NpcName;
            displayName = state.NpcName;
            return true;
        }

        error = I18n.Get("debug.npcNotFoundForget", new { query });
        return false;
    }

    public void ShowNearestNpcMemory()
    {
        if (!this.TryFindNearestNpcIgnoringDailyBudget(out NPC? npc) || npc == null)
        {
            this.showFeedback(I18n.Get("debug.noNearbyMemoryNpc"));
            return;
        }

        string summary = this.memory.BuildDebugSummary(npc, this.config.PromptMemoryEntries, this.config.EnableNpcState);
        this.monitor.Log(summary, LogLevel.Info);
        this.showFeedback(I18n.Get("debug.memoryPrintedHud", new { npc = npc.displayName }));
    }

    private void OnDebugCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log(I18n.Get("debug.needSaveLoadedDebug"), LogLevel.Info);
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
            this.monitor.Log(I18n.Get("debug.needSaveLoadedPrompt"), LogLevel.Info);
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
            this.config.HelpRequestCooldownDays
        );
        this.monitor.Log(I18n.Get("debug.promptPreview", new { npc = npc.displayName, context = promptContext }), LogLevel.Info);
    }

    private void OnExportCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log(I18n.Get("debug.needSaveLoadedExport"), LogLevel.Info);
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

            this.monitor.Log(I18n.Get("debug.exportedAll", new { path = directory }), LogLevel.Info);
            this.showFeedback(I18n.Get("debug.exportedAllHud"));
            return;
        }

        if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
        {
            this.monitor.Log(error, LogLevel.Info);
            return;
        }

        string filePath = Path.Combine(directory, $"{SanitizeFileName(npc.Name)}.md");
        File.WriteAllText(filePath, BehaviorDiagnostics.BuildNpcMarkdownReport(npc, this.memory, this.config));
        this.monitor.Log(I18n.Get("debug.exportedNpc", new { npc = npc.displayName, path = filePath }), LogLevel.Info);
        this.showFeedback(I18n.Get("debug.exportedNpcHud", new { npc = npc.displayName }));
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

            error = I18n.Get("debug.noNearbyDebugNpc");
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

        error = I18n.Get("debug.npcNotFound", new { query });
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
