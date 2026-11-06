namespace OverlayPoc.Chat;

internal sealed record ChatMessage(bool IsAssistant, string Text)
{
    /// <summary>Whether the message body is collapsed to just its heading.</summary>
    public bool Collapsed { get; set; }
}
