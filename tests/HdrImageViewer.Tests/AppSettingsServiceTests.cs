using HdrImageViewer.Models;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HdrImageViewerTests", Path.GetRandomFileName());
    private readonly IDisposable _scope;

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public AppSettingsServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _scope = AppSettingsService.OverrideFilePathForTesting(SettingsPath);
    }

    [Fact]
    public void FirstLaunch_UsesDocumentedAdvancedDefaults()
    {
        var settings = new AppUserSettings();

        Assert.Equal(ZoomInterpolationMode.Auto, settings.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Auto, settings.ZoomDownscaleMode);
        Assert.Equal(1.1, settings.WheelZoomStepFactor);
        Assert.Equal(140, settings.ZoomAnimationDurationMilliseconds);
        Assert.Equal(800, settings.MaxZoomPercent);
        Assert.Equal(OpenImageZoomMode.Fit, settings.OpenImageZoomMode);
        Assert.False(settings.LimitWheelZoomOutToFit);
        Assert.True(settings.RestoreLastImageOnStartup);
        Assert.Equal(2200, settings.ViewerChromeAutoHideMilliseconds);
        Assert.True(settings.ShowStatusFileName);
        Assert.True(settings.ShowStatusDimensions);
        Assert.True(settings.ShowStatusFileSize);
        Assert.True(settings.ShowStatusHdrKind);
        Assert.Equal(HdrHeadroomMode.SystemAdaptive, settings.HdrHeadroomMode);
        Assert.Equal(0, settings.HdrManualHeadroomStops);
        Assert.False(settings.SdrWhiteOverrideEnabled);
        Assert.Equal(203, settings.SdrWhiteNits);
        Assert.False(settings.AdaptiveToneMappingEnabled);
        Assert.True(settings.AnalysisAutoRefreshEnabled);
        Assert.Equal(320, settings.DecodedCacheBudgetMegabytes);
        Assert.Equal(200, settings.MetadataCacheMaxFiles);
        Assert.Equal(4, settings.DirectoryIndexCacheCount);
        Assert.True(settings.MemoryPressureAutoTrim);
        Assert.True(settings.ConfirmBeforeOverwrite);
        Assert.Equal(1, settings.BatchExportMaxRetries);
        Assert.True(settings.RtxVsrMenuEnabled);
        Assert.Equal(RtxVsrMode.HighBitrate, settings.RtxVsrMode);
        Assert.Equal(RtxVsrQualityPreset.Ultra, settings.RtxVsrQualityPreset);
        Assert.Equal(2, settings.RtxVsrScaleFactor);
    }

    [Fact]
    public void OldSettingsFile_MissingAdvancedFields_UsesDefaults()
    {
        File.WriteAllText(SettingsPath, """{"MouseWheelBehavior":1,"Theme":2}""");

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(MouseWheelBehavior.ZoomImage, settings.MouseWheelBehavior);
        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.Equal(ZoomInterpolationMode.Auto, settings.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Auto, settings.ZoomDownscaleMode);
        Assert.Equal(1.1, settings.WheelZoomStepFactor);
        Assert.Equal(800, settings.MaxZoomPercent);
        Assert.Equal(HdrHeadroomMode.SystemAdaptive, settings.HdrHeadroomMode);
        Assert.Equal(320, settings.DecodedCacheBudgetMegabytes);
    }

    [Fact]
    public void LoadSettings_ClampsAndRepairsOutOfRangeValues()
    {
        File.WriteAllText(SettingsPath, """
            {
              "ZoomInterpolation": 99,
              "WheelZoomStepFactor": 99.0,
              "ZoomAnimationDurationMilliseconds": 99999,
              "MaxZoomPercent": 99999,
              "OpenImageZoomMode": -1,
              "ViewerChromeAutoHideMilliseconds": -5,
              "HdrHeadroomMode": 2,
              "HdrManualHeadroomStops": -99.0,
              "SdrWhiteNits": 9999,
              "DecodedCacheBudgetMegabytes": 1,
              "MetadataCacheMaxFiles": 1,
              "DirectoryIndexCacheCount": 999,
              "BatchExportMaxRetries": 99,
              "RtxVsrMode": 99,
              "RtxVsrQualityPreset": 99,
              "RtxVsrScaleFactor": 3
            }
            """);

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(ZoomInterpolationMode.Auto, settings.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Auto, settings.ZoomDownscaleMode);
        Assert.Null(settings.LegacyZoomInterpolation);
        Assert.Null(settings.LegacyZoomInterpolationDownscale);
        Assert.Equal(1.5, settings.WheelZoomStepFactor);
        Assert.Equal(300, settings.ZoomAnimationDurationMilliseconds);
        Assert.Equal(1600, settings.MaxZoomPercent);
        Assert.Equal(OpenImageZoomMode.Fit, settings.OpenImageZoomMode);
        Assert.Equal(0, settings.ViewerChromeAutoHideMilliseconds);
        Assert.Equal(HdrHeadroomMode.SystemAdaptive, settings.HdrHeadroomMode);
        Assert.Equal(-4, settings.HdrManualHeadroomStops);
        Assert.Equal(400, settings.SdrWhiteNits);
        Assert.Equal(64, settings.DecodedCacheBudgetMegabytes);
        Assert.Equal(50, settings.MetadataCacheMaxFiles);
        Assert.Equal(16, settings.DirectoryIndexCacheCount);
        Assert.Equal(5, settings.BatchExportMaxRetries);
        Assert.Equal(AppUserSettings.DefaultRtxVsrMode, settings.RtxVsrMode);
        Assert.Equal(AppUserSettings.DefaultRtxVsrQualityPreset, settings.RtxVsrQualityPreset);
        Assert.Equal(2, settings.RtxVsrScaleFactor);
    }

    [Fact]
    public void LoadSettings_ClampsLowerAndUpperBoundsForEachNumber()
    {
        File.WriteAllText(SettingsPath, """
            {
              "WheelZoomStepFactor": 1.0,
              "ZoomAnimationDurationMilliseconds": -1,
              "MaxZoomPercent": 1,
              "HdrManualHeadroomStops": 9.0,
              "SdrWhiteNits": 1,
              "MetadataCacheMaxFiles": 99999,
              "BatchExportMaxRetries": -1,
              "RtxVsrMode": -1,
              "RtxVsrQualityPreset": -1,
              "RtxVsrScaleFactor": 8
            }
            """);

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(AppUserSettings.MinWheelZoomStepFactor, settings.WheelZoomStepFactor);
        Assert.Equal(0, settings.ZoomAnimationDurationMilliseconds);
        Assert.Equal(AppUserSettings.MinMaxZoomPercent, settings.MaxZoomPercent);
        Assert.Equal(AppUserSettings.MaxHdrManualHeadroomStops, settings.HdrManualHeadroomStops);
        Assert.Equal(AppUserSettings.MinSdrWhiteNits, settings.SdrWhiteNits);
        Assert.Equal(AppUserSettings.MaxMetadataCacheMaxFiles, settings.MetadataCacheMaxFiles);
        Assert.Equal(0, settings.BatchExportMaxRetries);
        Assert.Equal(AppUserSettings.DefaultRtxVsrMode, settings.RtxVsrMode);
        Assert.Equal(AppUserSettings.DefaultRtxVsrQualityPreset, settings.RtxVsrQualityPreset);
        Assert.Equal(2, settings.RtxVsrScaleFactor);
    }

    [Fact]
    public void AdvancedSettings_SurviveSaveReloadAndCopy()
    {
        var settings = new AppUserSettings
        {
            ZoomInterpolationUpscale = ZoomInterpolationMode.Lanczos,
            ZoomDownscaleMode = ZoomDownscaleMode.Robidoux,
            WheelZoomStepFactor = 1.25,
            ZoomAnimationDurationMilliseconds = 40,
            MaxZoomPercent = 1200,
            OpenImageZoomMode = OpenImageZoomMode.Last,
            LimitWheelZoomOutToFit = true,
            RestoreLastImageOnStartup = false,
            ViewerChromeAutoHideMilliseconds = 5000,
            ShowStatusFileName = false,
            ShowStatusDimensions = false,
            ShowStatusFileSize = false,
            ShowStatusHdrKind = false,
            HdrHeadroomMode = HdrHeadroomMode.Manual,
            HdrManualHeadroomStops = 1.5,
            SdrWhiteOverrideEnabled = true,
            SdrWhiteNits = 240,
            AdaptiveToneMappingEnabled = true,
            AnalysisAutoRefreshEnabled = false,
            DecodedCacheBudgetMegabytes = 512,
            MetadataCacheMaxFiles = 400,
            DirectoryIndexCacheCount = 8,
            MemoryPressureAutoTrim = false,
            ConfirmBeforeOverwrite = false,
            BatchExportMaxRetries = 4,
            RtxVsrMenuEnabled = true,
            RtxVsrMode = RtxVsrMode.Deblur,
            RtxVsrQualityPreset = RtxVsrQualityPreset.Medium,
            RtxVsrScaleFactor = 4,
        };
        AppSettingsService.SaveSettings(settings, SettingsPath);

        var restored = AppSettingsService.Copy(AppSettingsService.LoadSettings(SettingsPath));

        Assert.Equal(ZoomInterpolationMode.Lanczos, restored.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Robidoux, restored.ZoomDownscaleMode);
        Assert.Null(restored.LegacyZoomInterpolation);
        Assert.Null(restored.LegacyZoomInterpolationDownscale);
        Assert.Equal(1.25, restored.WheelZoomStepFactor);
        Assert.Equal(40, restored.ZoomAnimationDurationMilliseconds);
        Assert.Equal(1200, restored.MaxZoomPercent);
        Assert.Equal(OpenImageZoomMode.Last, restored.OpenImageZoomMode);
        Assert.True(restored.LimitWheelZoomOutToFit);
        Assert.False(restored.RestoreLastImageOnStartup);
        Assert.Equal(5000, restored.ViewerChromeAutoHideMilliseconds);
        Assert.False(restored.ShowStatusFileName);
        Assert.False(restored.ShowStatusDimensions);
        Assert.False(restored.ShowStatusFileSize);
        Assert.False(restored.ShowStatusHdrKind);
        Assert.Equal(HdrHeadroomMode.Manual, restored.HdrHeadroomMode);
        Assert.Equal(1.5, restored.HdrManualHeadroomStops);
        Assert.True(restored.SdrWhiteOverrideEnabled);
        Assert.Equal(240, restored.SdrWhiteNits);
        Assert.True(restored.AdaptiveToneMappingEnabled);
        Assert.False(restored.AnalysisAutoRefreshEnabled);
        Assert.Equal(512, restored.DecodedCacheBudgetMegabytes);
        Assert.Equal(400, restored.MetadataCacheMaxFiles);
        Assert.Equal(8, restored.DirectoryIndexCacheCount);
        Assert.False(restored.MemoryPressureAutoTrim);
        Assert.False(restored.ConfirmBeforeOverwrite);
        Assert.Equal(4, restored.BatchExportMaxRetries);
        Assert.True(restored.RtxVsrMenuEnabled);
        Assert.Equal(RtxVsrMode.Deblur, restored.RtxVsrMode);
        Assert.Equal(RtxVsrQualityPreset.Medium, restored.RtxVsrQualityPreset);
        Assert.Equal(4, restored.RtxVsrScaleFactor);
    }

    [Fact]
    public void ExportAndImportRoundTripsThroughTheService()
    {
        AppSettingsService.SetZoomInterpolationUpscale(ZoomInterpolationMode.Bicubic);
        AppSettingsService.SetZoomDownscaleMode(ZoomDownscaleMode.Kaiser2);
        AppSettingsService.SetMaxZoomPercent(1000);
        AppSettingsService.SetRtxVsrScaleFactor(4);
        AppSettingsService.SetShowStatusHdrKind(false);

        var json = AppSettingsService.ExportSettingsJson();
        Assert.Contains("ZoomInterpolationUpscale", json, StringComparison.Ordinal);
        Assert.Contains("\"ZoomDownscaleMode\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolation\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolationDownscale\"", json, StringComparison.Ordinal);

        Assert.True(AppSettingsService.TryParseSettingsJson(json, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(ZoomInterpolationMode.Bicubic, parsed!.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Kaiser2, parsed.ZoomDownscaleMode);
        Assert.Null(parsed.LegacyZoomInterpolation);
        Assert.Null(parsed.LegacyZoomInterpolationDownscale);
        Assert.Equal(1000, parsed.MaxZoomPercent);
        Assert.Equal(4, parsed.RtxVsrScaleFactor);
        Assert.False(parsed.ShowStatusHdrKind);
    }

    [Fact]
    public void ApplySettings_ReplacesEveryValueAndPersistsIt()
    {
        AppSettingsService.SetMaxZoomPercent(300);
        var replacement = new AppUserSettings
        {
            MaxZoomPercent = 1600,
            Theme = AppTheme.Dark,
            Language = "de-DE",
            RtxVsrMode = RtxVsrMode.Denoise,
            RtxVsrQualityPreset = RtxVsrQualityPreset.High,
        };

        AppSettingsService.ApplySettings(replacement);

        Assert.Equal(1600, AppSettingsService.Current.MaxZoomPercent);
        Assert.Equal(AppTheme.Dark, AppSettingsService.Current.Theme);
        Assert.Equal("de-DE", AppSettingsService.Current.Language);
        Assert.Equal(RtxVsrMode.Denoise, AppSettingsService.Current.RtxVsrMode);
        Assert.Equal(RtxVsrQualityPreset.High, AppSettingsService.Current.RtxVsrQualityPreset);
        Assert.Equal(1600, AppSettingsService.LoadSettings(SettingsPath).MaxZoomPercent);
    }

    [Fact]
    public void ResetAllSettings_RestoresEveryDefault()
    {
        AppSettingsService.SetMaxZoomPercent(1500);
        AppSettingsService.SetZoomInterpolationUpscale(ZoomInterpolationMode.Nearest);
        AppSettingsService.SetZoomDownscaleMode(ZoomDownscaleMode.Mitchell);
        AppSettingsService.SetLanguage("fr-FR");

        AppSettingsService.ResetAllSettings();

        var settings = AppSettingsService.Current;
        Assert.Equal(AppUserSettings.DefaultMaxZoomPercent, settings.MaxZoomPercent);
        Assert.Equal(ZoomInterpolationMode.Auto, settings.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Auto, settings.ZoomDownscaleMode);
        Assert.Equal(string.Empty, settings.Language);
        Assert.Equal(320, settings.DecodedCacheBudgetMegabytes);
        Assert.Equal(AppUserSettings.DefaultRtxVsrMode, settings.RtxVsrMode);
        Assert.Equal(AppUserSettings.DefaultRtxVsrQualityPreset, settings.RtxVsrQualityPreset);
    }

    [Fact]
    public void LegacySingleZoomInterpolation_MigratesBothDirectionsAndDisappearsOnSave()
    {
        File.WriteAllText(SettingsPath, """{"ZoomInterpolation":3,"MaxZoomPercent":900}""");

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(ZoomInterpolationMode.Bicubic, settings.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.CatmullRom, settings.ZoomDownscaleMode);
        Assert.Null(settings.LegacyZoomInterpolation);
        Assert.Null(settings.LegacyZoomInterpolationDownscale);
        Assert.Equal(900, settings.MaxZoomPercent);

        AppSettingsService.SaveSettings(settings, SettingsPath);

        var savedJson = File.ReadAllText(SettingsPath);
        Assert.Contains("\"ZoomInterpolationUpscale\": 3", savedJson, StringComparison.Ordinal);
        Assert.Contains("\"ZoomDownscaleMode\": 3", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolation\"", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolationDownscale\"", savedJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, ZoomInterpolationMode.Auto, ZoomDownscaleMode.Auto)]
    [InlineData(1, ZoomInterpolationMode.Nearest, ZoomDownscaleMode.Nearest)]
    [InlineData(2, ZoomInterpolationMode.Bilinear, ZoomDownscaleMode.Bilinear)]
    [InlineData(3, ZoomInterpolationMode.Bicubic, ZoomDownscaleMode.CatmullRom)]
    [InlineData(4, ZoomInterpolationMode.Lanczos, ZoomDownscaleMode.Lanczos2)]
    public void LegacySingleZoomInterpolation_MapsDownscaleByFilterName(
        int legacyValue,
        ZoomInterpolationMode expectedUpscale,
        ZoomDownscaleMode expectedDownscale)
    {
        File.WriteAllText(SettingsPath, $"{{\"ZoomInterpolation\":{legacyValue}}}");

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(expectedUpscale, settings.ZoomInterpolationUpscale);
        Assert.Equal(expectedDownscale, settings.ZoomDownscaleMode);
        Assert.Null(settings.LegacyZoomInterpolation);
        Assert.Null(settings.LegacyZoomInterpolationDownscale);
    }

    [Theory]
    [InlineData(0, ZoomDownscaleMode.Auto)]
    [InlineData(1, ZoomDownscaleMode.Nearest)]
    [InlineData(2, ZoomDownscaleMode.Bilinear)]
    [InlineData(3, ZoomDownscaleMode.CatmullRom)]
    [InlineData(4, ZoomDownscaleMode.Lanczos2)]
    public void LegacyV122DownscaleMode_MigratesByFilterName(int legacyValue, ZoomDownscaleMode expectedDownscale)
    {
        File.WriteAllText(SettingsPath, $"{{\"ZoomInterpolationDownscale\":{legacyValue}}}");

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(ZoomInterpolationMode.Auto, settings.ZoomInterpolationUpscale);
        Assert.Equal(expectedDownscale, settings.ZoomDownscaleMode);
        Assert.Null(settings.LegacyZoomInterpolation);
        Assert.Null(settings.LegacyZoomInterpolationDownscale);
    }

    [Fact]
    public void LegacyV122DownscaleMode_KeyDisappearsOnSave()
    {
        File.WriteAllText(SettingsPath, """{"ZoomInterpolationDownscale":4,"MaxZoomPercent":900}""");

        var settings = AppSettingsService.LoadSettings(SettingsPath);
        Assert.Equal(ZoomDownscaleMode.Lanczos2, settings.ZoomDownscaleMode);

        AppSettingsService.SaveSettings(settings, SettingsPath);

        var savedJson = File.ReadAllText(SettingsPath);
        Assert.Contains("\"ZoomDownscaleMode\": 6", savedJson, StringComparison.Ordinal);
        Assert.Contains("\"ZoomInterpolationUpscale\": 0", savedJson, StringComparison.Ordinal);
        Assert.Contains("\"MaxZoomPercent\": 900", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolation\"", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolationDownscale\"", savedJson, StringComparison.Ordinal);

        // The file the new build writes must keep loading as the migrated value.
        var reloaded = AppSettingsService.LoadSettings(SettingsPath);
        Assert.Equal(ZoomDownscaleMode.Lanczos2, reloaded.ZoomDownscaleMode);
    }

    [Fact]
    public void LegacyV122DownscaleMode_TakesPrecedenceOverPreSplitSingleValue()
    {
        // A file only contains both keys when it was hand-edited; the newer,
        // more specific v1.2.2 key wins for the downscale direction.
        File.WriteAllText(SettingsPath, """{"ZoomInterpolation":1,"ZoomInterpolationDownscale":3}""");

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(ZoomInterpolationMode.Nearest, settings.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.CatmullRom, settings.ZoomDownscaleMode);
        Assert.Null(settings.LegacyZoomInterpolation);
        Assert.Null(settings.LegacyZoomInterpolationDownscale);
    }

    [Fact]
    public void LegacyDownscaleValue_OutOfRangeFallsBackToAuto()
    {
        File.WriteAllText(SettingsPath, """{"ZoomInterpolationDownscale":99}""");

        var settings = AppSettingsService.LoadSettings(SettingsPath);

        Assert.Equal(ZoomDownscaleMode.Auto, settings.ZoomDownscaleMode);
        Assert.Null(settings.LegacyZoomInterpolationDownscale);
    }

    [Fact]
    public void LegacyKeys_AreNeverWrittenBackEvenWhenSetOnTheObject()
    {
        var settings = new AppUserSettings
        {
            ZoomInterpolationUpscale = ZoomInterpolationMode.Lanczos,
            ZoomDownscaleMode = ZoomDownscaleMode.Area,
            LegacyZoomInterpolation = ZoomInterpolationMode.Bilinear,
            LegacyZoomInterpolationDownscale = ZoomInterpolationMode.Bicubic,
        };

        AppSettingsService.SaveSettings(settings, SettingsPath);

        var savedJson = File.ReadAllText(SettingsPath);
        // SaveSettings serializes a normalised copy, so the legacy keys migrate
        // exactly like LoadSettings would and never reach the document.
        Assert.Contains("\"ZoomInterpolationUpscale\": 2", savedJson, StringComparison.Ordinal);
        Assert.Contains("\"ZoomDownscaleMode\": 3", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolation\"", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolationDownscale\"", savedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseSettingsJson_MigratesLegacyDownscaleKey()
    {
        const string legacyJson = """{"ZoomInterpolationUpscale":2,"ZoomInterpolationDownscale":4}""";

        Assert.True(AppSettingsService.TryParseSettingsJson(legacyJson, out var parsed, out var error), error);

        Assert.NotNull(parsed);
        Assert.Equal(ZoomInterpolationMode.Bilinear, parsed!.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Lanczos2, parsed.ZoomDownscaleMode);
        Assert.Null(parsed.LegacyZoomInterpolationDownscale);
    }

    [Fact]
    public void ZoomSettings_SaveReloadRoundTripThroughSettingsPath()
    {
        AppSettingsService.SetZoomInterpolationUpscale(ZoomInterpolationMode.Lanczos);
        AppSettingsService.SetZoomDownscaleMode(ZoomDownscaleMode.Area);
        AppSettingsService.FlushPendingSave();

        Assert.True(File.Exists(SettingsPath));
        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"ZoomInterpolationUpscale\": 4", json, StringComparison.Ordinal);
        Assert.Contains("\"ZoomDownscaleMode\": 8", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolation\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZoomInterpolationDownscale\"", json, StringComparison.Ordinal);

        var reloaded = AppSettingsService.LoadSettings(SettingsPath);
        Assert.Equal(ZoomInterpolationMode.Lanczos, reloaded.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Area, reloaded.ZoomDownscaleMode);

        var copy = AppSettingsService.Copy(AppSettingsService.Current);
        Assert.Equal(ZoomInterpolationMode.Lanczos, copy.ZoomInterpolationUpscale);
        Assert.Equal(ZoomDownscaleMode.Area, copy.ZoomDownscaleMode);
        Assert.Null(copy.LegacyZoomInterpolation);
        Assert.Null(copy.LegacyZoomInterpolationDownscale);
    }

    [Fact]
    public void ZoomNormalizers_RepairInvalidValues()
    {
        Assert.Equal(
            ZoomInterpolationMode.Auto,
            AppUserSettings.NormalizeZoomInterpolationUpscale((ZoomInterpolationMode)99));
        Assert.Equal(
            ZoomDownscaleMode.Auto,
            AppUserSettings.NormalizeZoomDownscaleMode((ZoomDownscaleMode)(-1)));
        Assert.Equal(
            ZoomDownscaleMode.Auto,
            AppUserSettings.NormalizeZoomDownscaleMode((ZoomDownscaleMode)99));
        Assert.Equal(
            ZoomInterpolationMode.Lanczos,
            AppUserSettings.NormalizeZoomInterpolationUpscale(ZoomInterpolationMode.Lanczos));
        Assert.Equal(
            ZoomDownscaleMode.Kaiser2,
            AppUserSettings.NormalizeZoomDownscaleMode(ZoomDownscaleMode.Kaiser2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void TryParseSettingsJson_RejectsInvalidDocuments(string json)
    {
        Assert.False(AppSettingsService.TryParseSettingsJson(json, out var parsed, out _));
        Assert.Null(parsed);
    }

    [Fact]
    public void SettingsFilePath_ReportsOverriddenPath()
    {
        Assert.Equal(Path.GetFullPath(SettingsPath), Path.GetFullPath(AppSettingsService.SettingsFilePath));
        Assert.Equal(
            Path.GetFullPath(_directory),
            Path.GetFullPath(AppSettingsService.SettingsDirectoryPath));
    }

    public void Dispose()
    {
        _scope.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
