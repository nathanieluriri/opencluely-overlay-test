using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OverlayPoc.Chat;
using OverlayPoc.Models;
using OverlayPoc.Services;
using static OverlayPoc.NativeMethods;

namespace OverlayPoc;

/// <summary>
/// Always-on-top overlay window. It is click-through and non-activating at
/// all times. Action hotkeys (Ctrl+Shift+O / I / Y / X / S / W) are always active
/// globally; raw typing is captured only while keyboard capture is enabled.
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

    // ── Service layer ─────────────────────────────────────────────────
    private readonly UsageTracker _usageTracker;
    private readonly ModelCatalog _modelCatalog;
    private readonly OpenRouterService _aiService;
    private readonly ToolRegistry _toolRegistry;

    private Button _interactiveButton;
    private ChatConversation _currentConversation;
    private Label _titleLabel;
    private string _lastForegroundTitle = "Waiting for active app";
    private Size _restoredSize = new(760, 620);
    private bool _captureHidden;
    private bool _compact;
    private bool _maximized;
    private bool _suppressHistorySelection;
    private CancellationTokenSource _thinkingCts;
    private bool _touchMode; // Ctrl+Shift+Y — off by default (click-through)
    private DashboardForm _dashboard;

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

        // Initialize service layer
        _usageTracker = new UsageTracker { Enabled = _config.EnableUsageTracking };
        _modelCatalog = new ModelCatalog(_config.ApiKey, _config.BaseUrl);
        _toolRegistry = new ToolRegistry();
        _aiService = new OpenRouterService(_config, _usageTracker, _modelCatalog, _toolRegistry);

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
        // No balloon tip — it can steal focus on some Windows versions.
        UpdateFocusTarget();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log("FORM closed");

        ReleaseHook();

        _thinkingCts?.Cancel();
        _thinkingCts?.Dispose();

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
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            RaiseIfCovered();
        });
        menu.Items.Add("Raise now", null, (_, _) => RaiseIfCovered());
        menu.Items.Add("Toggle keyboard capture (Ctrl+Shift+O)", null, (_, _) => ToggleInteractive());
        menu.Items.Add("Toggle HUD (Ctrl+Shift+I)", null, (_, _) => ToggleHud());
        menu.Items.Add("Toggle touch mode (Ctrl+Shift+Y)", null, (_, _) => ToggleTouchMode());
        menu.Items.Add("Screenshot & ask (Ctrl+Shift+S)", null, (_, _) => _ = AskScreenshotAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ask with web search", null, (_, _) => AskWithSearch());
        menu.Items.Add("Dashboard…", null, (_, _) => ShowDashboard());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
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

        // Use the actual module handle for WH_KEYBOARD_LL compatibility
        var hMod = GetModuleHandle(null);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);

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
        var isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;

        // Only act on key-down so hotkeys fire once per press.
        if (!isKeyDown)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
        var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

        // ── Global hotkeys: ALWAYS active, regardless of capture mode ──
        // These combos are swallowed so they never reach the foreground app.

        // Ctrl+Shift+O toggles capture on/off.
        if (kb.vkCode == VK_O && ctrl && shift)
        {
            BeginInvoke(new Action(() => ToggleInteractive()));
            return (IntPtr)1;
        }

        // Ctrl+Shift+I toggles HUD visibility.
        if (kb.vkCode == VK_I && ctrl && shift)
        {
            BeginInvoke(new Action(() => ToggleHud()));
            return (IntPtr)1;
        }

        // Ctrl+Shift+Y toggles touch/interactive mode.
        if (kb.vkCode == VK_Y && ctrl && shift)
        {
            BeginInvoke(new Action(() => ToggleTouchMode()));
            return (IntPtr)1;
        }

        // Ctrl+Shift+X copies selection & asks.
        if (kb.vkCode == VK_X && ctrl && shift)
        {
            BeginInvoke(new Action(() => _ = AskSelectionAsync()));
            return (IntPtr)1;
        }

        // Ctrl+Shift+S captures screen & asks vision model.
        if (kb.vkCode == VK_S && ctrl && shift)
        {
            BeginInvoke(new Action(() => _ = AskScreenshotAsync()));
            return (IntPtr)1;
        }

        // Ctrl+Shift+W asks with web search.
        if (kb.vkCode == VK_W && ctrl && shift)
        {
            BeginInvoke(new Action(() => AskWithSearch()));
            return (IntPtr)1;
        }

        // ── Typing capture: ONLY when capture mode is enabled ──
        // When capture is off, every other keystroke passes through normally.
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

        // Live preview on the HUD typing bar.
        _hud?.SetTypingText(_input.Text);
    }

    private void HandleBackspace()
    {
        if (_input.Text.Length > 0)
        {
            // Remove the last UTF-16 code unit, but if it's a low surrogate,
            // also remove the preceding high surrogate to keep pairs intact.
            var text = _input.Text;
            var removeCount = 1;
            if (text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]))
                removeCount = 2;

            _input.Text = text[..^removeCount];
            _input.SelectionStart = _input.Text.Length;
            _input.ScrollToCaret();
            _hud?.SetTypingText(_input.Text);
        }
    }

    // ── Toggle keyboard capture ───────────────────────────────────────

    internal void ToggleInteractive()
    {
        if (!IsHandleCreated || IsDisposed) return;

        _keyboardCaptureEnabled = !_keyboardCaptureEnabled;

        Program.Log(_keyboardCaptureEnabled
            ? "Keyboard capture ENABLED"
            : "Keyboard capture DISABLED");

        // Show/hide the typing preview bar on the HUD.
        _hud?.SetTypingVisible(_keyboardCaptureEnabled);
        if (!_keyboardCaptureEnabled)
            _hud?.ClearTyping();

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
                : "Hotkeys on  ·  Ctrl+Shift+O to type  ·  Ctrl+Shift+I HUD  ·  Ctrl+Shift+Y touch  ·  Ctrl+Shift+X copy/ask  ·  Ctrl+Shift+S screenshot  ·  Ctrl+Shift+W research";
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
        if (!_touchMode) return; // click-through in normal mode, so no drag anyway

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

    private SettingsForm _settingsForm;

    private void ShowSettings()
    {
        // Singleton — don't stack settings windows.
        if (_settingsForm is { IsDisposed: false })
        {
            ShowWindow(_settingsForm.Handle, SW_SHOWNOACTIVATE);
            return;
        }

        _settingsForm = new SettingsForm(_config);
        _settingsForm.FormClosed += (_, args) =>
        {
            if (_settingsForm.Saved)
            {
                Program.Log($"Settings applied. model={_config.Model} vision={_config.VisionModel ?? "(same)"} hasKey={_config.HasApiKey}");
                // Rebuild service with new config
                // (The service holds a reference to the same _config object, so it sees updates)
            }
            _settingsForm.Dispose();
            _settingsForm = null;
        };
        _settingsForm.Show();
    }

    private void ShowDashboard()
    {
        if (_dashboard is { IsDisposed: false })
        {
            _dashboard.BringToFront();
            _dashboard.Focus();
            return;
        }

        _dashboard = new DashboardForm(_usageTracker, _modelCatalog);
        _dashboard.FormClosed += (_, _) => _dashboard = null;
        _dashboard.Show();
    }

    /// <summary>Force the next message to use web search/fetch tools, then keep them on.</summary>
    internal void AskWithSearch()
    {
        if (_input.Text.Trim().Length == 0) return;

        _config.EnableWebSearch = true;
        _config.EnableWebFetch = true;
        SendMessage();
    }

    // ── HUD toggle (Ctrl+Shift+I) ─────────────────────────────────────

    internal void ToggleHud()
    {
        if (_hud is null || _hud.IsDisposed) return;

        if (_hud.Visible)
        {
            ShowWindow(_hud.Handle, SW_HIDE);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            RaiseIfCovered();
            Program.Log("HUD hidden, overlay shown");
        }
        else
        {
            ShowWindow(Handle, SW_HIDE);
            ShowWindow(_hud.Handle, SW_SHOWNOACTIVATE);
            Program.Log("HUD shown, overlay hidden");
        }
    }

    // ── Touch mode toggle (Ctrl+Shift+Y) ───────────────────────────────

    internal void ToggleTouchMode()
    {
        if (!IsHandleCreated || IsDisposed) return;

        _touchMode = !_touchMode;

        var exStyle = (int)(long)GetWindowLongPtr(Handle, GWL_EXSTYLE);

        if (_touchMode)
        {
            // Allow mouse interaction (remove click-through).
            exStyle &= ~WS_EX_TRANSPARENT;
            Program.Log("Touch mode ON — overlay is interactive");
        }
        else
        {
            // Click-through, no interaction possible.
            exStyle |= WS_EX_TRANSPARENT;
            Program.Log("Touch mode OFF — overlay is click-through");
        }

        SetWindowLongPtr(Handle, GWL_EXSTYLE, (IntPtr)exStyle);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        UpdateInteractionVisuals();
    }

    // ── Ask selection (Ctrl+Shift+X) ───────────────────────────────────

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

            // Deselect after copy: send Right Arrow to collapse the highlight
            if (_config.DeselectAfterCopy)
            {
                keybd_event((byte)VK_RIGHT, 0, 0, IntPtr.Zero);
                keybd_event((byte)VK_RIGHT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }

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

            // Add to conversation and send without changing window state.
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

    // ── Ask screenshot (Ctrl+Shift+S) ──────────────────────────────────

    private async Task AskScreenshotAsync()
    {
        try
        {
            Program.Log("AskScreenshot: capturing screen");

            // Release Shift first so the screenshot doesn't include the
            // modifier state in any on-screen indicators.
            keybd_event((byte)VK_SHIFT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            await Task.Delay(10);

            var imageBase64 = CaptureScreenBase64();
            if (string.IsNullOrEmpty(imageBase64))
            {
                Program.Log("AskScreenshot: capture failed");
                return;
            }

            Program.Log($"AskScreenshot: got {imageBase64.Length} base64 chars");

            if (_currentConversation is null)
                StartConversation();

            // Add a text placeholder to conversation history; the actual
            // image is sent inline to the vision model.
            var prompt = _config.VisionPrompt;
            _currentConversation!.Messages.Add(new ChatMessage(false, prompt));
            AddBubbleToUI("[Screenshot captured]", isAssistant: false);

            await SendMessageWithImageAsync(prompt, imageBase64);
        }
        catch (Exception ex)
        {
            Program.Log("AskScreenshot error: " + ex);
        }
    }

    private static string CaptureScreenBase64()
    {
        try
        {
            // Capture ALL screens (virtual screen spans every monitor).
            var bounds = SystemInformation.VirtualScreen;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            Program.Log("CaptureScreenBase64 failed: " + ex.Message);
            return "";
        }
    }

    private async Task SendMessageWithImageAsync(string text, string imageBase64)
    {
        if (!_config.HasApiKey)
        {
            AddBubbleToUI("No API key configured. Set OPENROUTER_API_KEY environment variable or create\r\n" +
                          $"{LlmConfigLoader.ConfigPath} with an \"ApiKey\" field.", isAssistant: true);
            return;
        }

        if (_currentConversation is null)
            StartConversation();

        var bubble = AddStreamingBubble();

        _thinkingCts?.Cancel();
        _thinkingCts?.Dispose();
        _thinkingCts = new CancellationTokenSource();
        var ct = _thinkingCts.Token;

        _hud?.SetThinking();

        var fullText = "";
        try
        {
            var conversation = _currentConversation!;

            // Use the new OpenRouterService for vision requests
            if (_aiService is not null)
            {
                await foreach (var delta in _aiService.StreamVisionAsync(
                                   conversation.Messages, imageBase64, ct))
                {
                    fullText += delta;
                    var current = fullText;
                    BeginInvoke(new Action(() =>
                    {
                        if (bubble.IsDisposed) return;
                        var label = bubble.Controls.OfType<Label>().FirstOrDefault();
                        if (label != null)
                            label.Text = current;
                        _messages.ScrollControlIntoView(bubble);
                        _hud?.SetThinking(current);
                    }));
                }
            }
        }
        catch (OperationCanceledException)
        {
            fullText += "\r\n\r\n[cancelled]";
        }
        catch (Exception ex)
        {
            fullText = $"Error: {ex.Message}";
            Program.Log("LLM vision error: " + ex);
        }

        var finalText = fullText;
        var wasError = fullText.StartsWith("Error:");
        BeginInvoke(new Action(() =>
        {
            if (!bubble.IsDisposed)
            {
                var label = bubble.Controls.OfType<Label>().FirstOrDefault();
                if (label != null)
                    label.Text = finalText;
            }

            if (!wasError && _currentConversation != null && !string.IsNullOrWhiteSpace(finalText))
                _currentConversation.Messages.Add(new ChatMessage(true, finalText));

            _hud?.SetResponse(finalText);
        }));
    }
}
