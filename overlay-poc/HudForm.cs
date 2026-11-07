using System.Drawing;
using System.Windows.Forms;
using static OverlayPoc.NativeMethods;

namespace OverlayPoc;

/// <summary>
/// Minimal heads-up strip: a single line of slightly transparent green text
/// fixed near the top of the screen. It is always-on-top, click-through,
/// never activates, and is excluded from screen capture.
/// When typing capture is enabled it also shows a live typing preview.
/// </summary>
internal sealed class HudForm : Form
{
    private readonly Label _text = new();
    private readonly Label _typing = new();
    private readonly Panel _typingBar = new();
    private bool _typingVisible;

    public HudForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        DoubleBuffered = true;

        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var width = Math.Min(screen.Width - 160, 1400);
        Bounds = new Rectangle((screen.Width - width) / 2, 30, width, 40);

        _text.Dock = DockStyle.Fill;
        _text.AutoSize = false;
        _text.AutoEllipsis = true;
        _text.TextAlign = ContentAlignment.MiddleCenter;
        _text.ForeColor = Color.FromArgb(205, 150, 255, 150);
        _text.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        _text.Text = "Ready";
        Controls.Add(_text);

        // Typing preview bar — hidden until capture mode is on.
        _typingBar.BackColor = Color.Black;
        _typingBar.Height = 36;
        _typingBar.Visible = false;

        _typing.Dock = DockStyle.Fill;
        _typing.AutoSize = false;
        _typing.AutoEllipsis = true;
        _typing.TextAlign = ContentAlignment.MiddleLeft;
        _typing.ForeColor = Color.FromArgb(220, 255, 255, 255);
        _typing.Font = new Font("Segoe UI", 11f, FontStyle.Regular);
        _typing.Padding = new Padding(12, 0, 12, 0);
        _typing.Text = "";
        _typingBar.Controls.Add(_typing);

        Controls.Add(_typingBar);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
        }
        catch (Exception ex)
        {
            Program.Log("HUD handle error: " + ex);
        }
    }

    public void SetMessage(string text) =>
        _text.Text = string.IsNullOrWhiteSpace(text) ? "Ready" : HudText.Summarize(text);

    public void SetThinking(string progress = "") =>
        _text.Text = string.IsNullOrWhiteSpace(progress)
            ? "Thinking…"
            : "Thinking… " + HudText.Summarize(progress);

    public void SetResponse(string text) =>
        _text.Text = HudText.Summarize(text);

    /// <summary>Show or hide the typing preview bar.</summary>
    public void SetTypingVisible(bool visible)
    {
        if (_typingVisible == visible) return;
        _typingVisible = visible;
        _typingBar.Visible = visible;

        // Resize the form to fit the typing bar below the main text.
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var width = Math.Min(screen.Width - 160, 1400);
        var height = visible ? 80 : 40;
        Bounds = new Rectangle((screen.Width - width) / 2, 30, width, height);

        _typingBar.Location = new Point(0, 40);
        _typingBar.Width = width;
    }

    /// <summary>Update the live typing preview text, showing the tail end
    /// so the most recent characters are always visible.</summary>
    public void SetTypingText(string text)
    {
        if (!_typingVisible) return;
        if (string.IsNullOrEmpty(text))
        {
            _typing.Text = " ";
            return;
        }

        // Show the last N characters so the user sees what they're typing now.
        const int maxChars = 120;
        var display = text.Length > maxChars
            ? "…" + text[^maxChars..]
            : text;
        _typing.Text = display;
    }

    /// <summary>Clear the typing preview and hide the bar.</summary>
    public void ClearTyping()
    {
        _typing.Text = "";
        SetTypingVisible(false);
    }
}

/// <summary>Cheap one-line summarizer for the HUD (max 20 words).</summary>
internal static class HudText
{
    private const int MaxWords = 20;

    public static string Summarize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Ready";

        var raw = text.Trim();

        // Prefer a complete first sentence when it is short enough.
        foreach (var end in new[] { '.', '!', '?', '\n' })
        {
            var index = raw.IndexOf(end);
            if (index > 0 && index <= 180)
            {
                var sentence = raw[..(index + 1)].Trim();
                if (WordCount(sentence) <= MaxWords)
                    return sentence;
            }
        }

        var words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "Ready";

        var joined = string.Join(" ", words.Take(MaxWords));
        return words.Length > MaxWords ? joined + "…" : joined;
    }

    private static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}