namespace HyprWin.Core.Configuration;

/// <summary>
/// Built-in animation presets that can be selected from settings.
/// </summary>
public sealed class AnimationPreset
{
    public string Name { get; init; } = "custom";
    public int OpenDurationMs { get; init; }
    public int CloseDurationMs { get; init; }
    public int MoveDurationMs { get; init; }
    public string Easing { get; init; } = "ease_out_cubic";
    public string WindowOpenStyle { get; init; } = "popin";
    public int PopinPercent { get; init; } = 80;
}

public static class AnimationPresets
{
    public static readonly IReadOnlyList<AnimationPreset> Presets = new List<AnimationPreset>
    {
        new()
        {
            Name = "custom",
            OpenDurationMs = 200,
            CloseDurationMs = 150,
            MoveDurationMs = 120,
            Easing = "ease_out_cubic",
            WindowOpenStyle = "popin",
            PopinPercent = 80,
        },
        new()
        {
            Name = "snappy",
            OpenDurationMs = 140,
            CloseDurationMs = 100,
            MoveDurationMs = 85,
            Easing = "ease_out_quint",
            WindowOpenStyle = "slide",
            PopinPercent = 90,
        },
        new()
        {
            Name = "smooth",
            OpenDurationMs = 240,
            CloseDurationMs = 180,
            MoveDurationMs = 150,
            Easing = "ease_in_out_cubic",
            WindowOpenStyle = "popin",
            PopinPercent = 82,
        },
        new()
        {
            Name = "energetic",
            OpenDurationMs = 220,
            CloseDurationMs = 140,
            MoveDurationMs = 120,
            Easing = "spring",
            WindowOpenStyle = "popin",
            PopinPercent = 74,
        },
        new()
        {
            Name = "minimal",
            OpenDurationMs = 80,
            CloseDurationMs = 80,
            MoveDurationMs = 70,
            Easing = "linear",
            WindowOpenStyle = "fade",
            PopinPercent = 100,
        },
    };

    public static AnimationPreset Find(string name)
    {
        return Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Presets[0];
    }
}
