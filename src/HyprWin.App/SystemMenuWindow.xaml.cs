using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HyprWin.Core;
using HyprWin.Core.Interop;

namespace HyprWin.App;

/// <summary>
/// macOS Control Center-style system menu popup.
/// Shows: Now Playing controls · Volume slider · Network status · Bluetooth toggle.
/// Positioned below the system-menu button in the top bar.
/// </summary>
public partial class SystemMenuWindow : Window
{
    private readonly SystemInfoService _sysInfo;
    private bool _suppressVolumeChange;

    public SystemMenuWindow(SystemInfoService sysInfo)
    {
        InitializeComponent();
        _sysInfo = sysInfo;

        // Populate immediately with current snapshot
        ApplyMetrics(sysInfo.Current);

        // Subscribe to live updates
        sysInfo.MetricsUpdated += OnMetricsUpdated;

        // Auto-close when focus leaves the window
        Deactivated += (_, _) => Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Tool window — no taskbar entry, no Alt+Tab
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | (int)NativeMethods.WS_EX_TOOLWINDOW | (int)NativeMethods.WS_EX_NOACTIVATE);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    // ── Metrics update ────────────────────────────────────────────────────────

    private void OnMetricsUpdated(SystemMetrics m)
    {
        Dispatcher.Invoke(() => ApplyMetrics(m));
    }

    private void ApplyMetrics(SystemMetrics m)
    {
        // ── Media ──
        if (m.HasMedia)
        {
            MediaTitle.Text  = string.IsNullOrEmpty(m.MediaTitle)  ? "Unknown title"  : m.MediaTitle;
            MediaArtist.Text = string.IsNullOrEmpty(m.MediaArtist) ? ""               : m.MediaArtist;
            BtnPlayPause.Content = m.MediaPlaying ? "⏸" : "▶";
        }
        else
        {
            MediaTitle.Text  = "Nothing playing";
            MediaArtist.Text = "";
            BtnPlayPause.Content = "▶";
        }

        // ── Volume ──
        _suppressVolumeChange = true;
        if (m.Volume >= 0)
        {
            VolumeSlider.Value = m.Volume;
            VolumeLabel.Text   = $"{m.Volume}%";
            BtnMute.Content    = m.IsMuted ? "🔇" : (m.Volume > 50 ? "🔊" : m.Volume > 0 ? "🔉" : "🔈");
        }
        else
        {
            VolumeLabel.Text = "--";
        }
        _suppressVolumeChange = false;

        // ── Network ──
        if (m.NetConnected)
        {
            NetName.Text = m.NetName;
            NetIcon.Text = m.IsWifi ? "📶" : "🌐";
            NetType.Text = m.IsWifi ? "Wi-Fi" : "Ethernet";
        }
        else
        {
            NetName.Text = "Not connected";
            NetIcon.Text = "🌐";
            NetType.Text = "";
        }

        // ── Bluetooth ──
        if (m.BtAvailable)
        {
            BtStatus.Text        = m.BtEnabled ? "On" : "Off";
            BtnBluetooth.Content = m.BtEnabled ? "ON"  : "OFF";
            BtnBluetooth.Background = m.BtEnabled
                ? new SolidColorBrush(Color.FromRgb(0x89, 0xb4, 0xfa))
                : new SolidColorBrush(Color.FromArgb(0x40, 0x58, 0x5b, 0x70));
            BtnBluetooth.Foreground = m.BtEnabled
                ? new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x2e))
                : new SolidColorBrush(Color.FromRgb(0xcd, 0xd6, 0xf4));
        }
        else
        {
            BtStatus.Text        = "Unavailable";
            BtnBluetooth.Content = "N/A";
            BtnBluetooth.IsEnabled = false;
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.MediaPreviousAsync();

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.MediaPlayPauseAsync();

    private void BtnNext_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.MediaNextAsync();

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        AudioManager.ToggleMute();
        BtnMute.Content = AudioManager.IsMuted() ? "🔇" : "🔊";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeChange) return;
        int vol = (int)e.NewValue;
        AudioManager.SetVolume(vol);
        VolumeLabel.Text = $"{vol}%";
    }

    private void BtnBluetooth_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.ToggleBluetoothAsync();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _sysInfo.MetricsUpdated -= OnMetricsUpdated;
        base.OnClosed(e);
    }
}
