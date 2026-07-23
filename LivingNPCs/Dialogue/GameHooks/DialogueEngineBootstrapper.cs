using System;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Dialogue.Ui;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// 对话引擎接线单入口（WP12 §4.7）：收编 WP14/WP15 的注册调用、装配 WP10 引擎与调度器、
/// 订阅输入队列泵、注册控制台命令；GameLaunched 做旧 mod 共存检测（§4.8）后再打 Harmony 补丁；
/// SaveLoaded / ReturnedToTitle 做存档作用域重置。ModEntry 只调用 <see cref="Attach"/> 一次。
/// </summary>
internal static class DialogueEngineBootstrapper
{
    private static bool attached;
    private static bool engineWiredAtAttach;
    private static bool legacyValleyTalkActive;
    private static string harmonyId = "Yuki.LivingNPCs";
    private static IModHelper helper = null!;

    /// <summary>
    /// §4.7 初始化清单的唯一入口。调用前提是主开关 EnableMod 为开（关闭时对话引擎整体不接线）。
    /// 引擎总开关（EnableDialogueEngine）关闭时只保留基础订阅与 WP14/WP15 注册，
    /// 跳过补丁、调度器、输入泵与命令注册（GMCM 可重新开启，关→开需重启，§4.3.4）。
    /// </summary>
    public static void Attach(
        IModHelper modHelper,
        IMonitor monitor,
        ModConfig config,
        string modUniqueId,
        Func<NPC, string>? conversationContextProvider = null,
        Func<NPC, string, string, int, string>? giftContextProvider = null)
    {
        if (attached)
        {
            return;
        }

        attached = true;
        helper = modHelper;
        if (!string.IsNullOrWhiteSpace(modUniqueId))
        {
            harmonyId = modUniqueId;
        }

        // ① WP14 持久化：SaveLoaded/Saving/ReturnedToTitle/GameLaunched 订阅、purge 命令、
        //    用量台账事件、旧 mod 加载检测（LegacyValleyTalkLoaded）。
        DialoguePersistence.RegisterEvents();

        // ② WP15 内容/配置：资产提供器集中注册（Entry 阶段，先于任何资产请求）、旧 config 导入、
        //    GameLaunched 内容包授权扫描 + LLM 客户端初始化。顺序在持久化之后（先导旧配置再建客户端）。
        DialogueContentSetup.Register(modHelper, config);

        // ③ 行为系统上下文注入点（WP16 方向反转；未接线时为 null，引擎侧照常）。
        GenerationRequests.ConversationContextProvider = conversationContextProvider;
        GenerationRequests.GiftContextProvider = giftContextProvider;

        modHelper.Events.GameLoop.GameLaunched += OnGameLaunched;
        modHelper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        modHelper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

        engineWiredAtAttach = config.EnableDialogueEngine;
        if (!engineWiredAtAttach)
        {
            monitor.Log(
                Util.GetConsoleString(
                    "dialogue.log.engineDisabledConfig",
                    null,
                    "The AI dialogue engine is disabled in the config; LLM client setup, Harmony patches, schedulers and dialogue commands stay off (enable it in GMCM and restart)."),
                LogLevel.Info);
            return;
        }

        // ④ WP10 引擎装配：内容门面 → 引擎 → 调度器（生成调度泵自订 UpdateTicked）→ Esc 挂点。
        var engine = DialogueEngineHost.CreateDefault(DialogueContentService.Instance!);
        AsyncBuilder.Instance.Scheduler = new GenerationScheduler(engine);

        // ⑤ 输入请求队列泵（§4.4）。
        modHelper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        modHelper.Events.Input.ButtonPressed += OnButtonPressed;

        // ⑥ 控制台命令（§4.3.3：livingnpcs_tokens 新注册；对话遗忘并入 livingnpcs_forget）。
        DialogueConsoleCommands.Register(modHelper);
    }

    private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        ConversationTranscriptExporter.ExportPending();
        TypedInputRequestQueue.OnUpdateTicked();
    }

    /// <summary>
    /// 在游戏把动作键交给 NPC.checkAction 之前认领“触发键 + 动作键”，避免拥抱/亲吻类 mod
    /// 的 Harmony prefix/postfix 抢先消费同一次点击。
    /// </summary>
    private static void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        try
        {
            if (!Context.IsWorldReady
                || Game1.activeClickableMenu != null
                || Game1.player?.CanMove != true
                || !e.Button.IsActionButton()
                || !DialoguePatchHelpers.IsTriggerKeyDown()
                || !DialoguePatchHelpers.TryFindNpcForInteraction(e.Cursor, out NPC? npc)
                || npc == null)
            {
                return;
            }

            TryClaimTypedDialogueAction(
                npc,
                PatchGuards.AllowInteractiveInput(npc),
                DialoguePatchHelpers.BeginTypedConversation,
                () => helper.Input.Suppress(e.Button));
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "claim modified action input", error = ex.Message },
                    $"Failed to claim modified action input: {ex.Message}"),
                LogLevel.Warn);
        }
    }

    /// <summary>测试缝：只有目标 NPC 通过主动输入门控时，才发起输入并抑制本次动作键。</summary>
    internal static bool TryClaimTypedDialogueAction(
        NPC? npc,
        bool allowInteractiveInput,
        Action<NPC> beginConversation,
        Action suppressActionButton)
    {
        if (npc == null || !allowInteractiveInput)
        {
            return false;
        }

        beginConversation(npc);
        suppressActionButton();
        return true;
    }

    /// <summary>共存检测放 GameLaunched（Entry 阶段 registry 可能不全）→ 通过后才 PatchAll（§4.8）。</summary>
    private static void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        try
        {
            legacyValleyTalkActive = DialoguePersistence.LegacyValleyTalkLoaded
                || helper.ModRegistry.IsLoaded("dandm1.ValleyTalk");
        }
        catch
        {
            legacyValleyTalkActive = DialoguePersistence.LegacyValleyTalkLoaded;
        }

        if (legacyValleyTalkActive)
        {
            EnableGate.EngineDisabledByCoexistence = true;
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.legacyCoexistenceEngine",
                    null,
                    "The old ValleyTalk mod (dandm1.ValleyTalk) is still installed. The LivingNPCs dialogue engine stays disabled to avoid double Harmony patches — please delete the ValleyTalk folder under Mods (it may be nested as ValleyTalk/ValleyTalk). Behavior features keep working."),
                LogLevel.Error);
            return;
        }

        if (!engineWiredAtAttach)
        {
            return;
        }

        try
        {
            var harmony = new Harmony(harmonyId);
            harmony.PatchAll(typeof(DialogueEngineBootstrapper).Assembly);
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.patchesApplied", new { count = harmony.GetPatchedMethods().Count() }),
                LogLevel.Trace);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "apply dialogue Harmony patches", error = ex },
                    $"Failed to apply dialogue Harmony patches: {ex}"),
                LogLevel.Error);
        }
    }

    private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // 存档作用域状态重置（§4.3）：会话上下文、未消费输入请求、在途生成、婚后缓冲、
        // 展示记录当日表。历史四桶的重载由 WP14 在其自身 SaveLoaded 订阅中完成（注册序在前）。
        ResetSaveScopedState();

        // 会话转录导出器全量导出（Diagnostics 搬运件，依赖历史已载入）。
        try
        {
            ConversationTranscriptExporter.ExportAllKnownHistories();
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "export conversation transcripts on save load", error = ex.Message },
                    $"Failed to export conversation transcripts on save load: {ex.Message}"),
                LogLevel.Warn);
        }

        if (legacyValleyTalkActive)
        {
            ShowCoexistenceHudWarning();
        }
    }

    private static void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        ResetSaveScopedState();
    }

    private static void ResetSaveScopedState()
    {
        try
        {
            AsyncBuilder.Instance.Scheduler?.CancelActiveGeneration();
            DialogueEngineHost.Instance?.History.ClearContext();
            ConversationTranscriptExporter.ClearPending();
            TypedInputRequestQueue.Clear();
            MarriageChoreBuffer.Clear();
            DisplayedDialogueRecorder.Reset();
            DialogueEngineHost.InvalidatePortraitCaches();
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "reset save-scoped dialogue state", error = ex.Message },
                    $"Failed to reset save-scoped dialogue state: {ex.Message}"),
                LogLevel.Warn);
        }
    }

    private static void ShowCoexistenceHudWarning()
    {
        try
        {
            string text = Util.GetString("coexistenceHud", returnNull: true)
                ?? "LivingNPCs: old ValleyTalk detected — AI dialogue is disabled. Delete the ValleyTalk folder under Mods.";
            Game1.addHUDMessage(new HUDMessage(text, HUDMessage.error_type));
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.coexistenceHudFailed", new { error = ex.Message }),
                LogLevel.Trace);
        }
    }
}
