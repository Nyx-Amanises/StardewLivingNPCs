using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class NicknamePreferenceTests
{
    [Fact]
    public void AcceptedNicknameRequestImmediatelyUpdatesDedicatedState()
    {
        var state = new LivingNpcState();

        bool updated = NicknamePreferenceService.TryUpdateStateFromDialogue(
            state,
            "还有你以后叫我小刚就好了。",
            "小刚……这样叫起来还有点不习惯，不过我会记住的。",
            currentTotalDays: 15,
            currentTimeOfDay: 1420);

        Assert.True(updated);
        Assert.Equal("小刚", state.FarmerNickname);
        Assert.Equal("Accepted", state.FarmerNicknameStatus);
        Assert.Equal(15, state.FarmerNicknameTotalDays);
        Assert.Equal(1420, state.FarmerNicknameTimeOfDay);
    }

    [Fact]
    public void RecoversPennyNicknameFromExistingPlayerPreference()
    {
        var state = new LivingNpcState
        {
            PlayerPreferenceMemories =
            {
                new PlayerPreferenceFact
                {
                    PreferenceKind = "habit",
                    Subject = "称呼小刚",
                    Summary = "农夫希望潘妮以后叫她小刚",
                    Importance = 85,
                    CreatedTotalDays = 15,
                    CreatedTimeOfDay = 1420,
                    LastUpdatedTotalDays = 15,
                    LastUpdatedTimeOfDay = 1420
                }
            }
        };

        bool recovered = NicknamePreferenceService.RecoverStateFromStoredMemories(state);

        Assert.True(recovered);
        Assert.Equal("小刚", state.FarmerNickname);
        Assert.Equal("Accepted", state.FarmerNicknameStatus);
        Assert.Equal(15, state.FarmerNicknameTotalDays);
        Assert.Equal(1420, state.FarmerNicknameTimeOfDay);
    }

    [Fact]
    public void RecoversEnglishNicknameWithSpacingAndQuotes()
    {
        var state = new LivingNpcState
        {
            PlayerPreferenceMemories =
            {
                new PlayerPreferenceFact
                {
                    PreferenceKind = "habit",
                    Subject = "nickname: Star",
                    Summary = "The farmer prefers to be called \"Star\", and Penny accepted.",
                    Importance = 85,
                    LastUpdatedTotalDays = 18,
                    LastUpdatedTimeOfDay = 900
                }
            }
        };

        Assert.True(NicknamePreferenceService.RecoverStateFromStoredMemories(state));
        Assert.Equal("Star", state.FarmerNickname);
        Assert.Equal("Accepted", state.FarmerNicknameStatus);
    }
    [Fact]
    public void RecoveryDoesNotMistakeUnrelatedAddressingMemoryForNickname()
    {
        var state = new LivingNpcState
        {
            PlayerPreferenceMemories =
            {
                new PlayerPreferenceFact
                {
                    PreferenceKind = "habit",
                    Subject = "家庭称呼的敏感",
                    Summary = "小刚体贴地包容了潘妮在家庭称呼上的敏感",
                    Importance = 90,
                    LastUpdatedTotalDays = 20,
                    LastUpdatedTimeOfDay = 900
                }
            }
        };

        Assert.False(NicknamePreferenceService.RecoverStateFromStoredMemories(state));
        Assert.Equal(string.Empty, state.FarmerNickname);
    }

    [Fact]
    public void RecoveryNeverOverwritesExistingDedicatedNickname()
    {
        var state = new LivingNpcState
        {
            FarmerNickname = "阿星",
            FarmerNicknameStatus = "Accepted",
            PlayerPreferenceMemories =
            {
                new PlayerPreferenceFact
                {
                    PreferenceKind = "habit",
                    Subject = "称呼小刚",
                    Summary = "农夫希望潘妮以后叫她小刚",
                    Importance = 85,
                    LastUpdatedTotalDays = 15,
                    LastUpdatedTimeOfDay = 1420
                }
            }
        };

        Assert.False(NicknamePreferenceService.RecoverStateFromStoredMemories(state));
        Assert.Equal("阿星", state.FarmerNickname);
    }
}
