using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>P1/P3/P11/P13 共用的小件：触发键查询、输入会话发起、占位文本与键解析。</summary>
internal static class DialoguePatchHelpers
{
    /// <summary>配置触发键当前是否按下。</summary>
    public static bool IsTriggerKeyDown()
    {
        var triggerKey = DialogueServices.Config?.InitiateTypedDialogueKey ?? SButton.None;
        if (triggerKey == SButton.None)
        {
            return false;
        }

        return DialogueServices.Helper?.Input.IsDown(triggerKey) == true;
    }

    /// <summary>从鼠标抓取格与玩家面对格中选择本次动作键实际指向的 NPC。</summary>
    public static bool TryFindNpcForInteraction(ICursorPosition cursor, out NPC? npc)
    {
        npc = null;
        if (cursor == null || Game1.currentLocation == null || Game1.player == null)
        {
            return false;
        }

        var playerTile = Game1.player.TilePoint;
        var facingTile = Game1.player.FacingDirection switch
        {
            0 => new Point(playerTile.X, playerTile.Y - 1),
            1 => new Point(playerTile.X + 1, playerTile.Y),
            2 => new Point(playerTile.X, playerTile.Y + 1),
            3 => new Point(playerTile.X - 1, playerTile.Y),
            _ => playerTile
        };
        var targetTiles = new[]
        {
            cursor.GrabTile,
            new Vector2(facingTile.X, facingTile.Y)
        };

        bool allowSleeping = DialogueServices.Config?.AllowWakeSleepingNpc == true;
        const float maxConversationDistanceToPlayer = 2.5f;
        npc = Game1.currentLocation.characters
            .Where(candidate =>
                candidate.currentLocation == Game1.currentLocation
                && !string.IsNullOrWhiteSpace(candidate.Name)
                && NpcInteractionEligibility.IsEligible(candidate)
                && !candidate.IsInvisible
                && (allowSleeping || !candidate.isSleeping.Value))
            .Select(candidate => new
            {
                Npc = candidate,
                DistanceToTarget = targetTiles.Min(tile => Vector2.Distance(candidate.Tile, tile)),
                DistanceToPlayer = Vector2.Distance(candidate.Tile, Game1.player.Tile)
            })
            .Where(pair => pair.DistanceToTarget <= 1.5f && pair.DistanceToPlayer <= maxConversationDistanceToPlayer)
            .OrderBy(pair => pair.DistanceToTarget)
            .ThenBy(pair => pair.DistanceToPlayer)
            .Select(pair => pair.Npc)
            .FirstOrDefault();

        return npc != null;
    }

    /// <summary>主动搭话共通动作（P1/P13 前半）：清空上次会话上下文并提交输入请求。</summary>
    public static void BeginTypedConversation(NPC npc)
    {
        DialogueEngineHost.Instance?.History.ClearContext();
        string prompt = Util.GetString("uiStartConversation", new { Name = npc.displayName }, returnNull: true)
            ?? "What do you want to say?";
        TypedInputRequestQueue.Submit(prompt, npc, dialogueKey: string.Empty, conversation: null);
    }

    /// <summary>占位对白文本：占位标记，原文非空则以 # 携带（§4.2.3）。</summary>
    public static string BuildPlaceholderText(string original)
    {
        return string.IsNullOrWhiteSpace(original)
            ? EngineConstants.DialogueGenerationTag
            : $"{EngineConstants.DialogueGenerationTag}#{original}";
    }

    /// <summary>对白全部非空行用空格连接（占位改写的"原版参考台词"）。</summary>
    public static string JoinDialogueLines(IEnumerable<DialogueLine?>? lines)
    {
        if (lines == null)
        {
            return string.Empty;
        }

        return string.Join(" ", lines
            .Select(line => line?.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>
    /// 占位对白 → 生成请求对话键：temporaryDialogueKey 优先，其次 TranslationKey，
    /// 均空用 default（P3/P7/P8/P10 构造的键经此透传给引擎的键文法）。
    /// </summary>
    public static string ResolvePlaceholderKey(GameDialogue placeholder)
    {
        return ResolvePlaceholderKey(placeholder.temporaryDialogueKey, placeholder.TranslationKey);
    }

    internal static string ResolvePlaceholderKey(string? temporaryDialogueKey, string? translationKey)
    {
        if (!string.IsNullOrWhiteSpace(temporaryDialogueKey))
        {
            return temporaryDialogueKey;
        }

        if (!string.IsNullOrWhiteSpace(translationKey))
        {
            return translationKey;
        }

        return "default";
    }
}
