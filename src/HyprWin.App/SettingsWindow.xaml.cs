using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using HyprWin.Core;
using HyprWin.Core.Configuration;

namespace HyprWin.App;

/// <summary>
/// Visual settings window for editing HyprWin configuration.
/// Reads current values from the TOML file and writes changes back.
/// The FileSystemWatcher on ConfigManager picks up changes automatically.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly string _configPath;
    private readonly HyprWinConfig _config;

    public SettingsWindow(string configPath, HyprWinConfig config)
    {
        InitializeComponent();
        _configPath = configPath;
        _config = config;

        LoadValues();
    }

    private void LoadValues()
    {
        // Layout
        GapsInner.Text = _config.Layout.GapsInner.ToString();
        GapsOuter.Text = _config.Layout.GapsOuter.ToString();
        BorderSize.Text = _config.Layout.BorderSize.ToString();
        Rounding.Text = _config.Layout.Rounding.ToString();

        // Theme
        BorderActive.Text = _config.Theme.BorderActive;
        BorderInactive.Text = _config.Theme.BorderInactive;
        TopBarBg.Text = _config.Theme.TopBarBg;
        TopBarFg.Text = _config.Theme.TopBarFg;
        TopBarAccent.Text = _config.Theme.TopBarAccent;

        // Animations
        AnimEnabled.IsChecked = _config.Animations.Enabled;
        AnimMoveDuration.Text = _config.Animations.WindowMoveDurationMs.ToString();

        // General
        TerminalCmd.Text = _config.General.TerminalCommand;
        foreach (ComboBoxItem item in WorkspaceMode.Items)
        {
            if (item.Content?.ToString() == _config.General.WorkspaceMode)
            {
                WorkspaceMode.SelectedItem = item;
                break;
            }
        }

        // Exclusions
        ExcludedProcesses.Text = string.Join(", ", _config.Exclude.ProcessNames);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                MessageBox.Show("Config file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toml = File.ReadAllText(_configPath);

            // Layout
            toml = SetTomlValue(toml, "gaps_inner", GapsInner.Text.Trim());
            toml = SetTomlValue(toml, "gaps_outer", GapsOuter.Text.Trim());
            toml = SetTomlValue(toml, "border_size", BorderSize.Text.Trim());
            toml = SetTomlValue(toml, "rounding", Rounding.Text.Trim());

            // Theme
            toml = SetTomlStringValue(toml, "border_active", BorderActive.Text.Trim());
            toml = SetTomlStringValue(toml, "border_inactive", BorderInactive.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_bg", TopBarBg.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_fg", TopBarFg.Text.Trim());
            toml = SetTomlStringValue(toml, "top_bar_accent", TopBarAccent.Text.Trim());

            // Animations — use section-aware replacement: "enabled" also exists in [top_bar]
            toml = SetTomlValueInSection(toml, "animations", "enabled", AnimEnabled.IsChecked == true ? "true" : "false");
            toml = SetTomlValue(toml, "window_move_duration_ms", AnimMoveDuration.Text.Trim());

            // General
            toml = SetTomlStringValue(toml, "terminal_command", TerminalCmd.Text.Trim());
            var selectedMode = (WorkspaceMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "monitor_bound";
            toml = SetTomlStringValue(toml, "workspace_mode", selectedMode);

            // Exclusions — rebuild the array
            var procs = ExcludedProcesses.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Select(s => $"\"{s}\"");
            var procsArray = $"[{string.Join(", ", procs)}]";
            toml = SetTomlArrayValue(toml, "process_names", procsArray);

            File.WriteAllText(_configPath, toml);
            Logger.Instance.Info("Settings saved via UI");

            Close();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error saving settings", ex);
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenToml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _configPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Failed to open config file", ex);
        }
    }

    /// <summary>
    /// Replace a TOML key's value (numeric/boolean). Matches: key = value
    /// Uses "${1}"/"${3}" notation to avoid $1digit ambiguity in .NET regex replacements
    /// (e.g. value="4" would produce "$14" which .NET treats as backreference to group 14).
    /// </summary>
    private static string SetTomlValue(string toml, string key, string value)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)(.+?)(\s*(?:#.*)?)$";
        return Regex.Replace(toml, pattern, "${1}" + value + "${3}", RegexOptions.Multiline);
    }

    /// <summary>
    /// Replace a TOML key's value only within a specific [section] block.
    /// Prevents false positives when the same key name appears in multiple sections (e.g. "enabled").
    /// </summary>
    private static string SetTomlValueInSection(string toml, string section, string key, string value)
    {
        // Locate the [section] header
        int start = toml.IndexOf($"\n[{section}]");
        if (start < 0)
        {
            if (!toml.StartsWith($"[{section}]"))
                return SetTomlValue(toml, key, value); // fallback: section not found
            start = 0;
        }
        else
        {
            start += 1; // skip the leading newline
        }

        // Find the next top-level [section] header (not [[array]] headers)
        int searchFrom = start + section.Length + 2;
        var nextSection = Regex.Match(toml.Substring(searchFrom), @"^\[(?!\[)", RegexOptions.Multiline);
        int end = nextSection.Success ? searchFrom + nextSection.Index : toml.Length;

        string before = toml[..start];
        string sectionContent = toml[start..end];
        string after = toml[end..];

        sectionContent = SetTomlValue(sectionContent, key, value);
        return before + sectionContent + after;
    }

    /// <summary>
    /// Replace a TOML key's string value (quoted). Matches: key = "value"
    /// </summary>
    private static string SetTomlStringValue(string toml, string key, string value)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)""[^""]*""(.*)$";
        return Regex.Replace(toml, pattern, "${1}\"" + value + "\"${2}", RegexOptions.Multiline);
    }

    /// <summary>
    /// Replace a TOML key's array value. Matches: key = [...]
    /// </summary>
    private static string SetTomlArrayValue(string toml, string key, string arrayValue)
    {
        var pattern = $@"^(\s*{Regex.Escape(key)}\s*=\s*)\[.*?\](.*)$";
        return Regex.Replace(toml, pattern, "${1}" + arrayValue + "${2}", RegexOptions.Multiline);
    }
}
