using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using System;

namespace ValleyTalk
{
    /// <summary>
    /// Menu for dialogue text input with larger interface
    /// </summary>
    public class DialogueTextInputMenu
    {
        public delegate void TextSubmittedDelegate(string input);

        private readonly DialogueTextInputBox _inputTextBox;
        private readonly TextSubmittedDelegate _onTextSubmitted;
        private readonly NPC _currentNpc;
        private DialogueBox _dialogueShell;
        private int _lastViewportWidth;
        private int _lastViewportHeight;

        private const int TextInsetX = 40;
        private const int TextInsetTop = 40;
        private const int TextInsetBottom = 40;
        private const int PortraitPanelWidth = 388;

        // Positions
        private Rectangle _menuBounds;
        private Rectangle _textBounds;

        public DialogueTextInputMenu(string title, TextSubmittedDelegate callback, NPC currentNpc)
        {
            _onTextSubmitted = callback;
            _currentNpc = currentNpc;

            _inputTextBox = new DialogueTextInputBox(500)
            {
                Font = Game1.dialogueFont,
                TextColor = Game1.textColor,
                Selected = true
            };
            _inputTextBox.OnSubmit += (sender) => Submit(sender.Text);

            UpdateLayout();

            // Set up keyboard input
            Game1.keyboardDispatcher.Subscriber = _inputTextBox;
        }

        public void Close()
        {
            Game1.keyboardDispatcher.Subscriber = null;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            UpdateLayout();

            DrawDialogueChrome(spriteBatch);

            // Draw text input box
            _inputTextBox.Draw(spriteBatch);

            // Draw mouse cursor
            if (!Game1.options.hardwareCursor)
            {
                spriteBatch.Draw(Game1.mouseCursors, new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                    Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 0, 16, 16),
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            }
        }

        public void ReceiveLeftClick(int x, int y)
        {
            UpdateLayout();

            if (_inputTextBox.ContainsPoint(x, y))
            {
                Game1.keyboardDispatcher.Subscriber = _inputTextBox;
            }
        }

        public void ReceiveKeyPress(Keys key)
        {
            if (key == Keys.Escape)
            {
                Submit("");
            }
            else
            {
                _inputTextBox.RecieveSpecialInput(key);
            }
        }

        public bool ContainsPoint(int x, int y)
        {
            return _menuBounds.Contains(x, y);
        }

        private void Submit(string text)
        {
            _onTextSubmitted?.Invoke(text ?? "");
        }

        private void UpdateLayout()
        {
            int viewportWidth = Math.Max(1, Game1.uiViewport.Width);
            int viewportHeight = Math.Max(1, Game1.uiViewport.Height);
            if (_dialogueShell == null || viewportWidth != _lastViewportWidth || viewportHeight != _lastViewportHeight)
            {
                _dialogueShell = _currentNpc != null
                    ? new DialogueBox(new Dialogue(_currentNpc, $"{SldConstants.DialogueKeyPrefix}Input", " "))
                    : new DialogueBox(" ");
                _lastViewportWidth = viewportWidth;
                _lastViewportHeight = viewportHeight;
            }

            _menuBounds = new Rectangle(_dialogueShell.x, _dialogueShell.y, _dialogueShell.width, _dialogueShell.height);

            int rightPadding = _currentNpc?.Portrait != null ? PortraitPanelWidth : TextInsetX;
            int textWidth = Math.Max(120, _menuBounds.Width - rightPadding - (TextInsetX * 2));
            int inputY = _menuBounds.Y + TextInsetTop;
            _textBounds = new Rectangle(
                _menuBounds.X + TextInsetX,
                inputY,
                textWidth,
                Math.Max(48, _menuBounds.Bottom - inputY - TextInsetBottom)
            );

            _inputTextBox.Position = new Vector2(
                _textBounds.X,
                _textBounds.Y
            );
            _inputTextBox.Extent = new Vector2(
                _textBounds.Width,
                _textBounds.Height
            );
        }

        private void DrawDialogueChrome(SpriteBatch spriteBatch)
        {
            if (_currentNpc?.Portrait == null)
            {
                Game1.drawDialogueBox(_menuBounds.X, _menuBounds.Y, _menuBounds.Width, _menuBounds.Height, false, true);
                return;
            }

            int textPanelWidth = Math.Max(120, _menuBounds.Width - PortraitPanelWidth);
            Game1.drawDialogueBox(_menuBounds.X, _menuBounds.Y, textPanelWidth, _menuBounds.Height, false, true);
            _dialogueShell.drawPortrait(spriteBatch);
        }
    }
}
