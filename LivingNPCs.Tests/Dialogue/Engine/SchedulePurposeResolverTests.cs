using LivingNPCs.Dialogue.Engine;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class SchedulePurposeResolverTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void ExplicitDesertFestivalMarkerIsConfirmed(int day)
    {
        SchedulePurposeResult result = SchedulePurposeResolver.Resolve(
            "Desert",
            "",
            "Strings\\schedules\\Sophia:DesertFestival_Sophia",
            0,
            day);

        Assert.Equal(SchedulePurposeKind.AttendDesertFestival, result.Kind);
        Assert.True(result.Confirmed);
        Assert.Equal("schedule-festival-marker", result.Source);
    }

    [Fact]
    public void DesertDateWithoutFestivalMarkerIsNotEnough()
    {
        SchedulePurposeResult result = SchedulePurposeResolver.Resolve("Desert", "", "", 0, 15);

        Assert.Equal(SchedulePurposeKind.None, result.Kind);
        Assert.False(result.Confirmed);
    }

    [Theory]
    [InlineData("Sophia_Inspect_Keg_Right", (int)SchedulePurposeKind.InspectKegs)]
    [InlineData("Sophia_Farm2", (int)SchedulePurposeKind.Work)]
    [InlineData("Sophia_Stretch_After_Work", (int)SchedulePurposeKind.Exercise)]
    [InlineData("Sophia_Phone", (int)SchedulePurposeKind.UsePhone)]
    [InlineData("Sophia_Read", (int)SchedulePurposeKind.Read)]
    [InlineData("Claire_Stocking", (int)SchedulePurposeKind.Work)]
    [InlineData("Susan_Craft", (int)SchedulePurposeKind.Work)]
    [InlineData("Morgan_Meditate", (int)SchedulePurposeKind.Meditate)]
    [InlineData("Victor_Wine2", (int)SchedulePurposeKind.Drink)]
    public void StableBehaviorTokensMapToConfirmedPurpose(string behavior, int expected)
    {
        SchedulePurposeResult result = SchedulePurposeResolver.Resolve("", behavior, "", 1, 5);

        Assert.Equal((SchedulePurposeKind)expected, result.Kind);
        Assert.True(result.Confirmed);
        Assert.Equal("schedule-behavior", result.Source);
    }

    [Theory]
    [InlineData("already")]
    [InlineData("flowerbed")]
    [InlineData("homework")]
    [InlineData("selfish")]
    public void PartialWordsDoNotProduceFalsePurpose(string behavior)
    {
        Assert.Equal(
            SchedulePurposeKind.None,
            SchedulePurposeResolver.Resolve("", behavior, "", 1, 5).Kind);
    }

    [Fact]
    public void NaturalLanguageMessageDoesNotBecomeConfirmedActivity()
    {
        SchedulePurposeResult result = SchedulePurposeResolver.Resolve(
            "Town",
            "",
            "I already finished my homework and read a book.",
            1,
            5);

        Assert.Equal(SchedulePurposeKind.None, result.Kind);
        Assert.False(result.Confirmed);
    }
}