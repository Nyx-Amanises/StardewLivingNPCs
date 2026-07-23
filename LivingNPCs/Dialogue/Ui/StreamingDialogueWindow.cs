using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using GameDialogue = StardewValley.Dialogue;
namespace LivingNPCs.Dialogue.Ui;

internal sealed class StreamingDialogueWindow : IClickableMenu
{
    private const int TextInsetX = 40;
    private const int TextInsetTop = 40;
    private const int TextInsetBottom = 40;
    private const int PortraitPanelWidth = 388;
    private const float RevealMillisecondsPerCharacter = 81f;

    private readonly object sync = new();
    private readonly NPC npc;
    private readonly StreamingDialogueUpdateQueue pendingUpdates = new();
    private readonly List<string> pages = new();
    private readonly List<int> pagePortraitFrameIndexes = new();
    private readonly List<StreamingDialoguePreview.DisplaySegment> displaySegments = new();
    private readonly List<StreamingResponseOption> responseOptions = new();
    private readonly List<Rectangle> responseOptionBounds = new();
    private string displayText = string.Empty;
    private bool generationComplete;
    private bool showingResponses;
    private Action? onFinished;
    private Action<StreamingResponseOption>? onResponseSelected;
    private int currentPageIndex;
    private int selectedResponseIndex;
    private int animationFrame;
    private int visibleCharacterCount;
    private float animationTimer;
    private float revealTimer;
    private int lastTextWidth;
    private int lastTextHeight;
    private DialogueBox? dialogueShell;
    private GameDialogue? portraitDialogue;
    private int lastViewportWidth;
    private int lastViewportHeight;

    public StreamingDialogueWindow(NPC npc)
    {
        this.npc = npc;
        this.UpdateLayout();
    }

    public void AppendToken(string token)
    {
        this.pendingUpdates.AppendToken(token);
    }

    public void Complete(
        GenerationResult result,
        IEnumerable<StreamingResponseOption> responseOptions,
        Action<StreamingResponseOption> onResponseSelected,
        Action onFinished)
    {
        this.pendingUpdates.Complete(result, responseOptions, onResponseSelected, onFinished);
    }

    public override void update(GameTime time)
    {
        base.update(time);
        this.UpdateLayout(rebuildIfNeeded: true);
        this.ApplyPendingUpdates();
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
        if (this.showingResponses)
        {
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
            this.DrawResponseOptions(b);
            this.DrawCursor(b);
            return;
        }

        this.DrawDialogueChrome(b);

        string pageText;
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
            }
            else
            {
                pageText = this.GetAnimatedDots();
            }
        }

        Rectangle textBounds = this.GetTextBounds();
        this.DrawSpriteTextBlock(b, pageText, textBounds, Game1.textColor);

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
        Action? finished = null;
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

    private void RebuildPages(IReadOnlyList<StreamingDialoguePreview.DisplaySegment> segments)
    {
        this.UpdateLayout();
        var stableSegments = (segments ?? Array.Empty<StreamingDialoguePreview.DisplaySegment>())
            .Where(segment => segment != null && !string.IsNullOrWhiteSpace(segment.Text))
            .ToList();
        this.displaySegments.Clear();
        this.displaySegments.AddRange(stableSegments);
        this.displayText = string.Join("\f", stableSegments.Select(segment => segment.Text));
        var rebuilt = new List<string>();
        var rebuiltPortraitFrames = new List<int>();
        Rectangle textBounds = this.GetTextBounds();
        int lineHeight = GetSpriteTextLineHeight(textBounds.Width);
        int maxLines = Math.Max(1, textBounds.Height / lineHeight);

        foreach (StreamingDialoguePreview.DisplaySegment segment in stableSegments)
        {
            var lines = WrapSpriteText(segment.Text, textBounds.Width);
            for (int i = 0; i < lines.Length; i += maxLines)
            {
                rebuilt.Add(string.Join("\n", lines.Skip(i).Take(maxLines)));
                rebuiltPortraitFrames.Add(segment.PortraitFrameIndex);
            }
        }

        this.pages.Clear();
        this.pagePortraitFrameIndexes.Clear();
        this.pages.AddRange(rebuilt.Where(page => !string.IsNullOrWhiteSpace(page)));
        this.pagePortraitFrameIndexes.AddRange(
            rebuilt
                .Select((page, index) => (page, index))
                .Where(item => !string.IsNullOrWhiteSpace(item.page))
                .Select(item => rebuiltPortraitFrames[item.index]));
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
        Action<StreamingResponseOption>? selectedCallback;

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

        int lineHeight = Math.Max(48, GetSpriteTextLineHeight(textBounds.Width) + 6);
        int y = textBounds.Y;

        for (int i = 0; i < this.responseOptions.Count; i++)
        {
            string optionText = string.Join("\n", WrapSpriteText(this.responseOptions[i].Text, textBounds.Width - 32));
            int optionHeight = Math.Max(lineHeight, (optionText.Count(c => c == '\n') + 1) * lineHeight);
            var bounds = new Rectangle(textBounds.X, y - 4, textBounds.Width, optionHeight);
            this.responseOptionBounds.Add(bounds);

            if (i == this.selectedResponseIndex)
            {
                b.Draw(Game1.staminaRect, bounds, Color.Brown * 0.18f);
            }

            this.DrawSpriteTextBlock(
                b,
                optionText,
                new Rectangle(bounds.X + 12, y, bounds.Width - 24, bounds.Height),
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
        if (this.dialogueShell == null || viewportWidth != this.lastViewportWidth || viewportHeight != this.lastViewportHeight)
        {
            this.dialogueShell = this.npc != null
                ? new DialogueBox(this.portraitDialogue = new GameDialogue(this.npc, EngineConstants.KeyStreaming, " "))
                : new DialogueBox(" ");
            this.lastViewportWidth = viewportWidth;
            this.lastViewportHeight = viewportHeight;
        }

        this.width = this.dialogueShell.width;
        this.height = this.dialogueShell.height;
        this.xPositionOnScreen = this.dialogueShell.x;
        this.yPositionOnScreen = this.dialogueShell.y;

        Rectangle textBounds = this.GetTextBounds();
        bool textBoundsChanged = textBounds.Width != this.lastTextWidth || textBounds.Height != this.lastTextHeight;
        this.lastTextWidth = textBounds.Width;
        this.lastTextHeight = textBounds.Height;

        if (rebuildIfNeeded && textBoundsChanged && !string.IsNullOrWhiteSpace(this.displayText))
        {
            lock (this.sync)
            {
                this.RebuildPages(this.displaySegments.ToArray());
            }
        }
    }

    private Rectangle GetTextBounds(bool includePortrait = true)
    {
        int rightPadding = includePortrait && this.npc?.Portrait != null ? PortraitPanelWidth : TextInsetX;
        return new Rectangle(
            this.xPositionOnScreen + TextInsetX,
            this.yPositionOnScreen + TextInsetTop,
            Math.Max(120, this.width - rightPadding - (TextInsetX * 2)),
            Math.Max(48, this.height - TextInsetTop - TextInsetBottom)
        );
    }

    private void DrawDialogueChrome(SpriteBatch b)
    {
        if (this.npc?.Portrait == null)
        {
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
            return;
        }

        int textPanelWidth = Math.Max(120, this.width - PortraitPanelWidth);
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, textPanelWidth, this.height, false, true);
        this.UpdatePortraitEmotion();
        this.dialogueShell?.drawPortrait(b);
    }

    private void ApplyPendingUpdates()
    {
        StreamingDialogueUpdateQueue.PendingUpdate pending = this.pendingUpdates.Drain();
        if (!pending.HasValue)
        {
            return;
        }

        if (pending.Final is { } final)
        {
            string parsedDialogueLine = final.Result.ParsedLines.FirstOrDefault() ?? string.Empty;
            if (final.Result.Commit?.Request is { } request)
            {
                parsedDialogueLine = DialogueEngineHost.RevalidatePortraitMarkers(
                    this.npc,
                    request,
                    parsedDialogueLine);
            }

            IReadOnlyList<StreamingDialoguePreview.DisplaySegment> finalSegments =
                StreamingDialoguePreview.PrepareDisplaySegments(parsedDialogueLine);
            lock (this.sync)
            {
                this.generationComplete = true;
                this.onFinished = final.OnFinished;
                this.onResponseSelected = final.OnResponseSelected;
                this.responseOptions.Clear();
                this.responseOptions.AddRange(final.ResponseOptions);
                this.RebuildPages(finalSegments);
                if (this.pages.Count == 0)
                {
                    this.RebuildPages(new[] { new StreamingDialoguePreview.DisplaySegment("...", 0) });
                }
            }

            return;
        }

        string preview = StreamingDialoguePreview.ExtractVisibleText(pending.PreviewRawText ?? string.Empty);
        IReadOnlyList<StreamingDialoguePreview.DisplaySegment> previewSegments =
            StreamingDialoguePreview.PrepareDisplaySegments(preview);
        lock (this.sync)
        {
            this.RebuildPages(previewSegments);
        }
    }

    private void UpdatePortraitEmotion()
    {
        int frameIndex;
        lock (this.sync)
        {
            frameIndex = this.currentPageIndex >= 0
                && this.currentPageIndex < this.pagePortraitFrameIndexes.Count
                ? this.pagePortraitFrameIndexes[this.currentPageIndex]
                : 0;
        }

        Texture2D? portrait = this.npc?.Portrait;
        int physicalFrameCount = portrait == null
            ? 0
            : PortraitFrameSemantics.GetPhysicalFrameCount(portrait.Width, portrait.Height);
        if (frameIndex < 0 || frameIndex >= physicalFrameCount)
        {
            frameIndex = 0;
        }

        string marker = PortraitMarkerRules.MarkerForIndex(frameIndex) ?? "0";
        if (this.portraitDialogue != null)
        {
            this.portraitDialogue.CurrentEmotion = "$" + marker;
        }
    }

    private void DrawSpriteTextBlock(SpriteBatch b, string text, Rectangle bounds, Color color)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        int lineHeight = GetSpriteTextLineHeight(bounds.Width);
        int y = bounds.Y;
        foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
        {
            if (y + lineHeight > bounds.Bottom)
            {
                break;
            }

            SpriteText.drawString(
                b,
                line,
                bounds.X,
                y,
                999999,
                bounds.Width,
                lineHeight,
                1f,
                0.88f,
                false,
                -1,
                "",
                color,
                SpriteText.ScrollTextAlignment.Left
            );
            y += lineHeight;
        }
    }

    private static string[] WrapSpriteText(string text, int maxWidth)
    {
        var lines = new List<string>();
        foreach (string paragraph in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            string currentLine = string.Empty;
            foreach (char character in paragraph)
            {
                string testLine = currentLine + character;
                if (SpriteText.getWidthOfString(testLine, 999999) <= maxWidth || string.IsNullOrEmpty(currentLine))
                {
                    currentLine = testLine;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = character.ToString();
                }
            }

            lines.Add(currentLine);
        }

        return lines.Where(line => !string.IsNullOrWhiteSpace(line)).DefaultIfEmpty(string.Empty).ToArray();
    }

    private static int GetSpriteTextLineHeight(int width)
    {
        return Math.Max(42, SpriteText.getHeightOfString("A", Math.Max(1, width)) + 4);
    }
}
