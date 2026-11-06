using System.Drawing;

namespace OverlayPoc;

/// <summary>Single place for the overlay's colours and fonts.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(18, 19, 24);
    public static readonly Color Surface = Color.FromArgb(23, 24, 30);
    public static readonly Color SurfaceRaised = Color.FromArgb(27, 29, 36);
    public static readonly Color InputBackground = Color.FromArgb(34, 36, 44);
    public static readonly Color ButtonBackground = Color.FromArgb(45, 48, 58);
    public static readonly Color ButtonBackgroundActive = Color.FromArgb(91, 73, 164);
    public static readonly Color UserBubble = Color.FromArgb(34, 58, 91);

    public static readonly Color TextPrimary = Color.FromArgb(237, 238, 242);
    public static readonly Color TextSecondary = Color.FromArgb(165, 171, 187);
    public static readonly Color TextMuted = Color.FromArgb(130, 137, 155);
    public static readonly Color TextHint = Color.FromArgb(133, 139, 154);
    public static readonly Color TextOnAccent = Color.FromArgb(205, 190, 255);
    public static readonly Color AssistantAccent = Color.FromArgb(171, 155, 255);
    public static readonly Color UserAccent = Color.FromArgb(148, 194, 255);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font BodySmall = new("Segoe UI", 9f);
    public static readonly Font Caption = new("Segoe UI", 8f);
    public static readonly Font CaptionBold = new("Segoe UI", 8f, FontStyle.Bold);
    public static readonly Font LabelBold = new("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font Button = new("Segoe UI", 8.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 12f);
    public static readonly Font Glyph = new("Segoe UI", 10f);
}
