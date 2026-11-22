using System.Globalization;
using HdrImageViewer.Services;
using HdrImageViewer.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HdrImageViewer.Pages;

public sealed partial class HomePage
{
    private bool _updatingZoomSlider;
    // Fit state shows the inward-bracket "fitted" glyph; every other zoom state
    // shows the outward "expand to fit" glyph, matching Windows Photos.
    private const string ZoomFitModeGlyph = "\uE9A6";
    private const string ZoomActualSizeModeGlyph = "\uE740";
    private string _statusFileSize = string.Empty;
    private string _statusFileSizePath = string.Empty;
    private long? _currentDocumentFileLength;
    private long _statusFileSizeRequestVersion;
    private (int Width, int Height)? _sourceOrientedPixelSize;

    private static bool IsImmersiveViewer => App.MainWindow is MainWindow { IsImmersiveViewing: true };

    // The viewer owns the commands and their state; the window hosts them in
    // its native TitleBar so browsing uses a single row of window chrome.
    public CommandBar TakeCommandBar()
    {
        HomeRoot.Children.Remove(TopActionBar);
        return TopActionBar;
    }

    private void InitializeViewerChrome()
    {
        OpenImageButton.Label = Localization.GetString("ViewerCommandOpen");
        TopCropButton.Label = Localization.GetString("ViewerCommandCrop");
        SingleLayerHdrSaveAsButton.Label = Localization.GetString("ViewerCommandExport");
        TopInspectorToggleButton.Label = Localization.GetString("ViewerCommandInfo");
        TopViewerToolsButton.Label = Localization.GetString("ViewerCommandAnalysis");
        TopBatchExportButton.Label = Localization.GetString("ViewerCommandBatch");
        ViewerSettingsButton.Label = Localization.GetString("NavSettings");
        ViewerAboutButton.Label = Localization.GetString("NavAbout");
        ReloadImageButton.Label = Localization.GetString("ToolbarReloadImage");
        ZoomFitMenuItem.Text = Localization.GetString("ToolbarZoomFit");
        ActualSizeButton.Text = Localization.GetString("ToolbarZoomActual");
        ZoomFillButton.Text = Localization.GetString("ToolbarZoomFillCanvas");
        SetChromeName(FilmstripToggleButton, "ViewerFilmstripToggle");
        SetChromeName(CloseInspectorButton, "ViewerCloseInspector");
        SetChromeName(ZoomMenuButton, "ViewerZoom");
        SetChromeName(ZoomSlider, "ViewerZoom");
        CropPanelTitle.Text = Localization.GetString("ViewerCommandCrop");
        CropPanelHint.Text = Localization.GetString("ViewerCropHint");
        CropOutputLabel.Text = Localization.GetString("ViewerCropOutput");
        CropExportModeSelector.Header = Localization.GetString("CropExportMode");
        CropHdrTransferSelector.Header = Localization.GetString("CropHdrCurve");
        CropUltraHdrGainMapModeSelector.Header = Localization.GetString("CropUltraHdrGainMapMode");
        CropUltraHdrBaseGamutSelector.Header = Localization.GetString("CropUltraHdrBaseGamut");
    }

    private static void SetChromeName(FrameworkElement element, string resourceKey)
    {
        var name = Localization.GetString(resourceKey);
        AutomationProperties.SetName(element, name);
        ToolTipService.SetToolTip(element, name);
    }

    private void ViewerSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewerChromeHideTimer?.Stop();
        (App.MainWindow as MainWindow)?.NavigateToSettings();
    }

    private void ViewerAbout_Click(object sender, RoutedEventArgs e)
    {
        _viewerChromeHideTimer?.Stop();
        (App.MainWindow as MainWindow)?.NavigateToAbout();
    }

    private void ExitViewingMode_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isCropModeEnabled)
        {
            SetCropMode(false);
            args.Handled = true;
        }
        else if (App.MainWindow is MainWindow { IsImmersiveViewing: true } mainWindow)
        {
            mainWindow.SetImmersiveViewing(false);
            args.Handled = true;
        }
    }

    private void FilmstripToggle_Click(object sender, RoutedEventArgs e) =>
        AppSettingsService.SetShowFilmstrip(FilmstripToggleButton.IsChecked == true);

    private void CloseInspector_Click(object sender, RoutedEventArgs e)
    {
        ToggleInspectorPanel();
        TopInspectorToggleButton.Focus(FocusState.Programmatic);
    }

    private async void ZoomPreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuFlyoutItem { Tag: string text }
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            {
                await ApplyZoomPercentageAsync(percentage);
            }
        }
        catch (Exception ex)
        {
            HandleUnexpectedUiException(ex);
        }
    }

    private async void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        try
        {
            if (_updatingZoomSlider || ZoomSlider is null || !ViewModel.HasImage || !IsLoaded)
            {
                return;
            }

            ShowViewerChromeTemporarily();
            await ApplyZoomPercentageAsync(e.NewValue, debounce: true);
        }
        catch (Exception ex)
        {
            HandleUnexpectedUiException(ex);
        }
    }

    private void UpdateZoomPresentation()
    {
        var hasImage = ViewModel.HasImage;
        ZoomMenuButton.IsEnabled = hasImage;
        ZoomSlider.IsEnabled = hasImage;
        var actualSizeScale = CalculateActualSizeZoomScale();
        var fitWidth = ImageSurface.Height > 0 && PreviewSurface.ActualHeight > 0
            ? ViewerViewportMath.CalculateFitSize(PreviewSurface.ActualWidth, PreviewSurface.ActualHeight, ImageSurface.Width / ImageSurface.Height).Width
            : 0;
        var percentage = ViewerViewportMath.CalculateZoomPercentage(ImageSurface.Width * NativeZoomFactor, fitWidth, actualSizeScale);
        // The lazy full-resolution upgrade compares the percentage that is
        // actually on screen against the uploaded texture's native resolution.
        _presentedZoomPercentage = percentage;
        var maximumPercentage = MaximumZoomPercentage;
        ZoomOutButton.IsEnabled = hasImage && percentage > 1.01;
        ZoomInButton.IsEnabled = hasImage && percentage < maximumPercentage - 0.01;
        ZoomLevelText.Text = hasImage ? $"{percentage:0.#}%" : "—";
        ToolTipService.SetToolTip(ZoomMenuButton, !hasImage ? Localization.GetString("ViewerZoom")
            : _isFitZoom ? Localization.GetString("ZoomModeFit")
            : _isFillZoom ? Localization.GetString("ZoomModeFill")
            : _isActualSizeZoom ? Localization.GetString("ToolbarZoomActual")
            : Localization.GetString("ViewerZoom"));
        AutomationProperties.SetName(ZoomMenuButton, $"{Localization.GetString("ViewerZoom")}: {ZoomLevelText.Text}");
        UpdateZoomModeToggle(hasImage);
        _updatingZoomSlider = true;
        try
        {
            ZoomSlider.Maximum = maximumPercentage;
            ZoomSlider.Value = Math.Clamp(percentage, ZoomSlider.Minimum, ZoomSlider.Maximum);
        }
        finally
        {
            _updatingZoomSlider = false;
        }
    }

    // The mode button mirrors Windows Photos: its icon shows the current zoom
    // state and its tooltip names the action the next click performs.
    private void UpdateZoomModeToggle(bool hasImage)
    {
        var isFitMode = !hasImage || _isFitZoom || _zoomTargetFit;
        ZoomModeToggleIcon.Glyph = isFitMode ? ZoomFitModeGlyph : ZoomActualSizeModeGlyph;
        var actionName = !hasImage
            ? Localization.GetString("ViewerZoom")
            : isFitMode ? Localization.GetString("ToolbarZoomActual")
            : Localization.GetString("ToolbarZoomWindow");
        ToolTipService.SetToolTip(ZoomFitButton, actionName);
        AutomationProperties.SetName(ZoomFitButton, actionName);
    }


    private static string FormatFileSizeBytes(long bytes)
    {
        return bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024):0.#} MB"
            : $"{bytes / 1024.0:0.#} KB";
    }

    private void UpdateViewerStatus(bool readFileSize = false)
    {
        if (ImageStatusText is null)
        {
            return;
        }

        if (!ViewModel.HasImage)
        {
            ImageStatusText.Text = string.Empty;
            _statusFileSize = string.Empty;
            _statusFileSizePath = string.Empty;
            _currentDocumentFileLength = null;
            return;
        }

        var currentPath = ViewModel.FilePath;
        if (_currentDocumentFileLength is { } cachedBytes
            && string.Equals(_statusFileSizePath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            _statusFileSize = FormatFileSizeBytes(cachedBytes);
        }
        else if (readFileSize
            && !string.IsNullOrWhiteSpace(currentPath)
            && !string.Equals(_statusFileSizePath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            _statusFileSize = string.Empty;
            _statusFileSizePath = currentPath;
            var requestVersion = ++_statusFileSizeRequestVersion;
            _ = PopulateFileSizeAsync(currentPath, requestVersion);
        }

        RenderViewerStatusBarText();
    }

    private async Task PopulateFileSizeAsync(string filePath, long requestVersion)
    {
        long? length = null;
        try
        {
            length = await Task.Run(() =>
            {
                var info = new FileInfo(filePath);
                return info.Exists ? (long?)info.Length : null;
            }, _lifetimeToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (_lifetimeToken.IsCancellationRequested
            || requestVersion != _statusFileSizeRequestVersion
            || !ViewModel.HasImage
            || !string.Equals(ViewModel.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (length is { } bytes)
        {
            _currentDocumentFileLength = bytes;
            _statusFileSizePath = filePath;
            _statusFileSize = FormatFileSizeBytes(bytes);
            RenderViewerStatusBarText();
        }
    }

    private void RenderViewerStatusBarText()
    {
        if (ImageStatusText is null || !ViewModel.HasImage)
        {
            return;
        }

        var parts = new List<string>();
        if (_settings.ShowStatusFileName)
        {
            parts.Add(ViewModel.FormatName);
        }

        // While RTX VSR is active the layout still follows the original document
        // pixels (that is what keeps the zoom and framing unchanged), but the
        // status bar should report the enhanced rendition's dimensions.
        var sourceSize = _rtxVsrActive && _rtxVsrAppliedPixelSize is { } enhancedSize
            ? enhancedSize
            : GetKnownSourcePixelSize();
        if (_settings.ShowStatusDimensions && sourceSize is { } size)
        {
            parts.Add($"{size.Width} × {size.Height}");
        }

        if (_settings.ShowStatusFileSize && _statusFileSize.Length > 0)
        {
            parts.Add(_statusFileSize);
        }

        if (_settings.ShowStatusHdrKind)
        {
            parts.Add(ViewModel.HdrKind);
        }

        if (_rtxVsrActive)
        {
            parts.Add(RtxVsrBadgeText);
        }

        ImageStatusText.Text = string.Join(" · ", parts);
        var toolTipParts = new List<string> { ViewModel.FileName, ViewModel.HdrKind, ViewModel.RenderStatus };
        if (_rtxVsrActive)
        {
            toolTipParts.Add(RtxVsrBadgeText);
        }

        ToolTipService.SetToolTip(ImageStatusText, string.Join("\n", toolTipParts));
    }

    private (int Width, int Height)? GetKnownSourcePixelSize()
    {
        if (_sourceOrientedPixelSize is { Width: > 0, Height: > 0 } size)
        {
            return size;
        }

        var document = _currentDocument;
        var width = document?.JxlProbe?.PixelWidth ?? document?.ExrProbe?.PixelWidth
            ?? (int?)document?.WicImageProbe?.PixelWidth;
        var height = document?.JxlProbe?.PixelHeight ?? document?.ExrProbe?.PixelHeight
            ?? (int?)document?.WicImageProbe?.PixelHeight;
        if (width is not > 0 || height is not > 0)
        {
            return null;
        }

        return _renderer.ContentOrientation is >= 5 and <= 8 ? (height.Value, width.Value) : (width.Value, height.Value);
    }
}
