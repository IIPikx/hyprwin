using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HyprWin.Core;
using HyprWin.Core.Interop;

namespace HyprWin.App;

/// <summary>
/// macOS Control Center-style system menu popup.
/// Shows: Quick toggles · Now Playing · Brightness · Volume · Network · Battery · Power.
/// </summary>
public partial class SystemMenuWindow : Window
{
    private readonly SystemInfoService _sysInfo;
    private bool _suppressVolumeChange;
    private bool _suppressBrightnessChange;
    private bool _closing;

    private static readonly SolidColorBrush ActiveToggleBg = Freeze(new SolidColorBrush(Color.FromRgb(0x89, 0xb4, 0xfa)));
    private static readonly SolidColorBrush InactiveToggleBg = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0x31, 0x32, 0x44)));
    private static readonly SolidColorBrush ActiveFg = Freeze(new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x2e)));
    private static readonly SolidColorBrush InactiveFg = Freeze(new SolidColorBrush(Color.FromRgb(0xa6, 0xad, 0xc8)));

    public SystemMenuWindow(SystemInfoService sysInfo)
    {
        InitializeComponent();
        _sysInfo = sysInfo;

        ApplyMetrics(sysInfo.Current);
        sysInfo.MetricsUpdated += OnMetricsUpdated;
        Deactivated += (_, _) => { if (!_closing) { _closing = true; Close(); } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | (int)NativeMethods.WS_EX_TOOLWINDOW | (int)NativeMethods.WS_EX_NOACTIVATE);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    private void OnMetricsUpdated(SystemMetrics m)
    {
        Dispatcher.Invoke(() => ApplyMetrics(m));
    }

    private void ApplyMetrics(SystemMetrics m)
    {
        // Media
        if (m.HasMedia)
        {
            MediaTitle.Text = string.IsNullOrEmpty(m.MediaTitle) ? "Unknown title" : m.MediaTitle;
            MediaArtist.Text = string.IsNullOrEmpty(m.MediaArtist) ? "" : m.MediaArtist;
            BtnPlayPause.Content = m.MediaPlaying ? "\uE769" : "\uE768"; // Pause : Play
        }
        else
        {
            MediaTitle.Text = "Nothing playing";
            MediaArtist.Text = "";
            BtnPlayPause.Content = "\uE768";
        }

        // Volume
        _suppressVolumeChange = true;
        if (m.Volume >= 0)
        {
            VolumeSlider.Value = m.Volume;
            VolumeLabel.Text = $"{m.Volume}%";
            BtnMute.Content = m.IsMuted ? "\uE74F" : (m.Volume > 50 ? "\uE767" : m.Volume > 0 ? "\uE993" : "\uE992");
        }
        else
        {
            VolumeLabel.Text = "--";
        }
        _suppressVolumeChange = false;

        // Brightness
        _suppressBrightnessChange = true;
        if (m.Brightness >= 0)
        {
            BrightnessSlider.Value = m.Brightness;
            BrightnessLabel.Text = $"{m.Brightness}%";
            BrightnessCard.Visibility = Visibility.Visible;
        }
        else
        {
            BrightnessCard.Visibility = Visibility.Collapsed;
        }
        _suppressBrightnessChange = false;

        // Network + WiFi toggle
        if (m.NetConnected)
        {
            NetName.Text = m.NetName;
            NetIcon.Text = m.IsWifi ? "\uE701" : "\uE839"; // WiFi : Ethernet
            NetType.Text = m.IsWifi ? "Wi-Fi" : "Ethernet";
            WifiIcon.Foreground = ActiveToggleBg;
        }
        else
        {
            NetName.Text = "Not connected";
            NetIcon.Text = "\uE774"; // Globe
            NetType.Text = "";
            WifiIcon.Foreground = InactiveFg;
        }

        // Bluetooth toggle
        if (m.BtAvailable)
        {
            BtIcon.Foreground = m.BtEnabled ? ActiveToggleBg : InactiveFg;
            BtLabel.Text = m.BtEnabled ? "Bluetooth" : "BT Off";
        }
        else
        {
            BtIcon.Foreground = InactiveFg;
            BtLabel.Text = "N/A";
        }

        // Battery
        if (m.HasBattery)
        {
            BatteryCard.Visibility = Visibility.Visible;
            BatteryPctText.Text = m.BatteryPct >= 0 ? $"{m.BatteryPct}%" : "--";
            BatteryStatus.Text = m.IsCharging ? "Charging" : "On battery";
            BatteryIcon.Text = m.IsCharging ? "\uEBAB" : GetBatteryIcon(m.BatteryPct);
            BatteryIcon.Foreground = m.BatteryPct <= 20 && !m.IsCharging
                ? Freeze(new SolidColorBrush(Color.FromRgb(0xf3, 0x8b, 0xa8)))
                : ActiveToggleBg;
        }
        else
        {
            BatteryCard.Visibility = Visibility.Collapsed;
        }
    }

    private static string GetBatteryIcon(int pct) => pct switch
    {
        >= 90 => "\uEBAD",
        >= 70 => "\uEBAC",
        >= 50 => "\uEBAB",
        >= 30 => "\uEBAA",
        >= 10 => "\uEBA9",
        _ => "\uEBA7"
    };

    // Quick Toggles
    private void BtnWifi_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:network-wifi", UseShellExecute = true }); }
        catch { }
    }

    private void BtnBluetooth_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.ToggleBluetoothAsync();

    private void BtnFocus_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:quiethours", UseShellExecute = true }); }
        catch { }
    }

    private void BtnNearby_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:crossdevice", UseShellExecute = true }); }
        catch { }
    }

    // Media
    private void BtnPrev_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.MediaPreviousAsync();

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.MediaPlayPauseAsync();

    private void BtnNext_Click(object sender, RoutedEventArgs e)
        => _ = _sysInfo.MediaNextAsync();

    // Volume
    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        AudioManager.ToggleMute();
        BtnMute.Content = AudioManager.IsMuted() ? "\uE74F" : "\uE767";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeChange) return;
        int vol = (int)e.NewValue;
        AudioManager.SetVolume(vol);
        VolumeLabel.Text = $"{vol}%";
    }

    // Brightness
    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressBrightnessChange) return;
        int brightness = (int)e.NewValue;
        _sysInfo.SetBrightness(brightness);
        BrightnessLabel.Text = $"{brightness}%";
    }

    // Quick Actions
    private void BtnDisplay_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:display", UseShellExecute = true }); }
        catch { }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:", UseShellExecute = true }); }
        catch { }
    }

    private void BtnPower_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:powersleep", UseShellExecute = true }); }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _sysInfo.MetricsUpdated -= OnMetricsUpdated;
        base.OnClosed(e);
    }

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
