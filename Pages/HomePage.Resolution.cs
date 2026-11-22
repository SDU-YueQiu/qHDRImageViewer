using HdrImageViewer.Models;
using HdrImageViewer.Presentation;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;
using Microsoft.UI.Xaml;

namespace HdrImageViewer.Pages;

/// <summary>
/// Keeps the viewer's D3D texture detailed enough for the zoom level on screen.
///
/// The renderer decodes a viewport-sized preview first (roughly 1.5x the
/// surface, capped at 3072 px) so opening a large photo stays fast. Only the
/// zoom controls (percentage combo, slider, fit/actual-size toggle) used to ask
/// for the full-resolution decode, so wheel, button and pinch zooming past the
/// preview's native resolution kept magnifying a low-resolution texture: the
/// image looked blurry until one of those controls happened to request full
/// resolution. This partial upgrades the texture lazily once the user has
/// zoomed beyond it, without changing the zoom level or the framing.
/// </summary>
public sealed partial class HomePage
{
    // A small margin keeps floating-point layout noise from starting a decode
    // that could not add any detail.
    private const double ZoomResolutionThresholdFactor = 1.01;

    // The lazy upgrade is automatic, so it refuses decodes whose texture would
    // not fit comfortably in memory (a 64 MP decode is 256 MB as RGBA8 and
    // 512 MB as RGBA16F). Larger images keep the previous behaviour: only an
    // explicit zoom control asks for their full-resolution decode.
    private const long AutoUpgradeMaxDecodedPixels = 64L * 1024L * 1024L;

    private double _presentedZoomPercentage = 100.0;
    private CancellationTokenSource? _zoomResolutionCts;
    private bool _isZoomResolutionLoadInFlight;

    /// <summary>
    /// Requests a full-resolution decode when the on-screen image is magnified
    /// beyond the uploaded texture. Safe to call after every committed zoom:
    /// it does nothing once the texture already matches the source.
    /// </summary>
    private void MaybeUpgradeZoomResolution()
    {
        if (_isZoomResolutionLoadInFlight
            || !IsLoaded
            || !ViewModel.HasImage
            || _lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        if (_currentDocument is not { } document
            || _rtxVsrActive
            || HdrSwapChainHost.Visibility != Visibility.Visible)
        {
            return;
        }

        // Opening a photo must stay on the preview decode: only a zoom level the
        // user asked for (wheel, buttons, percentage, pinch) may pay for the
        // full-resolution decode. Fit and fill derive their scale from the
        // window, not from a zoom gesture.
        if (_isFitZoom || _isFillZoom || _zoomTargetFit || _zoomTargetFill || _zoomTargetActualSize)
        {
            return;
        }

        var sourceSize = GetKnownSourcePixelSize();
        var decodedMax = Math.Max(_renderer.ContentPixelWidth, _renderer.ContentPixelHeight);
        if (sourceSize is not { } size || decodedMax <= 0)
        {
            return;
        }

        var sourceMax = Math.Max(size.Width, size.Height);
        var decodedPixels = ViewerViewportMath.EstimateDecodedPixelCount(
            size.Width,
            size.Height,
            D3D11HdrRenderPipeline.MaxTextureDimension);
        if (decodedPixels > AutoUpgradeMaxDecodedPixels)
        {
            return;
        }

        if (!ViewerViewportMath.ShouldUpgradeToFullResolution(
                _presentedZoomPercentage,
                decodedMax,
                sourceMax,
                D3D11HdrRenderPipeline.MaxTextureDimension,
                ZoomResolutionThresholdFactor))
        {
            return;
        }

        StartZoomResolutionUpgrade(document);
    }

    private void StartZoomResolutionUpgrade(HdrImageDocument document)
    {
        _isZoomResolutionLoadInFlight = true;
        var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _zoomResolutionCts = source;
        _ = LoadZoomResolutionAsync(document, source);
    }

    private async Task LoadZoomResolutionAsync(HdrImageDocument document, CancellationTokenSource source)
    {
        var cancellationToken = source.Token;
        try
        {
            if (!ReferenceEquals(document, _currentDocument))
            {
                return;
            }

            ViewModel.UpdateRenderStatus(Localization.GetString("StatusLoadingFullResolution"));
            await _renderer.LoadFullResolutionAsync(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(document, _currentDocument))
            {
                return;
            }

            _fullResolutionDocument = document;
            _sourceOrientedPixelSize ??= _renderer.ContentOrientation is >= 5 and <= 8
                ? (_renderer.ContentPixelHeight, _renderer.ContentPixelWidth)
                : (_renderer.ContentPixelWidth, _renderer.ContentPixelHeight);
            UpdateZoomControls();
            // The layout is untouched on purpose: the sharper texture replaces
            // the preview in place, so the zoom level and scroll position the
            // user is looking at do not move.
            await PresentViewportAsync(cancellationToken);
            ViewModel.UpdateRenderStatus(_renderer.LastRenderStatus);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.UpdateRenderStatus(
                Localization.GetString("StatusLoadingFullResolutionFailedFormat", ex.GetType().Name, ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_zoomResolutionCts, source))
            {
                _zoomResolutionCts = null;
                _isZoomResolutionLoadInFlight = false;
            }

            source.Dispose();
        }
    }

    private void CancelZoomResolutionUpgrade()
    {
        _isZoomResolutionLoadInFlight = false;
        CancelAndDispose(ref _zoomResolutionCts);
    }
}
