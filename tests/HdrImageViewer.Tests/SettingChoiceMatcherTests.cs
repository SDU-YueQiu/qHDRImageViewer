using HdrImageViewer.Rendering;
using HdrImageViewer.Settings;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class SettingChoiceMatcherTests
{
    private static IReadOnlyList<SettingChoice> ZoomInterpolationChoices()
    {
        return
        [
            new SettingChoice { LabelResourceKey = "SettingsInterpolationAuto", Value = (int)ZoomInterpolationMode.Auto },
            new SettingChoice { LabelResourceKey = "SettingsInterpolationNearest", Value = (int)ZoomInterpolationMode.Nearest },
            new SettingChoice { LabelResourceKey = "SettingsInterpolationBilinear", Value = (int)ZoomInterpolationMode.Bilinear },
            new SettingChoice { LabelResourceKey = "SettingsInterpolationBicubic", Value = (int)ZoomInterpolationMode.Bicubic },
            new SettingChoice { LabelResourceKey = "SettingsInterpolationLanczos", Value = (int)ZoomInterpolationMode.Lanczos },
        ];
    }

    [Theory]
    [InlineData(ZoomInterpolationMode.Auto, 0)]
    [InlineData(ZoomInterpolationMode.Nearest, 1)]
    [InlineData(ZoomInterpolationMode.Bilinear, 2)]
    [InlineData(ZoomInterpolationMode.Bicubic, 3)]
    [InlineData(ZoomInterpolationMode.Lanczos, 4)]
    public void FindChoiceIndex_MatchesEveryIntegerBackedChoice(ZoomInterpolationMode mode, int expectedIndex)
    {
        // Regression test: comparing the two failed `as string` casts made the first
        // integer choice look equal to every value, so ComboBoxes always showed Auto.
        var index = SettingChoiceMatcher.FindChoiceIndex(ZoomInterpolationChoices(), (int)mode);

        Assert.Equal(expectedIndex, index);
    }

    [Fact]
    public void FindChoiceIndex_ReturnsMinusOneForUnpersistedValue()
    {
        Assert.Equal(-1, SettingChoiceMatcher.FindChoiceIndex(ZoomInterpolationChoices(), 99));
    }

    [Fact]
    public void FindChoiceIndex_ReturnsMinusOneForNullValue()
    {
        Assert.Equal(-1, SettingChoiceMatcher.FindChoiceIndex(ZoomInterpolationChoices(), null));
    }

    [Fact]
    public void FindChoiceIndex_ReturnsMinusOneForEmptyChoices()
    {
        Assert.Equal(-1, SettingChoiceMatcher.FindChoiceIndex([], 0));
    }

    [Fact]
    public void FindChoiceIndex_DoesNotMatchDifferentNumericType()
    {
        // Values are deserialized as int; a boxed long is a different object and must
        // not silently select the first choice.
        Assert.Equal(-1, SettingChoiceMatcher.FindChoiceIndex(ZoomInterpolationChoices(), 4L));
    }

    [Fact]
    public void FindChoiceIndex_ReturnsFirstDuplicate()
    {
        IReadOnlyList<SettingChoice> choices =
        [
            new SettingChoice { LabelResourceKey = "First", Value = 7 },
            new SettingChoice { LabelResourceKey = "Second", Value = 7 },
        ];

        Assert.Equal(0, SettingChoiceMatcher.FindChoiceIndex(choices, 7));
    }

    [Fact]
    public void FindChoiceIndex_MatchesStringBackedChoicesCaseInsensitively()
    {
        IReadOnlyList<SettingChoice> choices =
        [
            new SettingChoice { LabelResourceKey = "System", Value = string.Empty },
            new SettingChoice { LabelResourceKey = "English", Value = "en-US" },
            new SettingChoice { LabelResourceKey = "German", Value = "de-DE" },
        ];

        Assert.Equal(2, SettingChoiceMatcher.FindChoiceIndex(choices, "DE-de"));
        Assert.Equal(0, SettingChoiceMatcher.FindChoiceIndex(choices, string.Empty));
        Assert.Equal(-1, SettingChoiceMatcher.FindChoiceIndex(choices, "fr-FR"));
    }

    [Fact]
    public void FindChoiceIndex_DoesNotMatchStringAndNumber()
    {
        IReadOnlyList<SettingChoice> choices =
        [
            new SettingChoice { LabelResourceKey = "System", Value = string.Empty },
            new SettingChoice { LabelResourceKey = "English", Value = "en-US" },
        ];

        Assert.Equal(-1, SettingChoiceMatcher.FindChoiceIndex(choices, 0));
    }

    [Fact]
    public void Matches_ComparesOnlyRealStringsAsStrings()
    {
        Assert.True(SettingChoiceMatcher.Matches("zh-CN", "ZH-cn"));
        Assert.False(SettingChoiceMatcher.Matches("zh-CN", "en-US"));
        Assert.True(SettingChoiceMatcher.Matches(4, 4));
        Assert.False(SettingChoiceMatcher.Matches(4, 0));
        Assert.False(SettingChoiceMatcher.Matches(0, string.Empty));
        Assert.False(SettingChoiceMatcher.Matches(0, null));
    }
}
