using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMailService
{
    private const string HelpRequestRewardMailKeyPrefix = "LivingNPCs.HelpRequestReward.";
    private const string GiftMailKeyPrefix = "LivingNPCs.GiftMail.";

    private readonly IModHelper helper;
    private readonly BehaviorMemory memory;
    private readonly Random random;

    public BehaviorMailService(IModHelper helper, BehaviorMemory memory, Random random)
    {
        this.helper = helper;
        this.memory = memory;
        this.random = random;
    }

    public void ApplyMailData(IDictionary<string, string> data)
    {
        foreach (var request in this.GetHelpRequestRewardMailRequests())
        {
            string key = this.GetHelpRequestRewardMailKey(request);
            if (!string.IsNullOrWhiteSpace(key))
            {
                data[key] = BuildHelpRequestRewardMailText(request);
            }
        }

        foreach (var mail in this.GetGiftMailRequests())
        {
            string key = GetGiftMailKey(mail);
            if (!string.IsNullOrWhiteSpace(key))
            {
                data[key] = BuildGiftMailText(mail);
            }
        }
    }

    public bool ShouldSendHelpRequestMoneyByMail(NpcHelpRequestFact request)
    {
        int chance = request.RewardMoney switch
        {
            >= 5000 => 60,
            >= 1000 => 45,
            _ => 25
        };

        unchecked
        {
            string seed = $"{request.QuestLogId}:{request.Summary}:{request.RequestedItemId}:{request.RewardMoney}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return Math.Abs(hash % 100) < chance;
        }
    }

    public void ScheduleHelpRequestMoneyRewardMail(NpcHelpRequestFact request)
    {
        string mailKey = this.GetHelpRequestRewardMailKey(request);
        request.RewardMoneyByMail = true;
        request.RewardMoneyMailKey = mailKey;
        request.RewardMoneyMailTotalDays = Game1.Date.TotalDays + 1;
        request.RewardMoneyGranted = true;

        if (!Game1.player.mailForTomorrow.Contains(mailKey) && !Game1.player.mailReceived.Contains(mailKey))
        {
            Game1.player.mailForTomorrow.Add(mailKey);
        }

        this.helper.GameContent.InvalidateCache("Data/mail");
    }

    public bool HasPendingGiftMail(LivingNpcState state, string motive)
    {
        return state.GiftMails.Any(mail =>
            mail.DueTotalDays >= Game1.Date.TotalDays
            && string.Equals(mail.Motive, motive, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryGetCurrentGiftMailInMailbox(out string mailKey, out NpcGiftMailFact? giftMail)
    {
        mailKey = string.Empty;
        giftMail = null;
        if (!Context.IsWorldReady || Game1.player == null || Game1.player.mailbox.Count == 0)
        {
            return false;
        }

        mailKey = Game1.player.mailbox[0];
        return this.TryGetGiftMail(mailKey, out giftMail);
    }

    public bool MarkGiftMailClaimed(string mailKey)
    {
        if (!Context.IsWorldReady || !this.TryGetGiftMail(mailKey, out NpcGiftMailFact? giftMail) || giftMail == null)
        {
            return false;
        }

        giftMail.Claimed = true;
        giftMail.ClaimedTotalDays = Game1.Date.TotalDays;
        giftMail.ClaimedTimeOfDay = Game1.timeOfDay;
        return true;
    }

    public int RestoreMissingGiftMailsToMailbox(bool includeReceived, bool restoreAll)
    {
        if (!Context.IsWorldReady || Game1.player == null)
        {
            return 0;
        }

        var candidates = this.GetGiftMailRequests()
            .Where(mail => !mail.Claimed && mail.QueuedForDelivery && mail.DueTotalDays <= Game1.Date.TotalDays)
            .Select(mail => new
            {
                Mail = mail,
                Key = GetGiftMailKey(mail)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key)
                && !ContainsMailKey(Game1.player.mailbox, entry.Key)
                && !ContainsMailKey(Game1.player.mailForTomorrow, entry.Key)
                && (includeReceived || !ContainsMailKey(Game1.player.mailReceived, entry.Key)))
            .OrderByDescending(entry => entry.Mail.DueTotalDays)
            .ThenByDescending(entry => entry.Mail.CreatedTotalDays)
            .ThenByDescending(entry => entry.Mail.CreatedTimeOfDay)
            .ToList();

        if (!restoreAll)
        {
            candidates = candidates.Take(1).ToList();
        }

        int restored = 0;
        foreach (var candidate in candidates)
        {
            if (ContainsMailKey(Game1.player.mailReceived, candidate.Key))
            {
                Game1.player.mailReceived.Remove(candidate.Key);
            }

            if (!ContainsMailKey(Game1.player.mailbox, candidate.Key))
            {
                Game1.player.mailbox.Add(candidate.Key);
                restored++;
            }
        }

        if (restored > 0)
        {
            this.helper.GameContent.InvalidateCache("Data/mail");
        }

        return restored;
    }

    public bool ScheduleGiftMail(
        NPC npc,
        LivingNpcState state,
        GiftSelection selection,
        string motive,
        string reason,
        int dueInDays,
        string sourceGiftName = ""
    )
    {
        if (Game1.player == null)
        {
            return false;
        }

        state.GiftMails ??= new List<NpcGiftMailFact>();
        string itemLabel = selection.DebugName;
        try
        {
            SObject preview = ItemRegistry.Create<SObject>(selection.ItemId);
            itemLabel = string.IsNullOrWhiteSpace(preview.DisplayName) ? itemLabel : preview.DisplayName;
        }
        catch
        {
            // Keep the selector's debug name if the item preview cannot be created yet.
        }

        var mail = new NpcGiftMailFact
        {
            MailKey = $"{GiftMailKeyPrefix}{SanitizeFileName(npc.Name)}.{Game1.Date.TotalDays}.{Game1.timeOfDay}.{this.random.Next(100000):D5}",
            NpcDisplayName = npc.displayName,
            ItemId = selection.ItemId,
            ItemLabel = itemLabel,
            Motive = motive,
            Reason = reason,
            SourceGiftName = sourceGiftName,
            Tier = selection.Tier == GiftTier.Meaningful ? "meaningful" : "small",
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            DueTotalDays = Game1.Date.TotalDays + Math.Max(1, dueInDays)
        };
        state.GiftMails.Add(mail);
        RememberAiGiftItem(state, selection.ItemId);
        this.QueueDueGiftMailsForTomorrow();
        this.helper.GameContent.InvalidateCache("Data/mail");
        return true;
    }

    public void QueueDueGiftMailsForTomorrow()
    {
        if (!Context.IsWorldReady || Game1.player == null)
        {
            return;
        }

        foreach (var state in this.memory.GetTrackedStates())
        {
            foreach (var mail in state.GiftMails.Where(mail =>
                         !mail.Claimed
                         && !mail.QueuedForDelivery
                         && mail.DueTotalDays <= Game1.Date.TotalDays + 1))
            {
                string key = GetGiftMailKey(mail);
                mail.MailKey = key;
                if (!Game1.player.mailForTomorrow.Contains(key) && !Game1.player.mailReceived.Contains(key))
                {
                    Game1.player.mailForTomorrow.Add(key);
                }

                mail.QueuedForDelivery = true;
            }
        }
    }

    public static void RememberAiGiftItem(LivingNpcState state, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        state.RecentAiGiftItemIds ??= new List<string>();
        string normalized = itemId.Trim();
        state.RecentAiGiftItemIds.RemoveAll(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
        state.RecentAiGiftItemIds.Insert(0, normalized);
        if (state.RecentAiGiftItemIds.Count > 3)
        {
            state.RecentAiGiftItemIds.RemoveRange(3, state.RecentAiGiftItemIds.Count - 3);
        }
    }

    private IEnumerable<NpcHelpRequestFact> GetHelpRequestRewardMailRequests()
    {
        return this.memory.GetTrackedStates()
            .SelectMany(state => state.HelpRequests)
            .Where(request => request.RewardMoneyByMail
                && request.RewardMoney > 0
                && !string.IsNullOrWhiteSpace(request.RewardMoneyMailKey));
    }

    private string GetHelpRequestRewardMailKey(NpcHelpRequestFact request)
    {
        if (!string.IsNullOrWhiteSpace(request.RewardMoneyMailKey))
        {
            return request.RewardMoneyMailKey.Trim();
        }

        string id = string.IsNullOrWhiteSpace(request.QuestLogId)
            ? SanitizeFileName($"{request.NpcDisplayName}-{request.Summary}")
            : request.QuestLogId.Trim();
        return $"{HelpRequestRewardMailKeyPrefix}{id}";
    }

    private static string BuildHelpRequestRewardMailText(NpcHelpRequestFact request)
    {
        int amount = Math.Clamp(request.RewardMoney <= 0 ? 200 : request.RewardMoney, 200, 10000);
        string npcName = string.IsNullOrWhiteSpace(request.NpcDisplayName)
            ? "镇上的居民"
            : request.NpcDisplayName.Trim();
        string itemLabel = string.IsNullOrWhiteSpace(request.RequestedItemLabel)
            ? "那件东西"
            : request.RequestedItemLabel.Trim();

        return $"@，谢谢你之前帮{npcName}带来{itemLabel}。这份谢礼由镇上的互助基金代为发放，请收下。^^    - LivingNPCs%money {amount} %%[#]互助谢礼";
    }

    private IEnumerable<NpcGiftMailFact> GetGiftMailRequests()
    {
        return this.memory.GetTrackedStates()
            .SelectMany(state => state.GiftMails ?? new List<NpcGiftMailFact>())
            .Where(mail => !string.IsNullOrWhiteSpace(mail.MailKey)
                && !string.IsNullOrWhiteSpace(mail.ItemId)
                && !string.IsNullOrWhiteSpace(mail.ItemLabel));
    }

    private bool TryGetGiftMail(string mailKey, out NpcGiftMailFact? giftMail)
    {
        giftMail = null;
        if (string.IsNullOrWhiteSpace(mailKey))
        {
            return false;
        }

        string normalizedKey = mailKey.Trim();
        giftMail = this.GetGiftMailRequests()
            .FirstOrDefault(mail => string.Equals(GetGiftMailKey(mail), normalizedKey, StringComparison.OrdinalIgnoreCase));
        return giftMail != null;
    }

    private static bool ContainsMailKey(IEnumerable<string> mailKeys, string key)
    {
        return mailKeys.Any(mailKey => string.Equals(mailKey, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetGiftMailKey(NpcGiftMailFact mail)
    {
        return string.IsNullOrWhiteSpace(mail.MailKey)
            ? $"{GiftMailKeyPrefix}{Guid.NewGuid():N}"
            : mail.MailKey.Trim();
    }

    private static string BuildGiftMailText(NpcGiftMailFact mail)
    {
        string npcName = string.IsNullOrWhiteSpace(mail.NpcDisplayName)
            ? "镇上的居民"
            : mail.NpcDisplayName.Trim();
        string itemLabel = string.IsNullOrWhiteSpace(mail.ItemLabel)
            ? "这件小东西"
            : mail.ItemLabel.Trim();
        string sourceGift = string.IsNullOrWhiteSpace(mail.SourceGiftName)
            ? "你送来的礼物"
            : mail.SourceGiftName.Trim();
        string body = mail.Motive switch
        {
            "reciprocal" => $"@，那天你送我的{sourceGift}，我一直记着。这个{itemLabel}算是一点回礼，希望你会喜欢。",
            "inventory_full" => $"@，刚才想把{itemLabel}交给你，不过你的背包好像满了。我把它放进信里寄过来了，记得收下。",
            "meaningful" => $"@，有些话当面反而不好说清楚。这个{itemLabel}想送给你，希望它能把我的心意带到。",
            "thanks" => $"@，谢谢你之前帮的忙。这个{itemLabel}是我的一点谢意，请收下。",
            "preference" => $"@，我记得你似乎会喜欢这样的东西，所以把{itemLabel}寄给你。希望它来得正好。",
            _ => $"@，今天想起你，觉得这个{itemLabel}也许会派上用场。请收下吧。"
        };
        string title = mail.Motive switch
        {
            "reciprocal" => $"{npcName}的回礼",
            "inventory_full" => $"{npcName}寄来的礼物",
            "meaningful" => $"{npcName}的心意",
            "thanks" => $"{npcName}的谢礼",
            _ => $"{npcName}的礼物"
        };
        string itemId = GetMailObjectId(mail.ItemId);
        return $"{body}^^    - {npcName}%item object {itemId} 1 %%[#]{title}";
    }

    private static string GetMailObjectId(string itemId)
    {
        string trimmed = itemId.Trim();
        if (trimmed.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[3..];
        }

        return trimmed;
    }

    private static string SanitizeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return safe;
    }
}
