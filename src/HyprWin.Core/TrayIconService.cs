using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Data model for a single system tray notification icon.
/// </summary>
public sealed class TrayIconInfo
{
    public IntPtr OwnerHwnd { get; init; }
    public uint IconId { get; init; }
    public uint CallbackMessage { get; init; }
    public IntPtr IconHandle { get; init; }
    public string Tooltip { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public ImageSource? IconImage { get; set; }
}

/// <summary>
/// Reads notification-area (system tray) icons from the hidden Windows taskbar
/// by inspecting the ToolbarWindow32 controls that Explorer uses internally.
/// Polls periodically and fires <see cref="IconsUpdated"/> with fresh icon data.
/// Click events can be forwarded to the original owner application.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    public event Action<List<TrayIconInfo>>? IconsUpdated;

    private System.Timers.Timer? _pollTimer;
    private bool _disposed;

    // ── Toolbar messages ──
    private const int TB_BUTTONCOUNT = 0x0418;
    private const int TB_GETBUTTON   = 0x0417;
    private const byte TBSTATE_HIDDEN = 0x08;

    // ── Process / memory constants ──
    private const uint PROCESS_VM_OPERATION        = 0x0008;
    private const uint PROCESS_VM_READ             = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION   = 0x0400;
    private const uint MEM_COMMIT                  = 0x1000;
    private const uint MEM_RELEASE                 = 0x8000;
    private const uint PAGE_READWRITE              = 0x04;

    // ── Mouse messages for click forwarding ──
    private const int WM_LBUTTONDOWN   = 0x0201;
    private const int WM_LBUTTONUP     = 0x0202;
    private const int WM_RBUTTONDOWN   = 0x0204;
    private const int WM_RBUTTONUP     = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_MOUSEMOVE     = 0x0200;

    // ── Struct sizes on x64 ──
    private const int TBBUTTON_SIZE_64 = 32;
    private const int TRAYDATA_SIZE    = 32;

    /// <summary>
    /// Start periodic polling of system tray icons.
    /// </summary>
    public void Start(int pollIntervalMs = 3000)
    {
        _pollTimer = new System.Timers.Timer(pollIntervalMs);
        _pollTimer.Elapsed += (_, _) => PollIcons();
        _pollTimer.Start();

        // Initial poll (off UI thread)
        Task.Run(() => PollIcons());
    }

    private void PollIcons()
    {
        if (_disposed) return;
        try
        {
            var icons = ReadTrayIcons();
            IconsUpdated?.Invoke(icons);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to poll tray icons", ex);
        }
    }

    /// <summary>
    /// Read all visible system tray icons from the notification area toolbars.
    /// Checks both the main tray and the overflow panel.
    /// </summary>
    public List<TrayIconInfo> ReadTrayIcons()
    {
        var result = new List<TrayIconInfo>();

        // 1. Main tray: Shell_TrayWnd > TrayNotifyWnd > SysPager > ToolbarWindow32
        var trayWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (trayWnd != IntPtr.Zero)
        {
            var notifyWnd = NativeMethods.FindWindowEx(trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notifyWnd != IntPtr.Zero)
            {
                var sysPager = NativeMethods.FindWindowEx(notifyWnd, IntPtr.Zero, "SysPager", null);
                if (sysPager != IntPtr.Zero)
                {
                    var toolbar = NativeMethods.FindWindowEx(sysPager, IntPtr.Zero, "ToolbarWindow32", null);
                    if (toolbar != IntPtr.Zero)
                        ReadToolbarIcons(toolbar, result);
                }
            }
        }

        // 2. Overflow panel: NotifyIconOverflowWindow > ToolbarWindow32
        //    On Windows 11 all icons live here when the taskbar is hidden.
        var overflowWnd = NativeMethods.FindWindow("NotifyIconOverflowWindow", null);
        if (overflowWnd != IntPtr.Zero)
        {
            var toolbar = NativeMethods.FindWindowEx(overflowWnd, IntPtr.Zero, "ToolbarWindow32", null);
            if (toolbar != IntPtr.Zero)
                ReadToolbarIcons(toolbar, result);
        }

        return result;
    }

    /// <summary>
    /// Read icons from a specific ToolbarWindow32 using cross-process memory access.
    /// </summary>
    private void ReadToolbarIcons(IntPtr toolbar, List<TrayIconInfo> result)
    {
        int buttonCount = (int)NativeMethods.SendMessage(toolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (buttonCount <= 0) return;

        NativeMethods.GetWindowThreadProcessId(toolbar, out uint processId);
        if (processId == 0) return;

        var hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false, processId);
        if (hProcess == IntPtr.Zero) return;

        IntPtr remoteMem = IntPtr.Zero;
        try
        {
            int allocSize = TBBUTTON_SIZE_64 + TRAYDATA_SIZE + 512;
            remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)allocSize, MEM_COMMIT, PAGE_READWRITE);
            if (remoteMem == IntPtr.Zero) return;

            var tbBuf   = new byte[TBBUTTON_SIZE_64];
            var trayBuf = new byte[TRAYDATA_SIZE];

            for (int i = 0; i < buttonCount; i++)
            {
                // TB_GETBUTTON writes TBBUTTON into remote memory
                NativeMethods.SendMessage(toolbar, TB_GETBUTTON, (IntPtr)i, remoteMem);

                if (!ReadProcessMemory(hProcess, remoteMem, tbBuf, (uint)TBBUTTON_SIZE_64, out _))
                    continue;

                // ── Parse TBBUTTON (x64 layout) ──
                // Offset  8: fsState  (1 byte)
                // Offset 16: dwData   (8 bytes → pointer to TRAYDATA)
                // Offset 24: iString  (8 bytes → pointer to tooltip)
                byte   fsState  = tbBuf[8];
                IntPtr dwData   = (IntPtr)BitConverter.ToInt64(tbBuf, 16);
                IntPtr iString  = (IntPtr)BitConverter.ToInt64(tbBuf, 24);

                if ((fsState & TBSTATE_HIDDEN) != 0) continue;
                if (dwData == IntPtr.Zero)            continue;

                // ── Read TRAYDATA from remote process ──
                if (!ReadProcessMemory(hProcess, dwData, trayBuf, (uint)TRAYDATA_SIZE, out _))
                    continue;

                IntPtr ownerHwnd       = (IntPtr)BitConverter.ToInt64(trayBuf, 0);
                uint   uID             = BitConverter.ToUInt32(trayBuf, 8);
                uint   uCallbackMsg    = BitConverter.ToUInt32(trayBuf, 12);
                IntPtr hIcon           = (IntPtr)BitConverter.ToInt64(trayBuf, 24);

                if (hIcon == IntPtr.Zero) continue;

                // Skip duplicates (same owner + ID already collected from main tray)
                if (result.Any(r => r.OwnerHwnd == ownerHwnd && r.IconId == uID))
                    continue;

                // ── Process name ──
                string processName = "";
                if (ownerHwnd != IntPtr.Zero && NativeMethods.IsWindow(ownerHwnd))
                {
                    NativeMethods.GetWindowThreadProcessId(ownerHwnd, out uint ownerPid);
                    try
                    {
                        using var proc = Process.GetProcessById((int)ownerPid);
                        processName = proc.ProcessName;
                    }
                    catch { /* process may have exited */ }
                }

                // ── Tooltip ──
                string tooltip = processName;
                if (iString != IntPtr.Zero && iString != (IntPtr)(-1) && (long)iString > 0x10000)
                {
                    try
                    {
                        var tipBuf = new byte[512];
                        if (ReadProcessMemory(hProcess, iString, tipBuf, 512, out _))
                        {
                            string text = System.Text.Encoding.Unicode.GetString(tipBuf);
                            int nul = text.IndexOf('\0');
                            if (nul >= 0) text = text[..nul];
                            if (!string.IsNullOrWhiteSpace(text))
                                tooltip = text;
                        }
                    }
                    catch { /* non-critical */ }
                }

                result.Add(new TrayIconInfo
                {
                    OwnerHwnd       = ownerHwnd,
                    IconId          = uID,
                    CallbackMessage = uCallbackMsg,
                    IconHandle      = hIcon,
                    Tooltip         = tooltip,
                    ProcessName     = processName,
                });
            }
        }
        finally
        {
            if (remoteMem != IntPtr.Zero)
                VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
            CloseHandle(hProcess);
        }
    }

    // ──────────────── Public helpers ────────────────

    /// <summary>
    /// Convert an HICON handle to a frozen WPF ImageSource.
    /// Must be called on the UI thread. Returns null on failure.
    /// </summary>
    public static ImageSource? IconToImageSource(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Forward a left-click or right-click to the original tray icon owner.
    /// Uses the legacy callback format (wParam = uID, lParam = mouse-message).
    /// </summary>
    public static void SendIconClick(TrayIconInfo icon, bool rightClick)
    {
        if (icon.OwnerHwnd == IntPtr.Zero || icon.CallbackMessage == 0) return;
        if (!NativeMethods.IsWindow(icon.OwnerHwnd)) return;

        try
        {
            int downMsg = rightClick ? WM_RBUTTONDOWN : WM_LBUTTONDOWN;
            int upMsg   = rightClick ? WM_RBUTTONUP   : WM_LBUTTONUP;

            // Simulate down + up so apps that check both messages work correctly
            NativeMethods.PostMessage(icon.OwnerHwnd, icon.CallbackMessage,
                (IntPtr)icon.IconId, (IntPtr)downMsg);
            NativeMethods.PostMessage(icon.OwnerHwnd, icon.CallbackMessage,
                (IntPtr)icon.IconId, (IntPtr)upMsg);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to forward tray click to {icon.ProcessName}", ex);
        }
    }

    /// <summary>
    /// Forward a double-click to the original tray icon owner.
    /// Many apps open their main window on tray icon double-click.
    /// </summary>
    public static void SendIconDoubleClick(TrayIconInfo icon)
    {
        if (icon.OwnerHwnd == IntPtr.Zero || icon.CallbackMessage == 0) return;
        if (!NativeMethods.IsWindow(icon.OwnerHwnd)) return;

        try
        {
            NativeMethods.PostMessage(icon.OwnerHwnd, icon.CallbackMessage,
                (IntPtr)icon.IconId, (IntPtr)WM_LBUTTONDBLCLK);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to forward tray double-click to {icon.ProcessName}", ex);
        }
    }

    // ──────────────── P/Invoke (cross-process memory) ────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
        uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
        uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, uint nSize, out int lpNumberOfBytesRead);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
    }
}
