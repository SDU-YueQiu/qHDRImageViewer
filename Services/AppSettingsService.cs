using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using HdrImageViewer.Models;
using HdrImageViewer.Rendering;

namespace HdrImageViewer.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppUserSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}

public enum MouseWheelBehavior
{
    NavigateImages,
    ZoomImage,
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed class AppUserSettings
{
    public const int DefaultAdjacentPreloadRadius = 5;
    public const int MinAdjacentPreloadRadius = 1;
    public const int MaxAdjacentPreloadRadius = 12;

    public const double DefaultWheelZoomStepFactor = 1.1;
    public const double MinWheelZoomStepFactor = 1.02;
    public const double MaxWheelZoomStepFactor = 1.5;

    public const int DefaultZoomAnimationDurationMilliseconds = 140;
    public const int MinZoomAnimationDurationMilliseconds = 0;
    public const int MaxZoomAnimationDurationMilliseconds = 300;

    public const int DefaultMaxZoomPercent = 800;
    public const int MinMaxZoomPercent = 100;
    public const int MaxMaxZoomPercent = 1600;

    public const int DefaultViewerChromeAutoHideMilliseconds = 2200;
    public const int MinViewerChromeAutoHideMilliseconds = 0;
    public const int MaxViewerChromeAutoHideMilliseconds = 10000;

    public const double DefaultHdrManualHeadroomStops = 0;
    public const double MinHdrManualHeadroomStops = -4;
    public const double MaxHdrManualHeadroomStops = 4;

    public const int DefaultSdrWhiteNits = 203;
    public const int MinSdrWhiteNits = 80;
    public const int MaxSdrWhiteNits = 400;

    public const int DefaultDecodedCacheBudgetMegabytes = 320;
    public const int MinDecodedCacheBudgetMegabytes = 64;
    public const int MaxDecodedCacheBudgetMegabytes = 1024;

    public const int DefaultMetadataCacheMaxFiles = 200;
    public const int MinMetadataCacheMaxFiles = 50;
    public const int MaxMetadataCacheMaxFiles = 1000;

    public const int DefaultDirectoryIndexCacheCount = 4;
    public const int MinDirectoryIndexCacheCount = 1;
    public const int MaxDirectoryIndexCacheCount = 16;

    public const int DefaultBatchExportMaxRetries = 1;
    public const int MinBatchExportMaxRetries = 0;
    public const int MaxBatchExportMaxRetries = 5;

    public const RtxVsrMode DefaultRtxVsrMode = RtxVsrMode.HighBitrate;
    public const RtxVsrQualityPreset DefaultRtxVsrQualityPreset = RtxVsrQualityPreset.Ultra;
    public const int DefaultRtxVsrScaleFactor = 2;

    public MouseWheelBehavior MouseWheelBehavior { get; set; } = MouseWheelBehavior.NavigateImages;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool TouchpadGesturesEnabled { get; set; } = true;

    public bool PreloadAdjacentImages { get; set; } = true;

    public int AdjacentPreloadRadius { get; set; } = DefaultAdjacentPreloadRadius;

    public bool SkipCloudPlaceholders { get; set; } = true;

    public bool ShowInspectorPanel { get; set; } = true;

    public bool ShowFilmstrip { get; set; } = true;

    public ColorGamutMappingMode ColorGamutMappingMode { get; set; } = ColorGamutMappingMode.Managed;

    public GainmapViewMode HdrViewMode { get; set; } = GainmapViewMode.Adaptive;

    public string Language { get; set; } = string.Empty;

    public ZoomInterpolationMode ZoomInterpolationUpscale { get; set; } = ZoomInterpolationMode.Auto;

    public ZoomDownscaleMode ZoomDownscaleMode { get; set; } = ZoomDownscaleMode.Auto;

    /// <summary>
    /// Legacy single zoom-interpolation preference written by builds that had
    /// only one setting. It is only read while loading: migration copies its
    /// value into both direction-specific preferences and clears it, so it is
    /// never written back to disk after the first save.
    /// </summary>
    [JsonPropertyName("ZoomInterpolation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZoomInterpolationMode? LegacyZoomInterpolation { get; set; }

    /// <summary>
    /// Legacy downscale preference written by v1.2.2 builds, which still used
    /// the shared <see cref="ZoomInterpolationMode"/> enum for the downscale
    /// direction. Read only while loading: migration maps it onto
    /// <see cref="ZoomDownscaleMode"/> by filter name and clears it, so the old
    /// key is never written back.
    /// </summary>
    [JsonPropertyName("ZoomInterpolationDownscale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZoomInterpolationMode? LegacyZoomInterpolationDownscale { get; set; }

    public double WheelZoomStepFactor { get; set; } = DefaultWheelZoomStepFactor;

    public int ZoomAnimationDurationMilliseconds { get; set; } = DefaultZoomAnimationDurationMilliseconds;

    public int MaxZoomPercent { get; set; } = DefaultMaxZoomPercent;

    public OpenImageZoomMode OpenImageZoomMode { get; set; } = OpenImageZoomMode.Fit;

    public bool LimitWheelZoomOutToFit { get; set; }

    public bool RestoreLastImageOnStartup { get; set; } = true;

    public int ViewerChromeAutoHideMilliseconds { get; set; } = DefaultViewerChromeAutoHideMilliseconds;

    public bool ShowStatusFileName { get; set; } = true;

    public bool ShowStatusDimensions { get; set; } = true;

    public bool ShowStatusFileSize { get; set; } = true;

    public bool ShowStatusHdrKind { get; set; } = true;

    public HdrHeadroomMode HdrHeadroomMode { get; set; } = HdrHeadroomMode.SystemAdaptive;

    public double HdrManualHeadroomStops { get; set; } = DefaultHdrManualHeadroomStops;

    public bool SdrWhiteOverrideEnabled { get; set; }

    public int SdrWhiteNits { get; set; } = DefaultSdrWhiteNits;

    public bool AdaptiveToneMappingEnabled { get; set; }

    public bool AnalysisAutoRefreshEnabled { get; set; } = true;

    public int DecodedCacheBudgetMegabytes { get; set; } = DefaultDecodedCacheBudgetMegabytes;

    public int MetadataCacheMaxFiles { get; set; } = DefaultMetadataCacheMaxFiles;

    public int DirectoryIndexCacheCount { get; set; } = DefaultDirectoryIndexCacheCount;

    public bool MemoryPressureAutoTrim { get; set; } = true;

    public bool ConfirmBeforeOverwrite { get; set; } = true;

    public int BatchExportMaxRetries { get; set; } = DefaultBatchExportMaxRetries;

    public bool RtxVsrMenuEnabled { get; set; } = true;

    public RtxVsrMode RtxVsrMode { get; set; } = DefaultRtxVsrMode;

    public RtxVsrQualityPreset RtxVsrQualityPreset { get; set; } = DefaultRtxVsrQualityPreset;

    public int RtxVsrScaleFactor { get; set; } = DefaultRtxVsrScaleFactor;

    public static GainmapViewMode NormalizeHdrViewMode(GainmapViewMode mode)
    {
        return Enum.IsDefined(mode) ? mode : GainmapViewMode.Adaptive;
    }

    public static ZoomInterpolationMode NormalizeZoomInterpolationUpscale(ZoomInterpolationMode mode)
    {
        return Enum.IsDefined(mode) ? mode : ZoomInterpolationMode.Auto;
    }

    public static ZoomDownscaleMode NormalizeZoomDownscaleMode(ZoomDownscaleMode mode)
    {
        return ZoomDownscaleModes.Normalize(mode);
    }

    public static OpenImageZoomMode NormalizeOpenImageZoomMode(OpenImageZoomMode mode)
    {
        return Enum.IsDefined(mode) ? mode : OpenImageZoomMode.Fit;
    }

    public static HdrHeadroomMode NormalizeHdrHeadroomMode(HdrHeadroomMode mode)
    {
        // Only the system-adaptive and manual headroom sources are user preferences;
        // every other value (including the internal ABL soft-proof mode) resets.
        return mode == HdrHeadroomMode.Manual ? HdrHeadroomMode.Manual : HdrHeadroomMode.SystemAdaptive;
    }

    public static double NormalizeWheelZoomStepFactor(double factor)
    {
        return double.IsFinite(factor)
            ? Math.Clamp(factor, MinWheelZoomStepFactor, MaxWheelZoomStepFactor)
            : DefaultWheelZoomStepFactor;
    }

    public static int NormalizeZoomAnimationDurationMilliseconds(int milliseconds)
    {
        return Math.Clamp(milliseconds, MinZoomAnimationDurationMilliseconds, MaxZoomAnimationDurationMilliseconds);
    }

    public static int NormalizeMaxZoomPercent(int percent)
    {
        return Math.Clamp(percent, MinMaxZoomPercent, MaxMaxZoomPercent);
    }

    public static int NormalizeViewerChromeAutoHideMilliseconds(int milliseconds)
    {
        return Math.Clamp(milliseconds, MinViewerChromeAutoHideMilliseconds, MaxViewerChromeAutoHideMilliseconds);
    }

    public static double NormalizeHdrManualHeadroomStops(double stops)
    {
        return double.IsFinite(stops)
            ? Math.Clamp(stops, MinHdrManualHeadroomStops, MaxHdrManualHeadroomStops)
            : DefaultHdrManualHeadroomStops;
    }

    public static int NormalizeSdrWhiteNits(int nits)
    {
        return Math.Clamp(nits, MinSdrWhiteNits, MaxSdrWhiteNits);
    }

    public static int NormalizeDecodedCacheBudgetMegabytes(int megabytes)
    {
        return Math.Clamp(megabytes, MinDecodedCacheBudgetMegabytes, MaxDecodedCacheBudgetMegabytes);
    }

    public static int NormalizeMetadataCacheMaxFiles(int files)
    {
        return Math.Clamp(files, MinMetadataCacheMaxFiles, MaxMetadataCacheMaxFiles);
    }

    public static int NormalizeDirectoryIndexCacheCount(int count)
    {
        return Math.Clamp(count, MinDirectoryIndexCacheCount, MaxDirectoryIndexCacheCount);
    }

    public static int NormalizeBatchExportMaxRetries(int retries)
    {
        return Math.Clamp(retries, MinBatchExportMaxRetries, MaxBatchExportMaxRetries);
    }

    public static RtxVsrMode NormalizeRtxVsrMode(RtxVsrMode mode)
    {
        return Enum.IsDefined(mode) ? mode : DefaultRtxVsrMode;
    }

    public static RtxVsrQualityPreset NormalizeRtxVsrQualityPreset(RtxVsrQualityPreset preset)
    {
        return Enum.IsDefined(preset) ? preset : DefaultRtxVsrQualityPreset;
    }

    public static int NormalizeRtxVsrScaleFactor(int scaleFactor)
    {
        return scaleFactor == 4 ? 4 : DefaultRtxVsrScaleFactor;
    }

    public static int NormalizeAdjacentPreloadRadius(int radius)
    {
        return Math.Clamp(radius, MinAdjacentPreloadRadius, MaxAdjacentPreloadRadius);
    }

    public GainmapViewMode GetEffectiveHdrViewMode(bool hasGainMap, bool isSingleLayerHdr)
    {
        // A document-specific fallback must never replace the saved preference.
        if (!hasGainMap && !isSingleLayerHdr)
        {
            return GainmapViewMode.Sdr;
        }

        var preferredMode = NormalizeHdrViewMode(HdrViewMode);
        return preferredMode == GainmapViewMode.GainMap && !hasGainMap
            ? GainmapViewMode.Adaptive
            : preferredMode;
    }
}

public static class AppSettingsService
{
    internal const string SettingsPathEnvironmentVariable = "HDRIV_SETTINGS_PATH";
    private const int SaveDebounceMilliseconds = 150;

    private static readonly object s_lock = new();
    private static readonly object s_saveGate = new();
    private static readonly Timer s_saveTimer = new(static _ => FlushPendingSave(), null, Timeout.Infinite, Timeout.Infinite);
    private static string s_settingsPath = ResolveDefaultSettingsPath();
    private static AppUserSettings? s_current;
    private static PendingSave? s_pendingSave;
    private static long s_saveVersion;

    static AppSettingsService()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => FlushPendingSave();
    }

    public static event EventHandler? SettingsChanged;

    public static AppUserSettings Current => Volatile.Read(ref s_current) ?? EnsureLoaded();

    /// <summary>
    /// Gets the full path of the JSON file that stores the user settings.
    /// </summary>
    public static string SettingsFilePath
    {
        get
        {
            lock (s_lock)
            {
                return s_settingsPath;
            }
        }
    }

    /// <summary>
    /// Gets the folder that contains the JSON settings file.
    /// </summary>
    public static string SettingsDirectoryPath
    {
        get
        {
            var path = SettingsFilePath;
            return Path.GetDirectoryName(path) ?? path;
        }
    }

    public static void SetMouseWheelBehavior(MouseWheelBehavior behavior)
    {
        UpdateSettings(settings => settings.MouseWheelBehavior = behavior);
    }

    public static void SetTheme(AppTheme theme)
    {
        UpdateSettings(settings => settings.Theme = Enum.IsDefined(theme) ? theme : AppTheme.System);
    }

    public static void SetTouchpadGesturesEnabled(bool enabled)
    {
        UpdateSettings(settings => settings.TouchpadGesturesEnabled = enabled);
    }

    public static void SetPreloadAdjacentImages(bool enabled)
    {
        UpdateSettings(settings => settings.PreloadAdjacentImages = enabled);
    }

    public static void SetAdjacentPreloadRadius(int radius)
    {
        UpdateSettings(settings => settings.AdjacentPreloadRadius = AppUserSettings.NormalizeAdjacentPreloadRadius(radius));
    }

    public static void SetSkipCloudPlaceholders(bool enabled)
    {
        UpdateSettings(settings => settings.SkipCloudPlaceholders = enabled);
    }

    public static void SetShowInspectorPanel(bool enabled)
    {
        UpdateSettings(settings => settings.ShowInspectorPanel = enabled);
    }

    public static void SetShowFilmstrip(bool enabled)
    {
        UpdateSettings(settings => settings.ShowFilmstrip = enabled);
    }

    public static void SetColorGamutMappingMode(ColorGamutMappingMode mode)
    {
        UpdateSettings(settings => settings.ColorGamutMappingMode = Enum.IsDefined(mode)
            ? mode
            : ColorGamutMappingMode.Managed);
    }

    public static void SetLanguage(string language)
    {
        UpdateSettings(settings => settings.Language = language ?? string.Empty);
        FlushPendingSave();
    }

    public static void SetHdrViewMode(GainmapViewMode mode)
    {
        UpdateSettings(settings => settings.HdrViewMode = AppUserSettings.NormalizeHdrViewMode(mode));
    }

    public static void SetZoomInterpolationUpscale(ZoomInterpolationMode mode)
    {
        UpdateSettings(settings => settings.ZoomInterpolationUpscale = AppUserSettings.NormalizeZoomInterpolationUpscale(mode));
    }

    public static void SetZoomDownscaleMode(ZoomDownscaleMode mode)
    {
        UpdateSettings(settings => settings.ZoomDownscaleMode = AppUserSettings.NormalizeZoomDownscaleMode(mode));
    }

    public static void SetWheelZoomStepFactor(double factor)
    {
        UpdateSettings(settings => settings.WheelZoomStepFactor = AppUserSettings.NormalizeWheelZoomStepFactor(factor));
    }

    public static void SetZoomAnimationDurationMilliseconds(int milliseconds)
    {
        UpdateSettings(settings =>
            settings.ZoomAnimationDurationMilliseconds = AppUserSettings.NormalizeZoomAnimationDurationMilliseconds(milliseconds));
    }

    public static void SetMaxZoomPercent(int percent)
    {
        UpdateSettings(settings => settings.MaxZoomPercent = AppUserSettings.NormalizeMaxZoomPercent(percent));
    }

    public static void SetOpenImageZoomMode(OpenImageZoomMode mode)
    {
        UpdateSettings(settings => settings.OpenImageZoomMode = AppUserSettings.NormalizeOpenImageZoomMode(mode));
    }

    public static void SetLimitWheelZoomOutToFit(bool enabled)
    {
        UpdateSettings(settings => settings.LimitWheelZoomOutToFit = enabled);
    }

    public static void SetRestoreLastImageOnStartup(bool enabled)
    {
        UpdateSettings(settings => settings.RestoreLastImageOnStartup = enabled);
    }

    public static void SetViewerChromeAutoHideMilliseconds(int milliseconds)
    {
        UpdateSettings(settings =>
            settings.ViewerChromeAutoHideMilliseconds = AppUserSettings.NormalizeViewerChromeAutoHideMilliseconds(milliseconds));
    }

    public static void SetShowStatusFileName(bool enabled)
    {
        UpdateSettings(settings => settings.ShowStatusFileName = enabled);
    }

    public static void SetShowStatusDimensions(bool enabled)
    {
        UpdateSettings(settings => settings.ShowStatusDimensions = enabled);
    }

    public static void SetShowStatusFileSize(bool enabled)
    {
        UpdateSettings(settings => settings.ShowStatusFileSize = enabled);
    }

    public static void SetShowStatusHdrKind(bool enabled)
    {
        UpdateSettings(settings => settings.ShowStatusHdrKind = enabled);
    }

    public static void SetHdrHeadroomMode(HdrHeadroomMode mode)
    {
        UpdateSettings(settings => settings.HdrHeadroomMode = AppUserSettings.NormalizeHdrHeadroomMode(mode));
    }

    public static void SetHdrManualHeadroomStops(double stops)
    {
        UpdateSettings(settings => settings.HdrManualHeadroomStops = AppUserSettings.NormalizeHdrManualHeadroomStops(stops));
    }

    public static void SetSdrWhiteOverrideEnabled(bool enabled)
    {
        UpdateSettings(settings => settings.SdrWhiteOverrideEnabled = enabled);
    }

    public static void SetSdrWhiteNits(int nits)
    {
        UpdateSettings(settings => settings.SdrWhiteNits = AppUserSettings.NormalizeSdrWhiteNits(nits));
    }

    public static void SetAdaptiveToneMappingEnabled(bool enabled)
    {
        UpdateSettings(settings => settings.AdaptiveToneMappingEnabled = enabled);
    }

    public static void SetAnalysisAutoRefreshEnabled(bool enabled)
    {
        UpdateSettings(settings => settings.AnalysisAutoRefreshEnabled = enabled);
    }

    public static void SetDecodedCacheBudgetMegabytes(int megabytes)
    {
        UpdateSettings(settings =>
            settings.DecodedCacheBudgetMegabytes = AppUserSettings.NormalizeDecodedCacheBudgetMegabytes(megabytes));
    }

    public static void SetMetadataCacheMaxFiles(int files)
    {
        UpdateSettings(settings => settings.MetadataCacheMaxFiles = AppUserSettings.NormalizeMetadataCacheMaxFiles(files));
    }

    public static void SetDirectoryIndexCacheCount(int count)
    {
        UpdateSettings(settings => settings.DirectoryIndexCacheCount = AppUserSettings.NormalizeDirectoryIndexCacheCount(count));
    }

    public static void SetMemoryPressureAutoTrim(bool enabled)
    {
        UpdateSettings(settings => settings.MemoryPressureAutoTrim = enabled);
    }

    public static void SetConfirmBeforeOverwrite(bool enabled)
    {
        UpdateSettings(settings => settings.ConfirmBeforeOverwrite = enabled);
    }

    public static void SetBatchExportMaxRetries(int retries)
    {
        UpdateSettings(settings => settings.BatchExportMaxRetries = AppUserSettings.NormalizeBatchExportMaxRetries(retries));
    }

    public static void SetRtxVsrMenuEnabled(bool enabled)
    {
        UpdateSettings(settings => settings.RtxVsrMenuEnabled = enabled);
    }

    public static void SetRtxVsrMode(RtxVsrMode mode)
    {
        UpdateSettings(settings => settings.RtxVsrMode = AppUserSettings.NormalizeRtxVsrMode(mode));
    }

    public static void SetRtxVsrQualityPreset(RtxVsrQualityPreset preset)
    {
        UpdateSettings(settings =>
            settings.RtxVsrQualityPreset = AppUserSettings.NormalizeRtxVsrQualityPreset(preset));
    }

    public static void SetRtxVsrScaleFactor(int scaleFactor)
    {
        UpdateSettings(settings => settings.RtxVsrScaleFactor = AppUserSettings.NormalizeRtxVsrScaleFactor(scaleFactor));
    }

    /// <summary>
    /// Serializes the current settings to the JSON document that is written to disk.
    /// </summary>
    public static string ExportSettingsJson()
    {
        return JsonSerializer.Serialize(Copy(Current), AppSettingsJsonContext.Default.AppUserSettings);
    }

    /// <summary>
    /// Parses and normalizes a settings JSON document without applying it.
    /// </summary>
    public static bool TryParseSettingsJson(
        string json,
        [NotNullWhen(true)] out AppUserSettings? settings,
        out string? error)
    {
        settings = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The settings document is empty.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppUserSettings);
            if (parsed is null)
            {
                error = "The settings document did not contain a settings object.";
                return false;
            }

            settings = NormalizeSettings(parsed);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Replaces every persisted setting with the supplied document and saves immediately.
    /// </summary>
    public static void ApplySettings(AppUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ReplaceAllSettings(settings);
        FlushPendingSave();
    }

    /// <summary>
    /// Restores every persisted setting to its documented default value.
    /// </summary>
    public static void ResetAllSettings()
    {
        ApplySettings(new AppUserSettings());
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void FlushPendingSave()
    {
        lock (s_saveGate)
        {
            PendingSave? pending;
            lock (s_lock)
            {
                pending = s_pendingSave;
                if (pending is null)
                {
                    return;
                }

                s_saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            while (pending is not null)
            {
                SaveSettings(pending.Settings, pending.Path);

                lock (s_lock)
                {
                    if (s_pendingSave is not null && s_pendingSave.Version == pending.Version)
                    {
                        s_pendingSave = null;
                        pending = null;
                    }
                    else
                    {
                        pending = s_pendingSave;
                        if (pending is not null)
                        {
                            s_saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        }
                    }
                }
            }
        }
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static IDisposable OverrideFilePathForTesting(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        FlushPendingSave();

        string previousPath;
        string? previousEnvironmentValue;
        lock (s_lock)
        {
            previousPath = s_settingsPath;
            previousEnvironmentValue = Environment.GetEnvironmentVariable(SettingsPathEnvironmentVariable);
            s_settingsPath = fullPath;
            Environment.SetEnvironmentVariable(SettingsPathEnvironmentVariable, fullPath);
            Volatile.Write(ref s_current, LoadSettings(fullPath));
        }

        return new SettingsPathOverrideScope(previousPath, previousEnvironmentValue);
    }

    private static AppUserSettings EnsureLoaded()
    {
        lock (s_lock)
        {
            var existing = s_current;
            if (existing is not null)
            {
                return existing;
            }

            var loaded = LoadSettings(s_settingsPath);
            Volatile.Write(ref s_current, loaded);
            return loaded;
        }
    }

    private static void UpdateSettings(Action<AppUserSettings> update)
    {
        lock (s_lock)
        {
            var current = s_current ?? LoadSettings(s_settingsPath);
            var next = Copy(current);
            update(next);
            next = Copy(next);
            Volatile.Write(ref s_current, next);
            var version = ++s_saveVersion;
            s_pendingSave = new PendingSave(next, s_settingsPath, version);
            s_saveTimer.Change(SaveDebounceMilliseconds, Timeout.Infinite);
        }

        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ReplaceAllSettings(AppUserSettings replacement)
    {
        lock (s_lock)
        {
            var next = Copy(replacement);
            Volatile.Write(ref s_current, next);
            var version = ++s_saveVersion;
            s_pendingSave = new PendingSave(next, s_settingsPath, version);
            s_saveTimer.Change(SaveDebounceMilliseconds, Timeout.Infinite);
        }

        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    private const string ProductDirectoryName = "qHdrImageViewer";
    private const string LegacyProductDirectoryName = "HdrImageViewer";

    private static string ResolveDefaultSettingsPath()
    {
        var environmentPath = Environment.GetEnvironmentVariable(SettingsPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return Path.GetFullPath(environmentPath);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsPath = Path.Combine(localAppData, ProductDirectoryName, "settings.json");
        TryMigrateLegacySettings(settingsPath, Path.Combine(localAppData, LegacyProductDirectoryName, "settings.json"));
        return settingsPath;
    }

    /// <summary>
    /// The product was renamed to qHdrImageViewer; keep the settings created by
    /// earlier builds (folder HdrImageViewer) instead of starting from defaults.
    /// </summary>
    private static void TryMigrateLegacySettings(string settingsPath, string legacySettingsPath)
    {
        try
        {
            if (File.Exists(settingsPath) || !File.Exists(legacySettingsPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(legacySettingsPath, settingsPath, overwrite: false);
            Debug.WriteLine($"[AppSettingsService] Migrated legacy settings from '{legacySettingsPath}'.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsService] Failed to migrate legacy settings: {ex.Message}");
        }
    }

    internal static AppUserSettings LoadSettings(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new AppUserSettings();
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppUserSettings)
                ?? new AppUserSettings();
            return NormalizeSettings(settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsService] Failed to load settings from '{settingsPath}': {ex.Message}");
            return new AppUserSettings();
        }
    }

    /// <summary>
    /// Clamps or repairs every value that can be loaded from disk or imported from JSON.
    /// Missing JSON members keep their property default, so old settings files keep working.
    /// </summary>
    internal static AppUserSettings NormalizeSettings(AppUserSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = AppTheme.System;
        }

        if (!Enum.IsDefined(settings.ColorGamutMappingMode))
        {
            settings.ColorGamutMappingMode = ColorGamutMappingMode.Managed;
        }

        if (!Enum.IsDefined(settings.MouseWheelBehavior))
        {
            settings.MouseWheelBehavior = MouseWheelBehavior.NavigateImages;
        }

        settings.AdjacentPreloadRadius = AppUserSettings.NormalizeAdjacentPreloadRadius(settings.AdjacentPreloadRadius);
        settings.HdrViewMode = AppUserSettings.NormalizeHdrViewMode(settings.HdrViewMode);
        settings.Language ??= string.Empty;
        settings.ZoomInterpolationUpscale =
            AppUserSettings.NormalizeZoomInterpolationUpscale(settings.ZoomInterpolationUpscale);
        settings.ZoomDownscaleMode =
            AppUserSettings.NormalizeZoomDownscaleMode(settings.ZoomDownscaleMode);
        if (settings.LegacyZoomInterpolation is { } legacyZoomInterpolation)
        {
            // Settings files written before the upscale/downscale split stored a
            // single value. Apply it to both directions so the rendering matches
            // the last explicit choice, then clear it so it is never saved again.
            var migrated = AppUserSettings.NormalizeZoomInterpolationUpscale(legacyZoomInterpolation);
            settings.ZoomInterpolationUpscale = migrated;
            settings.ZoomDownscaleMode = ZoomDownscaleModes.FromLegacy(migrated);
            settings.LegacyZoomInterpolation = null;
        }

        if (settings.LegacyZoomInterpolationDownscale is { } legacyZoomInterpolationDownscale)
        {
            // v1.2.2 wrote a dedicated downscale member that still used the old
            // shared ZoomInterpolationMode enum. Map it by filter name (Bicubic
            // -> CatmullRom, Lanczos -> Lanczos2) and drop the legacy key. This
            // value is more specific than the pre-split single value, so it wins
            // if a hand-edited file contains both.
            settings.ZoomDownscaleMode = ZoomDownscaleModes.FromLegacy(
                AppUserSettings.NormalizeZoomInterpolationUpscale(legacyZoomInterpolationDownscale));
            settings.LegacyZoomInterpolationDownscale = null;
        }
        settings.WheelZoomStepFactor = AppUserSettings.NormalizeWheelZoomStepFactor(settings.WheelZoomStepFactor);
        settings.ZoomAnimationDurationMilliseconds =
            AppUserSettings.NormalizeZoomAnimationDurationMilliseconds(settings.ZoomAnimationDurationMilliseconds);
        settings.MaxZoomPercent = AppUserSettings.NormalizeMaxZoomPercent(settings.MaxZoomPercent);
        settings.OpenImageZoomMode = AppUserSettings.NormalizeOpenImageZoomMode(settings.OpenImageZoomMode);
        settings.ViewerChromeAutoHideMilliseconds =
            AppUserSettings.NormalizeViewerChromeAutoHideMilliseconds(settings.ViewerChromeAutoHideMilliseconds);
        settings.HdrHeadroomMode = AppUserSettings.NormalizeHdrHeadroomMode(settings.HdrHeadroomMode);
        settings.HdrManualHeadroomStops = AppUserSettings.NormalizeHdrManualHeadroomStops(settings.HdrManualHeadroomStops);
        settings.SdrWhiteNits = AppUserSettings.NormalizeSdrWhiteNits(settings.SdrWhiteNits);
        settings.DecodedCacheBudgetMegabytes =
            AppUserSettings.NormalizeDecodedCacheBudgetMegabytes(settings.DecodedCacheBudgetMegabytes);
        settings.MetadataCacheMaxFiles = AppUserSettings.NormalizeMetadataCacheMaxFiles(settings.MetadataCacheMaxFiles);
        settings.DirectoryIndexCacheCount = AppUserSettings.NormalizeDirectoryIndexCacheCount(settings.DirectoryIndexCacheCount);
        settings.BatchExportMaxRetries = AppUserSettings.NormalizeBatchExportMaxRetries(settings.BatchExportMaxRetries);
        settings.RtxVsrMode = AppUserSettings.NormalizeRtxVsrMode(settings.RtxVsrMode);
        settings.RtxVsrQualityPreset = AppUserSettings.NormalizeRtxVsrQualityPreset(settings.RtxVsrQualityPreset);
        settings.RtxVsrScaleFactor = AppUserSettings.NormalizeRtxVsrScaleFactor(settings.RtxVsrScaleFactor);
        return settings;
    }

    internal static void SaveSettings(AppUserSettings settings, string settingsPath)
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Serialize a normalized copy so a legacy ZoomInterpolation value can
            // never leak back to disk after it has been migrated.
            var json = JsonSerializer.Serialize(Copy(settings), AppSettingsJsonContext.Default.AppUserSettings);
            temporaryPath = settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, settingsPath, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsService] Failed to save settings to '{settingsPath}': {ex.Message}");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"[AppSettingsService] Failed to delete temporary settings file '{temporaryPath}': {cleanupEx.Message}");
                }
            }
        }
    }

    internal static AppUserSettings Copy(AppUserSettings settings)
    {
        var copy = new AppUserSettings
        {
            MouseWheelBehavior = settings.MouseWheelBehavior,
            Theme = settings.Theme,
            TouchpadGesturesEnabled = settings.TouchpadGesturesEnabled,
            PreloadAdjacentImages = settings.PreloadAdjacentImages,
            AdjacentPreloadRadius = settings.AdjacentPreloadRadius,
            SkipCloudPlaceholders = settings.SkipCloudPlaceholders,
            ShowInspectorPanel = settings.ShowInspectorPanel,
            ShowFilmstrip = settings.ShowFilmstrip,
            ColorGamutMappingMode = settings.ColorGamutMappingMode,
            Language = settings.Language ?? string.Empty,
            HdrViewMode = settings.HdrViewMode,
            ZoomInterpolationUpscale = settings.ZoomInterpolationUpscale,
            ZoomDownscaleMode = settings.ZoomDownscaleMode,
            LegacyZoomInterpolation = settings.LegacyZoomInterpolation,
            LegacyZoomInterpolationDownscale = settings.LegacyZoomInterpolationDownscale,
            WheelZoomStepFactor = settings.WheelZoomStepFactor,
            ZoomAnimationDurationMilliseconds = settings.ZoomAnimationDurationMilliseconds,
            MaxZoomPercent = settings.MaxZoomPercent,
            OpenImageZoomMode = settings.OpenImageZoomMode,
            LimitWheelZoomOutToFit = settings.LimitWheelZoomOutToFit,
            RestoreLastImageOnStartup = settings.RestoreLastImageOnStartup,
            ViewerChromeAutoHideMilliseconds = settings.ViewerChromeAutoHideMilliseconds,
            ShowStatusFileName = settings.ShowStatusFileName,
            ShowStatusDimensions = settings.ShowStatusDimensions,
            ShowStatusFileSize = settings.ShowStatusFileSize,
            ShowStatusHdrKind = settings.ShowStatusHdrKind,
            HdrHeadroomMode = settings.HdrHeadroomMode,
            HdrManualHeadroomStops = settings.HdrManualHeadroomStops,
            SdrWhiteOverrideEnabled = settings.SdrWhiteOverrideEnabled,
            SdrWhiteNits = settings.SdrWhiteNits,
            AdaptiveToneMappingEnabled = settings.AdaptiveToneMappingEnabled,
            AnalysisAutoRefreshEnabled = settings.AnalysisAutoRefreshEnabled,
            DecodedCacheBudgetMegabytes = settings.DecodedCacheBudgetMegabytes,
            MetadataCacheMaxFiles = settings.MetadataCacheMaxFiles,
            DirectoryIndexCacheCount = settings.DirectoryIndexCacheCount,
            MemoryPressureAutoTrim = settings.MemoryPressureAutoTrim,
            ConfirmBeforeOverwrite = settings.ConfirmBeforeOverwrite,
            BatchExportMaxRetries = settings.BatchExportMaxRetries,
            RtxVsrMenuEnabled = settings.RtxVsrMenuEnabled,
            RtxVsrMode = settings.RtxVsrMode,
            RtxVsrQualityPreset = settings.RtxVsrQualityPreset,
            RtxVsrScaleFactor = settings.RtxVsrScaleFactor,
        };
        return NormalizeSettings(copy);
    }

    private sealed record PendingSave(AppUserSettings Settings, string Path, long Version);

    private sealed class SettingsPathOverrideScope(string previousPath, string? previousEnvironmentValue) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            FlushPendingSave();
            lock (s_lock)
            {
                s_settingsPath = previousPath;
                Environment.SetEnvironmentVariable(SettingsPathEnvironmentVariable, previousEnvironmentValue);
                Volatile.Write(ref s_current, LoadSettings(previousPath));
            }
        }
    }
}
