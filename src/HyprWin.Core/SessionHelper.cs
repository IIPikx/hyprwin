using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Utility class to inspect the current Windows session context.
/// </summary>
public static class SessionHelper
{
    /// <summary>
    /// Returns <c>true</c> when the calling process is running inside a
    /// Remote Desktop (RDP) or Terminal Services client session.
    /// Uses <c>GetSystemMetrics(SM_REMOTESESSION)</c> which is the official
    /// Win32 API for this check — it covers RDP, RemoteFX and thin clients.
    /// </summary>
    public static bool IsRemoteSession()
        => NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;
}
