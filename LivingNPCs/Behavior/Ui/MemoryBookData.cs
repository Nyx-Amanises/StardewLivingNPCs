using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Behavior.Ui;

/// <summary>手册行的语义类别：菜单据此上色与排版；数据层不做任何绘制。</summary>
internal enum MemoryBookLineKind
{
    SectionHeader,
    Body,
    PlayerLine,
    NpcLine,
    DateSeparator,
    MemoryFact,
    Muted,
    Empty
}

/// <summary>一行手册内容（未换行的逻辑行；换行由菜单按实际列宽处理）。</summary>
internal sealed record MemoryBookLine(MemoryBookLineKind Kind, string Text, string HoverText = "")
{
    /// <summary>分类标题对应的标准化记忆类别，独立于翻译后的显示文字。</summary>
    public string MemoryKind { get; init; } = string.Empty;
}

/// <summary>左侧名册里的一位 NPC。</summary>
internal sealed record MemoryBookNpcSummary(
    string NpcName,
    string DisplayName,
    int Hearts,
    int LastContactTotalDays,
    string SubtitleText
);

/// <summary>
/// 从联机 DTO 物化出的手册只读源。内部状态与消息负载彼此深拷贝；每次导出也返回克隆，
/// 因而菜单只能读取本次快照，不能反向污染消息缓存或主机心智。
/// </summary>
internal sealed class MemoryBookSnapshotSource
{
    private readonly Dictionary<string, LivingNpcState> statesByName;

    public MemoryBookSnapshotSource(
        IEnumerable<MemoryBookNpcSummary> roster,
        IEnumerable<LivingNpcState> states,
        int capturedTotalDays)
    {
        this.Roster = new ReadOnlyCollection<MemoryBookNpcSummary>(roster.ToList());
        this.statesByName = states
            .Where(state => !string.IsNullOrWhiteSpace(state.NpcName))
            .GroupBy(state => state.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Clone(),
                StringComparer.OrdinalIgnoreCase);
        this.CapturedTotalDays = capturedTotalDays;
    }

    public IReadOnlyList<MemoryBookNpcSummary> Roster { get; }

    public int CapturedTotalDays { get; }

    public bool TryGetState(string npcName, out LivingNpcState? state)
    {
        if (this.statesByName.TryGetValue(npcName, out LivingNpcState? stored))
        {
            state = stored.Clone();
            return true;
        }

        state = null;
        return false;
    }

    public List<LivingNpcState> ExportStates()
    {
        return this.statesByName.Values.Select(state => state.Clone()).ToList();
    }
}

/// <summary>手册标签页。</summary>
internal enum MemoryBookTab
{
    Relationship,
    Memories,
    Conversations,
    Moments
}

/// <summary>
/// 记忆手册的数据层：把行为记忆（LivingNpcState）与对话历史（StardewEventHistory）
/// 组装成逻辑行列表。纯函数 + 注入的翻译委托，单元测试不依赖游戏运行。
/// </summary>
internal static class MemoryBookData
{
    /// <summary>翻译委托：production 传 I18n.Get；测试传回显。</summary>
    internal delegate string Translate(string key, object? tokens = null);

    private static readonly string[] MemoryKindOrder = ["fact", "preference", "promise", "boundary", "relationship"];

    // ---- 联机只读快照 ----

    /// <summary>
    /// 从主机权威状态装配一次手册快照。只复制手册实际消费的字段；空状态与本地名册一样
    /// 不进入负载，对话历史也明确不属于该消息。
    /// </summary>
    public static BookSnapshotMessage CreateBookSnapshot(
        string requestId,
        int capturedTotalDays,
        IEnumerable<LivingNpcState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return new BookSnapshotMessage
        {
            RequestId = requestId ?? string.Empty,
            CapturedTotalDays = capturedTotalDays,
            Npcs = states
                .Where(state => state != null
                    && !string.IsNullOrWhiteSpace(state.NpcName)
                    && HasAnythingToShow(state))
                .GroupBy(state => state.NpcName, StringComparer.OrdinalIgnoreCase)
                .Select(group => CreateBookNpcSnapshot(group.First()))
                .ToList()
        };
    }

    /// <summary>把单个权威状态深拷贝成不含游戏对象的协议 DTO。</summary>
    public static BookNpcSnapshot CreateBookNpcSnapshot(LivingNpcState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new BookNpcSnapshot
        {
            NpcName = state.NpcName ?? string.Empty,
            CurrentEmotion = state.CurrentEmotion ?? "Calm",
            EmotionIntensity = state.EmotionIntensity,
            InteractionComfortTier = state.InteractionComfortTier ?? "Distant",
            RelationshipTrust = state.RelationshipTrust,
            FarmerNickname = state.FarmerNickname ?? string.Empty,
            ConsecutiveConversationDays = state.ConsecutiveConversationDays,
            LastConversationTotalDays = state.LastConversationTotalDays,
            RelationshipImpression = state.RelationshipImpression ?? string.Empty,
            RelationshipImpressionUpdatedTotalDays = state.RelationshipImpressionUpdatedTotalDays,
            LastGiftName = state.LastGiftName ?? string.Empty,
            LastGiftTotalDays = state.LastGiftTotalDays,
            LastUpdatedTotalDays = state.LastUpdatedTotalDays,
            LongTermMemories = state.LongTermMemories
                .Select(memory => new BookLongTermMemorySnapshot
                {
                    Kind = memory.Kind ?? "fact",
                    Summary = memory.Summary ?? string.Empty,
                    Importance = memory.Importance,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            PlayerPreferenceMemories = state.PlayerPreferenceMemories
                .Select(memory => new BookPlayerPreferenceSnapshot
                {
                    Summary = memory.Summary ?? string.Empty,
                    Importance = memory.Importance
                })
                .ToList(),
            SharedExperiences = state.SharedExperiences
                .Select(experience => new BookSharedExperienceSnapshot
                {
                    Summary = experience.Summary ?? string.Empty,
                    LocationName = experience.LocationName ?? string.Empty,
                    LocationLabel = experience.LocationLabel ?? string.Empty,
                    CreatedTotalDays = experience.CreatedTotalDays,
                    LastUpdatedTotalDays = experience.LastUpdatedTotalDays
                })
                .ToList(),
            HelpRequests = state.HelpRequests
                .Select(request => new BookHelpRequestSnapshot
                {
                    Summary = request.Summary ?? string.Empty,
                    Status = request.Status ?? "Pending",
                    CreatedTotalDays = request.CreatedTotalDays
                })
                .ToList(),
            Conflicts = state.Conflicts
                .Select(conflict => new BookConflictSnapshot
                {
                    CauseKind = conflict.CauseKind ?? "dialogue",
                    Summary = conflict.Summary ?? string.Empty,
                    Severity = conflict.Severity,
                    Status = conflict.Status ?? "Active"
                })
                .ToList()
        };
    }

    /// <summary>
    /// 把收到的纯 DTO 物化为独立只读源。显示名、心数与翻译均在 farmhand 本地求值；
    /// 由此不会把主机玩家的关系数值或语言混进手册界面。
    /// </summary>
    public static MemoryBookSnapshotSource BuildSourceFromSnapshot(
        BookSnapshotMessage snapshot,
        Func<string, string> displayName,
        Func<string, int> hearts,
        Translate translate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(hearts);
        ArgumentNullException.ThrowIfNull(translate);

        List<LivingNpcState> states = (snapshot.Npcs ?? new List<BookNpcSnapshot>())
            .Where(npc => npc != null && !string.IsNullOrWhiteSpace(npc.NpcName))
            .Select(BuildStateFromSnapshot)
            .GroupBy(state => state.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        List<MemoryBookNpcSummary> roster = BuildRoster(
            states,
            displayName,
            hearts,
            snapshot.CapturedTotalDays,
            translate);
        return new MemoryBookSnapshotSource(roster, states, snapshot.CapturedTotalDays);
    }

    /// <summary>从协议 DTO 重建仅供手册读取的临时状态；不会进入 BehaviorMemory。</summary>
    public static LivingNpcState BuildStateFromSnapshot(BookNpcSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new LivingNpcState
        {
            NpcName = snapshot.NpcName ?? string.Empty,
            CurrentEmotion = snapshot.CurrentEmotion ?? "Calm",
            EmotionIntensity = snapshot.EmotionIntensity,
            InteractionComfortTier = snapshot.InteractionComfortTier ?? "Distant",
            RelationshipTrust = snapshot.RelationshipTrust,
            FarmerNickname = snapshot.FarmerNickname ?? string.Empty,
            ConsecutiveConversationDays = snapshot.ConsecutiveConversationDays,
            LastConversationTotalDays = snapshot.LastConversationTotalDays,
            RelationshipImpression = snapshot.RelationshipImpression ?? string.Empty,
            RelationshipImpressionUpdatedTotalDays = snapshot.RelationshipImpressionUpdatedTotalDays,
            LastGiftName = snapshot.LastGiftName ?? string.Empty,
            LastGiftTotalDays = snapshot.LastGiftTotalDays,
            LastUpdatedTotalDays = snapshot.LastUpdatedTotalDays,
            LongTermMemories = (snapshot.LongTermMemories ?? new List<BookLongTermMemorySnapshot>())
                .Select(memory => new LongTermMemoryFact
                {
                    Kind = memory.Kind ?? "fact",
                    Summary = memory.Summary ?? string.Empty,
                    Importance = memory.Importance,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            PlayerPreferenceMemories = (snapshot.PlayerPreferenceMemories ?? new List<BookPlayerPreferenceSnapshot>())
                .Select(memory => new PlayerPreferenceFact
                {
                    Summary = memory.Summary ?? string.Empty,
                    Importance = memory.Importance
                })
                .ToList(),
            SharedExperiences = (snapshot.SharedExperiences ?? new List<BookSharedExperienceSnapshot>())
                .Select(experience => new SharedExperienceFact
                {
                    Summary = experience.Summary ?? string.Empty,
                    LocationName = experience.LocationName ?? string.Empty,
                    LocationLabel = experience.LocationLabel ?? string.Empty,
                    CreatedTotalDays = experience.CreatedTotalDays,
                    LastUpdatedTotalDays = experience.LastUpdatedTotalDays
                })
                .ToList(),
            HelpRequests = (snapshot.HelpRequests ?? new List<BookHelpRequestSnapshot>())
                .Select(request => new NpcHelpRequestFact
                {
                    Summary = request.Summary ?? string.Empty,
                    Status = request.Status ?? "Pending",
                    CreatedTotalDays = request.CreatedTotalDays
                })
                .ToList(),
            Conflicts = (snapshot.Conflicts ?? new List<BookConflictSnapshot>())
                .Select(conflict => new NpcConflictFact
                {
                    CauseKind = conflict.CauseKind ?? "dialogue",
                    Summary = conflict.Summary ?? string.Empty,
                    Severity = conflict.Severity,
                    Status = conflict.Status ?? "Active"
                })
                .ToList()
        };
    }

    // ---- 名册 ----

    /// <summary>按"最近接触优先、再按心数"排序的名册；没有任何记录的状态不上册。</summary>
    public static List<MemoryBookNpcSummary> BuildRoster(
        IEnumerable<LivingNpcState> states,
        Func<string, string> displayName,
        Func<string, int> hearts,
        int nowTotalDays,
        Translate translate)
    {
        var roster = new List<MemoryBookNpcSummary>();
        foreach (LivingNpcState state in states)
        {
            if (string.IsNullOrWhiteSpace(state.NpcName) || !HasAnythingToShow(state))
            {
                continue;
            }

            int lastContact = Math.Max(state.LastConversationTotalDays, state.LastGiftTotalDays);
            lastContact = Math.Max(lastContact, state.LastUpdatedTotalDays);
            string subtitle = lastContact >= 0
                ? translate("book.roster.lastContact", new { ago = FormatDaysAgo(lastContact, nowTotalDays, translate) })
                : translate("book.roster.noContact");

            roster.Add(new MemoryBookNpcSummary(
                state.NpcName,
                displayName(state.NpcName),
                Math.Max(0, hearts(state.NpcName)),
                lastContact,
                subtitle));
        }

        return roster
            .OrderByDescending(entry => entry.LastContactTotalDays)
            .ThenByDescending(entry => entry.Hearts)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasAnythingToShow(LivingNpcState state)
    {
        return state.LongTermMemories.Count > 0
            || state.PlayerPreferenceMemories.Count > 0
            || state.SharedExperiences.Count > 0
            || state.HelpRequests.Count > 0
            || !string.IsNullOrWhiteSpace(state.RelationshipImpression)
            || state.LastConversationTotalDays >= 0
            || state.LastGiftTotalDays >= 0;
    }

    // ---- 标签页内容 ----

    public static List<MemoryBookLine> BuildRelationshipCard(
        LivingNpcState state,
        string displayName,
        int hearts,
        int nowTotalDays,
        Translate translate)
    {
        var lines = new List<MemoryBookLine>
        {
            new(MemoryBookLineKind.SectionHeader, translate("book.card.header", new { npc = displayName }))
        };

        // CurrentEmotion 是闭集（10 值），可安全走 i18n 键；Mood 是开放字符串，不做键映射。
        lines.Add(new(MemoryBookLineKind.Body, translate("book.card.emotionLine", new
        {
            emotion = translate($"book.emotion.{state.CurrentEmotion.ToLowerInvariant()}"),
            intensity = state.EmotionIntensity
        })));

        lines.Add(new(MemoryBookLineKind.Body, translate("book.card.closeness", new
        {
            tier = translate($"book.tier.{state.InteractionComfortTier.ToLowerInvariant()}"),
            trust = TrustLabel(state.RelationshipTrust, translate),
            hearts
        })));

        if (!string.IsNullOrWhiteSpace(state.FarmerNickname))
        {
            lines.Add(new(MemoryBookLineKind.Body, translate("book.card.nickname", new { nickname = state.FarmerNickname })));
        }

        if (state.LastConversationTotalDays >= 0)
        {
            lines.Add(new(MemoryBookLineKind.Muted, translate("book.card.lastConversation", new
            {
                ago = FormatDaysAgo(state.LastConversationTotalDays, nowTotalDays, translate),
                streak = state.ConsecutiveConversationDays
            })));
        }

        if (state.HasUnresolvedConflict)
        {
            NpcConflictFact? conflict = state.Conflicts
                .Where(entry => entry.Status is "Active" or "Recovering")
                .OrderByDescending(entry => entry.Severity)
                .FirstOrDefault();
            if (conflict != null)
            {
                string statusKey = conflict.Status == "Recovering" ? "book.conflict.recovering" : "book.conflict.active";
                string cause = string.IsNullOrWhiteSpace(conflict.Summary) ? conflict.CauseKind : conflict.Summary.Trim();
                lines.Add(new(MemoryBookLineKind.MemoryFact, translate(statusKey, new { cause })));
            }
        }

        lines.Add(new(MemoryBookLineKind.SectionHeader, translate("book.card.impressionHeader")));
        if (!string.IsNullOrWhiteSpace(state.RelationshipImpression))
        {
            lines.Add(new(MemoryBookLineKind.Body, state.RelationshipImpression.Trim()));
            if (state.RelationshipImpressionUpdatedTotalDays >= 0)
            {
                lines.Add(new(MemoryBookLineKind.Muted, translate("book.card.impressionUpdated", new
                {
                    ago = FormatDaysAgo(state.RelationshipImpressionUpdatedTotalDays, nowTotalDays, translate)
                })));
            }
        }
        else
        {
            lines.Add(new(MemoryBookLineKind.Empty, translate("book.card.impressionEmpty")));
        }

        return lines;
    }

    public static List<MemoryBookLine> BuildMemoryLines(
        LivingNpcState state,
        int nowTotalDays,
        Translate translate)
    {
        var lines = new List<MemoryBookLine>();

        var grouped = state.LongTermMemories
            .Where(memory => !string.IsNullOrWhiteSpace(memory.Summary))
            .GroupBy(memory => NormalizeMemoryKind(memory.Kind))
            .OrderBy(group => Array.IndexOf(MemoryKindOrder, group.Key) is var index && index >= 0 ? index : MemoryKindOrder.Length)
            .ToList();

        foreach (var group in grouped)
        {
            lines.Add(new(MemoryBookLineKind.SectionHeader, translate($"book.memoryKind.{group.Key}"))
            {
                MemoryKind = group.Key
            });
            foreach (LongTermMemoryFact memory in group
                .OrderByDescending(entry => entry.Importance)
                .ThenByDescending(entry => entry.LastUpdatedTotalDays))
            {
                string hover = translate("book.memory.hover", new
                {
                    importance = memory.Importance,
                    reinforced = memory.TimesReinforced,
                    ago = FormatDaysAgo(memory.LastUpdatedTotalDays, nowTotalDays, translate)
                });
                lines.Add(new(MemoryBookLineKind.MemoryFact, memory.Summary.Trim(), hover));
            }
        }

        if (state.PlayerPreferenceMemories.Count > 0)
        {
            lines.Add(new(MemoryBookLineKind.SectionHeader, translate("book.memoryKind.playerPreference")));
            foreach (PlayerPreferenceFact preference in state.PlayerPreferenceMemories
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Summary))
                .OrderByDescending(entry => entry.Importance))
            {
                lines.Add(new(MemoryBookLineKind.MemoryFact, preference.Summary.Trim()));
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(new(MemoryBookLineKind.Empty, translate("book.memories.empty")));
        }

        return lines;
    }

    public static List<MemoryBookLine> BuildConversationLines(
        StardewEventHistory history,
        string displayName,
        string farmerName,
        Translate translate,
        int maxConversations = 12)
    {
        var lines = new List<MemoryBookLine>();
        var conversations = history.ConversationHistory
            .Where(entry => entry.Item2?.ConversationElements is { Count: > 0 })
            .OrderByDescending(entry => entry.Item1)
            .Take(maxConversations)
            .ToList();

        foreach ((StardewTime time, ConversationHistory conversation) in conversations)
        {
            lines.Add(new(MemoryBookLineKind.DateSeparator, FormatStardewDate(time, translate)));
            foreach (ConversationElement element in conversation.ConversationElements)
            {
                string cleaned = SanitizeDialogueText(element.Text);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                lines.Add(element.IsPlayerLine
                    ? new MemoryBookLine(MemoryBookLineKind.PlayerLine, $"{farmerName}: {cleaned}")
                    : new MemoryBookLine(MemoryBookLineKind.NpcLine, $"{displayName}: {cleaned}"));
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(new(MemoryBookLineKind.Empty, translate("book.conversations.empty")));
        }

        return lines;
    }

    public static List<MemoryBookLine> BuildMomentLines(
        LivingNpcState state,
        int nowTotalDays,
        Translate translate)
    {
        var lines = new List<MemoryBookLine>();

        var experiences = state.SharedExperiences
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Summary))
            .OrderByDescending(entry => entry.LastUpdatedTotalDays)
            .ToList();
        if (experiences.Count > 0)
        {
            lines.Add(new(MemoryBookLineKind.SectionHeader, translate("book.moments.sharedHeader")));
            foreach (SharedExperienceFact experience in experiences)
            {
                string when = FormatDaysAgo(
                    experience.LastUpdatedTotalDays >= 0 ? experience.LastUpdatedTotalDays : experience.CreatedTotalDays,
                    nowTotalDays,
                    translate);
                string location = string.IsNullOrWhiteSpace(experience.LocationLabel)
                    ? experience.LocationName
                    : experience.LocationLabel;
                string suffix = string.IsNullOrWhiteSpace(location)
                    ? when
                    : translate("book.moments.whenWhere", new { when, where = location });
                lines.Add(new(MemoryBookLineKind.Body, experience.Summary.Trim()));
                lines.Add(new(MemoryBookLineKind.Muted, suffix));
            }
        }

        var helpRequests = state.HelpRequests
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Summary))
            .OrderByDescending(entry => entry.CreatedTotalDays)
            .Take(10)
            .ToList();
        if (helpRequests.Count > 0)
        {
            lines.Add(new(MemoryBookLineKind.SectionHeader, translate("book.moments.helpHeader")));
            foreach (NpcHelpRequestFact request in helpRequests)
            {
                string statusKey = request.Status switch
                {
                    "Fulfilled" => "book.help.fulfilled",
                    "Pending" => "book.help.pending",
                    "Offered" => "book.help.offered",
                    "Declined" => "book.help.declined",
                    "Expired" => "book.help.expired",
                    _ => "book.help.other"
                };
                lines.Add(new(MemoryBookLineKind.MemoryFact, translate(statusKey, new { summary = request.Summary.Trim() })));
            }
        }

        if (state.LastGiftTotalDays >= 0 && !string.IsNullOrWhiteSpace(state.LastGiftName))
        {
            lines.Add(new(MemoryBookLineKind.SectionHeader, translate("book.moments.giftHeader")));
            lines.Add(new(MemoryBookLineKind.Body, translate("book.moments.lastGift", new
            {
                gift = state.LastGiftName,
                ago = FormatDaysAgo(state.LastGiftTotalDays, nowTotalDays, translate)
            })));
        }

        if (lines.Count == 0)
        {
            lines.Add(new(MemoryBookLineKind.Empty, translate("book.moments.empty")));
        }

        return lines;
    }

    // ---- 格式化 ----

    /// <summary>肖像/情绪标记：$h $s $l $a $u $neutral 及 $数字（历史里存的是带命令的原始台词）。</summary>
    private static readonly Regex DialoguePortraitTokens = new(@"\$(?:neutral|[hslau])\b|\$\d+", RegexOptions.Compiled);

    /// <summary>
    /// 把历史里的原始台词洗成手册可读文本：去 skip# 前缀、截掉 #$q 应答菜单区、
    /// 页界（#$b#/#$e#）换成停顿、去肖像标记、压缩多余空白。
    /// </summary>
    internal static string SanitizeDialogueText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = text.Replace("skip#", string.Empty, StringComparison.Ordinal);
        int responseMenuIndex = cleaned.IndexOf("#$q ", StringComparison.Ordinal);
        if (responseMenuIndex >= 0)
        {
            cleaned = cleaned[..responseMenuIndex];
        }

        cleaned = cleaned
            .Replace("#$b#", "　", StringComparison.Ordinal)
            .Replace("#$e#", "　", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        cleaned = DialoguePortraitTokens.Replace(cleaned, string.Empty);
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned.Trim();
    }

    internal static string NormalizeMemoryKind(string? kind)
    {
        string normalized = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        return MemoryKindOrder.Contains(normalized) ? normalized : "fact";
    }

    internal static string TrustLabel(int trust, Translate translate)
    {
        string key = trust switch
        {
            >= 75 => "book.trust.deep",
            >= 50 => "book.trust.solid",
            >= 30 => "book.trust.warming",
            _ => "book.trust.early"
        };
        return translate(key);
    }

    /// <summary>"今天 / 昨天 / N 天前 / 很久以前"。unknown（-1）返回空。</summary>
    internal static string FormatDaysAgo(int totalDays, int nowTotalDays, Translate translate)
    {
        if (totalDays < 0)
        {
            return translate("book.time.unknown");
        }

        int ago = Math.Max(0, nowTotalDays - totalDays);
        return ago switch
        {
            0 => translate("book.time.today"),
            1 => translate("book.time.yesterday"),
            <= 112 => translate("book.time.daysAgo", new { days = ago }),
            _ => translate("book.time.longAgo")
        };
    }

    internal static string FormatStardewDate(StardewTime time, Translate translate)
    {
        return translate("book.time.date", new
        {
            year = time.year,
            season = translate($"book.season.{time.season.ToString().ToLowerInvariant()}"),
            day = time.dayOfMonth
        });
    }
}
