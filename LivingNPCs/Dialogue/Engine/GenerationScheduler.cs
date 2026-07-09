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
    private readonly object gate = new();

    private GenerationRequest? pendingRequest;
    private NPC? pendingNpc;
    private bool inFlight;
    private int generation;

    public GenerationScheduler(DialogueEngine engine, bool subscribeEvents = true)
    {
        this.engine = engine;
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

    /// <summary>请求接纳：在途时新请求记警告并直接丢弃（不排队，§4.2）。</summary>
    public bool Enqueue(NPC npc, GenerationRequest request)
    {
        lock (this.gate)
        {
            if (this.inFlight || this.pendingRequest != null)
            {
                DialogueServices.Monitor?.Log(
                    $"Dropping dialogue generation request for {request.NpcName}: another request is already in flight.",
                    LogLevel.Warn);
                return false;
            }

            this.pendingRequest = request;
            this.pendingNpc = npc;
            return true;
        }
    }

    /// <summary>玩家按 Esc 取消：置取消标志、代际号 +1、关思考窗；底层请求靠超时自灭（§4.2）。</summary>
    public void CancelActiveGeneration()
    {
        lock (this.gate)
        {
            this.generation++;
            this.pendingRequest = null;
            this.pendingNpc = null;
            this.inFlight = false;
        }

        ThinkingDialogueController.Close();
    }

    /// <summary>延迟启动：游戏 tick 且无打开菜单时才真正启动（§4.2）。</summary>
    public void OnUpdateTicked()
    {
        GenerationRequest? request;
        NPC? npc;
        int myGeneration;
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
        }

        if (npc != null)
        {
            ThinkingDialogueController.Start(npc);
        }

        _ = Task.Run(() => this.RunAsync(npc, request!, myGeneration));
    }

    private async Task RunAsync(NPC? npc, GenerationRequest request, int myGeneration)
    {
        GenerationResult? result = null;
        Exception? failure = null;
        try
        {
            result = await this.engine.GenerateAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
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

        ThinkingDialogueController.Close();
        if (!isCurrent)
        {
            // 迟到结果凭代际号校验后静默丢弃（§4.16）。
            return;
        }

        try
        {
            if (failure != null || result == null)
            {
                if (failure != null)
                {
                    DialogueServices.Monitor?.Log($"Dialogue generation crashed for {request.NpcName}: {failure}", LogLevel.Error);
                }

                this.Draw(npc, EngineConstants.KeyError, EngineConstants.FallbackLine);
                return;
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

            this.Draw(npc, result.DialogueKey, text);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"Failed to present generated dialogue: {ex}", LogLevel.Error);
        }
    }

    private void Draw(NPC? npc, string key, string text)
    {
        if (npc == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var dialogue = new StardewValley.Dialogue(npc, key, text);
        Game1.currentSpeaker = npc;
        npc.CurrentDialogue.Push(dialogue);
        // 生成台词的历史由 EngineHistoryWriter 记，向 P11 声明豁免（免得展示记录器重复入历史）。
        GameHooks.DialogueDisplayInterceptor.MarkEnginePresented(dialogue);
        Game1.drawDialogue(npc);
    }
}
