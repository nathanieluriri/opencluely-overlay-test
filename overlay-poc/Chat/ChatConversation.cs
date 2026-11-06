namespace OverlayPoc.Chat;

internal sealed class ChatConversation
{
    public ChatConversation(string title) => Title = title;

    public string Title { get; set; }
    public List<ChatMessage> Messages { get; } = new();

    public override string ToString() => Title;
}
