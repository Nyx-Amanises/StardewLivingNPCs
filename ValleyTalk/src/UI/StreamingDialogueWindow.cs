using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace ValleyTalk;

internal sealed class StreamingDialogueWindow : IClickableMenu
{
    private const int OuterMargin = 16;
    private const int InnerMargin = 28;
    private const int PortraitSize = 256;
    private const int NameBandHeight = 44;

    private readonly object sync = new();
    private readonly NPC npc;
    private readonly List<string> pages = new();
    private string rawText = string.Empty;
    private bool generationComplete;
    private Action onFinished;
    private int currentPageIndex;
    private int animationFrame;
    private float animationTimer;

    public StreamingDialogueWindow(NPC npc)
    {
        this.npc = npc;
        this.width = Math.Min(Game1.uiViewport.Width - (OuterMargin * 2), 1248);
        this.height = Math.Min(Game1.uiViewport.Height - (OuterMargin * 2), 336);
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = Game1.uiViewport.Height - this.height - OuterMargin;
    }

    public void AppendToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        lock (this.sync)
        {
            this.rawText += token;
            this.RebuildPages(StreamingDialoguePreview.ExtractVisibleText(this.rawText));
        }
    }

    public void Complete(string finalText, Action onFinished)
    {
        lock (this.sync)
        {
            this.generationComplete = true;
            this.onFinished = onFinished;
            this.RebuildPages(StreamingDialoguePreview.PrepareDisplayText(finalText));
            if (this.pages.Count == 0)
            {
                this.pages.Add("...");
            }
        }
    }

    public override void update(GameTime time)
    {
        base.update(time);
        this.animationTimer += (float)time.ElapsedGameTime.TotalMilliseconds;
        if (this.animationTimer >= 350f)
        {
            this.animationFrame = (this.animationFrame + 1) % 4;
            this.animationTimer = 0f;
        }
    }

    public override void draw(SpriteBatch b)
    {
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

        Rectangle portraitBounds = this.GetPortraitBounds();
        if (this.npc?.Portrait != null)
        {
            b.Draw(
                this.npc.Portrait,
                portraitBounds,
                new Rectangle(0, 0, 64, 64),
                Color.White
            );
        }

        Rectangle nameBounds = new(
            portraitBounds.X,
            portraitBounds.Bottom + 6,
            portraitBounds.Width,
            NameBandHeight
        );
        Game1.drawDialogueBox(nameBounds.X, nameBounds.Y, nameBounds.Width, nameBounds.Height, false, true);
        string name = this.npc?.displayName ?? string.Empty;
        Vector2 nameSize = Game1.dialogueFont.MeasureString(name);
        b.DrawString(
            Game1.dialogueFont,
            name,
            new Vector2(
                nameBounds.X + ((nameBounds.Width - nameSize.X) / 2),
                nameBounds.Y + ((nameBounds.Height - nameSize.Y) / 2) + 6
            ),
            Game1.textColor
        );

        string pageText;
        bool canAdvance;
        lock (this.sync)
        {
            pageText = this.pages.Count > 0
                ? this.pages[Math.Clamp(this.currentPageIndex, 0, this.pages.Count - 1)]
                : this.GetAnimatedDots();
            canAdvance = this.currentPageIndex < this.pages.Count - 1 || this.generationComplete;
        }

        Rectangle textBounds = this.GetTextBounds();
        b.DrawString(
            Game1.dialogueFont,
            pageText,
            new Vector2(textBounds.X, textBounds.Y),
            Game1.textColor
        );

        if (canAdvance)
        {
            string prompt = this.currentPageIndex < this.pages.Count - 1 ? ">" : ">";
            Vector2 promptSize = Game1.dialogueFont.MeasureString(prompt);
            b.DrawString(
                Game1.dialogueFont,
                prompt,
                new Vector2(textBounds.Right - promptSize.X, textBounds.Bottom - promptSize.Y),
                Game1.textColor
            );
        }

        if (!Game1.options.hardwareCursor)
        {
            b.Draw(
                Game1.mouseCursors,
                new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 0, 16, 16),
                Color.White,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                1f
            );
        }
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        this.Advance();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key is Keys.Enter or Keys.Space or Keys.Escape)
        {
            this.Advance();
        }
    }

    public override bool overrideSnappyMenuCursorMovementBan()
    {
        return true;
    }

    private void Advance()
    {
        Action finished = null;
        lock (this.sync)
        {
            if (this.currentPageIndex < this.pages.Count - 1)
            {
                this.currentPageIndex++;
                Game1.playSound("smallSelect");
                return;
            }

            if (!this.generationComplete)
            {
                return;
            }

            finished = this.onFinished;
        }

        finished?.Invoke();
    }

    private void RebuildPages(string displayText)
    {
        var rebuilt = new List<string>();
        Rectangle textBounds = this.GetTextBounds();
        int maxLines = Math.Max(1, textBounds.Height / Math.Max(Game1.dialogueFont.LineSpacing, 1));

        foreach (string segment in displayText.Split('\f', StringSplitOptions.RemoveEmptyEntries))
        {
            string wrapped = Game1.parseText(segment, Game1.dialogueFont, textBounds.Width);
            var lines = wrapped.Split('\n');
            for (int i = 0; i < lines.Length; i += maxLines)
            {
                rebuilt.Add(string.Join("\n", lines.Skip(i).Take(maxLines)));
            }
        }

        this.pages.Clear();
        this.pages.AddRange(rebuilt.Where(page => !string.IsNullOrWhiteSpace(page)));
        this.currentPageIndex = Math.Clamp(this.currentPageIndex, 0, Math.Max(this.pages.Count - 1, 0));
    }

    private string GetAnimatedDots()
    {
        return new string('.', Math.Max(1, this.animationFrame + 1));
    }

    private Rectangle GetPortraitBounds()
    {
        return new Rectangle(
            this.xPositionOnScreen + this.width - InnerMargin - PortraitSize,
            this.yPositionOnScreen + InnerMargin - 2,
            PortraitSize,
            PortraitSize
        );
    }

    private Rectangle GetTextBounds()
    {
        Rectangle portraitBounds = this.GetPortraitBounds();
        return new Rectangle(
            this.xPositionOnScreen + InnerMargin,
            this.yPositionOnScreen + InnerMargin + 8,
            portraitBounds.X - this.xPositionOnScreen - (InnerMargin * 2),
            this.height - (InnerMargin * 2) - 8
        );
    }
}
