namespace HdrImageViewer.Rendering;

/// <summary>
/// Sampling filter used when the viewer displays the image smaller than its
/// source pixels (zoom below 100 %, i.e. minification).
/// </summary>
/// <remarks>
/// Minification is a prefiltering problem (remove frequencies above the output
/// Nyquist limit), so the set intentionally differs from
/// <see cref="ZoomInterpolationMode"/>: the sharp reconstruction filters used
/// for magnification are kept, but they are combined with the GPU mip chain so
/// the kernel actually averages the output pixel footprint, and dedicated
/// anti-aliasing filters (Mitchell, Robidoux, Kaiser, area) are added.
/// The numeric values are wire format: they are passed to the shader as the
/// downscale mode code in <c>ZoomFilter.y</c> and persisted as
/// <c>ZoomDownscaleMode</c> in the settings JSON, so existing values must never
/// change. See <c>docs/DOWNSCALING_FILTERS.md</c>.
/// </remarks>
public enum ZoomDownscaleMode
{
    /// <summary>
    /// Legacy path: the historical adaptive mip-filtered minification. Kept so
    /// the default downscale rendering does not change on upgrade.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Point sampling with gradient-selected mip level (crisp, aliased).
    /// </summary>
    Nearest = 1,

    /// <summary>
    /// Hardware bilinear filtering with trilinear mip selection.
    /// </summary>
    Bilinear = 2,

    /// <summary>
    /// Catmull-Rom cubic (B = 0, C = 0.5), the same kernel as the upscale
    /// "Bicubic" mode, evaluated in mip-texel space while minifying.
    /// </summary>
    CatmullRom = 3,

    /// <summary>
    /// Mitchell-Netravali cubic (B = C = 1/3): softer than Catmull-Rom with
    /// much less ringing; the classic balanced reconstruction filter.
    /// </summary>
    Mitchell = 4,

    /// <summary>
    /// Robidoux cubic (B = 12/(19+9√2), C = 113/(58+216√2)): the Keys cubic
    /// tuned by Nicolas Robidoux for downscaling, close to a sharpened
    /// Lanczos-2 in the passband.
    /// </summary>
    Robidoux = 5,

    /// <summary>
    /// Lanczos-2 windowed sinc (radius 2), the same kernel as the upscale
    /// "Lanczos" mode, evaluated in mip-texel space while minifying.
    /// </summary>
    Lanczos2 = 6,

    /// <summary>
    /// Kaiser-windowed sinc with radius 2 and beta 3.5: sharper than Lanczos-2
    /// because the Kaiser window tapers the side lobes less, with a slightly
    /// stronger ringing trade-off.
    /// </summary>
    Kaiser2 = 7,

    /// <summary>
    /// Box / area average: the output pixel footprint is averaged from the mip
    /// chain. Energy conserving and free of negative lobes, so it is the safest
    /// choice for checkerboards, noise and other pixel-scale detail.
    /// </summary>
    Area = 8,
}

/// <summary>
/// Helpers for <see cref="ZoomDownscaleMode"/>: shader-code mapping and the
/// migration mapping from the pre-1.2.3 <see cref="ZoomInterpolationMode"/>
/// downscale values. Kept free of UI/settings dependencies so it can be unit
/// tested directly.
/// </summary>
public static class ZoomDownscaleModes
{
    /// <summary>
    /// Lowest numeric value accepted by the shader downscale slot (Auto).
    /// </summary>
    public const int MinShaderCode = 0;

    /// <summary>
    /// Highest numeric value accepted by the shader downscale slot (Area).
    /// </summary>
    public const int MaxShaderCode = 8;

    /// <summary>
    /// Gets the shader mode code carried in <c>ZoomFilter.y</c> for
    /// <paramref name="mode"/>. The enum values are the wire format.
    /// </summary>
    public static int ToShaderCode(ZoomDownscaleMode mode)
    {
        return (int)mode;
    }

    /// <summary>
    /// Returns <paramref name="mode"/> when it is a defined enum value, else
    /// <see cref="ZoomDownscaleMode.Auto"/> so a corrupted settings file always
    /// falls back to the legacy rendering.
    /// </summary>
    public static ZoomDownscaleMode Normalize(ZoomDownscaleMode mode)
    {
        return Enum.IsDefined(mode) ? mode : ZoomDownscaleMode.Auto;
    }

    /// <summary>
    /// Maps the legacy downscale value (the <see cref="ZoomInterpolationMode"/>
    /// enum used by v1.2.2 and the single pre-split <c>ZoomInterpolation</c>
    /// setting) onto the dedicated minification set by filter name.
    /// </summary>
    public static ZoomDownscaleMode FromLegacy(ZoomInterpolationMode legacy)
    {
        return legacy switch
        {
            ZoomInterpolationMode.Nearest => ZoomDownscaleMode.Nearest,
            ZoomInterpolationMode.Bilinear => ZoomDownscaleMode.Bilinear,
            ZoomInterpolationMode.Bicubic => ZoomDownscaleMode.CatmullRom,
            ZoomInterpolationMode.Lanczos => ZoomDownscaleMode.Lanczos2,
            _ => ZoomDownscaleMode.Auto,
        };
    }
}
