namespace HdrImageViewer.Rendering;

/// <summary>
/// Sampling filter used when the viewer resamples the image for display.
/// </summary>
public enum ZoomInterpolationMode
{
    /// <summary>
    /// Viewer default: mip-filtered minification and the existing adaptive
    /// sharpened magnification path. Kept identical to the pre-setting
    /// behavior so upgrading does not change the default rendering.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Point sampling with mip selection (crisp pixels, no smoothing).
    /// </summary>
    Nearest = 1,

    /// <summary>
    /// Plain hardware bilinear filtering (mip trilinear when minified).
    /// </summary>
    Bilinear = 2,

    /// <summary>
    /// Catmull-Rom cubic 4x4 taps near 1:1 and above; mip trilinear for
    /// stronger minification.
    /// </summary>
    Bicubic = 3,

    /// <summary>
    /// Lanczos-2 windowed sinc, 4x4 taps near 1:1 and above; mip trilinear
    /// for stronger minification.
    /// </summary>
    Lanczos = 4,
}
