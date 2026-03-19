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
    public AnimationStyle Style { get; init; } = AnimationStyle.Slide;
    public int PopinPercent { get; init; } = 80;
    public long StartTick { get; init; }
    public bool IsComplete(long now) => now - StartTick >= (long)(DurationMs * Stopwatch.Frequency / 1000.0);
    public double GetProgress(long now)
    {
        double elapsed = (now - StartTick) * 1000.0 / Stopwatch.Frequency;
        return Math.Min(elapsed / DurationMs, 1.0);
    }
}

/// <summary>
/// Animation style — matches Hyprland's window animation styles.
/// </summary>
public enum AnimationStyle
{
    /// <summary>Slide in from offset (default Hyprland).</summary>
    Slide,
    /// <summary>Scale from center (popin X%). Hyprland default for windows.</summary>
    Popin,
    /// <summary>Fade in (opacity only — approximated via position snap).</summary>
    Fade,
}

/// <summary>
/// Cubic Bézier curve for custom animation easing.
/// Hyprland: bezier = NAME, X0, Y0, X1, Y1
/// Uses De Casteljau subdivision for fast, accurate evaluation.
/// </summary>
public sealed class CubicBezier
{
    private readonly double _x0, _y0, _x1, _y1;
    // Pre-sampled lookup table for O(1) evaluation (Hyprland does the same)
    private readonly double[] _lut;
    private const int LutSize = 256;

    public CubicBezier(double x0, double y0, double x1, double y1)
    {
        _x0 = Math.Clamp(x0, 0, 1);
        _y0 = y0; // Y can overshoot (spring/bounce effects)
        _x1 = Math.Clamp(x1, 0, 1);
        _y1 = y1;
        _lut = new double[LutSize + 1];
        BuildLut();
    }

    private void BuildLut()
    {
        for (int i = 0; i <= LutSize; i++)
        {
            double t = (double)i / LutSize;
            double x = SampleX(t);
            _lut[i] = x;
        }
    }

    private double SampleX(double t)
    {
        double mt = 1 - t;
        return 3 * mt * mt * t * _x0 + 3 * mt * t * t * _x1 + t * t * t;
    }

    private double SampleY(double t)
    {
        double mt = 1 - t;
        return 3 * mt * mt * t * _y0 + 3 * mt * t * t * _y1 + t * t * t;
    }

    /// <summary>
    /// Evaluate the curve: given x (time progress 0..1), return y (value 0..1, may overshoot).
    /// Uses Newton-Raphson to invert X, then samples Y.
    /// </summary>
    public double Evaluate(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        // Binary search for t where SampleX(t) ≈ x
        double lo = 0, hi = 1;
        for (int i = 0; i < 16; i++)
        {
            double mid = (lo + hi) * 0.5;
            double sx = SampleX(mid);
            if (sx < x) lo = mid; else hi = mid;
        }
        double t = (lo + hi) * 0.5;
        return SampleY(t);
    }

    /// <summary>Create a Func&lt;double,double&gt; delegate for this curve.</summary>
    public Func<double, double> ToFunc() => Evaluate;
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
    public static double EaseOutQuint(double t) => 1 - Math.Pow(1 - t, 5);
    public static double EaseOutExpo(double t) => t >= 1.0 ? 1.0 : 1 - Math.Pow(2, -10 * t);
    public static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double Spring(double t)
    {
        double c4 = (2 * Math.PI) / 3;
        return t == 0 ? 0 : t == 1 ? 1 :
            Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    }

    // Named bezier curves — populated from config
    private static readonly Dictionary<string, CubicBezier> _namedCurves = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a named bezier curve from config.</summary>
    public static void RegisterBezier(string name, double x0, double y0, double x1, double y1)
    {
        _namedCurves[name] = new CubicBezier(x0, y0, x1, y1);
        Logger.Instance.Debug($"Registered bezier curve '{name}': ({x0}, {y0}, {x1}, {y1})");
    }

    /// <summary>Clear all named bezier curves (called on config reload).</summary>
    public static void ClearBeziers() => _namedCurves.Clear();

    public static Func<double, double> FromString(string name)
    {
        // Check named curves first
        if (_namedCurves.TryGetValue(name, out var bezier))
            return bezier.ToFunc();

        return name.ToLowerInvariant() switch
        {
            "linear" => Linear,
            "ease_in" => EaseIn,
            "ease_out" => EaseOut,
            "ease_out_cubic" => EaseOutCubic,
            "ease_out_quint" => EaseOutQuint,
            "ease_out_expo" => EaseOutExpo,
            "ease_in_out_cubic" => EaseInOutCubic,
            "spring" => Spring,
            _ => EaseOutCubic,
        };
    }

    public static AnimationStyle ParseStyle(string style)
    {
        return style.ToLowerInvariant() switch
        {
            "slide" => AnimationStyle.Slide,
            "popin" => AnimationStyle.Popin,
            "fade" => AnimationStyle.Fade,
            _ => AnimationStyle.Popin,
        };
    }
}

/// <summary>
/// Animates window position/size changes using CompositionTarget.Rendering for frame-synced updates.
/// Uses SetWindowPos to interpolate between source and target rectangles.
/// Supports Hyprland animation styles: slide, popin, fade.
/// Supports custom bezier curves for easing.
/// </summary>
public sealed class AnimationEngine : IDisposable
{
    private readonly List<ActiveAnimation> _animations = new();
    private readonly object _lock = new();
    private bool _isRendering;
    private bool _disposed;

    // Pre-allocated list to avoid GC during rendering
    private readonly List<ActiveAnimation> _completedBuffer = new();

    public bool IsEnabled { get; set; } = true;
    public int MoveDurationMs { get; set; } = 120;
    public int OpenDurationMs { get; set; } = 200;
    public int CloseDurationMs { get; set; } = 150;
    public Func<double, double> EasingFunction { get; set; } = Easing.EaseOutCubic;
    public AnimationStyle OpenStyle { get; set; } = AnimationStyle.Popin;
    public int PopinPercent { get; set; } = 80;

    /// <summary>
    /// Update animation parameters from config.
    /// </summary>
    public void UpdateFromConfig(Configuration.AnimationsConfig config)
    {
        IsEnabled = config.Enabled;
        var presetName = config.Preset?.Trim() ?? "custom";
        if (!presetName.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            var preset = Configuration.AnimationPresets.Find(presetName);
            MoveDurationMs = preset.MoveDurationMs;
            OpenDurationMs = preset.OpenDurationMs;
            CloseDurationMs = preset.CloseDurationMs;
            EasingFunction = Easing.FromString(preset.Easing);
            OpenStyle = Easing.ParseStyle(preset.WindowOpenStyle);
            PopinPercent = Math.Clamp(preset.PopinPercent, 0, 100);
            return;
        }

        // custom: full manual control via TOML/UI
        MoveDurationMs = config.WindowMoveDurationMs;
        OpenDurationMs = config.WindowOpenDurationMs;
        CloseDurationMs = config.WindowCloseDurationMs;
        EasingFunction = Easing.FromString(config.Easing);
        OpenStyle = Easing.ParseStyle(config.WindowOpenStyle);
        PopinPercent = Math.Clamp(config.PopinPercent, 0, 100);
    }

    /// <summary>
    /// Animate a window from its current position to a target position.
    /// </summary>
    public void AnimateMove(IntPtr hwnd, NativeMethods.RECT from, NativeMethods.RECT to)
    {
        if (!IsEnabled || MoveDurationMs <= 0)
        {
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
                Style = AnimationStyle.Slide, // moves always use slide
                StartTick = Stopwatch.GetTimestamp(),
            };
            _animations.Add(anim);

            StartRendering();
        }
    }

    /// <summary>
    /// Animate a window opening with the configured style (popin, slide, fade).
    /// Hyprland default: popin 80% (scales from 80% to 100% from center).
    /// </summary>
    public void AnimateOpen(IntPtr hwnd, NativeMethods.RECT targetRect)
    {
        if (!IsEnabled || OpenDurationMs <= 0)
        {
            TilingEngine.ApplyWindowPosition(hwnd, targetRect);
            return;
        }

        NativeMethods.RECT fromRect;
        switch (OpenStyle)
        {
            case AnimationStyle.Popin:
            {
                // Scale from center — start at PopinPercent% of target size
                double scale = PopinPercent / 100.0;
                int cx = (targetRect.Left + targetRect.Right) / 2;
                int cy = (targetRect.Top + targetRect.Bottom) / 2;
                int hw = (int)(targetRect.Width * scale / 2);
                int hh = (int)(targetRect.Height * scale / 2);
                fromRect = new NativeMethods.RECT(cx - hw, cy - hh, cx + hw, cy + hh);
                break;
            }
            case AnimationStyle.Fade:
            {
                // Fade = snap to position immediately, no spatial animation
                // (true fade would need opacity control which Win32 supports via WS_EX_LAYERED)
                TilingEngine.ApplyWindowPosition(hwnd, targetRect);
                return;
            }
            case AnimationStyle.Slide:
            default:
            {
                // Slide in from 30px to the right
                fromRect = new NativeMethods.RECT(
                    targetRect.Left + 30, targetRect.Top,
                    targetRect.Right + 30, targetRect.Bottom);
                break;
            }
        }

        lock (_lock)
        {
            _animations.RemoveAll(a => a.WindowHandle == hwnd);
            var anim = new ActiveAnimation
            {
                WindowHandle = hwnd,
                From = fromRect,
                To = targetRect,
                DurationMs = OpenDurationMs,
                EasingFunction = EasingFunction,
                Style = OpenStyle,
                PopinPercent = PopinPercent,
                StartTick = Stopwatch.GetTimestamp(),
            };
            _animations.Add(anim);
            StartRendering();
        }
    }

    /// <summary>
    /// Check if a specific window is currently being animated.
    /// Used to skip redundant position updates during animation.
    /// </summary>
    public bool IsAnimating(IntPtr hwnd)
    {
        lock (_lock)
        {
            for (int i = 0; i < _animations.Count; i++)
                if (_animations[i].WindowHandle == hwnd) return true;
            return false;
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
        try
        {
            lock (_lock)
            {
                if (_animations.Count == 0)
                {
                    StopRendering();
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                _completedBuffer.Clear();

                for (int i = 0; i < _animations.Count; i++)
                {
                    var anim = _animations[i];
                    double t = anim.GetProgress(now);
                    double ease = anim.EasingFunction(t);

                    int x = Lerp(anim.From.Left, anim.To.Left, ease);
                    int y = Lerp(anim.From.Top, anim.To.Top, ease);
                    int r = Lerp(anim.From.Right, anim.To.Right, ease);
                    int b = Lerp(anim.From.Bottom, anim.To.Bottom, ease);

                    NativeMethods.SetWindowPos(anim.WindowHandle, IntPtr.Zero,
                        x, y, r - x, b - y,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
                        | NativeMethods.SWP_NOCOPYBITS);

                    if (t >= 1.0)
                        _completedBuffer.Add(anim);
                }

                for (int i = 0; i < _completedBuffer.Count; i++)
                {
                    var done = _completedBuffer[i];
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
        catch (Exception ex)
        {
            Logger.Instance.Debug($"AnimationEngine.OnRendering error: {ex.Message}");
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
