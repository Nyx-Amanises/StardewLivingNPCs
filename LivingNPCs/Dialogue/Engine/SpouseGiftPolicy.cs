using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 配偶主动送礼默认策略（用户裁决 2026-07-08）：已婚配偶的日常日程台词生成时，
/// 5% 概率送礼，物品从该 NPC 自己的「最爱/喜欢」礼物口味池抽取。
/// 纯逻辑（概率与候选解析）与游戏数据读取分离，可单测。
/// </summary>
internal static class SpouseGiftPolicy
{
    /// <summary>送礼概率：1/20 = 5%。</summary>
    public const int OneInChance = 20;

    /// <summary>接到 DialogueEngine.SpouseGiftPicker 的游戏侧入口。</summary>
    public static string? PickForGame(string npcName, GameStateSnapshot snapshot)
    {
        try
        {
            if (!Game1.NPCGiftTastes.TryGetValue(npcName, out string? raw) || string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string[] fields = raw.Split('/');
            string loved = fields.Length > 1 ? fields[1] : string.Empty;
            string liked = fields.Length > 3 ? fields[3] : string.Empty;
            return TryPick(loved, liked, Random.Shared);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>5% 概率通过后从候选池随机抽一件；无候选或未通过返回 null。</summary>
    public static string? TryPick(string lovedField, string likedField, Random random)
    {
        if (random.Next(OneInChance) != 0)
        {
            return null;
        }

        var candidates = ParseCandidateIds(lovedField, likedField);
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    /// <summary>
    /// 解析口味字段（空格分隔）为可送物品 ID：只保留具体物品（正整数或带限定符的 ID），
    /// 丢弃类别负数（如 -80）。
    /// </summary>
    public static List<string> ParseCandidateIds(string lovedField, string likedField)
    {
        return $"{lovedField} {likedField}"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.StartsWith("(", StringComparison.Ordinal)
                || (int.TryParse(token, out int id) && id > 0))
            .Where(token => !RsvAiPolicy.IsBlockedContentId(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
