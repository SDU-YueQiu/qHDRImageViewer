namespace HdrImageViewer.Presentation;

internal static class ViewerViewportMath
{
    public static (uint X, uint Y, uint Width, uint Height) CalculateCropPixels(
        double left, double top, double right, double bottom, uint pixelWidth, uint pixelHeight)
    {
        if (pixelWidth == 0 || pixelHeight == 0)
        {
            return default;
        }

        var x = (uint)Math.Clamp(Math.Round(left * pixelWidth), 0.0, pixelWidth - 1.0);
        var y = (uint)Math.Clamp(Math.Round(top * pixelHeight), 0.0, pixelHeight - 1.0);
        var endX = (uint)Math.Clamp(Math.Round(right * pixelWidth), x + 1.0, pixelWidth);
        var endY = (uint)Math.Clamp(Math.Round(bottom * pixelHeight), y + 1.0, pixelHeight);
        return (x, y, endX - x, endY - y);
    }

    public static (double Width, double Height) CalculateFitSize(
        double availableWidth,
        double availableHeight,
        double aspectRatio)
    {
        var availableAspectRatio = availableWidth / availableHeight;
        if (availableAspectRatio > aspectRatio)
        {
            var height = availableHeight;
            return (height * aspectRatio, height);
        }

        var width = availableWidth;
        return (width, width / aspectRatio);
    }

    public static (double Width, double Height) CalculateFillSize(
        double availableWidth,
        double availableHeight,
        double aspectRatio)
    {
        var availableAspectRatio = availableWidth / availableHeight;
        if (availableAspectRatio > aspectRatio)
        {
            var width = availableWidth;
            return (width, width / aspectRatio);
        }

        var height = availableHeight;
        return (height * aspectRatio, height);
    }

    /// <summary>
    /// Fit mode must not upscale an image past its native size: Windows Photos
    /// shows small images at 100 % (one source pixel per physical display pixel)
    /// centered in the viewport instead of stretching them to fill it. Returns
    /// the fit-relative scale to apply to a fit-to-viewport size; 1.0 keeps the
    /// plain fit size and smaller values cap it at the actual-size scale.
    /// </summary>
    public static double CalculateFitZoomTargetScale(double actualSizeScale)
    {
        if (!IsPositiveFinite(actualSizeScale))
        {
            return 1.0;
        }

        return Math.Min(1.0, actualSizeScale);
    }

    /// <summary>
    /// Fit target size that never upscales beyond actual size, so a small image
    /// opens at 100 % and only large images fill the viewport.
    /// </summary>
    public static (double Width, double Height) CalculateCappedFitSize(
        double availableWidth,
        double availableHeight,
        double aspectRatio,
        double actualSizeScale)
    {
        var (width, height) = CalculateFitSize(availableWidth, availableHeight, aspectRatio);
        var scale = CalculateFitZoomTargetScale(actualSizeScale);
        return (width * scale, height * scale);
    }

    /// <summary>
    /// Returns the scrollable content extents for the image. An image that is
    /// larger than the viewport keeps the existing image-sized extent; an image
    /// that fits inside the viewport gets one viewport of drag slack on each
    /// side so the ScrollViewer's panning can reposition it within the preview
    /// instead of being locked to the center.
    /// </summary>
    public static (double Width, double Height) CalculateImageViewportSize(
        double previewWidth,
        double previewHeight,
        double imageWidth,
        double imageHeight)
    {
        return (
            CalculateImageViewportExtent(previewWidth, imageWidth),
            CalculateImageViewportExtent(previewHeight, imageHeight));
    }

    public static double CalculateImageViewportExtent(double previewExtent, double imageExtent)
    {
        if (!IsPositiveFinite(imageExtent))
        {
            return IsPositiveFinite(previewExtent) ? previewExtent : 0.0;
        }

        if (!IsPositiveFinite(previewExtent) || imageExtent >= previewExtent)
        {
            return imageExtent;
        }

        // Contained image: add the slack that lets a drag move it to either
        // edge of the preview (centered offset is (extent - viewport) / 2).
        return (2.0 * previewExtent) - imageExtent;
    }

    /// <summary>
    /// Scroll offset that centers an image which is smaller than the viewport
    /// inside the drag-slack content extent produced by
    /// <see cref="CalculateImageViewportExtent"/>.
    /// </summary>
    public static double CalculateContainedImageCenterOffset(double imageExtent, double viewportExtent)
    {
        if (!IsPositiveFinite(imageExtent)
            || !IsPositiveFinite(viewportExtent)
            || imageExtent >= viewportExtent)
        {
            return 0.0;
        }

        return (CalculateImageViewportExtent(viewportExtent, imageExtent) - viewportExtent) / 2.0;
    }

    /// <summary>
    /// Rounds a logical offset to a whole physical display pixel so 1:1
    /// swap-chain content stays crisp while a contained image is dragged
    /// (fractional layout positions would resample the presented frame).
    /// </summary>
    public static double SnapToPhysicalPixel(double logicalOffset, double compositionScale)
    {
        if (!double.IsFinite(logicalOffset) || !IsPositiveFinite(compositionScale))
        {
            return logicalOffset;
        }

        return Math.Round(logicalOffset * compositionScale, MidpointRounding.AwayFromZero) / compositionScale;
    }

    public static double CalculatePointerAnchorBlend(double targetSize, double viewportSize)
    {
        if (targetSize <= 0.0 || viewportSize <= 1.0)
        {
            return 0.0;
        }

        var transition = Math.Clamp(viewportSize * 0.18, 96.0, 220.0);
        var amount = Math.Clamp((targetSize - viewportSize) / transition, 0.0, 1.0);
        return amount * amount * (3.0 - (2.0 * amount));
    }

    public static double CalculateActualSizeZoomScale(
        double availableWidth,
        double availableHeight,
        double aspectRatio,
        int contentPixelWidth,
        int contentPixelHeight,
        double compositionScaleX,
        double compositionScaleY,
        bool orientationSwapsDimensions)
    {
        if (!IsPositiveFinite(availableWidth)
            || !IsPositiveFinite(availableHeight)
            || !IsPositiveFinite(aspectRatio)
            || contentPixelWidth <= 0
            || contentPixelHeight <= 0
            || !IsPositiveFinite(compositionScaleX)
            || !IsPositiveFinite(compositionScaleY))
        {
            return 1.0;
        }

        var (fitWidth, fitHeight) = CalculateFitSize(availableWidth, availableHeight, aspectRatio);
        if (orientationSwapsDimensions)
        {
            (contentPixelWidth, contentPixelHeight) = (contentPixelHeight, contentPixelWidth);
        }

        var physicalFitWidth = fitWidth * Math.Max(compositionScaleX, double.Epsilon);
        var physicalFitHeight = fitHeight * Math.Max(compositionScaleY, double.Epsilon);
        var scaleX = contentPixelWidth / Math.Max(physicalFitWidth, 1.0);
        var scaleY = contentPixelHeight / Math.Max(physicalFitHeight, 1.0);
        var actualSizeScale = Math.Min(scaleX, scaleY);
        return IsPositiveFinite(actualSizeScale) ? actualSizeScale : 1.0;
    }

    /// <summary>
    /// Clamps an interactive zoom scale to the 1 % .. <paramref name="maximumZoomScale"/>
    /// range expressed in source pixels. <paramref name="maximumZoomScale"/> is the
    /// maximum relative to actual size (8.0 = 800 %), matching MaxZoomPercent.
    /// </summary>
    public static double ClampUserZoomScale(
        double targetScale,
        double actualSizeScale,
        double maximumZoomScale = 8.0)
    {
        if (!IsPositiveFinite(actualSizeScale)) return 1.0;
        if (double.IsNaN(targetScale)) return actualSizeScale;

        var maximum = IsPositiveFinite(maximumZoomScale) ? maximumZoomScale : 8.0;
        // The interaction limits are relative to source pixels, not to fit mode.
        return Math.Clamp(targetScale, actualSizeScale * 0.01, actualSizeScale * maximum);
    }

    /// <summary>
    /// Keeps a zoom-out from moving a fit-relative scale below fit-to-window.
    /// When the current scale already sits below fit, the requested zoom-out is
    /// treated as a no-op instead of snapping back up to fit.
    /// </summary>
    public static double ClampZoomOutToFit(double targetScale, double currentScale)
    {
        if (!double.IsFinite(targetScale) || !double.IsFinite(currentScale))
        {
            return targetScale;
        }

        return Math.Max(targetScale, Math.Min(currentScale, 1.0));
    }

    public static double CalculateZoomPercentage(double displayedWidth, double fitWidth, double actualSizeScale)
    {
        if (!IsPositiveFinite(displayedWidth)
            || !IsPositiveFinite(fitWidth)
            || !IsPositiveFinite(actualSizeScale)) return 100.0;

        var percentage = displayedWidth / fitWidth / actualSizeScale * 100.0;
        return IsPositiveFinite(percentage) ? percentage : 100.0;
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0.0;

    public static double CalculateNativeZoomCommitOffset(
        double imageSize, double contentSize, double viewportSize, double factor, double offset)
    {
        if (!IsPositiveFinite(imageSize) || !IsPositiveFinite(contentSize)
            || !IsPositiveFinite(viewportSize) || !IsPositiveFinite(factor) || !double.IsFinite(offset)) return 0;
        var scaledImage = imageSize * factor;
        var oldContent = Math.Max(viewportSize, contentSize * factor);
        var newContent = Math.Max(viewportSize, scaledImage);
        // Remove the scaled letterbox margins while retaining the same visible
        // image coordinates, clamping only when the image fits the viewport.
        return Math.Clamp(offset + (newContent - oldContent) / 2.0, 0, newContent - viewportSize);
    }

    public static double Lerp(double from, double to, double amount)
    {
        return from + ((to - from) * amount);
    }

    public static (int PixelWidth, int PixelHeight) CalculateSwapChainPixelSize(
        double previewWidth,
        double previewHeight,
        double compositionScaleX,
        double compositionScaleY)
    {
        if (previewWidth <= 0.0 || previewHeight <= 0.0)
        {
            return (0, 0);
        }

        var pixelWidth = (int)Math.Round(previewWidth * Math.Max(compositionScaleX, 0.0));
        var pixelHeight = (int)Math.Round(previewHeight * Math.Max(compositionScaleY, 0.0));
        return (Math.Max(0, pixelWidth), Math.Max(0, pixelHeight));
    }

    /// <summary>
    /// Maps the visible preview panel onto the logical image rectangle using the
    /// shader ImageLayout convention: fittedUv = (panelUv - offset) / scale.
    /// </summary>
    public static bool ImageFitsInPreview(
        double imageWidth,
        double imageHeight,
        double previewWidth,
        double previewHeight)
    {
        return imageWidth > 0.0
            && imageHeight > 0.0
            && previewWidth > 0.0
            && previewHeight > 0.0
            && imageWidth <= previewWidth + 0.5
            && imageHeight <= previewHeight + 0.5;
    }

    public static double CalculateZoomPreviewScale(double presentedImageWidth, double targetImageWidth)
    {
        if (presentedImageWidth <= 0.0 || targetImageWidth <= 0.0)
        {
            return 1.0;
        }

        return Math.Clamp(targetImageWidth / presentedImageWidth, 0.2, 8.0);
    }

    public static (double X, double Y) CalculateZoomPreviewOrigin(
        double anchorViewportX,
        double anchorViewportY,
        double previewWidth,
        double previewHeight)
    {
        if (previewWidth <= 0.0 || previewHeight <= 0.0)
        {
            return (0.5, 0.5);
        }

        return (
            Math.Clamp(anchorViewportX / previewWidth, 0.0, 1.0),
            Math.Clamp(anchorViewportY / previewHeight, 0.0, 1.0));
    }

    public static ViewerSwapChainHostLayout CalculateSwapChainHostLayout(
        double previewWidth,
        double previewHeight,
        double imageWidth,
        double imageHeight,
        double contentWidth,
        double contentHeight,
        double scrollX,
        double scrollY)
    {
        if (previewWidth <= 0.0
            || previewHeight <= 0.0
            || imageWidth <= 0.0
            || imageHeight <= 0.0)
        {
            return ViewerSwapChainHostLayout.Invalid;
        }

        var imageLeft = Math.Max(0.0, (contentWidth - imageWidth) / 2.0);
        var imageTop = Math.Max(0.0, (contentHeight - imageHeight) / 2.0);
        var visibleLeft = Math.Max(scrollX, imageLeft);
        var visibleTop = Math.Max(scrollY, imageTop);
        var visibleRight = Math.Min(scrollX + previewWidth, imageLeft + imageWidth);
        var visibleBottom = Math.Min(scrollY + previewHeight, imageTop + imageHeight);
        var hostWidth = visibleRight - visibleLeft;
        var hostHeight = visibleBottom - visibleTop;
        if (hostWidth < 2.0 || hostHeight < 2.0)
        {
            hostWidth = Math.Min(imageWidth, previewWidth);
            hostHeight = Math.Min(imageHeight, previewHeight);
            if (hostWidth < 2.0 || hostHeight < 2.0)
            {
                return ViewerSwapChainHostLayout.Invalid;
            }

            return new ViewerSwapChainHostLayout(hostWidth, hostHeight, 1.0f, 1.0f, 0.0f, 0.0f);
        }

        // The host is a sibling of the ScrollViewer, so it has to be placed at
        // the panned position explicitly: visibleLeft - scrollX equals the
        // image's on-screen left edge whenever the host is not covering the
        // whole preview. Clamped because the ScrollViewer offset may be one
        // frame stale right after a layout change.
        var hostLeft = Math.Clamp(visibleLeft - scrollX, 0.0, Math.Max(0.0, previewWidth - hostWidth));
        var hostTop = Math.Clamp(visibleTop - scrollY, 0.0, Math.Max(0.0, previewHeight - hostHeight));
        return new ViewerSwapChainHostLayout(
            hostWidth,
            hostHeight,
            (float)(imageWidth / hostWidth),
            (float)(imageHeight / hostHeight),
            (float)((imageLeft - visibleLeft) / hostWidth),
            (float)((imageTop - visibleTop) / hostHeight),
            hostLeft,
            hostTop);
    }

    public static ViewerVisibleImageLayout CalculateVisibleImageLayout(
        double previewWidth,
        double previewHeight,
        double imageWidth,
        double imageHeight,
        double contentWidth,
        double contentHeight,
        double scrollX,
        double scrollY)
    {
        if (previewWidth <= 0.0
            || previewHeight <= 0.0
            || imageWidth <= 0.0
            || imageHeight <= 0.0)
        {
            return ViewerVisibleImageLayout.Identity;
        }

        var imageLeft = Math.Max(0.0, (contentWidth - imageWidth) / 2.0);
        var imageTop = Math.Max(0.0, (contentHeight - imageHeight) / 2.0);
        return new ViewerVisibleImageLayout(
            (float)(imageWidth / previewWidth),
            (float)(imageHeight / previewHeight),
            (float)((imageLeft - scrollX) / previewWidth),
            (float)((imageTop - scrollY) / previewHeight));
    }
}

internal readonly record struct ViewerSwapChainHostLayout(
    double HostWidth,
    double HostHeight,
    float ScaleX,
    float ScaleY,
    float OffsetX,
    float OffsetY,
    double HostLeft = 0.0,
    double HostTop = 0.0)
{
    public static ViewerSwapChainHostLayout Invalid { get; } = new(0.0, 0.0, 1.0f, 1.0f, 0.0f, 0.0f);

    public bool IsValid =>
        HostWidth >= 2.0
        && HostHeight >= 2.0
        && float.IsFinite(ScaleX)
        && float.IsFinite(ScaleY)
        && ScaleX > 0.0f
        && ScaleY > 0.0f
        && float.IsFinite(OffsetX)
        && float.IsFinite(OffsetY)
        && double.IsFinite(HostLeft)
        && double.IsFinite(HostTop);

    public bool CoversPreview(double previewWidth, double previewHeight) =>
        HostWidth >= previewWidth - 0.5
        && HostHeight >= previewHeight - 0.5;
}

internal readonly record struct ViewerVisibleImageLayout(
    float ScaleX,
    float ScaleY,
    float OffsetX,
    float OffsetY)
{
    public static ViewerVisibleImageLayout Identity { get; } = new(1.0f, 1.0f, 0.0f, 0.0f);

    public bool IsValid =>
        float.IsFinite(ScaleX)
        && float.IsFinite(ScaleY)
        && ScaleX > 0.0f
        && ScaleY > 0.0f
        && float.IsFinite(OffsetX)
        && float.IsFinite(OffsetY);
}
