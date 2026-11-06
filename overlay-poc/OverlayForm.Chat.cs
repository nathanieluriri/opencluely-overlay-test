using System.Drawing;
using System.Windows.Forms;
using OverlayPoc.Chat;

namespace OverlayPoc;

internal sealed partial class OverlayForm
{
    private void SendMessage()
    {
        var text = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _input.Clear();

        if (_currentConversation is null)
            StartConversation();

        _currentConversation!.Messages.Add(new ChatMessage(false, text));
        AddBubbleToUI(text, isAssistant: false);

        // TODO: forward to LLM and add assistant response.
    }

    private void StartConversation()
    {
        var conversation = new ChatConversation($"Chat {_conversations.Count + 1}");
        _conversations.Add(conversation);

        _suppressHistorySelection = true;
        _history.Items.Add(conversation);
        _history.SelectedIndex = _history.Items.Count - 1;
        _suppressHistorySelection = false;

        SwitchToConversation(conversation);
    }

    private void OnHistorySelectionChanged(object sender, EventArgs e)
    {
        if (_suppressHistorySelection || _history.SelectedIndex < 0) return;

        var conversation = _history.SelectedItem as ChatConversation;
        if (conversation is not null)
            SwitchToConversation(conversation);
    }

    private void SwitchToConversation(ChatConversation conversation)
    {
        _currentConversation = conversation;
        _messages.Controls.Clear();

        foreach (var msg in conversation.Messages)
            AddBubbleToUI(msg.Text, msg.IsAssistant);
    }

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            SendMessage();
        }
    }

    private void OnMessagesSizeChanged(object sender, EventArgs e)
    {
        // Auto-scroll to the bottom when new bubbles are added.
        if (_messages.Controls.Count > 0)
            _messages.ScrollControlIntoView(_messages.Controls[^1]);
    }

    private void AddBubbleToUI(string text, bool isAssistant)
    {
        var bubble = new Panel
        {
            AutoSize = true,
            MaximumSize = new Size(_messages.ClientSize.Width - 36, 0),
            Padding = new Padding(12, 8, 12, 8),
            BackColor = isAssistant ? Theme.SurfaceRaised : Theme.UserBubble,
            Margin = new Padding(0, 0, 0, 8)
        };

        var label = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(bubble.MaximumSize.Width - 24, 0),
            Text = text,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            BackColor = Color.Transparent
        };

        var accent = new Panel
        {
            Width = 3,
            Height = label.Height,
            BackColor = isAssistant ? Theme.AssistantAccent : Theme.UserAccent,
            Location = new Point(0, 8),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };

        // Wrap text at the panel width.
        label.SizeChanged += (_, _) =>
        {
            accent.Height = label.Height;
        };

        bubble.Controls.Add(accent);
        bubble.Controls.Add(label);
        label.Location = new Point(10, 8);

        _messages.Controls.Add(bubble);
        _messages.ScrollControlIntoView(bubble);
    }
}
