using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Renders a colored border around the active (focused) window using a transparent WPF overlay.
/// Uses SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) for lag-free position tracking
/// and SetWindowPos for direct Win32 repositioning (bypasses WPF layout pass).
/// </summary>
public sealed class BorderRenderer : IDisposable
{
    private Window? _borderWindow;
    private Border? _borderElement;
    private IntPtr _borderHwnd;
    private IntPtr _trackedHwnd;
    private bool _disposed;

    // WinEvent hook for location changes (zero-lag tracking)
    private IntPtr _locationHook;
    private NativeMethods.WinEventDelegate? _locationCallback;

    // Fallback polling timer for focus changes and edge cases
    private System.Windows.Threading.DispatcherTimer? _fallbackTimer;

    // Cached last position to skip redundant updates
    private NativeMethods.RECT _lastRect;

    private string _activeColor = "#cba6f7";
    private string _inactiveColor = "#45475a";
    private int _borderSize = 2;
    private int _rounding = 8;

    /// <summary>
    /// Initialize the border overlay window.
    /// </summary>
    public void Start()
    {
        _borderWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            IsHitTestVisible = false,
            Width = 0,
            Height = 0,
            Left = -10000,
            Top = -10000,
        };

        _borderElement = new Border
        {
            BorderBrush = BrushFromHex(_activeColor),
            BorderThickness = new Thickness(_borderSize),
            CornerRadius = new CornerRadius(_rounding),
            Background = Brushes.Transparent,
        };

        _borderWindow.Content = _borderElement;
        _borderWindow.Show();

        // Cache the HWND for direct Win32 calls (avoids WPF interop overhead per frame)
        _borderHwnd = new WindowInteropHelper(_borderWindow).Handle;

        // Make the window extended-style transparent (click-through at Win32 level)
        int exStyle = NativeMethods.GetWindowLong(_borderHwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_borderHwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | (int)NativeMethods.WS_EX_TRANSPARENT | (int)NativeMethods.WS_EX_LAYERED | (int)NativeMethods.WS_EX_TOOLWINDOW);

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

        Logger.Instance.Info("Border renderer started (WinEvent-driven)");
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

        if (_borderElement != null)
        {
            _borderElement.BorderBrush = BrushFromHex(_activeColor);
            _borderElement.BorderThickness = new Thickness(_borderSize);
            _borderElement.CornerRadius = new CornerRadius(_rounding);
        }
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
        var fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd != _trackedHwnd && fgHwnd != _borderHwnd)
        {
            _trackedHwnd = fgHwnd;
            UpdateBorderPositionDirect();
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

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.MediumPurple;
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
