using System.Runtime.InteropServices;

namespace HyprWin.Core;

/// <summary>
/// Reads and sets the system master volume via the Windows Core Audio COM API (WASAPI).
/// No external dependencies required.
/// </summary>
public static class AudioManager
{
    // ─── COM interface IDs ───────────────────────────────────────────────────

    private static readonly Guid IID_IAudioEndpointVolume =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private static readonly Guid CLSID_MMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    private const int CLSCTX_INPROC_SERVER = 1;
    private const int eRender  = 0;
    private const int eConsole = 0;

    // ─── COM interfaces (minimal surface needed) ─────────────────────────────

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask,
            [MarshalAs(UnmanagedType.Interface)] out object ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(int stgmAccess,
            [MarshalAs(UnmanagedType.Interface)] out object ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

        [PreserveSig]
        int GetState(out int pdwState);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out int pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        int VolumeStepUp(ref Guid pguidEventContext);
        int VolumeStepDown(ref Guid pguidEventContext);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB,
            out float pflVolumeIncrementdB);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    [ClassInterface(ClassInterfaceType.None)]
    private class MMDeviceEnumeratorCoClass { }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Returns master volume 0–100, or -1 on failure.</summary>
    public static int GetVolume()
    {
        try
        {
            var vol = GetVolumeInterface();
            if (vol == null) return -1;
            var g = Guid.Empty;
            vol.GetMasterVolumeLevelScalar(out float level);
            Marshal.ReleaseComObject(vol);
            return (int)Math.Round(level * 100);
        }
        catch { return -1; }
    }

    /// <summary>Sets master volume 0–100.</summary>
    public static void SetVolume(int percent)
    {
        try
        {
            var vol = GetVolumeInterface();
            if (vol == null) return;
            float level = Math.Clamp(percent / 100f, 0f, 1f);
            var g = Guid.Empty;
            vol.SetMasterVolumeLevelScalar(level, ref g);
            Marshal.ReleaseComObject(vol);
        }
        catch { }
    }

    /// <summary>Returns whether the default audio endpoint is muted.</summary>
    public static bool IsMuted()
    {
        try
        {
            var vol = GetVolumeInterface();
            if (vol == null) return false;
            vol.GetMute(out bool muted);
            Marshal.ReleaseComObject(vol);
            return muted;
        }
        catch { return false; }
    }

    /// <summary>Toggles mute state.</summary>
    public static void ToggleMute()
    {
        try
        {
            var vol = GetVolumeInterface();
            if (vol == null) return;
            vol.GetMute(out bool muted);
            var g = Guid.Empty;
            vol.SetMute(!muted, ref g);
            Marshal.ReleaseComObject(vol);
        }
        catch { }
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private static IAudioEndpointVolume? GetVolumeInterface()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
            enumerator.GetDefaultAudioEndpoint(eRender, eConsole, out var device);
            var iid = IID_IAudioEndpointVolume;
            device.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out var obj);
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(device);
            return obj as IAudioEndpointVolume;
        }
        catch { return null; }
    }
}
