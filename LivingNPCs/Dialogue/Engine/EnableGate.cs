using System;
using System.Collections.Generic;
using StardewValley;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 每 NPC 启用判定与概率门（WP10 §4.4）。规则链顺序固定；概率门支持
/// "NPC×游戏日"结果记忆（retainResult），日期变更清空。
/// </summary>
internal sealed class EnableGate
{
    private readonly Func<string, NpcBio> getBio;
    private readonly Func<int> dayStamp;
    private readonly Random random;
    private readonly object gate = new();
    private readonly Dictionary<string, bool> dailyMemo = new(StringComparer.OrdinalIgnoreCase);
    private int memoDay = -1;

    /// <summary>旧版共存检测置真（01 §5：dandm1.ValleyTalk 仍在加载时引擎保持关闭）。</summary>
    public static bool EngineDisabledByCoexistence { get; set; }

    public EnableGate(Func<string, NpcBio> getBio, Func<int>? dayStamp = null, Random? random = null)
    {
        this.getBio = getBio;
        this.dayStamp = dayStamp ?? DefaultDayStamp;
        this.random = random ?? Random.Shared;
    }

    private static int DefaultDayStamp()
    {
        try
        {
            return (Game1.year * 112 * 4) + (Game1.seasonIndex * 28) + Game1.dayOfMonth;
        }
        catch
        {
            return 0;
        }
    }

    public bool IsEnabledFor(NPC? npc)
    {
        if (npc == null || RsvAiPolicy.IsBlockedNpc(npc))
        {
            return false;
        }

        return this.IsEnabledForName(npc.Name);
    }

    public bool IsEnabledForName(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName) || RsvAiPolicy.IsBlockedNpcName(npcName))
        {
            return false;
        }

        if (EngineDisabledByCoexistence || DialogueServices.Config?.EnableDialogueEngine != true)
        {
            return false;
        }

        var disabled = DialogueServices.Config.DisabledCharacters;
        foreach (string name in disabled)
        {
            if (string.Equals(name?.Trim(), npcName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (ThirdPartyContentPolicy.HasUnauthorizedContent)
        {
            var bio = this.getBio(npcName);
            if (bio == null || bio.Missing || string.IsNullOrWhiteSpace(bio.Biography))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 概率门（供 WP12 补丁调用，§4.4.5）：probability 取 0–4，4 恒真，0 恒假，
    /// 其余按 probability/4 概率放行；retainResult 时按 NPC×游戏日记忆当日结果。
    /// </summary>
    public bool PassProbability(string npcName, int probability, bool retainResult)
    {
        probability = Math.Clamp(probability, 0, 4);
        if (probability == 4)
        {
            return true;
        }

        if (probability == 0)
        {
            return false;
        }

        if (!retainResult)
        {
            return this.random.Next(4) < probability;
        }

        lock (this.gate)
        {
            int today = this.dayStamp();
            if (today != this.memoDay)
            {
                this.dailyMemo.Clear();
                this.memoDay = today;
            }

            if (this.dailyMemo.TryGetValue(npcName, out bool remembered))
            {
                return remembered;
            }

            bool result = this.random.Next(4) < probability;
            this.dailyMemo[npcName] = result;
            return result;
        }
    }
}
