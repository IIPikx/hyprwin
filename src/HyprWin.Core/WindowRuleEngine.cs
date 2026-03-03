using System.Text.RegularExpressions;

namespace HyprWin.Core;

/// <summary>
/// Hyprland-style window rule that matches windows by process name, class, or title
/// and applies effects like float, opacity, workspace assignment, pin, etc.
/// Each rule has match criteria (all must match) and one or more effects to apply.
/// </summary>
public sealed class WindowRule
{
    // ── Match criteria (all non-null criteria must match) ──
    public Regex? MatchProcess { get; init; }
    public Regex? MatchClass { get; init; }
    public Regex? MatchTitle { get; init; }

    // ── Static effects (applied once on window open) ──
    public bool? Float { get; init; }
    public bool? Fullscreen { get; init; }
    public int? Workspace { get; init; }
    public bool? Pin { get; init; }
    public bool? Center { get; init; }
    public bool? NoAnim { get; init; }
    public double? Opacity { get; init; }
    public string? BorderColor { get; init; }
    public int? BorderSize { get; init; }
    public (int w, int h)? Size { get; init; }
    public (int x, int y)? Move { get; init; }

    /// <summary>
    /// Check whether a window matches all of this rule's criteria.
    /// </summary>
    public bool Matches(ManagedWindow window)
    {
        if (MatchProcess != null && !MatchProcess.IsMatch(window.ProcessName ?? ""))
            return false;
        if (MatchClass != null && !MatchClass.IsMatch(window.ClassName ?? ""))
            return false;
        if (MatchTitle != null && !MatchTitle.IsMatch(window.Title ?? ""))
            return false;
        return true;
    }
}

/// <summary>
/// Evaluates Hyprland-style window rules against newly opened or changed windows.
/// Rules are configured via [[window_rule]] entries in hyprwin.toml.
/// </summary>
public sealed class WindowRuleEngine
{
    private List<WindowRule> _rules = new();

    /// <summary>
    /// Replace the current rule set. Called on config load/reload.
    /// </summary>
    public void SetRules(IReadOnlyList<WindowRule> rules)
    {
        _rules = rules.ToList();
        Logger.Instance.Info($"Window rules updated: {_rules.Count} rule(s)");
    }

    /// <summary>
    /// Evaluate all rules against a window and apply matching effects.
    /// Returns the aggregate result of all matching rules (last match wins per property).
    /// </summary>
    public WindowRuleResult Evaluate(ManagedWindow window)
    {
        var result = new WindowRuleResult();
        foreach (var rule in _rules)
        {
            if (!rule.Matches(window)) continue;

            // Last-match-wins for each property (Hyprland behavior)
            if (rule.Float.HasValue) result.Float = rule.Float.Value;
            if (rule.Fullscreen.HasValue) result.Fullscreen = rule.Fullscreen.Value;
            if (rule.Workspace.HasValue) result.Workspace = rule.Workspace.Value;
            if (rule.Pin.HasValue) result.Pin = rule.Pin.Value;
            if (rule.Center.HasValue) result.Center = rule.Center.Value;
            if (rule.NoAnim.HasValue) result.NoAnim = rule.NoAnim.Value;
            if (rule.Opacity.HasValue) result.Opacity = rule.Opacity.Value;
            if (rule.BorderColor != null) result.BorderColor = rule.BorderColor;
            if (rule.BorderSize.HasValue) result.BorderSize = rule.BorderSize.Value;
            if (rule.Size.HasValue) result.Size = rule.Size.Value;
            if (rule.Move.HasValue) result.Move = rule.Move.Value;

            Logger.Instance.Debug($"Window rule matched: [{window.ProcessName}] {window.Title}");
        }
        return result;
    }

    public bool HasRules => _rules.Count > 0;
}

/// <summary>
/// Aggregated result of all matching window rules for a given window.
/// </summary>
public sealed class WindowRuleResult
{
    public bool? Float { get; set; }
    public bool? Fullscreen { get; set; }
    public int? Workspace { get; set; }
    public bool? Pin { get; set; }
    public bool? Center { get; set; }
    public bool? NoAnim { get; set; }
    public double? Opacity { get; set; }
    public string? BorderColor { get; set; }
    public int? BorderSize { get; set; }
    public (int w, int h)? Size { get; set; }
    public (int x, int y)? Move { get; set; }

    /// <summary>True if any property was set by a matching rule.</summary>
    public bool HasAnyEffect =>
        Float.HasValue || Fullscreen.HasValue || Workspace.HasValue ||
        Pin.HasValue || Center.HasValue || NoAnim.HasValue ||
        Opacity.HasValue || BorderColor != null || BorderSize.HasValue ||
        Size.HasValue || Move.HasValue;
}
