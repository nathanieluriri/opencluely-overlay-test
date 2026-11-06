using System.Drawing;
using System.Windows.Forms;
using OverlayPoc.Chat;

namespace OverlayPoc;

internal sealed partial class OverlayForm
{
    private void BuildInterface()
    {
        BuildHeader();
        BuildContent();

        Controls.Add(_content);
        Controls.Add(_header);

        _conversations.Add(new ChatConversation("New conversation"));
        _history.Items.Add(_conversations[0]);
        _history.SelectedIndex = 0;
    }

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 54;
        _header.BackColor = Theme.SurfaceRaised;
        _header.Padding = new Padding(18, 0, 12, 0);

        var title = new Label
        {
            AutoSize = true,
            Text = "Focus chat  ·  Ctrl+Shift+O to type",
            ForeColor = Color.White,
            Font = Theme.Title,
            Location = new Point(18, 7)
        };

        var focusCaption = new Label
        {
            AutoSize = true,
            Text = "FOCUSED APP",
            ForeColor = Color.FromArgb(112, 121, 143),
            Font = Theme.LabelBold,
            Location = new Point(18, 30)
        };

        _status.Location = new Point(105, 29);
        _status.MaximumSize = new Size(340, 0);
        _status.AutoEllipsis = true;

        _header.Controls.Add(title);
        _header.Controls.Add(focusCaption);
        _header.Controls.Add(_status);

        var windowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 238,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.SurfaceRaised,
            Padding = new Padding(0, 9, 0, 0)
        };

        var hideButton = MakeButton("×", 34);
        hideButton.Click += (_, _) => Hide();

        var maximizeButton = MakeButton("□", 34);
        maximizeButton.Click += (_, _) => ToggleMaximize();

        var minimizeButton = MakeButton("−", 34);
        minimizeButton.Click += (_, _) => ToggleCompact();

        _interactiveButton = MakeButton("Interact", 78);
        _interactiveButton.Click += (_, _) => ToggleInteractive();

        windowButtons.Controls.Add(hideButton);
        windowButtons.Controls.Add(maximizeButton);
        windowButtons.Controls.Add(minimizeButton);
        windowButtons.Controls.Add(_interactiveButton);
        _header.Controls.Add(windowButtons);
    }

    private void BuildContent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildSidebar(), 0, 0);
        layout.Controls.Add(BuildChatPanel(), 1, 0);

        _content.Dock = DockStyle.Fill;
        _content.Controls.Add(layout);
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(12)
        };

        var historyTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = "CONVERSATIONS",
            ForeColor = Theme.TextMuted,
            Font = Theme.CaptionBold,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var newConversation = MakeButton("+  New chat", 34);
        newConversation.Dock = DockStyle.Top;
        newConversation.Click += (_, _) => StartConversation();

        _history.Dock = DockStyle.Fill;
        _history.BorderStyle = BorderStyle.None;
        _history.BackColor = Theme.Surface;
        _history.ForeColor = Theme.TextPrimary;
        _history.Font = Theme.BodySmall;
        _history.IntegralHeight = false;
        _history.SelectedIndexChanged += OnHistorySelectionChanged;

        sidebar.Controls.Add(_history);
        sidebar.Controls.Add(newConversation);
        sidebar.Controls.Add(historyTitle);
        return sidebar;
    }

    private Control BuildChatPanel()
    {
        var chat = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(18, 14, 18, 12)
        };

        _messages.Dock = DockStyle.Fill;
        _messages.FlowDirection = FlowDirection.TopDown;
        _messages.WrapContents = false;
        _messages.AutoScroll = true;
        _messages.BackColor = Theme.Background;
        _messages.Padding = new Padding(2);
        _messages.SizeChanged += (_, _) => ResizeMessageCards();

        chat.Controls.Add(_messages);
        chat.Controls.Add(BuildComposer());
        return chat;
    }

    private Control BuildComposer()
    {
        var composer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 112,
            BackColor = Theme.InputBackground,
            Padding = new Padding(12)
        };

        _input.Multiline = true;
        _input.AcceptsReturn = true;
        _input.ScrollBars = ScrollBars.Vertical;
        _input.Dock = DockStyle.Fill;
        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = Theme.InputBackground;
        _input.ForeColor = Theme.TextPrimary;
        _input.Font = Theme.Body;
        _input.PlaceholderText = "Ask anything about your current task...";
        _input.KeyDown += OnComposerKeyDown;

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            BackColor = Theme.InputBackground
        };

        var hint = new Label
        {
            AutoSize = true,
            Text = "Enter to send · Shift+Enter for a new line",
            ForeColor = Theme.TextHint,
            Font = Theme.Caption,
            Location = new Point(0, 8)
        };

        var sendButton = MakeButton("Send", 76);
        sendButton.Dock = DockStyle.Right;
        sendButton.Click += (_, _) => SendMessage();

        footer.Controls.Add(hint);
        footer.Controls.Add(sendButton);

        composer.Controls.Add(_input);
        composer.Controls.Add(footer);
        return composer;
    }

    private static Button MakeButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.ButtonBackground,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Button,
            Margin = new Padding(3, 0, 3, 0),
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }
}
