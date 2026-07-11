using StardewValley;
using StardewValley.Characters;
using System.Runtime.CompilerServices;
using Xunit;

namespace LivingNPCs.Tests;

public sealed class NpcInteractionEligibilityTests
{
    [Fact]
    public void IsEligible_AllowsVillagerNpc()
    {
        Assert.True(NpcInteractionEligibility.IsEligible(new NPC()));
    }

    [Fact]
    public void IsEligible_RejectsPet()
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        Assert.False(NpcInteractionEligibility.IsEligible(pet));
    }

    [Fact]
    public void IsEligible_RejectsHorse()
    {
        var horse = (Horse)RuntimeHelpers.GetUninitializedObject(typeof(Horse));
        Assert.False(NpcInteractionEligibility.IsEligible(horse));
    }

    [Fact]
    public void IsEligible_RejectsNull()
    {
        Assert.False(NpcInteractionEligibility.IsEligible(null));
    }
}
