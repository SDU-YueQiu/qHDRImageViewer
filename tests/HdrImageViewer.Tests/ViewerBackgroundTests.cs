using System.Numerics;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ViewerBackgroundTests
{
    [Theory]
    [InlineData(ViewerBackgroundMode.Window, false)]
    [InlineData(ViewerBackgroundMode.Black, true)]
    [InlineData(ViewerBackgroundMode.DarkGray, true)]
    [InlineData(ViewerBackgroundMode.MidGray, true)]
    [InlineData(ViewerBackgroundMode.LightGray, true)]
    [InlineData(ViewerBackgroundMode.White, true)]
    public void TryGetSrgb_OnlyWindowModeDefersToTheTheme(ViewerBackgroundMode mode, bool hasColor)
    {
        Assert.Equal(hasColor, ViewerBackgroundPalette.TryGetSrgb(mode, out _, out _, out _));
    }

    [Fact]
    public void TryGetSrgb_ReturnsTheDocumentedPresets()
    {
        Assert.True(ViewerBackgroundPalette.TryGetSrgb(ViewerBackgroundMode.Black, out var r, out var g, out var b));
        Assert.Equal((0, 0, 0), (r, g, b));

        Assert.True(ViewerBackgroundPalette.TryGetSrgb(ViewerBackgroundMode.DarkGray, out r, out g, out b));
        Assert.Equal((32, 32, 32), (r, g, b));

        Assert.True(ViewerBackgroundPalette.TryGetSrgb(ViewerBackgroundMode.MidGray, out r, out g, out b));
        Assert.Equal((96, 96, 96), (r, g, b));

        Assert.True(ViewerBackgroundPalette.TryGetSrgb(ViewerBackgroundMode.LightGray, out r, out g, out b));
        Assert.Equal((208, 208, 208), (r, g, b));

        Assert.True(ViewerBackgroundPalette.TryGetSrgb(ViewerBackgroundMode.White, out r, out g, out b));
        Assert.Equal((255, 255, 255), (r, g, b));
    }

    [Theory]
    [InlineData(0, 0.0f)]
    [InlineData(255, 1.0f)]
    [InlineData(128, 0.2158f)]
    public void ToLinear_ConvertsSrgbToScRgb(byte channel, float expected)
    {
        Assert.Equal(expected, ViewerBackgroundPalette.ToLinear(channel), 3);
    }

    [Fact]
    public void ToScRgb_KeepsAlphaOpaqueAndLinearisesChannels()
    {
        var color = ViewerBackgroundPalette.ToScRgb(new Vector3(128 / 255f, 0.0f, 1.0f));

        Assert.Equal(0.2158f, color.X, 3);
        Assert.Equal(0.0f, color.Y);
        Assert.Equal(1.0f, color.Z, 3);
        Assert.Equal(1.0f, color.W);
    }

    [Theory]
    [InlineData(80.0, 1.0)]
    [InlineData(203.0, 2.5375)]
    [InlineData(280.0, 3.5)]
    public void ToScRgb_ScalesByTheDisplaySdrWhite(double sdrWhiteNits, double expectedScale)
    {
        var color = ViewerBackgroundPalette.ToScRgb(Vector3.One, (float)(sdrWhiteNits / 80.0));

        Assert.Equal((float)expectedScale, color.X, 3);
        Assert.Equal(1.0f, color.W);
    }

    [Fact]
    public void BackgroundClear_MatchesTheSdrWhiteScaleOfAnHdrDisplay()
    {
        var hdr = new HdrDisplayConfiguration("Hdr", true, true, 203.0, 1000.0, 600.0, "test");
        var sdr = new HdrDisplayConfiguration("Sdr", false, false, 80.0, 0.0, 0.0, "test");
        var windowColor = new Vector3(243 / 255f);

        Assert.Equal(2.5375f, hdr.SceneToSdrWhiteScale, 3);
        Assert.Equal(1.0f, sdr.SceneToSdrWhiteScale, 3);

        var hdrClear = ViewerBackgroundPalette.ToScRgb(windowColor, hdr.SceneToSdrWhiteScale);
        var sdrClear = ViewerBackgroundPalette.ToScRgb(windowColor, sdr.SceneToSdrWhiteScale);

        // The HDR clear has to be exactly the SDR white ratio brighter, which is
        // what makes it land on the same apparent grey as the XAML backdrop.
        Assert.Equal(0.896f, sdrClear.X, 3);
        Assert.Equal(sdrClear.X * 2.5375f, hdrClear.X, 3);
    }

    [Theory]
    [InlineData((ViewerBackgroundMode)42, ViewerBackgroundMode.Window)]
    [InlineData(ViewerBackgroundMode.MidGray, ViewerBackgroundMode.MidGray)]
    public void Normalize_FallsBackToWindowForUndefinedValues(
        ViewerBackgroundMode stored, ViewerBackgroundMode expected)
    {
        Assert.Equal(expected, ViewerBackgroundPalette.Normalize(stored));
    }

    [Fact]
    public void Settings_RoundTripTheViewerBackground()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(), "HdrImageViewer.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        using var _ = AppSettingsService.OverrideFilePathForTesting(settingsPath);

        AppSettingsService.SetViewerBackground(ViewerBackgroundMode.White);
        AppSettingsService.FlushPendingSave();

        Assert.Equal(ViewerBackgroundMode.White, AppSettingsService.Current.ViewerBackground);
        Assert.Contains("\"ViewerBackground\": 5", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }
}
