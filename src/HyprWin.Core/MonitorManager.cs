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

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get { lock (_lock) return _monitors.ToList().AsReadOnly(); }
    }

    public int Count
    {
        get { lock (_lock) return _monitors.Count; }
    }

    /// <summary>
    /// Enumerate all connected monitors and build the monitor list.
    /// </summary>
    public void Enumerate(int topBarHeight = 30, string topBarPosition = "top")
    {
        lock (_lock)
        {
            _monitors.Clear();
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

                        _monitors.Add(new MonitorInfo
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

            Logger.Instance.Info($"Enumerated {_monitors.Count} monitor(s)");
            foreach (var mon in _monitors)
            {
                Logger.Instance.Debug($"  Monitor {mon.Index}: {mon.DeviceName} " +
                    $"Bounds={mon.Bounds} Work={mon.WorkArea} Effective={mon.EffectiveWorkArea} " +
                    $"DPI={mon.DpiX}x{mon.DpiY}");
            }
        }
    }

    /// <summary>
    /// Get the monitor info for a given monitor handle.
    /// </summary>
    public MonitorInfo? GetByHandle(IntPtr hMonitor)
    {
        lock (_lock)
            return _monitors.FirstOrDefault(m => m.Handle == hMonitor);
    }

    /// <summary>
    /// Get monitor by index.
    /// </summary>
    public MonitorInfo? GetByIndex(int index)
    {
        lock (_lock)
            return index >= 0 && index < _monitors.Count ? _monitors[index] : null;
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
