using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Engine;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

/// <summary>
/// M1 修复：JsonPropertyLinePattern 不再把 "- 单词: 正文" 形式的台词/选项行误判为元数据，
/// 同时保住对真实 JSON 成员行（引号属性名、'type":' 残缺形、结构性值）的过滤能力。
/// </summary>
public class MetadataLineFilterFixTests
{
    private static readonly IReadOnlySet<string> Portraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "0", "h", "s", "l", "a", "u"
    };

    [Theory]
    [InlineData("- Abigail: Hello there!")]
    [InlineData("- Note: I saw you yesterday.")]
    [InlineData("- Warning: the mines are dangerous!")]
    [InlineData("Sure: sounds good")]
    [InlineData("Okay: let me think about it")]
    [InlineData("- normal line")]
    public void ProseWithSingleWordColonIsNotMetadata(string line)
    {
        Assert.False(ResponseParser.IsMetadataLine(line));
    }

    [Theory]
    // 既有钉死行为（引号属性名 / 前导花括号 / 'type":' 残缺形 / 结构行）。
    [InlineData("\"rapportDelta\": 2,")]
    [InlineData("{endConversation: true")]
    [InlineData("type\":\"give_small_gift\",")]
    [InlineData("itemId\":\"(O)611\",")]
    [InlineData("itemLabel\":\"Blackberry Cobbler\"")]
    [InlineData("].")]
    // 收窄后仍需覆盖的真实 JSON 形态：裸名 + 结构性值、markdown 列表化的引号成员。
    [InlineData("actions: [")]
    [InlineData("ambientFollowUp: {")]
    [InlineData("type: \"give_small_gift\",")]
    [InlineData("- \"rapportDelta\": 2,")]
    public void RealJsonMemberLinesAreStillMetadata(string line)
    {
        Assert.True(ResponseParser.IsMetadataLine(line));
    }

    [Fact]
    public void SpeakerPrefixedDialogueLineSurvivesParsing()
    {
        var parsed = ResponseParser.Parse(
            "- Abigail: Hello there!$h\n%Hi!",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("Abigail: Hello there!$h", parsed.DialogueLine);
        Assert.Equal(new[] { "Hi!" }, parsed.Options);
    }

    [Fact]
    public void SingleWordColonOptionLineSurvivesOptionParsing()
    {
        Assert.Equal(
            "Sure: sounds good",
            ResponseParser.TryParseOption("Sure: sounds good", "Yuki", fixPunctuation: false));
    }

    [Fact]
    public void QuotedJsonMembersWithoutMarkerStillNeverBecomeOptions()
    {
        var parsed = ResponseParser.Parse(
            "- 好的，我记下了。$h\n%谢谢你\ntype\":\"give_small_gift\",\nitemId\":\"(O)611\",",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal(new[] { "谢谢你" }, parsed.Options);
    }
}

/// <summary>
/// M2 修复：台词中的合法百分号（"20%"、"%20"、"% "、行尾 %）不再触发响应尾巴截断；
/// 粘连的 "%选项" / "%%%%选项" 尾巴仍被切除。最终展示与流式预览共用同一条路径。
/// </summary>
public class PercentTruncationFixTests
{
    private static readonly IReadOnlySet<string> Portraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "0", "h", "s", "l", "a", "u"
    };

    [Theory]
    [InlineData("收成比去年多了20%！要不要来看看？")]
    [InlineData("今天雨的概率是 5 % 左右")]
    [InlineData("top %1 player")]
    [InlineData("利润提高了……%")]
    public void ProsePercentSignsAreNeverTruncated(string text)
    {
        Assert.Equal(text, StreamingDialoguePreview.StripHiddenAndResponseTail(text));
    }

    [Theory]
    [InlineData("你好。%好的，走吧", "你好。")]
    [InlineData("你好。%%没问题", "你好。")]
    [InlineData("你好。%%%%好的", "你好。")]
    public void GluedOptionTailsAreStillCut(string text, string expected)
    {
        Assert.Equal(expected, StreamingDialoguePreview.StripHiddenAndResponseTail(text));
    }

    [Fact]
    public void PercentageFollowedByGluedTailKeepsThePercentage()
    {
        Assert.Equal("50%", StreamingDialoguePreview.StripHiddenAndResponseTail("50%%好的"));
    }

    [Theory]
    [InlineData("收成多了20%！真棒", -1)]
    [InlineData("你好。%好的", 3)]
    [InlineData("%行首选项语法不在此截", -1)]
    [InlineData("说好了 %20 折扣", -1)]
    public void GluedTailIndexFollowsMidLineOptionSemantics(string text, int expected)
    {
        Assert.Equal(expected, StreamingDialoguePreview.FindGluedResponseTailIndex(text));
    }

    [Fact]
    public void FinalParseKeepsPercentagesInsideTheDialogueLine()
    {
        var parsed = ResponseParser.Parse(
            "- 今年收成比去年多了20%！真棒。$h\n%不错嘛",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("今年收成比去年多了20%！真棒。$h", parsed.DialogueLine);
        Assert.Equal(new[] { "不错嘛" }, parsed.Options);
    }

    [Fact]
    public void StreamingPreviewKeepsPercentagesInVisibleText()
    {
        string display = StreamingDialoguePreview.PrepareDisplayText("- 收成多了20%！真棒$h");

        Assert.Equal("收成多了20%！真棒", display);
    }
}

/// <summary>
/// M3 修复：zh locale 的语言重试指示不再是空串——重试请求必须与首次不同，
/// 并与英文侧文案强度对等（明确要求只用目标语言重写台词与选项）。
/// </summary>
public class LanguageRetryInstructionFixTests
{
    [Theory]
    [InlineData("zh")]
    [InlineData("zh-CN")]
    [InlineData("ZH-TW")]
    public void ChineseLocaleGetsANonEmptyChineseInstruction(string locale)
    {
        string instruction = ConversationTextPostProcessor.GetLanguageRetryInstruction(locale);

        Assert.False(string.IsNullOrWhiteSpace(instruction));
        Assert.StartsWith("\n", instruction, StringComparison.Ordinal);
        Assert.Contains("中文", instruction, StringComparison.Ordinal);
        Assert.Contains("回应选项", instruction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("")]
    [InlineData("ja")]
    public void OtherLocalesKeepTheEnglishInstruction(string locale)
    {
        string instruction = ConversationTextPostProcessor.GetLanguageRetryInstruction(locale);

        Assert.Contains("English only", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryInstructionAlwaysChangesTheRetryPrompt()
    {
        const string baseCommand = "base command text";

        string zhRetry = baseCommand + ConversationTextPostProcessor.GetLanguageRetryInstruction("zh-CN");
        string enRetry = baseCommand + ConversationTextPostProcessor.GetLanguageRetryInstruction("en");

        Assert.NotEqual(baseCommand, zhRetry);
        Assert.NotEqual(baseCommand, enRetry);
        Assert.NotEqual(zhRetry, enRetry);
    }
}
