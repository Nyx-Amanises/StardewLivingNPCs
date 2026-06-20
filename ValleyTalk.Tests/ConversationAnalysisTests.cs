using ValleyTalk;
using Xunit;

namespace ValleyTalk.Tests;

public sealed class ConversationAnalysisTests
{
    [Fact]
    public void EmptyParseResultsDoNotShareMutableMetadata()
    {
        var firstEmpty = ConversationAnalysis.Parse(string.Empty);
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"actions":[{"type":"companion_outing","targetLocation":"Beach","travelConsent":"accepted_now","durationMinutes":60}]}
        """);

        Assert.True(firstEmpty.MergeSupplementalActionMetadata(supplemental));

        var secondEmpty = ConversationAnalysis.Parse("plain dialogue with no metadata");

        Assert.Empty(secondEmpty.Actions);
        Assert.False(secondEmpty.HasWorldActionOrHelpMetadata);
    }
}
