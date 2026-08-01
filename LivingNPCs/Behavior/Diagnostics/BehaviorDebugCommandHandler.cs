using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorDebugCommandHandler
{
    /// <summary>WP12 挂接：对话引擎的遗忘处理并入本命令（与 livingnpcs_forget 同名合并，
    /// 见 RewriteSpec/12 实现记录）。(npcName, displayName)。</summary>
    internal static Action<string, string>? DialogueForgetNpc { get; set; }

    /// <summary>WP12 挂接：all confirm 分支连带清全部对话历史。</summary>
    internal static Action? DialogueForgetAll { get; set; }

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorMailService mailService;
    private readonly Func<Multiplayer.MultiplayerDebugStatus> getMultiplayerDebugStatus;
    private readonly Action<string> showFeedback;
    private readonly Action afterMemoryCleared;

    public BehaviorDebugCommandHandler(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        BehaviorMailService mailService,
        Func<Multiplayer.MultiplayerDebugStatus> getMultiplayerDebugStatus,
        Action<string> showFeedback,
        Action afterMemoryCleared)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.mailService = mailService;
        this.getMultiplayerDebugStatus = getMultiplayerDebugStatus;
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
            DialogueForgetAll?.Invoke();
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
        // 对话历史先于行为记忆清理：即使行为侧无记忆，对话历史也应被遗忘（WP12 合并语义）。
        DialogueForgetNpc?.Invoke(npcName, displayName);
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

        WriteDebugCommandOutput(
            this.getMultiplayerDebugStatus(),
            () =>
            {
                if (!this.TryResolveNpcArgument(args, out NPC? npc, out string error) || npc == null)
                {
                    return error;
                }

                return this.memory.BuildDebugSummary(
                    npc,
                    this.config.PromptMemoryEntries,
                    this.config.EnableNpcState);
            },
            section => this.monitor.Log(section, LogLevel.Info),
            TranslateI18n);
    }

    /// <summary>
    /// 按控制台顺序写出调试段。联机段先写，再求值 NPC 段，因此附近无 NPC 或 NPC
    /// 解析较慢时也不会吞掉最关键的同步状态。
    /// </summary>
    internal static void WriteDebugCommandOutput(
        Multiplayer.MultiplayerDebugStatus status,
        Func<string> getNpcSection,
        Action<string> writeSection,
        Func<string, object?, string> translate)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(getNpcSection);
        ArgumentNullException.ThrowIfNull(writeSection);
        ArgumentNullException.ThrowIfNull(translate);

        writeSection(FormatMultiplayerStatus(status, translate));
        writeSection(getNpcSection());
    }

    /// <summary>把不可变联机状态快照格式化为独立控制台段；不访问 SMAPI 或游戏对象。</summary>
    internal static string FormatMultiplayerStatus(
        Multiplayer.MultiplayerDebugStatus status,
        Func<string, object?, string> translate)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(translate);

        string roleKey;
        string syncStatusKey;
        object? syncStatusTokens = null;

        if (!status.IsMultiplayer)
        {
            roleKey = "debug.multiplayer.role.singlePlayer";
            syncStatusKey = "debug.multiplayer.sync.singlePlayer";
        }
        else
        {
            roleKey = status.Role switch
            {
                Multiplayer.MultiplayerRole.HostPlayer => "debug.multiplayer.role.hostPlayer",
                Multiplayer.MultiplayerRole.SplitScreenSecondary => "debug.multiplayer.role.splitScreenSecondary",
                Multiplayer.MultiplayerRole.RemoteFarmhand => "debug.multiplayer.role.remoteFarmhand",
                _ => "debug.multiplayer.role.unknown"
            };
            switch (status.Role)
            {
                case Multiplayer.MultiplayerRole.HostPlayer:
                    syncStatusKey = "debug.multiplayer.sync.hosting";
                    syncStatusTokens = new { count = status.CompatiblePeerCount };
                    break;
                case Multiplayer.MultiplayerRole.SplitScreenSecondary:
                    syncStatusKey = "debug.multiplayer.sync.splitScreenDisabled";
                    break;
                case Multiplayer.MultiplayerRole.RemoteFarmhand:
                    syncStatusKey = status.HandshakeState switch
                    {
                        Multiplayer.ProtocolHandshakeState.AwaitingHost => "debug.multiplayer.sync.awaitingHost",
                        Multiplayer.ProtocolHandshakeState.Compatible when status.HostAuthorityActive
                            => "debug.multiplayer.sync.compatible",
                        Multiplayer.ProtocolHandshakeState.Compatible
                            => "debug.multiplayer.sync.compatibleInactive",
                        Multiplayer.ProtocolHandshakeState.Incompatible => "debug.multiplayer.sync.incompatible",
                        _ => "debug.multiplayer.sync.unknown"
                    };
                    break;
                default:
                    syncStatusKey = "debug.multiplayer.sync.unknown";
                    break;
            }
        }

        string role = translate(roleKey, null);
        string syncStatus = translate(syncStatusKey, syncStatusTokens);

        return string.Join(Environment.NewLine, new[]
        {
            translate("debug.multiplayer.heading", null),
            translate("debug.multiplayer.role", new { role }),
            translate("debug.multiplayer.protocol", new { version = status.ProtocolVersion }),
            translate("debug.multiplayer.syncStatus", new { status = syncStatus }),
            translate("debug.multiplayer.relationshipViews", new { count = status.RelationshipViewCount }),
            translate("debug.multiplayer.pendingExchanges", new { count = status.PendingExchangeReportCount })
        });
    }

    private static string TranslateI18n(string key, object? tokens)
    {
        return tokens == null ? I18n.Get(key) : I18n.Get(key, tokens);
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
