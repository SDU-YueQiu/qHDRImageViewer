namespace HdrImageViewer.Rendering;

/// <param name="ZoomInterpolationUpscale">
/// Sampling filter used when the image is displayed larger than its source
/// pixels (zoom percentage above 100 %, i.e. magnification).
/// </param>
/// <param name="ZoomDownscale">
/// Sampling filter used when the image is displayed smaller than its source
/// pixels (zoom percentage below 100 %, i.e. minification). Dedicated
/// minification set; see <see cref="ZoomDownscaleMode"/>.
/// </param>
public sealed record HdrRendererSettings(
    GainmapViewMode ViewMode,
    HdrHeadroomMode HeadroomMode,
    float? DisplayCapacityOverrideLog2,
    bool AdaptiveToneMappingEnabled,
    float ReferenceWhiteExposureScale,
    ColorGamutMappingMode ColorGamutMappingMode,
    HdrDisplayConfiguration DisplayConfiguration,
    ZoomInterpolationMode ZoomInterpolationUpscale = ZoomInterpolationMode.Auto,
    ZoomDownscaleMode ZoomDownscale = ZoomDownscaleMode.Auto)
{
    public static HdrRendererSettings Default { get; } = new(
        GainmapViewMode.Adaptive,
        HdrHeadroomMode.SystemAdaptive,
        null,
        false,
        1.0f,
        ColorGamutMappingMode.Managed,
        HdrDisplayConfiguration.Unknown,
        ZoomInterpolationMode.Auto,
        ZoomDownscaleMode.Auto);
}
