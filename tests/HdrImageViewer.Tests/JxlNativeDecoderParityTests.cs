using System.Buffers.Binary;
using System.Runtime.InteropServices;
using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

/// <summary>
/// HDR color correctness gate for the in-process libjxl decoder: the pixels
/// produced by <see cref="JxlNativeDecoder"/> must match what djxl.exe emits when
/// asked for the exact output color encoding the renderer is told about, for
/// every HDR JPEG XL fixture under the repo's local test/ directory. The test
/// reports a skip when the local-only fixtures or bundled libjxl binaries are
/// absent (fresh checkouts / CI without the external/ toolchain).
/// </summary>
public sealed class JxlNativeDecoderParityTests
{
    public sealed class JxlFactAttribute : FactAttribute
    {
        public JxlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HDRVIEWER_REQUIRE_NATIVE_TESTS") != "1")
                Skip = MissingDependency();
        }
    }

    private static string? MissingDependency() =>
        !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Requires Windows." :
        FindJxlFixtures().Count == 0 || FindTool("djxl.exe") is null || NativeToolLocator.FindFirstTool("libjxl.dll", "jxl.dll") is null
            ? "Requires HDR JXL fixtures, djxl.exe and a libjxl DLL." : null;

    [JxlFact]
    public async Task InProcessDecodeMatchesDjxlPpm16ForHdrFixtures()
    {
        Assert.Null(MissingDependency());
        var fixtures = FindJxlFixtures();
        var djxl = FindTool("djxl.exe")!;

        foreach (var fixture in fixtures)
        {
            var probe = JxlNativeDecoder.TryProbe(fixture);
            Assert.NotNull(probe);
            if (!probe.IsHdrTransfer)
            {
                // SDR fixtures are compared by the SDR decode tests, not by this
                // HDR PPM16 gate.
                continue;
            }

            var transfer = DecodeTransferFromProbe(probe);
            if (transfer == DecodedBitmapTransfer.LinearSceneScRgb)
            {
                var expectedLinear = await DecodeWithDjxlToPfmAsync(djxl, fixture, probe.UsesOriginalProfile);
                var actualLinear = JxlNativeDecoder.Decode(
                    fixture,
                    DecodedBitmapTransfer.LinearSceneScRgb,
                    probe.UsesBt2020Primaries,
                    "parity test",
                    CancellationToken.None);
                Assert.Equal(expectedLinear.PixelWidth, actualLinear.PixelWidth);
                Assert.Equal(expectedLinear.PixelHeight, actualLinear.PixelHeight);
                Assert.Equal(DecodedBitmapPixelFormat.Rgba16Float, actualLinear.PixelFormat);
                Assert.Equal(expectedLinear.RgbaPixels.Length, actualLinear.RgbaPixels.Length);
                var floatMismatch = DescribeFloatMismatch(expectedLinear.RgbaPixels, actualLinear.RgbaPixels);
                Assert.True(floatMismatch is null, $"{Path.GetFileName(fixture)}: {floatMismatch}");
                continue;
            }

            // Images stored in their original profile (uses_original_profile)
            // already carry that encoding's code values, so djxl's default output
            // is the matching reference. XYB images are compared against the
            // explicit output color space the app requests from libjxl.
            var colorSpace = probe.UsesOriginalProfile
                ? null
                : transfer == DecodedBitmapTransfer.Hlg
                    ? "RGB_D65_202_Rel_HLG"
                    : "RGB_D65_202_Rel_PeQ";
            var (width, height, expectedPixels) = await DecodeWithDjxlToRgba16Async(djxl, fixture, colorSpace);
            var actual = JxlNativeDecoder.Decode(
                fixture,
                transfer,
                probe.UsesBt2020Primaries,
                "parity test",
                CancellationToken.None);

            Assert.Equal(width, actual.PixelWidth);
            Assert.Equal(height, actual.PixelHeight);
            Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, actual.PixelFormat);
            Assert.Equal(expectedPixels.Length, actual.RgbaPixels.Length);

            var mismatch = DescribeFirstMismatch(expectedPixels, actual.RgbaPixels, width, tolerance: 2);
            Assert.True(mismatch is null, $"{Path.GetFileName(fixture)}: {mismatch}");
        }
    }

    private static DecodedBitmapTransfer DecodeTransferFromProbe(JxlProbeResult probe)
    {
        return probe.TransferFunction.Contains("PQ", StringComparison.OrdinalIgnoreCase)
            ? DecodedBitmapTransfer.Pq
            : probe.TransferFunction.Contains("HLG", StringComparison.OrdinalIgnoreCase)
                ? DecodedBitmapTransfer.Hlg
                : probe.TransferFunction.Contains("linear", StringComparison.OrdinalIgnoreCase)
                    ? DecodedBitmapTransfer.LinearSceneScRgb
                    : probe.TransferFunction.Contains("709", StringComparison.OrdinalIgnoreCase)
                        ? DecodedBitmapTransfer.Bt709
                        : DecodedBitmapTransfer.Sdr;
    }

    private static string? DescribeFirstMismatch(byte[] expected, byte[] actual, int width, int tolerance)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return null;
        }

        var mismatchedSamples = 0;
        var maxDelta = 0;
        var firstIndex = -1;
        for (var i = 0; i + 1 < expected.Length; i += 2)
        {
            int e = expected[i] | (expected[i + 1] << 8);
            int a = actual[i] | (actual[i + 1] << 8);
            var delta = Math.Abs(e - a);
            maxDelta = Math.Max(maxDelta, delta);
            if (delta > tolerance)
            {
                mismatchedSamples++;
                if (firstIndex < 0)
                {
                    firstIndex = i;
                }
            }
        }

        if (mismatchedSamples == 0)
        {
            return null;
        }

        var pixel = firstIndex / 8;
        return $"pixels differ from the djxl output: {mismatchedSamples} uint16 samples over tolerance {tolerance} (max delta {maxDelta}), first at byte {firstIndex} (pixel {pixel % width},{pixel / width}).";
    }

    private static string? DescribeFloatMismatch(byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return null;
        }

        var maxDelta = 0.0f;
        for (var i = 0; i + 1 < expected.Length; i += 2)
        {
            var e = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i)));
            var a = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(i)));
            maxDelta = Math.Max(maxDelta, Math.Abs(e - a));
        }

        return maxDelta > 0.002f
            ? $"pixels differ from the djxl PFM output: max half-float delta {maxDelta}."
            : null;
    }

    private static async Task<(int Width, int Height, byte[] Rgba16Pixels)> DecodeWithDjxlToRgba16Async(
        string djxl,
        string jxlPath,
        string? colorSpace)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HdrImageViewerTests", "jxl-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var ppmPath = Path.Combine(tempDir, "decoded.ppm");
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
            await NativeProcessRunner.RunAsync(process, "libjxl djxl", CancellationToken.None);
            var bitmap = PortableImageReader.ReadPpmAsRgba16(
                ppmPath,
                decoderName: "djxl parity fixture",
                DecodedBitmapTransfer.Pq,
                usesBt2020Primaries: true,
                CancellationToken.None);
            return (bitmap.PixelWidth, bitmap.PixelHeight, bitmap.RgbaPixels);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static async Task<DecodedBitmap> DecodeWithDjxlToPfmAsync(string djxl, string jxlPath, bool usesOriginalProfile)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HdrImageViewerTests", "jxl-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pfmPath = Path.Combine(tempDir, "decoded.pfm");
        try
        {
            using var process = NativeProcessRunner.Create(djxl);
            process.StartInfo.ArgumentList.Add(jxlPath);
            process.StartInfo.ArgumentList.Add(pfmPath);
            process.StartInfo.ArgumentList.Add("--quiet");
            process.StartInfo.ArgumentList.Add("--output_format");
            process.StartInfo.ArgumentList.Add("pfm");
            if (!usesOriginalProfile)
            {
                process.StartInfo.ArgumentList.Add("--color_space=RGB_D65_SRG_Rel_Lin");
            }

            await NativeProcessRunner.RunAsync(process, "libjxl djxl", CancellationToken.None);
            return PortableImageReader.ReadPfmAsLinearScRgb(pfmPath, "djxl parity fixture", CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static List<string> FindJxlFixtures()
    {
        var fixtures = new List<string>();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var testDirectory = Path.Combine(directory.FullName, "test");
            if (Directory.Exists(testDirectory))
            {
                fixtures.AddRange(Directory.EnumerateFiles(testDirectory, "*.jxl", SearchOption.AllDirectories));
                if (fixtures.Count > 0)
                {
                    break;
                }
            }

            directory = directory.Parent;
        }

        return fixtures;
    }

    private static string? FindTool(string fileName)
    {
        return NativeToolLocator.FindTool(fileName);
    }
}
