namespace HdrImageViewer.Settings;

/// <summary>
/// One selectable option of a choice setting. Integer values represent enums and
/// string values represent free-form preferences.
/// </summary>
public sealed class SettingChoice
{
    /// <summary>
    /// Gets the resource key of the localized option label.
    /// </summary>
    public required string LabelResourceKey { get; init; }

    /// <summary>
    /// Gets the raw value persisted when this option is selected.
    /// </summary>
    public required object Value { get; init; }
}

/// <summary>
/// Dependency-free matching between a persisted setting value and the raw values of
/// the <see cref="SettingChoice"/> options. The settings page uses it to restore the
/// selection of every choice ComboBox; keeping it separate keeps the lookup unit
/// testable without WinUI or the settings registry.
/// </summary>
public static class SettingChoiceMatcher
{
    /// <summary>
    /// Returns the index of the choice whose value matches <paramref name="value"/>,
    /// or <c>-1</c> when no choice matches.
    /// </summary>
    public static int FindChoiceIndex(IReadOnlyList<SettingChoice> choices, object? value)
    {
        ArgumentNullException.ThrowIfNull(choices);
        for (var index = 0; index < choices.Count; index++)
        {
            if (Matches(choices[index].Value, value))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Compares one raw choice value with the persisted setting value. Two strings
    /// are compared case-insensitively; every other combination falls back to
    /// instance equality. The string comparison is only attempted when both
    /// operands really are strings: enum-backed choices store boxed integers, and
    /// comparing the two failed string casts made every integer choice look equal
    /// to the first option.
    /// </summary>
    public static bool Matches(object? choiceValue, object? value)
    {
        if (choiceValue is string choiceText && value is string valueText)
        {
            return string.Equals(choiceText, valueText, StringComparison.OrdinalIgnoreCase);
        }

        return Equals(choiceValue, value);
    }
}
