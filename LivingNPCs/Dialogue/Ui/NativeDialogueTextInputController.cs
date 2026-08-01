using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.GameHooks;
using GameDialogue = StardewValley.Dialogue;
namespace LivingNPCs.Dialogue.Ui;

internal static class NativeDialogueTextInputController
{
    private const int CharacterLimit = 500;
    private const int WrapSafetyPixels = 96;

    private static bool active;
    private static NPC currentNpc;
    private static DialogueBox activeBox;
    private static GameDialogue activeDialogue;
    private static string prompt;
    private static string text;
    private static int caretPosition;
    private static Action<string> onSubmitted;
    private static InputSubscriber subscriber;
    private static Keys? repeatingKey;
    private static double nextRepeatAtMilliseconds;

    private const double InitialRepeatDelayMilliseconds = 320d;
    private const double RepeatDelayMilliseconds = 48d;

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
        activeDialogue = null;
        prompt = string.IsNullOrWhiteSpace(title) ? "What do you want to say?" : title;
        text = string.Empty;
        caretPosition = 0;
        onSubmitted = callback;

        var dialogue = new GameDialogue(npc, EngineConstants.KeyInput, prompt);
        activeDialogue = dialogue;
        npc.CurrentDialogue.Push(dialogue);
        Game1.currentSpeaker = npc;
        Game1.DrawDialogue(dialogue);
        activeBox = Game1.activeClickableMenu as DialogueBox;

        subscriber = new InputSubscriber();
        Game1.keyboardDispatcher.Subscriber = subscriber;
        DialogueServices.Helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
        DialogueServices.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
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
                BeginKeyRepeat(key);
                break;
            case Keys.Delete:
                Delete();
                BeginKeyRepeat(key);
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
        var npc = currentNpc;
        var dialogueBox = Game1.activeClickableMenu as DialogueBox;
        bool closingInputBox = IsInputBox(dialogueBox);

        DialogueUiStateGuard.RemoveDialogue(npc, activeDialogue);
        if (closingInputBox)
        {
            DialogueUiStateGuard.ClearDialogueState(npc, dialogueBox);
        }
        else if (DialogueUiStateGuard.ShouldClearResidualDialogueState(
            menuIsNull: Game1.activeClickableMenu == null,
            currentSpeakerIsOwnNpc: npc != null && ReferenceEquals(Game1.currentSpeaker, npc),
            ownNpcStackEmpty: DialogueUiStateGuard.HasEmptyDialogueStack(npc)))
        {
            // 输入框已被外力替换/关闭时的兜底：仅清理确属本会话的残留（F3 三重守门），
            // 不动其他代码正在显示的菜单。
            DialogueUiStateGuard.ClearDialogueState(npc);
        }

        ReleaseInput();
        ClearFields();
    }

    /// <summary>
    /// 存档作用域强制释放（SaveLoaded / ReturnedToTitle 由接线层调用）：残留会话直接废弃，
    /// 不触发提交回调（不发好感、不发起生成），并归还键盘订阅。无会话时安全空转。
    /// </summary>
    public static void ForceClose()
    {
        if (!active)
        {
            return;
        }

        AbandonSession("save-scope reset");
    }

    /// <summary>
    /// 看门狗 / 强制释放共用的废弃路径：与 Cancel 等价的输入释放，但绝不调用提交回调。
    /// 先释放订阅并清空会话字段（此后 IsInputBox 恒 false），再摘除残留对白；仅当全局对话
    /// 状态确为本会话残留（无任何菜单 + currentSpeaker 仍指向本 NPC + 对白栈已空）时才清理
    /// 全局状态，绝不动其他代码打开的菜单。
    /// </summary>
    private static void AbandonSession(string reason)
    {
        NPC npc = currentNpc;
        GameDialogue dialogue = activeDialogue;

        ReleaseInput();
        ClearFields();

        try
        {
            DialogueUiStateGuard.RemoveDialogue(npc, dialogue);
            if (ShouldClearAbandonedDialogueState(
                menuIsNull: Game1.activeClickableMenu == null,
                currentSpeakerIsOwnNpc: npc != null && ReferenceEquals(Game1.currentSpeaker, npc),
                ownNpcStackEmpty: DialogueUiStateGuard.HasEmptyDialogueStack(npc)))
            {
                DialogueUiStateGuard.ClearDialogueState(npc);
            }
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "clean up an abandoned typed-dialogue session", error = ex.Message },
                    $"Failed to clean up an abandoned typed-dialogue session: {ex.Message}"),
                StardewModdingAPI.LogLevel.Trace);
        }

        DialogueServices.Monitor?.Log(
            Util.GetConsoleString(
                "dialogue.log.inputSessionAbandoned",
                new { npc = npc?.Name ?? "?", reason },
                $"Abandoned the typed-dialogue input session for {npc?.Name} ({reason}); no text was submitted."),
            StardewModdingAPI.LogLevel.Trace);
    }

    /// <summary>看门狗判定（纯函数）：会话活跃而宿主输入框已不在 → 废弃。</summary>
    internal static bool ShouldAbandonSession(bool sessionActive, bool hostInputBoxAlive)
    {
        return sessionActive && !hostInputBoxAlive;
    }

    /// <summary>
    /// 废弃后是否清理全局对话状态：转发共用三重守门
    /// <see cref="DialogueUiStateGuard.ShouldClearResidualDialogueState"/>（F1/F3 同一套语义）。
    /// </summary>
    internal static bool ShouldClearAbandonedDialogueState(bool menuIsNull, bool currentSpeakerIsOwnNpc, bool ownNpcStackEmpty)
    {
        return DialogueUiStateGuard.ShouldClearResidualDialogueState(menuIsNull, currentSpeakerIsOwnNpc, ownNpcStackEmpty);
    }

    /// <summary>归还键盘输入：订阅者仍是本会话的才清（他人已接管则不夺回），并退订 tick 泵。</summary>
    private static void ReleaseInput()
    {
        var dispatcher = Game1.keyboardDispatcher;
        if (dispatcher != null && ReferenceEquals(dispatcher.Subscriber, subscriber))
        {
            dispatcher.Subscriber = null;
        }

        var events = DialogueServices.Helper?.Events;
        if (events != null)
        {
            events.GameLoop.UpdateTicked -= OnUpdateTicked;
        }
    }

    private static void ClearFields()
    {
        active = false;
        currentNpc = null;
        activeBox = null;
        activeDialogue = null;
        prompt = null;
        text = string.Empty;
        caretPosition = 0;
        onSubmitted = null;
        subscriber = null;
        repeatingKey = null;
        nextRepeatAtMilliseconds = 0d;
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
        int leftPadding = 16;
        int topPadding = 64;
        int rightPadding = dialogueBox.characterDialogue?.speaker?.Portrait != null ? 360 : 28;
        int bottomPadding = 40;
        return new Rectangle(
            dialogueBox.x + leftPadding,
            dialogueBox.y + topPadding,
            Math.Max(160, dialogueBox.width - rightPadding - leftPadding - 24),
            Math.Max(80, dialogueBox.height - topPadding - bottomPadding)
        );
    }

    private static void DrawWrappedText(SpriteBatch spriteBatch, string value, Rectangle bounds, Color color)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        int wrapWidth = Math.Max(1, bounds.Width - WrapSafetyPixels);
        int lineHeight = GetLineHeight(wrapWidth);
        int maxLines = Math.Max(1, bounds.Height / lineHeight);
        string[] lines = WrapText(value, wrapWidth);
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
                wrapWidth,
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

    private static void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        // 看门狗也是静态订阅；副屏看不到主屏菜单，不能因此误判并废弃主屏输入会话。
        if (!ShouldProcessUpdateTick(PatchGuards.IsSplitScreenSecondaryBlocked(notifyPlayer: false)) || !active)
        {
            return;
        }

        // 看门狗：宿主对话框可被外力关闭（联机 2 点强制昏倒、节日强制散场、其他 mod 关菜单、
        // 断线回标题等），此时会话必须立即废弃并归还键盘订阅，否则劫持永久残留，之后任意时刻
        // 按 Enter 会凭空触发提交（发好感 + 发起生成），甚至把旧存档的会话带进新存档。
        if (ShouldAbandonSession(active, hostInputBoxAlive: IsInputBox(Game1.activeClickableMenu as DialogueBox)))
        {
            AbandonSession("host dialogue box closed externally");
            return;
        }

        if (repeatingKey == null)
        {
            return;
        }

        var keyboardState = Keyboard.GetState();
        Keys key = repeatingKey.Value;
        if (!keyboardState.IsKeyDown(key))
        {
            repeatingKey = null;
            return;
        }

        double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d;
        if (now < nextRepeatAtMilliseconds)
        {
            return;
        }

        PerformRepeatableEdit(key);
        nextRepeatAtMilliseconds = now + RepeatDelayMilliseconds;
    }

    /// <summary>分屏 v1：副屏 tick 不得驱动或废弃进程级静态输入会话。</summary>
    internal static bool ShouldProcessUpdateTick(bool isSplitScreenSecondary)
    {
        return !isSplitScreenSecondary;
    }

    private static void BeginKeyRepeat(Keys key)
    {
        repeatingKey = key;
        double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d;
        nextRepeatAtMilliseconds = now + InitialRepeatDelayMilliseconds;
    }

    private static void PerformRepeatableEdit(Keys key)
    {
        switch (key)
        {
            case Keys.Back:
                Backspace();
                break;
            case Keys.Delete:
                Delete();
                break;
        }
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
                BeginKeyRepeat(Keys.Back);
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
