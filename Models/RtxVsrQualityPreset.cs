namespace HdrImageViewer.Models;

/// <summary>
/// Quality tier of an RTX Video Super Resolution mode. Each mode family in the
/// NVIDIA VFX SDK 1.3 exposes exactly four tiers; the tier order matches the
/// native Low/Medium/High/Ultra order (e.g. 1-4, 8-11, 12-15 and 16-19).
/// </summary>
public enum RtxVsrQualityPreset
{
    Low = 0,
    Medium = 1,
    High = 2,
    Ultra = 3,
}
