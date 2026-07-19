using System;
using System.Collections.Generic;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Content;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

public sealed class RsvRemainingIngressTests
{
    [Theory]
    [InlineData("RSVFayeMom")]
    [InlineData("RSVAntonFix")]
    [InlineData("RSVtent")]
    [InlineData("rsvSpirit1")]
    [InlineData("RSVProtest")]
    public void StructuredRsvNpcInternalNamesAreBlocked(string npcName)
    {
        Assert.True(RsvAiPolicy.IsBlockedNpcName(npcName));
    }

    [Fact]
    public void OrdinaryRsvpAcronymIsNotTreatedAsAnRsvIdentity()
    {
        Assert.False(RsvAiPolicy.IsBlockedNpcName("RSVP"));
        Assert.False(RsvAiPolicy.IsBlockedDialogueKey("RSVP_invitation"));
    }

    [Theory]
    [InlineData("RSVFayeMom_intro")]
    [InlineData("M_RSVAntonFix_reaction")]
    [InlineData("event/RSVtent")]
    public void DialogueKeysContainingStructuredRsvNpcNamesAreBlocked(string key)
    {
        Assert.True(RsvAiPolicy.IsBlockedDialogueKey(key));
    }

    [Theory]
    [InlineData("RSVFayeMom appeared in an old memory.")]
    [InlineData("speaker: RSVAntonFix")]
    [InlineData("event/RSVtent happened")]
    public void FreeTextContainingStructuredRsvIdentifiersIsBlocked(string text)
    {
        Assert.True(RsvAiPolicy.ContainsBlockedReference(text));
        Assert.Equal(string.Empty, RsvAiPolicy.RemoveBlockedLines(text));
    }

    [Theory]
    [InlineData("Please RSVP for the invitation.")]
    [InlineData("RSVP_invitation remains an ordinary key.")]
    [InlineData("notRSVFayeMom is not a standalone identifier.")]
    public void FreeTextDoesNotTreatEmbeddedOrRsvpTextAsRsvIdentifiers(string text)
    {
        Assert.False(RsvAiPolicy.ContainsBlockedReference(text));
        Assert.Equal(text, RsvAiPolicy.RemoveBlockedLines(text));
    }

    [Fact]
    public void DialogueSampleLoaderRejectsRsvKeysAndTextAtMergeBoundary()
    {
        var bio = new NpcBio
        {
            Dialogue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mon"] = "A quiet morning on the farm.",
                ["event_postweddingreception"] = "What a lovely gathering.",
                ["Tue"] = "Torts is visiting today.",
                ["M_RSVFayeMom_reaction"] = "This line looks generic."
            }
        };

        Dictionary<string, string> samples = DialogueSampleLoader.GetSamples(
            npc: null,
            bio,
            expansionCompatible: true);

        Assert.Single(samples);
        Assert.Equal("A quiet morning on the farm.", samples["Mon"]);
    }

    [Theory]
    [InlineData("Rafseazz.RSVCP_Aurorean_Iris")]
    [InlineData("RSV_Obelisk")]
    [InlineData("RSV Obelisk")]
    public void WorldProgressionExcludesRsvFarmContentFromAggregates(string contentId)
    {
        Assert.False(WorldProgression.IsAllowedFarmAggregateContent(contentId));
    }

    [Theory]
    [InlineData("Barn")]
    [InlineData("White Chicken")]
    [InlineData("(O)24")]
    public void WorldProgressionKeepsOrdinaryFarmContentInAggregates(string contentId)
    {
        Assert.True(WorldProgression.IsAllowedFarmAggregateContent(contentId));
    }
}
