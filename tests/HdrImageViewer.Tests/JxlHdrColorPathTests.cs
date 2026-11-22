using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

/// <summary>
/// HDR color-path regression tests for the in-process libjxl JPEG XL decoder.
/// The tests generate real fixtures with the bundled cjxl.exe and compare the
/// in-process decode against djxl.exe output requested in the exact color
/// encoding the renderer is told about. They cover:
///  - PQ/HLG structured JPEG XL color encodings (no conversion expected),
///  - scene-linear (float) JPEG XL decoded to RGBA16F (JXL_TYPE_FLOAT16),
///  - HDR JPEG XL whose original color profile is an attached ICC profile
///    (the decoder has to be asked for PQ/HLG output explicitly),
///  - jhgm ISO 21496-1 gain-map bundles stored in raw and Brotli-compressed
///    ("brob") container boxes.
/// All tests are skipped unless the pinned codec runtime is present locally.
/// </summary>
public sealed class JxlHdrColorPathTests : IDisposable
{
    private const int Width = 64;
    private const int Height = 32;

    private readonly string _tempDirectory;

    public JxlHdrColorPathTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "HdrImageViewerTests",
            "jxl-hdr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    public sealed class JxlNativeFactAttribute : FactAttribute
    {
        public JxlNativeFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HDRVIEWER_REQUIRE_NATIVE_TESTS") != "1")
            {
                Skip = MissingNativeDependency();
            }
        }
    }

    // ------------------------------------------------------------------
    // Structured PQ / HLG color encodings
    // ------------------------------------------------------------------

    [JxlNativeFact]
    public async Task StructuredPqJxlIsProbedAsPqBt2020AndDecodesToDjxlPq()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;
        var decoder = NativeToolLocator.FindTool("djxl.exe")!;

        var ppmPath = Path.Combine(_tempDirectory, "pq16.ppm");
        WritePatchPpm16(ppmPath, index => PqPatches()[index]);
        var jxlPath = Path.Combine(_tempDirectory, "pq16.jxl");
        await EncodeWithCjxlAsync(
            encoder,
            ppmPath,
            jxlPath,
            "-x", "color_space=RGB_D65_202_Rel_PeQ",
            "--intensity_target=1000");

        var probe = await JxlProbe.ProbeAsync(jxlPath);
        Assert.NotNull(probe);
        Assert.Contains("PQ", probe.TransferFunction, StringComparison.OrdinalIgnoreCase);
        Assert.True(probe.UsesBt2020Primaries);
        Assert.Equal(GainMapColorGamut.Bt2100, probe.ColorGamut);

        var decoded = JxlNativeDecoder.DecodeFile(
            jxlPath,
            DecodedBitmapTransfer.Pq,
            probe.ColorGamut,
            "pq test",
            preserveAlpha: false,
            CancellationToken.None);
        Assert.Equal(DecodedBitmapTransfer.Pq, decoded.Transfer);
        Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, decoded.PixelFormat);
        Assert.True(decoded.UsesBt2020Primaries);
        Assert.Equal(Width, decoded.PixelWidth);
        Assert.Equal(Height, decoded.PixelHeight);

        var reference = await DecodeWithDjxlToPpm16Async(decoder, jxlPath, "RGB_D65_202_Rel_PeQ");
        Assert.Equal(reference.PixelWidth, decoded.PixelWidth);
        Assert.Equal(reference.PixelHeight, decoded.PixelHeight);
        AssertU16Close(reference.RgbaPixels, decoded.RgbaPixels, tolerance: 2);
    }

    [JxlNativeFact]
    public async Task StructuredHlgJxlIsProbedAsHlgBt2020AndDecodesToDjxlHlg()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;
        var decoder = NativeToolLocator.FindTool("djxl.exe")!;

        var ppmPath = Path.Combine(_tempDirectory, "hlg16.ppm");
        WritePatchPpm16(ppmPath, index => HlgPatches()[index]);
        var jxlPath = Path.Combine(_tempDirectory, "hlg16.jxl");
        await EncodeWithCjxlAsync(
            encoder,
            ppmPath,
            jxlPath,
            "-x", "color_space=RGB_D65_202_Rel_HLG",
            "--intensity_target=1000");

        var probe = await JxlProbe.ProbeAsync(jxlPath);
        Assert.NotNull(probe);
        Assert.Contains("HLG", probe.TransferFunction, StringComparison.OrdinalIgnoreCase);
        Assert.True(probe.UsesBt2020Primaries);

        var decoded = JxlNativeDecoder.DecodeFile(
            jxlPath,
            DecodedBitmapTransfer.Hlg,
            probe.ColorGamut,
            "hlg test",
            preserveAlpha: false,
            CancellationToken.None);
        Assert.Equal(DecodedBitmapTransfer.Hlg, decoded.Transfer);
        Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, decoded.PixelFormat);

        var reference = await DecodeWithDjxlToPpm16Async(decoder, jxlPath, "RGB_D65_202_Rel_HLG");
        AssertU16Close(reference.RgbaPixels, decoded.RgbaPixels, tolerance: 2);
    }

    // ------------------------------------------------------------------
    // Scene-linear (float) JPEG XL: regression for JXL_TYPE_FLOAT16
    // ------------------------------------------------------------------

    [JxlNativeFact]
    public async Task SceneLinearJxlDecodesInProcessToFloat16MatchingDjxlPfm()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;
        var decoder = NativeToolLocator.FindTool("djxl.exe")!;

        var ppmPath = Path.Combine(_tempDirectory, "linear16.ppm");
        WritePatchPpm16(ppmPath, index => LinearPatches()[index]);
        var jxlPath = Path.Combine(_tempDirectory, "linear16.jxl");
        await EncodeWithCjxlAsync(encoder, ppmPath, jxlPath, "-x", "color_space=RGB_D65_SRG_Rel_Lin");

        var probe = await JxlProbe.ProbeAsync(jxlPath);
        Assert.NotNull(probe);
        Assert.Contains("linear", probe.TransferFunction, StringComparison.OrdinalIgnoreCase);
        Assert.True(probe.IsHdrTransfer);

        // The old decoder passed the wrong JxlDataType value for float16 (4
        // instead of 5), so this decode always failed with JXL_DEC_ERROR and the
        // app silently fell back to spawning djxl and reading a temporary EXR.
        var decoded = JxlNativeDecoder.DecodeFile(
            jxlPath,
            DecodedBitmapTransfer.LinearSceneScRgb,
            probe.ColorGamut,
            "linear test",
            preserveAlpha: false,
            CancellationToken.None);
        Assert.Equal(DecodedBitmapPixelFormat.Rgba16Float, decoded.PixelFormat);
        Assert.Equal(DecodedBitmapTransfer.LinearSceneScRgb, decoded.Transfer);
        Assert.Equal(Width, decoded.PixelWidth);
        Assert.Equal(Height, decoded.PixelHeight);

        var reference = await DecodeWithDjxlToPfmAsync(decoder, jxlPath);
        Assert.Equal(DecodedBitmapPixelFormat.Rgba16Float, reference.PixelFormat);
        AssertFloatClose(reference.RgbaPixels, decoded.RgbaPixels, tolerance: 0.002f);
    }

    // ------------------------------------------------------------------
    // HDR JPEG XL with an attached ICC profile
    // ------------------------------------------------------------------

    [JxlNativeFact]
    public async Task IccAttachedHdrJxlIsProbedFromIccAndConvertedToPqOutput()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;
        var decoder = NativeToolLocator.FindTool("djxl.exe")!;

        var iccPath = Path.Combine(_tempDirectory, "rec2100-pq.icc");
        await File.WriteAllBytesAsync(iccPath, TestIccProfile.BuildRec2100PqProfile());

        var ppmPath = Path.Combine(_tempDirectory, "icc16.ppm");
        WritePatchPpm16(ppmPath, index => LinearPatches()[index]);
        var jxlPath = Path.Combine(_tempDirectory, "icc16.jxl");
        await EncodeWithCjxlAsync(encoder, ppmPath, jxlPath, "-x", $"icc_pathname={iccPath}");

        // The image's original profile is an ICC profile, so libjxl's default
        // output color encoding for it is scene-linear sRGB. The probe must
        // report the image's real PQ / BT.2020 encoding (from the ICC profile),
        // not the decoder's default output.
        var probe = await JxlProbe.ProbeAsync(jxlPath);
        Assert.NotNull(probe);
        Assert.Contains("PQ", probe.TransferFunction, StringComparison.OrdinalIgnoreCase);
        Assert.True(probe.UsesBt2020Primaries);
        Assert.Equal(GainMapColorGamut.Bt2100, probe.ColorGamut);
        Assert.False(probe.UsesOriginalProfile);

        var decoded = JxlNativeDecoder.DecodeFile(
            jxlPath,
            DecodedBitmapTransfer.Pq,
            probe.ColorGamut,
            "icc test",
            preserveAlpha: false,
            CancellationToken.None);
        Assert.Equal(DecodedBitmapTransfer.Pq, decoded.Transfer);
        Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, decoded.PixelFormat);
        Assert.True(decoded.UsesBt2020Primaries);

        // The decoder must have been asked for PQ BT.2020 output. Without that
        // request the samples are scene-linear sRGB and the renderer's inverse
        // PQ EOTF makes the image almost black.
        var reference = await DecodeWithDjxlToPpm16Async(decoder, jxlPath, "RGB_D65_202_Rel_PeQ");
        AssertU16Close(reference.RgbaPixels, decoded.RgbaPixels, tolerance: 2);
    }

    [JxlNativeFact]
    public async Task LosslessIccHdrJxlKeepsOriginalProfileEncoding()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;
        var decoder = NativeToolLocator.FindTool("djxl.exe")!;

        var iccPath = Path.Combine(_tempDirectory, "rec2100-pq-lossless.icc");
        await File.WriteAllBytesAsync(iccPath, TestIccProfile.BuildRec2100PqProfile());

        var ppmPath = Path.Combine(_tempDirectory, "icc-lossless.ppm");
        WritePatchPpm16(ppmPath, index => PqPatches()[index]);
        var jxlPath = Path.Combine(_tempDirectory, "icc-lossless.jxl");
        await EncodeWithCjxlAsync(
            encoder,
            ppmPath,
            jxlPath,
            "-d", "0",
            "-x", $"icc_pathname={iccPath}");

        var probe = await JxlProbe.ProbeAsync(jxlPath);
        Assert.NotNull(probe);
        Assert.Contains("PQ", probe.TransferFunction, StringComparison.OrdinalIgnoreCase);
        Assert.True(probe.UsesBt2020Primaries);
        Assert.True(probe.UsesOriginalProfile);

        // A lossless (uses_original_profile) image is stored in its original ICC
        // profile's encoding: the decoder already outputs PQ code values and must
        // not be asked to convert them again.
        var decoded = JxlNativeDecoder.DecodeFile(
            jxlPath,
            DecodedBitmapTransfer.Pq,
            probe.ColorGamut,
            "icc lossless test",
            preserveAlpha: false,
            CancellationToken.None);
        Assert.Equal(DecodedBitmapTransfer.Pq, decoded.Transfer);
        Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, decoded.PixelFormat);

        var centerX = Width / 8;
        var centerY = Height / 8;
        var sampleOffset = ((centerY * Width) + centerX) * 8;
        var patch0Code = BinaryPrimitives.ReadUInt16LittleEndian(decoded.RgbaPixels.AsSpan(sampleOffset)) / 65535.0;
        Assert.True(
            Math.Abs(patch0Code - PqEncode(10.0)) < 0.001,
            $"lossless ICC decode changed the stored PQ code: {patch0Code:0.#####} vs {PqEncode(10.0):0.#####}.");

        var reference = await DecodeWithDjxlToPpm16Async(decoder, jxlPath);
        AssertU16Close(reference.RgbaPixels, decoded.RgbaPixels, tolerance: 2);
    }

    // ------------------------------------------------------------------
    // jhgm ISO 21496-1 gain maps (raw + Brotli-compressed boxes)
    // ------------------------------------------------------------------

    [JxlNativeFact]
    public async Task GainMapBoxIsReadFromRawAndBrotliCompressedJhgmBoxes()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;

        var baseCodestream = await EncodeSdrCodestreamAsync(encoder, "base");
        var gainCodestream = await EncodeSdrCodestreamAsync(encoder, "gain");
        var bundle = BuildGainMapBundle(BuildGainMapMetadata(), gainCodestream);

        var rawPath = Path.Combine(_tempDirectory, "raw-gainmap.jxl");
        await File.WriteAllBytesAsync(
            rawPath,
            BuildJxlContainer(baseCodestream, BuildBox("jhgm", bundle)));
        var brobPath = Path.Combine(_tempDirectory, "brob-gainmap.jxl");
        await File.WriteAllBytesAsync(
            brobPath,
            BuildJxlContainer(
                baseCodestream,
                BuildBox("brob", Encoding.ASCII.GetBytes("jhgm").Concat(BrotliCompress(bundle)).ToArray())));
        var plainPath = Path.Combine(_tempDirectory, "no-gainmap.jxl");
        await File.WriteAllBytesAsync(plainPath, BuildJxlContainer(baseCodestream, extraBox: null));

        Assert.Equal(bundle, JxlNativeDecoder.TryReadGainMapBox(rawPath));
        Assert.Equal(bundle, JxlNativeDecoder.TryReadGainMapBox(brobPath));
        Assert.Null(JxlNativeDecoder.TryReadGainMapBox(plainPath));

        foreach (var path in new[] { rawPath, brobPath })
        {
            var probe = await JxlProbe.ProbeAsync(path);
            Assert.NotNull(probe);
            Assert.True(probe.HasGainMapBox);
            Assert.True(probe.IsHdrCandidate);
        }

        var plainProbe = await JxlProbe.ProbeAsync(plainPath);
        Assert.NotNull(plainProbe);
        Assert.False(plainProbe.HasGainMapBox);
    }

    [JxlNativeFact]
    public async Task GainMapRenderInputsAreDecodedFromRawAndBrobBundles()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;

        var baseCodestream = await EncodeSdrCodestreamAsync(encoder, "base");
        var gainCodestream = await EncodeSdrCodestreamAsync(encoder, "gain");
        var metadata = BuildGainMapMetadata();

        var cases = new (string Name, byte[] Bundle)[]
        {
            ("raw", BuildGainMapBundle(metadata, gainCodestream)),
            (
                "brob",
                BuildGainMapBundle(metadata, gainCodestream)),
        };
        foreach (var (name, bundle) in cases)
        {
            var path = Path.Combine(_tempDirectory, $"{name}-render.jxl");
            var box = name == "brob"
                ? BuildBox("brob", Encoding.ASCII.GetBytes("jhgm").Concat(BrotliCompress(bundle)).ToArray())
                : BuildBox("jhgm", bundle);
            await File.WriteAllBytesAsync(path, BuildJxlContainer(baseCodestream, box));

            var probe = await JxlProbe.ProbeAsync(path);
            Assert.NotNull(probe);
            Assert.True(probe.HasGainMapBox);
            var document = CreateDocument(path, probe);
            var inputs = await JxlGainMapDecoder.DecodeRenderInputsAsync(document);

            Assert.Equal(DecodedBitmapTransfer.Sdr, inputs.Primary.Transfer);
            Assert.Equal(DecodedBitmapPixelFormat.Rgba8Unorm, inputs.Primary.PixelFormat);
            Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, inputs.GainMap.PixelFormat);
            Assert.Equal(Width, inputs.Primary.PixelWidth);
            Assert.Equal(Height, inputs.Primary.PixelHeight);
            Assert.Equal(new Vector4(1.0f, 1.0f, 1.0f, 0.0f), inputs.Constants.GainMapMax);
            Assert.Equal(new Vector4(0.0f, 2.0f, 0.0f, 0.0f), inputs.Constants.HdrCapacity);
            Assert.Equal(1.0f, inputs.Constants.GainMapControl.X);

            var metadataRead = JxlGainMapDecoder.TryReadMetadata(path);
            Assert.NotNull(metadataRead);
            Assert.Equal(2.0f, metadataRead.AlternateHdrHeadroom);
            Assert.Equal(0.0f, metadataRead.BaseHdrHeadroom);
            Assert.False(metadataRead.UseBaseColorSpace);
        }
    }

    [JxlNativeFact]
    public async Task GainMapBundleWithAlternateColorEncodingIsSkippedInsteadOfRejected()
    {
        Assert.Null(MissingNativeDependency());
        var encoder = NativeToolLocator.FindTool("cjxl.exe")!;

        var baseCodestream = await EncodeSdrCodestreamAsync(encoder, "base");
        var gainCodestream = await EncodeSdrCodestreamAsync(encoder, "gain");
        // A packed JPEG XL alternate ColorEncoding bundle (real files may carry
        // one; the app treats it as optional metadata and must not reject it).
        var packedColorEncoding = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        var bundle = BuildGainMapBundle(BuildGainMapMetadata(), gainCodestream, packedColorEncoding);
        var path = Path.Combine(_tempDirectory, "encoded-gainmap.jxl");
        await File.WriteAllBytesAsync(path, BuildJxlContainer(baseCodestream, BuildBox("jhgm", bundle)));

        Assert.Equal(bundle, JxlNativeDecoder.TryReadGainMapBox(path));
        var probe = await JxlProbe.ProbeAsync(path);
        Assert.NotNull(probe);
        Assert.True(probe.HasGainMapBox);

        var inputs = await JxlGainMapDecoder.DecodeRenderInputsAsync(CreateDocument(path, probe));
        Assert.Equal(new Vector4(0.0f, 2.0f, 0.0f, 0.0f), inputs.Constants.HdrCapacity);
        // No alternate ICC/encoding is decoded, so the reconstruction falls back
        // to the base color space flag rather than failing.
        Assert.Equal(1.0f, inputs.Constants.SourceEncoding.W);
    }

    // ------------------------------------------------------------------
    // Fixture helpers
    // ------------------------------------------------------------------

    private static string? MissingNativeDependency() =>
        !OperatingSystem.IsWindows() ? "Requires Windows."
        : NativeToolLocator.FindFirstTool("libjxl.dll", "jxl.dll") is null
            ? "Requires the bundled libjxl DLL."
            : NativeToolLocator.FindTool("cjxl.exe") is null || NativeToolLocator.FindTool("djxl.exe") is null
                ? "Requires the bundled cjxl.exe and djxl.exe."
                : null;

    private static HdrImageDocument CreateDocument(string path, JxlProbeResult probe) =>
        new(
            path,
            Path.GetFileName(path),
            new ImageFormatDescriptor("JPEG XL gain map", HdrImageKind.GainMap, "test", "test", "test", "test"),
            JxlProbe: probe);

    private static (double R, double G, double B)[] PqPatches() =>
    [
        PqGray(10.0),
        PqGray(100.0),
        PqGray(1000.0),
        PqGray(4000.0),
        PqPrimary(0, 100.0),
        PqPrimary(1, 100.0),
        PqPrimary(2, 100.0),
        PqGray(203.0),
    ];

    private static (double R, double G, double B)[] HlgPatches() =>
    [
        HlgGray(10.0),
        HlgGray(100.0),
        HlgGray(1000.0),
        HlgGray(1000.0),
        HlgPrimary(0, 100.0),
        HlgPrimary(1, 100.0),
        HlgPrimary(2, 100.0),
        HlgGray(203.0),
    ];

    private static (double R, double G, double B)[] LinearPatches() =>
    [
        (0.02, 0.02, 0.02),
        (0.10, 0.10, 0.10),
        (0.45, 0.45, 0.45),
        (0.90, 0.90, 0.90),
        (0.80, 0.05, 0.10),
        (0.05, 0.70, 0.10),
        (0.10, 0.10, 0.90),
        (0.30, 0.45, 0.60),
    ];

    private static readonly double[] LuminanceGains = [0.2627002120112671, 0.6779980715188708, 0.05930171646986196];

    private static (double R, double G, double B) PqGray(double nits)
    {
        var code = PqEncode(nits);
        return (code, code, code);
    }

    private static (double R, double G, double B) PqPrimary(int channel, double nits)
    {
        var rgb = new double[3];
        rgb[channel] = PqEncode(nits / LuminanceGains[channel]);
        return (rgb[0], rgb[1], rgb[2]);
    }

    private static (double R, double G, double B) HlgGray(double nits)
    {
        var code = HlgEncode(nits / 1000.0);
        return (code, code, code);
    }

    private static (double R, double G, double B) HlgPrimary(int channel, double nits)
    {
        var rgb = new double[3];
        rgb[channel] = HlgEncode(nits / LuminanceGains[channel] / 1000.0);
        return (rgb[0], rgb[1], rgb[2]);
    }

    private static double PqEncode(double nits)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = (2523.0 / 4096.0) * 128.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = (2413.0 / 4096.0) * 32.0;
        const double c3 = (2392.0 / 4096.0) * 32.0;
        var y = Math.Pow(Math.Max(nits, 0.0) / 10000.0, m1);
        return Math.Pow((c1 + (c2 * y)) / (1.0 + (c3 * y)), m2);
    }

    private static double HlgEncode(double sceneLinear)
    {
        const double a = 0.17883277;
        const double b = 1.0 - (4.0 * a);
        var c = 0.5 - (a * Math.Log(4.0 * a));
        var e = Math.Clamp(sceneLinear, 0.0, 1.0);
        return e <= 1.0 / 12.0 ? Math.Sqrt(3.0 * e) : (a * Math.Log((12.0 * e) - b)) + c;
    }

    private static byte[] BuildGainMapMetadata()
    {
        // ISO 21496-1 bare payload as stored in a jhgm bundle:
        // min_ver u16 | writer_ver u16 | flags u8 | fractions.
        var data = new byte[61];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, 0); // min version
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0); // writer version
        span[4] = 0; // flags: single channel, alternate does not use base color space
        WriteFraction(span[5..], 0, 1); // base headroom
        WriteFraction(span[13..], 2, 1); // alternate headroom (2 stops)
        WriteSignedFraction(span[21..], 0, 1); // gain map min
        WriteSignedFraction(span[29..], 1000, 1000); // gain map max
        WriteFraction(span[37..], 1000, 1000); // gamma
        WriteSignedFraction(span[45..], 1, 64); // SDR offset
        WriteSignedFraction(span[53..], 1, 64); // HDR offset
        return data;
    }

    private static void WriteFraction(Span<byte> destination, uint numerator, uint denominator)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, numerator);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], denominator);
    }

    private static void WriteSignedFraction(Span<byte> destination, int numerator, uint denominator)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination, numerator);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], denominator);
    }

    private static byte[] BuildGainMapBundle(byte[] metadata, byte[] gainMapCodestream, byte[]? packedColorEncoding = null)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(0); // jhgm bundle version
        WriteUInt16BigEndian(stream, (ushort)metadata.Length);
        stream.Write(metadata);
        stream.WriteByte((byte)(packedColorEncoding?.Length ?? 0));
        if (packedColorEncoding is not null)
        {
            stream.Write(packedColorEncoding);
        }

        WriteUInt32BigEndian(stream, 0); // alternate compressed ICC size
        stream.Write(gainMapCodestream);
        return stream.ToArray();
    }

    private static byte[] BuildBox(string type, byte[] payload)
    {
        using var stream = new MemoryStream();
        WriteUInt32BigEndian(stream, (uint)(8 + payload.Length));
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload);
        return stream.ToArray();
    }

    private static byte[] BuildJxlContainer(byte[] baseCodestream, byte[]? extraBox)
    {
        using var stream = new MemoryStream();
        // Signature box.
        WriteUInt32BigEndian(stream, 12);
        stream.Write("JXL "u8);
        stream.Write([0x0D, 0x0A, 0x87, 0x0A]);
        // ftyp box.
        WriteUInt32BigEndian(stream, 20);
        stream.Write("ftyp"u8);
        stream.Write("jxl "u8);
        WriteUInt32BigEndian(stream, 0);
        stream.Write("jxl "u8);
        stream.Write(BuildBox("jxlc", baseCodestream));
        if (extraBox is not null)
        {
            stream.Write(extraBox);
        }

        return stream.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(data);
        }

        return output.ToArray();
    }

    private static void WriteUInt16BigEndian(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WritePatchPpm16(string path, Func<int, (double R, double G, double B)> patchColor)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        var header = Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n65535\n");
        stream.Write(header);
        Span<byte> sample = stackalloc byte[6];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (r, g, b) = patchColor(PatchIndex(x, y));
                WriteSample(sample, 0, r);
                WriteSample(sample, 2, g);
                WriteSample(sample, 4, b);
                stream.Write(sample);
            }
        }
    }

    private static void WritePatchPpm8(string path, Func<int, (double R, double G, double B)> patchColor)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        stream.Write(Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n"));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (r, g, b) = patchColor(PatchIndex(x, y));
                stream.WriteByte((byte)Math.Round(Math.Clamp(r, 0.0, 1.0) * 255.0));
                stream.WriteByte((byte)Math.Round(Math.Clamp(g, 0.0, 1.0) * 255.0));
                stream.WriteByte((byte)Math.Round(Math.Clamp(b, 0.0, 1.0) * 255.0));
            }
        }
    }

    private static void WriteSample(Span<byte> destination, int offset, double value)
    {
        var sample = (ushort)Math.Round(Math.Clamp(value, 0.0, 1.0) * 65535.0);
        BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], sample);
    }

    private static int PatchIndex(int x, int y) =>
        (y < Height / 2 ? 0 : 4) + Math.Min(x * 4 / Width, 3);

    private async Task<byte[]> EncodeSdrCodestreamAsync(string cjxl, string name)
    {
        var ppmPath = Path.Combine(_tempDirectory, name + ".ppm");
        WritePatchPpm8(ppmPath, index => LinearPatches()[index]);
        var jxlPath = Path.Combine(_tempDirectory, name + ".jxl");
        await EncodeWithCjxlAsync(cjxl, ppmPath, jxlPath, "--container=0");
        var codestream = await File.ReadAllBytesAsync(jxlPath);
        Assert.Equal(0xFF, codestream[0]);
        Assert.Equal(0x0A, codestream[1]);
        return codestream;
    }

    private static async Task EncodeWithCjxlAsync(string cjxl, string source, string destination, params string[] extraArguments)
    {
        using var process = NativeProcessRunner.Create(cjxl);
        process.StartInfo.ArgumentList.Add(source);
        process.StartInfo.ArgumentList.Add(destination);
        process.StartInfo.ArgumentList.Add("--quiet");
        foreach (var argument in extraArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        await NativeProcessRunner.RunAsync(process, "libjxl cjxl fixture", CancellationToken.None);
    }

    private async Task<DecodedBitmap> DecodeWithDjxlToPpm16Async(string djxl, string jxlPath, string? colorSpace = null)
    {
        var ppmPath = Path.Combine(_tempDirectory, "reference-" + Guid.NewGuid().ToString("N") + ".ppm");
        try
        {
            using var process = NativeProcessRunner.Create(djxl);
            process.StartInfo.ArgumentList.Add(jxlPath);
            process.StartInfo.ArgumentList.Add(ppmPath);
            process.StartInfo.ArgumentList.Add("--quiet");
            process.StartInfo.ArgumentList.Add("--output_format");
            process.StartInfo.ArgumentList.Add("ppm");
            process.StartInfo.ArgumentList.Add("--bits_per_sample");
            process.StartInfo.ArgumentList.Add("16");
            if (colorSpace is not null)
            {
                process.StartInfo.ArgumentList.Add($"--color_space={colorSpace}");
            }

            await NativeProcessRunner.RunAsync(process, "libjxl djxl reference", CancellationToken.None);
            return PortableImageReader.ReadPpmAsRgba16(
                ppmPath,
                "djxl reference",
                DecodedBitmapTransfer.Pq,
                usesBt2020Primaries: true,
                CancellationToken.None);
        }
        finally
        {
            TryDelete(ppmPath);
        }
    }

    private async Task<DecodedBitmap> DecodeWithDjxlToPfmAsync(string djxl, string jxlPath)
    {
        var pfmPath = Path.Combine(_tempDirectory, "reference-" + Guid.NewGuid().ToString("N") + ".pfm");
        try
        {
            using var process = NativeProcessRunner.Create(djxl);
            process.StartInfo.ArgumentList.Add(jxlPath);
            process.StartInfo.ArgumentList.Add(pfmPath);
            process.StartInfo.ArgumentList.Add("--quiet");
            process.StartInfo.ArgumentList.Add("--output_format");
            process.StartInfo.ArgumentList.Add("pfm");
            process.StartInfo.ArgumentList.Add("--color_space=RGB_D65_SRG_Rel_Lin");
            await NativeProcessRunner.RunAsync(process, "libjxl djxl reference", CancellationToken.None);
            return PortableImageReader.ReadPfmAsLinearScRgb(
                pfmPath,
                "djxl reference",
                CancellationToken.None);
        }
        finally
        {
            TryDelete(pfmPath);
        }
    }

    private static void AssertU16Close(byte[] expected, byte[] actual, int tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        var maxDelta = 0;
        var mismatches = 0;
        for (var i = 0; i + 1 < expected.Length; i += 2)
        {
            var e = BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i));
            var a = BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(i));
            var delta = Math.Abs(e - a);
            maxDelta = Math.Max(maxDelta, delta);
            if (delta > tolerance)
            {
                mismatches++;
            }
        }

        Assert.True(
            mismatches == 0,
            $"decoded pixels differ from the djxl reference: {mismatches} samples over tolerance {tolerance}, max delta {maxDelta} u16.");
    }

    private static void AssertFloatClose(byte[] expected, byte[] actual, float tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        var maxDelta = 0.0f;
        for (var i = 0; i + 1 < expected.Length; i += 2)
        {
            var e = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i)));
            var a = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(i)));
            maxDelta = Math.Max(maxDelta, Math.Abs(e - a));
        }

        Assert.True(
            maxDelta <= tolerance,
            $"decoded float pixels differ from the djxl reference: max delta {maxDelta} > {tolerance}.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Builds a minimal ICC v2 profile that carries Rec.2100/BT.2020 primaries
    /// plus a PQ-labelled description and a sampled (non power law) tone curve.
    /// The custom curve keeps libjxl/cjxl from normalizing the image profile to
    /// a structured color encoding, so the fixture really exercises the
    /// ICC-original-profile decode path.
    /// </summary>
    private static class TestIccProfile
    {
        public static byte[] BuildRec2100PqProfile()
        {
            var description = "Rec.2100 PQ BT.2020 (SMPTE ST 2084)";
            var tagData = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["desc"] = BuildTextDescription(description),
                ["wtpt"] = BuildXyzTag(0.9642, 1.0, 0.8249),
                ["rXYZ"] = BuildXyzTag(0.673459, 0.279033, -0.001931),
                ["gXYZ"] = BuildXyzTag(0.165661, 0.675338, 0.029979),
                ["bXYZ"] = BuildXyzTag(0.125100, 0.045629, 0.797162),
                ["rTRC"] = BuildCurveTag(),
                ["gTRC"] = BuildCurveTag(),
                ["bTRC"] = BuildCurveTag(),
            };

            const int headerSize = 128;
            var tagTableSize = 4 + (tagData.Count * 12);
            var offset = headerSize + tagTableSize;
            var entries = new byte[tagTableSize];
            BinaryPrimitives.WriteUInt32BigEndian(entries, (uint)tagData.Count);
            var body = new MemoryStream();
            var entryIndex = 4;
            foreach (var (signature, data) in tagData)
            {
                Encoding.ASCII.GetBytes(signature).CopyTo(entries, entryIndex);
                BinaryPrimitives.WriteUInt32BigEndian(entries.AsSpan(entryIndex + 4), (uint)offset);
                BinaryPrimitives.WriteUInt32BigEndian(entries.AsSpan(entryIndex + 8), (uint)data.Length);
                entryIndex += 12;
                body.Write(data);
                var padding = (4 - (data.Length % 4)) % 4;
                for (var i = 0; i < padding; i++)
                {
                    body.WriteByte(0);
                }

                offset += data.Length + padding;
            }

            var profile = new byte[headerSize + tagTableSize + body.Length];
            BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
            Encoding.ASCII.GetBytes("lcms").CopyTo(profile, 4);
            BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(8), 0x02100000);
            Encoding.ASCII.GetBytes("mntr").CopyTo(profile, 12);
            Encoding.ASCII.GetBytes("RGB ").CopyTo(profile, 16);
            Encoding.ASCII.GetBytes("XYZ ").CopyTo(profile, 20);
            WriteDateTime(profile, 24);
            Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);
            Encoding.ASCII.GetBytes("MSFT").CopyTo(profile, 40);
            WriteS15Fixed16(profile, 68, 0.9642);
            WriteS15Fixed16(profile, 72, 1.0);
            WriteS15Fixed16(profile, 76, 0.8249);
            entries.CopyTo(profile, headerSize);
            body.ToArray().CopyTo(profile, headerSize + tagTableSize);
            return profile;
        }

        private static void WriteDateTime(byte[] profile, int offset)
        {
            BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(offset), 2026);
            BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(offset + 2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(offset + 4), 1);
            BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(offset + 6), 0);
            BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(offset + 8), 0);
            BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(offset + 10), 0);
        }

        private static void WriteS15Fixed16(byte[] data, int offset, double value) =>
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), (int)Math.Round(value * 65536.0));

        private static byte[] BuildXyzTag(double x, double y, double z)
        {
            var data = new byte[20];
            Encoding.ASCII.GetBytes("XYZ ").CopyTo(data, 0);
            WriteS15Fixed16(data, 8, x);
            WriteS15Fixed16(data, 12, y);
            WriteS15Fixed16(data, 16, z);
            return data;
        }

        private static byte[] BuildCurveTag()
        {
            const int length = 256;
            var data = new byte[12 + (length * 2)];
            Encoding.ASCII.GetBytes("curv").CopyTo(data, 0);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), length);
            for (var i = 0; i < length; i++)
            {
                var x = i / (length - 1.0);
                // Deliberately not a pure power law so the profile stays an ICC
                // profile in the encoded JPEG XL instead of being normalized.
                var value = Math.Pow(x, 2.4) + (0.0007 * (1.0 - x));
                var sample = (ushort)Math.Round(Math.Clamp(value, 0.0, 1.0) * 65535.0);
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + (i * 2)), sample);
            }

            return data;
        }

        private static byte[] BuildTextDescription(string text)
        {
            var ascii = Encoding.ASCII.GetBytes(text + "\0");
            var data = new byte[12 + ascii.Length + 4 + 4 + 2 + 1];
            Encoding.ASCII.GetBytes("desc").CopyTo(data, 0);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)ascii.Length);
            ascii.CopyTo(data, 12);
            return data;
        }
    }
}
