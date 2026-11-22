using HdrImageViewer.Models;
using HdrImageViewer.Rendering;

namespace HdrImageViewer.Services;

/// <summary>
/// Result of one RTX VSR enhancement: enhanced SDR content plus metadata.
/// <see cref="ScaleFactor"/> is the EFFECTIVE output scale: 1 for the
/// same-resolution Denoise/Deblur modes, otherwise 2 or 4.
/// </summary>
internal sealed record RtxVsrOutcome(
    DecodedBitmap Bitmap,
    int ScaleFactor,
    int QualityLevel,
    RtxVsrMode Mode,
    RtxVsrQualityPreset QualityPreset,
    bool SourceWasHdr,
    double RunMilliseconds,
    string EngineDescription);

/// <summary>Cheap, cached capability state shown by the viewer and settings pages.</summary>
internal sealed record RtxVsrAvailability(
    bool DllsFound,
    bool GpuSupported,
    string LibDirectory,
    string Detail,
    string GpuName,
    string DriverVersion,
    string ComputeCapability,
    string MemoryTotal)
{
    public bool IsAvailable => DllsFound && GpuSupported;

    public static RtxVsrAvailability Unavailable(string detail) =>
        new(false, false, string.Empty, detail, string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// App-level RTX Video Super Resolution orchestration. The NVIDIA runtime DLLs
/// are intentionally not bundled with the application: they are discovered at
/// runtime (see <see cref="RtxVsrEngine.CandidateLibDirs"/>) and only needed
/// when the feature is used.
/// </summary>
internal static class RtxVsrService
{
    /// <summary>VRAM-safe output cap; two 2x passes on the default cap is plenty for photos.</summary>
    private const long DefaultMaxOutputPixels = 36_000_000;

    private static readonly object s_stateLock = new();
    private static readonly Lazy<RtxVsrEngine> s_engine = new(
        static () => new RtxVsrEngine { MaxOutputPixels = DefaultMaxOutputPixels },
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly SemaphoreSlim s_enhanceGate = new(1, 1);

    private static RtxVsrAvailability? s_availability;
    private static RtxVsrOutcome? s_cachedOutcome;
    private static string s_cachedKey = string.Empty;
    private static string s_lastStatus = "RTX VSR not probed";

    public static string LastStatus
    {
        get
        {
            lock (s_stateLock)
            {
                return s_lastStatus;
            }
        }
    }

    /// <summary>
    /// Cheap probe (no model load): checks the NVIDIA DLLs, then asks
    /// nvidia-smi for the adapter and compute capability. Run off the UI thread
    /// because the GPU query launches one short process.
    /// </summary>
    public static RtxVsrAvailability Probe()
    {
        lock (s_stateLock)
        {
            if (s_availability is { } cached)
            {
                return cached;
            }
        }

        RtxVsrAvailability result;
        if (!RtxVsrEngine.IsAvailable(out var libDirectory, out var fileDetail))
        {
            result = RtxVsrAvailability.Unavailable(fileDetail);
        }
        else if (!RtxVsrEngine.TryProbeGpu(out var gpu, out var gpuDetail) || gpu is null)
        {
            result = new RtxVsrAvailability(true, false, libDirectory, gpuDetail, string.Empty, string.Empty, string.Empty, string.Empty);
        }
        else
        {
            var gpuSupported = gpu.TuringOrNewer;
            result = new RtxVsrAvailability(
                true,
                gpuSupported,
                libDirectory,
                gpuSupported ? "RTX VSR capable" : "GPU compute capability is below the RTX VSR floor (7.5)",
                gpu.Name,
                gpu.DriverVersion,
                gpu.ComputeCapability,
                gpu.MemoryTotal);
        }

        lock (s_stateLock)
        {
            s_availability = result;
            s_lastStatus = result.Detail;
            return result;
        }
    }

    public static void InvalidateProbe()
    {
        lock (s_stateLock)
        {
            s_availability = null;
        }
    }

    public static void ClearCache()
    {
        lock (s_stateLock)
        {
            s_cachedOutcome = null;
            s_cachedKey = string.Empty;
        }
    }

    /// <summary>
    /// Enhances the SDR rendition of <paramref name="document"/> with RTX VSR.
    /// The app-level <paramref name="mode"/> + <paramref name="preset"/> pair is
    /// mapped to a native QualityLevel by <see cref="RtxVsrEngine.QualityLevelFor"/>.
    /// Upscaling modes (Standard / HighBitrate) honour <paramref name="scaleFactor"/>
    /// (2x uses the native model once; 4x runs the 2x model twice because that
    /// measured better than a single 4x destination). Denoise / Deblur are
    /// same-resolution modes: they always run a single pass at the input size and
    /// ignore the scale preference.
    /// Runs the native effect on a background thread; safe to await from the UI.
    /// </summary>
    public static async Task<RtxVsrOutcome> EnhanceAsync(
        HdrImageDocument document,
        RtxVsrMode mode,
        RtxVsrQualityPreset preset,
        int scaleFactor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var availability = Probe();
        if (!availability.DllsFound)
        {
            throw new RtxVsrException(availability.Detail);
        }

        mode = AppUserSettings.NormalizeRtxVsrMode(mode);
        preset = AppUserSettings.NormalizeRtxVsrQualityPreset(preset);
        var sameResolution = RtxVsrEngine.IsSameResolutionMode(mode);
        var scale = sameResolution ? 1 : NormalizeScaleFactor(scaleFactor);
        var quality = RtxVsrEngine.QualityLevelFor(mode, preset);
        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(document.Path);
        var cacheKey = $"{document.Path}|{lastWriteTimeUtc.Ticks}|{mode}|{preset}|{scale}|{quality}";
        lock (s_stateLock)
        {
            if (s_cachedOutcome is { } cached && string.Equals(s_cachedKey, cacheKey, StringComparison.Ordinal))
            {
                s_lastStatus = "RTX VSR result served from cache";
                return cached;
            }
        }

        var frame = await SdrImageExportService.DecodeSdrBgraAsync(document, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var inputPixels = (long)frame.Width * frame.Height;
        if (!sameResolution)
        {
            var outputPixels = scale == 4 ? inputPixels * 16 : inputPixels * 4;
            if (outputPixels > DefaultMaxOutputPixels)
            {
                throw new RtxVsrException(
                    $"Image is too large for RTX VSR at {scale}x ({frame.Width}x{frame.Height}); " +
                    $"the {DefaultMaxOutputPixels / 1_000_000} MP output cap would be exceeded. " +
                    "Use 2x, a same-resolution mode, or a smaller image.");
            }
        }

        var engine = s_engine.Value;
        await s_enhanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sourceWasHdr = IsHdrSource(document);
            var runWatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await Task.Run(
                () =>
                {
                    if (!engine.IsInitialized)
                    {
                        if (!engine.Initialize())
                        {
                            throw new RtxVsrException("RTX VSR engine failed to initialize: " + engine.Status);
                        }
                    }

                    if (sameResolution)
                    {
                        // Denoise / Deblur require destination size == input size.
                        var sameSize = engine.ProcessToSize(
                            frame.BgraPixels, frame.Width, frame.Height, frame.Width, frame.Height, quality);
                        return (sameSize.Bgra, sameSize.Width, sameSize.Height);
                    }

                    if (scale == 4)
                    {
                        var first = engine.Process(frame.BgraPixels, frame.Width, frame.Height, quality, 2);
                        var second = engine.Process(first.Bgra, first.Width, first.Height, quality, 2);
                        return (second.Bgra, second.Width, second.Height);
                    }

                    var single = engine.Process(frame.BgraPixels, frame.Width, frame.Height, quality, 2);
                    return (single.Bgra, single.Width, single.Height);
                },
                cancellationToken).ConfigureAwait(false);
            runWatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            var rgba = await Task.Run(
                () => ConvertBgraToRgba(result.Bgra, result.Width, result.Height, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            var bitmap = new DecodedBitmap(
                result.Width,
                result.Height,
                rgba,
                ColorManagedToSrgb: true,
                DecoderName: "RTX VSR",
                PixelFormat: DecodedBitmapPixelFormat.Rgba8Unorm,
                Transfer: DecodedBitmapTransfer.Sdr);

            var outcome = new RtxVsrOutcome(
                bitmap,
                scale,
                quality,
                mode,
                preset,
                sourceWasHdr,
                runWatch.Elapsed.TotalMilliseconds,
                $"{result.Width}x{result.Height} {mode}/{preset} q{quality} (scale {scale}) in {runWatch.Elapsed.TotalMilliseconds:0} ms");
            lock (s_stateLock)
            {
                s_cachedOutcome = outcome;
                s_cachedKey = cacheKey;
                s_lastStatus = "RTX VSR enhanced " + outcome.EngineDescription;
            }

            return outcome;
        }
        finally
        {
            s_enhanceGate.Release();
        }
    }

    public static int NormalizeScaleFactor(int scaleFactor) => scaleFactor == 4 ? 4 : 2;

    private static bool IsHdrSource(HdrImageDocument document) =>
        document.HasRenderableGainMap
        || document.Format.Kind == HdrImageKind.SingleLayerHdr
        || document.HeifAvifProbe?.HasHdrTransfer == true
        || document.JxlProbe?.IsHdrTransfer == true
        || document.RadianceHdrProbe is not null
        || document.ExrProbe is not null;

    private static byte[] ConvertBgraToRgba(byte[] bgra, int width, int height, CancellationToken cancellationToken)
    {
        var pixelCount = checked(width * height);
        var rgba = new byte[checked(pixelCount * 4)];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var sourceIndex = y * width * 4;
            var targetIndex = sourceIndex;
            for (var x = 0; x < width; x++)
            {
                var b = bgra[sourceIndex];
                var g = bgra[sourceIndex + 1];
                var r = bgra[sourceIndex + 2];
                var a = bgra[sourceIndex + 3];
                rgba[targetIndex] = r;
                rgba[targetIndex + 1] = g;
                rgba[targetIndex + 2] = b;
                rgba[targetIndex + 3] = a;
                sourceIndex += 4;
                targetIndex += 4;
            }
        });
        return rgba;
    }
}

/// <summary>User-facing RTX VSR failure with a ready-to-display reason.</summary>
internal sealed class RtxVsrException : Exception
{
    public RtxVsrException(string message)
        : base(message)
    {
    }
}
