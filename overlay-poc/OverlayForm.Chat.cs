using System.Drawing;
using System.Windows.Forms;
using OverlayPoc.Chat;

namespace OverlayPoc;

internal sealed partial class OverlayForm
{
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
        _input.Focus();
    }

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            SendMessage();
        }
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
        _messages.SuspendLayout();
        try
        {
            // Controls.Clear() alone leaves the old controls alive; dispose them
            // so switching conversations does not leak handles.
            var previous = _messages.Controls.Cast<Control>().ToArray();
            _messages.Controls.Clear();
            foreach (var control in previous) control.Dispose();

            if (_currentConversation != null)
            {
                foreach (var message in _currentConversation.Messages)
                {
                    _messages.Controls.Add(CreateMessageCard(message));
                }
            }
        }
        finally
        {
            _messages.ResumeLayout();
        }

        ResizeMessageCards();

        if (_messages.Controls.Count > 0)
        {
            _messages.ScrollControlIntoView(_messages.Controls[_messages.Controls.Count - 1]);
        }
    }

    private Control CreateMessageCard(ChatMessage message)
    {
        var card = new Panel
        {
            Width = Math.Max(240, _messages.ClientSize.Width - 28),
            AutoSize = true,
            Margin = new Padding(2, 4, 6, 8),
            Padding = new Padding(12),
            BackColor = message.IsAssistant ? Theme.InputBackground : Theme.UserBubble
        };

        var heading = new Panel
        {
            Dock = DockStyle.Top,
            Height = 25,
            BackColor = card.BackColor
        };

        var name = new Label
        {
            AutoSize = true,
            Text = message.IsAssistant ? "ASSISTANT" : "YOU",
            ForeColor = message.IsAssistant ? Theme.AssistantAccent : Theme.UserAccent,
            Font = Theme.CaptionBold,
            Location = new Point(0, 3)
        };

        // WinForms throws "Control does not support transparent background
        // colors" for buttons, so the collapse toggle uses the card colour.
        var toggle = new Button
        {
            Text = "−",
            Dock = DockStyle.Right,
            Width = 28,
            Height = 23,
            FlatStyle = FlatStyle.Flat,
            BackColor = card.BackColor,
            ForeColor = Theme.TextSecondary,
            Font = Theme.Glyph,
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };

        var body = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(Math.Max(180, card.Width - 28), 0),
            Text = message.Text,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            Padding = new Padding(0, 5, 0, 2)
        };

        toggle.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            toggle.Text = body.Visible ? "−" : "+";
            card.PerformLayout();
        };

        heading.Controls.Add(toggle);
        heading.Controls.Add(name);
        card.Controls.Add(body);
        card.Controls.Add(heading);
        return card;
    }

    private void ResizeMessageCards()
    {
        var width = Math.Max(240, _messages.ClientSize.Width - 28);

        foreach (Control card in _messages.Controls)
        {
            card.Width = width;

            var body = card.Controls.OfType<Label>().FirstOrDefault();
            if (body != null)
            {
                body.MaximumSize = new Size(Math.Max(180, width - 28), 0);
            }
        }
    }
}
