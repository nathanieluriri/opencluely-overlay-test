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
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _tray;
    private readonly Panel _header = new();
    private readonly Panel _content = new();
    private readonly FlowLayoutPanel _messages = new();
    private readonly TextBox _input = new();
    private readonly ListBox _history = new();
    private readonly List<ChatConversation> _conversations = new();
    private readonly LlmConfig _config;
    private readonly HudForm _hud;

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
    private CancellationTokenSource _thinkingCts;

    // ── Keyboard capture (no focus change, ever) ───────────────────────

    private bool _keyboardCaptureEnabled;
    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _hookProc;

    public OverlayForm()
    {
        Program.Log("FORM ctor");

        _hookProc = KeyboardProc;
        _config = LlmConfigLoader.Load();
        Program.Log($"Config loaded. model={_config.Model} hasKey={_config.HasApiKey}");

        _hud = new HudForm();

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

            SetHook();

            if (_tray != null)
            {
                _tray.Text = _captureHidden
                    ? "Overlay POC (hidden from screen capture)"
                    : "Overlay POC";
            }

            Program.Log($"HANDLE created. captureHidden={_captureHidden}");

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
            "Ctrl+Shift+O captures typing  ·  Ctrl+Shift+I toggles HUD  ·  Ctrl+Shift+V asks selection",
            ToolTipIcon.Info);
        UpdateFocusTarget();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log("FORM closed");

        ReleaseHook();

        _timer?.Stop();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }

        if (_hud != null && !_hud.IsDisposed)
        {
            _hud.Close();
            _hud.Dispose();
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
        menu.Items.Add("Toggle HUD (Ctrl+Shift+I)", null, (_, _) => ToggleHud());
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
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // ── Ctrl+Shift+O toggles capture (always active) ──────────
        if (kb.vkCode == VK_O
            && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
            var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            if (ctrl && shift)
            {
                BeginInvoke(new Action(() => ToggleInteractive()));
                return (IntPtr)1;
            }
        }

        // ── Ctrl+Shift+I toggles HUD visibility ────────────────────
        if (kb.vkCode == VK_I
            && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
            var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            if (ctrl && shift)
            {
                BeginInvoke(new Action(() => ToggleHud()));
                return (IntPtr)1;
            }
        }

        // ── Ctrl+Shift+V copies selection & asks ───────────────────
        if (kb.vkCode == VK_V
            && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
            var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            if (ctrl && shift)
            {
                BeginInvoke(new Action(() => _ = AskSelectionAsync()));
                return (IntPtr)1;
            }
        }

        if (!_keyboardCaptureEnabled)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        // Never suppress modifier keys.
        if (kb.vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT
            or VK_CONTROL or VK_LCONTROL or VK_RCONTROL
            or VK_MENU or VK_LMENU or VK_RMENU
            or VK_LWIN or VK_RWIN
            or VK_CAPITAL or VK_NUMLOCK or VK_SCROLL)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
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
                    var text = VkToText(kb.vkCode, kb.scanCode);
                    if (text.Length > 0)
                    {
                        BeginInvoke(new Action(() => AppendToInput(text)));
                    }

                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static string VkToText(uint vkCode, uint scanCode)
    {
        var keyState = new byte[256];
        GetKeyboardState(keyState);

        // Clear Ctrl states so the Ctrl+Shift+O toggle combo doesn't
        // corrupt the first characters typed afterwards.
        keyState[VK_CONTROL] = 0;
        keyState[VK_LCONTROL] = 0;
        keyState[VK_RCONTROL] = 0;

        var buf = new StringBuilder(8);
        var result = ToUnicode(vkCode, scanCode, keyState, buf, buf.Capacity, 0);

        if (result > 0)
            return buf.ToString(0, result);

        if (result < 0)
            return "";

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

        Program.Log(_keyboardCaptureEnabled
            ? "Keyboard capture ENABLED"
            : "Keyboard capture DISABLED");

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
                : "Focus chat  ·  Ctrl+Shift+O type  ·  Ctrl+Shift+I HUD  ·  Ctrl+Shift+V ask selection";
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

    // ── HUD toggle (Ctrl+Shift+I) ─────────────────────────────────────

    private void ToggleHud()
    {
        if (_hud is null || _hud.IsDisposed) return;

        if (_hud.Visible)
        {
            _hud.Hide();
            Show();
            RaiseIfCovered();
            Program.Log("HUD hidden, overlay shown");
        }
        else
        {
            Hide();
            _hud.Show();
            Program.Log("HUD shown, overlay hidden");
        }
    }

    // ── Ask selection (Ctrl+Shift+V) ───────────────────────────────────

    private async Task AskSelectionAsync()
    {
        try
        {
            // Release Shift first so the foreground app sees Ctrl+C, not
            // Ctrl+Shift+C (which triggers browser element inspector).
            keybd_event((byte)VK_SHIFT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            await Task.Delay(10);

            keybd_event((byte)VK_CONTROL, 0, 0, IntPtr.Zero);
            keybd_event((byte)VK_C, 0, 0, IntPtr.Zero);
            keybd_event((byte)VK_C, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

            await Task.Delay(80);

            string text = null;
            try
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                Program.Log("Clipboard read failed: " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Program.Log("AskSelection: clipboard was empty");
                return;
            }

            text = text.Trim();
            Program.Log($"AskSelection: got {text.Length} chars");

            // Ensure overlay is visible and expanded.
            if (_compact)
                ToggleCompact();
            if (_hud is { Visible: true })
            {
                _hud.Hide();
                Show();
                RaiseIfCovered();
            }
            if (!Visible)
            {
                Show();
                RaiseIfCovered();
            }

            // Paste into input so the user can see what was captured.
            _input.Text = text;

            // Add to conversation and send.
            if (_currentConversation is null)
                StartConversation();
            _currentConversation!.Messages.Add(new ChatMessage(false, text));
            AddBubbleToUI(text, isAssistant: false);

            await SendMessageAsync(text);
        }
        catch (Exception ex)
        {
            Program.Log("AskSelection error: " + ex);
        }
    }

}
