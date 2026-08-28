using System.Runtime.InteropServices;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Represents information about a physical monitor.
/// </summary>
public record MonitorInfo
{
    public IntPtr Handle { get; init; }
    public NativeMethods.RECT Bounds { get; init; }     // Full monitor rect
    public NativeMethods.RECT WorkArea { get; init; }    // Excludes system bars
    public int Index { get; init; }
    public string DeviceName { get; init; } = "";
    public uint DpiX { get; init; } = 96;
    public uint DpiY { get; init; } = 96;
    public double ScaleFactor => DpiX / 96.0;

    /// <summary>
    /// Effective work area after subtracting the HyprWin top bar.
    /// </summary>
    public NativeMethods.RECT EffectiveWorkArea { get; init; }
}

/// <summary>
/// Enumerates and manages physical monitors. Provides per-monitor info and
/// calculates effective work areas after accounting for the custom top bar.
/// </summary>
public sealed class MonitorManager
{
    private readonly List<MonitorInfo> _monitors = new();
    private readonly object _lock = new();
    private volatile IReadOnlyList<MonitorInfo> _cachedMonitors = Array.Empty<MonitorInfo>();

    /// <summary>
    /// Fired when display topology changes (e.g. monitor plugged/unplugged, resolution changed).
    /// </summary>
    public event Action? MonitorChanged;

    /// <summary>
    /// Zero-allocation read of the current monitor list.
    /// Returns the cached snapshot; never allocates a new list.
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors => _cachedMonitors;

    public int Count => _cachedMonitors.Count;

    /// <summary>
    /// Enumerate all connected monitors and build the monitor list.
    /// </summary>
    public void Enumerate(int topBarHeight = 30, string topBarPosition = "top")
    {
        var newList = new List<MonitorInfo>();
        int index = 0;

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFOEX();
                mi.cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    uint dpiX = 96, dpiY = 96;
                    try
                    {
                        NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                    }
                    catch { /* fallback to 96 */ }

                    // Calculate effective work area.
                    // Use rcMonitor (full physical bounds) — NOT rcWork — because
                    // HyprWin hides the native taskbar, so the system work-area
                    // reservation is irrelevant and we must reclaim that space.
                    // Only subtract the HyprWin top bar height.
                    var effectiveWork = mi.rcMonitor;
                    if (topBarPosition.Equals("top", StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveWork = new NativeMethods.RECT(
                            mi.rcMonitor.Left,
                            mi.rcMonitor.Top + topBarHeight,
                            mi.rcMonitor.Right,
                            mi.rcMonitor.Bottom);
                    }
                    else // bottom
                    {
                        effectiveWork = new NativeMethods.RECT(
                            mi.rcMonitor.Left,
                            mi.rcMonitor.Top,
                            mi.rcMonitor.Right,
                            mi.rcMonitor.Bottom - topBarHeight);
                    }

                    newList.Add(new MonitorInfo
                    {
                        Handle = hMonitor,
                        Bounds = mi.rcMonitor,
                        WorkArea = mi.rcWork,
                        EffectiveWorkArea = effectiveWork,
                        Index = index,
                        DeviceName = mi.szDevice,
                        DpiX = dpiX,
                        DpiY = dpiY,
                    });
                    index++;
                }
                return true;
            }, IntPtr.Zero);

        // Atomically replace the cached list — readers need no lock.
        lock (_lock)
        {
            _monitors.Clear();
            _monitors.AddRange(newList);
            _cachedMonitors = _monitors.ToList().AsReadOnly();
        }

        Logger.Instance.Info($"Enumerated {_monitors.Count} monitor(s)");
        foreach (var mon in _monitors)
        {
            Logger.Instance.Debug($"  Monitor {mon.Index}: {mon.DeviceName} " +
                $"Bounds={mon.Bounds} Work={mon.WorkArea} Effective={mon.EffectiveWorkArea} " +
                $"DPI={mon.DpiX}x{mon.DpiY}");
        }
    }

    /// <summary>
    /// Called by the WPF message pump when WM_DISPLAYCHANGE is received.
    /// Re-enumerates monitors and notifies subscribers.
    /// </summary>
    public void OnDisplayChange(int topBarHeight = 30, string topBarPosition = "top")
    {
        Logger.Instance.Info("WM_DISPLAYCHANGE received — re-enumerating monitors");
        Enumerate(topBarHeight, topBarPosition);
        MonitorChanged?.Invoke();
    }

    /// <summary>
    /// Get the monitor info for a given monitor handle.
    /// </summary>
    public MonitorInfo? GetByHandle(IntPtr hMonitor)
    {
        var list = _cachedMonitors;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Handle == hMonitor) return list[i];
        return null;
    }

    /// <summary>
    /// Get monitor by index.
    /// </summary>
    public MonitorInfo? GetByIndex(int index)
    {
        var list = _cachedMonitors;
        return index >= 0 && index < list.Count ? list[index] : null;
    }

    /// <summary>
    /// Determine which monitor a window belongs to.
    /// </summary>
    public MonitorInfo? GetMonitorForWindow(IntPtr hwnd)
    {
        var hMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return GetByHandle(hMon);
    }

    /// <summary>
    /// Get the monitor that the cursor is currently on.
    /// </summary>
    public MonitorInfo? GetMonitorAtCursor()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            var nativePoint = new NativeMethods.POINT { X = point.X, Y = point.Y };
            var hMon = NativeMethods.MonitorFromPoint(nativePoint, NativeMethods.MONITOR_DEFAULTTONEAREST);
            return GetByHandle(hMon);
        }
        return GetByIndex(0);
    }
}
