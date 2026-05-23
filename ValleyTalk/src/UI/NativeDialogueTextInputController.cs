using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace ValleyTalk;

internal static class NativeDialogueTextInputController
{
    private const int CharacterLimit = 500;

    private static bool active;
    private static NPC currentNpc;
    private static DialogueBox activeBox;
    private static string prompt;
    private static string text;
    private static int caretPosition;
    private static Action<string> onSubmitted;
    private static InputSubscriber subscriber;

    public static void Start(string title, NPC npc, Action<string> callback)
    {
        if (npc == null)
        {
            callback?.Invoke(string.Empty);
            return;
        }

        active = true;
        currentNpc = npc;
        activeBox = null;
        prompt = string.IsNullOrWhiteSpace(title) ? "你想说什么？" : title;
        text = string.Empty;
        caretPosition = 0;
        onSubmitted = callback;

        var dialogue = new Dialogue(npc, $"{SldConstants.DialogueKeyPrefix}Input", prompt);
        npc.CurrentDialogue.Push(dialogue);
        Game1.DrawDialogue(dialogue);
        npc.CurrentDialogue.TryPop(out _);
        activeBox = Game1.activeClickableMenu as DialogueBox;

        subscriber = new InputSubscriber();
        Game1.keyboardDispatcher.Subscriber = subscriber;
    }

    public static bool IsInputBox(DialogueBox dialogueBox)
    {
        if (!active || dialogueBox == null)
        {
            return false;
        }

        if (activeBox != null && !ReferenceEquals(activeBox, dialogueBox))
        {
            return false;
        }

        return string.Equals(dialogueBox.characterDialogue?.speaker?.Name, currentNpc?.Name, StringComparison.Ordinal);
    }

    public static bool TryGetDisplayText(DialogueBox dialogueBox, out string displayText)
    {
        displayText = null;
        if (!IsInputBox(dialogueBox))
        {
            return false;
        }

        dialogueBox.characterIndexInDialogue = 999999;
        displayText = prompt;
        return true;
    }

    public static void DrawInputText(DialogueBox dialogueBox, SpriteBatch spriteBatch)
    {
        if (!TryGetInputText(dialogueBox, out string inputText))
        {
            return;
        }

        Rectangle textBounds = GetInputBounds(dialogueBox);
        DrawWrappedText(spriteBatch, inputText, textBounds, Game1.textColor);
    }

    public static void HandleSpecialKey(Keys key)
    {
        if (!active)
        {
            return;
        }

        switch (key)
        {
            case Keys.Enter:
                Submit();
                break;
            case Keys.Escape:
                Cancel();
                break;
            case Keys.Left:
                caretPosition = Math.Max(0, caretPosition - 1);
                break;
            case Keys.Right:
                caretPosition = Math.Min(text.Length, caretPosition + 1);
                break;
            case Keys.Home:
                caretPosition = 0;
                break;
            case Keys.End:
                caretPosition = text.Length;
                break;
            case Keys.Back:
                Backspace();
                break;
            case Keys.Delete:
                Delete();
                break;
        }
    }

    private static void Submit()
    {
        string submitted = text ?? string.Empty;
        Action<string> callback = onSubmitted;
        Close();
        callback?.Invoke(submitted);
    }

    private static void Cancel()
    {
        Action<string> callback = onSubmitted;
        Close();
        callback?.Invoke(string.Empty);
    }

    private static void Close()
    {
        if (Game1.activeClickableMenu is DialogueBox dialogueBox && IsInputBox(dialogueBox))
        {
            Game1.exitActiveMenu();
        }

        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, subscriber))
        {
            Game1.keyboardDispatcher.Subscriber = null;
        }

        active = false;
        currentNpc = null;
        activeBox = null;
        prompt = null;
        text = string.Empty;
        caretPosition = 0;
        onSubmitted = null;
        subscriber = null;
    }

    private static void InsertText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (char character in value)
        {
            InsertCharacter(character);
        }
    }

    private static void InsertCharacter(char character)
    {
        if (char.IsControl(character) || text.Length >= CharacterLimit)
        {
            return;
        }

        text = text.Insert(caretPosition, character.ToString());
        caretPosition++;
    }

    private static void Backspace()
    {
        if (caretPosition <= 0 || text.Length == 0)
        {
            return;
        }

        text = text.Remove(caretPosition - 1, 1);
        caretPosition--;
    }

    private static void Delete()
    {
        if (caretPosition >= text.Length)
        {
            return;
        }

        text = text.Remove(caretPosition, 1);
    }

    private static bool ShouldShowCaret()
    {
        return ((int)((Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d) / 450d) % 2) == 0;
    }

    private static bool TryGetInputText(DialogueBox dialogueBox, out string inputText)
    {
        inputText = null;
        if (!IsInputBox(dialogueBox))
        {
            return false;
        }

        string caret = ShouldShowCaret() ? "|" : string.Empty;
        int safeCaretPosition = Math.Clamp(caretPosition, 0, text?.Length ?? 0);
        inputText = string.IsNullOrEmpty(text)
            ? caret
            : text.Insert(safeCaretPosition, caret);
        return true;
    }

    private static Rectangle GetInputBounds(DialogueBox dialogueBox)
    {
        int rightPadding = dialogueBox.characterDialogue?.speaker?.Portrait != null ? 388 : 40;
        return new Rectangle(
            dialogueBox.x + 40,
            dialogueBox.y + 92,
            Math.Max(120, dialogueBox.width - rightPadding - 80),
            Math.Max(48, dialogueBox.height - 128)
        );
    }

    private static void DrawWrappedText(SpriteBatch spriteBatch, string value, Rectangle bounds, Color color)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        int lineHeight = GetLineHeight(bounds.Width);
        int maxLines = Math.Max(1, bounds.Height / lineHeight);
        string[] lines = WrapText(value, bounds.Width);
        if (lines.Length > maxLines)
        {
            lines = lines.Skip(lines.Length - maxLines).ToArray();
        }

        int y = bounds.Y;
        foreach (string line in lines)
        {
            if (y + lineHeight > bounds.Bottom)
            {
                break;
            }

            SpriteText.drawString(
                spriteBatch,
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

    private static string[] WrapText(string value, int maxWidth)
    {
        var lines = new List<string>();
        foreach (string paragraph in (value ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
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

    private static int GetLineHeight(int width)
    {
        return Math.Max(42, SpriteText.getHeightOfString("A", Math.Max(1, width)) + 4);
    }

    private sealed class InputSubscriber : IKeyboardSubscriber
    {
        public bool Selected { get; set; } = true;

        public void RecieveTextInput(char inputChar)
        {
            if (inputChar is '\r' or '\n')
            {
                Submit();
                return;
            }

            if (inputChar == '\b')
            {
                Backspace();
                return;
            }

            InsertCharacter(inputChar);
        }

        public void RecieveTextInput(string text)
        {
            InsertText(text);
        }

        public void RecieveCommandInput(char command)
        {
            if ((Keys)command == Keys.Enter)
            {
                Submit();
            }
        }

        public void RecieveSpecialInput(Keys key)
        {
            HandleSpecialKey(key);
        }
    }
}
