using System.Diagnostics;
using System.Windows.Media;
using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Represents an active window animation (position interpolation).
/// </summary>
internal class ActiveAnimation
{
    public IntPtr WindowHandle { get; init; }
    public NativeMethods.RECT From { get; init; }
    public NativeMethods.RECT To { get; init; }
    public double DurationMs { get; init; }
    public Func<double, double> EasingFunction { get; init; } = Easing.EaseOutCubic;
    public Stopwatch Stopwatch { get; } = new();
    public bool IsComplete => Stopwatch.ElapsedMilliseconds >= DurationMs;
}

/// <summary>
/// Easing function implementations.
/// </summary>
public static class Easing
{
    public static double Linear(double t) => t;
    public static double EaseIn(double t) => t * t;
    public static double EaseOut(double t) => 1 - (1 - t) * (1 - t);
    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double Spring(double t)
    {
        double c4 = (2 * Math.PI) / 3;
        return t == 0 ? 0 : t == 1 ? 1 :
            Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    }

    public static Func<double, double> FromString(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "linear" => Linear,
            "ease_in" => EaseIn,
            "ease_out" => EaseOut,
            "ease_out_cubic" => EaseOutCubic,
            "spring" => Spring,
            _ => EaseOutCubic,
        };
    }
}

/// <summary>
/// Animates window position/size changes using CompositionTarget.Rendering for frame-synced updates.
/// Uses SetWindowPos to interpolate between source and target rectangles.
/// </summary>
public sealed class AnimationEngine : IDisposable
{
    private readonly List<ActiveAnimation> _animations = new();
    private readonly object _lock = new();
    private bool _isRendering;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public int MoveDurationMs { get; set; } = 120;
    public int OpenDurationMs { get; set; } = 200;
    public int CloseDurationMs { get; set; } = 150;
    public Func<double, double> EasingFunction { get; set; } = Easing.EaseOutCubic;

    /// <summary>
    /// Update animation parameters from config.
    /// </summary>
    public void UpdateFromConfig(Configuration.AnimationsConfig config)
    {
        IsEnabled = config.Enabled;
        MoveDurationMs = config.WindowMoveDurationMs;
        OpenDurationMs = config.WindowOpenDurationMs;
        CloseDurationMs = config.WindowCloseDurationMs;
        EasingFunction = Easing.FromString(config.Easing);
    }

    /// <summary>
    /// Animate a window from its current position to a target position.
    /// </summary>
    public void AnimateMove(IntPtr hwnd, NativeMethods.RECT from, NativeMethods.RECT to)
    {
        if (!IsEnabled || MoveDurationMs <= 0)
        {
            // Skip animation, apply directly
            TilingEngine.ApplyWindowPosition(hwnd, to);
            return;
        }

        // If rects are the same, skip
        if (from.Left == to.Left && from.Top == to.Top &&
            from.Right == to.Right && from.Bottom == to.Bottom)
            return;

        lock (_lock)
        {
            // Remove any existing animation for this window
            _animations.RemoveAll(a => a.WindowHandle == hwnd);

            var anim = new ActiveAnimation
            {
                WindowHandle = hwnd,
                From = from,
                To = to,
                DurationMs = MoveDurationMs,
                EasingFunction = EasingFunction,
            };
            anim.Stopwatch.Start();
            _animations.Add(anim);

            StartRendering();
        }
    }

    /// <summary>
    /// Animate a window opening (slide-in from offset).
    /// </summary>
    public void AnimateOpen(IntPtr hwnd, NativeMethods.RECT targetRect)
    {
        if (!IsEnabled || OpenDurationMs <= 0)
        {
            TilingEngine.ApplyWindowPosition(hwnd, targetRect);
            return;
        }

        // Slide in from 30px to the right
        var from = new NativeMethods.RECT(
            targetRect.Left + 30, targetRect.Top,
            targetRect.Right + 30, targetRect.Bottom);

        lock (_lock)
        {
            _animations.RemoveAll(a => a.WindowHandle == hwnd);
            var anim = new ActiveAnimation
            {
                WindowHandle = hwnd,
                From = from,
                To = targetRect,
                DurationMs = OpenDurationMs,
                EasingFunction = EasingFunction,
            };
            anim.Stopwatch.Start();
            _animations.Add(anim);
            StartRendering();
        }
    }

    private void StartRendering()
    {
        if (_isRendering) return;
        _isRendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_isRendering) return;
        _isRendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_animations.Count == 0)
            {
                StopRendering();
                return;
            }

            var completed = new List<ActiveAnimation>();

            foreach (var anim in _animations)
            {
                double elapsed = anim.Stopwatch.ElapsedMilliseconds;
                double t = Math.Min(elapsed / anim.DurationMs, 1.0);
                double ease = anim.EasingFunction(t);

                int x = Lerp(anim.From.Left, anim.To.Left, ease);
                int y = Lerp(anim.From.Top, anim.To.Top, ease);
                int r = Lerp(anim.From.Right, anim.To.Right, ease);
                int b = Lerp(anim.From.Bottom, anim.To.Bottom, ease);

                NativeMethods.SetWindowPos(anim.WindowHandle, IntPtr.Zero,
                    x, y, r - x, b - y,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

                if (t >= 1.0)
                {
                    completed.Add(anim);
                }
            }

            foreach (var done in completed)
            {
                done.Stopwatch.Stop();
                // Snap to final position
                NativeMethods.SetWindowPos(done.WindowHandle, IntPtr.Zero,
                    done.To.Left, done.To.Top, done.To.Width, done.To.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                _animations.Remove(done);
            }

            if (_animations.Count == 0)
                StopRendering();
        }
    }

    private static int Lerp(int a, int b, double t) => a + (int)((b - a) * t);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRendering();
        lock (_lock)
            _animations.Clear();
    }
}
