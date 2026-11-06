using System.Runtime.InteropServices;
using System.Text;

namespace OverlayPoc;

/// <summary>
/// Win32 entry points and constants used to keep the overlay topmost,
/// click-through, hidden from screen capture, and to install the global
/// low-level keyboard hook.
/// </summary>
internal static class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;

    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    internal const uint GW_HWNDPREV = 3;
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    internal const int WM_NCLBUTTONDOWN = 0x00A1;
    internal const int HTCAPTION = 2;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    // ── Low-level keyboard hook ────────────────────────────────────────

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;

    internal const int VK_BACK = 0x08;
    internal const int VK_TAB = 0x09;
    internal const int VK_RETURN = 0x0D;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_PAUSE = 0x13;
    internal const int VK_CAPITAL = 0x14;
    internal const int VK_O = 0x4F;
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_SPACE = 0x20;
    internal const int VK_PRIOR = 0x21;
    internal const int VK_NEXT = 0x22;
    internal const int VK_END = 0x23;
    internal const int VK_HOME = 0x24;
    internal const int VK_LEFT = 0x25;
    internal const int VK_UP = 0x26;
    internal const int VK_RIGHT = 0x27;
    internal const int VK_DOWN = 0x28;
    internal const int VK_SNAPSHOT = 0x2C;
    internal const int VK_INSERT = 0x2D;
    internal const int VK_DELETE = 0x2E;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;
    internal const int VK_APPS = 0x5D;
    internal const int VK_NUMLOCK = 0x90;
    internal const int VK_SCROLL = 0x91;
    internal const int VK_LSHIFT = 0xA0;
    internal const int VK_RSHIFT = 0xA1;
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;
    internal const int VK_LMENU = 0xA4;
    internal const int VK_RMENU = 0xA5;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    internal static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out] StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    internal static extern bool GetKeyboardState(byte[] lpKeyState);

    // ── Existing window-management declarations ────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
