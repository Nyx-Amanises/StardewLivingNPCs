using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Behavior.Ui;

/// <summary>
/// 游戏内记忆手册：左页 NPC 名册（头像 + 心数 + 最近接触），右页四个标签
/// （关系卡 / 长期记忆 / 对话回忆 / 共同经历）。数据在打开与切换时快照成逻辑行，
/// 绘制层只负责换行、上色与滚动；支持鼠标滚轮、滚动条、手柄（B 关闭、LB/RB 切标签、方向键滚动）。
/// </summary>
internal sealed class MemoryBookMenu : IClickableMenu
{
    private enum RemoteLoadState
    {
        Ready,
        Loading,
        TimedOut
    }

    private const int RosterRowHeight = 88;
    private const int TabButtonHeight = 76;
    private const int ContentPadding = 28;
    private const int FooterHeight = 28;

    /// <summary>内容区裁剪用（复用实例，避免每帧分配）。</summary>
    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    private readonly MemoryBookData.Translate translate;
    private readonly Func<string, StardewEventHistory> getHistory;
    private readonly Func<string> getFarmerName;
    private readonly MemoryBookAssets assets;
    private readonly bool uiInitialized;
    private readonly Dictionary<string, LivingNpcState> statesByName;
    private readonly List<MemoryBookNpcSummary> roster;
    private readonly Dictionary<(string Npc, MemoryBookTab Tab), List<MemoryBookLine>> pageCache = new();

    private readonly List<ClickableComponent> rosterRows = new();
    private readonly List<ClickableComponent> tabButtons = new();
    private ClickableTextureComponent? rosterUpArrow;
    private ClickableTextureComponent? rosterDownArrow;

    private Rectangle bookBounds;
    private Rectangle leftPageBounds;
    private Rectangle rightPageBounds;
    private Rectangle rosterHeaderBounds;
    private Rectangle rosterBounds;
    private Rectangle profileBounds;
    private Rectangle contentBounds;
    private Rectangle footerBounds;
    private int rosterScrollIndex;
    private float contentScroll;
    private float contentHeight;
    private int selectedRosterIndex;
    private MemoryBookTab activeTab = MemoryBookTab.Relationship;
    private string hoverText = string.Empty;
    private int capturedTotalDays;
    private RemoteLoadState remoteLoadState;
    private int hoveredRosterIndex = -1;
    private MemoryBookTab? hoveredTab;
    private bool contentScrollbarHovered;
    private bool draggingContentScrollbar;
    private int contentScrollbarDragOffset;

    /// <summary>换好行的绘制行（按当前列宽缓存；列宽或数据变化时重建）。</summary>
    private readonly List<(MemoryBookLine Line, string Wrapped, float Height)> wrappedLines = new();
    private int wrappedForWidth = -1;
    private (string Npc, MemoryBookTab Tab)? wrappedForPage;

    private MemoryBookMenu(
        List<MemoryBookNpcSummary> roster,
        Dictionary<string, LivingNpcState> statesByName,
        Func<string, StardewEventHistory> getHistory,
        Func<string> getFarmerName,
        MemoryBookData.Translate translate,
        MemoryBookAssets? assets,
        int capturedTotalDays,
        RemoteLoadState remoteLoadState,
        bool initializeUi)
    {
        this.roster = roster;
        this.statesByName = statesByName;
        this.getHistory = getHistory;
        this.getFarmerName = getFarmerName;
        this.translate = translate;
        this.assets = assets ?? MemoryBookAssets.Fallback;
        this.capturedTotalDays = capturedTotalDays;
        this.remoteLoadState = remoteLoadState;
        this.uiInitialized = initializeUi;

        if (initializeUi)
        {
            this.RebuildLayout();
            this.initializeUpperRightCloseButton();
            this.PositionCloseButton();
            Game1.playSound("bigSelect");
        }
    }

    /// <summary>入口：没有任何可展示的 NPC 时返回 null（调用方给 HUD 提示）。</summary>
    public static MemoryBookMenu? TryCreate(BehaviorMemory memory)
    {
        return TryCreate(memory, MemoryBookAssets.Fallback);
    }

    internal static MemoryBookMenu? TryCreate(BehaviorMemory memory, MemoryBookAssets assets)
    {
        MemoryBookData.Translate translate = TranslateI18n;
        int nowTotalDays = Game1.Date.TotalDays;
        var states = memory.GetTrackedStates().ToList();
        var statesByName = states
            .Where(state => !string.IsNullOrWhiteSpace(state.NpcName))
            .GroupBy(state => state.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var roster = MemoryBookData.BuildRoster(
            states,
            npcName => Game1.getCharacterFromName(npcName)?.displayName ?? npcName,
            npcName => SafeHearts(npcName),
            nowTotalDays,
            translate);

        if (roster.Count == 0)
        {
            return null;
        }

        return new MemoryBookMenu(
            roster,
            statesByName,
            npcName => DialogueHistoryStore.Instance.GetHistory(npcName),
            () => Game1.player?.Name ?? "Farmer",
            translate,
            assets,
            nowTotalDays,
            RemoteLoadState.Ready,
            initializeUi: true);
    }

    /// <summary>
    /// farmhand 入口：立即打开安全的加载中菜单；对话页解析器仍指向本地历史，
    /// 收到主机快照后由 <see cref="ApplySnapshot"/> 原地填充。
    /// </summary>
    public static MemoryBookMenu CreateRemoteLoading()
    {
        return CreateRemoteLoading(MemoryBookAssets.Fallback);
    }

    internal static MemoryBookMenu CreateRemoteLoading(MemoryBookAssets assets)
    {
        MemoryBookData.Translate translate = TranslateI18n;
        return new MemoryBookMenu(
            new List<MemoryBookNpcSummary>(),
            new Dictionary<string, LivingNpcState>(StringComparer.OrdinalIgnoreCase),
            npcName => DialogueHistoryStore.Instance.GetHistory(npcName),
            () => Game1.player?.Name ?? "Farmer",
            translate,
            assets,
            Game1.Date.TotalDays,
            RemoteLoadState.Loading,
            initializeUi: true);
    }

    /// <summary>无游戏绘制依赖的构造缝，仅用于验证菜单状态与本地历史路由。</summary>
    internal static MemoryBookMenu CreateRemoteLoading(
        Func<string, StardewEventHistory> getHistory,
        Func<string> getFarmerName,
        MemoryBookData.Translate translate)
    {
        return new MemoryBookMenu(
            new List<MemoryBookNpcSummary>(),
            new Dictionary<string, LivingNpcState>(StringComparer.OrdinalIgnoreCase),
            getHistory,
            getFarmerName,
            translate,
            MemoryBookAssets.Fallback,
            capturedTotalDays: -1,
            RemoteLoadState.Loading,
            initializeUi: false);
    }

    private static int SafeHearts(string npcName)
    {
        try
        {
            return Game1.player?.getFriendshipHeartLevelForNPC(npcName) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string TranslateI18n(string key, object? tokens = null)
    {
        return tokens == null ? I18n.Get(key) : I18n.Get(key, tokens);
    }

    // ---- 布局 ----

    private void RebuildLayout()
    {
        int viewportWidth = Game1.uiViewport.Width;
        int viewportHeight = Game1.uiViewport.Height;
        int desiredWidth = Math.Clamp(viewportWidth - 64, 720, 1400);
        int desiredHeight = Math.Clamp(viewportHeight - 64, 500, 820);
        this.width = Math.Min(desiredWidth, Math.Max(320, viewportWidth - 16));
        this.height = Math.Min(desiredHeight, Math.Max(360, viewportHeight - 16));
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.uiViewport.Height - this.height) / 2;

        this.bookBounds = new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height);

        int pageTop = this.yPositionOnScreen + 58;
        int pageBottom = this.yPositionOnScreen + this.height - FooterHeight - 18;
        int innerWidth = Math.Max(240, this.width - 48);
        int pageGap = this.width < 760 ? 16 : 24;
        int minimumRightWidth = Math.Clamp((innerWidth * 55) / 100, 160, 420);
        int availableRosterWidth = Math.Max(120, innerWidth - pageGap - minimumRightWidth);
        int minimumRosterWidth = Math.Min(this.width < 900 ? 240 : 260, availableRosterWidth);
        int maximumRosterWidth = Math.Max(minimumRosterWidth, Math.Min(360, availableRosterWidth));
        int rosterWidth = Math.Clamp((this.width * 29) / 100, minimumRosterWidth, maximumRosterWidth);

        this.leftPageBounds = new Rectangle(
            this.xPositionOnScreen + 24,
            pageTop,
            rosterWidth,
            Math.Max(180, pageBottom - pageTop));
        int rightPageX = this.leftPageBounds.Right + pageGap;
        this.rightPageBounds = new Rectangle(
            rightPageX,
            pageTop,
            Math.Max(120, innerWidth - rosterWidth - pageGap),
            this.leftPageBounds.Height);

        this.rosterHeaderBounds = new Rectangle(
            this.leftPageBounds.X + 18,
            this.leftPageBounds.Y + 18,
            Math.Max(80, this.leftPageBounds.Width - 36),
            44);
        this.rosterBounds = new Rectangle(
            this.leftPageBounds.X + 18,
            this.rosterHeaderBounds.Bottom + 8,
            Math.Max(80, this.leftPageBounds.Width - 36),
            Math.Max(80, this.leftPageBounds.Bottom - this.rosterHeaderBounds.Bottom - 26));

        int profileHeight = this.height < 620 ? 80 : 92;
        this.profileBounds = new Rectangle(
            this.rightPageBounds.X + 20,
            this.rightPageBounds.Y + 18,
            Math.Max(120, this.rightPageBounds.Width - 40),
            profileHeight);
        int tabsY = this.profileBounds.Bottom + 8;
        this.contentBounds = new Rectangle(
            this.rightPageBounds.X + 20,
            tabsY + TabButtonHeight - 4,
            Math.Max(120, this.rightPageBounds.Width - 40),
            Math.Max(60, this.rightPageBounds.Bottom - (tabsY + TabButtonHeight - 4) - 18));
        this.footerBounds = new Rectangle(
            this.xPositionOnScreen + 28,
            pageBottom + 2,
            this.width - 56,
            FooterHeight);

        this.rosterRows.Clear();
        int visibleRows = this.VisibleRosterRows;
        for (int i = 0; i < visibleRows; i++)
        {
            this.rosterRows.Add(new ClickableComponent(
                new Rectangle(
                    this.rosterBounds.X,
                    this.rosterBounds.Y + (i * RosterRowHeight),
                    this.rosterBounds.Width - 48,
                    RosterRowHeight - 8),
                $"row{i}"));
        }

        this.rosterUpArrow = new ClickableTextureComponent(
            new Rectangle(this.rosterBounds.Right - 44, this.rosterBounds.Y, 44, 44),
            Game1.mouseCursors,
            new Rectangle(421, 459, 11, 12),
            4f);
        this.rosterDownArrow = new ClickableTextureComponent(
            new Rectangle(this.rosterBounds.Right - 44, this.rosterBounds.Bottom - 44, 44, 44),
            Game1.mouseCursors,
            new Rectangle(421, 472, 11, 12),
            4f);

        this.tabButtons.Clear();
        MemoryBookTab[] tabs = Enum.GetValues<MemoryBookTab>();
        int tabWidth = this.profileBounds.Width / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            this.tabButtons.Add(new ClickableComponent(
                new Rectangle(
                    this.profileBounds.X + (i * tabWidth),
                    tabsY,
                    tabWidth - 6,
                    TabButtonHeight),
                tabs[i].ToString()));
        }

        this.wrappedForWidth = -1;
        this.draggingContentScrollbar = false;
        this.ClampScrolls();
    }

    private int VisibleRosterRows => Math.Max(1, this.rosterBounds.Height / RosterRowHeight);

    private int VisibleContentHeight => Math.Max(1, this.contentBounds.Height);

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        if (!this.uiInitialized)
        {
            return;
        }

        this.RebuildLayout();
        this.initializeUpperRightCloseButton();
        this.PositionCloseButton();
    }

    private void PositionCloseButton()
    {
        if (this.upperRightCloseButton != null)
        {
            this.upperRightCloseButton.bounds = new Rectangle(
                this.xPositionOnScreen + this.width - 64,
                this.yPositionOnScreen + 12,
                48,
                48);
        }
    }

    // ---- 数据 ----

    internal bool IsRemoteLoading => this.remoteLoadState == RemoteLoadState.Loading;

    internal bool IsRemoteTimedOut => this.remoteLoadState == RemoteLoadState.TimedOut;

    internal bool HasBrowsableContent => this.remoteLoadState == RemoteLoadState.Ready && this.roster.Count > 0;

    internal string? SelectedNpcName
    {
        get
        {
            return this.TryGetSelectedNpc(out MemoryBookNpcSummary? npc) && npc != null
                ? npc.NpcName
                : null;
        }
    }

    internal string StatusText => this.GetStatusText();

    internal int CachedPageCount => this.pageCache.Count;

    /// <summary>把主机快照原地装入当前菜单；成功的空快照与超时是两个不同状态。</summary>
    public void ApplySnapshot(MemoryBookSnapshotSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.ApplySnapshot(source.Roster, source.ExportStates(), source.CapturedTotalDays);
    }

    /// <summary>请求超时时保留菜单壳并显示占位；迟到的有效快照仍可随后覆盖该状态。</summary>
    public void MarkTimedOut()
    {
        if (this.remoteLoadState != RemoteLoadState.Loading)
        {
            return;
        }

        this.remoteLoadState = RemoteLoadState.TimedOut;
        this.ResetTransientViewState();
    }

    internal List<MemoryBookLine> GetPageLines(string npcName, MemoryBookTab tab)
    {
        var cacheKey = (npcName, tab);
        if (this.pageCache.TryGetValue(cacheKey, out List<MemoryBookLine>? cached))
        {
            return cached;
        }

        MemoryBookNpcSummary? summary = this.roster.FirstOrDefault(
            entry => string.Equals(entry.NpcName, npcName, StringComparison.OrdinalIgnoreCase));
        string displayName = summary?.DisplayName ?? npcName;
        int hearts = summary?.Hearts ?? 0;

        // 对话历史属于当前 farmhand 本地文件，绝不依赖主机快照里是否有该 NPC 的行为状态。
        List<MemoryBookLine> lines;
        if (tab == MemoryBookTab.Conversations)
        {
            lines = MemoryBookData.BuildConversationLines(
                this.SafeHistory(npcName),
                displayName,
                this.SafeFarmerName(),
                this.translate);
        }
        else if (!this.statesByName.TryGetValue(npcName, out LivingNpcState? state))
        {
            lines = new List<MemoryBookLine> { new(MemoryBookLineKind.Empty, this.translate("book.memories.empty")) };
        }
        else
        {
            lines = tab switch
            {
                MemoryBookTab.Relationship => MemoryBookData.BuildRelationshipCard(
                    state, displayName, hearts, this.capturedTotalDays, this.translate),
                MemoryBookTab.Memories => MemoryBookData.BuildMemoryLines(state, this.capturedTotalDays, this.translate),
                _ => MemoryBookData.BuildMomentLines(state, this.capturedTotalDays, this.translate)
            };
        }

        this.pageCache[cacheKey] = lines;
        return lines;
    }

    private void ApplySnapshot(
        IEnumerable<MemoryBookNpcSummary> incomingRoster,
        IEnumerable<LivingNpcState> incomingStates,
        int capturedTotalDays)
    {
        string? previouslySelectedNpc = this.SelectedNpcName;
        List<MemoryBookNpcSummary> rosterCopy = incomingRoster
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.NpcName))
            .GroupBy(entry => entry.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(entry => new MemoryBookNpcSummary(
                entry.NpcName,
                entry.DisplayName,
                entry.Hearts,
                entry.LastContactTotalDays,
                entry.SubtitleText))
            .ToList();
        Dictionary<string, LivingNpcState> stateCopies = incomingStates
            .Where(state => state != null && !string.IsNullOrWhiteSpace(state.NpcName))
            .GroupBy(state => state.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Clone(),
                StringComparer.OrdinalIgnoreCase);

        this.roster.Clear();
        this.roster.AddRange(rosterCopy);
        this.statesByName.Clear();
        foreach ((string npcName, LivingNpcState state) in stateCopies)
        {
            this.statesByName[npcName] = state;
        }

        this.capturedTotalDays = capturedTotalDays;
        this.remoteLoadState = RemoteLoadState.Ready;
        int preservedIndex = previouslySelectedNpc == null
            ? -1
            : this.roster.FindIndex(entry => string.Equals(
                entry.NpcName,
                previouslySelectedNpc,
                StringComparison.OrdinalIgnoreCase));
        this.selectedRosterIndex = preservedIndex >= 0 ? preservedIndex : 0;
        this.ResetTransientViewState();

        if (this.uiInitialized)
        {
            this.RebuildLayout();
        }
        else
        {
            this.ClampScrolls();
        }
    }

    private bool TryGetSelectedNpc(out MemoryBookNpcSummary? npc)
    {
        if (!this.HasBrowsableContent)
        {
            npc = null;
            return false;
        }

        this.selectedRosterIndex = Math.Clamp(this.selectedRosterIndex, 0, this.roster.Count - 1);
        npc = this.roster[this.selectedRosterIndex];
        return true;
    }

    private string GetStatusText()
    {
        string key = this.remoteLoadState switch
        {
            RemoteLoadState.Loading => "book.remote.loading",
            RemoteLoadState.TimedOut => "book.remote.timeout",
            _ when this.roster.Count == 0 => "book.remote.empty",
            _ => string.Empty
        };
        return string.IsNullOrEmpty(key) ? string.Empty : this.translate(key);
    }

    private void ResetTransientViewState()
    {
        this.pageCache.Clear();
        this.wrappedLines.Clear();
        this.wrappedForWidth = -1;
        this.wrappedForPage = null;
        this.rosterScrollIndex = 0;
        this.contentScroll = 0f;
        this.contentHeight = 0f;
        this.hoverText = string.Empty;
        this.hoveredRosterIndex = -1;
        this.hoveredTab = null;
        this.contentScrollbarHovered = false;
        this.draggingContentScrollbar = false;
        this.contentScrollbarDragOffset = 0;
    }

    private StardewEventHistory SafeHistory(string npcName)
    {
        try
        {
            return this.getHistory(npcName);
        }
        catch
        {
            return new StardewEventHistory { NpcName = npcName };
        }
    }

    private string SafeFarmerName()
    {
        try
        {
            string name = this.getFarmerName();
            return string.IsNullOrWhiteSpace(name) ? "Farmer" : name;
        }
        catch
        {
            return "Farmer";
        }
    }

    private void EnsureWrapped()
    {
        if (!this.TryGetSelectedNpc(out MemoryBookNpcSummary? selectedNpc) || selectedNpc == null)
        {
            this.wrappedLines.Clear();
            this.wrappedForPage = null;
            this.contentHeight = 0f;
            return;
        }

        int layoutWidth = this.contentBounds.Width;
        var page = (selectedNpc.NpcName, this.activeTab);
        if (this.wrappedForWidth == layoutWidth && this.wrappedForPage == page)
        {
            return;
        }

        this.wrappedLines.Clear();
        foreach (MemoryBookLine line in this.GetPageLines(page.NpcName, this.activeTab))
        {
            SpriteFont font = this.FontFor(line.Kind);
            string wrapped = Game1.parseText(line.Text, font, this.ContentTextWidthFor(line.Kind));
            float height = font.MeasureString(wrapped).Y + this.SpacingFor(line.Kind);
            this.wrappedLines.Add((line, wrapped, height));
        }

        this.contentHeight = this.wrappedLines.Sum(entry => entry.Height) + ContentPadding * 2;
        this.wrappedForWidth = layoutWidth;
        this.wrappedForPage = page;
        this.ClampScrolls();
    }

    private SpriteFont FontFor(MemoryBookLineKind kind)
    {
        return kind == MemoryBookLineKind.SectionHeader ? Game1.dialogueFont : Game1.smallFont;
    }

    private int ContentTextWidthFor(MemoryBookLineKind kind)
    {
        int usableWidth = Math.Max(36, this.contentBounds.Width - (ContentPadding * 2) - 28);
        int reservedWidth = kind switch
        {
            MemoryBookLineKind.SectionHeader => 56,
            MemoryBookLineKind.PlayerLine or MemoryBookLineKind.NpcLine => 80,
            MemoryBookLineKind.DateSeparator => 56,
            MemoryBookLineKind.MemoryFact => 34,
            MemoryBookLineKind.Muted => 32,
            MemoryBookLineKind.Empty => 24,
            _ => 28
        };
        return Math.Max(16, usableWidth - reservedWidth);
    }

    private float SpacingFor(MemoryBookLineKind kind)
    {
        return kind switch
        {
            MemoryBookLineKind.SectionHeader => 28f,
            MemoryBookLineKind.PlayerLine or MemoryBookLineKind.NpcLine => 32f,
            MemoryBookLineKind.DateSeparator => 22f,
            MemoryBookLineKind.MemoryFact => 14f,
            MemoryBookLineKind.Muted => 12f,
            MemoryBookLineKind.Empty => 88f,
            _ => 12f
        };
    }

    private static Color ColorFor(MemoryBookLineKind kind)
    {
        return kind switch
        {
            MemoryBookLineKind.SectionHeader => MemoryBookPalette.Ink,
            MemoryBookLineKind.PlayerLine => MemoryBookPalette.Conversations,
            MemoryBookLineKind.NpcLine => MemoryBookPalette.Ink,
            MemoryBookLineKind.DateSeparator => MemoryBookPalette.Muted,
            MemoryBookLineKind.MemoryFact => MemoryBookPalette.Ink,
            MemoryBookLineKind.Muted => MemoryBookPalette.Muted,
            MemoryBookLineKind.Empty => MemoryBookPalette.Muted,
            _ => MemoryBookPalette.Ink
        };
    }

    private void ClampScrolls()
    {
        this.rosterScrollIndex = Math.Clamp(this.rosterScrollIndex, 0, Math.Max(0, this.roster.Count - this.VisibleRosterRows));
        float maxScroll = Math.Max(0f, this.contentHeight - this.VisibleContentHeight);
        this.contentScroll = Math.Clamp(this.contentScroll, 0f, maxScroll);
    }

    internal void SelectRoster(int index, bool playSound = true)
    {
        if (!this.HasBrowsableContent)
        {
            return;
        }

        index = Math.Clamp(index, 0, this.roster.Count - 1);
        if (index == this.selectedRosterIndex)
        {
            return;
        }

        this.selectedRosterIndex = index;
        this.contentScroll = 0f;
        this.draggingContentScrollbar = false;
        if (index < this.rosterScrollIndex)
        {
            this.rosterScrollIndex = index;
        }
        else if (index >= this.rosterScrollIndex + this.VisibleRosterRows)
        {
            this.rosterScrollIndex = index - this.VisibleRosterRows + 1;
        }

        if (playSound && this.uiInitialized)
        {
            Game1.playSound("smallSelect");
        }
    }

    private void SelectTab(MemoryBookTab tab, bool playSound = true)
    {
        if (tab == this.activeTab)
        {
            return;
        }

        this.activeTab = tab;
        this.contentScroll = 0f;
        this.draggingContentScrollbar = false;
        if (playSound && this.uiInitialized)
        {
            Game1.playSound("shwip");
        }
    }

    // ---- 输入 ----

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (!this.HasBrowsableContent)
        {
            if (this.uiInitialized)
            {
                base.receiveLeftClick(x, y, playSound);
            }

            return;
        }

        base.receiveLeftClick(x, y, playSound);

        for (int i = 0; i < this.rosterRows.Count; i++)
        {
            if (this.rosterRows[i].containsPoint(x, y) && this.rosterScrollIndex + i < this.roster.Count)
            {
                this.SelectRoster(this.rosterScrollIndex + i);
                return;
            }
        }

        foreach (ClickableComponent tabButton in this.tabButtons)
        {
            if (tabButton.containsPoint(x, y) && Enum.TryParse(tabButton.name, out MemoryBookTab tab))
            {
                this.SelectTab(tab);
                return;
            }
        }

        if (this.rosterUpArrow?.containsPoint(x, y) == true)
        {
            this.ScrollRoster(-1);
            return;
        }

        if (this.rosterDownArrow?.containsPoint(x, y) == true)
        {
            this.ScrollRoster(1);
            return;
        }

        this.EnsureWrapped();
        if (this.TryGetContentScrollbarGeometry(out Rectangle track, out Rectangle thumb)
            && ContentScrollbarHitBounds(track).Contains(x, y))
        {
            this.draggingContentScrollbar = true;
            this.contentScrollbarHovered = true;
            this.contentScrollbarDragOffset = thumb.Contains(x, y)
                ? y - thumb.Y
                : thumb.Height / 2;
            this.SetContentScrollFromThumbTop(y - this.contentScrollbarDragOffset, track, thumb);
            if (this.uiInitialized)
            {
                Game1.playSound("smallSelect");
            }
        }
    }

    public override void leftClickHeld(int x, int y)
    {
        if (!this.HasBrowsableContent || !this.draggingContentScrollbar)
        {
            base.leftClickHeld(x, y);
            return;
        }

        this.EnsureWrapped();
        if (this.TryGetContentScrollbarGeometry(out Rectangle track, out Rectangle thumb))
        {
            this.SetContentScrollFromThumbTop(y - this.contentScrollbarDragOffset, track, thumb);
        }
        else
        {
            this.draggingContentScrollbar = false;
        }
    }

    public override void releaseLeftClick(int x, int y)
    {
        this.draggingContentScrollbar = false;
        base.releaseLeftClick(x, y);
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (!this.HasBrowsableContent)
        {
            return;
        }

        base.receiveScrollWheelAction(direction);
        Point cursor = Game1.getMousePosition(true);
        if (this.rosterBounds.Contains(cursor))
        {
            this.ScrollRoster(direction > 0 ? -1 : 1);
            return;
        }

        this.ScrollContent(direction > 0 ? -84f : 84f);
    }

    private void ScrollRoster(int delta)
    {
        if (!this.HasBrowsableContent)
        {
            return;
        }

        int previous = this.rosterScrollIndex;
        this.rosterScrollIndex = Math.Clamp(
            this.rosterScrollIndex + delta,
            0,
            Math.Max(0, this.roster.Count - this.VisibleRosterRows));
        if (previous != this.rosterScrollIndex && this.uiInitialized)
        {
            Game1.playSound("shiny4");
        }
    }

    private void ScrollContent(float delta)
    {
        if (!this.HasBrowsableContent)
        {
            return;
        }

        this.contentScroll += delta;
        this.ClampScrolls();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key is Keys.Escape)
        {
            this.exitThisMenu();
            return;
        }

        if (!this.HasBrowsableContent)
        {
            return;
        }

        switch (key)
        {
            case Keys.Up:
                this.ScrollContent(-64f);
                return;
            case Keys.Down:
                this.ScrollContent(64f);
                return;
            case Keys.Left:
                this.SelectTab(PreviousTab(this.activeTab));
                return;
            case Keys.Right:
                this.SelectTab(NextTab(this.activeTab));
                return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons b)
    {
        if (b == Buttons.B)
        {
            this.exitThisMenu();
            return;
        }

        if (!this.HasBrowsableContent)
        {
            return;
        }

        switch (b)
        {
            case Buttons.LeftShoulder:
                this.SelectTab(PreviousTab(this.activeTab));
                return;
            case Buttons.RightShoulder:
                this.SelectTab(NextTab(this.activeTab));
                return;
            case Buttons.LeftTrigger:
                this.SelectRoster(this.selectedRosterIndex - 1);
                return;
            case Buttons.RightTrigger:
                this.SelectRoster(this.selectedRosterIndex + 1);
                return;
            case Buttons.DPadUp:
                this.ScrollContent(-64f);
                return;
            case Buttons.DPadDown:
                this.ScrollContent(64f);
                return;
        }
    }

    private static MemoryBookTab NextTab(MemoryBookTab tab)
    {
        MemoryBookTab[] tabs = Enum.GetValues<MemoryBookTab>();
        return tabs[(Array.IndexOf(tabs, tab) + 1) % tabs.Length];
    }

    private static MemoryBookTab PreviousTab(MemoryBookTab tab)
    {
        MemoryBookTab[] tabs = Enum.GetValues<MemoryBookTab>();
        return tabs[(Array.IndexOf(tabs, tab) - 1 + tabs.Length) % tabs.Length];
    }

    public override void performHoverAction(int x, int y)
    {
        this.hoverText = string.Empty;
        this.hoveredRosterIndex = -1;
        this.hoveredTab = null;
        this.contentScrollbarHovered = false;
        if (!this.HasBrowsableContent)
        {
            if (this.uiInitialized)
            {
                base.performHoverAction(x, y);
            }

            return;
        }

        base.performHoverAction(x, y);
        this.rosterUpArrow?.tryHover(x, y);
        this.rosterDownArrow?.tryHover(x, y);

        for (int i = 0; i < this.rosterRows.Count; i++)
        {
            int rosterIndex = this.rosterScrollIndex + i;
            if (rosterIndex < this.roster.Count && this.rosterRows[i].containsPoint(x, y))
            {
                this.hoveredRosterIndex = rosterIndex;
                break;
            }
        }

        foreach (ClickableComponent tabButton in this.tabButtons)
        {
            if (tabButton.containsPoint(x, y) && Enum.TryParse(tabButton.name, out MemoryBookTab tab))
            {
                this.hoveredTab = tab;
                break;
            }
        }

        this.EnsureWrapped();
        if (this.TryGetContentScrollbarGeometry(out Rectangle track, out _)
            && ContentScrollbarHitBounds(track).Contains(x, y))
        {
            this.contentScrollbarHovered = true;
            return;
        }

        if (this.draggingContentScrollbar)
        {
            return;
        }

        if (!this.contentBounds.Contains(x, y))
        {
            return;
        }

        float lineY = this.contentBounds.Y + ContentPadding - this.contentScroll;
        foreach ((MemoryBookLine line, string _, float height) in this.wrappedLines)
        {
            if (!string.IsNullOrEmpty(line.HoverText)
                && y >= lineY
                && y < lineY + height
                && lineY > this.contentBounds.Y - height
                && lineY < this.contentBounds.Bottom)
            {
                this.hoverText = line.HoverText;
                return;
            }

            lineY += height;
        }
    }

    // ---- 绘制 ----

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.58f);
        this.DrawBookShell(b);
        this.DrawTitle(b);

        if (!this.HasBrowsableContent)
        {
            this.DrawStatusPlaceholder(b);
            this.DrawFooter(b);
            base.draw(b);
            this.drawMouse(b);
            return;
        }

        this.DrawRoster(b);
        this.DrawProfileHeader(b);
        this.DrawTabs(b);
        this.DrawContent(b);
        this.DrawFooter(b);

        base.draw(b);

        if (!string.IsNullOrEmpty(this.hoverText))
        {
            drawHoverText(b, this.hoverText, Game1.smallFont);
        }

        this.drawMouse(b);
    }

    private void DrawBookShell(SpriteBatch b)
    {
        this.DrawFrame(b, MemoryBookFrame.Cover, this.bookBounds, Color.White, 4f, drawShadow: true);
        this.DrawFrame(b, MemoryBookFrame.Page, this.leftPageBounds, Color.White, 3f, drawShadow: false);
        this.DrawFrame(b, MemoryBookFrame.Page, this.rightPageBounds, Color.White, 3f, drawShadow: false);

        this.DrawPaperPattern(b, this.leftPageBounds);
        this.DrawPaperPattern(b, this.rightPageBounds);

        if (!this.assets.HasCustomArt || this.assets.Texture == null)
        {
            var divider = new Rectangle(
                this.leftPageBounds.Right + ((this.rightPageBounds.X - this.leftPageBounds.Right - 8) / 2),
                this.leftPageBounds.Y + 8,
                8,
                this.leftPageBounds.Height - 16);
            b.Draw(Game1.staminaRect, divider, MemoryBookPalette.LeatherDark * 0.72f);
            return;
        }

        Texture2D texture = this.assets.Texture;
        int spineWidth = MemoryBookAssets.SpineSource.Width * 3;
        int spineX = this.leftPageBounds.Right
            + ((this.rightPageBounds.X - this.leftPageBounds.Right - spineWidth) / 2);
        int tileHeight = MemoryBookAssets.SpineSource.Height * 3;
        for (int y = this.leftPageBounds.Y + 12; y < this.leftPageBounds.Bottom - 12; y += tileHeight)
        {
            int height = Math.Min(tileHeight, this.leftPageBounds.Bottom - 12 - y);
            b.Draw(
                texture,
                new Rectangle(spineX, y, spineWidth, height),
                MemoryBookAssets.SpineSource,
                Color.White);
        }

        b.Draw(
            texture,
            new Rectangle(this.bookBounds.X + 10, this.bookBounds.Y + 6, 72, 72),
            MemoryBookAssets.SprigSource,
            Color.White);
        b.Draw(
            texture,
            new Rectangle(this.bookBounds.Right - 82, this.bookBounds.Bottom - 82, 72, 72),
            MemoryBookAssets.FlowerSource,
            Color.White);

        this.DrawIcon(
            b,
            MemoryBookIcon.Sparkle,
            new Rectangle(this.bookBounds.Right - 150, this.bookBounds.Y + 12, 32, 32));
        this.DrawIcon(
            b,
            MemoryBookIcon.Sparkle,
            new Rectangle(this.bookBounds.Right - 112, this.bookBounds.Y + 28, 16, 16));
    }

    private void DrawTitle(SpriteBatch b)
    {
        int bannerScale = Math.Clamp((this.bookBounds.Width - 80) / MemoryBookAssets.TitleBannerSource.Width, 2, 4);
        int bannerWidth = MemoryBookAssets.TitleBannerSource.Width * bannerScale;
        int bannerHeight = MemoryBookAssets.TitleBannerSource.Height * bannerScale;
        Rectangle banner = new(
            this.bookBounds.X + (this.bookBounds.Width - bannerWidth) / 2,
            this.bookBounds.Y - 4,
            bannerWidth,
            bannerHeight);

        if (this.assets.Texture != null)
        {
            b.Draw(this.assets.Texture, banner, MemoryBookAssets.TitleBannerSource, Color.White);
        }
        else
        {
            this.DrawFrame(b, MemoryBookFrame.Header, banner, Color.White, 3f, drawShadow: true);
        }

        string title = this.translate("book.title");
        SpriteFont titleFont = Game1.dialogueFont;
        Vector2 size = titleFont.MeasureString(title);
        if (size.X > banner.Width - 48)
        {
            titleFont = Game1.smallFont;
            size = titleFont.MeasureString(title);
        }

        var position = new Vector2(
            banner.X + (banner.Width - size.X) / 2f,
            banner.Y + (banner.Height - size.Y) / 2f - 2f);
        b.DrawString(titleFont, title, position + new Vector2(2f, 3f), MemoryBookPalette.Shadow * 0.38f);
        b.DrawString(titleFont, title, position, MemoryBookPalette.Ink);
    }

    private void DrawStatusPlaceholder(SpriteBatch b)
    {
        Rectangle note = new(
            this.bookBounds.X + Math.Max(72, this.bookBounds.Width / 7),
            this.bookBounds.Y + (this.bookBounds.Height - 220) / 2,
            this.bookBounds.Width - Math.Max(144, (this.bookBounds.Width / 7) * 2),
            220);
        this.DrawFrame(b, MemoryBookFrame.Status, note, Color.White, 3f, drawShadow: true);

        Rectangle iconBounds = new(note.X + (note.Width - 64) / 2, note.Y + 26, 64, 64);
        this.DrawIcon(b, MemoryBookIcon.EmptyBook, iconBounds);

        string text = Game1.parseText(
            this.GetStatusText(),
            Game1.dialogueFont,
            Math.Max(180, note.Width - 72));
        Vector2 size = Game1.dialogueFont.MeasureString(text);
        b.DrawString(
            Game1.dialogueFont,
            text,
            new Vector2(
                note.X + (note.Width - size.X) / 2f,
                note.Y + 112),
            MemoryBookPalette.Muted);
    }

    private void DrawRoster(SpriteBatch b)
    {
        this.DrawFrame(b, MemoryBookFrame.Header, this.rosterHeaderBounds, Color.White, 2f, drawShadow: false);
        this.DrawIcon(
            b,
            MemoryBookIcon.Villagers,
            new Rectangle(this.rosterHeaderBounds.X + 10, this.rosterHeaderBounds.Y + 6, 32, 32));

        int countReserve = this.rosterHeaderBounds.Width >= 220 ? 48 : 0;
        string header = FitSingleLine(
            Game1.smallFont,
            this.translate("book.roster.title"),
            Math.Max(8, this.rosterHeaderBounds.Width - 60 - countReserve));
        Vector2 headerSize = Game1.smallFont.MeasureString(header);
        b.DrawString(
            Game1.smallFont,
            header,
            new Vector2(this.rosterHeaderBounds.X + 48, this.rosterHeaderBounds.Y + (this.rosterHeaderBounds.Height - headerSize.Y) / 2f),
            MemoryBookPalette.Ink);

        if (this.rosterHeaderBounds.Width >= 220)
        {
            string countText = this.roster.Count.ToString();
            Vector2 countSize = Game1.smallFont.MeasureString(countText);
            b.DrawString(
                Game1.smallFont,
                countText,
                new Vector2(this.rosterHeaderBounds.Right - countSize.X - 12, this.rosterHeaderBounds.Y + (this.rosterHeaderBounds.Height - countSize.Y) / 2f),
                MemoryBookPalette.Muted);
        }

        int visibleRows = this.VisibleRosterRows;
        for (int i = 0; i < visibleRows; i++)
        {
            int rosterIndex = this.rosterScrollIndex + i;
            if (rosterIndex >= this.roster.Count)
            {
                break;
            }

            MemoryBookNpcSummary entry = this.roster[rosterIndex];
            Rectangle rowBounds = this.rosterRows[i].bounds;
            bool selected = rosterIndex == this.selectedRosterIndex;
            bool hovered = rosterIndex == this.hoveredRosterIndex;
            bool compact = rowBounds.Width < 260;

            this.DrawFrame(
                b,
                selected ? MemoryBookFrame.SelectedRow : MemoryBookFrame.Roster,
                rowBounds,
                hovered && !selected ? Color.White * 0.92f : Color.White,
                2f,
                drawShadow: selected);

            if (selected && this.assets.Texture != null)
            {
                b.Draw(
                    this.assets.Texture,
                    new Rectangle(rowBounds.X - 12, rowBounds.Y + 18, 24, 40),
                    MemoryBookAssets.BookmarkSource,
                    Color.White);
            }

            int portraitSize = compact ? 48 : 64;
            Rectangle portraitBounds = new(
                rowBounds.X + 8,
                rowBounds.Y + (rowBounds.Height - portraitSize) / 2,
                portraitSize,
                portraitSize);
            this.DrawPortrait(b, entry.NpcName, portraitBounds, selected);

            int textX = portraitBounds.Right + (compact ? 10 : 12);
            int availableTextWidth = Math.Max(8, rowBounds.Right - textX - 8);
            string displayName = FitSingleLine(Game1.smallFont, entry.DisplayName, availableTextWidth);
            b.DrawString(
                Game1.smallFont,
                displayName,
                new Vector2(textX, rowBounds.Y + 8),
                selected ? MemoryBookPalette.Relationship : MemoryBookPalette.Ink);

            if (compact)
            {
                float detailLineHeight = Game1.smallFont.LineSpacing;
                float detailY = rowBounds.Bottom - detailLineHeight - 6;
                string detail = FitSingleLine(
                    Game1.smallFont,
                    $"{Math.Clamp(entry.Hearts, 0, 10)}/10 · {entry.SubtitleText}",
                    Math.Max(8, availableTextWidth - 22));
                int iconY = (int)Math.Round(detailY + Math.Max(0f, (detailLineHeight - 16f) / 2f));
                this.DrawIcon(b, MemoryBookIcon.Relationship, new Rectangle(textX, iconY, 16, 16));
                b.DrawString(
                    Game1.smallFont,
                    detail,
                    new Vector2(textX + 22, detailY),
                    MemoryBookPalette.Relationship);
            }
            else
            {
                this.DrawHearts(b, textX, rowBounds.Y + 34, entry.Hearts);
                string subtitle = FitSingleLine(Game1.smallFont, entry.SubtitleText, availableTextWidth);
                Vector2 subtitleSize = Game1.smallFont.MeasureString(subtitle);
                b.DrawString(
                    Game1.smallFont,
                    subtitle,
                    new Vector2(textX, rowBounds.Bottom - subtitleSize.Y - 4),
                    MemoryBookPalette.Muted);
            }
        }

        if (this.rosterScrollIndex > 0)
        {
            this.DrawArrowButton(b, this.rosterUpArrow, MemoryBookIcon.ArrowUp);
        }

        if (this.rosterScrollIndex + visibleRows < this.roster.Count)
        {
            this.DrawArrowButton(b, this.rosterDownArrow, MemoryBookIcon.ArrowDown);
        }
    }

    private void DrawPortrait(SpriteBatch b, string npcName, Rectangle bounds, bool selected)
    {
        Rectangle outer = Inflate(bounds, 4);
        b.Draw(Game1.staminaRect, outer, MemoryBookPalette.Ink);
        b.Draw(Game1.staminaRect, Inset(outer, 2), selected ? MemoryBookPalette.GoldLight : MemoryBookPalette.PaperShadow);
        b.Draw(Game1.staminaRect, Inset(outer, 4), MemoryBookPalette.PaperBright);

        NPC? npc = Game1.getCharacterFromName(npcName);
        if (npc?.Portrait != null)
        {
            b.Draw(npc.Portrait, bounds, new Rectangle(0, 0, 64, 64), Color.White);
        }
        else
        {
            b.Draw(
                Game1.mouseCursors,
                bounds,
                new Rectangle(540, 333, 12, 14),
                Color.White * 0.72f);
        }
    }

    private void DrawArrowButton(SpriteBatch b, ClickableTextureComponent? button, MemoryBookIcon icon)
    {
        if (button == null)
        {
            return;
        }

        Point cursor = Game1.getMousePosition(true);
        bool hovered = button.containsPoint(cursor.X, cursor.Y);
        this.DrawFrame(
            b,
            hovered ? MemoryBookFrame.TabActive : MemoryBookFrame.TabIdle,
            button.bounds,
            hovered ? Color.White : Color.White * 0.9f,
            2f,
            drawShadow: false);
        this.DrawIcon(b, icon, Inset(button.bounds, 6));
    }

    private void DrawHearts(SpriteBatch b, int x, int y, int hearts)
    {
        int shown = Math.Min(10, Math.Max(0, hearts));
        for (int i = 0; i < 10; i++)
        {
            Rectangle source = i < shown ? new Rectangle(211, 428, 7, 6) : new Rectangle(218, 428, 7, 6);
            b.Draw(
                Game1.mouseCursors,
                new Vector2(x + (i * 16), y),
                source,
                Color.White,
                0f,
                Vector2.Zero,
                2f,
                SpriteEffects.None,
                0.88f);
        }
    }

    private void DrawProfileHeader(SpriteBatch b)
    {
        if (!this.TryGetSelectedNpc(out MemoryBookNpcSummary? selectedNpc) || selectedNpc == null)
        {
            return;
        }

        this.DrawFrame(b, MemoryBookFrame.Roster, this.profileBounds, Color.White, 2f, drawShadow: false);

        Rectangle portrait = new(
            this.profileBounds.X + 14,
            this.profileBounds.Y + (this.profileBounds.Height - 64) / 2,
            64,
            64);
        this.DrawPortrait(b, selectedNpc.NpcName, portrait, selected: true);

        int textX = portrait.Right + 18;
        int iconReserve = this.profileBounds.Width >= 520 ? 68 : 12;
        int availableNameWidth = Math.Max(60, this.profileBounds.Right - textX - iconReserve);
        string profileLabel = FitSingleLine(Game1.smallFont, this.translate("book.profile.label"), availableNameWidth);
        b.DrawString(
            Game1.smallFont,
            profileLabel,
            new Vector2(textX, this.profileBounds.Y + 8),
            MemoryBookPalette.Muted);

        SpriteFont nameFont = Game1.dialogueFont;
        Vector2 nameSize = nameFont.MeasureString(selectedNpc.DisplayName);
        if (nameSize.X > availableNameWidth || nameSize.Y > this.profileBounds.Height - 36)
        {
            nameFont = Game1.smallFont;
        }

        string profileName = FitSingleLine(nameFont, selectedNpc.DisplayName, availableNameWidth);
        nameSize = nameFont.MeasureString(profileName);

        float nameY = this.profileBounds.Y + Math.Min(28, Math.Max(20, this.profileBounds.Height - (int)nameSize.Y - 22));
        b.DrawString(
            nameFont,
            profileName,
            new Vector2(textX, nameY),
            MemoryBookPalette.Ink);

        int heartY = this.profileBounds.Bottom - 24;
        bool compact = this.profileBounds.Width < 430;
        if (compact)
        {
            this.DrawIcon(b, MemoryBookIcon.Relationship, new Rectangle(textX, heartY + 1, 16, 16));
            b.DrawString(
                Game1.smallFont,
                $"{Math.Clamp(selectedNpc.Hearts, 0, 10)}/10",
                new Vector2(textX + 22, heartY - 2),
                MemoryBookPalette.Relationship);
        }
        else
        {
            this.DrawHearts(b, textX, heartY, selectedNpc.Hearts);
        }

        int subtitleX = textX + (compact ? 90 : 178);
        int subtitleWidth = this.profileBounds.Right - subtitleX - iconReserve;
        if (subtitleWidth >= 100)
        {
            string subtitle = FitSingleLine(Game1.smallFont, selectedNpc.SubtitleText, subtitleWidth);
            b.DrawString(
                Game1.smallFont,
                subtitle,
                new Vector2(subtitleX, heartY - 10),
                MemoryBookPalette.Muted);
        }

        if (this.profileBounds.Width >= 520)
        {
            Rectangle seal = new(this.profileBounds.Right - 62, this.profileBounds.Y + 16, 48, 48);
            this.DrawFrame(b, MemoryBookFrame.TabActive, seal, Color.White, 2f, drawShadow: true);
            this.DrawIcon(b, IconFor(this.activeTab), Inset(seal, 8), highlighted: true);
        }
    }

    private void DrawTabs(SpriteBatch b)
    {
        foreach (ClickableComponent tabButton in this.tabButtons)
        {
            if (!Enum.TryParse(tabButton.name, out MemoryBookTab tab))
            {
                continue;
            }

            bool active = tab == this.activeTab;
            bool hovered = this.hoveredTab == tab;
            Rectangle bounds = tabButton.bounds;
            int lift = active ? 0 : 6;
            Rectangle drawnBounds = new(bounds.X, bounds.Y + lift, bounds.Width, bounds.Height - lift);
            this.DrawFrame(
                b,
                active ? MemoryBookFrame.TabActive : MemoryBookFrame.TabIdle,
                drawnBounds,
                hovered && !active ? Color.White * 0.92f : Color.White,
                2f,
                drawShadow: active);

            Rectangle iconBounds = new(
                drawnBounds.X + (drawnBounds.Width - 32) / 2,
                drawnBounds.Y + 4,
                32,
                32);
            this.DrawIcon(b, IconFor(tab), iconBounds, highlighted: active);

            string label = FitSingleLine(
                Game1.smallFont,
                this.translate($"book.tab.{tabButton.name.ToLowerInvariant()}"),
                Math.Max(8, drawnBounds.Width - 12));
            Vector2 size = Game1.smallFont.MeasureString(label);
            b.DrawString(
                Game1.smallFont,
                label,
                new Vector2(
                    drawnBounds.X + (drawnBounds.Width - size.X) / 2,
                    drawnBounds.Bottom - size.Y - 7),
                active ? MemoryBookPalette.Ink : MemoryBookPalette.Muted);

            Color accent = AccentFor(tab);
            b.Draw(
                Game1.staminaRect,
                new Rectangle(drawnBounds.X + 10, drawnBounds.Bottom - 5, Math.Max(8, drawnBounds.Width - 20), 3),
                accent * (active ? 1f : 0.45f));
        }
    }

    private void DrawContent(SpriteBatch b)
    {
        this.EnsureWrapped();

        this.DrawFrame(b, MemoryBookFrame.Content, this.contentBounds, Color.White, 3f, drawShadow: false);
        this.DrawPaperPattern(b, this.contentBounds);

        Rectangle ruledArea = Inset(this.contentBounds, 14);
        for (int y = ruledArea.Y + 42; y < ruledArea.Bottom - 8; y += 44)
        {
            b.Draw(
                Game1.staminaRect,
                new Rectangle(ruledArea.X + 12, y, Math.Max(8, ruledArea.Width - 40), 1),
                MemoryBookPalette.PaperShadow * 0.28f);
        }

        Rectangle clip = new(
            this.contentBounds.X + 12,
            this.contentBounds.Y + 12,
            Math.Max(1, this.contentBounds.Width - 48),
            Math.Max(1, this.contentBounds.Height - 24));

        b.End();
        Rectangle previousScissor = b.GraphicsDevice.ScissorRectangle;
        b.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            null,
            ScissorRasterizer);
        b.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(clip, b.GraphicsDevice.Viewport.Bounds);

        float lineY = this.contentBounds.Y + ContentPadding - this.contentScroll;
        foreach ((MemoryBookLine line, string wrapped, float height) in this.wrappedLines)
        {
            if (lineY + height > this.contentBounds.Y && lineY < this.contentBounds.Bottom)
            {
                this.DrawContentLine(b, line, wrapped, lineY);
            }

            lineY += height;
        }

        b.End();
        b.GraphicsDevice.ScissorRectangle = previousScissor;
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

        this.DrawContentScrollbar(b);
    }

    private void DrawContentLine(SpriteBatch b, MemoryBookLine line, string wrapped, float lineY)
    {
        SpriteFont font = this.FontFor(line.Kind);
        Vector2 textSize = font.MeasureString(wrapped);
        int x = this.contentBounds.X + ContentPadding;
        int y = (int)Math.Round(lineY);
        int usableWidth = Math.Max(36, this.contentBounds.Width - (ContentPadding * 2) - 28);

        switch (line.Kind)
        {
            case MemoryBookLineKind.SectionHeader:
            {
                Rectangle header = new(x, y + 2, usableWidth, Math.Max(38, (int)Math.Ceiling(textSize.Y) + 12));
                this.DrawFrame(b, MemoryBookFrame.Header, header, Color.White, 2f, drawShadow: false);
                MemoryBookIcon icon = line.MemoryKind == "promise" ? MemoryBookIcon.Promise : IconFor(this.activeTab);
                // Localized fonts can make the header taller than the 32px icon.
                Rectangle iconBounds = new(header.X + 8, header.Y + (header.Height - 32) / 2, 32, 32);
                this.DrawIcon(b, icon, iconBounds);
                b.DrawString(font, wrapped, new Vector2(header.X + 48, header.Y + 6), ColorFor(line.Kind));
                break;
            }

            case MemoryBookLineKind.PlayerLine:
            case MemoryBookLineKind.NpcLine:
            {
                bool player = line.Kind == MemoryBookLineKind.PlayerLine;
                int offset = player ? 24 : 0;
                Rectangle note = new(
                    x + offset,
                    y + 2,
                    Math.Max(72, usableWidth - 24),
                    Math.Max(48, (int)Math.Ceiling(textSize.Y) + 18));
                this.DrawFrame(
                    b,
                    player ? MemoryBookFrame.PlayerNote : MemoryBookFrame.NpcNote,
                    note,
                    Color.White,
                    2f,
                    drawShadow: false);
                this.DrawIcon(
                    b,
                    player ? MemoryBookIcon.Quill : MemoryBookIcon.Conversations,
                    new Rectangle(note.X + 8, note.Y + 8, 32, 32));
                b.DrawString(font, wrapped, new Vector2(note.X + 48, note.Y + 7), ColorFor(line.Kind));
                break;
            }

            case MemoryBookLineKind.DateSeparator:
            {
                int combinedWidth = (int)Math.Ceiling(textSize.X) + 40;
                int centerX = this.contentBounds.X + (this.contentBounds.Width - 24) / 2;
                int labelX = centerX - combinedWidth / 2;
                int lineWidth = Math.Max(0, (usableWidth - combinedWidth - 24) / 2);
                int ruleY = y + 16;
                if (lineWidth > 0)
                {
                    b.Draw(Game1.staminaRect, new Rectangle(x, ruleY, lineWidth, 2), MemoryBookPalette.Gold * 0.62f);
                    b.Draw(
                        Game1.staminaRect,
                        new Rectangle(centerX + combinedWidth / 2 + 12, ruleY, lineWidth, 2),
                        MemoryBookPalette.Gold * 0.62f);
                }

                this.DrawIcon(b, MemoryBookIcon.Calendar, new Rectangle(labelX, y, 32, 32));
                b.DrawString(font, wrapped, new Vector2(labelX + 40, y), ColorFor(line.Kind));
                break;
            }

            case MemoryBookLineKind.MemoryFact:
                this.DrawIcon(b, MemoryBookIcon.Pin, new Rectangle(x + 4, y + 4, 16, 16));
                b.DrawString(font, wrapped, new Vector2(x + 26, y), ColorFor(line.Kind));
                break;

            case MemoryBookLineKind.Muted:
                this.DrawIcon(b, MemoryBookIcon.Clock, new Rectangle(x + 2, y + 4, 16, 16));
                b.DrawString(font, wrapped, new Vector2(x + 24, y), ColorFor(line.Kind));
                break;

            case MemoryBookLineKind.Empty:
            {
                Rectangle icon = new(
                    this.contentBounds.X + (this.contentBounds.Width - 64) / 2 - 12,
                    y + 2,
                    64,
                    64);
                this.DrawIcon(b, MemoryBookIcon.EmptyBook, icon);
                Vector2 emptySize = font.MeasureString(wrapped);
                b.DrawString(
                    font,
                    wrapped,
                    new Vector2(this.contentBounds.X + (this.contentBounds.Width - 24 - emptySize.X) / 2f, y + 72),
                    ColorFor(line.Kind));
                break;
            }

            default:
                b.Draw(Game1.staminaRect, new Rectangle(x + 5, y + 10, 6, 6), AccentFor(this.activeTab));
                b.DrawString(font, wrapped, new Vector2(x + 20, y), ColorFor(line.Kind));
                break;
        }
    }

    private void DrawContentScrollbar(SpriteBatch b)
    {
        if (!this.TryGetContentScrollbarGeometry(out Rectangle track, out Rectangle thumb))
        {
            return;
        }

        Color tint = this.draggingContentScrollbar || this.contentScrollbarHovered
            ? Color.White
            : Color.White * 0.86f;
        this.DrawFrame(b, MemoryBookFrame.ScrollTrack, track, tint, 1f, drawShadow: false);
        if (this.assets.Texture != null)
        {
            b.Draw(
                this.assets.Texture,
                thumb,
                MemoryBookAssets.ScrollThumbSource,
                tint);
        }
        else
        {
            b.Draw(
                Game1.mouseCursors,
                thumb,
                new Rectangle(435, 463, 6, 10),
                tint);
        }
    }

    private bool TryGetContentScrollbarGeometry(out Rectangle track, out Rectangle thumb)
    {
        track = new Rectangle(this.contentBounds.Right - 24, this.contentBounds.Y + 14, 16, Math.Max(1, this.contentBounds.Height - 28));
        thumb = Rectangle.Empty;

        float maxScroll = Math.Max(0f, this.contentHeight - this.VisibleContentHeight);
        if (maxScroll <= 0f || this.contentHeight <= 0f)
        {
            return false;
        }

        int maximumThumbHeight = Math.Max(1, track.Height - 4);
        int minimumThumbHeight = Math.Min(36, maximumThumbHeight);
        int thumbHeight = Math.Clamp(
            (int)(track.Height * (this.VisibleContentHeight / this.contentHeight)),
            minimumThumbHeight,
            maximumThumbHeight);
        float ratio = Math.Clamp(this.contentScroll / maxScroll, 0f, 1f);
        int thumbY = track.Y + (int)Math.Round((track.Height - thumbHeight) * ratio);
        thumb = new Rectangle(track.X, thumbY, track.Width, thumbHeight);
        return true;
    }

    private void SetContentScrollFromThumbTop(int thumbTop, Rectangle track, Rectangle thumb)
    {
        int travel = Math.Max(0, track.Height - thumb.Height);
        float maxScroll = Math.Max(0f, this.contentHeight - this.VisibleContentHeight);
        float ratio = travel == 0
            ? 0f
            : Math.Clamp((thumbTop - track.Y) / (float)travel, 0f, 1f);
        this.contentScroll = maxScroll * ratio;
        this.ClampScrolls();
    }

    private static Rectangle ContentScrollbarHitBounds(Rectangle track)
    {
        return new Rectangle(track.Center.X - 22, track.Y, 44, track.Height);
    }

    private void DrawFooter(SpriteBatch b)
    {
        string hint = FitSingleLine(
            Game1.smallFont,
            this.translate("book.footer.hint"),
            Math.Max(8, this.footerBounds.Width - 96));
        Vector2 size = Game1.smallFont.MeasureString(hint);
        float x = this.footerBounds.X + (this.footerBounds.Width - size.X) / 2f;
        float y = this.footerBounds.Y + (this.footerBounds.Height - size.Y) / 2f;
        Color textColor = this.assets.HasCustomArt ? MemoryBookPalette.PaperBright : MemoryBookPalette.Ink;

        this.DrawIcon(b, MemoryBookIcon.Leaf, new Rectangle((int)x - 26, this.footerBounds.Y + 6, 16, 16));
        b.DrawString(Game1.smallFont, hint, new Vector2(x, y), textColor);
        this.DrawIcon(b, MemoryBookIcon.Sparkle, new Rectangle((int)(x + size.X) + 10, this.footerBounds.Y + 6, 16, 16));
    }

    private void DrawFrame(
        SpriteBatch b,
        MemoryBookFrame frame,
        Rectangle bounds,
        Color tint,
        float scale,
        bool drawShadow)
    {
        Texture2D texture;
        Rectangle source;
        if (this.assets.Texture != null)
        {
            texture = this.assets.Texture;
            source = MemoryBookAssets.FrameSource(frame);
        }
        else
        {
            texture = Game1.mouseCursors;
            source = frame == MemoryBookFrame.SelectedRow
                ? new Rectangle(384, 396, 15, 15)
                : new Rectangle(384, 373, 18, 18);
        }

        drawTextureBox(
            b,
            texture,
            source,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            tint,
            scale,
            drawShadow);
    }

    private void DrawPaperPattern(SpriteBatch b, Rectangle bounds)
    {
        if (this.assets.Texture == null)
        {
            return;
        }

        Rectangle inner = Inset(bounds, 24);
        const int tileSize = 32;
        const int step = 64;
        for (int y = inner.Y; y < inner.Bottom; y += step)
        {
            for (int x = inner.X; x < inner.Right; x += step)
            {
                b.Draw(
                    this.assets.Texture,
                    new Rectangle(x, y, tileSize, tileSize),
                    MemoryBookAssets.PaperPatternSource,
                    Color.White);
            }
        }
    }

    private void DrawIcon(
        SpriteBatch b,
        MemoryBookIcon icon,
        Rectangle bounds,
        bool highlighted = false)
    {
        if (this.assets.Texture != null)
        {
            b.Draw(
                this.assets.Texture,
                bounds,
                MemoryBookAssets.IconSource(icon, highlighted),
                Color.White);
            return;
        }

        Rectangle source = icon switch
        {
            MemoryBookIcon.Relationship => new Rectangle(211, 428, 7, 6),
            MemoryBookIcon.ArrowUp => new Rectangle(421, 459, 11, 12),
            MemoryBookIcon.ArrowDown => new Rectangle(421, 472, 11, 12),
            _ => new Rectangle(346, 392, 8, 8)
        };
        b.Draw(Game1.mouseCursors, bounds, source, Color.White);
    }

    private static MemoryBookIcon IconFor(MemoryBookTab tab)
    {
        return tab switch
        {
            MemoryBookTab.Relationship => MemoryBookIcon.Relationship,
            MemoryBookTab.Memories => MemoryBookIcon.Memories,
            MemoryBookTab.Conversations => MemoryBookIcon.Conversations,
            _ => MemoryBookIcon.Moments
        };
    }

    private static Color AccentFor(MemoryBookTab tab)
    {
        return tab switch
        {
            MemoryBookTab.Relationship => MemoryBookPalette.Relationship,
            MemoryBookTab.Memories => MemoryBookPalette.Memories,
            MemoryBookTab.Conversations => MemoryBookPalette.Conversations,
            _ => MemoryBookPalette.Moments
        };
    }

    private static Rectangle Inset(Rectangle bounds, int amount)
    {
        return new Rectangle(
            bounds.X + amount,
            bounds.Y + amount,
            Math.Max(1, bounds.Width - (amount * 2)),
            Math.Max(1, bounds.Height - (amount * 2)));
    }

    private static Rectangle Inflate(Rectangle bounds, int amount)
    {
        return new Rectangle(
            bounds.X - amount,
            bounds.Y - amount,
            bounds.Width + (amount * 2),
            bounds.Height + (amount * 2));
    }

    private static string FitSingleLine(SpriteFont font, string? text, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
        {
            return string.Empty;
        }

        string singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (font.MeasureString(singleLine).X <= maxWidth)
        {
            return singleLine;
        }

        const string ellipsis = "…";
        if (font.MeasureString(ellipsis).X > maxWidth)
        {
            return string.Empty;
        }

        int low = 0;
        int high = singleLine.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            string candidate = singleLine[..middle].TrimEnd() + ellipsis;
            if (font.MeasureString(candidate).X <= maxWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return singleLine[..low].TrimEnd() + ellipsis;
    }
}
