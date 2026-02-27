using LibreHardwareMonitor.Hardware;

namespace HyprWin.Core;

/// <summary>
/// Wraps LibreHardwareMonitor to read CPU/GPU temperatures and GPU load.
/// Requires administrator privileges for full sensor access — degrades gracefully otherwise.
/// Call Initialize() once, then Update() periodically.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private Computer?  _computer;
    private bool       _initialized;
    private bool       _disposed;

    public float CpuTempC   { get; private set; }
    public float GpuTempC   { get; private set; }
    public float GpuLoadPct { get; private set; }

    public bool IsAvailable => _initialized;

    public void Initialize()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
            };
            _computer.Open();
            _initialized = true;
            Logger.Instance.Info("HardwareMonitor: LibreHardwareMonitor initialized");
        }
        catch (Exception ex)
        {
            _initialized = false;
            Logger.Instance.Warn($"HardwareMonitor unavailable (try running as admin for temps): {ex.Message}");
        }
    }

    /// <summary>
    /// Refresh sensor readings. Call from the metrics polling timer.
    /// </summary>
    public void Update()
    {
        if (!_initialized || _computer == null) return;

        try
        {
            float cpuTemp = 0, gpuTemp = 0, gpuLoad = 0;

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();

                // Some CPUs place core sensors in sub-hardware
                foreach (var sub in hardware.SubHardware)
                    sub.Update();

                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        foreach (var sensor in AllSensors(hardware))
                        {
                            if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                                continue;

                            var name = sensor.Name;
                            // Prefer "CPU Package" / "Tdie" (AMD) / "Tctl" as the canonical reading
                            if (name.Contains("Package",  StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Tdie",     StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Tctl",     StringComparison.OrdinalIgnoreCase))
                            {
                                cpuTemp = sensor.Value.Value;
                            }
                            else if (cpuTemp == 0)
                            {
                                cpuTemp = Math.Max(cpuTemp, sensor.Value.Value);
                            }
                        }
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        foreach (var sensor in AllSensors(hardware))
                        {
                            if (!sensor.Value.HasValue) continue;

                            if (sensor.SensorType == SensorType.Temperature)
                                gpuTemp = sensor.Value.Value;
                            else if (sensor.SensorType == SensorType.Load &&
                                     (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Equals("GPU",    StringComparison.OrdinalIgnoreCase)))
                                gpuLoad = sensor.Value.Value;
                        }
                        break;
                }
            }

            CpuTempC   = cpuTemp;
            GpuTempC   = gpuTemp;
            GpuLoadPct = gpuLoad;
        }
        catch (Exception ex)
        {
            Logger.Instance.Debug($"HardwareMonitor.Update error: {ex.Message}");
        }
    }

    private static IEnumerable<ISensor> AllSensors(IHardware hardware)
    {
        foreach (var s in hardware.Sensors)
            yield return s;
        foreach (var sub in hardware.SubHardware)
            foreach (var s in sub.Sensors)
                yield return s;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _computer?.Close(); } catch { }
    }
}
