using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Dialogue.Ui;
using LivingNPCs.Tests.Dialogue.Persistence;
using StardewValley;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

/// <summary>本文件各引擎级用例共享的最小引擎装配（对齐既有流式/取消测试的服务配置）。</summary>
internal static class RetryFixEngineHarness
{
    public static (DialogueEngine Engine, DialogueHistoryStore Store) Create(ILlmClient client)
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig
        {
            EnableSemanticContextRouting = false,
            EnableLivingNpcActionDecisionPass = false,
            TypedResponses = "With Generated"
        });
        ThirdPartyContentPolicy.ResetForTests();
        LegacyLlm.Instance = new LegacyLlmDummy();

        var store = new DialogueHistoryStore(new FakePersistenceEnvironment()) { ArchiveSink = null };
        var engine = new DialogueEngine(new DialogueEngineServices
        {
            GetBio = _ => new NpcBio
            {
                Biography = "A biography for testing.",
                ValidPortraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "h", "s" }
            },
            LookupPrompt = (key, _, _, _) => $"[{key}]",
            GetWorldSummaryText = optimized => optimized ? "BRIEF" : "FULL",
            GetSamples = (_, _) => new Dictionary<string, string>(),
            GetClient = () => client,
            History = new EngineHistoryWriter(store),
            GetHistory = store.GetHistory,
            Now = () => new StardewTime(2, Season.Summer, 5, 1300),
            GetItemDisplayName = id => "Item-" + id,
            GetNpcDisplayName = name => name,
            GetLocale = () => "en",
            Random = new Random(7)
        });
        return (engine, store);
    }

    public static void Reset()
    {
        LegacyLlm.Instance = new LegacyLlmDummy();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }
}

/// <summary>
/// L1 修复：流式重试通过 OnToken 发送 RetryReset 控制记号，预览缓冲清空，
/// attempt 2 从干净状态显示，不再与 attempt 1 的作废文本拼接。
/// </summary>
[Collection("LlmLayer")]
public sealed class StreamingRetryResetTests : IDisposable
{
    public void Dispose()
    {
        RetryFixEngineHarness.Reset();
    }

    [Fact]
    public void UpdateQueueClearsPreviewOnRetryResetToken()
    {
        var queue = new StreamingDialogueUpdateQueue();
        queue.AppendToken("- 第一次尝试的");
        queue.AppendToken("作废文本");
        queue.AppendToken(StreamingControlTokens.RetryReset);
        queue.AppendToken("- 第二次尝试");

        StreamingDialogueUpdateQueue.PendingUpdate pending = queue.Drain();

        Assert.True(pending.HasValue);
        Assert.Equal("- 第二次尝试", pending.PreviewRawText);
    }

    [Fact]
    public void ResetAloneMarksPreviewDirtyWithEmptyText()
    {
        var queue = new StreamingDialogueUpdateQueue();
        queue.AppendToken("- 作废中");
        Assert.Equal("- 作废中", queue.Drain().PreviewRawText);

        queue.AppendToken(StreamingControlTokens.RetryReset);
        StreamingDialogueUpdateQueue.PendingUpdate pending = queue.Drain();

        // 空字符串预览必须被派发（窗据此清屏回到"…"动画），而不是被当作"无更新"。
        Assert.True(pending.HasValue);
        Assert.Equal(string.Empty, pending.PreviewRawText);
    }

    [Fact]
    public void ResetAfterCompletionIsIgnored()
    {
        var queue = new StreamingDialogueUpdateQueue();
        queue.AppendToken("- 文本");
        queue.Complete(new GenerationResult(), Array.Empty<StreamingResponseOption>(), null, null);
        queue.AppendToken(StreamingControlTokens.RetryReset);

        StreamingDialogueUpdateQueue.PendingUpdate pending = queue.Drain();

        Assert.NotNull(pending.Final);
    }

    [Fact]
    public async Task StreamAsyncEmitsSingleResetBetweenFailedAndSuccessfulAttempts()
    {
        var client = new ScriptedStreamingClient
        {
            AttemptDeltas = new[]
            {
                new[] { "%only ", "option" },                      // attempt 1：不可解析（无台词行）
                new[] { "- Hello", " farmer!$h", "\n%Hi" }         // attempt 2：正常回复
            }
        };
        var (engine, _) = RetryFixEngineHarness.Create(client);
        var sink = new RecordingSink();

        await engine.StreamAsync(new GenerationRequest
        {
            NpcName = "Abigail",
            Trigger = GenerationTrigger.Conversation,
            Conversation = new List<ConversationTurn> { new("hi", true, "retry-reset-turn") },
            Snapshot = new GameStateSnapshot { FarmerName = "Yuki" }
        }, sink, CancellationToken.None);

        // 恰好一个重置记号，且位于 attempt 1 与 attempt 2 的 token 之间。
        int resetIndex = sink.Tokens.IndexOf(StreamingControlTokens.RetryReset);
        Assert.True(resetIndex >= 0, "expected a RetryReset control token");
        Assert.Equal(1, sink.Tokens.Count(token => token == StreamingControlTokens.RetryReset));
        Assert.Equal(new[] { "%only ", "option" }, sink.Tokens.Take(resetIndex));
        Assert.Equal(new[] { "- Hello", " farmer!$h", "\n%Hi" }, sink.Tokens.Skip(resetIndex + 1));

        // 把引擎实际发出的 token 流原样灌进真实预览缓冲：只剩 attempt 2 的干净文本。
        var queue = new StreamingDialogueUpdateQueue();
        foreach (string token in sink.Tokens)
        {
            queue.AppendToken(token);
        }

        Assert.Equal("- Hello farmer!$h\n%Hi", queue.Drain().PreviewRawText);

        Assert.NotNull(sink.Completed);
        Assert.Equal("Hello farmer!$h", sink.Completed!.ParsedLines[0]);
    }

    [Fact]
    public async Task SingleSuccessfulAttemptEmitsNoControlToken()
    {
        var client = new ScriptedStreamingClient
        {
            AttemptDeltas = new[] { new[] { "- Hello friend.$h" } }
        };
        var (engine, _) = RetryFixEngineHarness.Create(client);
        var sink = new RecordingSink();

        await engine.StreamAsync(new GenerationRequest
        {
            NpcName = "Abigail",
            Trigger = GenerationTrigger.Conversation,
            Conversation = new List<ConversationTurn> { new("hi", true, "no-retry-turn") },
            Snapshot = new GameStateSnapshot { FarmerName = "Yuki" }
        }, sink, CancellationToken.None);

        Assert.DoesNotContain(StreamingControlTokens.RetryReset, sink.Tokens);
    }

    private sealed class RecordingSink : IStreamSink
    {
        public List<string> Tokens { get; } = new();
        public GenerationResult? Completed { get; private set; }

        public void OnToken(string delta)
        {
            lock (this.Tokens)
            {
                this.Tokens.Add(delta);
            }
        }

        public void OnCompleted(GenerationResult result, IReadOnlyList<StreamingResponseOption> options)
        {
            this.Completed = result;
        }

        public void OnFailed()
        {
        }
    }

    /// <summary>按调用序返回各次尝试的 token 脚本（超出脚本时重复最后一份）。</summary>
    private sealed class ScriptedStreamingClient : ILlmClient, ILlmCapabilities
    {
        private int calls;

        public IReadOnlyList<IReadOnlyList<string>> AttemptDeltas { get; init; } =
            Array.Empty<IReadOnlyList<string>>();

        public string ProviderId => "ScriptedStreaming";
        public bool IsHighlySensoredModel => false;
        public string ExtraInstructions => string.Empty;

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            return Task.FromResult(LlmReply.Failure("non-streaming path should not be used here", 500));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            int index = Math.Min(this.calls, this.AttemptDeltas.Count - 1);
            this.calls++;
            foreach (string delta in this.AttemptDeltas[index])
            {
                ct.ThrowIfCancellationRequested();
                yield return LlmStreamEvent.Delta(delta);
                await Task.Yield();
            }
        }
    }
}

/// <summary>L2 修复：无 '-' 前缀恢复路径定位所选实例本身，重复台词行不再变成玩家选项。</summary>
public class RecoveredDuplicateLineTests
{
    private static readonly IReadOnlySet<string> Portraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "0", "h", "s"
    };

    [Fact]
    public void DuplicateRecoveredLineDoesNotBecomeAnOption()
    {
        var parsed = ResponseParser.Parse(
            "Hello there!\nHello there!\n%Hi",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("Hello there!", parsed.DialogueLine);
        Assert.Equal(new[] { "Hi" }, parsed.Options);
    }

    [Fact]
    public void DuplicateRecoveredLineWithoutOptionsYieldsNoOptions()
    {
        var parsed = ResponseParser.Parse(
            "Greetings.\nGreetings.",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("Greetings.", parsed.DialogueLine);
        Assert.Empty(parsed.Options);
    }

    [Fact]
    public void RecoveryStillPicksLastCandidateAndSkipsPleasantries()
    {
        var parsed = ResponseParser.Parse(
            "Sure, here is the line:\nWhat a lovely morning.\n%Morning!",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("What a lovely morning.", parsed.DialogueLine);
        Assert.Single(parsed.Options);
    }
}

/// <summary>
/// L3 修复：元数据标记提取与清理侧大小写语义一致；缺叹号/混大小写的标记残片不成为选项。
/// </summary>
public class MetadataMarkerRobustnessTests
{
    private static readonly IReadOnlySet<string> Portraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "0", "h", "s"
    };

    [Theory]
    [InlineData("- 你好。\n!LivingNpcs_Meta {\"rapportDelta\":3}")]
    [InlineData("- 你好。\n!livingnpcs_meta {\"rapportDelta\":3}")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"rapportDelta\":3}")]
    public void AnalysisParsesMarkerCaseInsensitively(string raw)
    {
        ConversationAnalysis analysis = ConversationAnalysis.Parse(raw);

        Assert.Equal(3, analysis.RapportDelta);
    }

    [Fact]
    public void MixedCaseMarkerStillCarriesEndConversation()
    {
        ConversationAnalysis analysis = ConversationAnalysis.Parse(
            "- 再见。\n!LivingNPCs_META {\"endConversation\":true}");

        Assert.True(analysis.EndConversation);
    }

    [Theory]
    [InlineData("LIVINGNPCS_META {\"rapportDelta\":2}")]
    [InlineData("livingnpcs_meta {\"rapportDelta\":2}")]
    [InlineData("%LIVINGNPCS_META 好的")]
    [InlineData("% livingnpcs_meta {\"x\":1}")]
    public void MarkerFragmentsNeverBecomeOptions(string line)
    {
        Assert.Null(ResponseParser.TryParseOption(line, "Yuki", fixPunctuation: false));
    }

    [Fact]
    public void BanglessShortMetadataLineIsDroppedFromParsedOptions()
    {
        var parsed = ResponseParser.Parse(
            "- 好的。$h\n%谢谢\nLIVINGNPCS_META {\"rapportDelta\":2}",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal(new[] { "谢谢" }, parsed.Options);
    }
}

/// <summary>L4 修复：无可见文本的分页连同页界一起剔除，不产生相邻或悬空页界（空白对话页）。</summary>
public class EmptyPortraitPageTests
{
    private static readonly IReadOnlySet<string> Portraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "0", "h", "s"
    };

    [Theory]
    [InlineData("你好#$b#$h#$b#再见", "你好#$b#再见")]
    [InlineData("$h#$b#正文$s", "正文$s")]
    [InlineData("正文$s#$b#$h", "正文$s")]
    [InlineData("A#$b##$b#B", "A#$b#B")]
    [InlineData("第一页$h#$e#$s#$b#第三页", "第一页$h#$b#第三页")]
    public void EmptyPagesAreDroppedWithTheirBoundaries(string input, string expected)
    {
        Assert.Equal(expected, ResponseParser.NormalizePortraitMarkers(input, Portraits));
    }

    [Theory]
    [InlineData("A$h#$b#B$s", "A$h#$b#B$s")]
    [InlineData("单页$h", "单页$h")]
    public void NonEmptyPagesAreUntouched(string input, string expected)
    {
        Assert.Equal(expected, ResponseParser.NormalizePortraitMarkers(input, Portraits));
    }

    [Fact]
    public void ParseDropsPortraitOnlyPageEndToEnd()
    {
        var parsed = ResponseParser.Parse(
            "- 你好#$b# $h #$b#再见",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("你好#$b#再见", parsed.DialogueLine);
        Assert.DoesNotContain("#$b##$b#", parsed.DialogueLine, StringComparison.Ordinal);
    }
}

/// <summary>跨层小项：空目标的 accepted_now 出游（行为层会拒绝执行）不再强制关闭对话。</summary>
[Collection("LlmLayer")]
public sealed class EmptyOutingTargetTests : IDisposable
{
    public void Dispose()
    {
        RetryFixEngineHarness.Reset();
    }

    private static ConversationAnalysis OutingAnalysis(string targetLocation)
    {
        return new ConversationAnalysis
        {
            Actions = new List<ConversationWorldActionRequest>
            {
                new()
                {
                    Type = "companion_outing",
                    TravelConsent = "accepted_now",
                    TargetLocation = targetLocation
                }
            }
        };
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotARealPlace")]
    public void MissingOrUnknownTargetDoesNotCountAsImmediateOuting(string targetLocation)
    {
        Assert.False(DialogueEngine.HasAcceptedImmediateCompanionOuting(
            OutingAnalysis(targetLocation), "我们去海边吧", "好呀，我们走吧！"));
    }

    [Fact]
    public void KnownTargetStillCountsAsImmediateOuting()
    {
        Assert.True(DialogueEngine.HasAcceptedImmediateCompanionOuting(
            OutingAnalysis("Beach"), "我们去海边吧", "好呀，我们走吧！"));
    }

    [Fact]
    public async Task EmptyTargetAcceptedNowKeepsConversationOpen()
    {
        const string reply = """
            - 好的，那我们出发吧。$h
            %我准备好了
            !LIVINGNPCS_META {"rapportDelta":2,"endConversation":false,"actions":[{"type":"companion_outing","targetLocation":"","travelConsent":"accepted_now","durationMinutes":60}]}
            """;
        var (engine, _) = RetryFixEngineHarness.Create(new FixedReplyClient(reply));

        GenerationResult result = await engine.GenerateAsync(new GenerationRequest
        {
            NpcName = "Haley",
            Trigger = GenerationTrigger.Conversation,
            Conversation = new List<ConversationTurn> { new("我们现在出发吗？", true, "empty-target-outing") },
            Snapshot = new GameStateSnapshot { FarmerName = "Yuki" }
        }, CancellationToken.None);

        Assert.False(result.EndConversation);
        Assert.Equal(2, result.ParsedLines.Length);
    }

    private sealed class FixedReplyClient : ILlmClient, ILlmCapabilities
    {
        private readonly string replyText;

        public FixedReplyClient(string replyText)
        {
            this.replyText = replyText;
        }

        public string ProviderId => "FixedReply";
        public bool IsHighlySensoredModel => false;
        public string ExtraInstructions => string.Empty;

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            return Task.FromResult(LlmReply.Success(this.replyText, null));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return LlmStreamEvent.Delta(this.replyText);
            await Task.CompletedTask;
        }
    }
}
