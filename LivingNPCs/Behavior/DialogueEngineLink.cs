using System;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.GameHooks;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

/// <summary>
/// 行为系统与进程内对话引擎之间的薄适配层（WP16：取代旧的跨 mod 桥接）。
/// 引擎未装配、被禁用或共存禁用时，所有方法降级为旧"未连接"行为
/// （返回 false / null），行为系统照常运行。默认构造接到 WP12 的门控与请求装配、
/// WP10 的邮件/印象生成器；测试可注入替身或 null（null = 永远未连接）。
/// </summary>
internal sealed class DialogueEngineLink
{
    private readonly IMonitor monitor;
    private readonly Func<bool>? engineAvailable;
    private readonly Func<NPC, SObject, int, bool>? giftDialogueRequester;
    private readonly Func<GiftMailRequest, CancellationToken, Task<string?>>? giftMailGenerator;
    private readonly Func<MemoryImpressionRequest, CancellationToken, Task<string?>>? impressionGenerator;

    public DialogueEngineLink(IMonitor monitor)
        : this(
            monitor,
            engineAvailable: () => PatchGuards.EngineReady,
            giftDialogueRequester: RequestGiftDialogueViaEngine,
            giftMailGenerator: (request, ct) => GiftMailGenerator.Instance.GenerateAsync(request, ct),
            impressionGenerator: (request, ct) => MemoryImpressionGenerator.Instance.GenerateAsync(request, ct))
    {
    }

    internal DialogueEngineLink(
        IMonitor monitor,
        Func<bool>? engineAvailable,
        Func<NPC, SObject, int, bool>? giftDialogueRequester,
        Func<GiftMailRequest, CancellationToken, Task<string?>>? giftMailGenerator,
        Func<MemoryImpressionRequest, CancellationToken, Task<string?>>? impressionGenerator)
    {
        this.monitor = monitor;
        this.engineAvailable = engineAvailable;
        this.giftDialogueRequester = giftDialogueRequester;
        this.giftMailGenerator = giftMailGenerator;
        this.impressionGenerator = impressionGenerator;
    }

    /// <summary>引擎当前可生成（总开关 + 运行时闸 + 共存 + LLM 客户端可用）。</summary>
    public bool IsConnected
    {
        get
        {
            try
            {
                return this.engineAvailable?.Invoke() == true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 请求引擎立刻为"NPC 收到某物品"生成一次 AI 反应对话（求助物品递交路径）。
    /// 确定性判定、无概率门；引擎忙或网络不可用时拒绝。true 仅表示"已受理排队"。
    /// </summary>
    public bool TryRequestGiftDialogue(NPC npc, SObject gift, int taste)
    {
        if (npc == null || gift == null || RsvAiPolicy.IsBlockedNpc(npc) || !this.IsConnected)
        {
            return false;
        }

        try
        {
            return this.giftDialogueRequester?.Invoke(npc, gift, taste) == true;
        }
        catch (Exception ex)
        {
            this.monitor.Log(I18n.Get("log.link.giftDialogueFailed", new { npc = npc.Name, error = ex.Message }), LogLevel.Debug);
            return false;
        }
    }

    /// <summary>生成礼物邮件正文；不可用或失败返回 null（调用方回落模板）。</summary>
    public async Task<string?> GenerateGiftMailAsync(GiftMailRequest request, CancellationToken ct)
    {
        if (request == null
            || RsvAiPolicy.IsBlockedNpcName(request.NpcName)
            || !this.IsConnected
            || this.giftMailGenerator == null)
        {
            return null;
        }

        try
        {
            return await this.giftMailGenerator(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.monitor.Log(I18n.Get("log.link.giftMailFailed", new { npc = request.NpcName, error = ex.Message }), LogLevel.Debug);
            return null;
        }
    }

    /// <summary>压缩一批被挤出的长期记忆为关系印象；不可用或失败返回 null（调用方保留 backlog）。</summary>
    public async Task<string?> GenerateMemoryImpressionAsync(MemoryImpressionRequest request, CancellationToken ct)
    {
        if (request == null
            || RsvAiPolicy.IsBlockedNpcName(request.NpcName)
            || !this.IsConnected
            || this.impressionGenerator == null)
        {
            return null;
        }

        try
        {
            return await this.impressionGenerator(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.monitor.Log(I18n.Get("log.link.impressionFailed", new { npc = request.NpcName, error = ex.Message }), LogLevel.Debug);
            return null;
        }
    }

    private static bool RequestGiftDialogueViaEngine(NPC npc, SObject gift, int taste)
    {
        if (GenerationRequests.SchedulerBusy
            || !PatchGuards.IsEnabledFor(npc)
            || !NetworkGate.IsAvailableForGeneration())
        {
            return false;
        }

        return GenerationRequests.Enqueue(npc, GenerationRequests.BuildGift(npc, gift, taste));
    }
}
