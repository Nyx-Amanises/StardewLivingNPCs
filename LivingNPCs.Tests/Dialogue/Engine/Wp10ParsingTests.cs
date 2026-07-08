using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public class DialogueKeyGrammarTests
{
    [Fact]
    public void Parses_MarriagePrefix_Season_Day_And_Year()
    {
        var facts = DialogueKeyGrammar.Parse("M_spring_15_2");
        Assert.True(facts.IsMarriageKey);
        Assert.Equal("spring", facts.Season);
        Assert.Equal(15, facts.DayOfMonth);
        Assert.Equal(2, facts.Year);
    }

    [Fact]
    public void Parses_Weekday_With_Hearts()
    {
        var facts = DialogueKeyGrammar.Parse("Mon4");
        Assert.Equal(1, facts.Weekday);
        Assert.Equal(4, facts.Hearts);
    }

    [Fact]
    public void Parses_Location_With_Hearts()
    {
        var facts = DialogueKeyGrammar.Parse("Saloon8");
        Assert.Equal("Saloon", facts.LocationPrefix);
        Assert.Equal(8, facts.Hearts);
    }

    [Theory]
    [InlineData("cc_Boulder", "cc_Boulder")]
    [InlineData("FlowerDance_Accept_Spouse", "FlowerDance_Accept_Spouse")]
    [InlineData("Characters_MovieInvite_Invited", "Characters_MovieInvite_Invited")]
    public void Parses_MultiSegment_SpecialKeys(string key, string expected)
    {
        Assert.Equal(expected, DialogueKeyGrammar.Parse(key).SpecialContextKey);
    }

    [Fact]
    public void Parses_GiftAccept()
    {
        var facts = DialogueKeyGrammar.Parse("Accept_Emerald");
        Assert.True(facts.IsGiftAccept);
        Assert.Equal("Emerald", facts.GiftItem);
    }

    [Fact]
    public void Parses_Inlaw()
    {
        Assert.Equal("Abigail", DialogueKeyGrammar.Parse("inlaw_Abigail").InlawName);
    }

    [Fact]
    public void Parses_Guid_As_ChatId_Only()
    {
        string guid = Guid.NewGuid().ToString();
        var facts = DialogueKeyGrammar.Parse(guid + "_spring_2");
        Assert.NotEqual(string.Empty, facts.ChatId);
        Assert.Equal(string.Empty, facts.Season);
    }

    [Fact]
    public void Parses_SpouseAction_And_RandomAction_With_TimeSlot()
    {
        Assert.Equal(SpouseAction.FunLeave, DialogueKeyGrammar.Parse("funLeave").SpouseAction);
        var rainy = DialogueKeyGrammar.Parse("Indoor_Day");
        Assert.Equal(RandomAction.Indoor, rainy.RandomAction);
        Assert.Equal("Day", rainy.TimeSlot);
    }

    [Fact]
    public void Parses_ResortTags()
    {
        Assert.Equal("Resort_Leaving", DialogueKeyGrammar.Parse("Resort_Leaving").ResortTag);
        Assert.Equal("Resort", DialogueKeyGrammar.Parse("Resort").ResortTag);
    }
}

public class SampleSelectorTests
{
    private static DialogueKeyFacts Facts(Action<DialogueKeyFacts>? setup = null)
    {
        var facts = new DialogueKeyFacts();
        setup?.Invoke(facts);
        return facts;
    }

    [Fact]
    public void Score_Weights_Hearts_And_Season_And_Gift()
    {
        var current = Facts(f => { f.Hearts = 6; f.Season = "spring"; });
        Assert.Equal(300, SampleSelector.Score(current, Facts(f => { f.Hearts = 3; f.Season = "spring"; })));
        Assert.Equal(50, SampleSelector.Score(current, Facts(f => { f.Hearts = 6; f.Season = "fall"; })));
        Assert.Equal(2000, SampleSelector.Score(current, Facts(f => { f.Hearts = 6; f.Season = "spring"; f.IsGiftAccept = true; })));
    }

    [Fact]
    public void Score_SpouseName_Mismatch_Is_Heavy()
    {
        var current = Facts(f => f.SpouseName = "Abigail");
        Assert.Equal(10000, SampleSelector.Score(current, Facts(f => f.SpouseName = "Leah")));
        Assert.Equal(2000, SampleSelector.Score(current, Facts()));
    }

    [Fact]
    public void Select_Takes_Twenty_Most_Similar()
    {
        var samples = new Dictionary<string, string>();
        for (int day = 1; day <= 28; day++)
        {
            samples[$"spring_{day}"] = $"line {day}";
        }

        var library = SampleSelector.BuildLibrary(samples);
        var current = Facts(f => { f.Season = "spring"; f.DayOfMonth = 3; });
        var selected = SampleSelector.Select(library, current, new Random(1));

        Assert.Equal(SampleSelector.MaxSamples, selected.Count);
        Assert.Contains(selected, sample => sample.Text == "line 3");
    }
}

public class ResponseParserTests
{
    private static readonly IReadOnlySet<string> Portraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "h", "s", "l", "a", "u"
    };

    [Fact]
    public void Parses_Dialogue_Options_And_Drops_Metadata()
    {
        string raw = "- Hello there!$h\n%Nice to see you\n%What are you doing?\n!LIVINGNPCS_META {\"rapportDelta\":1}";
        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("Hello there!$h", parsed.DialogueLine);
        Assert.Equal(2, parsed.Options.Count);
        Assert.Equal("Nice to see you", parsed.Options[0]);
    }

    [Fact]
    public void Recovers_Dialogue_Line_Without_Dash_Prefix()
    {
        string raw = "Sure, here is the line:\nWhat a lovely morning.\n%Morning!";
        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("What a lovely morning.", parsed.DialogueLine);
        Assert.Single(parsed.Options);
    }

    [Fact]
    public void Fails_When_Nothing_Usable()
    {
        Assert.False(ResponseParser.Parse("%option only", Portraits, "Yuki", false).Success);
        Assert.False(ResponseParser.Parse("", Portraits, "Yuki", false).Success);
    }

    [Fact]
    public void Removes_Invalid_Dollar_Tokens_Keeps_Valid_Portraits()
    {
        var parsed = ResponseParser.Parse("- Fine day$h and weird$zz token", Portraits, "Yuki", false);
        Assert.Equal("Fine day$h and weird token", parsed.DialogueLine);
    }

    [Fact]
    public void Drops_Metadata_Like_Lines()
    {
        Assert.True(ResponseParser.IsMetadataLine("\"rapportDelta\": 2,"));
        Assert.True(ResponseParser.IsMetadataLine("{endConversation: true"));
        Assert.False(ResponseParser.IsMetadataLine("- normal line"));
    }

    [Fact]
    public void Splits_Overlong_Segment_At_Sentence_Boundaries()
    {
        string sentence = new string('x', 120) + ". ";
        string raw = "- " + sentence + sentence + sentence;
        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", false);

        Assert.True(parsed.Success);
        Assert.Contains("#$b#", parsed.DialogueLine);
        Assert.All(parsed.DialogueLine.Split("#$b#"), segment => Assert.True(segment.Length <= ResponseParser.MaxSegmentLength));
    }

    [Fact]
    public void Rejects_Unsplittable_Overlong_Segment()
    {
        string raw = "- " + new string('x', 260);
        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", false);
        Assert.False(parsed.Success);
        Assert.Equal("overlong-segment", parsed.FailureReason);
    }

    [Fact]
    public void Option_Rules_Length_And_Prefix_Stripping()
    {
        Assert.Null(ResponseParser.TryParseOption("%" + new string('y', 95), "Yuki", false));
        Assert.Equal("Tell me more", ResponseParser.TryParseOption("Option 1: Tell me more", "Yuki", false));
        Assert.Null(ResponseParser.TryParseOption("- another dialogue line", "Yuki", false));
        Assert.Null(ResponseParser.TryParseOption("### heading", "Yuki", false));
        Assert.Equal("Yuki will help", ResponseParser.TryParseOption("%@ will help", "Yuki", false));
    }

    [Fact]
    public void FixPunctuation_Appends_Sentence_End_Before_Portrait()
    {
        string fixedText = ResponseParser.FixSegmentPunctuation("你好呀$h", Portraits);
        Assert.Equal("你好呀。$h", fixedText);
    }
}

[Collection("LlmLayer")]
public class ResponseFormatterTests
{
    [Fact]
    public void Builds_Menu_With_Contract_Ids_And_Keys()
    {
        ResponseFormatter.ResetQuestionIdForTests();
        string formatted = ResponseFormatter.BuildFormattedLine(
            "Hello.",
            new[] { "Hi!", "Bye." },
            typedResponses: "With Generated",
            endConversation: false,
            allowTypedFallback: true,
            dontSkipNext: false);

        Assert.StartsWith(EngineConstants.SkipPrefix + "Hello.", formatted);
        Assert.Matches(new Regex(@"#\$q 2000\d+ SLD_Default#"), formatted);
        Assert.Contains("#$r -999999 0 SLD_Silent#", formatted);
        Assert.Contains("#$r -999998 0 SLD_Next#Hi!", formatted);
        Assert.Contains("#$r -999998 0 SLD_Next#Bye.", formatted);
        Assert.Contains("#$r -999997 0 SLD_TypedResponse#", formatted);
    }

    [Fact]
    public void No_Menu_When_Conversation_Ended_Or_Single_Line()
    {
        string ended = ResponseFormatter.BuildFormattedLine("Bye.", new[] { "x" }, "With Generated", true, false, true);
        Assert.Equal("Bye.", ended);

        string single = ResponseFormatter.BuildFormattedLine("Hm.", Array.Empty<string>(), "With Generated", false, false, true);
        Assert.Equal("Hm.", single);
    }

    [Fact]
    public void TypedResponses_Never_Omits_Typed_Entry()
    {
        string formatted = ResponseFormatter.BuildFormattedLine("Hello.", new[] { "Hi" }, "Never", false, false, true);
        Assert.DoesNotContain("SLD_TypedResponse", formatted);
    }

    [Fact]
    public void Streaming_Options_Follow_Spec()
    {
        Assert.Empty(ResponseFormatter.BuildStreamingOptions(new[] { "a" }, "With Generated", endConversation: true));
        Assert.Empty(ResponseFormatter.BuildStreamingOptions(Array.Empty<string>(), "With Generated", false));

        var options = ResponseFormatter.BuildStreamingOptions(new[] { "a", "b" }, "With Generated", false);
        Assert.Equal(StreamingResponseOptionKind.Silent, options[0].Kind);
        Assert.Equal(2, options.Count(option => option.Kind == StreamingResponseOptionKind.Generated));
        Assert.Equal(StreamingResponseOptionKind.Typed, options[^1].Kind);

        var never = ResponseFormatter.BuildStreamingOptions(new[] { "a" }, "Never", false);
        Assert.DoesNotContain(never, option => option.Kind == StreamingResponseOptionKind.Typed);

        var alwaysSingle = ResponseFormatter.BuildStreamingOptions(Array.Empty<string>(), "Always", false);
        Assert.Contains(alwaysSingle, option => option.Kind == StreamingResponseOptionKind.Typed);
    }
}
