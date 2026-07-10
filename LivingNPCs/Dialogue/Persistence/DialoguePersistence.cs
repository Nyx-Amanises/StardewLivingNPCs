using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 持久化层的接线门面：SaveLoaded/Saving/ReturnedToTitle/GameLaunched 事件、purge 控制台命令。
/// 调用点已由 WP12 收编：<see cref="GameHooks.DialogueEngineBootstrapper.Attach"/> 统一调用 RegisterEvents。
/// </summary>
internal static class DialoguePersistence
{
    public const string PurgeCommandName = "livingnpcs_purge_valleytalk";

    /// <summary>旧对话引擎是否仍在加载（01 §5：在则迁移与对话引擎均不启动）。RegisterEvents 后有效。</summary>
    public static bool LegacyValleyTalkLoaded { get; private set; }

    private static IPersistenceEnvironment? env;
    private static LegacySaveDataMigrator? saveMigrator;

    public static void RegisterEvents()
    {
        var helper = DialogueServices.Helper;
        if (helper == null)
        {
            return;
        }

        env = new SmapiPersistenceEnvironment(helper);
        LegacyValleyTalkLoaded = env.IsLegacyValleyTalkLoaded;
        saveMigrator = new LegacySaveDataMigrator(env);
        TokenUsageTracker.Instance.RegisterEvents();
        Llm.LlmHudNotifier.EnsurePump();

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

        helper.ConsoleCommands.Add(
            PurgeCommandName,
            "Removes the legacy dandm1.ValleyTalk keys from the current save. Usage: livingnpcs_purge_valleytalk confirm",
            OnPurgeCommand);

        if (LegacyValleyTalkLoaded)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.legacyCoexistence",
                    null,
                    "The old ValleyTalk mod (dandm1.ValleyTalk) is still loaded. The LivingNPCs dialogue engine and data migration stay disabled until its folder is removed."),
                LogLevel.Error);
        }
    }

    private static void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        try
        {
            new LegacyFolderMigrator(DialogueServices.Helper.DirectoryPath).RunOnGameLaunched(LegacyValleyTalkLoaded);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.migrationFailed",
                    new { what = "folder data", error = ex.Message },
                    $"Legacy ValleyTalk migration failed (folder data): {ex.Message}"),
                LogLevel.Warn);
        }
    }

    private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        DialogueHistoryStore.Instance.OnSaveLoaded();
        try
        {
            saveMigrator?.RunOnSaveLoaded();
        }
        catch (Exception ex)
        {
            // 失败绝不打断存档载入（§4.4）。
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.migrationFailed",
                    new { what = "save data", error = ex.Message },
                    $"Legacy ValleyTalk migration failed (save data): {ex.Message}"),
                LogLevel.Warn);
        }
    }

    private static void OnSaving(object? sender, SavingEventArgs e)
    {
        try
        {
            DialogueHistoryStore.Instance.FlushOnSaving();
        }
        catch (Exception ex)
        {
            // 失败绝不打断存档保存（§4.4）。
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "flush dialogue histories on save", error = ex.Message },
                    $"Failed to flush dialogue histories on save: {ex.Message}"),
                LogLevel.Error);
        }
    }

    private static void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        DialogueHistoryStore.Instance.OnReturnedToTitle();
    }

    private static void OnPurgeCommand(string command, string[] args)
    {
        var monitor = DialogueServices.Monitor;
        if (env == null || saveMigrator == null || monitor == null)
        {
            return;
        }

        if (!env.IsWorldReady || !env.IsMainPlayer)
        {
            monitor.Log(Util.GetConsoleString(
                "dialogue.purge.hostOnly",
                null,
                "This command needs a loaded save and only works on the host."), LogLevel.Info);
            return;
        }

        if (args.Length == 0 || !string.Equals(args[0], "confirm", StringComparison.OrdinalIgnoreCase))
        {
            int count = saveMigrator.CountLegacyKeys();
            monitor.Log(Util.GetConsoleString(
                "dialogue.purge.usage",
                new { count, command = PurgeCommandName },
                $"This save holds {count} legacy ValleyTalk keys. They are kept for rollback safety; to remove them permanently run: {PurgeCommandName} confirm"), LogLevel.Info);
            return;
        }

        int removed = saveMigrator.PurgeLegacyKeys();
        monitor.Log(Util.GetConsoleString(
            "dialogue.purge.done",
            new { count = removed },
            $"Removed {removed} legacy ValleyTalk keys. The change becomes permanent the next time the game saves."), LogLevel.Info);
    }
}
