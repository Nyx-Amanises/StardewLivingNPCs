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
        "0", "h", "s", "l", "a", "u"
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
    public void Drops_Entire_PrettyPrinted_Metadata_Tail_From_Options()
    {
        string raw = """
            - 其实，我给你带了一份黑莓脆皮馅饼。$h
            % 谢谢你，这真贴心。
            % 只要是你做的，我都喜欢。
            % 我们可以一起分着吃。

            !LivingNpcs_Meta
            {
              "actions": [
                {
                  "type": "give_small_gift",
                  "itemId": "(O)611",
                  "itemLabel": "黑莓脆皮馅饼"
                }
              ]
            }
            """;

        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", fixPunctuation: true);

        Assert.True(parsed.Success);
        Assert.Equal(3, parsed.Options.Count);
        Assert.Equal("谢谢你，这真贴心。", parsed.Options[0]);
        Assert.DoesNotContain(parsed.Options, option => option.Contains("give_small_gift", StringComparison.Ordinal));
        Assert.DoesNotContain(parsed.Options, option => option.Contains("itemId", StringComparison.Ordinal));
        Assert.DoesNotContain(parsed.Options, option => option.Contains("itemLabel", StringComparison.Ordinal));
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
        var parsed = ResponseParser.Parse("- Fine day$h and weird$zz token with stray$c command", Portraits, "Yuki", false);
        Assert.Equal("Fine day and weird token with stray command$h", parsed.DialogueLine);
    }

    [Fact]
    public void Removes_Unavailable_U_Portrait_From_LegacyOutput()
    {
        var portraitsWithoutU = new HashSet<string>(Portraits, StringComparer.OrdinalIgnoreCase);
        portraitsWithoutU.Remove("u");

        var parsed = ResponseParser.Parse("- 谢谢你……$u", portraitsWithoutU, "Yuki", false);

        Assert.True(parsed.Success);
        Assert.Equal("谢谢你……$0", parsed.DialogueLine);
        Assert.DoesNotContain("$u", parsed.DialogueLine, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableStandardExpressionsFallBackToNeutralPerPage()
    {
        var onlyNeutralAndHappy = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0", "h" };

        var parsed = ResponseParser.Parse(
            "- sad$s#$b#unique$u#$b#angry$a#$b#happy$h",
            onlyNeutralAndHappy,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("sad$0#$b#unique$0#$b#angry$0#$b#happy$h", parsed.DialogueLine);
    }

    [Fact]
    public void NumericOneThroughFiveNeverAliasStandardExpressions()
    {
        var parsed = ResponseParser.Parse(
            "- Price one$1#$b#Price five$5",
            Portraits,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("Price one#$b#Price five", parsed.DialogueLine);
        Assert.DoesNotContain("$h", parsed.DialogueLine, StringComparison.Ordinal);
        Assert.DoesNotContain("$a", parsed.DialogueLine, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnsupportedConfiguredLetters_AndNormalizesBuiltInCase()
    {
        var configured = new HashSet<string>(Portraits, StringComparer.OrdinalIgnoreCase)
        {
            "7", "11", "x", "smile"
        };

        var parsed = ResponseParser.Parse(
            "- odd$x word$smile numeric$7 alternate$11 happy$H",
            configured,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("odd word numeric alternate happy$h", parsed.DialogueLine);
        Assert.DoesNotContain("$x", parsed.DialogueLine, StringComparison.Ordinal);
        Assert.DoesNotContain("$smile", parsed.DialogueLine, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesExplicitNumericPortraitsOnSeparatePages()
    {
        var configured = new HashSet<string>(Portraits, StringComparer.OrdinalIgnoreCase) { "7", "11" };

        var parsed = ResponseParser.Parse(
            "- numeric$7#$b#alternate$11",
            configured,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("numeric$7#$b#alternate$11", parsed.DialogueLine);
    }

    [Fact]
    public void KeepsTheLastAvailableExpressionIndependentlyOnEveryPage()
    {
        var configured = new HashSet<string>(Portraits, StringComparer.OrdinalIgnoreCase) { "7" };

        var parsed = ResponseParser.Parse(
            "- First$h then$7#$b#Second$a then$s#$b#No expression",
            configured,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("First then$7#$b#Second then$s#$b#No expression", parsed.DialogueLine);
    }

    [Fact]
    public void Keeps_Only_Last_Portrait_Per_Page_And_Removes_Bare_Dollars()
    {
        string raw = "- 早上好，@。$l你起得真早$……明天呢？$u#$b#第二页$a还在继续#$e#最后$h一句$0仍继续";
        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal(
            "早上好，@。你起得真早……明天呢？$u#$b#第二页还在继续$a#$e#最后一句仍继续$0",
            parsed.DialogueLine);
    }

    [Fact]
    public void NormalizesUppercasePageCommandsWithoutLeavingBareHashes()
    {
        var parsed = ResponseParser.Parse(
            "- First$h#$B#Second$s#$E#Last$u",
            Portraits,
            "Yuki",
            fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal("First$h#$b#Second$s#$e#Last$u", parsed.DialogueLine);
    }

    [Fact]
    public void Drops_Metadata_Like_Lines()
    {
        Assert.True(ResponseParser.IsMetadataLine("\"rapportDelta\": 2,"));
        Assert.True(ResponseParser.IsMetadataLine("{endConversation: true"));
        Assert.True(ResponseParser.IsMetadataLine("type\":\"give_small_gift\","));
        Assert.True(ResponseParser.IsMetadataLine("itemId\":\"(O)611\","));
        Assert.True(ResponseParser.IsMetadataLine("itemLabel\":\"Blackberry Cobbler\""));
        Assert.True(ResponseParser.IsMetadataLine("]."));
        Assert.False(ResponseParser.IsMetadataLine("- normal line"));
    }

    [Fact]
    public void PrettyPrinted_Or_Malformed_Metadata_Never_Becomes_Options()
    {
        string raw = """
            - 谢谢你，潘妮，这真贴心。$h
            % 保持沉默
            % 谢谢你，潘妮，这真贴心。
            !LIVINGNPCS_META {
              "actions": [{
                type":"give_small_gift",
                itemId":"(O)611",
                itemLabel":"黑莓脆皮馅饼"
              }]
            }
            """;

        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", fixPunctuation: false);

        Assert.True(parsed.Success);
        Assert.Equal(2, parsed.Options.Count);
        Assert.DoesNotContain(parsed.Options, option => option.Contains("give_small_gift", StringComparison.Ordinal));
        Assert.DoesNotContain(parsed.Options, option => option.Contains("itemId", StringComparison.Ordinal));
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
    public void AutoSplitPagesInheritTheOriginalPortraitMarker()
    {
        string sentence = new string('x', 90) + ". ";
        var parsed = ResponseParser.Parse("- " + sentence + sentence + sentence + "$a", Portraits, "Yuki", false);

        Assert.True(parsed.Success);
        string[] pages = parsed.DialogueLine.Split("#$b#");
        Assert.True(pages.Length > 1);
        Assert.All(pages, page => Assert.EndsWith("$a", page, StringComparison.Ordinal));
        Assert.All(pages, page => Assert.True(page.Length <= ResponseParser.MaxSegmentLength));
    }

    [Fact]
    public void LongSegmentSplitPreservesExplicitEndPageBoundary()
    {
        string sentence = new string('x', 90) + ". ";
        var parsed = ResponseParser.Parse(
            "- " + sentence + sentence + sentence + "$h#$e#Final page.$s",
            Portraits,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Contains("#$e#Final page.$s", parsed.DialogueLine, StringComparison.Ordinal);
        Assert.All(
            parsed.DialogueLine.Split(new[] { "#$b#", "#$e#" }, StringSplitOptions.None),
            page => Assert.True(page.Length <= ResponseParser.MaxSegmentLength));
    }

    [Fact]
    public void NamedEmotionLabelsAreRemovedWithoutCreatingExtraPages()
    {
        var parsed = ResponseParser.Parse(
            "- I am $blushing and $embarrassed about that.",
            Portraits,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("I am  and  about that.", parsed.DialogueLine);
        Assert.DoesNotContain("#$b#", parsed.DialogueLine, StringComparison.Ordinal);
        Assert.DoesNotContain("#$e#", parsed.DialogueLine, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("- Here you go[(O)395]$h", "Here you go$h")]
    [InlineData("- Here you go[395]$h", "Here you go$h")]
    [InlineData("- Please do not show [stage directions].$s", "Please do not show .$s")]
    public void RemovesModelSuppliedBracketCommands(string raw, string expected)
    {
        var parsed = ResponseParser.Parse(raw, Portraits, "Yuki", false);

        Assert.True(parsed.Success);
        Assert.Equal(expected, parsed.DialogueLine);
    }

    [Fact]
    public void TrimsRepeatedMixedCasePageMarkersAtBothEnds()
    {
        var parsed = ResponseParser.Parse(
            "- #$B##$e#Actual line$h#$E##$b#",
            Portraits,
            "Yuki",
            false);

        Assert.True(parsed.Success);
        Assert.Equal("Actual line$h", parsed.DialogueLine);
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
        Assert.Equal("想说和符号", ResponseParser.TryParseOption("%想说$h和$符号", "Yuki", false));
    }

    [Fact]
    public void FixPunctuation_Appends_Sentence_End_Before_Portrait()
    {
        string fixedText = ResponseParser.FixSegmentPunctuation("你好呀$h", Portraits);
        Assert.Equal("你好呀。$h", fixedText);
    }

    [Fact]
    public void FixPunctuationHandlesBothPageBoundaryCommands()
    {
        string fixedText = ResponseParser.FixSegmentPunctuation(
            "First$h#$e#Second$s#$b#Third$a",
            Portraits);

        Assert.Equal("First.$h#$e#Second.$s#$b#Third.$a", fixedText);
    }

    [Fact]
    public void PresentationFallbackNeutralizesOnlyPortraitTokens()
    {
        string formatted =
            "Warm$h#$b#Custom$7#$e#Neutral$0"
            + "#$q 20001 SLD_Default#Respond"
            + "#$r -999998 0 SLD_Next#Answer"
            + "[(O)395]";

        string safe = ResponseParser.DowngradePortraitMarkersToNeutral(formatted);

        Assert.Equal(
            "Warm$0#$b#Custom$0#$e#Neutral$0"
            + "#$q 20001 SLD_Default#Respond"
            + "#$r -999998 0 SLD_Next#Answer"
            + "[(O)395]",
            safe);
    }
}

public class StreamingDialoguePreviewTests
{
    [Fact]
    public void RawStreamingPreviewDoesNotTrustPortraitMarkers()
    {
        string preview = StreamingDialoguePreview.ExtractVisibleText("- Hello$h#$b#Still streaming$7");
        var segments = StreamingDialoguePreview.PrepareDisplaySegments(preview);

        Assert.Equal(new[] { "Hello", "Still streaming" }, segments.Select(segment => segment.Text));
        Assert.All(segments, segment => Assert.Equal(0, segment.PortraitFrameIndex));
    }

    [Fact]
    public void PrepareDisplayText_Removes_All_Dollar_Signs_After_Converting_Page_Markers()
    {
        string display = StreamingDialoguePreview.PrepareDisplayText("- Hello$l world$#$b#Next$u#$e#Last$");

        Assert.Equal("Hello world\fNext\fLast", display);
        Assert.DoesNotContain('$', display);
    }

    [Fact]
    public void PrepareDisplaySegmentsKeepsValidatedPortraitPerPage()
    {
        var segments = StreamingDialoguePreview.PrepareDisplaySegments(
            "- Warm$h#$b#Private$7#$e#Unknown$zz");

        Assert.Equal(new[] { "Warm", "Private", "Unknown" }, segments.Select(segment => segment.Text));
        Assert.Equal(new[] { 1, 7, 0 }, segments.Select(segment => segment.PortraitFrameIndex));
        Assert.DoesNotContain(segments, segment => segment.Text.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("A gift for you.$h[(O)395]")]
    [InlineData("A gift for you.$h[395]")]
    public void FinalDisplayHidesTrailingItemCommands(string dialogue)
    {
        var segments = StreamingDialoguePreview.PrepareDisplaySegments(dialogue);

        var segment = Assert.Single(segments);
        Assert.Equal("A gift for you.", segment.Text);
        Assert.Equal(1, segment.PortraitFrameIndex);
    }

    [Theory]
    [InlineData("h", 1)]
    [InlineData("7", 7)]
    [InlineData("0", 0)]
    [InlineData("1", 0)]
    [InlineData("4096", 0)]
    [InlineData("not-a-marker", 0)]
    public void InvalidStreamingPortraitMarkersFallbackToNeutral(string marker, int expectedFrame)
    {
        Assert.Equal(expectedFrame, StreamingDialoguePreview.ResolvePortraitFrameIndex(marker));
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

public class SpouseGiftPolicyTests
{
    [Fact]
    public void ParseCandidateIds_Keeps_Items_Drops_Categories()
    {
        var ids = SpouseGiftPolicy.ParseCandidateIds("66 128 -80", "(O)174 -4 216");
        Assert.Equal(new[] { "66", "128", "(O)174", "216" }, ids);
    }

    [Fact]
    public void TryPick_Respects_Probability_And_Pool()
    {
        // 命中概率的种子：找一个 Next(20)==0 的种子。
        int luckySeed = Enumerable.Range(0, 1000).First(seed => new Random(seed).Next(SpouseGiftPolicy.OneInChance) == 0);
        string? picked = SpouseGiftPolicy.TryPick("66 72", "", new Random(luckySeed));
        Assert.Contains(picked, new[] { "66", "72" });

        int unluckySeed = Enumerable.Range(0, 1000).First(seed => new Random(seed).Next(SpouseGiftPolicy.OneInChance) != 0);
        Assert.Null(SpouseGiftPolicy.TryPick("66 72", "", new Random(unluckySeed)));

        Assert.Null(SpouseGiftPolicy.TryPick("-80", "-4", new Random(luckySeed)));
    }
}
