using System.Diagnostics;
using System.Net.NetworkInformation;

namespace HyprWin.Core;

/// <summary>
/// Snapshot of all system metrics used by the top bar and system menu.
/// Immutable — a new instance is created on every poll cycle.
/// </summary>
public sealed class SystemMetrics
{
    // CPU / Memory
    public float   CpuUsagePct  { get; init; }
    public float   RamUsagePct  { get; init; }
    public ulong   RamUsedMb    { get; init; }
    public ulong   RamTotalMb   { get; init; }

    // GPU
    public float   GpuUsagePct  { get; init; }

    // Temperatures
    public float   CpuTempC     { get; init; }
    public float   GpuTempC     { get; init; }
    public bool    HasTemp       { get; init; }

    // Audio
    public int     Volume        { get; init; }  // 0-100, -1 = unavailable
    public bool    IsMuted       { get; init; }

    // Network
    public bool    NetConnected  { get; init; }
    public string  NetName       { get; init; } = "";
    public bool    IsWifi        { get; init; }

    // Media (populated via async refresh, may lag one cycle)
    public bool    HasMedia      { get; init; }
    public string  MediaTitle    { get; init; } = "";
    public string  MediaArtist   { get; init; } = "";
    public bool    MediaPlaying  { get; init; }

    // Bluetooth
    public bool    BtAvailable   { get; init; }
    public bool    BtEnabled     { get; init; }
}

/// <summary>
/// Background service that polls hardware metrics and fires <see cref="MetricsUpdated"/>
/// every two seconds. Media and Bluetooth state are queried via WinRT asynchronously.
/// Subscribers must Dispatcher.Invoke when touching UI elements.
/// </summary>
public sealed class SystemInfoService : IDisposable
{
    // ── Hardware providers ────────────────────────────────────────────────────
    private readonly HardwareMonitor    _hwMon   = new();
    private PerformanceCounter?         _cpuCounter;
    private bool                        _firstCpuRead = true;

    // ── Timers ────────────────────────────────────────────────────────────────
    private System.Threading.Timer?     _pollTimer;
    private bool                        _disposed;

    // ── Latest snapshot (thread-safe via volatile reference swap) ────────────
    private volatile SystemMetrics _metrics = new();
    public  SystemMetrics Current => _metrics;

    /// <summary>Fired (on a background thread) whenever the metrics snapshot is refreshed.</summary>
    public event Action<SystemMetrics>? MetricsUpdated;

    // ── Async media/BT state (latches from last WinRT query) ─────────────────
    private bool   _btAvailable;
    private bool   _btEnabled;
    private bool   _hasMedia;
    private string _mediaTitle   = "";
    private string _mediaArtist  = "";
    private bool   _mediaPlaying;

    // ─────────────────────────────────────────────────────────────────────────

    public void Start()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // first call always 0 - discard
        }
        catch (Exception ex) { Logger.Instance.Warn($"CPU counter unavailable: {ex.Message}"); }

        _hwMon.Initialize();

        // Poll every 2 s; WinRT queries run async fire-and-forget
        _pollTimer = new System.Threading.Timer(_ => Poll(), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        // Initial async queries
        _ = RefreshMediaAsync();
        _ = RefreshBluetoothAsync();

        Logger.Instance.Info("SystemInfoService started");
    }

    // ── Core polling ──────────────────────────────────────────────────────────

    private void Poll()
    {
        if (_disposed) return;
        try
        {
            _hwMon.Update();

            float cpu = ReadCpu();
            (float ramPct, ulong usedMb, ulong totalMb) = ReadMemory();

            (bool netConn, string netName, bool isWifi) = ReadNetwork();
            int  volume  = AudioManager.GetVolume();
            bool muted   = AudioManager.IsMuted();

            var snap = new SystemMetrics
            {
                CpuUsagePct = cpu,
                RamUsagePct = ramPct,
                RamUsedMb   = usedMb,
                RamTotalMb  = totalMb,
                GpuUsagePct = _hwMon.GpuLoadPct,
                CpuTempC    = _hwMon.CpuTempC,
                GpuTempC    = _hwMon.GpuTempC,
                HasTemp     = _hwMon.IsAvailable,
                Volume      = volume,
                IsMuted     = muted,
                NetConnected = netConn,
                NetName     = netName,
                IsWifi      = isWifi,
                HasMedia    = _hasMedia,
                MediaTitle  = _mediaTitle,
                MediaArtist = _mediaArtist,
                MediaPlaying = _mediaPlaying,
                BtAvailable = _btAvailable,
                BtEnabled   = _btEnabled,
            };

            _metrics = snap;
            MetricsUpdated?.Invoke(snap);

            // Refresh async state every ~10 s (every 5th poll)
            if (++_asyncRefreshCounter >= 5)
            {
                _asyncRefreshCounter = 0;
                _ = RefreshMediaAsync();
                _ = RefreshBluetoothAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Debug($"SystemInfoService.Poll error: {ex.Message}");
        }
    }

    private int _asyncRefreshCounter;

    // ── Hardware readers ─────────────────────────────────────────────────────

    private float ReadCpu()
    {
        try
        {
            if (_cpuCounter == null) return 0;
            if (_firstCpuRead) { _firstCpuRead = false; return 0; }
            return _cpuCounter.NextValue();
        }
        catch { return 0; }
    }

    private static (float pct, ulong usedMb, ulong totalMb) ReadMemory()
    {
        try
        {
            var mem = new Interop.NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<
                    Interop.NativeMethods.MEMORYSTATUSEX>()
            };
            if (!Interop.NativeMethods.GlobalMemoryStatusEx(ref mem)) return (0, 0, 0);
            ulong totalMb = mem.ullTotalPhys / 1024 / 1024;
            ulong freeMb  = mem.ullAvailPhys  / 1024 / 1024;
            ulong usedMb  = totalMb - freeMb;
            float pct     = mem.dwMemoryLoad;
            return (pct, usedMb, totalMb);
        }
        catch { return (0, 0, 0); }
    }

    private static (bool connected, string name, bool isWifi) ReadNetwork()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                string name = ni.Name;

                // For WiFi, try to extract SSID from description
                if (wifi && ni.Description.Length > 0)
                    name = ni.Description;

                return (true, name, wifi);
            }
            return (false, "", false);
        }
        catch { return (false, "", false); }
    }

    // ── WinRT async queries ───────────────────────────────────────────────────

    private async Task RefreshMediaAsync()
    {
        try
        {
            var sessionManager = await Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = sessionManager.GetCurrentSession();

            if (session == null)
            {
                _hasMedia = false;
                return;
            }

            var props    = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();

            _hasMedia    = true;
            _mediaTitle  = props.Title  ?? "";
            _mediaArtist = props.Artist ?? "";
            _mediaPlaying = playback.PlaybackStatus ==
                Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            _hasMedia = false;
            _mediaTitle = _mediaArtist = "";
        }
    }

    private async Task RefreshBluetoothAsync()
    {
        try
        {
            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            var bt = radios.FirstOrDefault(r =>
                r.Kind == Windows.Devices.Radios.RadioKind.Bluetooth);

            _btAvailable = bt != null;
            _btEnabled   = bt?.State == Windows.Devices.Radios.RadioState.On;
        }
        catch
        {
            _btAvailable = false;
            _btEnabled   = false;
        }
    }

    // ── Commands (called from UI) ─────────────────────────────────────────────

    public async Task ToggleBluetoothAsync()
    {
        try
        {
            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            var bt = radios.FirstOrDefault(r =>
                r.Kind == Windows.Devices.Radios.RadioKind.Bluetooth);
            if (bt == null) return;

            var target = bt.State == Windows.Devices.Radios.RadioState.On
                ? Windows.Devices.Radios.RadioState.Off
                : Windows.Devices.Radios.RadioState.On;

            await bt.SetStateAsync(target);
            _btEnabled = target == Windows.Devices.Radios.RadioState.On;
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Bluetooth toggle failed: {ex.Message}");
        }
    }

    public async Task MediaPlayPauseAsync()
    {
        try
        {
            var mgr = await Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var s = mgr.GetCurrentSession();
            if (s == null) return;
            if (_mediaPlaying) await s.TryPauseAsync();
            else               await s.TryPlayAsync();
        }
        catch { }
    }

    public async Task MediaPreviousAsync()
    {
        try
        {
            var mgr = await Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = mgr.GetCurrentSession();
            if (session != null) await session.TrySkipPreviousAsync();
        }
        catch { }
    }

    public async Task MediaNextAsync()
    {
        try
        {
            var mgr = await Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = mgr.GetCurrentSession();
            if (session != null) await session.TrySkipNextAsync();
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        _cpuCounter?.Dispose();
        _hwMon.Dispose();
        Logger.Instance.Info("SystemInfoService disposed");
    }
}
