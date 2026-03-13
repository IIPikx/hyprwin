using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Renders a colored border around the active (focused) window.
/// Uses a non-transparent WPF window with a Win32 region (SetWindowRgn) for the frame shape.
/// This avoids AllowsTransparency=true software rendering and uses GPU-accelerated rendering.
/// Position tracking via SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) for zero-lag updates
/// and direct Win32 SetWindowPos (bypasses WPF layout pass).
/// </summary>
public sealed class BorderRenderer : IDisposable
{
    private Window? _borderWindow;
    private IntPtr _borderHwnd;
    private IntPtr _trackedHwnd;
    private bool _disposed;

    // WinEvent hook for location changes (zero-lag tracking)
    private IntPtr _locationHook;
    private NativeMethods.WinEventDelegate? _locationCallback;

    // Fallback polling timer for focus changes and edge cases
    private System.Windows.Threading.DispatcherTimer? _fallbackTimer;

    // Cached last position/size to skip redundant updates
    private NativeMethods.RECT _lastRect;
    private int _lastRegionW;
    private int _lastRegionH;

    private string _activeColor = "#cba6f7";
    private string _inactiveColor = "#45475a";
    private int _borderSize = 2;
    private int _rounding = 8;

    /// <summary>
    /// Initialize the border overlay window.
    /// </summary>
    public void Start()
    {
        var brush = BrushFromHex(_activeColor);

        _borderWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false, // GPU-accelerated rendering — no software fallback
            Background = brush,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            IsHitTestVisible = false,
            Width = 1,
            Height = 1,
            Left = -10000,
            Top = -10000,
        };

        _borderWindow.Show();

        // Cache the HWND for direct Win32 calls (avoids WPF interop overhead per frame)
        _borderHwnd = new WindowInteropHelper(_borderWindow).Handle;

        // Make the window click-through at the Win32 level:
        // WS_EX_LAYERED + WS_EX_TRANSPARENT = click passes through to windows underneath
        // WS_EX_TOOLWINDOW = hides from Alt+Tab
        int exStyle = NativeMethods.GetWindowLong(_borderHwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_borderHwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | (int)NativeMethods.WS_EX_TRANSPARENT | (int)NativeMethods.WS_EX_LAYERED | (int)NativeMethods.WS_EX_TOOLWINDOW);

        // Set fully opaque — the region handles the visible shape, not transparency
        NativeMethods.SetLayeredWindowAttributes(_borderHwnd, 0, 255, NativeMethods.LWA_ALPHA);

        _borderWindow.Title = "HyprWinBorder";

        // Install WinEvent hook for EVENT_OBJECT_LOCATIONCHANGE — fires immediately
        // when ANY window moves/resizes, giving us zero-lag border tracking.
        _locationCallback = OnLocationChanged;
        _locationHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationCallback,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        // Low-frequency fallback timer for edge cases (focus changes missed, etc.)
        _fallbackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _fallbackTimer.Tick += OnFallbackTick;
        _fallbackTimer.Start();

        Logger.Instance.Info("Border renderer started (WinEvent-driven, region-based)");
    }

    /// <summary>
    /// Update theme from config.
    /// </summary>
    public void UpdateTheme(string activeColor, string inactiveColor, int borderSize, int rounding)
    {
        _activeColor = activeColor;
        _inactiveColor = inactiveColor;
        _borderSize = borderSize;
        _rounding = rounding;

        if (_borderWindow != null)
        {
            _borderWindow.Background = BrushFromHex(_activeColor);
        }

        // Force region recalculation on next position update
        _lastRegionW = 0;
        _lastRegionH = 0;
    }

    /// <summary>
    /// Update which window is being tracked (called on focus change).
    /// </summary>
    public void TrackWindow(IntPtr hwnd)
    {
        _trackedHwnd = hwnd;
        UpdateBorderPositionDirect();
    }

    /// <summary>
    /// Hide the border overlay (e.g. when a fullscreen game is focused).
    /// </summary>
    public void Hide()
    {
        if (_borderHwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_borderHwnd, NativeMethods.HWND_TOPMOST,
            -10000, -10000, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        _trackedHwnd = IntPtr.Zero;
    }

    /// <summary>
    /// WinEvent callback — fires immediately when any window changes position/size.
    /// We filter for only our tracked window to minimize overhead.
    /// </summary>
    private void OnLocationChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only respond to location changes of the tracked window
        if (hwnd != _trackedHwnd || _trackedHwnd == IntPtr.Zero) return;
        if (idObject != NativeMethods.OBJID_WINDOW) return;

        UpdateBorderPositionDirect();
    }

    /// <summary>
    /// Low-frequency fallback to catch focus changes and edge cases.
    /// </summary>
    private void OnFallbackTick(object? sender, EventArgs e)
    {
        try
        {
            var fgHwnd = NativeMethods.GetForegroundWindow();
            if (fgHwnd != _trackedHwnd && fgHwnd != _borderHwnd)
            {
                _trackedHwnd = fgHwnd;
                UpdateBorderPositionDirect();
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Debug($"BorderRenderer.OnFallbackTick error: {ex.Message}");
        }
    }

    /// <summary>
    /// Repositions the border overlay using direct Win32 SetWindowPos.
    /// Bypasses WPF layout/render pass entirely for minimal latency.
    /// </summary>
    private void UpdateBorderPositionDirect()
    {
        if (_borderHwnd == IntPtr.Zero || _trackedHwnd == IntPtr.Zero) return;

        try
        {
            if (!NativeMethods.IsWindow(_trackedHwnd) || !NativeMethods.IsWindowVisible(_trackedHwnd))
            {
                // Hide border off-screen
                NativeMethods.SetWindowPos(_borderHwnd, NativeMethods.HWND_TOPMOST,
                    -10000, -10000, 0, 0,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                return;
            }

            // Use extended frame bounds for accurate visible rect
            var rect = NativeMethods.GetExtendedFrameBounds(_trackedHwnd);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                if (!NativeMethods.GetWindowRect(_trackedHwnd, out rect)) return;
            }

            // Skip update if position hasn't changed (avoid redundant SetWindowPos)
            if (rect.Left == _lastRect.Left && rect.Top == _lastRect.Top &&
                rect.Right == _lastRect.Right && rect.Bottom == _lastRect.Bottom)
                return;
            _lastRect = rect;

            // Position the border slightly outside the window
            int offset = _borderSize;
            int x = rect.Left - offset;
            int y = rect.Top - offset;
            int w = rect.Width + offset * 2;
            int h = rect.Height + offset * 2;

            // Update the Win32 region if the window size changed (creates frame shape)
            UpdateWindowRegion(w, h);

            // Direct Win32 repositioning — no WPF layout pass, minimal latency
            NativeMethods.SetWindowPos(_borderHwnd, NativeMethods.HWND_TOPMOST,
                x, y, w, h,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        catch
        {
            // Silently handle — window may have been destroyed
        }
    }

    /// <summary>
    /// Creates a frame-shaped Win32 region (outer rounded rect minus inner rounded rect)
    /// and applies it to the border window. Only recreated when size changes.
    /// </summary>
    private void UpdateWindowRegion(int w, int h)
    {
        if (w == _lastRegionW && h == _lastRegionH) return;
        _lastRegionW = w;
        _lastRegionH = h;

        int bs = _borderSize;
        int outerRound = (_rounding + bs) * 2;   // ellipse diameter for outer corners
        int innerRound = _rounding * 2;           // ellipse diameter for inner corners

        // Outer rect = full window area, Inner rect = window area minus border thickness
        // CreateRoundRectRgn uses exclusive right/bottom, hence +1
        IntPtr outer = NativeMethods.CreateRoundRectRgn(0, 0, w + 1, h + 1, outerRound, outerRound);
        IntPtr inner = NativeMethods.CreateRoundRectRgn(bs, bs, w - bs + 1, h - bs + 1, innerRound, innerRound);
        IntPtr frame = NativeMethods.CreateRectRgn(0, 0, 0, 0); // empty destination

        // frame = outer minus inner → hollow border shape
        NativeMethods.CombineRgn(frame, outer, inner, NativeMethods.RGN_DIFF);

        // SetWindowRgn takes ownership of 'frame' — do NOT delete it
        NativeMethods.SetWindowRgn(_borderHwnd, frame, true);

        // Clean up temporary regions (frame is now owned by the window)
        NativeMethods.DeleteObject(outer);
        NativeMethods.DeleteObject(inner);
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze(); // Freeze for cross-thread safety and performance
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush(Colors.MediumPurple);
            brush.Freeze();
            return brush;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _fallbackTimer?.Stop();
        if (_locationHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_locationHook);
        _borderWindow?.Close();
        Logger.Instance.Info("Border renderer stopped");
    }
}
