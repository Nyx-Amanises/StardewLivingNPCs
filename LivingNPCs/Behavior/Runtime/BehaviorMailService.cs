using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMailService
{
    private const string HelpRequestRewardMailKeyPrefix = "LivingNPCs.HelpRequestReward.";
    private const string GiftMailKeyPrefix = "LivingNPCs.GiftMail.";
    private const int ProfiledMailVariantCount = 3;
    private const int MaxGenerationAttempts = 3;

    private static readonly HashSet<string> AiMailMotives = new(StringComparer.OrdinalIgnoreCase)
    {
        "reciprocal", "birthday", "help_request_reward"
    };

    private readonly IModHelper helper;
    private readonly BehaviorMemory memory;
    private readonly Random random;
    private readonly ModConfig config;
    private readonly ValleyTalkPromptBridge valleyTalkBridge;

    public BehaviorMailService(IModHelper helper, BehaviorMemory memory, Random random, ModConfig config, ValleyTalkPromptBridge valleyTalkBridge)
    {
        this.helper = helper;
        this.memory = memory;
        this.random = random;
        this.config = config;
        this.valleyTalkBridge = valleyTalkBridge;
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
        request.RewardMoneyClaimQueued = false;
        request.RewardMoneyQuestPosted = false;

        if (!Game1.player.mailForTomorrow.Contains(mailKey) && !Game1.player.mailReceived.Contains(mailKey))
        {
            Game1.player.mailForTomorrow.Add(mailKey);
        }

        this.InvalidateMailCache();
    }

    public void InvalidateMailCache()
    {
        this.helper.GameContent.InvalidateCache(asset => asset.NameWithoutLocale.IsEquivalentTo("Data/mail"));
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

    /// <summary>Diagnostic dump of every tracked gift mail vs. the player's actual mail lists and Data/mail.</summary>
    public List<string> DescribeGiftMails()
    {
        var lines = new List<string>();
        if (!Context.IsWorldReady || Game1.player == null)
        {
            lines.Add("世界未就绪。");
            return lines;
        }

        Dictionary<string, string> mailData;
        try
        {
            this.InvalidateMailCache();
            mailData = this.helper.GameContent.Load<Dictionary<string, string>>("Data/mail");
        }
        catch (Exception ex)
        {
            mailData = new Dictionary<string, string>();
            lines.Add($"无法加载 Data/mail：{ex.Message}");
        }

        int livingEntries = mailData.Keys.Count(k =>
            k.StartsWith(GiftMailKeyPrefix, StringComparison.OrdinalIgnoreCase)
            || k.StartsWith(HelpRequestRewardMailKeyPrefix, StringComparison.OrdinalIgnoreCase));
        var mails = this.GetGiftMailRequests().ToList();
        lines.Add($"已追踪礼物信 {mails.Count} 封；邮箱 {Game1.player.mailbox.Count} 封，明日 {Game1.player.mailForTomorrow.Count} 封。当前是第 {Game1.Date.TotalDays} 天。");
        lines.Add($"强制重载后 Data/mail 共 {mailData.Count} 条，其中 LivingNPCs 条目 {livingEntries} 条（正常应≈已追踪数；若为 0 则说明 Data/mail 编辑根本没生效）。");
        lines.Add($"邮箱原始 key：{(Game1.player.mailbox.Count == 0 ? "（空）" : string.Join(" | ", Game1.player.mailbox))}");
        foreach (var mail in mails)
        {
            string key = GetGiftMailKey(mail);
            string text = BuildGiftMailText(mail);
            lines.Add(
                $"- [{mail.Motive}] {mail.ItemLabel} ({mail.ItemId}) 到期第{mail.DueTotalDays}天 claimed={mail.Claimed} queued={mail.QueuedForDelivery} | "
                + $"邮箱={ContainsMailKey(Game1.player.mailbox, key)} 明日={ContainsMailKey(Game1.player.mailForTomorrow, key)} 已收={ContainsMailKey(Game1.player.mailReceived, key)} Data/mail有此条={mailData.ContainsKey(key)}");
            lines.Add($"    key={key}");
            lines.Add($"    text={text.Replace("^", " / ")}");
        }

        var trackedKeys = mails.Select(GetGiftMailKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = Game1.player.mailbox
            .Concat(Game1.player.mailForTomorrow)
            .Where(k => k.StartsWith(GiftMailKeyPrefix, StringComparison.OrdinalIgnoreCase) && !trackedKeys.Contains(k))
            .Distinct()
            .ToList();
        if (orphans.Count > 0)
        {
            lines.Add($"⚠ 邮箱/明日里有 {orphans.Count} 个无追踪记录的孤儿礼物信（很可能是改名或旧测试遗留的死信，点开即消失、无内容）：");
            foreach (string key in orphans)
            {
                lines.Add($"    孤儿 key={key} Data/mail有此条={mailData.ContainsKey(key)}");
            }
        }

        return lines;
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
            NpcName = npc.Name,
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
        this.TryStartGiftMailGeneration(mail, npc);
        this.QueueDueGiftMailsForTomorrow();
        this.InvalidateMailCache();
        return true;
    }

    private void TryStartGiftMailGeneration(NpcGiftMailFact mail, NPC npc)
    {
        if (!this.config.EnableAiGiftMail || this.valleyTalkBridge == null || !this.valleyTalkBridge.IsConnected || npc == null)
        {
            return;
        }

        if (!AiMailMotives.Contains(mail.Motive))
        {
            return;
        }

        mail.GenerationStatus = "pending";
        mail.GenerationAttempts = 0;
        this.valleyTalkBridge.RequestGiftMailText(mail.MailKey, npc, this.BuildGenerationPayload(mail));
    }

    /// <summary>
    /// Polls ValleyTalk for any pending AI gift-mail bodies and stores the result on the fact.
    /// Called at save-load and day-start before the mail asset is rebuilt. Returns true if any mail
    /// became ready, so the caller can invalidate the mail cache.
    /// </summary>
    public bool ResolvePendingGiftMailGenerations()
    {
        if (!this.config.EnableAiGiftMail || this.valleyTalkBridge == null || !this.valleyTalkBridge.IsConnected)
        {
            return false;
        }

        bool changed = false;
        foreach (var state in this.memory.GetTrackedStates())
        {
            var pending = (state.GiftMails ?? new List<NpcGiftMailFact>())
                .Where(m => string.Equals(m.GenerationStatus, "pending", StringComparison.OrdinalIgnoreCase) && !m.Claimed)
                .ToList();
            foreach (var mail in pending)
            {
                string text = this.valleyTalkBridge.TryGetGiftMailText(mail.MailKey);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (IsUsableGeneratedBody(text))
                    {
                        mail.GeneratedBody = text;
                        mail.GenerationStatus = "ready";
                        changed = true;
                    }
                    else
                    {
                        mail.GenerationStatus = "failed";
                    }

                    continue;
                }

                mail.GenerationAttempts++;
                if (mail.GenerationAttempts >= MaxGenerationAttempts)
                {
                    mail.GenerationStatus = "failed";
                }
                else
                {
                    // ValleyTalk may have restarted and lost its in-memory result; ask again.
                    NPC npc = Game1.getCharacterFromName(mail.NpcName);
                    if (npc != null)
                    {
                        this.valleyTalkBridge.RequestGiftMailText(mail.MailKey, npc, this.BuildGenerationPayload(mail));
                    }
                }
            }
        }

        return changed;
    }

    private string BuildGenerationPayload(NpcGiftMailFact mail)
    {
        return JsonSerializer.Serialize(new
        {
            motive = mail.Motive,
            itemLabel = mail.ItemLabel,
            sourceGift = mail.SourceGiftName,
            npcDisplayName = mail.NpcDisplayName,
            tier = mail.Tier,
            timeoutSeconds = this.config.AiGiftMailTimeoutSeconds
        });
    }

    private static bool TryGetUsableGeneratedBody(NpcGiftMailFact mail, out string body)
    {
        body = string.Empty;
        if (mail == null
            || !string.Equals(mail.GenerationStatus, "ready", StringComparison.OrdinalIgnoreCase)
            || !IsUsableGeneratedBody(mail.GeneratedBody))
        {
            return false;
        }

        body = mail.GeneratedBody.Trim();
        return true;
    }

    private static bool IsUsableGeneratedBody(string body)
    {
        return !string.IsNullOrWhiteSpace(body)
            && body.IndexOf('%') < 0
            && body.IndexOf('[') < 0
            && body.IndexOf(']') < 0;
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
            ? I18n.Get("help.mail.fallbackNpc")
            : request.NpcDisplayName.Trim();
        string itemLabel = string.IsNullOrWhiteSpace(request.RequestedItemLabel)
            ? I18n.Get("help.mail.fallbackItem")
            : request.RequestedItemLabel.Trim();
        string body = I18n.Get("help.mail.body.rewardMoney", new { npc = npcName, item = itemLabel });
        string title = I18n.Get("help.mail.title.rewardMoney");

        return $"{EnsureSigned(body, "LivingNPCs")}%money {amount} %%[#]{title}";
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
            ? I18n.Get("gift.mail.fallbackNpc")
            : mail.NpcDisplayName.Trim();
        string itemLabel = string.IsNullOrWhiteSpace(mail.ItemLabel)
            ? I18n.Get("gift.mail.fallbackItem")
            : mail.ItemLabel.Trim();
        string motive = NormalizeGiftMailMotive(mail.Motive);
        string sourceGift = string.IsNullOrWhiteSpace(mail.SourceGiftName)
            ? I18n.Get(motive == "help_request_reward"
                ? "gift.mail.fallbackHelpRequestItem"
                : "gift.mail.fallbackSourceGift")
            : mail.SourceGiftName.Trim();
        var tokens = new { npc = npcName, item = itemLabel, sourceGift };
        string profile = ResolveNpcMailProfile(mail);
        string body = TryGetUsableGeneratedBody(mail, out string generated)
            ? generated
            : GetGiftMailBody(motive, profile, mail, tokens);
        string title = I18n.Get($"gift.mail.title.{motive}", tokens);
        string itemId = EnsureQualifiedItemId(mail.ItemId);
        return $"{EnsureSigned(body, npcName)}%item id {itemId} 1 %%[#]{title}";
    }

    private static string GetGiftMailBody(string motive, string profile, NpcGiftMailFact mail, object tokens)
    {
        if (UsesProfiledGiftMail(motive) && !string.IsNullOrWhiteSpace(profile))
        {
            if (TryGetGiftMailVariantBody(motive, profile, mail, tokens, out string profiledBody))
            {
                return profiledBody;
            }

            string tone = ResolveGiftMailToneProfile(motive, profile);
            if (!string.IsNullOrWhiteSpace(tone)
                && !string.Equals(tone, profile, StringComparison.OrdinalIgnoreCase)
                && TryGetGiftMailVariantBody(motive, tone, mail, tokens, out string toneBody))
            {
                return toneBody;
            }
        }

        if (UsesVariantGiftMail(motive)
            && TryGetGiftMailVariantBody(motive, string.Empty, mail, tokens, out string variantBody))
        {
            return variantBody;
        }

        return I18n.Get($"gift.mail.body.{motive}", tokens);
    }

    private static bool UsesProfiledGiftMail(string motive)
    {
        return motive is "reciprocal" or "help_request_reward" or "birthday";
    }

    private static bool UsesVariantGiftMail(string motive)
    {
        return motive is "reciprocal" or "help_request_reward" or "birthday";
    }

    private static bool TryGetGiftMailVariantBody(string motive, string profile, NpcGiftMailFact mail, object tokens, out string body)
    {
        int firstVariant = SelectProfiledMailVariant(mail);
        for (int offset = 0; offset < ProfiledMailVariantCount; offset++)
        {
            int variant = (firstVariant + offset) % ProfiledMailVariantCount;
            string key = string.IsNullOrWhiteSpace(profile)
                ? $"gift.mail.body.{motive}.{variant}"
                : $"gift.mail.body.{motive}.{profile}.{variant}";
            string text = I18n.Get(key, tokens);
            if (!LooksMissingTranslation(key, text))
            {
                body = text;
                return true;
            }
        }

        body = string.Empty;
        return false;
    }

    private static int SelectProfiledMailVariant(NpcGiftMailFact mail)
    {
        unchecked
        {
            string seed = $"{mail.MailKey}:{mail.NpcName}:{mail.NpcDisplayName}:{mail.Motive}:{mail.ItemId}:{mail.SourceGiftName}:{mail.CreatedTotalDays}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return Math.Abs(hash % ProfiledMailVariantCount);
        }
    }

    private static string ResolveNpcMailProfile(NpcGiftMailFact mail)
    {
        string key = NormalizeNpcKey(string.IsNullOrWhiteSpace(mail.NpcName) ? mail.NpcDisplayName : mail.NpcName);
        return key switch
        {
            "penny" or "潘妮" => "penny",
            "sebastian" or "塞巴斯蒂安" => "sebastian",
            "abigail" or "阿比盖尔" => "abigail",
            "alex" or "亚历克斯" => "alex",
            "leah" or "莉亚" => "leah",
            "elliott" or "艾利欧特" => "elliott",
            "sam" or "山姆" => "sam",
            "harvey" or "哈维" => "harvey",
            "haley" or "海莉" => "haley",
            "emily" or "艾米丽" => "emily",
            "robin" or "罗宾" => "robin",
            "gus" or "格斯" => "gus",
            "marnie" or "玛妮" => "marnie",
            "willy" or "威利" => "willy",
            "evelyn" or "艾芙琳" => "evelyn",
            "george" or "乔治" => "george",
            "pam" or "帕姆" or "潘姆" => "pam",
            "clint" or "克林特" => "clint",
            "wizard" or "法师" => "wizard",
            "krobus" or "科罗布斯" => "krobus",
            "pierre" or "皮埃尔" => "pierre",
            "caroline" or "卡罗琳" => "caroline",
            "demetrius" or "德米特里厄斯" => "demetrius",
            "jodi" or "乔迪" => "jodi",
            "kent" or "肯特" => "kent",
            "lewis" or "刘易斯" => "lewis",
            "sandy" or "桑迪" => "sandy",
            "dwarf" or "矮人" => "dwarf",
            "leo" or "雷欧" => "leo",
            "vincent" or "文森特" => "vincent",
            "jas" or "贾斯" => "jas",
            "andy" => "andy",
            "claire" => "claire",
            "sophia" => "sophia",
            "susan" => "susan",
            "victor" => "victor",
            "olivia" => "olivia",
            "lance" => "lance",
            "scarlett" => "scarlett",
            "gunther" or "gunthersilvian" => "gunther",
            "martin" => "martin",
            "morris" => "morris",
            "magnus" => "wizard",
            "linus" or "莱纳斯" => "linus",
            "maru" or "玛鲁" => "maru",
            "shane" or "谢恩" => "shane",
            _ => string.Empty
        };
    }

    private static string NormalizeNpcKey(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(" ", string.Empty);
    }

    private static string ResolveGiftMailToneProfile(string motive, string profile)
    {
        if (motive != "birthday")
        {
            return string.Empty;
        }

        return profile switch
        {
            "penny" or "caroline" or "claire" or "sophia" => "gentle",
            "sebastian" or "george" or "pam" or "clint" or "shane" => "reserved",
            "abigail" or "alex" or "sam" or "martin" => "energetic",
            "haley" or "emily" or "sandy" or "olivia" or "scarlett" => "bright",
            "robin" or "pierre" or "demetrius" or "andy" or "susan" or "maru" => "practical",
            "harvey" or "kent" or "lewis" or "victor" or "gunther" or "morris" => "formal",
            "wizard" or "krobus" or "dwarf" => "mystic",
            "willy" or "linus" or "leo" or "lance" => "outdoors",
            "gus" or "marnie" or "evelyn" or "jodi" => "warm",
            "vincent" or "jas" => "child",
            "leah" or "elliott" => "artistic",
            _ => string.Empty
        };
    }

    private static bool LooksMissingTranslation(string key, string text)
    {
        return string.IsNullOrWhiteSpace(text)
            || string.Equals(text, key, StringComparison.Ordinal);
    }

    private static string EnsureSigned(string body, string signer)
    {
        string trimmed = (body ?? string.Empty).TrimEnd();
        if (HasSignature(trimmed, signer))
        {
            return trimmed;
        }

        return $"{trimmed}^^    - {signer}";
    }

    private static bool HasSignature(string body, string signer)
    {
        if (string.IsNullOrWhiteSpace(signer))
        {
            return false;
        }

        return body.EndsWith($"- {signer}", StringComparison.OrdinalIgnoreCase)
            || body.EndsWith($"- {signer.Trim()}", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGiftMailMotive(string motive)
    {
        return motive switch
        {
            "reciprocal" => "reciprocal",
            "inventory_full" => "inventory_full",
            "meaningful" => "meaningful",
            "thanks" => "thanks",
            "preference" => "preference",
            "help_request_reward" => "help_request_reward",
            "birthday" => "birthday",
            _ => "default"
        };
    }

    private static string EnsureQualifiedItemId(string itemId)
    {
        string trimmed = itemId.Trim();
        // 1.6 mail attachments use "%item id <qualifiedId> <count> %%"; make sure the id is
        // qualified (default to the object category) so the attachment resolves and is grabbable.
        return trimmed.StartsWith("(", StringComparison.Ordinal) ? trimmed : $"(O){trimmed}";
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
