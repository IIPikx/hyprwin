using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HyprWin.Core;
using HyprWin.Core.Configuration;
using HyprWin.Core.Interop;

namespace HyprWin.App;

/// <summary>
/// Custom top bar window that replaces the native taskbar.
/// Displays workspace indicators (left), clock (center), and system metrics (right).
/// One instance is created per monitor.
/// </summary>
public partial class TopBarWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly WorkspaceManager _workspaceManager;
    private HyprWinConfig _config;
    private string _configPath = "";

    private DispatcherTimer? _clockTimer;
    private SystemInfoService? _sysInfo;
    private TrayIconService? _trayIconService;

    public TopBarWindow(MonitorInfo monitor, HyprWinConfig config, WorkspaceManager workspaceManager, SystemInfoService sysInfo, TrayIconService? trayIconService = null)
    {
        InitializeComponent();

        _monitor = monitor;
        _config = config;
        _workspaceManager = workspaceManager;
        _sysInfo = sysInfo;
        _trayIconService = trayIconService;

        ApplyConfig(config);
        PositionOnMonitor();

        SetupTimers();
        _sysInfo.MetricsUpdated += OnMetricsUpdated;
        if (_trayIconService != null)
            _trayIconService.IconsUpdated += OnTrayIconsUpdated;
        UpdateWorkspaceIndicators();
    }

    // WndProc constants for intercepting minimize / z-order changes
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND        = 0x0112;
    private const int SC_MINIMIZE           = 0xF020;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Install WndProc hook to block minimize and z-order demotion
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        // WS_EX_TOOLWINDOW prevents it from appearing in Alt+Tab
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | (int)NativeMethods.WS_EX_TOOLWINDOW);

        // Keep on top
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Low-level message hook: prevents "Show Desktop" from minimising or
    /// pushing the TopBar behind the desktop.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_SYSCOMMAND:
                // Block SC_MINIMIZE (triggered by Show Desktop)
                if ((wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;

            case WM_WINDOWPOSCHANGING:
                // Prevent any z-order change that would push us below HWND_TOPMOST
                var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<WINDOWPOS>(lParam);
                pos.hwndInsertAfter = NativeMethods.HWND_TOPMOST;
                System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
                break;
        }
        return IntPtr.Zero;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // When the TopBar loses focus (e.g. desktop click), immediately
        // re-assert topmost + visible to stay on screen.
        if (_barHiddenByFullscreen) return;

        Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        // Never allow the TopBar to be minimized
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        base.OnStateChanged(e);
    }

    /// <summary>
    /// Apply configuration to all visual elements.
    /// </summary>
    public void ApplyConfig(HyprWinConfig config)
    {
        _config = config;
        var theme = config.Theme;
        var bar = config.TopBar;

        // Bar background
        BarBorder.Background = BrushFromHex(theme.TopBarBg);

        // Font
        var fontFamily = new FontFamily(bar.Font + ", Segoe UI"); // Fallback
        var fontSize = (double)bar.FontSize;
        var fgBrush = BrushFromHex(theme.TopBarFg);
        var accentBrush = BrushFromHex(theme.TopBarAccent);

        // Apply to clock
        ClockText.FontFamily = fontFamily;
        ClockText.FontSize = fontSize;
        ClockText.Foreground = fgBrush;

        // Apply to system modules
        foreach (var tb in new[] { CpuText, CpuTempText, GpuText, GpuTempText, MemoryText, VolumeText, NetworkText })
        {
            tb.FontFamily = fontFamily;
            tb.FontSize = fontSize;
            tb.Foreground = fgBrush;
        }

        // Show/hide modules based on config
        var rightModules = bar.ModulesRight.Modules;
        bool showTray = rightModules.Contains("tray");
        TrayIcons.Visibility      = showTray ? Visibility.Visible : Visibility.Collapsed;
        TraySeparator.Visibility  = showTray ? Visibility.Visible : Visibility.Collapsed;
        TraySeparator.Foreground  = fgBrush;
        TraySeparator.FontFamily  = fontFamily;
        TraySeparator.FontSize    = fontSize;
        CpuText.Visibility     = rightModules.Contains("cpu")      ? Visibility.Visible : Visibility.Collapsed;
        CpuTempText.Visibility = rightModules.Contains("cpu_temp") ? Visibility.Visible : Visibility.Collapsed;
        GpuText.Visibility     = rightModules.Contains("gpu")      ? Visibility.Visible : Visibility.Collapsed;
        GpuTempText.Visibility = rightModules.Contains("gpu_temp") ? Visibility.Visible : Visibility.Collapsed;
        MemoryText.Visibility  = rightModules.Contains("memory")   ? Visibility.Visible : Visibility.Collapsed;
        VolumeText.Visibility  = rightModules.Contains("volume")   ? Visibility.Visible : Visibility.Collapsed;
        NetworkText.Visibility = rightModules.Contains("network")  ? Visibility.Visible : Visibility.Collapsed;

        // Buttons
        WindowsMenuButton.Foreground = fgBrush;
        TaskManagerButton.Foreground = fgBrush;
        TaskManagerButton.FontFamily = fontFamily;
        SystemMenuButton.Foreground = fgBrush;
        SystemMenuButton.FontFamily = fontFamily;
        SettingsButton.Foreground = fgBrush;
        SettingsButton.FontFamily = fontFamily;

        // Height
        Height = bar.Height;

        PositionOnMonitor();
        UpdateWorkspaceIndicators();
        UpdateClock();
    }

    private void PositionOnMonitor()
    {
        var workArea = _monitor.WorkArea;

        if (_config.TopBar.Position.Equals("bottom", StringComparison.OrdinalIgnoreCase))
        {
            Left = workArea.Left;
            Top = workArea.Bottom - _config.TopBar.Height;
        }
        else
        {
            Left = workArea.Left;
            Top = workArea.Top;
        }
        Width = workArea.Width;
    }

    // ──────────────── Timers ────────────────

    private DispatcherTimer? _fullscreenTimer;
    private bool _barHiddenByFullscreen;

    private void SetupTimers()
    {
        // Clock update — every second
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        // Fullscreen detection — every 750 ms
        _fullscreenTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _fullscreenTimer.Start();

        // System metrics are driven by SystemInfoService.MetricsUpdated events.
    }

    // ──────────────── Fullscreen detection ────────────────

    private void CheckFullscreen()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) { RestoreBarIfNeeded(); return; }

        // Ignore our own window handle — but still restore the bar if it was hidden
        var ownHwnd = new WindowInteropHelper(this).Handle;
        if (fg == ownHwnd) { RestoreBarIfNeeded(); return; }

        // Compare foreground window rect (physical pixels) against this monitor's full bounds
        if (!NativeMethods.GetWindowRect(fg, out var r)) { RestoreBarIfNeeded(); return; }

        var b = _monitor.Bounds;
        bool coversMonitor = r.Left <= b.Left && r.Top <= b.Top
                          && r.Right >= b.Right && r.Bottom >= b.Bottom;

        // A window that merely fills rcWork (maximized) is NOT fullscreen — it must cover
        // the very top edge where the top bar lives.
        if (coversMonitor && r.Top <= b.Top)
        {
            if (!_barHiddenByFullscreen)
            {
                _barHiddenByFullscreen = true;
                Visibility = Visibility.Hidden;
            }
        }
        else
        {
            RestoreBarIfNeeded();
        }
    }

    private void RestoreBarIfNeeded()
    {
        if (!_barHiddenByFullscreen) return;
        _barHiddenByFullscreen = false;
        Visibility = Visibility.Visible;
        // Re-assert HWND_TOPMOST in case another window took over
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // ──────────────── Clock ────────────────

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var clockConfig = _config.TopBar.Clock;

        string time = now.ToString(clockConfig.Format);
        if (clockConfig.ShowDate)
        {
            string date = now.ToString(clockConfig.DateFormat);
            ClockText.Text = $"{time}  |  {date}";
        }
        else
        {
            ClockText.Text = time;
        }
    }

    // ──────────────── System Metrics ────────────────

    private void OnMetricsUpdated(SystemMetrics m)
    {
        Dispatcher.Invoke(() => ApplySystemMetrics(m));
    }

    private void ApplySystemMetrics(SystemMetrics m)
    {
        if (CpuText.Visibility == Visibility.Visible)
            CpuText.Text = $" CPU {m.CpuUsagePct:F0}%";

        if (CpuTempText.Visibility == Visibility.Visible)
            CpuTempText.Text = m.CpuTempC > 0 ? $" {m.CpuTempC:F0}°C" : " --°C";

        if (GpuText.Visibility == Visibility.Visible)
            GpuText.Text = m.GpuUsagePct >= 0 ? $" GPU {m.GpuUsagePct:F0}%" : " GPU --";

        if (GpuTempText.Visibility == Visibility.Visible)
            GpuTempText.Text = m.GpuTempC > 0 ? $" {m.GpuTempC:F0}°C" : " --°C";

        if (MemoryText.Visibility == Visibility.Visible)
            MemoryText.Text = $" RAM {m.RamUsagePct:F0}%";

        if (VolumeText.Visibility == Visibility.Visible)
        {
            string icon = m.IsMuted ? "🔇" : "🔊";
            VolumeText.Text = $" {icon} {m.Volume}%";
        }

        if (NetworkText.Visibility == Visibility.Visible)
        {
            string down = FormatBytesRate(m.NetDownBytesPerSec);
            string up   = FormatBytesRate(m.NetUpBytesPerSec);
            NetworkText.Text = $" ↓{down} ↑{up}";
        }
    }

    private static string FormatBytesRate(long bytesPerSec)
    {
        if (bytesPerSec < 1024)
            return $"{bytesPerSec} B/s";
        if (bytesPerSec < 1024 * 1024)
            return $"{bytesPerSec / 1024.0:F0} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):F1} MB/s";
    }

    // ──────────────── Workspace Indicators ────────────────

    public void UpdateWorkspaceIndicators()
    {
        var wsConfig = _config.TopBar.Workspaces;
        var accentBrush = BrushFromHex(_config.Theme.TopBarAccent);
        var fgBrush = BrushFromHex(_config.Theme.TopBarFg);
        var fontFamily = new FontFamily(_config.TopBar.Font + ", Segoe UI");

        int activeWsIndex = _workspaceManager.GetActiveWorkspaceIndex(_monitor.Index);
        int count = wsConfig.ShowCount;

        var items = new List<WorkspaceItem>();
        for (int i = 0; i < count; i++)
        {
            bool isActive = i == activeWsIndex;
            items.Add(new WorkspaceItem
            {
                Index = i,
                Indicator = isActive ? wsConfig.ActiveIndicator : wsConfig.InactiveIndicator,
                Foreground = isActive ? accentBrush : fgBrush,
                FontSize = _config.TopBar.FontSize,
                FontFamily = fontFamily,
            });
        }

        WorkspaceIndicators.ItemsSource = items;
    }

    public void SetConfigPath(string path) => _configPath = path;

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int wsIndex)
        {
            _workspaceManager.SwitchWorkspace(_monitor.Index, wsIndex);
            UpdateWorkspaceIndicators();
        }
    }

    // ──────────────── System Tray Icons ────────────────

    private void OnTrayIconsUpdated(List<TrayIconInfo> icons)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                foreach (var icon in icons)
                    icon.IconImage ??= TrayIconService.IconToImageSource(icon.IconHandle);

                // Only update if icon set actually changed (avoid flicker)
                var filtered = icons.Where(i => i.IconImage != null).ToList();
                TrayIcons.ItemsSource = filtered;
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("Failed to update tray icons in TopBar", ex);
            }
        });
    }

    private void TrayIcon_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is TrayIconInfo icon)
            TrayIconService.SendIconClick(icon, rightClick: false);
    }

    private void TrayIcon_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is TrayIconInfo icon)
            TrayIconService.SendIconClick(icon, rightClick: true);
        e.Handled = true; // Prevent TopBar context menu from showing
    }

    private void BarBorder_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;

        var menu = new ContextMenu();

        var exitItem = new MenuItem { Header = "HyprWin schließen" };
        // Use BeginInvoke so the context menu fully closes before Shutdown() runs,
        // preventing "Cannot set Visibility while a Window is closing" races.
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () => Application.Current.Shutdown());

        menu.Items.Add(exitItem);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void WindowsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Simulate a single Win key press+release to open the Start menu
            HyprWin.Core.Interop.NativeMethods.keybd_event((byte)HyprWin.Core.Interop.NativeMethods.VK_LWIN, 0, 0, UIntPtr.Zero);
            HyprWin.Core.Interop.NativeMethods.keybd_event((byte)HyprWin.Core.Interop.NativeMethods.VK_LWIN, 0, HyprWin.Core.Interop.NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to open Windows menu", ex);
        }
    }

    private void TaskManagerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to launch Task Manager", ex);
        }
    }

    private void SystemMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sysInfo == null) return;
        try
        {
            var btn = (Button)sender;

            // PointToScreen returns physical (device) pixels.
            // Window.Left/Top expects logical WPF pixels.
            // Retrieve the DPI scale of this window to convert correctly.
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            // Bottom-right corner of the button in physical pixels
            var physBR = btn.PointToScreen(new Point(btn.ActualWidth, btn.ActualHeight));

            // Convert to logical pixels and anchor right edge of menu to right edge of button
            const double menuWidth = 300;
            double logicalRight = physBR.X / dpiX;
            double logicalTop   = physBR.Y / dpiY + 4;

            var menu = new SystemMenuWindow(_sysInfo)
            {
                Left = logicalRight - menuWidth,
                Top  = logicalTop
            };
            menu.Show();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to open system menu", ex);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsWindow = new SettingsWindow(_configPath, _config);
            settingsWindow.Show();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to open settings window", ex);
        }
    }

    // ──────────────── Calendar popup ────────────────

    private void ClockText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            // Get DPI scale of this window
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            // Bottom-center of the ClockText in physical pixels
            var physPt = ClockText.PointToScreen(
                new Point(ClockText.ActualWidth / 2, ClockText.ActualHeight));

            // Convert to logical (WPF) pixels
            const double calWidth = 252;
            double logicalX = physPt.X / dpiX;
            double logicalY = physPt.Y / dpiY + 6;

            var cal = new CalendarPopupWindow
            {
                Left = logicalX - calWidth / 2,
                Top  = logicalY
            };
            cal.Show();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to open calendar", ex);
        }
    }

    // ──────────────── Helpers ────────────────

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.White;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _clockTimer?.Stop();
        _fullscreenTimer?.Stop();
        if (_sysInfo != null)
            _sysInfo.MetricsUpdated -= OnMetricsUpdated;
        if (_trayIconService != null)
            _trayIconService.IconsUpdated -= OnTrayIconsUpdated;
        base.OnClosed(e);
    }
}

/// <summary>
/// Data model for workspace indicator items.
/// </summary>
public class WorkspaceItem
{
    public int Index { get; set; }
    public string Indicator { get; set; } = "○";
    public Brush Foreground { get; set; } = Brushes.White;
    public double FontSize { get; set; } = 12;
    public FontFamily FontFamily { get; set; } = new("Segoe UI");
}
