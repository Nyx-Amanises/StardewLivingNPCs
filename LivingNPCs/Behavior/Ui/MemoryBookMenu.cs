using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
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
    private const int RosterRowHeight = 100;
    private const int TabButtonHeight = 64;
    private const int ContentPadding = 24;

    /// <summary>内容区裁剪用（复用实例，避免每帧分配）。</summary>
    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    private readonly MemoryBookData.Translate translate;
    private readonly Func<string, StardewEventHistory> getHistory;
    private readonly Dictionary<string, LivingNpcState> statesByName;
    private readonly List<MemoryBookNpcSummary> roster;
    private readonly Dictionary<(string Npc, MemoryBookTab Tab), List<MemoryBookLine>> pageCache = new();

    private readonly List<ClickableComponent> rosterRows = new();
    private readonly List<ClickableComponent> tabButtons = new();
    private ClickableTextureComponent? rosterUpArrow;
    private ClickableTextureComponent? rosterDownArrow;

    private Rectangle rosterBounds;
    private Rectangle contentBounds;
    private int rosterScrollIndex;
    private float contentScroll;
    private float contentHeight;
    private int selectedRosterIndex;
    private MemoryBookTab activeTab = MemoryBookTab.Relationship;
    private string hoverText = string.Empty;

    /// <summary>换好行的绘制行（按当前列宽缓存；列宽或数据变化时重建）。</summary>
    private readonly List<(MemoryBookLine Line, string Wrapped, float Height)> wrappedLines = new();
    private int wrappedForWidth = -1;
    private (string Npc, MemoryBookTab Tab)? wrappedForPage;

    private MemoryBookMenu(
        List<MemoryBookNpcSummary> roster,
        Dictionary<string, LivingNpcState> statesByName,
        Func<string, StardewEventHistory> getHistory,
        MemoryBookData.Translate translate)
    {
        this.roster = roster;
        this.statesByName = statesByName;
        this.getHistory = getHistory;
        this.translate = translate;

        this.RebuildLayout();
        this.initializeUpperRightCloseButton();
        Game1.playSound("bigSelect");
    }

    /// <summary>入口：没有任何可展示的 NPC 时返回 null（调用方给 HUD 提示）。</summary>
    public static MemoryBookMenu? TryCreate(BehaviorMemory memory)
    {
        MemoryBookData.Translate translate = static (key, tokens) => I18n.Get(key, tokens);
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
            translate);
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

    // ---- 布局 ----

    private void RebuildLayout()
    {
        int menuWidth = Math.Min(1360, Game1.uiViewport.Width - 96);
        int menuHeight = Math.Min(800, Game1.uiViewport.Height - 96);
        this.width = Math.Max(960, menuWidth);
        this.height = Math.Max(600, menuHeight);
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.uiViewport.Height - this.height) / 2;

        int rosterWidth = Math.Max(300, this.width / 4 + 40);
        this.rosterBounds = new Rectangle(
            this.xPositionOnScreen + 36,
            this.yPositionOnScreen + 120,
            rosterWidth,
            this.height - 160);
        this.contentBounds = new Rectangle(
            this.rosterBounds.Right + 40,
            this.yPositionOnScreen + 120 + TabButtonHeight + 8,
            this.width - rosterWidth - 36 * 2 - 40,
            this.height - 160 - TabButtonHeight - 8);

        this.rosterRows.Clear();
        int visibleRows = this.VisibleRosterRows;
        for (int i = 0; i < visibleRows; i++)
        {
            this.rosterRows.Add(new ClickableComponent(
                new Rectangle(
                    this.rosterBounds.X,
                    this.rosterBounds.Y + (i * RosterRowHeight),
                    this.rosterBounds.Width - 52,
                    RosterRowHeight - 8),
                $"row{i}"));
        }

        this.rosterUpArrow = new ClickableTextureComponent(
            new Rectangle(this.rosterBounds.Right - 44, this.rosterBounds.Y, 44, 48),
            Game1.mouseCursors,
            new Rectangle(421, 459, 11, 12),
            4f);
        this.rosterDownArrow = new ClickableTextureComponent(
            new Rectangle(this.rosterBounds.Right - 44, this.rosterBounds.Bottom - 48, 44, 48),
            Game1.mouseCursors,
            new Rectangle(421, 472, 11, 12),
            4f);

        this.tabButtons.Clear();
        MemoryBookTab[] tabs = Enum.GetValues<MemoryBookTab>();
        int tabWidth = this.contentBounds.Width / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            this.tabButtons.Add(new ClickableComponent(
                new Rectangle(
                    this.contentBounds.X + (i * tabWidth),
                    this.contentBounds.Y - TabButtonHeight,
                    tabWidth - 8,
                    TabButtonHeight),
                tabs[i].ToString()));
        }

        this.wrappedForWidth = -1;
        this.ClampScrolls();
    }

    private int VisibleRosterRows => Math.Max(1, this.rosterBounds.Height / RosterRowHeight);

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.RebuildLayout();
        this.initializeUpperRightCloseButton();
    }

    // ---- 数据 ----

    private MemoryBookNpcSummary SelectedNpc => this.roster[Math.Clamp(this.selectedRosterIndex, 0, this.roster.Count - 1)];

    private List<MemoryBookLine> GetPageLines(string npcName, MemoryBookTab tab)
    {
        var cacheKey = (npcName, tab);
        if (this.pageCache.TryGetValue(cacheKey, out List<MemoryBookLine>? cached))
        {
            return cached;
        }

        int nowTotalDays = Game1.Date.TotalDays;
        List<MemoryBookLine> lines;
        if (!this.statesByName.TryGetValue(npcName, out LivingNpcState? state))
        {
            lines = new List<MemoryBookLine> { new(MemoryBookLineKind.Empty, this.translate("book.memories.empty")) };
        }
        else
        {
            string displayName = this.SelectedNpc.DisplayName;
            lines = tab switch
            {
                MemoryBookTab.Relationship => MemoryBookData.BuildRelationshipCard(
                    state, displayName, this.SelectedNpc.Hearts, nowTotalDays, this.translate),
                MemoryBookTab.Memories => MemoryBookData.BuildMemoryLines(state, nowTotalDays, this.translate),
                MemoryBookTab.Conversations => MemoryBookData.BuildConversationLines(
                    this.SafeHistory(npcName), displayName, Game1.player?.Name ?? "Farmer", this.translate),
                _ => MemoryBookData.BuildMomentLines(state, nowTotalDays, this.translate)
            };
        }

        this.pageCache[cacheKey] = lines;
        return lines;
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

    private void EnsureWrapped()
    {
        int textWidth = this.contentBounds.Width - (ContentPadding * 2) - 40;
        var page = (this.SelectedNpc.NpcName, this.activeTab);
        if (this.wrappedForWidth == textWidth && this.wrappedForPage == page)
        {
            return;
        }

        this.wrappedLines.Clear();
        foreach (MemoryBookLine line in this.GetPageLines(page.NpcName, this.activeTab))
        {
            string wrapped = Game1.parseText(line.Text, this.FontFor(line.Kind), Math.Max(120, textWidth - this.IndentFor(line.Kind)));
            float height = this.FontFor(line.Kind).MeasureString(wrapped).Y + this.SpacingFor(line.Kind);
            this.wrappedLines.Add((line, wrapped, height));
        }

        this.contentHeight = this.wrappedLines.Sum(entry => entry.Height) + ContentPadding * 2;
        this.wrappedForWidth = textWidth;
        this.wrappedForPage = page;
        this.ClampScrolls();
    }

    private SpriteFont FontFor(MemoryBookLineKind kind)
    {
        return kind == MemoryBookLineKind.SectionHeader ? Game1.dialogueFont : Game1.smallFont;
    }

    private int IndentFor(MemoryBookLineKind kind)
    {
        return kind is MemoryBookLineKind.MemoryFact or MemoryBookLineKind.PlayerLine or MemoryBookLineKind.NpcLine ? 28 : 0;
    }

    private float SpacingFor(MemoryBookLineKind kind)
    {
        return kind switch
        {
            MemoryBookLineKind.SectionHeader => 18f,
            MemoryBookLineKind.DateSeparator => 14f,
            _ => 8f
        };
    }

    private static Color ColorFor(MemoryBookLineKind kind)
    {
        return kind switch
        {
            MemoryBookLineKind.SectionHeader => new Color(86, 22, 12),
            MemoryBookLineKind.PlayerLine => new Color(46, 80, 146),
            MemoryBookLineKind.NpcLine => new Color(60, 60, 60),
            MemoryBookLineKind.DateSeparator => new Color(140, 100, 60),
            MemoryBookLineKind.MemoryFact => new Color(50, 60, 40),
            MemoryBookLineKind.Muted => new Color(130, 120, 100),
            MemoryBookLineKind.Empty => new Color(150, 130, 110),
            _ => Game1.textColor
        };
    }

    private void ClampScrolls()
    {
        this.rosterScrollIndex = Math.Clamp(this.rosterScrollIndex, 0, Math.Max(0, this.roster.Count - this.VisibleRosterRows));
        float maxScroll = Math.Max(0f, this.contentHeight - this.contentBounds.Height);
        this.contentScroll = Math.Clamp(this.contentScroll, 0f, maxScroll);
    }

    private void SelectRoster(int index, bool playSound = true)
    {
        index = Math.Clamp(index, 0, this.roster.Count - 1);
        if (index == this.selectedRosterIndex)
        {
            return;
        }

        this.selectedRosterIndex = index;
        this.contentScroll = 0f;
        if (index < this.rosterScrollIndex)
        {
            this.rosterScrollIndex = index;
        }
        else if (index >= this.rosterScrollIndex + this.VisibleRosterRows)
        {
            this.rosterScrollIndex = index - this.VisibleRosterRows + 1;
        }

        if (playSound)
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
        if (playSound)
        {
            Game1.playSound("shwip");
        }
    }

    // ---- 输入 ----

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
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
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
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
        int previous = this.rosterScrollIndex;
        this.rosterScrollIndex = Math.Clamp(
            this.rosterScrollIndex + delta,
            0,
            Math.Max(0, this.roster.Count - this.VisibleRosterRows));
        if (previous != this.rosterScrollIndex)
        {
            Game1.playSound("shiny4");
        }
    }

    private void ScrollContent(float delta)
    {
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
        switch (b)
        {
            case Buttons.B:
                this.exitThisMenu();
                return;
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
        base.performHoverAction(x, y);
        this.hoverText = string.Empty;
        this.rosterUpArrow?.tryHover(x, y);
        this.rosterDownArrow?.tryHover(x, y);

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
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

        // 书本框体：经典羊皮纸菜单框 + 顶部卷轴标题。
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
        SpriteText.drawStringWithScrollCenteredAt(
            b,
            this.translate("book.title"),
            this.xPositionOnScreen + this.width / 2,
            this.yPositionOnScreen + 40);

        this.DrawRoster(b);
        this.DrawDivider(b);
        this.DrawTabs(b);
        this.DrawContent(b);

        base.draw(b);

        if (!string.IsNullOrEmpty(this.hoverText))
        {
            drawHoverText(b, this.hoverText, Game1.smallFont);
        }

        this.drawMouse(b);
    }

    private void DrawRoster(SpriteBatch b)
    {
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

            if (selected)
            {
                drawTextureBox(
                    b,
                    Game1.mouseCursors,
                    new Rectangle(384, 396, 15, 15),
                    rowBounds.X - 4,
                    rowBounds.Y - 4,
                    rowBounds.Width + 8,
                    rowBounds.Height + 8,
                    Color.White,
                    4f,
                    drawShadow: false);
            }

            NPC? npc = Game1.getCharacterFromName(entry.NpcName);
            if (npc?.Portrait != null)
            {
                b.Draw(
                    npc.Portrait,
                    new Rectangle(rowBounds.X + 8, rowBounds.Y + (rowBounds.Height - 64) / 2, 64, 64),
                    new Rectangle(0, 0, 64, 64),
                    Color.White);
            }
            else
            {
                b.Draw(
                    Game1.mouseCursors,
                    new Rectangle(rowBounds.X + 8, rowBounds.Y + (rowBounds.Height - 64) / 2, 64, 64),
                    new Rectangle(540, 333, 12, 14),
                    Color.White * 0.7f);
            }

            int textX = rowBounds.X + 84;
            b.DrawString(
                Game1.smallFont,
                entry.DisplayName,
                new Vector2(textX, rowBounds.Y + 8),
                selected ? new Color(86, 22, 12) : Game1.textColor);

            this.DrawHearts(b, textX, rowBounds.Y + 40, entry.Hearts);

            b.DrawString(
                Game1.smallFont,
                Game1.parseText(entry.SubtitleText, Game1.smallFont, Math.Max(80, rowBounds.Width - 92)),
                new Vector2(textX, rowBounds.Y + 60),
                new Color(130, 120, 100) * 0.9f);
        }

        if (this.rosterScrollIndex > 0)
        {
            this.rosterUpArrow?.draw(b);
        }

        if (this.rosterScrollIndex + visibleRows < this.roster.Count)
        {
            this.rosterDownArrow?.draw(b);
        }
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

    private void DrawDivider(SpriteBatch b)
    {
        var divider = new Rectangle(
            this.rosterBounds.Right + 16,
            this.yPositionOnScreen + 110,
            4,
            this.height - 150);
        b.Draw(Game1.staminaRect, divider, new Color(120, 80, 48) * 0.5f);
    }

    private void DrawTabs(SpriteBatch b)
    {
        foreach (ClickableComponent tabButton in this.tabButtons)
        {
            bool active = Enum.TryParse(tabButton.name, out MemoryBookTab tab) && tab == this.activeTab;
            Rectangle bounds = tabButton.bounds;
            int lift = active ? 0 : 8;
            drawTextureBox(
                b,
                Game1.mouseCursors,
                new Rectangle(384, 373, 18, 18),
                bounds.X,
                bounds.Y + lift,
                bounds.Width,
                bounds.Height - lift,
                active ? Color.White : Color.White * 0.75f,
                4f,
                drawShadow: false);

            string label = this.translate($"book.tab.{tabButton.name.ToLowerInvariant()}");
            Vector2 size = Game1.smallFont.MeasureString(label);
            b.DrawString(
                Game1.smallFont,
                label,
                new Vector2(
                    bounds.X + (bounds.Width - size.X) / 2,
                    bounds.Y + lift + (bounds.Height - lift - size.Y) / 2),
                active ? new Color(86, 22, 12) : new Color(120, 100, 80));
        }
    }

    private void DrawContent(SpriteBatch b)
    {
        this.EnsureWrapped();

        drawTextureBox(
            b,
            Game1.mouseCursors,
            new Rectangle(384, 373, 18, 18),
            this.contentBounds.X,
            this.contentBounds.Y,
            this.contentBounds.Width,
            this.contentBounds.Height,
            Color.White,
            4f,
            drawShadow: false);

        Rectangle clip = new(
            this.contentBounds.X + 8,
            this.contentBounds.Y + 8,
            this.contentBounds.Width - 16,
            this.contentBounds.Height - 16);

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
                float x = this.contentBounds.X + ContentPadding + this.IndentFor(line.Kind);
                if (line.Kind == MemoryBookLineKind.MemoryFact)
                {
                    b.Draw(
                        Game1.mouseCursors,
                        new Vector2(this.contentBounds.X + ContentPadding, lineY + 6),
                        new Rectangle(346, 392, 8, 8),
                        Color.White,
                        0f,
                        Vector2.Zero,
                        2f,
                        SpriteEffects.None,
                        0.88f);
                }

                if (line.Kind == MemoryBookLineKind.DateSeparator)
                {
                    Vector2 size = Game1.smallFont.MeasureString(wrapped);
                    float centered = this.contentBounds.X + (this.contentBounds.Width - size.X) / 2;
                    b.DrawString(Game1.smallFont, wrapped, new Vector2(centered, lineY), ColorFor(line.Kind));
                }
                else
                {
                    b.DrawString(this.FontFor(line.Kind), wrapped, new Vector2(x, lineY), ColorFor(line.Kind));
                }
            }

            lineY += height;
        }

        b.End();
        b.GraphicsDevice.ScissorRectangle = previousScissor;
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

        this.DrawContentScrollbar(b);
    }

    private void DrawContentScrollbar(SpriteBatch b)
    {
        float maxScroll = Math.Max(0f, this.contentHeight - this.contentBounds.Height);
        if (maxScroll <= 0f)
        {
            return;
        }

        var track = new Rectangle(this.contentBounds.Right - 24, this.contentBounds.Y + 12, 24, this.contentBounds.Height - 24);
        drawTextureBox(
            b,
            Game1.mouseCursors,
            new Rectangle(403, 383, 6, 6),
            track.X,
            track.Y,
            track.Width,
            track.Height,
            Color.White,
            4f,
            drawShadow: false);

        float ratio = this.contentScroll / maxScroll;
        int thumbHeight = Math.Max(40, (int)(track.Height * (this.contentBounds.Height / this.contentHeight)));
        int thumbY = track.Y + (int)((track.Height - thumbHeight) * ratio);
        b.Draw(
            Game1.mouseCursors,
            new Rectangle(track.X + 4, thumbY, 16, thumbHeight),
            new Rectangle(435, 463, 6, 10),
            Color.White);
    }
}
