using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
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

        private readonly string _title;
        private readonly DialogueTextInputBox _inputTextBox;
        private readonly TextSubmittedDelegate _onTextSubmitted;

        // Menu dimensions
        private const int MaxMenuWidth = 1200;
        private const int MenuHeight = 292;
        private const int TextBoxHeight = 152;
        private const int Margin = 28;

        // Positions
        private Vector2 _menuPosition;
        private Rectangle _menuBounds;

        public DialogueTextInputMenu(string title, TextSubmittedDelegate callback, NPC currentNpc)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "你想说什么？" : title;
            _onTextSubmitted = callback;

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

            // Draw menu background
            Game1.drawDialogueBox(_menuBounds.X, _menuBounds.Y, _menuBounds.Width, _menuBounds.Height, false, true);

            // Draw title
            var titleSize = Game1.dialogueFont.MeasureString(_title);
            var titlePos = new Vector2(
                _menuPosition.X + 2 * Margin,
                _menuPosition.Y + 2 * Margin
            );
            spriteBatch.DrawString(Game1.dialogueFont, _title, titlePos, Game1.textColor);

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
            int menuWidth = Math.Min(Game1.uiViewport.Width - (Margin * 2), MaxMenuWidth);
            int menuHeight = Math.Min(MenuHeight, Game1.uiViewport.Height - (Margin * 2));
            _menuPosition = new Vector2(
                (Game1.uiViewport.Width - menuWidth) / 2,
                Game1.uiViewport.Height - menuHeight - Margin
            );

            _menuBounds = new Rectangle((int)_menuPosition.X, (int)_menuPosition.Y, menuWidth, menuHeight);

            var titleSize = Game1.dialogueFont.MeasureString(_title);
            _inputTextBox.Position = new Vector2(
                _menuPosition.X + (Margin * 2),
                _menuPosition.Y + titleSize.Y + (Margin * 3)
            );
            _inputTextBox.Extent = new Vector2(
                menuWidth - (Margin * 4),
                Math.Min(TextBoxHeight, menuHeight - (int)titleSize.Y - (Margin * 5))
            );
        }
    }
}
