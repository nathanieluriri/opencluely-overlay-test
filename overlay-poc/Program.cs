using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OverlayPoc;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OverlayForm());
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
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(235, 15, 15, 18);
        Opacity = 0.92;

        var screen = Screen.PrimaryScreen.Bounds;
        Bounds = new Rectangle(screen.Width / 2 - 240, 40, 480, 52);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Lime,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Text = "OVERLAY POC — checking..."
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
        menu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => RaiseIfCovered();
        _timer.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        exStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        _captureHidden = SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
        RaiseIfCovered();
        UpdateStatus();
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
    }

    private void UpdateStatus()
    {
        _status.Text = _captureHidden
            ? "OVERLAY POC — capture-hidden: YES | click-through: YES"
            : "OVERLAY POC — capture-hidden: NO (need Win10 2004+) | click-through: YES";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnFormClosed(e);
    }
}
