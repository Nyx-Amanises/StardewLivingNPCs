using System;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Dialogue.Ui;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 非流式路径的异步时序（WP10 §4.2）：单飞行、延迟启动（菜单关闭后的 UpdateTicked）、
/// 代际号取消、主线程 tick 交接、异常回退呈现。WP12 的补丁调用 <see cref="Enqueue"/>、
/// <see cref="CancelActiveGeneration"/>；WP12 事件接线调用 <see cref="OnUpdateTicked"/>
/// （或由构造函数自注册 SMAPI 事件）。
/// </summary>
internal sealed class GenerationScheduler
{
    private readonly DialogueEngine engine;
    private readonly Func<NPC?, string, string, bool> draw;
    private readonly Func<NPC?, GenerationRequest, string, string> revalidatePortraitMarkers;
    private readonly object gate = new();

    private GenerationRequest? pendingRequest;
    private NPC? pendingNpc;
    private bool inFlight;
    private int generation;
    private CancellationTokenSource? activeCancellation;
    private Task? activeTask;

    /// <summary>失败 HUD 提示节流（被动台词失败也走这里，避免刷屏）。</summary>
    private static long lastFailureNoticeTicks = long.MinValue;
    private const long FailureNoticeThrottleMs = 20_000;

    public GenerationScheduler(
        DialogueEngine engine,
        bool subscribeEvents = true,
        Func<NPC?, string, string, bool>? drawOverride = null,
        Func<NPC?, GenerationRequest, string, string>? portraitRevalidatorOverride = null)
    {
        this.engine = engine;
        this.draw = drawOverride ?? this.Draw;
        this.revalidatePortraitMarkers = portraitRevalidatorOverride
            ?? DialogueEngineHost.RevalidatePortraitMarkers;
        if (subscribeEvents && DialogueServices.Helper != null)
        {
            DialogueServices.Helper.Events.GameLoop.UpdateTicked += (_, _) => this.OnUpdateTicked();
        }
    }

    /// <summary>是否有请求在途或待启动（单飞行判定）。</summary>
    public bool IsBusy
    {
        get
        {
            lock (this.gate)
            {
                return this.inFlight || this.pendingRequest != null;
            }
        }
    }

    /// <summary>仅供确定性单元测试等待 worker 收尾；生产代码不依赖此观察点。</summary>
    internal Task? ActiveTaskForTests
    {
        get
        {
            lock (this.gate)
            {
                return this.activeTask;
            }
        }
    }

    /// <summary>请求接纳：在途时新请求记警告并直接丢弃（不排队，§4.2）。</summary>
    public bool Enqueue(NPC npc, GenerationRequest request)
    {
        lock (this.gate)
        {
            if (this.inFlight || this.pendingRequest != null)
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.generationBusy",
                        new { npc = request.NpcName },
                        $"Dropping dialogue generation request for {request.NpcName}: another request is already in flight."),
                    LogLevel.Warn);
                return false;
            }

            this.pendingRequest = request;
            this.pendingNpc = npc;
            return true;
        }
    }

    /// <summary>玩家按 Esc 取消当前代际、传播取消令牌并关闭思考窗。</summary>
    public void CancelActiveGeneration()
    {
        CancellationTokenSource? cancellation;
        lock (this.gate)
        {
            this.generation++;
            this.pendingRequest = null;
            this.pendingNpc = null;
            this.inFlight = false;
            cancellation = this.activeCancellation;
            this.activeCancellation = null;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The worker finished concurrently; generation invalidation still rejects its result.
        }

        ThinkingDialogueController.Close();
    }

    /// <summary>延迟启动：游戏 tick 且无打开菜单时才真正启动（§4.2）。</summary>
    public void OnUpdateTicked()
    {
        GenerationRequest? request;
        NPC? npc;
        int myGeneration;
        CancellationTokenSource cancellation;
        lock (this.gate)
        {
            if (this.pendingRequest == null || this.inFlight)
            {
                return;
            }

            if (Game1.activeClickableMenu != null)
            {
                return;
            }

            request = this.pendingRequest;
            npc = this.pendingNpc;
            this.pendingRequest = null;
            this.pendingNpc = null;
            this.inFlight = true;
            myGeneration = ++this.generation;
            cancellation = new CancellationTokenSource();
            this.activeCancellation = cancellation;
        }

        if (npc != null)
        {
            ThinkingDialogueController.Start(npc);
        }

        Task task = Task.Run(() => this.RunAsync(npc, request!, myGeneration, cancellation));
        lock (this.gate)
        {
            this.activeTask = task;
        }
    }

    private async Task RunAsync(
        NPC? npc,
        GenerationRequest request,
        int myGeneration,
        CancellationTokenSource cancellation)
    {
        GenerationResult? result = null;
        Exception? failure = null;
        try
        {
            result = await this.engine.GenerateAsync(request, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (this.gate)
            {
                if (ReferenceEquals(this.activeCancellation, cancellation))
                {
                    this.activeCancellation = null;
                }
            }

            cancellation.Dispose();
        }

        // 主线程交接：注册一次性的 UpdateTicked，把关窗 + 呈现推迟到下一游戏 tick（§4.2）。
        void Handler(object? sender, UpdateTickedEventArgs args)
        {
            DialogueServices.Helper!.Events.GameLoop.UpdateTicked -= Handler;
            this.Present(npc, request, result, failure, myGeneration);
        }

        if (DialogueServices.Helper != null)
        {
            DialogueServices.Helper.Events.GameLoop.UpdateTicked += Handler;
        }
        else
        {
            this.Present(npc, request, result, failure, myGeneration);
        }
    }

    private void Present(NPC? npc, GenerationRequest request, GenerationResult? result, Exception? failure, int myGeneration)
    {
        bool isCurrent;
        lock (this.gate)
        {
            isCurrent = myGeneration == this.generation;
            if (isCurrent)
            {
                // 状态复位：仅当仍是当前代际时清空共享请求状态（§4.2）。
                this.inFlight = false;
            }
        }

        if (!isCurrent)
        {
            // 迟到结果凭代际号校验后静默丢弃（§4.16）。
            return;
        }

        ThinkingDialogueController.Close();

        try
        {
            if (failure != null || result == null)
            {
                if (failure != null)
                {
                    DialogueServices.Monitor?.Log(
                        Util.GetConsoleString(
                            "dialogue.log.generationFailed",
                            new { npc = request.NpcName, error = failure },
                            $"Dialogue generation failed for {request.NpcName}: {failure}"),
                        LogLevel.Error);
                }

                NotifyGenerationFailure();
                this.draw(npc, EngineConstants.KeyError, EngineConstants.FallbackLine);
                return;
            }

            if (result.IsFallback)
            {
                // 引擎内部已兜底为占位行；仍要让玩家知道这不是 NPC 在故意冷场。
                NotifyGenerationFailure();
            }

            if (string.IsNullOrWhiteSpace(result.FormattedLine))
            {
                return;
            }

            string text = result.FormattedLine;
            if (text.StartsWith(EngineConstants.SkipPrefix, StringComparison.Ordinal))
            {
                text = text[EngineConstants.SkipPrefix.Length..];
            }

            text = this.revalidatePortraitMarkers(npc, request, text);

            if (this.draw(npc, result.DialogueKey, text))
            {
                this.engine.CommitResult(result);
            }
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "present generated dialogue", error = ex },
                    $"Failed to present generated dialogue: {ex}"),
                LogLevel.Error);
        }
    }

    /// <summary>生成失败时的游戏内提示（占位行"..."之外的可见解释）；20 秒节流。</summary>
    private static void NotifyGenerationFailure()
    {
        long now = Environment.TickCount64;
        if (lastFailureNoticeTicks != long.MinValue && now - lastFailureNoticeTicks < FailureNoticeThrottleMs)
        {
            return;
        }

        lastFailureNoticeTicks = now;
        LlmHudNotifier.Show(Util.GetConsoleString(
            "dialogue.hud.generationFailed",
            null,
            "LivingNPCs: the AI reply failed to generate, so a placeholder line is shown. See the SMAPI console for the reason (API key, model name, server address, or network)."));
    }

    /// <summary>测试复位：清除失败提示节流。</summary>
    internal static void ResetFailureNoticeForTests()
    {
        lastFailureNoticeTicks = long.MinValue;
    }

    private bool Draw(NPC? npc, string key, string text)
    {
        if (npc == null || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var dialogue = new StardewValley.Dialogue(npc, key, text);
        Game1.currentSpeaker = npc;
        npc.CurrentDialogue.Push(dialogue);
        // 生成台词的历史由 EngineHistoryWriter 记，向 P11 声明豁免（免得展示记录器重复入历史）。
        GameHooks.DialogueDisplayInterceptor.MarkEnginePresented(dialogue);
        Game1.drawDialogue(npc);
        return true;
    }
}
