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

        _ = SendMessageAsync(text);
    }

    internal async Task SendMessageAsync(string text)
    {
        if (!_config.HasApiKey)
        {
            AddBubbleToUI("No API key configured. Set OPENROUTER_API_KEY environment variable or create\r\n" +
                          $"{LlmConfigLoader.ConfigPath} with an \"ApiKey\" field.", isAssistant: true);
            return;
        }

        if (_currentConversation is null)
            StartConversation();

        // Create a placeholder assistant bubble that we will update as tokens stream in.
        var bubble = AddStreamingBubble();

        // Cancel any in-flight request.
        _thinkingCts?.Cancel();
        _thinkingCts = new CancellationTokenSource();
        var ct = _thinkingCts.Token;

        _hud?.SetThinking();

        var fullText = "";
        try
        {
            var conversation = _currentConversation!;

            await foreach (var delta in OpenRouterClient.StreamAsync(
                               conversation.Messages, _config, ct))
            {
                fullText += delta;

                // Update bubble on UI thread.
                var current = fullText;
                BeginInvoke(new Action(() =>
                {
                    if (bubble.IsDisposed) return;
                    var label = bubble.Controls.OfType<Label>().FirstOrDefault();
                    if (label != null)
                        label.Text = current;
                    _hud?.SetThinking(current);
                }));
            }
        }
        catch (OperationCanceledException)
        {
            fullText += "\r\n\r\n[cancelled]";
        }
        catch (Exception ex)
        {
            fullText = $"Error: {ex.Message}";
            Program.Log("LLM error: " + ex);
        }

        // Final update.
        var finalText = fullText;
        BeginInvoke(new Action(() =>
        {
            if (!bubble.IsDisposed)
            {
                var label = bubble.Controls.OfType<Label>().FirstOrDefault();
                if (label != null)
                    label.Text = finalText;
            }

            // Add the assistant message to the conversation.
            if (_currentConversation != null && !string.IsNullOrWhiteSpace(finalText))
                _currentConversation.Messages.Add(new ChatMessage(true, finalText));

            _hud?.SetResponse(finalText);
        }));
    }

    /// <summary>Creates an empty assistant bubble and returns its panel for streaming updates.</summary>
    private Panel AddStreamingBubble()
    {
        var bubble = new Panel
        {
            AutoSize = true,
            MaximumSize = new Size(_messages.ClientSize.Width - 36, 0),
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Theme.SurfaceRaised,
            Margin = new Padding(0, 0, 0, 8)
        };

        var label = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(bubble.MaximumSize.Width - 24, 0),
            Text = "Thinking…",
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            BackColor = Color.Transparent
        };

        var accent = new Panel
        {
            Width = 3,
            Height = label.Height,
            BackColor = Theme.AssistantAccent,
            Location = new Point(0, 8),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };

        label.SizeChanged += (_, _) =>
        {
            if (!accent.IsDisposed)
                accent.Height = label.Height;
        };

        bubble.Controls.Add(accent);
        bubble.Controls.Add(label);
        label.Location = new Point(10, 8);

        _messages.Controls.Add(bubble);
        _messages.ScrollControlIntoView(bubble);

        return bubble;
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
