using System.Runtime.InteropServices;
using System.Windows.Interop;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

public enum SwipeDirection { Left, Right, Up, Down }

/// <summary>
/// Detects multi-finger swipe gestures on Windows Precision Touchpads via Raw Input HID.
/// Registers for HID Digitizer/Touchpad raw input on a message-only window,
/// parses contact count and per-finger positions using HidP_* functions,
/// and tracks centroid movement to detect swipe gestures.
/// </summary>
public sealed class TouchpadGestureService : IDisposable
{
    private HwndSource? _hwndSource;
    private bool _disposed;

    private readonly Dictionary<IntPtr, IntPtr> _devicePreparsed = new();
    private readonly Dictionary<IntPtr, int> _deviceMaxLinkCollections = new();

    private int _contactCount;
    private bool _gestureActive;
    private double _lastCentroidX, _lastCentroidY;
    private double _accDx, _accDy;
    private long _gestureStartTick;

    public int RequiredContacts { get; set; } = 3;
    public double SwipeThreshold { get; set; } = 150;
    public bool IsEnabled { get; set; } = true;
    public bool IsAvailable { get; private set; }

    public event Action<SwipeDirection>? SwipeDetected;

    public void Start()
    {
        if (_hwndSource != null) return;

        var parameters = new HwndSourceParameters("HyprWinTouchpad")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        var rid = new NativeMethods.RAWINPUTDEVICE[]
        {
            new()
            {
                UsagePage = NativeMethods.HID_USAGE_PAGE_DIGITIZER,
                Usage = NativeMethods.HID_USAGE_DIGITIZER_TOUCH_PAD,
                Flags = (uint)NativeMethods.RIDEV_INPUTSINK,
                WindowHandle = _hwndSource.Handle,
            }
        };

        bool registered = NativeMethods.RegisterRawInputDevices(
            rid, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());

        IsAvailable = registered;

        if (registered)
            Logger.Instance.Info("Touchpad gesture service started (raw input registered)");
        else
            Logger.Instance.Info("No precision touchpad detected — gesture service inactive");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_INPUT && IsEnabled)
        {
            ProcessRawInput(lParam);
        }
        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr lParam)
    {
        try
        {
            uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            uint size = 0;

            NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint copied = NativeMethods.GetRawInputData(
                    lParam, NativeMethods.RID_INPUT, buffer, ref size, headerSize);
                if (copied == unchecked((uint)-1)) return;

                var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
                if (header.dwType != NativeMethods.RIM_TYPEHID) return;

                int hidOffset = (int)headerSize;
                uint dwSizeHid = (uint)Marshal.ReadInt32(buffer + hidOffset);
                uint dwCount = (uint)Marshal.ReadInt32(buffer + hidOffset + 4);

                if (dwSizeHid == 0 || dwCount == 0) return;

                IntPtr preparsed = GetOrCachePreparsedData(header.hDevice);
                if (preparsed == IntPtr.Zero) return;

                IntPtr reportsStart = buffer + hidOffset + 8;
                for (uint i = 0; i < dwCount; i++)
                {
                    byte[] report = new byte[dwSizeHid];
                    Marshal.Copy(reportsStart + (int)(i * dwSizeHid), report, 0, (int)dwSizeHid);
                    ProcessHidReport(preparsed, report, header.hDevice);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Debug($"Touchpad raw input error: {ex.Message}");
        }
    }

    private IntPtr GetOrCachePreparsedData(IntPtr hDevice)
    {
        if (_devicePreparsed.TryGetValue(hDevice, out var cached))
            return cached;

        uint ppSize = 0;
        NativeMethods.GetRawInputDeviceInfo(
            hDevice, NativeMethods.RIDI_PREPARSEDDATA, IntPtr.Zero, ref ppSize);
        if (ppSize == 0) return IntPtr.Zero;

        IntPtr preparsed = Marshal.AllocHGlobal((int)ppSize);
        uint result = NativeMethods.GetRawInputDeviceInfo(
            hDevice, NativeMethods.RIDI_PREPARSEDDATA, preparsed, ref ppSize);

        if (result == unchecked((uint)-1))
        {
            Marshal.FreeHGlobal(preparsed);
            return IntPtr.Zero;
        }

        int maxLc = 10;
        if (NativeMethods.HidP_GetCaps(preparsed, out var caps) == NativeMethods.HIDP_STATUS_SUCCESS)
        {
            maxLc = Math.Min((int)caps.NumberLinkCollectionNodes, 15);
            Logger.Instance.Debug(
                $"Touchpad device caps: {caps.NumberLinkCollectionNodes} link collections, " +
                $"report size={caps.InputReportByteLength}");
        }

        _devicePreparsed[hDevice] = preparsed;
        _deviceMaxLinkCollections[hDevice] = maxLc;
        return preparsed;
    }

    private void ProcessHidReport(IntPtr preparsed, byte[] report, IntPtr hDevice)
    {
        int status = NativeMethods.HidP_GetUsageValue(
            NativeMethods.HidP_Input,
            NativeMethods.HID_USAGE_PAGE_DIGITIZER, 0,
            NativeMethods.HID_USAGE_DIGITIZER_CONTACT_COUNT,
            out uint rawContactCount,
            preparsed, report, (uint)report.Length);

        if (status != NativeMethods.HIDP_STATUS_SUCCESS) return;

        int contacts = (int)rawContactCount;
        int maxLc = _deviceMaxLinkCollections.GetValueOrDefault(hDevice, 10);

        double sumX = 0, sumY = 0;
        int validContacts = 0;

        for (ushort lc = 1; lc <= maxLc && validContacts < contacts; lc++)
        {
            int xStatus = NativeMethods.HidP_GetUsageValue(
                NativeMethods.HidP_Input,
                NativeMethods.HID_USAGE_PAGE_GENERIC, lc,
                NativeMethods.HID_USAGE_GENERIC_X,
                out uint x, preparsed, report, (uint)report.Length);

            int yStatus = NativeMethods.HidP_GetUsageValue(
                NativeMethods.HidP_Input,
                NativeMethods.HID_USAGE_PAGE_GENERIC, lc,
                NativeMethods.HID_USAGE_GENERIC_Y,
                out uint y, preparsed, report, (uint)report.Length);

            if (xStatus == NativeMethods.HIDP_STATUS_SUCCESS &&
                yStatus == NativeMethods.HIDP_STATUS_SUCCESS)
            {
                sumX += x;
                sumY += y;
                validContacts++;
            }
        }

        UpdateGestureState(contacts, validContacts, sumX, sumY);
    }

    private void UpdateGestureState(int contacts, int validContacts, double sumX, double sumY)
    {
        if (contacts >= RequiredContacts && validContacts > 0)
        {
            double cx = sumX / validContacts;
            double cy = sumY / validContacts;

            if (!_gestureActive)
            {
                _gestureActive = true;
                _lastCentroidX = cx;
                _lastCentroidY = cy;
                _accDx = 0;
                _accDy = 0;
                _gestureStartTick = Environment.TickCount64;
            }
            else
            {
                _accDx += cx - _lastCentroidX;
                _accDy += cy - _lastCentroidY;
                _lastCentroidX = cx;
                _lastCentroidY = cy;
            }
        }
        else if (_gestureActive && contacts < RequiredContacts)
        {
            _gestureActive = false;
            long duration = Environment.TickCount64 - _gestureStartTick;

            if (duration < 2000)
            {
                double absDx = Math.Abs(_accDx);
                double absDy = Math.Abs(_accDy);

                if (absDx > SwipeThreshold || absDy > SwipeThreshold)
                {
                    SwipeDirection dir;
                    if (absDx > absDy)
                        dir = _accDx > 0 ? SwipeDirection.Right : SwipeDirection.Left;
                    else
                        dir = _accDy > 0 ? SwipeDirection.Down : SwipeDirection.Up;

                    Logger.Instance.Debug(
                        $"Touchpad {RequiredContacts}-finger swipe: {dir} " +
                        $"(dx={_accDx:F0}, dy={_accDy:F0}, dur={duration}ms)");
                    SwipeDetected?.Invoke(dir);
                }
            }

            _accDx = 0;
            _accDy = 0;
        }

        _contactCount = contacts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hwndSource?.Dispose();

        foreach (var pp in _devicePreparsed.Values)
            Marshal.FreeHGlobal(pp);
        _devicePreparsed.Clear();
    }
}
