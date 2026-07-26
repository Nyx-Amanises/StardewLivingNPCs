using System.Collections.Generic;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Ui;
using StardewValley;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Ui;

/// <summary>
/// F11 修复验证：自由输入提交的顺序改为"先入队、成功才发好感"；
/// 调度器忙导致入队失败时不发好感、给玩家可见提示，文本不静默消失。
/// </summary>
[Collection("LlmLayer")]
public sealed class TypedInputRequestQueueBusyDropTests : System.IDisposable
{
    public TypedInputRequestQueueBusyDropTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        TypedInputRequestQueue.ResetForTests();
    }

    public void Dispose()
    {
        TypedInputRequestQueue.ResetForTests();
    }

    [Fact]
    public void Busy_Drop_Skips_Friendship_And_Notifies_The_Player()
    {
        var npc = new NPC();
        bool friendshipGranted = false;
        var hudMessages = new List<string>();
        TypedInputRequestQueue.FriendshipGranterForTests = _ => friendshipGranted = true;
        TypedInputRequestQueue.EnqueueForTests = (_, _) => false;
        TypedInputRequestQueue.HudNotifierForTests = hudMessages.Add;

        TypedInputRequestQueue.HandleSubmitted(new TypedInputRequest("p", npc, "default", null), "hello there");

        Assert.False(friendshipGranted);
        Assert.Single(hudMessages);
        Assert.False(string.IsNullOrWhiteSpace(hudMessages[0]));
    }

    [Fact]
    public void Successful_Enqueue_Grants_Friendship_Without_Notice()
    {
        var npc = new NPC();
        bool friendshipGranted = false;
        var hudMessages = new List<string>();
        TypedInputRequestQueue.FriendshipGranterForTests = _ => friendshipGranted = true;
        TypedInputRequestQueue.EnqueueForTests = (_, _) => true;
        TypedInputRequestQueue.HudNotifierForTests = hudMessages.Add;

        TypedInputRequestQueue.HandleSubmitted(new TypedInputRequest("p", npc, "default", null), "hello there");

        Assert.True(friendshipGranted);
        Assert.Empty(hudMessages);
    }

    [Fact]
    public void Blank_Submission_Still_Has_No_Side_Effects()
    {
        var npc = new NPC();
        bool friendshipGranted = false;
        bool enqueued = false;
        var hudMessages = new List<string>();
        TypedInputRequestQueue.FriendshipGranterForTests = _ => friendshipGranted = true;
        TypedInputRequestQueue.EnqueueForTests = (_, _) => enqueued = true;
        TypedInputRequestQueue.HudNotifierForTests = hudMessages.Add;

        TypedInputRequestQueue.HandleSubmitted(new TypedInputRequest("p", npc, "default", null), "   ");

        Assert.False(friendshipGranted);
        Assert.False(enqueued);
        Assert.Empty(hudMessages);
    }
}
