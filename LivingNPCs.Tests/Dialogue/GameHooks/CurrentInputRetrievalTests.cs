using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.GameHooks;
using LivingNPCs.Dialogue.Ui;
using StardewValley;

namespace LivingNPCs.Tests.Dialogue.GameHooks;

[Collection("LlmLayer")]
public sealed class CurrentInputRetrievalTests : IDisposable
{
    private readonly Func<NPC, string, string>? oldConversation = GenerationRequests.ConversationContextProvider;
    private readonly Func<NPC, GenerationContentSnapshot?>? oldContent = GenerationRequests.ContentSnapshotProvider;
    private readonly Func<Func<bool, WorldRetrievalQuery, WorldRetrievalResult>>? oldWorld = GenerationRequests.WorldContextProvider;

    public CurrentInputRetrievalTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        GenerationRequests.ContentSnapshotProvider = null;
        GenerationRequests.WorldContextProvider = null;
        TypedInputRequestQueue.ResetForTests();
    }

    public void Dispose()
    {
        GenerationRequests.ConversationContextProvider = this.oldConversation;
        GenerationRequests.ContentSnapshotProvider = this.oldContent;
        GenerationRequests.WorldContextProvider = this.oldWorld;
        TypedInputRequestQueue.ResetForTests();
    }

    [Fact]
    public void SubmittedTextReachesBehaviorRecallDespiteOlderConversationTopic()
    {
        var npc = new NPC { Name = "Penny", displayName = "Penny" };
        const string input = "还记得我们在海滩钓鱼的那次吗？";
        string? recallQuery = null;
        GenerationRequest? generated = null;
        GenerationRequests.ConversationContextProvider = (_, text) =>
        {
            recallQuery = text;
            return "selected fishing memory";
        };
        TypedInputRequestQueue.EnqueueForTests = (_, request) => { generated = request; return true; };
        TypedInputRequestQueue.FriendshipGranterForTests = _ => { };
        var previous = new[] { new ConversationTurn("We talked about the library.", false, "old") };

        TypedInputRequestQueue.HandleSubmitted(new TypedInputRequest("prompt", npc, "default", previous), input);

        Assert.Equal(input, recallQuery);
        Assert.NotNull(generated);
        Assert.Equal(input, generated!.CurrentPlayerText);
        Assert.Equal("selected fishing memory", generated.BehaviorContext);
        Assert.Equal(input, generated.Conversation[^1].Text);
    }

    [Theory]
    [InlineData("Let's go to the beach.")]
    [InlineData("我们去图书馆吧。")]
    [InlineData("")]
    public void ExplicitOptionInputWinsOverAnyEarlierPlayerLine(string input)
    {
        string? query = null;
        GenerationRequests.ConversationContextProvider = (_, text) => { query = text; return string.Empty; };
        var npc = new NPC { Name = "Penny", displayName = "Penny" };
        var previous = new[] { new ConversationTurn("Old question about mining.", true, "old") };

        GenerationRequest request = GenerationRequests.BuildConversation(npc, "default", previous, input);

        Assert.Equal(input, query);
        Assert.Equal(input, request.CurrentPlayerText);
    }

    [Fact]
    public void ScheduledDialogueDoesNotTreatOriginalNpcLineAsPlayerQuery()
    {
        string? query = null;
        GenerationRequests.ConversationContextProvider = (_, text) => { query = text; return string.Empty; };

        GenerationRequest request = GenerationRequests.BuildScheduled(
            new NPC { Name = "Penny", displayName = "Penny" }, "spring_Mon", "The beach is quiet.");

        Assert.Empty(query!);
        Assert.Empty(request.CurrentPlayerText);
    }

    [Theory]
    [InlineData("Tell me about Torts.")]
    [InlineData(RsvAiPolicy.WithheldPlayerMessage)]
    public void BlockedNewInputDoesNotFallBackToAnEarlierQuestion(string input)
    {
        string? query = null;
        GenerationRequests.ConversationContextProvider = (_, text) => { query = text; return string.Empty; };

        var request = GenerationRequests.BuildConversation(
            new NPC { Name = "Penny", displayName = "Penny" }, "default",
            new[] { new ConversationTurn("Tell me about the library.", true, "old") }, input);

        Assert.Empty(query!);
        WorldRetrievalQuery worldQuery = WorldRetrievalQueryFactory.Create(request, request.Conversation, "Penny");
        Assert.Empty(worldQuery.PlayerText);
        Assert.Empty(worldQuery.RecentDialogue);
    }

    [Fact]
    public void WorldCaptureRefreshesPerRequestAndSurvivesPortraitCaptureFailure()
    {
        var npc = new NPC { Name = "Penny", displayName = "Penny" };
        GenerationRequests.ConversationContextProvider = null;
        GenerationRequests.ContentSnapshotProvider = _ => throw new InvalidOperationException("portrait unavailable");
        int captures = 0;
        GenerationRequests.WorldContextProvider = () =>
        {
            int capturedVersion = ++captures;
            return (_, _) => new WorldRetrievalResult { CoreText = $"world version {capturedVersion}" };
        };

        var first = GenerationRequests.BuildConversation(npc, "default", Array.Empty<ConversationTurn>(), "hello");
        var second = GenerationRequests.BuildConversation(npc, "default", Array.Empty<ConversationTurn>(), "hello again");

        Assert.Equal(2, captures);
        Assert.Null(first.ContentSnapshot);
        Assert.True(first.UsesCapturedWorldContext);
        Assert.Equal("world version 1", first.WorldContextRetriever!(false, new WorldRetrievalQuery()).CoreText);
        Assert.Equal("world version 2", second.WorldContextRetriever!(false, new WorldRetrievalQuery()).CoreText);
    }

    [Fact]
    public void FailedWorldCaptureStillMarksTheRequestAsRuntimeCaptured()
    {
        GenerationRequests.ConversationContextProvider = null;
        GenerationRequests.WorldContextProvider = () => throw new InvalidOperationException("world unavailable");

        var request = GenerationRequests.BuildConversation(
            new NPC { Name = "Penny", displayName = "Penny" }, "default", Array.Empty<ConversationTurn>(), "hello");

        Assert.True(request.UsesCapturedWorldContext);
        Assert.Null(request.WorldContextRetriever);
    }
}
