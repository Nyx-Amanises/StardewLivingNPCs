using System;
using System.Collections.Generic;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.GameHooks;

namespace LivingNPCs.Dialogue.Ui;

/// <summary>一次自由文本输入请求（WP12 §4.4）：提示语、NPC、对话键、可空的聊天历史。</summary>
internal sealed record TypedInputRequest(
    string Prompt,
    NPC Npc,
    string DialogueKey,
    IReadOnlyList<ConversationTurn>? Conversation);

/// <summary>
/// 自由文本输入请求队列（WP12 §4.4，替代旧 TextInputHandler）：同一时刻只保留最后一个请求；
/// UpdateTicked 泵在无打开菜单时消费 → 启动原生输入控制器。提交回调：空白仅重置状态；
/// 非空 → 文本作为玩家台词并入聊天历史 → 每日搭话好感 → 发起会话生成。
/// </summary>
internal static class TypedInputRequestQueue
{
    private static readonly object gate = new();
    private static TypedInputRequest? pending;

    /// <summary>测试注入：消费条件（null → Game1.activeClickableMenu == null）。</summary>
    internal static Func<bool>? CanConsumeForTests;

    /// <summary>测试注入：输入控制器启动（null → NativeDialogueTextInputController.Start）。</summary>
    internal static Action<string, NPC, Action<string>>? InputStarterForTests;

    /// <summary>测试注入：搭话好感（null → npc.grantConversationFriendship(Game1.player)）。</summary>
    internal static Action<NPC>? FriendshipGranterForTests;

    /// <summary>测试注入：生成入队（null → GenerationRequests.Enqueue）。</summary>
    internal static Func<NPC, GenerationRequest, bool>? EnqueueForTests;

    public static bool HasPending
    {
        get
        {
            lock (gate)
            {
                return pending != null;
            }
        }
    }

    internal static void ResetForTests()
    {
        Clear();
        CanConsumeForTests = null;
        InputStarterForTests = null;
        FriendshipGranterForTests = null;
        EnqueueForTests = null;
    }

    /// <summary>提交请求；覆盖之前尚未消费的请求（只保留最后一个）。</summary>
    public static void Submit(string prompt, NPC npc, string dialogueKey, IReadOnlyList<ConversationTurn>? conversation)
    {
        if (npc == null)
        {
            return;
        }

        lock (gate)
        {
            pending = new TypedInputRequest(prompt ?? string.Empty, npc, dialogueKey ?? string.Empty, conversation);
        }
    }

    /// <summary>存档作用域重置（换档/回标题时丢弃未消费请求）。</summary>
    public static void Clear()
    {
        lock (gate)
        {
            pending = null;
        }
    }

    /// <summary>UpdateTicked 泵：请求发起方已关闭当前对话，通常下一两帧即可打开输入框。</summary>
    public static void OnUpdateTicked()
    {
        TypedInputRequest? request;
        lock (gate)
        {
            if (pending == null || !CanConsume())
            {
                return;
            }

            request = pending;
            pending = null;
        }

        StartInput(request!);
    }

    private static bool CanConsume()
    {
        Func<bool>? probe = CanConsumeForTests;
        if (probe != null)
        {
            return probe();
        }

        try
        {
            return Game1.activeClickableMenu == null;
        }
        catch
        {
            return false;
        }
    }

    private static void StartInput(TypedInputRequest request)
    {
        Action<string, NPC, Action<string>>? starter = InputStarterForTests;
        if (starter != null)
        {
            starter(request.Prompt, request.Npc, text => HandleSubmitted(request, text));
            return;
        }

        NativeDialogueTextInputController.Start(request.Prompt, request.Npc, text => HandleSubmitted(request, text));
    }

    /// <summary>提交回调（§4.4）：空白 → 无副作用；非空 → 好感 + 会话生成。</summary>
    internal static void HandleSubmitted(TypedInputRequest request, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        GrantFriendship(request.Npc);

        var turns = new List<ConversationTurn>(request.Conversation ?? Array.Empty<ConversationTurn>())
        {
            new(text, true, Guid.NewGuid().ToString("N"))
        };

        var generationRequest = GenerationRequests.BuildConversation(request.Npc, request.DialogueKey, turns);
        var enqueue = EnqueueForTests ?? GenerationRequests.Enqueue;
        enqueue(request.Npc, generationRequest);
    }

    private static void GrantFriendship(NPC npc)
    {
        try
        {
            Action<NPC>? granter = FriendshipGranterForTests;
            if (granter != null)
            {
                granter(npc);
                return;
            }

            npc.grantConversationFriendship(Game1.player);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"Failed to grant conversation friendship for {npc.Name}: {ex.Message}", StardewModdingAPI.LogLevel.Trace);
        }
    }
}
