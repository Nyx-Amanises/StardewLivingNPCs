using LivingNPCs.Dialogue.GameHooks;
using StardewValley;
using Xunit;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Tests.Dialogue.GameHooks;

public sealed class RsvDialogueKeyGuardTests
{
    [Theory]
    [InlineData("RSV_GH1")]
    [InlineData("Characters/Dialogue/Pam:RSV.OngoingSpiritRealm")]
    [InlineData("Rafseazz.RSVCP.Dialogue.Wedding")]
    [InlineData("Characters/Dialogue/Pam:Custom_Ridgeside_Ridge")]
    [InlineData("event_postweddingreception")]
    [InlineData("Characters/Dialogue/Pam:event_postweddingreception_4")]
    [InlineData("Characters/Dialogue/Penny:june_arrival")]
    [InlineData("daily_Torts_2")]
    [InlineData("Lidens_Return")]
    [InlineData("AntonLikesFarmer")]
    [InlineData("AntonLikesPaula")]
    [InlineData("PaulaLikesAnton")]
    [InlineData("PaulaLikesFarmer")]
    [InlineData("RSVFayeFasionShowInfo")]
    public void SharedPolicyBlocksRsvDialogueKeys(string dialogueKey)
    {
        Assert.True(global::LivingNPCs.RsvAiPolicy.IsBlockedDialogueKey(dialogueKey));
    }

    [Theory]
    [InlineData("spring_1")]
    [InlineData("Resort_Leaving")]
    [InlineData("inlaw_Abigail")]
    [InlineData("daily_TortsRealm_2")]
    [InlineData("RSVP_invitation")]
    public void SharedPolicyLeavesUnrelatedDialogueKeysAvailable(string dialogueKey)
    {
        Assert.False(global::LivingNPCs.RsvAiPolicy.IsBlockedDialogueKey(dialogueKey));
    }

    [Fact]
    public void PushTemporaryDialogueLeavesBlockedSourceKeyToVanilla()
    {
        var npc = new NPC();
        string translationKey = "Characters/Dialogue/Pam:event_postweddingreception";

        bool runOriginal = NPC_PushTemporaryDialogue_Patch.Prefix(npc, ref translationKey);

        Assert.True(runOriginal);
        Assert.Equal("Characters/Dialogue/Pam:event_postweddingreception", translationKey);
    }

    [Fact]
    public void RetrieveDialogueLeavesBlockedPrefaceToVanilla()
    {
        var npc = new NPC();
        GameDialogue result = null!;

        bool runOriginal = NPC_TryToRetrieveDialogue_Patch.Prefix(
            npc,
            "event_postweddingreception",
            heartLevel: 4,
            appendToEnd: string.Empty,
            ref result);

        Assert.True(runOriginal);
        Assert.Null(result);
    }

    [Fact]
    public void OuterDialoguePatchAndRecorderRecognizeEitherOriginalKeyField()
    {
        Assert.True(NPC_CheckForNewCurrentDialogue_Patch.HasBlockedSourceDialogueKey(
            "daily_Torts_2",
            "heart_4"));
        Assert.True(NPC_CheckForNewCurrentDialogue_Patch.HasBlockedSourceDialogueKey(
            null,
            "Characters/Dialogue/Pam:event_postweddingreception"));

        Assert.True(DisplayedDialogueRecorder.HasBlockedSourceDialogueKey(
            "RSV_GH1",
            null));
        Assert.True(DisplayedDialogueRecorder.HasBlockedSourceDialogueKey(
            null,
            "Rafseazz.RSVCP.Dialogue.Wedding"));
        Assert.False(DisplayedDialogueRecorder.HasBlockedSourceDialogueKey(
            "Resort_Leaving",
            "Characters/Dialogue/Abigail:Resort_Leaving"));
    }
}
