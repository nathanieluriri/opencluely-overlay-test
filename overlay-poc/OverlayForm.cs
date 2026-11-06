using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OverlayPoc.Chat;
using static OverlayPoc.NativeMethods;

namespace OverlayPoc;

/// <summary>
/// Always-on-top overlay window. It is click-through and non-activating at
/// all times. Ctrl+Shift+O toggles global keyboard capture — keystrokes flow
/// into the chat input without the overlay ever stealing focus.
/// </summary>
internal sealed partial class OverlayForm : Form
{
    private const int HotkeyId = 1;

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
    private ChatConversation _currentConversation;
    private Label _titleLabel;
    private IntPtr _lastForegroundWindow;
    private string _lastForegroundTitle = "Waiting for active app";
    private Size _restoredSize = new(760, 620);
    private bool _captureHidden;
    private bool _compact;
    private bool _maximized;
    private bool _suppressHistorySelection;

    // ── Keyboard capture (no focus change, ever) ───────────────────────

    private bool _keyboardCaptureEnabled;
    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _hookProc;

    public OverlayForm()
    {
        Program.Log("FORM ctor");

        _hookProc = KeyboardProc;

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

    // ── Window lifecycle ──────────────────────────────────────────────

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
            "Ctrl+Shift+O captures typing into the chat without changing focus.",
            ToolTipIcon.Info);
        UpdateFocusTarget();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log("FORM closed");

        ReleaseHook();

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

    // ── Tray icon ─────────────────────────────────────────────────────

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show overlay", null, (_, _) =>
        {
            Show();
            RaiseIfCovered();
        });
        menu.Items.Add("Raise now", null, (_, _) => RaiseIfCovered());
        menu.Items.Add("Toggle keyboard capture (Ctrl+Shift+O)", null, (_, _) => ToggleInteractive());
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

    // ── Focus timer ───────────────────────────────────────────────────

    private System.Windows.Forms.Timer CreateFocusTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            UpdateFocusTarget();
            RaiseIfCovered();
        };
        timer.Start();
        return timer;
    }

    // ── Global keyboard hook ──────────────────────────────────────────

    private void SetHook()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
            IntPtr.Zero, 0);

        if (_hookId == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Program.Log($"HOOK set failed: error {err}");
        }
        else
        {
            Program.Log("HOOK installed");
        }
    }

    private void ReleaseHook()
    {
        if (_hookId == IntPtr.Zero) return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        Program.Log("HOOK released");
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_keyboardCaptureEnabled)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Never suppress modifier keys — they must reach the system so
        // combos (including our own Ctrl+Shift+O) continue to work.
        if (kb.vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT
            or VK_CONTROL or VK_LCONTROL or VK_RCONTROL
            or VK_MENU or VK_LMENU or VK_RMENU
            or VK_LWIN or VK_RWIN
            or VK_CAPITAL or VK_NUMLOCK or VK_SCROLL)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // Detect Ctrl+Shift+O in the hook so it works even while capturing.
        if (kb.vkCode == VK_O)
        {
            var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
            var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            if (ctrl && shift && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                // Let the hotkey through so the RegisterHotKey path also fires.
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
        }

        if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
        {
            switch (kb.vkCode)
            {
                case VK_RETURN:
                    BeginInvoke(new Action(() => SendMessage()));
                    return (IntPtr)1;

                case VK_BACK:
                    BeginInvoke(new Action(() => HandleBackspace()));
                    return (IntPtr)1;

                case VK_ESCAPE:
                    BeginInvoke(new Action(() => ToggleInteractive()));
                    return (IntPtr)1;

                case VK_TAB:
                    BeginInvoke(new Action(() => AppendToInput("\t")));
                    return (IntPtr)1;

                default:
                {
                    var text = VkToText(kb.vkCode, kb.scanCode, kb.flags);
                    if (text.Length > 0)
                    {
                        BeginInvoke(new Action(() => AppendToInput(text)));
                    }

                    // Suppress all character-producing keys (and navigation
                    // keys that we don't explicitly handle) so the target
                    // app never sees them.
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Translate a virtual-key / scan-code pair into a string using the
    /// current keyboard layout. Returns an empty string for non-printable keys.
    /// </summary>
    private static string VkToText(uint vkCode, uint scanCode, uint flags)
    {
        // Build a keyboard-state snapshot.
        var keyState = new byte[256];
        GetKeyboardState(keyState);

        // ToUnicode may alter the keyboard state for dead keys; work on a copy.
        var buf = new StringBuilder(8);
        var result = ToUnicode(vkCode, scanCode, keyState, buf, buf.Capacity, 0);

        if (result > 0)
            return buf.ToString(0, result);

        // Dead key — the character is pending, not produced yet.
        if (result < 0)
            return "";

        // No translation available; handle a few explicit cases.
        return vkCode switch
        {
            VK_SPACE => " ",
            _ => ""
        };
    }

    private void AppendToInput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        _input.Text += text;
        _input.SelectionStart = _input.Text.Length;
        _input.ScrollToCaret();
    }

    private void HandleBackspace()
    {
        if (_input.Text.Length > 0)
        {
            _input.Text = _input.Text[..^1];
            _input.SelectionStart = _input.Text.Length;
            _input.ScrollToCaret();
        }
    }

    // ── Toggle keyboard capture ───────────────────────────────────────

    private void ToggleInteractive()
    {
        if (!IsHandleCreated || IsDisposed) return;

        _keyboardCaptureEnabled = !_keyboardCaptureEnabled;

        if (_keyboardCaptureEnabled)
        {
            SetHook();
            Program.Log("Keyboard capture ENABLED");
        }
        else
        {
            ReleaseHook();
            Program.Log("Keyboard capture DISABLED");
        }

        // The window NEVER changes style — it stays transparent and
        // click-through regardless of capture state.
        UpdateInteractionVisuals();
    }

    // ── Window management ─────────────────────────────────────────────

    private void RaiseIfCovered()
    {
        if (!IsHandleCreated || IsDisposed) return;

        if (GetWindow(Handle, GW_HWNDPREV) == IntPtr.Zero) return;

        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Program.Log("Raised above covering window");
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

        _interactiveButton.Text = _keyboardCaptureEnabled ? "Capturing" : "Capture";
        _interactiveButton.BackColor = _keyboardCaptureEnabled
            ? Theme.ButtonBackgroundActive
            : Theme.ButtonBackground;
        _status.ForeColor = _keyboardCaptureEnabled ? Theme.TextOnAccent : Theme.TextSecondary;

        if (_titleLabel != null)
        {
            _titleLabel.Text = _keyboardCaptureEnabled
                ? "Capturing  ·  typing goes to chat  ·  Ctrl+Shift+O or Esc to stop"
                : "Focus chat  ·  Ctrl+Shift+O to capture typing";
        }
    }

    // ── Drag support ──────────────────────────────────────────────────

    private void EnableDrag(Control control)
    {
        control.MouseDown += BeginDrag;

        foreach (Control child in control.Controls)
        {
            if (child is Button or FlowLayoutPanel) continue;
            EnableDrag(child);
        }
    }

    private void BeginDrag(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        ReleaseCapture();
        NativeMethods.SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // ── Window sizing ─────────────────────────────────────────────────

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

    // ── WndProc (global hotkey) ───────────────────────────────────────

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleInteractive();
            return;
        }

        base.WndProc(ref m);
    }
}
