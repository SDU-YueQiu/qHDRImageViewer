namespace HdrImageViewer.Models;

/// <summary>
/// NVIDIA RTX Video Super Resolution mode family (NVIDIA VFX SDK 1.3).
/// The numeric values are application-level identifiers, not the native
/// QualityLevel values used by NVVideoEffects; <c>RtxVsrEngine</c> maps each
/// mode + <see cref="RtxVsrQualityPreset"/> pair to the documented QualityLevel.
/// </summary>
public enum RtxVsrMode
{
    /// <summary>Standard AI upscaling for typical compressed sources (native levels 1-4).</summary>
    Standard = 0,

    /// <summary>Denoise at the source resolution (native levels 8-11; no upscaling).</summary>
    Denoise = 1,

    /// <summary>Deblur at the source resolution (native levels 12-15; no upscaling).</summary>
    Deblur = 2,

    /// <summary>High-bitrate detail restoration for clean sources such as photos (native levels 16-19).</summary>
    HighBitrate = 3,
}
