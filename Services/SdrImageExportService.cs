using HdrImageViewer.Models;
using Windows.Graphics.Imaging;

namespace HdrImageViewer.Services;

/// <summary>
/// Full-resolution SDR pixels for the current document, in BGRA8 order.
/// Used by the SDR exporter and by content-processing features such as RTX VSR.
/// </summary>
internal readonly record struct SdrPixelFrame(byte[] BgraPixels, int Width, int Height);

internal static class SdrImageExportService
{
    internal static async Task<SdrPixelFrame> DecodeSdrBgraAsync(
        HdrImageDocument document,
        CancellationToken cancellationToken)
    {
        byte[] pixels;
        int width;
        int height;
        if (document.HasRenderableGainMap)
        {
            var inputs = await GainMapRenderInputDecoder.DecodeRenderInputsAsync(document, null, cancellationToken);
            var orientation = new ExifOrientationTransform(
                inputs.Primary.PixelWidth, inputs.Primary.PixelHeight, (int)inputs.Constants.Orientation.X);
            width = orientation.Width;
            height = orientation.Height;
            pixels = await Task.Run(() => ThumbnailPixelConverter.ConvertGainMapToBgra8(inputs, cancellationToken), cancellationToken);
        }
        else
        {
            var bitmap = await BitmapDecodeService.DecodeDocumentForHdrExportAsync(document, cancellationToken);
            width = bitmap.PixelWidth;
            height = bitmap.PixelHeight;
            pixels = await Task.Run(
                () => bitmap.IsHdrEncoded
                    ? ThumbnailPixelConverter.ConvertHdrToBgra8(bitmap, cancellationToken)
                    : ThumbnailPixelConverter.ConvertSdrToBgra8(bitmap, cancellationToken),
                cancellationToken);
        }

        return new SdrPixelFrame(pixels, width, height);
    }

    public static async Task ExportAsync(HdrImageDocument document, string destination, bool jpeg, CancellationToken cancellationToken)
    {
        var frame = await DecodeSdrBgraAsync(document, cancellationToken);
        using var transaction = new ExportFileTransaction(destination);
        await using (var stream = new FileStream(transaction.TemporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
        {
            using var random = stream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, random);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)frame.Width, (uint)frame.Height, 96, 96, frame.BgraPixels);
            await encoder.FlushAsync();
        }

        transaction.Commit(cancellationToken);
    }
}
