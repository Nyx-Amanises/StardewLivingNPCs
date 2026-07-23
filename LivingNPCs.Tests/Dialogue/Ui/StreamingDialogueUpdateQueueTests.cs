using System;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Ui;

public sealed class StreamingDialogueUpdateQueueTests
{
    [Fact]
    public async Task BackgroundTokensAreCoalescedUntilTheGameThreadDrainsThem()
    {
        var queue = new StreamingDialogueUpdateQueue();

        await Task.Run(() =>
        {
            queue.AppendToken("Hel");
            queue.AppendToken("lo");
        });

        StreamingDialogueUpdateQueue.PendingUpdate update = queue.Drain();
        Assert.Equal("Hello", update.PreviewRawText);
        Assert.Null(update.Final);
        Assert.False(queue.Drain().HasValue);
    }

    [Fact]
    public async Task FinalResultAtomicallySupersedesPreviewAndRejectsLateTokens()
    {
        var queue = new StreamingDialogueUpdateQueue();
        var result = new GenerationResult { ParsedLines = new[] { "Final line.$h" } };
        var option = new StreamingResponseOption("Answer", StreamingResponseOptionKind.Generated);

        await Task.Run(() =>
        {
            queue.AppendToken("partial");
            queue.Complete(result, new[] { option }, null, null);
            queue.AppendToken(" late");
            queue.Complete(new GenerationResult { ParsedLines = new[] { "Wrong" } }, null, null, null);
        });

        StreamingDialogueUpdateQueue.PendingUpdate update = queue.Drain();
        Assert.Null(update.PreviewRawText);
        Assert.NotNull(update.Final);
        Assert.Same(result, update.Final!.Result);
        Assert.Equal("Answer", Assert.Single(update.Final.ResponseOptions).Text);
        Assert.False(queue.Drain().HasValue);
    }
}
