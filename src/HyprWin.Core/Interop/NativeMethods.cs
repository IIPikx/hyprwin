using System.Runtime.InteropServices;
using System.Text;

namespace HyprWin.Core.Interop;

/// <summary>
/// All Win32 P/Invoke declarations used by HyprWin.
/// </summary>
public static class NativeMethods
{
    // ──────────────────────── Delegates ────────────────────────
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    // ──────────────────────── Constants ────────────────────────

    // Hook types
    public const int WH_KEYBOARD_LL = 13;

    // Window messages
    public const int WM_KEYDOWN    = 0x0100;
    public const int WM_KEYUP      = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP   = 0x0105;
    public const int WM_CLOSE      = 0x0010;
    public const int WM_DISPLAYCHANGE = 0x007E;

    // Virtual key codes
    public const int VK_LWIN   = 0x5B;
    public const int VK_RWIN   = 0x5C;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU  = 0xA4; // Left Alt
    public const int VK_RMENU  = 0xA5; // Right Alt
    public const int VK_SHIFT  = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU   = 0x12; // Alt
    public const int VK_RETURN = 0x0D;
    public const int VK_LEFT   = 0x25;
    public const int VK_UP     = 0x26;
    public const int VK_RIGHT  = 0x27;
    public const int VK_DOWN   = 0x28;
    public const int VK_TAB    = 0x09;
    public const int VK_SPACE  = 0x20;
    public const int VK_F11    = 0x7A;

    // ShowWindow commands
    public const int SW_HIDE       = 0;
    public const int SW_SHOW       = 5;
    public const int SW_RESTORE    = 9;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_MINIMIZE   = 6;
    public const int SW_MAXIMIZE   = 3;

    // SetWindowPos flags
    public const uint SWP_NOACTIVATE   = 0x0010;
    public const uint SWP_NOZORDER     = 0x0004;
    public const uint SWP_SHOWWINDOW   = 0x0040;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOSIZE       = 0x0001;
    public const uint SWP_NOMOVE       = 0x0002;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    // GetWindowLong indices
    public const int GWL_STYLE   = -16;
    public const int GWL_EXSTYLE = -20;

    // Window styles
    public const uint WS_VISIBLE      = 0x10000000;
    public const uint WS_CAPTION      = 0x00C00000;
    public const uint WS_CHILD        = 0x40000000;
    public const uint WS_POPUP        = 0x80000000;
    public const uint WS_MAXIMIZE     = 0x01000000;
    public const uint WS_MINIMIZE     = 0x20000000;
    public const uint WS_THICKFRAME   = 0x00040000;

    // Extended window styles
    public const uint WS_EX_TOOLWINDOW   = 0x00000080;
    public const uint WS_EX_NOACTIVATE   = 0x08000000;
    public const uint WS_EX_APPWINDOW    = 0x00040000;
    public const uint WS_EX_TRANSPARENT  = 0x00000020;
    public const uint WS_EX_LAYERED      = 0x00080000;

    // WinEvent constants
    public const uint EVENT_OBJECT_CREATE      = 0x8000;
    public const uint EVENT_OBJECT_DESTROY     = 0x8001;
    public const uint EVENT_OBJECT_SHOW        = 0x8002;
    public const uint EVENT_OBJECT_HIDE        = 0x8003;
    public const uint EVENT_SYSTEM_FOREGROUND  = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZEEND  = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND   = 0x0017;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE  = 0x800C;
    public const uint WINEVENT_OUTOFCONTEXT    = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS  = 0x0002;
    public const int  OBJID_WINDOW             = 0;
    public const int  CHILDID_SELF             = 0;

    // DWM attributes
    public const int DWMWA_EXTENDED_FRAME_BOUNDS      = 9;
    public const int DWMWA_CLOAKED                    = 14;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE   = 33;

    // DWM window corner preferences
    public const int DWMWCP_DEFAULT    = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND      = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    // AppBar messages
    public const uint ABM_NEW       = 0x00;
    public const uint ABM_REMOVE    = 0x01;
    public const uint ABM_QUERYPOS  = 0x02;
    public const uint ABM_SETPOS    = 0x03;
    public const uint ABE_TOP       = 1;
    public const uint ABE_BOTTOM    = 3;

    // Shell broadcast
    public const uint WM_SETTINGCHANGE = 0x001A;
    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

    // AnimateWindow flags
    public const uint AW_SLIDE         = 0x00040000;
    public const uint AW_ACTIVATE      = 0x00020000;
    public const uint AW_BLEND         = 0x00080000;
    public const uint AW_HIDE          = 0x00010000;
    public const uint AW_HOR_POSITIVE  = 0x00000001;
    public const uint AW_HOR_NEGATIVE  = 0x00000002;
    public const uint AW_VER_POSITIVE  = 0x00000004;
    public const uint AW_VER_NEGATIVE  = 0x00000008;

    // GetKeyState
    public const int KEY_PRESSED = 0x8000;

    // Keyboard event flags
    public const uint KEYEVENTF_KEYUP = 0x0002;

    // Low-level keyboard hook flags
    public const uint LLKHF_INJECTED = 0x00000010;

    // ──────────────────────── Structs ────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public int CenterX => Left + Width / 2;
        public int CenterY => Top + Height / 2;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }

        public override string ToString() => $"({Left},{Top},{Right},{Bottom}) [{Width}x{Height}]";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    // ──────────────────────── Functions ────────────────────────

    // Keyboard hooks
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Key state
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    // Window enumeration & info
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd); // Is minimized

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd); // Is maximized

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // Window positioning
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AnimateWindow(IntPtr hWnd, uint dwTime, uint dwFlags);

    // Focus
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>
    /// Grants a process the right to call SetForegroundWindow.
    /// Pass 0xFFFFFFFF to grant all processes (ASFW_ANY).
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    // Use ASFW_ANY to grant foreground rights to any process (including ourselves).
    public const uint ASFW_ANY = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // Find window
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    // Messages
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // Monitors
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    // DWM
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // WinEvent hooks
    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // AppBar
    [DllImport("shell32.dll")]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // Memory info
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // System metrics
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXSCREEN      = 0;
    public const int SM_CYSCREEN      = 1;
    public const int SM_REMOTESESSION = 0x1000; // Non-zero when running inside a Terminal Services / RDP client session

    // DPI
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public const int MDT_EFFECTIVE_DPI = 0;

    // Cursor
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // Keyboard simulation
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // Register window message
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    // ──────────────────────── Helpers ────────────────────────
    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsWindowCloaked(IntPtr hWnd)
    {
        DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return cloaked != 0;
    }

    public static RECT GetExtendedFrameBounds(IntPtr hWnd)
    {
        DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>());
        return rect;
    }

    public static void SetCornerPreference(IntPtr hWnd, int preference)
    {
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    /// <summary>
    /// Forcefully bring a window to the foreground, even if our process is not the foreground process.
    /// </summary>
    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        // Grant foreground rights to our process before calling SetForegroundWindow.
        // The LL keyboard hook executes with foreground input context, so
        // AllowSetForegroundWindow works here — but the actual UI action runs via
        // BeginInvoke with a slight delay. We call AllowSetForegroundWindow(ASFW_ANY)
        // BOTH at hook time (see KeyboardHook.cs) and here right before the call.
        AllowSetForegroundWindow(ASFW_ANY);
        SetForegroundWindow(hWnd);
        BringWindowToTop(hWnd);
    }
}
