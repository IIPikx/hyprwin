using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private DispatcherTimer? _systemTimer;

    // System metrics
    private PerformanceCounter? _cpuCounter;

    public TopBarWindow(MonitorInfo monitor, HyprWinConfig config, WorkspaceManager workspaceManager)
    {
        InitializeComponent();

        _monitor = monitor;
        _config = config;
        _workspaceManager = workspaceManager;

        ApplyConfig(config);
        PositionOnMonitor();

        SetupTimers();
        SetupSystemCounters();
        UpdateWorkspaceIndicators();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Make the bar click-through for non-interactive areas
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        // Don't set WS_EX_TRANSPARENT — we want clicks on buttons to work
        // Instead, the WS_EX_TOOLWINDOW prevents it from appearing in Alt+Tab
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | (int)NativeMethods.WS_EX_TOOLWINDOW);

        // Keep on top
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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
        CpuText.FontFamily = fontFamily;
        CpuText.FontSize = fontSize;
        CpuText.Foreground = fgBrush;

        MemoryText.FontFamily = fontFamily;
        MemoryText.FontSize = fontSize;
        MemoryText.Foreground = fgBrush;

        VolumeText.FontFamily = fontFamily;
        VolumeText.FontSize = fontSize;
        VolumeText.Foreground = fgBrush;

        // Show/hide modules based on config
        var rightModules = bar.ModulesRight.Modules;
        CpuText.Visibility = rightModules.Contains("cpu") ? Visibility.Visible : Visibility.Collapsed;
        MemoryText.Visibility = rightModules.Contains("memory") ? Visibility.Visible : Visibility.Collapsed;
        VolumeText.Visibility = rightModules.Contains("volume") ? Visibility.Visible : Visibility.Collapsed;

        // Settings button
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

    private void SetupTimers()
    {
        // Clock update — every second
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        // System metrics — every 2 seconds
        _systemTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _systemTimer.Tick += (_, _) => UpdateSystemMetrics();
        _systemTimer.Start();
    }

    private void SetupSystemCounters()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // First call always returns 0
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Failed to initialize CPU counter: {ex.Message}");
        }
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

    private void UpdateSystemMetrics()
    {
        // CPU
        if (CpuText.Visibility == Visibility.Visible)
        {
            try
            {
                float cpu = _cpuCounter?.NextValue() ?? 0;
                CpuText.Text = $"  {cpu:F0}%";
            }
            catch
            {
                CpuText.Text = "  --";
            }
        }

        // Memory
        if (MemoryText.Visibility == Visibility.Visible)
        {
            try
            {
                var memStatus = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
                if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
                {
                    MemoryText.Text = $"  {memStatus.dwMemoryLoad}%";
                }
            }
            catch
            {
                MemoryText.Text = "  --";
            }
        }

        // Volume (simplified — just show a static indicator for now)
        if (VolumeText.Visibility == Visibility.Visible)
        {
            VolumeText.Text = " 墳";
        }
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
        _systemTimer?.Stop();
        _cpuCounter?.Dispose();
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
