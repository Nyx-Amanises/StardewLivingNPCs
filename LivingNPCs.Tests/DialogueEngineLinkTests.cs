using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests;

/// <summary>
/// WP16 §7.7：DialogueEngineLink 的降级行为——引擎不可用（可用性委托返回 false 或未注入）
/// 时，各方法回落到旧"未连接"语义：false / null，且不抛异常。
/// </summary>
public sealed class DialogueEngineLinkTests
{
    private static readonly GiftMailRequest MailRequest =
        new("Abigail", "Abigail", "reciprocal", "(O)66", "Amethyst", "Leek", "small", 30);

    private static readonly MemoryImpressionRequest ImpressionRequest =
        new("Abigail", "Abigail", "", new[] { "[-3d] Helped fix the fence." }, 45);

    private static DialogueEngineLink Link(
        bool? available,
        System.Func<GiftMailRequest, CancellationToken, Task<string?>>? mail = null,
        System.Func<MemoryImpressionRequest, CancellationToken, Task<string?>>? impression = null)
    {
        return new DialogueEngineLink(
            new FakeMonitor(),
            engineAvailable: available.HasValue ? () => available.Value : null,
            giftDialogueRequester: null,
            giftMailGenerator: mail,
            impressionGenerator: impression);
    }

    [Fact]
    public void UnavailableEngineReportsNotConnected()
    {
        Assert.False(Link(available: false).IsConnected);
        Assert.False(Link(available: null).IsConnected);
        Assert.True(Link(available: true).IsConnected);
    }

    [Fact]
    public void ThrowingAvailabilityProbeCountsAsNotConnected()
    {
        var link = new DialogueEngineLink(
            new FakeMonitor(),
            engineAvailable: () => throw new System.InvalidOperationException("not wired yet"),
            giftDialogueRequester: null,
            giftMailGenerator: null,
            impressionGenerator: null);
        Assert.False(link.IsConnected);
    }

    [Fact]
    public void GiftDialogueRequestWithNullNpcIsRejected()
    {
        Assert.False(Link(available: true).TryRequestGiftDialogue(null!, null!, taste: 0));
    }

    [Fact]
    public async Task GiftMailFallsBackToNullWhenEngineUnavailable()
    {
        var link = Link(available: false, mail: (_, _) => Task.FromResult<string?>("Dear @, thanks!"));
        Assert.Null(await link.GenerateGiftMailAsync(MailRequest, CancellationToken.None));
    }

    [Fact]
    public async Task GiftMailPassesThroughWhenAvailable()
    {
        var link = Link(available: true, mail: (_, _) => Task.FromResult<string?>("Dear @, thanks!"));
        Assert.Equal("Dear @, thanks!", await link.GenerateGiftMailAsync(MailRequest, CancellationToken.None));
    }

    [Fact]
    public async Task GiftMailSwallowsGeneratorExceptionsAsNull()
    {
        var link = Link(available: true, mail: (_, _) => throw new System.InvalidOperationException("boom"));
        Assert.Null(await link.GenerateGiftMailAsync(MailRequest, CancellationToken.None));
    }

    [Fact]
    public async Task ImpressionFallsBackToNullWhenEngineUnavailable()
    {
        var link = Link(available: false, impression: (_, _) => Task.FromResult<string?>("An impression."));
        Assert.Null(await link.GenerateMemoryImpressionAsync(ImpressionRequest, CancellationToken.None));
    }

    [Fact]
    public async Task ImpressionPassesThroughWhenAvailable()
    {
        var link = Link(available: true, impression: (_, _) => Task.FromResult<string?>("An impression."));
        Assert.Equal("An impression.", await link.GenerateMemoryImpressionAsync(ImpressionRequest, CancellationToken.None));
    }

    [Fact]
    public async Task MissingGeneratorsDegradeToNull()
    {
        var link = Link(available: true);
        Assert.Null(await link.GenerateGiftMailAsync(MailRequest, CancellationToken.None));
        Assert.Null(await link.GenerateMemoryImpressionAsync(ImpressionRequest, CancellationToken.None));
    }
}
