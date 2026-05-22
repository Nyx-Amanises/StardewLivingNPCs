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
        private readonly NPC _currentNpc;

        // Menu dimensions
        private const int MaxMenuWidth = 1248;
        private const int MenuHeight = 336;
        private const int OuterMargin = 16;
        private const int InnerMargin = 28;
        private const int PortraitPanelWidth = 320;
        private const int PortraitSize = 256;

        // Positions
        private Vector2 _menuPosition;
        private Rectangle _menuBounds;
        private Rectangle _textBounds;
        private Rectangle _portraitBounds;

        public DialogueTextInputMenu(string title, TextSubmittedDelegate callback, NPC currentNpc)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "你想说什么？" : title;
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

            // Draw menu background
            Game1.drawDialogueBox(_menuBounds.X, _menuBounds.Y, _menuBounds.Width, _menuBounds.Height, false, true);

            // Draw title
            var titlePos = new Vector2(
                _textBounds.X,
                _menuBounds.Y + InnerMargin
            );
            spriteBatch.DrawString(Game1.dialogueFont, _title, titlePos, Game1.textColor);

            DrawPortrait(spriteBatch);

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
            int menuWidth = Math.Min(viewportWidth - (OuterMargin * 2), MaxMenuWidth);
            int menuHeight = Math.Min(MenuHeight, viewportHeight - (OuterMargin * 2));
            _menuPosition = new Vector2(
                (viewportWidth - menuWidth) / 2,
                viewportHeight - menuHeight - OuterMargin
            );

            _menuBounds = new Rectangle((int)_menuPosition.X, (int)_menuPosition.Y, menuWidth, menuHeight);

            var titleSize = Game1.dialogueFont.MeasureString(_title);
            int portraitPanelWidth = _currentNpc?.Portrait != null
                ? Math.Min(PortraitPanelWidth, Math.Max(0, menuWidth / 3))
                : 0;
            int rightPadding = portraitPanelWidth > 0 ? portraitPanelWidth : InnerMargin;
            _textBounds = new Rectangle(
                _menuBounds.X + InnerMargin,
                _menuBounds.Y + InnerMargin + (int)titleSize.Y + 18,
                Math.Max(80, _menuBounds.Width - rightPadding - (InnerMargin * 2)),
                Math.Max(48, _menuBounds.Height - (InnerMargin * 2) - (int)titleSize.Y - 24)
            );
            if (portraitPanelWidth > 0)
            {
                int portraitSize = Math.Min(PortraitSize, Math.Max(96, _menuBounds.Height - (InnerMargin * 2) - 64));
                var portraitPanel = new Rectangle(
                    _menuBounds.X + _menuBounds.Width - portraitPanelWidth,
                    _menuBounds.Y + InnerMargin,
                    portraitPanelWidth - InnerMargin,
                    _menuBounds.Height - (InnerMargin * 2)
                );
                _portraitBounds = new Rectangle(
                    portraitPanel.X + ((portraitPanel.Width - portraitSize) / 2),
                    _menuBounds.Y + InnerMargin,
                    portraitSize,
                    portraitSize
                );
            }
            else
            {
                _portraitBounds = Rectangle.Empty;
            }

            _inputTextBox.Position = new Vector2(
                _textBounds.X,
                _textBounds.Y
            );
            _inputTextBox.Extent = new Vector2(
                _textBounds.Width,
                _textBounds.Height
            );
        }

        private void DrawPortrait(SpriteBatch spriteBatch)
        {
            if (_currentNpc?.Portrait == null || _portraitBounds == Rectangle.Empty)
            {
                return;
            }

            spriteBatch.Draw(
                _currentNpc.Portrait,
                _portraitBounds,
                new Rectangle(0, 0, 64, 64),
                Color.White
            );

            string name = _currentNpc.displayName ?? string.Empty;
            Vector2 nameSize = Game1.dialogueFont.MeasureString(name);
            int nameY = Math.Min(
                _menuBounds.Bottom - InnerMargin - (int)nameSize.Y,
                _portraitBounds.Bottom + 8
            );
            spriteBatch.DrawString(
                Game1.dialogueFont,
                name,
                new Vector2(
                    _portraitBounds.X + ((_portraitBounds.Width - nameSize.X) / 2),
                    nameY
                ),
                Game1.textColor
            );
        }
    }
}
