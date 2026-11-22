using HdrImageViewer.Models;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;

namespace HdrImageViewer.Settings;

/// <summary>
/// A labelled group of settings rendered under one header (or inside a collapsible
/// expander for the advanced page).
/// </summary>
public sealed class SettingsGroupDefinition
{
    public required string Id { get; init; }

    public required string LabelResourceKey { get; init; }

    public required IReadOnlyList<SettingDefinition> Settings { get; init; }

    public bool IsCollapsible { get; init; }
}

/// <summary>
/// One navigation entry of the settings page.
/// </summary>
public sealed class SettingsPageDefinition
{
    public required string Id { get; init; }

    public required string LabelResourceKey { get; init; }

    public required string Glyph { get; init; }

    public required IReadOnlyList<SettingsGroupDefinition> Groups { get; init; }

    public required IReadOnlyList<SettingDefinition> Settings { get; init; }
}

/// <summary>
/// A search hit with the breadcrumb that the search drop-down displays.
/// </summary>
public sealed class SettingsSearchResult
{
    public required SettingDefinition Setting { get; init; }

    public required SettingsPageDefinition Page { get; init; }

    public required SettingsGroupDefinition Group { get; init; }

    public required string Breadcrumb { get; init; }
}

/// <summary>
/// Static catalog of every settings page, group and item. Adding a setting here
/// automatically adds it to the page UI, the navigation tree, search and reset.
/// </summary>
public static class SettingsRegistry
{
    public const string PageGeneral = "general";
    public const string PageViewer = "viewer";
    public const string PageHdrColor = "hdr";
    public const string PagePerformance = "performance";
    public const string PageToolsExport = "tools";
    public const string PageAppearance = "appearance";
    public const string PageAdvanced = "advanced";

    public static IReadOnlyList<SettingsPageDefinition> Pages { get; } = BuildPages();

    public static IReadOnlyList<SettingDefinition> AllSettings { get; } =
        Pages.SelectMany(static page => page.Settings).ToArray();

    public static SettingsPageDefinition GetPage(string pageId)
    {
        foreach (var page in Pages)
        {
            if (string.Equals(page.Id, pageId, StringComparison.Ordinal))
            {
                return page;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "Unknown settings page id.");
    }

    public static SettingDefinition? FindSetting(string id)
    {
        foreach (var setting in AllSettings)
        {
            if (string.Equals(setting.Id, id, StringComparison.Ordinal))
            {
                return setting;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches the localized label, description, identifier, page and group labels
    /// of every setting. All whitespace-separated tokens must match.
    /// </summary>
    public static IReadOnlyList<SettingsSearchResult> Search(string? query)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return [];
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        var results = new List<(int Rank, SettingsSearchResult Result)>();
        foreach (var page in Pages)
        {
            var pageLabel = Localization.GetString(page.LabelResourceKey);
            foreach (var group in page.Groups)
            {
                var groupLabel = Localization.GetString(group.LabelResourceKey);
                foreach (var setting in group.Settings)
                {
                    var label = Localization.GetString(setting.LabelResourceKey);
                    var description = setting.DescriptionResourceKey is null
                        ? string.Empty
                        : Localization.GetString(setting.DescriptionResourceKey);
                    var haystack = string.Join(
                        ' ',
                        label,
                        description,
                        setting.Id,
                        pageLabel,
                        groupLabel,
                        BuildChoiceSearchText(setting));

                    var matchesAllTokens = true;
                    foreach (var token in tokens)
                    {
                        if (!haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesAllTokens = false;
                            break;
                        }
                    }

                    if (!matchesAllTokens)
                    {
                        continue;
                    }

                    var rank = label.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : label.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                            ? 1
                            : 2;
                    var breadcrumb = Localization.GetString("SettingsSearchBreadcrumbFormat", pageLabel, label);
                    results.Add((rank, new SettingsSearchResult
                    {
                        Setting = setting,
                        Page = page,
                        Group = group,
                        Breadcrumb = breadcrumb,
                    }));
                }
            }
        }

        return results
            .OrderBy(static item => item.Rank)
            .ThenBy(static item => Localization.GetString(item.Result.Setting.LabelResourceKey), StringComparer.CurrentCulture)
            .Select(static item => item.Result)
            .Take(50)
            .ToArray();
    }

    private static string BuildChoiceSearchText(SettingDefinition setting)
    {
        if (setting.Choices.Count == 0)
        {
            return string.Empty;
        }

        var labels = new List<string>(setting.Choices.Count);
        foreach (var choice in setting.Choices)
        {
            labels.Add(Localization.GetString(choice.LabelResourceKey));
        }

        return string.Join(' ', labels);
    }

    private static IReadOnlyList<SettingsPageDefinition> BuildPages()
    {
        return
        [
            BuildGeneralPage(),
            BuildViewerPage(),
            BuildHdrColorPage(),
            BuildPerformancePage(),
            BuildToolsPage(),
            BuildAppearancePage(),
            BuildAdvancedPage(),
        ];
    }

    private static SettingsPageDefinition BuildGeneralPage()
    {
        var pageId = PageGeneral;
        return CreatePage(pageId, "SettingsPageGeneralTitle", "\uE713",
            CreateGroup(pageId, "startup", "SettingsGroupStartupHeader",
                SettingDefinitionFactory.Toggle(
                    "RestoreLastImageOnStartup", pageId, "startup",
                    "SettingsRestoreLastImageOnStartupLabel",
                    "SettingsRestoreLastImageOnStartupDescription",
                    "\uE72C",
                    static settings => settings.RestoreLastImageOnStartup,
                    AppSettingsService.SetRestoreLastImageOnStartup,
                    defaultValue: true)),
            CreateGroup(pageId, "system", "SettingsGroupSystemHeader",
                SettingDefinitionFactory.Button(
                    "OpenDefaultApps", pageId, "system",
                    "SettingsOpenDefaultAppsLabel",
                    "SettingsOpenDefaultAppsDescription",
                    "SettingsOpenDefaultAppsAction",
                    "\uE8A7")));
    }

    private static SettingsPageDefinition BuildViewerPage()
    {
        var pageId = PageViewer;
        return CreatePage(pageId, "SettingsPageViewerTitle", "\uE9A6",
            CreateGroup(pageId, "zoom", "SettingsGroupZoomHeader",
                SettingDefinitionFactory.Choice(
                    "ZoomInterpolationUpscale", pageId, "zoom",
                    "SettingsZoomInterpolationUpscaleLabel",
                    "SettingsZoomInterpolationUpscaleDescription",
                    "\uE7AA",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationAuto", (int)ZoomInterpolationMode.Auto),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationNearest", (int)ZoomInterpolationMode.Nearest),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationBilinear", (int)ZoomInterpolationMode.Bilinear),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationBicubic", (int)ZoomInterpolationMode.Bicubic),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationLanczos", (int)ZoomInterpolationMode.Lanczos),
                    ],
                    (int)ZoomInterpolationMode.Auto,
                    static settings => (int)settings.ZoomInterpolationUpscale,
                    static value => AppSettingsService.SetZoomInterpolationUpscale((ZoomInterpolationMode)value)),
                SettingDefinitionFactory.Choice(
                    "ZoomDownscaleMode", pageId, "zoom",
                    "SettingsZoomInterpolationDownscaleLabel",
                    "SettingsZoomInterpolationDownscaleDescription",
                    "\uE7AA",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationAuto", (int)ZoomDownscaleMode.Auto),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationArea", (int)ZoomDownscaleMode.Area),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationBilinear", (int)ZoomDownscaleMode.Bilinear),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationBicubic", (int)ZoomDownscaleMode.CatmullRom),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationMitchell", (int)ZoomDownscaleMode.Mitchell),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationRobidoux", (int)ZoomDownscaleMode.Robidoux),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationLanczos", (int)ZoomDownscaleMode.Lanczos2),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationKaiser2", (int)ZoomDownscaleMode.Kaiser2),
                        SettingDefinitionFactory.ChoiceItem("SettingsInterpolationNearest", (int)ZoomDownscaleMode.Nearest),
                    ],
                    (int)ZoomDownscaleMode.Auto,
                    static settings => (int)settings.ZoomDownscaleMode,
                    static value => AppSettingsService.SetZoomDownscaleMode((ZoomDownscaleMode)value)),
                SettingDefinitionFactory.DoubleNumber(
                    "WheelZoomStepFactor", pageId, "zoom",
                    "SettingsWheelZoomStepFactorLabel",
                    "SettingsWheelZoomStepFactorDescription",
                    "\uE962",
                    AppUserSettings.DefaultWheelZoomStepFactor,
                    AppUserSettings.MinWheelZoomStepFactor,
                    AppUserSettings.MaxWheelZoomStepFactor,
                    0.01,
                    "SettingsUnitMultiplier",
                    static settings => settings.WheelZoomStepFactor,
                    AppSettingsService.SetWheelZoomStepFactor),
                SettingDefinitionFactory.IntNumber(
                    "ZoomAnimationDurationMilliseconds", pageId, "zoom",
                    "SettingsZoomAnimationDurationMillisecondsLabel",
                    "SettingsZoomAnimationDurationMillisecondsDescription",
                    "\uE945",
                    AppUserSettings.DefaultZoomAnimationDurationMilliseconds,
                    AppUserSettings.MinZoomAnimationDurationMilliseconds,
                    AppUserSettings.MaxZoomAnimationDurationMilliseconds,
                    10,
                    "SettingsUnitMilliseconds",
                    static settings => settings.ZoomAnimationDurationMilliseconds,
                    AppSettingsService.SetZoomAnimationDurationMilliseconds),
                SettingDefinitionFactory.IntNumber(
                    "MaxZoomPercent", pageId, "zoom",
                    "SettingsMaxZoomPercentLabel",
                    "SettingsMaxZoomPercentDescription",
                    "\uE9D9",
                    AppUserSettings.DefaultMaxZoomPercent,
                    AppUserSettings.MinMaxZoomPercent,
                    AppUserSettings.MaxMaxZoomPercent,
                    25,
                    "SettingsUnitPercent",
                    static settings => settings.MaxZoomPercent,
                    AppSettingsService.SetMaxZoomPercent),
                SettingDefinitionFactory.Choice(
                    "OpenImageZoomMode", pageId, "zoom",
                    "SettingsOpenImageZoomModeLabel",
                    "SettingsOpenImageZoomModeDescription",
                    "\uE8B7",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsOpenZoomFit", (int)OpenImageZoomMode.Fit),
                        SettingDefinitionFactory.ChoiceItem("SettingsOpenZoomActualSize", (int)OpenImageZoomMode.ActualSize),
                        SettingDefinitionFactory.ChoiceItem("SettingsOpenZoomLast", (int)OpenImageZoomMode.Last),
                    ],
                    (int)OpenImageZoomMode.Fit,
                    static settings => (int)settings.OpenImageZoomMode,
                    static value => AppSettingsService.SetOpenImageZoomMode((OpenImageZoomMode)value)),
                SettingDefinitionFactory.Toggle(
                    "LimitWheelZoomOutToFit", pageId, "zoom",
                    "SettingsLimitWheelZoomOutToFitLabel",
                    "SettingsLimitWheelZoomOutToFitDescription",
                    "\uE76B",
                    static settings => settings.LimitWheelZoomOutToFit,
                    AppSettingsService.SetLimitWheelZoomOutToFit)),
            CreateGroup(pageId, "mouse", "SettingsGroupMouseHeader",
                SettingDefinitionFactory.Choice(
                    "MouseWheelBehavior", pageId, "mouse",
                    "SettingsMouseWheelBehaviorLabel",
                    "SettingsMouseWheelBehaviorDescription",
                    "\uE962",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsMouseWheelNavigate", (int)MouseWheelBehavior.NavigateImages),
                        SettingDefinitionFactory.ChoiceItem("SettingsMouseWheelZoom", (int)MouseWheelBehavior.ZoomImage),
                    ],
                    (int)MouseWheelBehavior.NavigateImages,
                    static settings => (int)settings.MouseWheelBehavior,
                    static value => AppSettingsService.SetMouseWheelBehavior((MouseWheelBehavior)value)),
                SettingDefinitionFactory.Toggle(
                    "TouchpadGesturesEnabled", pageId, "mouse",
                    "SettingsTouchpadGesturesEnabledLabel",
                    "SettingsTouchpadGesturesEnabledDescription",
                    "\uE7C9",
                    static settings => settings.TouchpadGesturesEnabled,
                    AppSettingsService.SetTouchpadGesturesEnabled,
                    defaultValue: true)),
            CreateGroup(pageId, "onscreen", "SettingsGroupOnScreenHeader",
                SettingDefinitionFactory.IntNumber(
                    "ViewerChromeAutoHideMilliseconds", pageId, "onscreen",
                    "SettingsViewerChromeAutoHideMillisecondsLabel",
                    "SettingsViewerChromeAutoHideMillisecondsDescription",
                    "\uE9D9",
                    AppUserSettings.DefaultViewerChromeAutoHideMilliseconds,
                    AppUserSettings.MinViewerChromeAutoHideMilliseconds,
                    AppUserSettings.MaxViewerChromeAutoHideMilliseconds,
                    100,
                    "SettingsUnitMilliseconds",
                    static settings => settings.ViewerChromeAutoHideMilliseconds,
                    AppSettingsService.SetViewerChromeAutoHideMilliseconds),
                SettingDefinitionFactory.Toggle(
                    "ShowInspectorPanel", pageId, "onscreen",
                    "SettingsShowInspectorPanelLabel",
                    "SettingsShowInspectorPanelDescription",
                    "\uE946",
                    static settings => settings.ShowInspectorPanel,
                    AppSettingsService.SetShowInspectorPanel,
                    defaultValue: true),
                SettingDefinitionFactory.Toggle(
                    "ShowFilmstrip", pageId, "onscreen",
                    "SettingsShowFilmstripLabel",
                    "SettingsShowFilmstripDescription",
                    "\uE7AA",
                    static settings => settings.ShowFilmstrip,
                    AppSettingsService.SetShowFilmstrip,
                    defaultValue: true)),
            CreateGroup(pageId, "statusbar", "SettingsGroupStatusBarHeader",
                SettingDefinitionFactory.Toggle(
                    "ShowStatusFileName", pageId, "statusbar",
                    "SettingsShowStatusFileNameLabel",
                    "SettingsShowStatusFileNameDescription",
                    "\uE713",
                    static settings => settings.ShowStatusFileName,
                    AppSettingsService.SetShowStatusFileName,
                    defaultValue: true),
                SettingDefinitionFactory.Toggle(
                    "ShowStatusDimensions", pageId, "statusbar",
                    "SettingsShowStatusDimensionsLabel",
                    "SettingsShowStatusDimensionsDescription",
                    "\uE745",
                    static settings => settings.ShowStatusDimensions,
                    AppSettingsService.SetShowStatusDimensions,
                    defaultValue: true),
                SettingDefinitionFactory.Toggle(
                    "ShowStatusFileSize", pageId, "statusbar",
                    "SettingsShowStatusFileSizeLabel",
                    "SettingsShowStatusFileSizeDescription",
                    "\uE76C",
                    static settings => settings.ShowStatusFileSize,
                    AppSettingsService.SetShowStatusFileSize,
                    defaultValue: true),
                SettingDefinitionFactory.Toggle(
                    "ShowStatusHdrKind", pageId, "statusbar",
                    "SettingsShowStatusHdrKindLabel",
                    "SettingsShowStatusHdrKindDescription",
                    "\uE71F",
                    static settings => settings.ShowStatusHdrKind,
                    AppSettingsService.SetShowStatusHdrKind,
                    defaultValue: true)));
    }

    private static SettingsPageDefinition BuildHdrColorPage()
    {
        var pageId = PageHdrColor;
        return CreatePage(pageId, "SettingsPageHdrColorTitle", "\uE790",
            CreateGroup(pageId, "display", "SettingsGroupHdrDisplayHeader",
                SettingDefinitionFactory.Choice(
                    "HdrViewMode", pageId, "display",
                    "SettingsHdrViewModeLabel",
                    "SettingsHdrViewModeDescription",
                    "\uE790",
                    [
                        SettingDefinitionFactory.ChoiceItem("HdrPreviewModeSdr", (int)GainmapViewMode.Sdr),
                        SettingDefinitionFactory.ChoiceItem("HdrPreviewModeAdaptive", (int)GainmapViewMode.Adaptive),
                        SettingDefinitionFactory.ChoiceItem("HdrPreviewModeAlternate", (int)GainmapViewMode.AlternateImage),
                        SettingDefinitionFactory.ChoiceItem("HdrPreviewModeGainMap", (int)GainmapViewMode.GainMap),
                    ],
                    (int)GainmapViewMode.Adaptive,
                    static settings => (int)settings.HdrViewMode,
                    static value => AppSettingsService.SetHdrViewMode((GainmapViewMode)value)),
                SettingDefinitionFactory.Choice(
                    "ColorGamutMappingMode", pageId, "display",
                    "SettingsColorGamutMappingModeLabel",
                    "SettingsColorGamutMappingModeDescription",
                    "\uE790",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsGamutManaged", (int)ColorGamutMappingMode.Managed),
                        SettingDefinitionFactory.ChoiceItem("SettingsGamutClip", (int)ColorGamutMappingMode.Clip),
                    ],
                    (int)ColorGamutMappingMode.Managed,
                    static settings => (int)settings.ColorGamutMappingMode,
                    static value => AppSettingsService.SetColorGamutMappingMode((ColorGamutMappingMode)value)),
                SettingDefinitionFactory.Toggle(
                    "AdaptiveToneMappingEnabled", pageId, "display",
                    "SettingsAdaptiveToneMappingEnabledLabel",
                    "SettingsAdaptiveToneMappingEnabledDescription",
                    "\uE945",
                    static settings => settings.AdaptiveToneMappingEnabled,
                    AppSettingsService.SetAdaptiveToneMappingEnabled)),
            CreateGroup(pageId, "headroom", "SettingsGroupHeadroomHeader",
                SettingDefinitionFactory.Choice(
                    "HdrHeadroomMode", pageId, "headroom",
                    "SettingsHdrHeadroomModeLabel",
                    "SettingsHdrHeadroomModeDescription",
                    "\uE9D9",
                    [
                        SettingDefinitionFactory.ChoiceItem("HdrHeadroomSystemAuto", (int)HdrHeadroomMode.SystemAdaptive),
                        SettingDefinitionFactory.ChoiceItem("HdrHeadroomManualOverride", (int)HdrHeadroomMode.Manual),
                    ],
                    (int)HdrHeadroomMode.SystemAdaptive,
                    static settings => (int)settings.HdrHeadroomMode,
                    static value => AppSettingsService.SetHdrHeadroomMode((HdrHeadroomMode)value)),
                SettingDefinitionFactory.DoubleNumber(
                    "HdrManualHeadroomStops", pageId, "headroom",
                    "SettingsHdrManualHeadroomStopsLabel",
                    "SettingsHdrManualHeadroomStopsDescription",
                    "\uE9D9",
                    AppUserSettings.DefaultHdrManualHeadroomStops,
                    AppUserSettings.MinHdrManualHeadroomStops,
                    AppUserSettings.MaxHdrManualHeadroomStops,
                    0.25,
                    "SettingsUnitStops",
                    static settings => settings.HdrManualHeadroomStops,
                    AppSettingsService.SetHdrManualHeadroomStops),
                SettingDefinitionFactory.Toggle(
                    "SdrWhiteOverrideEnabled", pageId, "headroom",
                    "SettingsSdrWhiteOverrideEnabledLabel",
                    "SettingsSdrWhiteOverrideEnabledDescription",
                    "\uE790",
                    static settings => settings.SdrWhiteOverrideEnabled,
                    AppSettingsService.SetSdrWhiteOverrideEnabled),
                SettingDefinitionFactory.IntNumber(
                    "SdrWhiteNits", pageId, "headroom",
                    "SettingsSdrWhiteNitsLabel",
                    "SettingsSdrWhiteNitsDescription",
                    "\uE790",
                    AppUserSettings.DefaultSdrWhiteNits,
                    AppUserSettings.MinSdrWhiteNits,
                    AppUserSettings.MaxSdrWhiteNits,
                    5,
                    "SettingsUnitNits",
                    static settings => settings.SdrWhiteNits,
                    AppSettingsService.SetSdrWhiteNits)),
            CreateGroup(pageId, "analysis", "SettingsGroupAnalysisHeader",
                SettingDefinitionFactory.Toggle(
                    "AnalysisAutoRefreshEnabled", pageId, "analysis",
                    "SettingsAnalysisAutoRefreshEnabledLabel",
                    "SettingsAnalysisAutoRefreshEnabledDescription",
                    "\uE72C",
                    static settings => settings.AnalysisAutoRefreshEnabled,
                    AppSettingsService.SetAnalysisAutoRefreshEnabled,
                    defaultValue: true)));
    }

    private static SettingsPageDefinition BuildPerformancePage()
    {
        var pageId = PagePerformance;
        return CreatePage(pageId, "SettingsPagePerformanceTitle", "\uE9D9",
            CreateGroup(pageId, "preload", "SettingsGroupPreloadHeader",
                SettingDefinitionFactory.Toggle(
                    "PreloadAdjacentImages", pageId, "preload",
                    "SettingsPreloadAdjacentImagesLabel",
                    "SettingsPreloadAdjacentImagesDescription",
                    "\uE945",
                    static settings => settings.PreloadAdjacentImages,
                    AppSettingsService.SetPreloadAdjacentImages,
                    defaultValue: true),
                SettingDefinitionFactory.IntNumber(
                    "AdjacentPreloadRadius", pageId, "preload",
                    "SettingsAdjacentPreloadRadiusLabel",
                    "SettingsAdjacentPreloadRadiusDescription",
                    "\uE8F1",
                    AppUserSettings.DefaultAdjacentPreloadRadius,
                    AppUserSettings.MinAdjacentPreloadRadius,
                    AppUserSettings.MaxAdjacentPreloadRadius,
                    1,
                    "SettingsUnitImages",
                    static settings => settings.AdjacentPreloadRadius,
                    AppSettingsService.SetAdjacentPreloadRadius),
                SettingDefinitionFactory.Toggle(
                    "SkipCloudPlaceholders", pageId, "preload",
                    "SettingsSkipCloudPlaceholdersLabel",
                    "SettingsSkipCloudPlaceholdersDescription",
                    "\uE753",
                    static settings => settings.SkipCloudPlaceholders,
                    AppSettingsService.SetSkipCloudPlaceholders,
                    defaultValue: true)),
            CreateGroup(pageId, "cache", "SettingsGroupCacheHeader",
                SettingDefinitionFactory.IntNumber(
                    "DecodedCacheBudgetMegabytes", pageId, "cache",
                    "SettingsDecodedCacheBudgetMegabytesLabel",
                    "SettingsDecodedCacheBudgetMegabytesDescription",
                    "\uE8F1",
                    AppUserSettings.DefaultDecodedCacheBudgetMegabytes,
                    AppUserSettings.MinDecodedCacheBudgetMegabytes,
                    AppUserSettings.MaxDecodedCacheBudgetMegabytes,
                    32,
                    "SettingsUnitMegabytes",
                    static settings => settings.DecodedCacheBudgetMegabytes,
                    AppSettingsService.SetDecodedCacheBudgetMegabytes),
                SettingDefinitionFactory.IntNumber(
                    "MetadataCacheMaxFiles", pageId, "cache",
                    "SettingsMetadataCacheMaxFilesLabel",
                    "SettingsMetadataCacheMaxFilesDescription",
                    "\uE8F1",
                    AppUserSettings.DefaultMetadataCacheMaxFiles,
                    AppUserSettings.MinMetadataCacheMaxFiles,
                    AppUserSettings.MaxMetadataCacheMaxFiles,
                    25,
                    "SettingsUnitFiles",
                    static settings => settings.MetadataCacheMaxFiles,
                    AppSettingsService.SetMetadataCacheMaxFiles),
                SettingDefinitionFactory.IntNumber(
                    "DirectoryIndexCacheCount", pageId, "cache",
                    "SettingsDirectoryIndexCacheCountLabel",
                    "SettingsDirectoryIndexCacheCountDescription",
                    "\uE7AA",
                    AppUserSettings.DefaultDirectoryIndexCacheCount,
                    AppUserSettings.MinDirectoryIndexCacheCount,
                    AppUserSettings.MaxDirectoryIndexCacheCount,
                    1,
                    "SettingsUnitFolders",
                    static settings => settings.DirectoryIndexCacheCount,
                    AppSettingsService.SetDirectoryIndexCacheCount)),
            CreateGroup(pageId, "memory", "SettingsGroupMemoryHeader",
                SettingDefinitionFactory.Toggle(
                    "MemoryPressureAutoTrim", pageId, "memory",
                    "SettingsMemoryPressureAutoTrimLabel",
                    "SettingsMemoryPressureAutoTrimDescription",
                    "\uE945",
                    static settings => settings.MemoryPressureAutoTrim,
                    AppSettingsService.SetMemoryPressureAutoTrim,
                    defaultValue: true)));
    }

    private static SettingsPageDefinition BuildToolsPage()
    {
        var pageId = PageToolsExport;
        return CreatePage(pageId, "SettingsPageToolsExportTitle", "\uEB9F",
            CreateGroup(pageId, "export", "SettingsGroupExportHeader",
                SettingDefinitionFactory.Toggle(
                    "ConfirmBeforeOverwrite", pageId, "export",
                    "SettingsConfirmBeforeOverwriteLabel",
                    "SettingsConfirmBeforeOverwriteDescription",
                    "\uE711",
                    static settings => settings.ConfirmBeforeOverwrite,
                    AppSettingsService.SetConfirmBeforeOverwrite,
                    defaultValue: true),
                SettingDefinitionFactory.IntNumber(
                    "BatchExportMaxRetries", pageId, "export",
                    "SettingsBatchExportMaxRetriesLabel",
                    "SettingsBatchExportMaxRetriesDescription",
                    "\uE72C",
                    AppUserSettings.DefaultBatchExportMaxRetries,
                    AppUserSettings.MinBatchExportMaxRetries,
                    AppUserSettings.MaxBatchExportMaxRetries,
                    1,
                    "SettingsUnitRetries",
                    static settings => settings.BatchExportMaxRetries,
                    AppSettingsService.SetBatchExportMaxRetries)));
    }

    private static SettingsPageDefinition BuildAppearancePage()
    {
        var pageId = PageAppearance;
        return CreatePage(pageId, "SettingsPageAppearanceTitle", "\uE7AA",
            CreateGroup(pageId, "theme", "SettingsGroupThemeHeader",
                SettingDefinitionFactory.Choice(
                    "Theme", pageId, "theme",
                    "SettingsThemeLabel",
                    "SettingsThemeDescription",
                    "\uE790",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsThemeSystem", (int)AppTheme.System),
                        SettingDefinitionFactory.ChoiceItem("SettingsThemeLight", (int)AppTheme.Light),
                        SettingDefinitionFactory.ChoiceItem("SettingsThemeDark", (int)AppTheme.Dark),
                    ],
                    (int)AppTheme.System,
                    static settings => (int)settings.Theme,
                    static value => AppSettingsService.SetTheme((AppTheme)value)),
                SettingDefinitionFactory.Choice(
                    "ViewerBackground", pageId, "theme",
                    "SettingsViewerBackgroundLabel",
                    "SettingsViewerBackgroundDescription",
                    "\uE790",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsBackgroundWindow", (int)ViewerBackgroundMode.Window),
                        SettingDefinitionFactory.ChoiceItem("SettingsBackgroundBlack", (int)ViewerBackgroundMode.Black),
                        SettingDefinitionFactory.ChoiceItem("SettingsBackgroundDarkGray", (int)ViewerBackgroundMode.DarkGray),
                        SettingDefinitionFactory.ChoiceItem("SettingsBackgroundMidGray", (int)ViewerBackgroundMode.MidGray),
                        SettingDefinitionFactory.ChoiceItem("SettingsBackgroundLightGray", (int)ViewerBackgroundMode.LightGray),
                        SettingDefinitionFactory.ChoiceItem("SettingsBackgroundWhite", (int)ViewerBackgroundMode.White),
                    ],
                    (int)ViewerBackgroundMode.Window,
                    static settings => (int)settings.ViewerBackground,
                    static value => AppSettingsService.SetViewerBackground((ViewerBackgroundMode)value)),
                SettingDefinitionFactory.StringChoice(
                    "Language", pageId, "theme",
                    "SettingsLanguageLabel",
                    "SettingsLanguageDescription",
                    "\uE774",
                    BuildLanguageChoices(),
                    string.Empty,
                    static settings => settings.Language,
                    AppSettingsService.SetLanguage,
                    requiresRestart: true)));
    }

    private static SettingsPageDefinition BuildAdvancedPage()
    {
        var pageId = PageAdvanced;
        return CreatePage(pageId, "SettingsPageAdvancedTitle", "\uE8B7",
            CreateGroup(pageId, "configuration", "SettingsGroupConfigurationHeader",
                SettingDefinitionFactory.Button(
                    "OpenConfigFolder", pageId, "configuration",
                    "SettingsOpenConfigFolderLabel",
                    "SettingsOpenConfigFolderDescription",
                    "SettingsOpenConfigFolderAction",
                    "\uE8A7"),
                SettingDefinitionFactory.Button(
                    "ExportSettings", pageId, "configuration",
                    "SettingsExportSettingsLabel",
                    "SettingsExportSettingsDescription",
                    "SettingsExportSettingsAction",
                    "\uE894"),
                SettingDefinitionFactory.Button(
                    "ImportSettings", pageId, "configuration",
                    "SettingsImportSettingsLabel",
                    "SettingsImportSettingsDescription",
                    "SettingsImportSettingsAction",
                    "\uE8A3")),
            CreateGroup(pageId, "diagnostics", "SettingsGroupDiagnosticsHeader",
                SettingDefinitionFactory.Info(
                    "Diagnostics", pageId, "diagnostics",
                    "SettingsDiagnosticsLabel",
                    "SettingsDiagnosticsDescription",
                    "\uE946",
                    SettingsDiagnostics.GetText)),
            CreateCollapsibleGroup(pageId, "danger", "SettingsGroupDangerHeader",
                SettingDefinitionFactory.Button(
                    "ResetAllSettings", pageId, "danger",
                    "SettingsResetAllSettingsLabel",
                    "SettingsResetAllSettingsDescription",
                    "SettingsResetAllSettingsAction",
                    "\uE74D")),
            CreateCollapsibleGroup(pageId, "experimental", "SettingsGroupExperimentalHeader",
                SettingDefinitionFactory.Toggle(
                    "RtxVsrMenuEnabled", pageId, "experimental",
                    "SettingsRtxVsrMenuEnabledLabel",
                    "SettingsRtxVsrMenuEnabledDescription",
                    "\uE945",
                    static settings => settings.RtxVsrMenuEnabled,
                    AppSettingsService.SetRtxVsrMenuEnabled,
                    defaultValue: true,
                    isAdvanced: true),
                SettingDefinitionFactory.Choice(
                    "RtxVsrMode", pageId, "experimental",
                    "SettingsRtxVsrModeLabel",
                    "SettingsRtxVsrModeDescription",
                    "\uE945",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrModeStandard", (int)RtxVsrMode.Standard),
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrModeDenoise", (int)RtxVsrMode.Denoise),
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrModeDeblur", (int)RtxVsrMode.Deblur),
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrModeHighBitrate", (int)RtxVsrMode.HighBitrate),
                    ],
                    (int)AppUserSettings.DefaultRtxVsrMode,
                    static settings => (int)settings.RtxVsrMode,
                    static value => AppSettingsService.SetRtxVsrMode((RtxVsrMode)value),
                    isAdvanced: true),
                SettingDefinitionFactory.Choice(
                    "RtxVsrQualityPreset", pageId, "experimental",
                    "SettingsRtxVsrQualityPresetLabel",
                    "SettingsRtxVsrQualityPresetDescription",
                    "\uE945",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrQualityLow", (int)RtxVsrQualityPreset.Low),
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrQualityMedium", (int)RtxVsrQualityPreset.Medium),
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrQualityHigh", (int)RtxVsrQualityPreset.High),
                        SettingDefinitionFactory.ChoiceItem("SettingsRtxVsrQualityUltra", (int)RtxVsrQualityPreset.Ultra),
                    ],
                    (int)AppUserSettings.DefaultRtxVsrQualityPreset,
                    static settings => (int)settings.RtxVsrQualityPreset,
                    static value => AppSettingsService.SetRtxVsrQualityPreset((RtxVsrQualityPreset)value),
                    isAdvanced: true),
                SettingDefinitionFactory.Choice(
                    "RtxVsrScaleFactor", pageId, "experimental",
                    "SettingsRtxVsrScaleFactorLabel",
                    "SettingsRtxVsrScaleFactorDescription",
                    "\uE7AA",
                    [
                        SettingDefinitionFactory.ChoiceItem("SettingsVsrScale2x", 2),
                        SettingDefinitionFactory.ChoiceItem("SettingsVsrScale4x", 4),
                    ],
                    2,
                    static settings => settings.RtxVsrScaleFactor,
                    AppSettingsService.SetRtxVsrScaleFactor,
                    isAdvanced: true)));
    }

    private static IReadOnlyList<SettingChoice> BuildLanguageChoices()
    {
        return
        [
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageSystem", string.Empty),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameZhCn", "zh-CN"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameEnUs", "en-US"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameRuRu", "ru-RU"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameDeDe", "de-DE"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameFrFr", "fr-FR"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameEsEs", "es-ES"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameItIt", "it-IT"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNamePtBr", "pt-BR"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameJaJp", "ja-JP"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameKoKr", "ko-KR"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNamePlPl", "pl-PL"),
            SettingDefinitionFactory.ChoiceItem("SettingsLanguageNameUkUa", "uk-UA"),
        ];
    }

    private static SettingsPageDefinition CreatePage(
        string id,
        string labelResourceKey,
        string glyph,
        params SettingsGroupDefinition[] groups)
    {
        return new SettingsPageDefinition
        {
            Id = id,
            LabelResourceKey = labelResourceKey,
            Glyph = glyph,
            Groups = groups,
            Settings = groups.SelectMany(static group => group.Settings).ToArray(),
        };
    }

    private static SettingsGroupDefinition CreateGroup(
        string pageId,
        string groupId,
        string labelResourceKey,
        params SettingDefinition[] settings)
    {
        return new SettingsGroupDefinition
        {
            Id = pageId + "." + groupId,
            LabelResourceKey = labelResourceKey,
            Settings = settings,
        };
    }

    private static SettingsGroupDefinition CreateCollapsibleGroup(
        string pageId,
        string groupId,
        string labelResourceKey,
        params SettingDefinition[] settings)
    {
        return new SettingsGroupDefinition
        {
            Id = pageId + "." + groupId,
            LabelResourceKey = labelResourceKey,
            Settings = settings,
            IsCollapsible = true,
        };
    }
}
