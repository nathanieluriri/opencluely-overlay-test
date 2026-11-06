using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OverlayPoc.Chat;
using static OverlayPoc.NativeMethods;

namespace OverlayPoc;

/// <summary>
/// Always-on-top overlay window. It is click-through and non-activating by
/// default; Ctrl+Shift+O (or the tray menu) unlocks it for typing and dragging.
/// UI construction lives in OverlayForm.Ui.cs, chat logic in OverlayForm.Chat.cs.
/// </summary>
internal sealed partial class OverlayForm : Form
{
    private const int HotkeyId = 1;
    private const int IdleLockMilliseconds = 20000;

    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _tray;
    private readonly Panel _header = new();
    private readonly Panel _content = new();
    private readonly FlowLayoutPanel _messages = new();
    private readonly TextBox _input = new();
    private readonly ListBox _history = new();
    private readonly List<ChatConversation> _conversations = new();

    private Button _interactiveButton;
    private Label _titleLabel;
    private ChatConversation _currentConversation;
    private IntPtr _lastForegroundWindow;
    private string _lastForegroundTitle = "Waiting for active app";
    private Size _restoredSize = new(760, 620);
    private bool _captureHidden;
    private bool _interactive;
    private bool _compact;
    private bool _maximized;
    private bool _suppressHistorySelection;

    public OverlayForm()
    {
        Program.Log("FORM ctor");

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Theme.Background;
        Opacity = 0.97;
        DoubleBuffered = true;

        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        Bounds = new Rectangle(
            screen.Width / 2 - _restoredSize.Width / 2,
            40,
            _restoredSize.Width,
            _restoredSize.Height);

        _status = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextSecondary,
            Font = Theme.BodySmall,
            Text = _lastForegroundTitle,
            TextAlign = ContentAlignment.MiddleLeft
        };

        BuildInterface();
        EnableDrag(_header);
        EnableDrag(_messages);

        _tray = CreateTrayIcon();
        _timer = CreateFocusTimer();

        Program.Log("FORM ctor done");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            var exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

            _captureHidden = SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);

            var hotkeyRegistered = RegisterHotKey(
                Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_O);

            if (_tray != null)
            {
                _tray.Text = _captureHidden
                    ? "Overlay POC (hidden from screen capture)"
                    : "Overlay POC";
            }

            Program.Log($"HANDLE created. captureHidden={_captureHidden} hotkey={hotkeyRegistered}");

            RaiseIfCovered();
            UpdateInteractionVisuals();
        }
        catch (Exception ex)
        {
            Program.Log("HANDLE error: " + ex);
            throw;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Program.Log("FORM shown");
        _tray.ShowBalloonTip(3000, "Focus chat",
            "Ctrl+Shift+O unlocks the overlay for typing and dragging.", ToolTipIcon.Info);
        UpdateFocusTarget();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show overlay", null, (_, _) =>
        {
            Show();
            RaiseIfCovered();
        });
        menu.Items.Add("Raise now", null, (_, _) => RaiseIfCovered());
        menu.Items.Add("Toggle interactive (Ctrl+Shift+O)", null, (_, _) => ToggleInteractive());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add("Exit", null, (_, _) => Close());

        return new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Overlay POC",
            ContextMenuStrip = menu
        };
    }

    private System.Windows.Forms.Timer CreateFocusTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            UpdateFocusTarget();
            RaiseIfCovered();

            if (_interactive && GetIdleMilliseconds() > IdleLockMilliseconds)
            {
                Program.Log("Idle timeout: locking overlay");
                ToggleInteractive();
            }
        };
        timer.Start();
        return timer;
    }

    /// <summary>
    /// Re-asserts topmost only when another window is actually above us.
    /// Calling SetWindowPos unconditionally on a timer is what made the
    /// earlier build flicker.
    /// </summary>
    private void RaiseIfCovered()
    {
        if (!IsHandleCreated || IsDisposed) return;

        // GW_HWNDPREV is the window directly above ours in the z-order.
        // NULL means nothing covers us, so there is nothing to fix.
        if (GetWindow(Handle, GW_HWNDPREV) == IntPtr.Zero) return;

        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Program.Log("Raised above covering window");
    }

    private void ToggleInteractive()
    {
        if (!IsHandleCreated || IsDisposed) return;

        _interactive = !_interactive;

        var exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        exStyle = _interactive
            ? exStyle & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)
            : exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        Program.Log($"Interactive={_interactive}");
        UpdateInteractionVisuals();

        if (_interactive)
        {
            // Unlocking is meant to take the user straight to typing. Focus has
            // to be applied after the activation message is processed.
            Activate();
            BeginInvoke(new Action(() =>
            {
                if (!_compact) _input.Focus();
            }));
        }
        else
        {
            if (_lastForegroundWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_lastForegroundWindow);
            }
            RaiseIfCovered();
        }
    }

    private static long GetIdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;

        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    private void UpdateFocusTarget()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == Handle) return;

        var title = new StringBuilder(512);
        if (GetWindowText(foreground, title, title.Capacity) <= 0) return;

        _lastForegroundWindow = foreground;
        _lastForegroundTitle = title.ToString();
        _status.Text = _lastForegroundTitle;
    }

    private void UpdateInteractionVisuals()
    {
        if (_interactiveButton is null) return;

        _interactiveButton.Text = _interactive ? "Lock" : "Interact";
        _interactiveButton.BackColor = _interactive
            ? Theme.ButtonBackgroundActive
            : Theme.ButtonBackground;
        _status.ForeColor = _interactive ? Theme.TextOnAccent : Theme.TextSecondary;

        if (_titleLabel != null)
        {
            _titleLabel.Text = _interactive
                ? "Typing mode  ·  keys go to this window  ·  Esc to lock"
                : "Focus chat  ·  Ctrl+Shift+O to type";
        }
    }

    private void EnableDrag(Control control)
    {
        control.MouseDown += BeginDrag;

        foreach (Control child in control.Controls)
        {
            // Buttons (window controls) and the button strip keep their clicks.
            if (child is Button or FlowLayoutPanel) continue;
            EnableDrag(child);
        }
    }

    private void BeginDrag(object sender, MouseEventArgs e)
    {
        if (!_interactive || e.Button != MouseButtons.Left) return;

        // Hand the drag to Windows itself. Tracking MouseMove by hand dropped
        // moves once capture moved from the clicked label to the form, which is
        // why dragging felt broken after the UI rewrite.
        ReleaseCapture();
        // Qualified because this class has its own SendMessage() for chat input.
        NativeMethods.SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private void ToggleCompact()
    {
        if (!_compact)
        {
            _restoredSize = Size;
            _content.Visible = false;
            Height = _header.Height;
            _compact = true;
            _maximized = false;
        }
        else
        {
            _content.Visible = true;
            Size = _restoredSize;
            _compact = false;
        }
    }

    private void ToggleMaximize()
    {
        if (_compact)
        {
            _content.Visible = true;
            Size = _restoredSize;
            _compact = false;
        }

        if (_maximized)
        {
            Size = _restoredSize;
            _maximized = false;
        }
        else
        {
            _restoredSize = Size;
            Bounds = Screen.FromControl(this).WorkingArea;
            _maximized = true;
        }
    }

    private static void OpenLog()
    {
        try
        {
            Process.Start("notepad.exe", Program.LogPath);
        }
        catch (Exception ex)
        {
            Program.Log("Open log failed: " + ex.Message);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleInteractive();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log("FORM closed");

        try { UnregisterHotKey(Handle, HotkeyId); } catch { }

        _timer?.Stop();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }

        base.OnFormClosed(e);
    }
}
