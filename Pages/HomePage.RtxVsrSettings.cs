using HdrImageViewer.Services;

namespace HdrImageViewer.Pages;

/// <summary>
/// Reacts to RTX VSR setting changes. Kept in a separate partial file so the
/// feature implementation in HomePage.RtxVsr.cs stays untouched.
/// </summary>
public sealed partial class HomePage
{
    private void ApplyRtxVsrSettingChange(AppUserSettings previous)
    {
        var modeChanged = previous.RtxVsrMode != _settings.RtxVsrMode;
        var presetChanged = previous.RtxVsrQualityPreset != _settings.RtxVsrQualityPreset;
        var scaleChanged = previous.RtxVsrScaleFactor != _settings.RtxVsrScaleFactor;
        var enabledChanged = previous.RtxVsrMenuEnabled != _settings.RtxVsrMenuEnabled;
        if (!modeChanged && !presetChanged && !scaleChanged && !enabledChanged)
        {
            return;
        }

        // The cached enhancement key includes the mode, tier and effective scale,
        // so any change to those inputs has to invalidate the cached outcome.
        RtxVsrService.ClearCache();
        UpdateRtxVsrUi();

        if (enabledChanged)
        {
            // The switch now only controls whether the viewer right-click menu
            // offers RTX VSR. Turning it off while the effect is active restores
            // the original rendition (keeping the current framing), and it never
            // applies the effect to an image on its own.
            if (!_settings.RtxVsrMenuEnabled && _rtxVsrActive)
            {
                _ = ClearRtxVsrAsync();
            }

            return;
        }

        if (_rtxVsrActive && (modeChanged || presetChanged || scaleChanged))
        {
            // Re-run the enhancement with the new mode/tier/scale while VSR stays
            // on; the feature partial applies the new result and updates the badge.
            _ = ApplyRtxVsrAsync();
        }
    }
}
