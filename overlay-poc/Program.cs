using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OverlayPoc;

internal static class Program
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverlayPoc");

    private static readonly string LogPath = Path.Combine(LogDir, "poc.log");

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
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint GW_HWNDPREV = 3;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

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

    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _tray;
    private bool _captureHidden;

    public OverlayForm()
    {
        Program.Log("FORM ctor");

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(235, 15, 15, 18);
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

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Overlay POC"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Raise now", null, (_, _) => RaiseIfCovered());
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
            Program.Log($"HANDLE created. captureHidden={_captureHidden}");

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

    private void UpdateStatus()
    {
        _status.Text = _captureHidden
            ? "OVERLAY POC — capture-hidden: YES | click-through: YES"
            : "OVERLAY POC — capture-hidden: NO (need Win10 2004+) | click-through: YES";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Log("FORM closed");
        _timer?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnFormClosed(e);
    }
}
