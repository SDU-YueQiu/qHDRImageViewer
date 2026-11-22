using HdrImageViewer.Services;

namespace HdrImageViewer.Settings;

/// <summary>
/// Payload of the "setting changed" notification raised after a value was applied.
/// </summary>
public sealed class SettingChangedEventArgs : EventArgs
{
    public SettingChangedEventArgs(SettingDefinition definition, object? previousValue, object? newValue)
    {
        Definition = definition;
        PreviousValue = previousValue;
        NewValue = newValue;
    }

    public SettingDefinition Definition { get; }

    public object? PreviousValue { get; }

    public object? NewValue { get; }
}

/// <summary>
/// Result of parsing and applying an imported settings document.
/// </summary>
public sealed class SettingsImportResult
{
    public SettingsImportResult(bool success, string? errorDetail)
    {
        Success = success;
        ErrorDetail = errorDetail;
    }

    public bool Success { get; }

    public string? ErrorDetail { get; }
}

/// <summary>
/// Thin binding layer between the declarative registry and <see cref="AppSettingsService"/>.
/// Applying a value, comparing against the default and importing/exporting all go
/// through this class so the page only deals with controls.
/// </summary>
public sealed class SettingsViewModel
{
    /// <summary>
    /// Raised after one setting was applied and its value actually changed.
    /// </summary>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// Raised after every setting was replaced (import or reset-all) so the page
    /// can refresh all visible values.
    /// </summary>
    public event EventHandler? SettingsReloaded;

    public static object? GetValue(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.GetValue();
    }

    public static bool IsModifiedFromDefault(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return !Equals(definition.GetValue(), definition.DefaultValue);
    }

    /// <summary>
    /// Applies a value through the settings service and raises
    /// <see cref="SettingChanged"/> when the effective value changed.
    /// </summary>
    public void SetValue(SettingDefinition definition, object? value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var previous = definition.GetValue();
        definition.SetValue(value);
        var current = definition.GetValue();
        if (!Equals(previous, current))
        {
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(definition, previous, current));
        }
    }

    public void Reset(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        SetValue(definition, definition.DefaultValue);
    }

    /// <summary>
    /// Restores every value setting of one page to its documented default.
    /// </summary>
    public void ResetPage(string pageId)
    {
        foreach (var definition in SettingsRegistry.GetPage(pageId).Settings)
        {
            if (definition.Kind is SettingKind.Toggle or SettingKind.Choice or SettingKind.Number or SettingKind.Text)
            {
                Reset(definition);
            }
        }
    }

    /// <summary>
    /// Restores every persisted setting (all pages) to its documented default.
    /// </summary>
    public void ResetAll()
    {
        AppSettingsService.ResetAllSettings();
        SettingsReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Serializes the current settings to the JSON format used by the settings file.
    /// </summary>
    public static string ExportToJson() => AppSettingsService.ExportSettingsJson();

    /// <summary>
    /// Parses, normalizes and applies an imported settings document.
    /// </summary>
    public SettingsImportResult ImportFromJson(string json)
    {
        if (!AppSettingsService.TryParseSettingsJson(json, out var settings, out var error))
        {
            return new SettingsImportResult(false, error);
        }

        AppSettingsService.ApplySettings(settings);
        SettingsReloaded?.Invoke(this, EventArgs.Empty);
        return new SettingsImportResult(true, null);
    }

    /// <summary>
    /// Runs the optional command attached to a button definition.
    /// </summary>
    public Task ExecuteActionAsync(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.ActionAsync?.Invoke(this) ?? Task.CompletedTask;
    }
}
