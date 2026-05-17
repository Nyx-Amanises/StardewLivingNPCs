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
    private const int MaxWindowWidth = 1248;
    private const int MaxWindowHeight = 336;
    private const int PortraitPanelWidth = 320;
    private const int PortraitSize = 256;
    private const float RevealMillisecondsPerCharacter = 81f;

    private readonly object sync = new();
    private readonly NPC npc;
    private readonly List<string> pages = new();
    private readonly List<StreamingResponseOption> responseOptions = new();
    private readonly List<Rectangle> responseOptionBounds = new();
    private string rawText = string.Empty;
    private string displayText = string.Empty;
    private bool generationComplete;
    private bool showingResponses;
    private Action onFinished;
    private Action<StreamingResponseOption> onResponseSelected;
    private int currentPageIndex;
    private int selectedResponseIndex;
    private int animationFrame;
    private int visibleCharacterCount;
    private float animationTimer;
    private float revealTimer;
    private int lastTextWidth;
    private int lastTextHeight;

    public StreamingDialogueWindow(NPC npc)
    {
        this.npc = npc;
        this.UpdateLayout();
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

    public void Complete(
        string finalText,
        IEnumerable<StreamingResponseOption> responseOptions,
        Action<StreamingResponseOption> onResponseSelected,
        Action onFinished)
    {
        lock (this.sync)
        {
            this.generationComplete = true;
            this.onFinished = onFinished;
            this.onResponseSelected = onResponseSelected;
            this.responseOptions.Clear();
            if (responseOptions != null)
            {
                this.responseOptions.AddRange(responseOptions.Where(option => option != null && !string.IsNullOrWhiteSpace(option.Text)));
            }
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
        this.UpdateLayout(rebuildIfNeeded: true);
        this.animationTimer += (float)time.ElapsedGameTime.TotalMilliseconds;
        if (this.animationTimer >= 350f)
        {
            this.animationFrame = (this.animationFrame + 1) % 4;
            this.animationTimer = 0f;
        }

        lock (this.sync)
        {
            if (this.pages.Count == 0)
            {
                return;
            }

            string currentPage = this.pages[Math.Clamp(this.currentPageIndex, 0, this.pages.Count - 1)];
            if (this.visibleCharacterCount >= currentPage.Length)
            {
                return;
            }

            this.revealTimer += (float)time.ElapsedGameTime.TotalMilliseconds;
            int charactersToReveal = (int)(this.revealTimer / RevealMillisecondsPerCharacter);
            if (charactersToReveal <= 0)
            {
                return;
            }

            this.visibleCharacterCount = Math.Min(currentPage.Length, this.visibleCharacterCount + charactersToReveal);
            this.revealTimer %= RevealMillisecondsPerCharacter;
        }
    }

    public override void draw(SpriteBatch b)
    {
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

        if (this.showingResponses)
        {
            this.DrawResponseOptions(b);
            this.DrawCursor(b);
            return;
        }

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

        string name = this.npc?.displayName ?? string.Empty;
        Vector2 nameSize = Game1.dialogueFont.MeasureString(name);
        b.DrawString(
            Game1.dialogueFont,
            name,
            new Vector2(
                portraitBounds.X + ((portraitBounds.Width - nameSize.X) / 2),
                portraitBounds.Bottom - nameSize.Y - 10
            ),
            Game1.textColor
        );

        string pageText;
        bool canAdvance;
        lock (this.sync)
        {
            if (this.pages.Count > 0)
            {
                string currentPage = this.pages[Math.Clamp(this.currentPageIndex, 0, this.pages.Count - 1)];
                pageText = currentPage[..Math.Min(this.visibleCharacterCount, currentPage.Length)];
                if (string.IsNullOrEmpty(pageText) && !this.generationComplete)
                {
                    pageText = this.GetAnimatedDots();
                }
                canAdvance = this.visibleCharacterCount >= currentPage.Length
                    && (this.currentPageIndex < this.pages.Count - 1 || this.generationComplete);
            }
            else
            {
                pageText = this.GetAnimatedDots();
                canAdvance = false;
            }
        }

        Rectangle textBounds = this.GetTextBounds();
        b.DrawString(
            Game1.dialogueFont,
            pageText,
            new Vector2(textBounds.X, textBounds.Y),
            Game1.textColor
        );

        this.DrawCursor(b);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.showingResponses)
        {
            for (int i = 0; i < this.responseOptionBounds.Count; i++)
            {
                if (this.responseOptionBounds[i].Contains(x, y))
                {
                    this.SelectResponse(i);
                    return;
                }
            }
        }

        this.Advance();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (this.showingResponses)
        {
            if (key is Keys.Up or Keys.W)
            {
                this.selectedResponseIndex = Math.Max(0, this.selectedResponseIndex - 1);
                Game1.playSound("shiny4");
                return;
            }

            if (key is Keys.Down or Keys.S)
            {
                this.selectedResponseIndex = Math.Min(Math.Max(0, this.responseOptions.Count - 1), this.selectedResponseIndex + 1);
                Game1.playSound("shiny4");
                return;
            }

            if (key is Keys.Enter or Keys.Space)
            {
                this.SelectResponse(this.selectedResponseIndex);
                return;
            }

            if (key is Keys.Escape)
            {
                this.SelectSilentOrClose();
                return;
            }
        }

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
            if (this.pages.Count > 0)
            {
                string currentPage = this.pages[Math.Clamp(this.currentPageIndex, 0, this.pages.Count - 1)];
                if (this.visibleCharacterCount < currentPage.Length)
                {
                    this.visibleCharacterCount = currentPage.Length;
                    this.revealTimer = 0f;
                    return;
                }
            }

            if (this.currentPageIndex < this.pages.Count - 1)
            {
                this.currentPageIndex++;
                this.visibleCharacterCount = 0;
                this.revealTimer = 0f;
                Game1.playSound("smallSelect");
                return;
            }

            if (!this.generationComplete)
            {
                return;
            }

            if (this.responseOptions.Count > 0)
            {
                this.showingResponses = true;
                this.selectedResponseIndex = 0;
                Game1.playSound("smallSelect");
                return;
            }

            finished = this.onFinished;
        }

        finished?.Invoke();
    }

    private void RebuildPages(string displayText)
    {
        this.UpdateLayout();
        this.displayText = displayText ?? string.Empty;
        var rebuilt = new List<string>();
        Rectangle textBounds = this.GetTextBounds();
        int maxLines = Math.Max(1, textBounds.Height / Math.Max(Game1.dialogueFont.LineSpacing, 1));

        foreach (string segment in this.displayText.Split('\f', StringSplitOptions.RemoveEmptyEntries))
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
        if (this.pages.Count == 0)
        {
            this.visibleCharacterCount = 0;
        }
        else
        {
            string currentPage = this.pages[this.currentPageIndex];
            this.visibleCharacterCount = Math.Min(this.visibleCharacterCount, currentPage.Length);
        }
    }

    private string GetAnimatedDots()
    {
        return new string('.', Math.Max(1, this.animationFrame + 1));
    }

    private void SelectResponse(int index)
    {
        StreamingResponseOption selected;
        Action<StreamingResponseOption> selectedCallback;

        lock (this.sync)
        {
            if (index < 0 || index >= this.responseOptions.Count)
            {
                return;
            }

            selected = this.responseOptions[index];
            selectedCallback = this.onResponseSelected;
        }

        selectedCallback?.Invoke(selected);
    }

    private void SelectSilentOrClose()
    {
        int silentIndex = this.responseOptions.FindIndex(option => option.Kind == StreamingResponseOptionKind.Silent);
        if (silentIndex >= 0)
        {
            this.SelectResponse(silentIndex);
            return;
        }

        this.onFinished?.Invoke();
    }

    private void DrawResponseOptions(SpriteBatch b)
    {
        Rectangle textBounds = this.GetTextBounds(includePortrait: false);
        this.responseOptionBounds.Clear();

        int lineHeight = Math.Max(48, Game1.dialogueFont.LineSpacing + 12);
        int y = textBounds.Y;

        for (int i = 0; i < this.responseOptions.Count; i++)
        {
            string optionText = Game1.parseText(this.responseOptions[i].Text, Game1.dialogueFont, textBounds.Width - 32);
            int optionHeight = Math.Max(lineHeight, (optionText.Count(c => c == '\n') + 1) * lineHeight);
            var bounds = new Rectangle(textBounds.X, y - 4, textBounds.Width, optionHeight);
            this.responseOptionBounds.Add(bounds);

            if (i == this.selectedResponseIndex)
            {
                b.Draw(Game1.staminaRect, bounds, Color.Brown * 0.18f);
            }

            b.DrawString(
                Game1.dialogueFont,
                optionText,
                new Vector2(bounds.X + 12, y),
                i == this.selectedResponseIndex ? new Color(96, 32, 16) : Game1.textColor
            );

            y += optionHeight;
            if (y > textBounds.Bottom - lineHeight)
            {
                break;
            }
        }
    }

    private void DrawCursor(SpriteBatch b)
    {
        if (Game1.options.hardwareCursor)
        {
            return;
        }

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

    private void UpdateLayout(bool rebuildIfNeeded = false)
    {
        int viewportWidth = Math.Max(1, Game1.uiViewport.Width);
        int viewportHeight = Math.Max(1, Game1.uiViewport.Height);
        this.width = Math.Min(viewportWidth - (OuterMargin * 2), MaxWindowWidth);
        this.height = Math.Min(viewportHeight - (OuterMargin * 2), MaxWindowHeight);
        this.xPositionOnScreen = (viewportWidth - this.width) / 2;
        this.yPositionOnScreen = viewportHeight - this.height - OuterMargin;

        Rectangle textBounds = this.GetTextBounds();
        bool textBoundsChanged = textBounds.Width != this.lastTextWidth || textBounds.Height != this.lastTextHeight;
        this.lastTextWidth = textBounds.Width;
        this.lastTextHeight = textBounds.Height;

        if (rebuildIfNeeded && textBoundsChanged && !string.IsNullOrWhiteSpace(this.displayText))
        {
            this.RebuildPages(this.displayText);
        }
    }

    private Rectangle GetPortraitBounds()
    {
        int portraitSize = Math.Min(PortraitSize, Math.Max(96, this.height - (InnerMargin * 2) - 44));
        Rectangle portraitPanel = this.GetPortraitPanelBounds();
        return new Rectangle(
            portraitPanel.X + ((portraitPanel.Width - portraitSize) / 2),
            this.yPositionOnScreen + InnerMargin,
            portraitSize,
            portraitSize
        );
    }

    private Rectangle GetPortraitPanelBounds()
    {
        return new Rectangle(
            this.xPositionOnScreen + this.width - PortraitPanelWidth,
            this.yPositionOnScreen + InnerMargin,
            PortraitPanelWidth - InnerMargin,
            this.height - (InnerMargin * 2)
        );
    }

    private Rectangle GetTextBounds(bool includePortrait = true)
    {
        int rightPadding = includePortrait ? PortraitPanelWidth : InnerMargin;
        return new Rectangle(
            this.xPositionOnScreen + InnerMargin,
            this.yPositionOnScreen + InnerMargin + 4,
            this.width - rightPadding - (InnerMargin * 2),
            this.height - (InnerMargin * 2) - 4
        );
    }
}
