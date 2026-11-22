using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HdrImageViewer.Rendering;
using Xunit;

namespace HdrImageViewer.Tests;

/// <summary>
/// Pins the dedicated minification filter set: the enum values are wire format
/// (they are persisted in settings and passed to the HLSL downscale slot), so
/// both the migration mapping and the GainMap.hlsl mode constants must follow
/// them.
/// </summary>
public sealed class ZoomDownscaleModeTests
{
    private static readonly (ZoomDownscaleMode Mode, string HlslName)[] ShaderModeConstants =
    [
        (ZoomDownscaleMode.Nearest, "ZoomDownscaleNearest"),
        (ZoomDownscaleMode.Bilinear, "ZoomDownscaleBilinear"),
        (ZoomDownscaleMode.CatmullRom, "ZoomDownscaleCatmullRom"),
        (ZoomDownscaleMode.Mitchell, "ZoomDownscaleMitchell"),
        (ZoomDownscaleMode.Robidoux, "ZoomDownscaleRobidoux"),
        (ZoomDownscaleMode.Lanczos2, "ZoomDownscaleLanczos2"),
        (ZoomDownscaleMode.Kaiser2, "ZoomDownscaleKaiser2"),
        (ZoomDownscaleMode.Area, "ZoomDownscaleArea"),
    ];

    [Theory]
    [InlineData(ZoomDownscaleMode.Auto, 0)]
    [InlineData(ZoomDownscaleMode.Nearest, 1)]
    [InlineData(ZoomDownscaleMode.Bilinear, 2)]
    [InlineData(ZoomDownscaleMode.CatmullRom, 3)]
    [InlineData(ZoomDownscaleMode.Mitchell, 4)]
    [InlineData(ZoomDownscaleMode.Robidoux, 5)]
    [InlineData(ZoomDownscaleMode.Lanczos2, 6)]
    [InlineData(ZoomDownscaleMode.Kaiser2, 7)]
    [InlineData(ZoomDownscaleMode.Area, 8)]
    public void EnumValues_AreStableShaderCodes(ZoomDownscaleMode mode, int expected)
    {
        Assert.Equal(expected, (int)mode);
        Assert.Equal(expected, ZoomDownscaleModes.ToShaderCode(mode));
        Assert.InRange(
            ZoomDownscaleModes.ToShaderCode(mode),
            ZoomDownscaleModes.MinShaderCode,
            ZoomDownscaleModes.MaxShaderCode);
    }

    [Fact]
    public void EnumValues_AreContiguousAndUnique()
    {
        var values = new HashSet<int>();
        foreach (var mode in Enum.GetValues<ZoomDownscaleMode>())
        {
            Assert.True(values.Add((int)mode), $"Duplicate shader code {(int)mode}.");
            Assert.Equal(ZoomDownscaleModes.ToShaderCode(mode), (int)mode);
        }

        for (var code = ZoomDownscaleModes.MinShaderCode; code <= ZoomDownscaleModes.MaxShaderCode; code++)
        {
            Assert.Contains(code, values);
        }
    }

    [Theory]
    [InlineData(ZoomInterpolationMode.Auto, ZoomDownscaleMode.Auto)]
    [InlineData(ZoomInterpolationMode.Nearest, ZoomDownscaleMode.Nearest)]
    [InlineData(ZoomInterpolationMode.Bilinear, ZoomDownscaleMode.Bilinear)]
    [InlineData(ZoomInterpolationMode.Bicubic, ZoomDownscaleMode.CatmullRom)]
    [InlineData(ZoomInterpolationMode.Lanczos, ZoomDownscaleMode.Lanczos2)]
    public void FromLegacy_MapsOldDownscaleValuesByFilterName(
        ZoomInterpolationMode legacy,
        ZoomDownscaleMode expected)
    {
        Assert.Equal(expected, ZoomDownscaleModes.FromLegacy(legacy));
    }

    [Fact]
    public void FromLegacy_RejectsUndefinedLegacyValues()
    {
        Assert.Equal(ZoomDownscaleMode.Auto, ZoomDownscaleModes.FromLegacy((ZoomInterpolationMode)99));
        Assert.Equal(ZoomDownscaleMode.Auto, ZoomDownscaleModes.FromLegacy((ZoomInterpolationMode)(-1)));
    }

    [Fact]
    public void Normalize_RepairsUndefinedValues()
    {
        Assert.Equal(ZoomDownscaleMode.Auto, ZoomDownscaleModes.Normalize((ZoomDownscaleMode)99));
        Assert.Equal(ZoomDownscaleMode.Auto, ZoomDownscaleModes.Normalize((ZoomDownscaleMode)(-1)));
        Assert.Equal(ZoomDownscaleMode.Area, ZoomDownscaleModes.Normalize(ZoomDownscaleMode.Area));
    }

    [Theory]
    [MemberData(nameof(ShaderModeConstantData))]
    public void HlslDownscaleModeConstants_MatchEnumValues(ZoomDownscaleMode mode, string hlslName)
    {
        var hlsl = ReadShaderSource();
        var pattern = new Regex(
            "static const float " + Regex.Escape(hlslName) + @" = (\d+)\.0f;",
            RegexOptions.CultureInvariant);
        var match = pattern.Match(hlsl);

        Assert.True(match.Success, $"GainMap.hlsl is missing '{hlslName} = {(int)mode}.0f'.");
        Assert.Equal((int)mode, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void HlslKernelTuningConstants_MatchCpuMirror()
    {
        var hlsl = ReadShaderSource();

        Assert.Contains("MitchellB = 0.33333333333333333f", hlsl, StringComparison.Ordinal);
        Assert.Contains("MitchellC = 0.33333333333333333f", hlsl, StringComparison.Ordinal);
        Assert.Contains("RobidouxB = 0.37821575509399867f", hlsl, StringComparison.Ordinal);
        Assert.Contains("RobidouxC = 0.31089212245300067f", hlsl, StringComparison.Ordinal);
        Assert.Contains("MinifyKaiserRadius = 2.0f", hlsl, StringComparison.Ordinal);
        Assert.Contains("MinifyKaiserBeta = 3.5f", hlsl, StringComparison.Ordinal);
        Assert.Contains("MinifyLanczosRadius = 2.0f", hlsl, StringComparison.Ordinal);

        Assert.Equal(ZoomFilterKernel.RobidouxB, 0.37821575509399867f);
        Assert.Equal(ZoomFilterKernel.RobidouxC, 0.31089212245300067f);
        Assert.Equal(ZoomFilterKernel.DefaultKaiserBeta, 3.5f);
        Assert.Equal(1.0f / 3.0f, ZoomFilterKernel.MitchellB);
        Assert.Equal(1.0f / 3.0f, ZoomFilterKernel.MitchellC);
    }

    [Fact]
    public void HlslDispatch_UsesDedicatedMinificationPath()
    {
        var hlsl = ReadShaderSource();

        // The explicit filter entry point must know the direction so the same
        // numeric value can mean Bicubic while magnifying and CatmullRom while
        // minifying (and Lanczos while magnifying vs Mitchell while minifying).
        Assert.Contains("bool minifying", hlsl, StringComparison.Ordinal);
        Assert.Contains("SampleTextureMinified(", hlsl, StringComparison.Ordinal);
        Assert.Contains("SampleTextureMinifiedKernel(", hlsl, StringComparison.Ordinal);
        Assert.Contains("SampleTextureMinifiedArea(", hlsl, StringComparison.Ordinal);
        Assert.Contains("SampleTextureFilteredExplicit(tex, uv, duvDx, duvDy, mode, !magnifying)", hlsl, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ShaderModeConstantData()
    {
        foreach (var entry in ShaderModeConstants)
        {
            yield return [entry.Mode, entry.HlslName];
        }
    }

    private static string ReadShaderSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "qHdrImageViewer.csproj")))
            {
                return File.ReadAllText(Path.Combine(directory.FullName, "Rendering", "Shaders", "GainMap.hlsl"));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing qHdrImageViewer.csproj");
    }
}
