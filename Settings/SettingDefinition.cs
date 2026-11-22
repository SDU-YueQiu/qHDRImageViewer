using HdrImageViewer.Services;

namespace HdrImageViewer.Settings;

/// <summary>
/// The kind of value displayed by a setting card.
/// </summary>
public enum SettingKind
{
    Toggle,
    Choice,
    Number,
    Text,
    Button,
    Info,
}

/// <summary>
/// Declarative description of a single setting. The registry owns the catalog;
/// the settings page renders one card per definition and never hard-codes values.
/// </summary>
public sealed class SettingDefinition
{
    /// <summary>
    /// Gets the stable identifier. It is also the suffix of the UI automation id.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the identifier of the registry page that hosts this setting.
    /// </summary>
    public required string PageId { get; init; }

    /// <summary>
    /// Gets the identifier of the group inside the page.
    /// </summary>
    public required string GroupId { get; init; }

    /// <summary>
    /// Gets the resource key of the localized card header.
    /// </summary>
    public required string LabelResourceKey { get; init; }

    /// <summary>
    /// Gets the optional resource key of the localized one-line description.
    /// </summary>
    public string? DescriptionResourceKey { get; init; }

    /// <summary>
    /// Gets the value kind that decides which control renders the card.
    /// </summary>
    public required SettingKind Kind { get; init; }

    /// <summary>
    /// Gets the documented default value used by the per-item reset.
    /// </summary>
    public required object DefaultValue { get; init; }

    /// <summary>
    /// Gets the minimum value of a <see cref="SettingKind.Number"/> setting.
    /// </summary>
    public double Minimum { get; init; }

    /// <summary>
    /// Gets the maximum value of a <see cref="SettingKind.Number"/> setting.
    /// </summary>
    public double Maximum { get; init; }

    /// <summary>
    /// Gets the step used by the number box spin buttons.
    /// </summary>
    public double Step { get; init; } = 1;

    /// <summary>
    /// Gets the optional resource key of the localized unit shown after a number.
    /// </summary>
    public string? UnitResourceKey { get; init; }

    /// <summary>
    /// Gets the selectable options of a <see cref="SettingKind.Choice"/> setting.
    /// </summary>
    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];

    /// <summary>
    /// Gets the optional resource key of the localized text of an action button.
    /// </summary>
    public string? ActionLabelResourceKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting is meant for advanced users.
    /// </summary>
    public bool IsAdvanced { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting only takes effect after a restart.
    /// </summary>
    public bool RequiresRestart { get; init; }

    /// <summary>
    /// Gets the Segoe Fluent Icons glyph shown next to the card header.
    /// </summary>
    public string Glyph { get; init; } = "\uE713";

    /// <summary>
    /// Gets the reader for the current value of the setting.
    /// </summary>
    public required Func<object?> GetValue { get; init; }

    /// <summary>
    /// Gets the writer that applies a new value through <see cref="AppSettingsService"/>.
    /// </summary>
    public required Action<object?> SetValue { get; init; }

    /// <summary>
    /// Gets the optional command of a <see cref="SettingKind.Button"/> setting.
    /// </summary>
    public Func<SettingsViewModel, Task>? ActionAsync { get; init; }

    /// <summary>
    /// Gets the optional provider of the read-only text of a
    /// <see cref="SettingKind.Info"/> setting.
    /// </summary>
    public Func<string>? InfoTextProvider { get; init; }

    /// <summary>
    /// Gets the UI automation id of the value control (Settings{Id}).
    /// </summary>
    public string AutomationId => "Settings" + Id;
}

/// <summary>
/// Factory helpers that keep <see cref="SettingsRegistry"/> free of type casts.
/// </summary>
public static class SettingDefinitionFactory
{
    public static SettingChoice ChoiceItem(string labelResourceKey, int value) => new()
    {
        LabelResourceKey = labelResourceKey,
        Value = value,
    };

    public static SettingChoice ChoiceItem(string labelResourceKey, string value) => new()
    {
        LabelResourceKey = labelResourceKey,
        Value = value,
    };

    public static SettingDefinition Toggle(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string glyph,
        Func<AppUserSettings, bool> get,
        Action<bool> set,
        bool defaultValue = false,
        bool isAdvanced = false)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Toggle,
            DefaultValue = defaultValue,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            GetValue = () => get(AppSettingsService.Current),
            SetValue = value => set(value is bool boolean ? boolean : defaultValue),
        };
    }

    public static SettingDefinition Choice(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string glyph,
        IReadOnlyList<SettingChoice> choices,
        int defaultValue,
        Func<AppUserSettings, int> get,
        Action<int> set,
        bool isAdvanced = false)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Choice,
            DefaultValue = defaultValue,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Choices = choices,
            GetValue = () => get(AppSettingsService.Current),
            SetValue = value => set(value is int integer ? integer : defaultValue),
        };
    }

    public static SettingDefinition StringChoice(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string glyph,
        IReadOnlyList<SettingChoice> choices,
        string defaultValue,
        Func<AppUserSettings, string> get,
        Action<string> set,
        bool requiresRestart = false,
        bool isAdvanced = false)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        var fallback = defaultValue ?? string.Empty;
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Choice,
            DefaultValue = fallback,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            RequiresRestart = requiresRestart,
            Choices = choices,
            GetValue = () => get(AppSettingsService.Current) ?? string.Empty,
            SetValue = value => set(value as string ?? fallback),
        };
    }

    public static SettingDefinition IntNumber(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string glyph,
        int defaultValue,
        int minimum,
        int maximum,
        int step,
        string? unitResourceKey,
        Func<AppUserSettings, int> get,
        Action<int> set,
        bool isAdvanced = false)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Number,
            DefaultValue = defaultValue,
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
            UnitResourceKey = unitResourceKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            GetValue = () => get(AppSettingsService.Current),
            SetValue = value => set(value is int integer ? integer : defaultValue),
        };
    }

    public static SettingDefinition DoubleNumber(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string glyph,
        double defaultValue,
        double minimum,
        double maximum,
        double step,
        string? unitResourceKey,
        Func<AppUserSettings, double> get,
        Action<double> set,
        bool isAdvanced = false)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Number,
            DefaultValue = defaultValue,
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
            UnitResourceKey = unitResourceKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            GetValue = () => get(AppSettingsService.Current),
            SetValue = value => set(value is double number ? number : defaultValue),
        };
    }

    public static SettingDefinition Button(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string actionLabelResourceKey,
        string glyph,
        Func<SettingsViewModel, Task>? actionAsync = null,
        bool isAdvanced = false)
    {
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Button,
            DefaultValue = string.Empty,
            Glyph = glyph,
            ActionLabelResourceKey = actionLabelResourceKey,
            ActionAsync = actionAsync,
            IsAdvanced = isAdvanced,
            GetValue = static () => string.Empty,
            SetValue = static _ => { },
        };
    }

    public static SettingDefinition Info(
        string id,
        string pageId,
        string groupId,
        string labelResourceKey,
        string? descriptionResourceKey,
        string glyph,
        Func<string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);
        return new SettingDefinition
        {
            Id = id,
            PageId = pageId,
            GroupId = groupId,
            LabelResourceKey = labelResourceKey,
            DescriptionResourceKey = descriptionResourceKey,
            Kind = SettingKind.Info,
            DefaultValue = string.Empty,
            Glyph = glyph,
            InfoTextProvider = textProvider,
            GetValue = static () => string.Empty,
            SetValue = static _ => { },
        };
    }
}
