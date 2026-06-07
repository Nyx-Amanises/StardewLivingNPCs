using System;
using StardewValley;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal static class GiftMemoryDetailsFactory
{
    public static GiftMemoryDetails Build(NPC npc, SObject gift)
    {
        int taste = TryGetTaste(npc, gift);
        var labels = DescribeTaste(taste);
        string itemName = string.IsNullOrWhiteSpace(gift.DisplayName) ? gift.Name : gift.DisplayName;
        return new GiftMemoryDetails(gift.QualifiedItemId, itemName, labels.DebugLabel, labels.PromptLabel, taste);
    }

    public static GiftTasteLabels DescribeTaste(int taste)
    {
        return taste switch
        {
            0 => new GiftTasteLabels("最爱", "loved gift"),
            2 => new GiftTasteLabels("喜欢", "liked gift"),
            4 => new GiftTasteLabels("不喜欢", "disliked gift"),
            6 => new GiftTasteLabels("讨厌", "hated gift"),
            8 => new GiftTasteLabels("普通", "neutral gift"),
            _ => new GiftTasteLabels("未知喜好", "unknown gift taste")
        };
    }

    private static int TryGetTaste(NPC npc, SObject gift)
    {
        try
        {
            var method = typeof(NPC).GetMethod("getGiftTasteForThisItem", [typeof(SObject)]);
            object? result = method?.Invoke(npc, [gift]);
            return result is int taste ? taste : -1;
        }
        catch
        {
            return -1;
        }
    }
}
