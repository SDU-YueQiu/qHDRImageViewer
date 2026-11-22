using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HdrImageViewer.Pages;

/// <summary>
/// RTX Video Super Resolution is an independent content-processing feature: it
/// replaces the displayed pixels with an AI-enhanced SDR rendition and never
/// changes the viewer's zoom model or framing. The zoom mode (fit / fill /
/// actual size / custom fit-relative scale) and the scroll position are
/// deliberately preserved across toggles so enabling and disabling it gives a
/// direct A/B comparison at the same place. The feature is controlled from the
/// image right-click menu; that menu entry only exists while the feature is
/// enabled in the settings page, and the only toolbar surface is the read-only
/// "RTX VSR" badge shown while the feature is active.
/// </summary>
public sealed partial class HomePage
{
    private bool _rtxVsrActive;
    private bool _isRtxVsrProcessing;
    private bool _rtxVsrSourceWasHdr;
    private int _rtxVsrAppliedScaleFactor = 2;
    private RtxVsrMode _rtxVsrAppliedMode = RtxVsrMode.HighBitrate;
    private RtxVsrQualityPreset _rtxVsrAppliedPreset = RtxVsrQualityPreset.Ultra;
    private (int Width, int Height)? _rtxVsrAppliedPixelSize;
    private RtxVsrAvailability? _rtxVsrAvailability;
    private CancellationTokenSource? _rtxVsrCts;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rtxVsrInfoTimer;
    private HdrImageDocument? _rtxVsrPreviousFullResolutionDocument;

    private void InitializeRtxVsr()
    {
        UpdateRtxVsrMenuItemText();
        UpdateRtxVsrUi();
        _ = ProbeRtxVsrAsync();
    }

    private async Task ProbeRtxVsrAsync()
    {
        try
        {
            var availability = await Task.Run(RtxVsrService.Probe, _lifetimeToken);
            if (_lifetimeToken.IsCancellationRequested)
            {
                return;
            }

            _rtxVsrAvailability = availability;
            DispatcherQueue.TryEnqueue(UpdateRtxVsrUi);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _rtxVsrAvailability = RtxVsrAvailability.Unavailable(ex.Message);
            DispatcherQueue.TryEnqueue(UpdateRtxVsrUi);
        }
    }

    private void UpdateRtxVsrUi()
    {
        var hasImage = ViewModel.HasImage;
        if (!hasImage && _rtxVsrActive)
        {
            // The document was cleared without going through the load pipeline.
            _rtxVsrActive = false;
            _rtxVsrAppliedPixelSize = null;
        }

        if (RtxVsrBadge is not null)
        {
            var badgeText = BuildRtxVsrBadgeText();
            if (RtxVsrBadgeHost is not null)
            {
                RtxVsrBadgeHost.Visibility = _rtxVsrActive ? Visibility.Visible : Visibility.Collapsed;
            }

            if (RtxVsrBadgeTextBlock is not null)
            {
                RtxVsrBadgeTextBlock.Text = badgeText;
            }

            AutomationProperties.SetName(RtxVsrBadge, badgeText);
            if (RtxVsrBadgeHost is not null)
            {
                AutomationProperties.SetName(RtxVsrBadgeHost, badgeText);
            }

            // The badge is read-only; its tooltip names the exact mode + tier and
            // the comparison hint, so the applied AI result is discoverable.
            var badgeToolTip = _rtxVsrActive
                ? Localization.GetString("RtxVsrBadgeMode", BuildRtxVsrModeTierText()) + "\n" +
                  Localization.GetString("RtxVsrCompareHint")
                : string.Empty;
            ToolTipService.SetToolTip(RtxVsrBadge, badgeToolTip);
            if (RtxVsrBadgeHost is not null)
            {
                ToolTipService.SetToolTip(RtxVsrBadgeHost, badgeToolTip);
            }
        }

        if (RtxVsrToggleMenuItem is not null)
        {
            // The settings switch controls whether the viewer right-click menu
            // offers RTX VSR at all; it no longer applies the effect automatically.
            RtxVsrToggleMenuItem.Visibility = _settings.RtxVsrMenuEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            RtxVsrToggleMenuItem.IsEnabled =
                hasImage && _rtxVsrAvailability?.IsAvailable == true && !_isRtxVsrProcessing;
            UpdateRtxVsrMenuItemText();
        }
    }

    /// <summary>
    /// The menu entry is a plain action, not a checkable toggle: its label names
    /// what the next click does so the menu reads naturally.
    /// </summary>
    private void UpdateRtxVsrMenuItemText()
    {
        if (RtxVsrToggleMenuItem is null)
        {
            return;
        }

        var text = Localization.GetString(
            _rtxVsrActive ? "ContextMenuRtxVsrDisable" : "ContextMenuRtxVsr");
        RtxVsrToggleMenuItem.Text = text;
        AutomationProperties.SetName(RtxVsrToggleMenuItem, text);
    }

    private string BuildRtxVsrBadgeText()
    {
        if (!_rtxVsrActive)
        {
            return string.Empty;
        }

        return RtxVsrEngine.IsSameResolutionMode(_rtxVsrAppliedMode)
            // Denoise / Deblur run at the source resolution, so the useful
            // information is the mode family and quality tier, not a factor.
            ? Localization.GetString("RtxVsrBadgeMode", BuildRtxVsrModeTierText())
            : Localization.GetString("RtxVsrBadgeUpscale", _rtxVsrAppliedScaleFactor);
    }

    private string BuildRtxVsrModeTierText()
    {
        var modeName = Localization.GetString(RtxVsrModeResourceKey(_rtxVsrAppliedMode));
        var tierName = Localization.GetString(RtxVsrQualityResourceKey(_rtxVsrAppliedPreset));
        return $"{modeName} {tierName}";
    }

    private static string RtxVsrModeResourceKey(RtxVsrMode mode) => mode switch
    {
        RtxVsrMode.Standard => "SettingsRtxVsrModeStandard",
        RtxVsrMode.Denoise => "SettingsRtxVsrModeDenoise",
        RtxVsrMode.Deblur => "SettingsRtxVsrModeDeblur",
        _ => "SettingsRtxVsrModeHighBitrate",
    };

    private static string RtxVsrQualityResourceKey(RtxVsrQualityPreset preset) => preset switch
    {
        RtxVsrQualityPreset.Low => "SettingsRtxVsrQualityLow",
        RtxVsrQualityPreset.Medium => "SettingsRtxVsrQualityMedium",
        RtxVsrQualityPreset.High => "SettingsRtxVsrQualityHigh",
        _ => "SettingsRtxVsrQualityUltra",
    };

    /// <summary>Flyout.Opening handler: menus never keep stale checked/enabled state.</summary>
    private void ImageContextMenu_Opening(object sender, object e) => UpdateRtxVsrUi();

    private async void RtxVsrToggleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_settings.RtxVsrMenuEnabled)
            {
                return;
            }

            if (_rtxVsrActive)
            {
                await ClearRtxVsrAsync();
            }
            else
            {
                await ApplyRtxVsrAsync();
            }
        }
        catch (Exception ex)
        {
            HandleUnexpectedUiException(ex);
            await ResetRtxVsrAfterFailureAsync(Localization.GetString("RtxVsrFailedStatus", ex.Message));
        }
    }

    private async Task ApplyRtxVsrAsync()
    {
        if (_isRtxVsrProcessing || _currentDocument is not { } document)
        {
            UpdateRtxVsrUi();
            return;
        }

        var availability = _rtxVsrAvailability;
        if (availability?.IsAvailable != true)
        {
            UpdateRtxVsrUi();
            return;
        }

        var wasActive = _rtxVsrActive;
        var previousFullResolutionDocument = _fullResolutionDocument;
        _isRtxVsrProcessing = true;
        _rtxVsrCts?.Cancel();
        _rtxVsrCts?.Dispose();
        _rtxVsrCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        var cancellationToken = _rtxVsrCts.Token;
        UpdateRtxVsrUi();
        var processingStatus = _rtxVsrActive
            ? Localization.GetString("RtxVsrReprocessingStatus")
            : Localization.GetString("RtxVsrProcessingStatus");
        ViewModel.UpdateRenderStatus(processingStatus);
        ShowRtxVsrInfoBar(processingStatus, InfoBarSeverity.Informational, autoClose: false);

        try
        {
            var outcome = await RtxVsrService.EnhanceAsync(
                document,
                _settings.RtxVsrMode,
                _settings.RtxVsrQualityPreset,
                _settings.RtxVsrScaleFactor,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(document, _currentDocument))
            {
                return;
            }

            await _renderer.LoadSdrOverrideAsync(outcome.Bitmap, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(document, _currentDocument))
            {
                return;
            }

            if (!wasActive)
            {
                _rtxVsrPreviousFullResolutionDocument = previousFullResolutionDocument;
            }

            _rtxVsrActive = true;
            _rtxVsrSourceWasHdr = outcome.SourceWasHdr;
            _rtxVsrAppliedScaleFactor = outcome.ScaleFactor;
            _rtxVsrAppliedMode = outcome.Mode;
            _rtxVsrAppliedPreset = outcome.QualityPreset;
            _rtxVsrAppliedPixelSize = (outcome.Bitmap.PixelWidth, outcome.Bitmap.PixelHeight);
            _fullResolutionDocument = document;
            // Framing is deliberately untouched: the layout still targets the
            // document's original pixel size, so the zoom mode, zoom scale and
            // scroll offsets remain exactly what they were before the toggle.
            // That makes switching RTX VSR on/off a direct A/B comparison.
            UpdateImageSurfaceLayout();
            UpdateZoomControls();
            await PresentViewportAsync(cancellationToken);
            UpdateViewerStatus();
            var activeStatus = Localization.GetString("RtxVsrActiveStatus",
                outcome.Bitmap.PixelWidth,
                outcome.Bitmap.PixelHeight,
                outcome.RunMilliseconds);
            ViewModel.UpdateRenderStatus(activeStatus);
            // Keep the result discoverable: the toast explains that the AI
            // rendition is applied (not a no-op) and how to compare it.
            ShowRtxVsrInfoBar(
                activeStatus + "\n" + BuildRtxVsrCompareHint(),
                InfoBarSeverity.Success,
                autoClose: true,
                closeDelayMilliseconds: 9000);
        }
        catch (OperationCanceledException)
        {
        }
        catch (RtxVsrException ex)
        {
            await ResetRtxVsrAfterFailureAsync(Localization.GetString("RtxVsrFailedStatus", ex.Message));
        }
        catch (VsrException ex)
        {
            await ResetRtxVsrAfterFailureAsync(Localization.GetString("RtxVsrFailedStatus", ex.Message));
        }
        catch (Exception ex)
        {
            await ResetRtxVsrAfterFailureAsync(Localization.GetString("RtxVsrFailedStatus", ex.Message));
        }
        finally
        {
            _isRtxVsrProcessing = false;
            UpdateRtxVsrUi();
        }
    }

    private async Task ClearRtxVsrAsync()
    {
        _rtxVsrCts?.Cancel();
        if (!_rtxVsrActive || _currentDocument is not { } document)
        {
            _rtxVsrActive = false;
            _rtxVsrAppliedPixelSize = null;
            UpdateRtxVsrUi();
            return;
        }

        _isRtxVsrProcessing = true;
        UpdateRtxVsrUi();
        try
        {
            await _renderer.LoadAsync(document, _lifetimeToken);
            _fullResolutionDocument = _rtxVsrPreviousFullResolutionDocument;
            _rtxVsrPreviousFullResolutionDocument = null;
            _rtxVsrAppliedPixelSize = null;
            _rtxVsrActive = false;
            // The layout target was never switched to the enhanced bitmap, so the
            // restored rendition appears at exactly the same zoom and scroll
            // position the enhanced one occupied.
            UpdateImageSurfaceLayout();
            UpdateZoomControls();
            await PresentViewportAsync(_lifetimeToken);
            UpdateViewerStatus();
            var disabledStatus = Localization.GetString("RtxVsrDisabledStatus");
            ViewModel.UpdateRenderStatus(disabledStatus);
            ShowRtxVsrInfoBar(disabledStatus, InfoBarSeverity.Informational, autoClose: true, closeDelayMilliseconds: 3000);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.UpdateRenderStatus(Localization.GetString("RtxVsrRestoreFailedStatus", ex.Message));
        }
        finally
        {
            _isRtxVsrProcessing = false;
            UpdateRtxVsrUi();
        }
    }

    private async Task ResetRtxVsrAfterFailureAsync(string status)
    {
        _rtxVsrCts?.Cancel();
        _rtxVsrActive = false;
        _rtxVsrSourceWasHdr = false;
        _rtxVsrAppliedPixelSize = null;
        UpdateRtxVsrUi();
        UpdateViewerStatus();
        ViewModel.UpdateRenderStatus(status);
        ShowRtxVsrInfoBar(status, InfoBarSeverity.Error, autoClose: false);
        if (_currentDocument is not null)
        {
            _ = QueueRtxVsrRestoreAfterFailureAsync(_currentDocument);
        }

        await Task.CompletedTask;
    }

    private async Task QueueRtxVsrRestoreAfterFailureAsync(HdrImageDocument document)
    {
        try
        {
            await _renderer.LoadAsync(document, _lifetimeToken);
            if (ReferenceEquals(document, _currentDocument))
            {
                _fullResolutionDocument = _rtxVsrPreviousFullResolutionDocument;
                _rtxVsrPreviousFullResolutionDocument = null;
                UpdateImageSurfaceLayout();
                UpdateZoomControls();
                await PresentViewportAsync(_lifetimeToken);
            }
        }
        catch (Exception ex)
        {
            ViewModel.UpdateRenderStatus(Localization.GetString("RtxVsrRestoreFailedStatus", ex.Message));
        }
    }

    /// <summary>Resets the per-image feature state when a new document is loaded.</summary>
    private void ResetRtxVsrForNewDocument()
    {
        _rtxVsrCts?.Cancel();
        _rtxVsrCts?.Dispose();
        _rtxVsrCts = null;
        _rtxVsrActive = false;
        _isRtxVsrProcessing = false;
        _rtxVsrSourceWasHdr = false;
        _rtxVsrAppliedPixelSize = null;
        _rtxVsrPreviousFullResolutionDocument = null;
        HideRtxVsrInfoBar();
        UpdateRtxVsrUi();
    }

    /// <summary>Status-bar text: reads the effective mode/factor while VSR is active.</summary>
    private string RtxVsrBadgeText
    {
        get
        {
            if (!_rtxVsrActive)
            {
                return string.Empty;
            }

            return RtxVsrEngine.IsSameResolutionMode(_rtxVsrAppliedMode)
                ? BuildRtxVsrBadgeText()
                : Localization.GetString(
                    _rtxVsrSourceWasHdr ? "RtxVsrBadgeSdr" : "RtxVsrBadge",
                    _rtxVsrAppliedScaleFactor);
        }
    }

    // ---- VSR feedback toast -----------------------------------------------------

    private void ShowRtxVsrInfoBar(string message, InfoBarSeverity severity, bool autoClose, int closeDelayMilliseconds = 6000)
    {
        if (RtxVsrInfoBar is null || string.IsNullOrEmpty(message))
        {
            return;
        }

        if (_rtxVsrInfoTimer is null)
        {
            _rtxVsrInfoTimer = DispatcherQueue.CreateTimer();
            _rtxVsrInfoTimer.IsRepeating = false;
            _rtxVsrInfoTimer.Tick += (_, _) =>
            {
                _rtxVsrInfoTimer?.Stop();
                if (RtxVsrInfoBar is not null)
                {
                    RtxVsrInfoBar.IsOpen = false;
                }
            };
        }

        _rtxVsrInfoTimer.Stop();
        RtxVsrInfoBar.Severity = severity;
        RtxVsrInfoBar.Message = message;
        RtxVsrInfoBar.IsOpen = true;
        if (autoClose)
        {
            _rtxVsrInfoTimer.Interval = TimeSpan.FromMilliseconds(closeDelayMilliseconds);
            _rtxVsrInfoTimer.Start();
        }
    }

    private void HideRtxVsrInfoBar()
    {
        _rtxVsrInfoTimer?.Stop();
        if (RtxVsrInfoBar is not null)
        {
            RtxVsrInfoBar.IsOpen = false;
        }
    }

    private static string BuildRtxVsrCompareHint()
    {
        return Localization.GetString("RtxVsrCompareHint");
    }
}
