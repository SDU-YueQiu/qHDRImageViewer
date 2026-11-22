using HdrImageViewer.Models;

namespace HdrImageViewer.Services;

/// <summary>
/// Test-only stand-in for the WinUI/WinRT-bound <c>BitmapDecodeService</c>.
/// The production service pulls in Windows Imaging, Media Foundation and
/// LibHeifSharp, so the linked <see cref="JxlGainMapDecoder"/> source needs a
/// lightweight decode entry point in the plain net10.0 unit-test host. The JPEG
/// XL base image is decoded with the in-process libjxl decoder exactly as the
/// production JXL preview path does.
/// </summary>
internal static class BitmapDecodeService
{
    public static Task<DecodedBitmap> DecodeDocumentAsync(
        HdrImageDocument document,
        int? maxPixelSize = null,
        CancellationToken cancellationToken = default)
    {
        var probe = document.JxlProbe;
        var transfer = probe is null
            ? DecodedBitmapTransfer.Sdr
            : probe.TransferFunction.Contains("PQ", StringComparison.OrdinalIgnoreCase)
                ? DecodedBitmapTransfer.Pq
                : probe.TransferFunction.Contains("HLG", StringComparison.OrdinalIgnoreCase)
                    ? DecodedBitmapTransfer.Hlg
                    : probe.TransferFunction.Contains("linear", StringComparison.OrdinalIgnoreCase)
                        ? DecodedBitmapTransfer.LinearSceneScRgb
                        : probe.TransferFunction.Contains("709", StringComparison.OrdinalIgnoreCase)
                            ? DecodedBitmapTransfer.Bt709
                            : DecodedBitmapTransfer.Sdr;
        var bitmap = JxlNativeDecoder.DecodeFile(
            document.Path,
            transfer,
            probe?.ColorGamut ?? GainMapColorGamut.Unknown,
            "unit-test in-process libjxl",
            preserveAlpha: true,
            cancellationToken);
        return Task.FromResult(BitmapPreviewResampler.Downscale(bitmap, maxPixelSize, cancellationToken));
    }

    public static Task<DecodedBitmap> DecodeFileRawRgba16Async(
        string path,
        string decoderName,
        int? maxPixelSize = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"The unit-test host cannot run the WinRT fallback decode for '{path}' ({decoderName}, max {maxPixelSize?.ToString() ?? "full"} pixels).");
}
