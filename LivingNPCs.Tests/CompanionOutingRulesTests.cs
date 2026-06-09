using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class CompanionOutingRulesTests
{
    [Theory]
    [InlineData("companion_outing", "companion_outing")]
    [InlineData("escort_to_location", "companion_outing")]
    [InlineData("walk_together", "none")]
    public void WorldActionNormalizationMigratesOnlyDestinationOutings(string input, string expected)
    {
        Assert.Equal(expected, BehaviorValueNormalizer.NormalizeWorldActionType(input));
    }

    [Fact]
    public void MetadataParserEnforcesFiveHourMinimumForOutings()
    {
        var analysis = ValleyTalkExchangeParser.Parse(
            """
            {
              "actions": [
                {
                  "type": "companion_outing",
                  "targetLocation": "Beach",
                  "durationMinutes": 15
                }
              ]
            }
            """
        );

        var action = Assert.Single(analysis.Actions);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal(300, action.DurationMinutes);
    }

    [Theory]
    [InlineData(1700, 300, true)]
    [InlineData(1800, 300, true)]
    [InlineData(1810, 300, false)]
    [InlineData(1900, 300, false)]
    public void OutingMustFitTravelAndFiveHourStay(int startTime, int stayMinutes, bool expected)
    {
        Assert.Equal(expected, CompanionOutingRules.CanFitMinimumStay(startTime, stayMinutes));
    }

    [Fact]
    public void StableAnchorChoiceIsDeterministicAndLimitedToTopThree()
    {
        int first = CompanionOutingRules.SelectStableTopCandidateIndex("Penny", "SeedShop", 42, 12);
        int second = CompanionOutingRules.SelectStableTopCandidateIndex("Penny", "SeedShop", 42, 12);

        Assert.Equal(first, second);
        Assert.InRange(first, 0, 2);
    }

    [Theory]
    [InlineData("SeedShop", "Let's browse the shop.", "browse")]
    [InlineData("Beach", "Let's look at the scenery.", "scenic")]
    [InlineData("Saloon", "Let's spend some time there.", "social")]
    [InlineData("Beach", "我们去看浪吧。", "scenic")]
    [InlineData("ArchaeologyHouse", "去图书馆翻书。", "browse")]
    [InlineData("Saloon", "靠吧台聊一会。", "social")]
    [InlineData("Mountain", "在山湖边聊天。", "quiet")]
    [InlineData("FlowerDance", "沿着花舞节边缘散步。", "festival")]
    public void ActivityStyleReflectsDestinationAndConversation(string target, string reason, string expected)
    {
        Assert.Equal(expected, CompanionOutingRules.DetermineActivityStyle(target, reason));
    }

    [Fact]
    public void TimeMathTracksFiveGameHours()
    {
        Assert.Equal(1820, BehaviorTimeMath.AddMinutesToTime(1320, 300));
        Assert.Equal(300, BehaviorTimeMath.GetElapsedMinutes(1320, 1820));
    }
}
