using System.Drawing;
using System.Windows.Forms;
using OverlayPoc.Chat;

namespace OverlayPoc;

internal sealed partial class OverlayForm
{
    private const int CardPadding = 12;
    private const int CardHeaderHeight = 22;
    private const int CardSpacing = 10;

    private int _lastMessagesWidth;
    private bool _rendering;

    private void OnHistorySelectionChanged(object sender, EventArgs e)
    {
        if (_suppressHistorySelection) return;

        if (_history.SelectedItem is ChatConversation conversation)
        {
            _currentConversation = conversation;
            RenderConversation();
        }
    }

    private void StartConversation()
    {
        var conversation = new ChatConversation("New conversation");
        _conversations.Insert(0, conversation);

        _suppressHistorySelection = true;
        _history.Items.Insert(0, conversation);
        _history.SelectedIndex = 0;
        _suppressHistorySelection = false;

        _currentConversation = conversation;
        _input.Clear();
        RenderConversation();
        _input.Focus();
    }

    private void SendMessage()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _currentConversation is null) return;

        _currentConversation.Messages.Add(new ChatMessage(false, text));

        if (_currentConversation.Messages.Count == 1)
        {
            _currentConversation.Title = text.Length > 28 ? text[..28] + "…" : text;
            RefreshSelectedHistoryItem();
        }

        _currentConversation.Messages.Add(new ChatMessage(true,
            "This prototype does not have an assistant service connected yet. " +
            "Your conversation is kept in memory until the app closes."));

        _input.Clear();
        RenderConversation();

        // Sending ends typing mode: the overlay goes back to click-through and
        // focus returns to whatever the user was doing.
        if (_interactive) ToggleInteractive();
    }

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            if (_interactive) ToggleInteractive();
            return;
        }

        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            SendMessage();
        }
    }

    private void OnMessagesSizeChanged(object sender, EventArgs e)
    {
        if (_rendering) return;

        var width = _messages.ClientSize.Width;
        if (width <= 0) return;

        // A vertical scrollbar also changes ClientSize.Width. The threshold
        // ignores that 17px change so the conversation does not rebuild in a
        // loop; real window resizes still reflow every card.
        if (Math.Abs(width - _lastMessagesWidth) < 20) return;

        _lastMessagesWidth = width;
        RenderConversation();
    }

    private void RefreshSelectedHistoryItem()
    {
        var index = _history.SelectedIndex;
        if (index < 0) return;

        _suppressHistorySelection = true;
        _history.Items[index] = _currentConversation;
        _history.SelectedIndex = index;
        _suppressHistorySelection = false;
    }

    private void RenderConversation()
    {
        if (_currentConversation is null || _rendering) return;

        _rendering = true;
        _messages.SuspendLayout();
        try
        {
            // Controls.Clear() alone leaves the old controls alive; dispose them
            // so switching conversations does not leak handles.
            var previous = _messages.Controls.Cast<Control>().ToArray();
            _messages.Controls.Clear();
            foreach (var control in previous) control.Dispose();

            foreach (var message in _currentConversation.Messages)
            {
                _messages.Controls.Add(CreateMessageCard(message));
            }
        }
        finally
        {
            _messages.ResumeLayout();
            _rendering = false;
        }

        if (_messages.Controls.Count > 0)
        {
            _messages.ScrollControlIntoView(
                _messages.Controls[_messages.Controls.Count - 1]);
        }
    }

    /// <summary>
    /// Builds a card with an explicit size instead of AutoSize. AutoSize panels
    /// containing docked children were the reason messages rendered with the
    /// wrong width and clipped text.
    /// </summary>
    private Control CreateMessageCard(ChatMessage message)
    {
        var cardWidth = Math.Max(240, _messages.ClientSize.Width - 30);
        var textWidth = Math.Max(180, cardWidth - CardPadding * 2);

        Label body = null;
        var bodyHeight = 0;

        if (!message.Collapsed)
        {
            body = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Text = message.Text,
                ForeColor = Theme.TextPrimary,
                Font = Theme.Body,
                Location = new Point(CardPadding, CardPadding + CardHeaderHeight)
            };

            // Let the label itself decide how tall the wrapped text is; using a
            // separate TextRenderer measurement clipped long messages.
            bodyHeight = body.PreferredSize.Height + 6;
        }

        var card = new Panel
        {
            Width = cardWidth,
            Height = CardPadding * 2 + CardHeaderHeight + bodyHeight,
            Margin = new Padding(0, 0, 0, CardSpacing),
            BackColor = message.IsAssistant ? Theme.InputBackground : Theme.UserBubble
        };

        var name = new Label
        {
            AutoSize = false,
            Text = message.IsAssistant ? "ASSISTANT" : "YOU",
            ForeColor = message.IsAssistant ? Theme.AssistantAccent : Theme.UserAccent,
            Font = Theme.CaptionBold,
            Location = new Point(CardPadding, CardPadding),
            Size = new Size(textWidth - 32, 18),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var toggle = new Button
        {
            Text = message.Collapsed ? "+" : "−",
            FlatStyle = FlatStyle.Flat,
            BackColor = card.BackColor,
            ForeColor = Theme.TextSecondary,
            Font = Theme.Glyph,
            Cursor = Cursors.Hand,
            Size = new Size(24, 20),
            Location = new Point(cardWidth - CardPadding - 24, CardPadding),
            TabStop = false,
            FlatAppearance = { BorderSize = 0 }
        };

        toggle.Click += (_, _) =>
        {
            message.Collapsed = !message.Collapsed;
            // Rebuild after the click finishes, not while this button's own
            // event handler is still on the stack (it is about to be disposed).
            BeginInvoke(new Action(RenderConversation));
        };

        card.Controls.Add(name);
        card.Controls.Add(toggle);

        if (body != null)
        {
            card.Controls.Add(body);
        }

        // Grabbing a bubble also moves the overlay.
        EnableDrag(card);
        return card;
    }
}
