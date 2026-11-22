using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

/// <summary>
/// Pins the app-level RTX VSR mode + tier contract to the native NVIDIA VFX SDK
/// 1.3 QualityLevel table documented for the VideoSuperRes effect:
///   Standard    1-4    (VSR_LOW..VSR_ULTRA, upscaling)
///   Denoise     8-11   (DENOISE_*, output size == input size)
///   Deblur      12-15  (DEBLUR_*, output size == input size)
///   HighBitrate 16-19  (HIGHBITRATE_*, upscaling)
/// </summary>
public sealed class RtxVsrMappingTests
{
    private static readonly (RtxVsrMode Mode, RtxVsrQualityPreset Preset, int Level)[] AllMappings =
    [
        (RtxVsrMode.Standard, RtxVsrQualityPreset.Low, 1),
        (RtxVsrMode.Standard, RtxVsrQualityPreset.Medium, 2),
        (RtxVsrMode.Standard, RtxVsrQualityPreset.High, 3),
        (RtxVsrMode.Standard, RtxVsrQualityPreset.Ultra, 4),
        (RtxVsrMode.Denoise, RtxVsrQualityPreset.Low, 8),
        (RtxVsrMode.Denoise, RtxVsrQualityPreset.Medium, 9),
        (RtxVsrMode.Denoise, RtxVsrQualityPreset.High, 10),
        (RtxVsrMode.Denoise, RtxVsrQualityPreset.Ultra, 11),
        (RtxVsrMode.Deblur, RtxVsrQualityPreset.Low, 12),
        (RtxVsrMode.Deblur, RtxVsrQualityPreset.Medium, 13),
        (RtxVsrMode.Deblur, RtxVsrQualityPreset.High, 14),
        (RtxVsrMode.Deblur, RtxVsrQualityPreset.Ultra, 15),
        (RtxVsrMode.HighBitrate, RtxVsrQualityPreset.Low, 16),
        (RtxVsrMode.HighBitrate, RtxVsrQualityPreset.Medium, 17),
        (RtxVsrMode.HighBitrate, RtxVsrQualityPreset.High, 18),
        (RtxVsrMode.HighBitrate, RtxVsrQualityPreset.Ultra, 19),
    ];

    public static TheoryData<RtxVsrMode, RtxVsrQualityPreset, int> Mappings
    {
        get
        {
            var data = new TheoryData<RtxVsrMode, RtxVsrQualityPreset, int>();
            foreach (var (mode, preset, level) in AllMappings)
            {
                data.Add(mode, preset, level);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Mappings))]
    public void QualityLevelFor_MapsEveryModeAndTierToTheDocumentedNativeLevel(
        RtxVsrMode mode,
        RtxVsrQualityPreset preset,
        int expectedLevel)
    {
        Assert.Equal(expectedLevel, RtxVsrEngine.QualityLevelFor(mode, preset));
    }

    [Fact]
    public void QualityLevelFor_CoversAllFourTiersOfAllFourModes()
    {
        var modes = new[] { RtxVsrMode.Standard, RtxVsrMode.Denoise, RtxVsrMode.Deblur, RtxVsrMode.HighBitrate };
        var presets = new[]
        {
            RtxVsrQualityPreset.Low,
            RtxVsrQualityPreset.Medium,
            RtxVsrQualityPreset.High,
            RtxVsrQualityPreset.Ultra,
        };

        var levels = new HashSet<int>();
        foreach (var mode in modes)
        {
            foreach (var preset in presets)
            {
                levels.Add(RtxVsrEngine.QualityLevelFor(mode, preset));
            }
        }

        Assert.Equal(16, levels.Count);
        Assert.Equal(
            Enumerable.Range(1, 4).Concat(Enumerable.Range(8, 12)),
            levels.Order());
    }

    [Fact]
    public void DefaultPhotoModeMapsToThePreviousDefaultLevel()
    {
        // The old raw default was QualityLevel 19 = HIGHBITRATE_ULTRA; the new
        // HighBitrate + Ultra default must preserve exactly that behaviour.
        Assert.Equal(
            RtxVsrEngine.DefaultQuality,
            RtxVsrEngine.QualityLevelFor(RtxVsrMode.HighBitrate, RtxVsrQualityPreset.Ultra));
        Assert.Equal(19, RtxVsrEngine.DefaultQuality);
    }

    [Theory]
    [InlineData(RtxVsrMode.Denoise, true)]
    [InlineData(RtxVsrMode.Deblur, true)]
    [InlineData(RtxVsrMode.Standard, false)]
    [InlineData(RtxVsrMode.HighBitrate, false)]
    public void IsSameResolutionMode_IdentifiesDenoiseAndDeblur(RtxVsrMode mode, bool expected)
    {
        Assert.Equal(expected, RtxVsrEngine.IsSameResolutionMode(mode));
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(16, false)]
    [InlineData(19, false)]
    [InlineData(21, false)]
    [InlineData(23, false)]
    public void IsSameResolutionMode_IntLevelHelperMatchesNativeTable(int level, bool expected)
    {
        Assert.Equal(expected, RtxVsrEngine.IsSameResolutionMode(level));
    }

    [Fact]
    public void QualityName_DescribesEveryMappedLevel()
    {
        foreach (var (mode, preset, expectedLevel) in AllMappings)
        {
            var name = RtxVsrEngine.QualityName(expectedLevel);
            Assert.DoesNotContain("LEVEL_", name, StringComparison.Ordinal);
            Assert.Equal(expectedLevel, RtxVsrEngine.QualityLevelFor(mode, preset));
        }

        Assert.Equal("HIGHBITRATE_ULTRA", RtxVsrEngine.QualityName(19));
        Assert.Equal("DENOISE_ULTRA", RtxVsrEngine.QualityName(11));
        Assert.Equal("DEBLUR_LOW", RtxVsrEngine.QualityName(12));
    }

    [Fact]
    public void ModesAndPresetsExposeTheDocumentedEnumContract()
    {
        Assert.Equal(4, Enum.GetValues<RtxVsrMode>().Length);
        Assert.Equal(4, Enum.GetValues<RtxVsrQualityPreset>().Length);
        Assert.Equal(0, (int)RtxVsrMode.Standard);
        Assert.Equal(1, (int)RtxVsrMode.Denoise);
        Assert.Equal(2, (int)RtxVsrMode.Deblur);
        Assert.Equal(3, (int)RtxVsrMode.HighBitrate);
        Assert.Equal(0, (int)RtxVsrQualityPreset.Low);
        Assert.Equal(3, (int)RtxVsrQualityPreset.Ultra);
    }
}
