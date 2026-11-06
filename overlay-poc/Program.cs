using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OverlayPoc;

internal static class Program
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverlayPoc");

    internal static readonly string LogPath = Path.Combine(LogDir, "poc.log");

    [STAThread]
    private static void Main()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            Log("START (process alive)");

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Fatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Log("Creating form");
            Application.Run(new OverlayForm());
            Log("EXIT normal");
        }
        catch (Exception ex)
        {
            Fatal(ex);
        }
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void Fatal(Exception ex)
    {
        var text = ex?.ToString() ?? "Unknown fatal error";
        Log("FATAL: " + text);

        try
        {
            MessageBox.Show(
                "OverlayPoc failed to start:\n\n" + (ex?.Message ?? text) +
                "\n\nFull log:\n" + LogPath,
                "OverlayPoc",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Nothing else we can do.
        }
    }
}

internal sealed class OverlayForm : Form
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const uint GW_HWNDPREV = 3;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private const int WM_HOTKEY = 0x0312;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 1;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _tray;
    private bool _captureHidden;
    private bool _interactive;

    public OverlayForm()
    {
        Program.Log("FORM ctor");

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(15, 15, 18);
        Opacity = 0.92;

        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        Bounds = new Rectangle(screen.Width / 2 - 240, 40, 480, 52);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Lime,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Text = "OVERLAY POC — starting..."
        };
        Controls.Add(_status);

        MouseDown += BeginDrag;
        _status.MouseDown += BeginDrag;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Overlay POC"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Raise now", null, (_, _) => RaiseIfCovered());
        menu.Items.Add("Toggle interactive (Ctrl+Shift+O)", null, (_, _) => ToggleInteractive());
        menu.Items.Add("Open log", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", Program.LogPath); } catch { }
        });
        menu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => RaiseIfCovered();
        _timer.Start();

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
            var hotkeyOk = RegisterHotKey(Handle, HotkeyId,
                MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.O);
            Program.Log($"HANDLE created. captureHidden={_captureHidden} hotkey={hotkeyOk}");

            RaiseIfCovered();
            UpdateStatus();
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
        _tray.ShowBalloonTip(3000, "Overlay POC", "Running. Right-click the tray icon to exit.", ToolTipIcon.Info);
    }

    private void RaiseIfCovered()
    {
        if (!IsHandleCreated || IsDisposed) return;

        // GW_HWNDPREV is the window directly above us in the z-order.
        // NULL means nothing is above us, so there is nothing to fix and no
        // need to touch the z-order (which is what caused the flicker).
        if (GetWindow(Handle, GW_HWNDPREV) == IntPtr.Zero) return;

        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Program.Log("Raised above covering window");
    }

    private void BeginDrag(object sender, MouseEventArgs e)
    {
        if (!_interactive || e.Button != MouseButtons.Left) return;

        // Hand the drag to Windows' standard window-move loop.
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private void ToggleInteractive()
    {
        if (!IsHandleCreated || IsDisposed) return;

        _interactive = !_interactive;

        var exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        if (_interactive)
        {
            exStyle &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        }
        else
        {
            exStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        }
        SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        Program.Log($"Interactive={_interactive}");
        UpdateStatus();

        if (!_interactive)
        {
            RaiseIfCovered();
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

    private void UpdateStatus()
    {
        if (_interactive)
        {
            _status.ForeColor = Color.Yellow;
            _status.Text = "INTERACTIVE — drag me | Ctrl+Shift+O to lock";
        }
        else
        {
            _status.ForeColor = Color.Lime;
            _status.Text = _captureHidden
                ? "OVERLAY POC — capture-hidden: YES | click-through: YES | Ctrl+Shift+O to interact"
                : "OVERLAY POC — capture-hidden: NO (Win10 2004+) | Ctrl+Shift+O to interact";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log("FORM closed");
        try { UnregisterHotKey(Handle, HotkeyId); } catch { }
        _timer?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnFormClosed(e);
    }
}
