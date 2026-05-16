using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace LivingNPCs.UI;

internal sealed class HelpRequestLogMenu : IClickableMenu
{
    private const int OuterMargin = 24;
    private const int InnerMargin = 28;
    private const int RowHeight = 74;
    private const int VisibleRows = 7;

    private readonly IReadOnlyList<HelpRequestLogEntry> entries;
    private int scrollIndex;

    public HelpRequestLogMenu(IReadOnlyList<HelpRequestLogEntry> entries)
    {
        this.entries = entries;
        this.width = Math.Min(Game1.uiViewport.Width - (OuterMargin * 2), 1100);
        this.height = Math.Min(Game1.uiViewport.Height - (OuterMargin * 2), 696);
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.uiViewport.Height - this.height) / 2;
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.35f);
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

        Vector2 titlePosition = new(this.xPositionOnScreen + InnerMargin, this.yPositionOnScreen + InnerMargin);
        b.DrawString(Game1.dialogueFont, "NPC 求助", titlePosition, Game1.textColor);

        string subtitle = this.entries.Count == 0
            ? "当前没有待完成的求助。"
            : $"待完成 {this.entries.Count} 项";
        Vector2 subtitlePosition = new(titlePosition.X, titlePosition.Y + 48);
        b.DrawString(Game1.smallFont, subtitle, subtitlePosition, Game1.textColor);

        Rectangle contentBounds = new(
            this.xPositionOnScreen + InnerMargin,
            this.yPositionOnScreen + 116,
            this.width - (InnerMargin * 2),
            this.height - 176
        );

        if (this.entries.Count == 0)
        {
            string emptyText = "等某位 NPC 真正向你开口后，这里会显示请求内容和截止日期。";
            string wrapped = Game1.parseText(emptyText, Game1.dialogueFont, contentBounds.Width);
            b.DrawString(Game1.dialogueFont, wrapped, new Vector2(contentBounds.X, contentBounds.Y + 24), Game1.textColor);
        }
        else
        {
            foreach ((HelpRequestLogEntry entry, int rowIndex) in this.GetVisibleEntries().Select((entry, index) => (entry, index)))
            {
                int rowY = contentBounds.Y + (rowIndex * RowHeight);
                Rectangle rowBounds = new(contentBounds.X, rowY, contentBounds.Width, RowHeight - 8);
                IClickableMenu.drawTextureBox(
                    b,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    rowBounds.X,
                    rowBounds.Y,
                    rowBounds.Width,
                    rowBounds.Height,
                    Color.White,
                    1f,
                    false
                );

                string headline = $"{entry.NpcDisplayName}  ·  {entry.TypeLabel}";
                b.DrawString(Game1.dialogueFont, headline, new Vector2(rowBounds.X + 18, rowBounds.Y + 10), Game1.textColor);

                string detail = entry.DetailText;
                string due = entry.DueText;
                string wrapped = Game1.parseText(detail, Game1.smallFont, rowBounds.Width - 220);
                b.DrawString(Game1.smallFont, wrapped, new Vector2(rowBounds.X + 20, rowBounds.Y + 42), Game1.textColor);

                Vector2 dueSize = Game1.smallFont.MeasureString(due);
                b.DrawString(
                    Game1.smallFont,
                    due,
                    new Vector2(rowBounds.Right - dueSize.X - 18, rowBounds.Y + 44),
                    entry.IsOverdue ? Color.IndianRed : Game1.textColor
                );
            }
        }

        string footer = this.entries.Count > VisibleRows
            ? $"第 {this.scrollIndex + 1}-{Math.Min(this.scrollIndex + VisibleRows, this.entries.Count)} 项 / 共 {this.entries.Count} 项  ·  鼠标滚轮或 ↑↓ 滚动  ·  Esc 关闭"
            : "Esc 关闭";
        b.DrawString(
            Game1.smallFont,
            footer,
            new Vector2(this.xPositionOnScreen + InnerMargin, this.yPositionOnScreen + this.height - 48),
            Game1.textColor
        );

        this.drawMouse(b);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            Game1.exitActiveMenu();
            return;
        }

        if (key == Keys.Down)
        {
            this.Scroll(1);
        }
        else if (key == Keys.Up)
        {
            this.Scroll(-1);
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        this.Scroll(direction > 0 ? -1 : 1);
    }

    public override bool overrideSnappyMenuCursorMovementBan()
    {
        return true;
    }

    private IEnumerable<HelpRequestLogEntry> GetVisibleEntries()
    {
        return this.entries
            .Skip(this.scrollIndex)
            .Take(VisibleRows);
    }

    private void Scroll(int delta)
    {
        int maxScroll = Math.Max(0, this.entries.Count - VisibleRows);
        int next = Math.Clamp(this.scrollIndex + delta, 0, maxScroll);
        if (next != this.scrollIndex)
        {
            this.scrollIndex = next;
            Game1.playSound("shwip");
        }
    }
}

internal sealed record HelpRequestLogEntry(
    string NpcDisplayName,
    string TypeLabel,
    string DetailText,
    string DueText,
    bool IsOverdue
);
