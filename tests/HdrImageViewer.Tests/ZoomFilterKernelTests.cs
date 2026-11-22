using System.Runtime.InteropServices;
using HdrImageViewer.Rendering;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ZoomFilterKernelTests
{
    [Fact]
    public void GainMapShaderConstants_KeepsHlslCbufferLayoutInSync()
    {
        // ZoomFilter is the 16th float4 in the GainMapConstants cbuffer
        // (GainMap.hlsl): 15 original vectors followed by the filter mode.
        Assert.Equal(16 * 16, Marshal.SizeOf<GainMapShaderConstants>());
        Assert.Equal(15 * 16, Marshal.OffsetOf<GainMapShaderConstants>(nameof(GainMapShaderConstants.ZoomFilter)).ToInt32());
    }

    [Fact]
    public void CatmullRomWeights_PassThroughAtIntegerOffsets()
    {
        var atZero = ZoomFilterKernel.CatmullRomWeights(0.0f);
        Assert.Equal(0.0f, atZero.W0, 6);
        Assert.Equal(1.0f, atZero.W1, 6);
        Assert.Equal(0.0f, atZero.W2, 6);
        Assert.Equal(0.0f, atZero.W3, 6);

        var atOne = ZoomFilterKernel.CatmullRomWeights(1.0f);
        Assert.Equal(0.0f, atOne.W0, 6);
        Assert.Equal(0.0f, atOne.W1, 6);
        Assert.Equal(1.0f, atOne.W2, 6);
        Assert.Equal(0.0f, atOne.W3, 6);
    }

    [Fact]
    public void CatmullRomWeights_MatchHalfTexelReferenceValues()
    {
        var weights = ZoomFilterKernel.CatmullRomWeights(0.5f);
        Assert.Equal(-0.0625f, weights.W0, 6);
        Assert.Equal(0.5625f, weights.W1, 6);
        Assert.Equal(0.5625f, weights.W2, 6);
        Assert.Equal(-0.0625f, weights.W3, 6);
    }

    [Fact]
    public void CatmullRomWeights_SumToOneAndAreSymmetric()
    {
        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0f;
            var weights = ZoomFilterKernel.CatmullRomWeights(t);
            Assert.Equal(1.0f, weights.W0 + weights.W1 + weights.W2 + weights.W3, 5);

            var reversed = ZoomFilterKernel.CatmullRomWeights(1.0f - t);
            AssertClose(reversed.W3, weights.W0);
            AssertClose(reversed.W2, weights.W1);
            AssertClose(reversed.W1, weights.W2);
            AssertClose(reversed.W0, weights.W3);
        }
    }

    [Fact]
    public void LanczosWeight_IsInterpolatoryAndCompactlySupported()
    {
        Assert.Equal(1.0f, ZoomFilterKernel.LanczosWeight(0.0f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.LanczosWeight(1.0f), 5);
        Assert.Equal(0.0f, ZoomFilterKernel.LanczosWeight(-1.0f), 5);
        Assert.Equal(0.0f, ZoomFilterKernel.LanczosWeight(2.0f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.LanczosWeight(-2.5f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.LanczosWeight(3.0f, radius: 2.0f), 6);
    }

    [Fact]
    public void LanczosWeight_HasExpectedSignsAndReferenceValues()
    {
        // sinc(0.5) * sinc(0.25)
        Assert.Equal(0.573_159f, ZoomFilterKernel.LanczosWeight(0.5f), 5);
        // sinc(1.5) * sinc(0.75) is the first negative lobe.
        Assert.True(ZoomFilterKernel.LanczosWeight(1.5f) < 0.0f);
        AssertClose(-0.063684f, ZoomFilterKernel.LanczosWeight(1.5f));
    }

    [Fact]
    public void LanczosWeights_PassThroughAtIntegerOffsets()
    {
        var atZero = ZoomFilterKernel.LanczosWeights(0.0f);
        Assert.Equal(1.0f, atZero.W1, 5);
        Assert.Equal(0.0f, atZero.W0, 5);
        Assert.Equal(0.0f, atZero.W2, 5);
        Assert.Equal(0.0f, atZero.W3, 5);

        var atOne = ZoomFilterKernel.LanczosWeights(1.0f);
        Assert.Equal(1.0f, atOne.W2, 5);
        Assert.Equal(0.0f, atOne.W0, 5);
        Assert.Equal(0.0f, atOne.W1, 5);
        Assert.Equal(0.0f, atOne.W3, 5);
    }

    [Fact]
    public void LanczosWeights_AreNormalizedSymmetricAndRing()
    {
        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0f;
            var weights = ZoomFilterKernel.LanczosWeights(t);
            Assert.Equal(1.0f, weights.W0 + weights.W1 + weights.W2 + weights.W3, 5);

            var reversed = ZoomFilterKernel.LanczosWeights(1.0f - t);
            AssertClose(reversed.W3, weights.W0);
            AssertClose(reversed.W2, weights.W1);
            AssertClose(reversed.W1, weights.W2);
            AssertClose(reversed.W0, weights.W3);
        }

        var half = ZoomFilterKernel.LanczosWeights(0.5f);
        Assert.Equal(half.W3, half.W0, 6);
        Assert.Equal(half.W2, half.W1, 6);
        Assert.True(half.W0 < 0.0f);
        Assert.True(half.W1 > 0.0f);
        Assert.Equal(1.0f, half.W0 + half.W1 + half.W2 + half.W3, 5);
    }

    [Fact]
    public void LanczosWeights_SupportWiderRadius()
    {
        var weights = ZoomFilterKernel.LanczosWeights(0.5f, radius: 3.0f);
        Assert.Equal(1.0f, weights.W0 + weights.W1 + weights.W2 + weights.W3, 5);
        Assert.True(float.IsFinite(weights.W0));
        Assert.True(float.IsFinite(weights.W3));
    }

    [Fact]
    public void LanczosWeight_RejectsInvalidRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.LanczosWeight(0.5f, radius: 0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.LanczosWeights(0.5f, radius: -1.0f));
    }

    [Fact]
    public void BicubicAndLanczosDifferAtSameFraction()
    {
        var bicubic = ZoomFilterKernel.CatmullRomWeights(0.25f);
        var lanczos = ZoomFilterKernel.LanczosWeights(0.25f);
        Assert.NotEqual(bicubic.W0, lanczos.W0, 3);
        Assert.Equal(1.0f, lanczos.W0 + lanczos.W1 + lanczos.W2 + lanczos.W3, 5);
        Assert.True(lanczos.W0 < bicubic.W0);
    }

    [Fact]
    public void CubicBCWeights_MatchCatmullRomWhenB0C05()
    {
        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0f;
            var catmull = ZoomFilterKernel.CatmullRomWeights(t);
            var bc = ZoomFilterKernel.CubicBCWeights(t, 0.0f, 0.5f);
            AssertClose(catmull.W0, bc.W0);
            AssertClose(catmull.W1, bc.W1);
            AssertClose(catmull.W2, bc.W2);
            AssertClose(catmull.W3, bc.W3);
        }
    }

    [Fact]
    public void CubicBCWeight_MatchesKeyKernelAtHalfTexel()
    {
        // Catmull-Rom kernel value at distance 0.5 is 0.5625 and its first
        // negative lobe at distance 1.5 is -0.0625.
        Assert.Equal(0.5625f, ZoomFilterKernel.CubicBCWeight(0.5f, 0.0f, 0.5f), 6);
        Assert.Equal(-0.0625f, ZoomFilterKernel.CubicBCWeight(1.5f, 0.0f, 0.5f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.CubicBCWeight(2.0f, 0.0f, 0.5f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.CubicBCWeight(2.5f, 0.0f, 0.5f), 6);
        Assert.Equal(1.0f, ZoomFilterKernel.CubicBCWeight(0.0f, 0.0f, 0.5f), 6);
    }

    [Fact]
    public void MitchellWeights_PassThroughAtIntegerOffsetsAndSoftenTheCenter()
    {
        var atZero = ZoomFilterKernel.MitchellWeights(0.0f);
        // Mitchell is not interpolatory: the centre weight is 8/9 with 1/18
        // sidelobes, so it acts as a mild low-pass even at integer offsets.
        Assert.Equal(8.0f / 9.0f, atZero.W1, 5);
        Assert.Equal(1.0f / 18.0f, atZero.W0, 5);
        Assert.Equal(1.0f / 18.0f, atZero.W2, 5);
        Assert.Equal(0.0f, atZero.W3, 5);
        Assert.Equal(1.0f, atZero.W0 + atZero.W1 + atZero.W2 + atZero.W3, 5);
    }

    [Fact]
    public void MitchellWeights_HaveSmallerNegativeLobesThanCatmullRom()
    {
        var half = ZoomFilterKernel.MitchellWeights(0.5f);
        var catmull = ZoomFilterKernel.CatmullRomWeights(0.5f);
        Assert.Equal(-0.03472222f, half.W0, 5);
        Assert.Equal(0.53472222f, half.W1, 5);
        Assert.Equal(1.0f, half.W0 + half.W1 + half.W2 + half.W3, 5);
        Assert.True(MathF.Abs(half.W0) < MathF.Abs(catmull.W0));
    }

    [Fact]
    public void RobidouxWeights_UseTheImageMagickRobidouxConstants()
    {
        // B = 12/(19+9*sqrt(2)), C = 113/(58+216*sqrt(2)).
        Assert.Equal(ZoomFilterKernel.RobidouxB, 12.0 / (19.0 + (9.0 * Math.Sqrt(2.0))), 7);
        Assert.Equal(ZoomFilterKernel.RobidouxC, 113.0 / (58.0 + (216.0 * Math.Sqrt(2.0))), 7);

        var half = ZoomFilterKernel.RobidouxWeights(0.5f);
        Assert.Equal(-0.03098202f, half.W0, 5);
        Assert.Equal(0.53098202f, half.W1, 5);
        Assert.Equal(1.0f, half.W0 + half.W1 + half.W2 + half.W3, 5);

        var lobe = ZoomFilterKernel.RobidouxWeights(0.25f).W0;
        Assert.True(lobe < 0.0f);
        Assert.True(MathF.Abs(lobe) < MathF.Abs(ZoomFilterKernel.CatmullRomWeights(0.25f).W0));
    }

    [Fact]
    public void CubicBCWeights_AreNormalisedSymmetricAndFinite()
    {
        foreach (var (b, c) in new[]
        {
            (ZoomFilterKernel.MitchellB, ZoomFilterKernel.MitchellC),
            (ZoomFilterKernel.RobidouxB, ZoomFilterKernel.RobidouxC),
        })
        {
            for (var i = 0; i <= 20; i++)
            {
                var t = i / 20.0f;
                var weights = ZoomFilterKernel.CubicBCWeights(t, b, c);
                Assert.Equal(1.0f, weights.W0 + weights.W1 + weights.W2 + weights.W3, 5);
                Assert.True(float.IsFinite(weights.W0) && float.IsFinite(weights.W1)
                    && float.IsFinite(weights.W2) && float.IsFinite(weights.W3));

                var reversed = ZoomFilterKernel.CubicBCWeights(1.0f - t, b, c);
                AssertClose(reversed.W3, weights.W0);
                AssertClose(reversed.W2, weights.W1);
                AssertClose(reversed.W1, weights.W2);
                AssertClose(reversed.W0, weights.W3);
            }
        }
    }

    [Fact]
    public void BesselI0_MatchesReferenceValues()
    {
        Assert.Equal(1.0f, ZoomFilterKernel.BesselI0(0.0f), 6);
        Assert.Equal(1.2660659f, ZoomFilterKernel.BesselI0(1.0f), 5);
        Assert.Equal(2.2795853f, ZoomFilterKernel.BesselI0(2.0f), 5);
        Assert.Equal(4.8807926f, ZoomFilterKernel.BesselI0(3.0f), 4);
        Assert.Equal(11.301922f, ZoomFilterKernel.BesselI0(4.0f), 3);
        Assert.Equal(27.239872f, ZoomFilterKernel.BesselI0(5.0f), 2);
        Assert.Equal(ZoomFilterKernel.BesselI0(-2.0f), ZoomFilterKernel.BesselI0(2.0f), 6);
    }

    [Fact]
    public void KaiserWeight_IsSincTaperedByTheKaiserWindow()
    {
        // beta = 0 turns the Kaiser window into a rectangular window, so the
        // kernel degenerates to the truncated sinc.
        Assert.Equal(1.0f, ZoomFilterKernel.KaiserWeight(0.0f, 2.0f, 0.0f), 6);
        Assert.Equal(0.63661975f, ZoomFilterKernel.KaiserWeight(0.5f, 2.0f, 0.0f), 5);
        Assert.Equal(0.0f, ZoomFilterKernel.KaiserWeight(1.0f, 2.0f, 0.0f), 5);
        Assert.Equal(-0.21220659f, ZoomFilterKernel.KaiserWeight(1.5f, 2.0f, 0.0f), 5);

        // The default beta tapers the side lobes but keeps the same zero at
        // integer offsets; the first negative lobe is smaller than sinc's.
        Assert.Equal(-0.08229633f, ZoomFilterKernel.KaiserWeight(1.5f), 5);
        Assert.True(
            MathF.Abs(ZoomFilterKernel.KaiserWeight(1.5f))
            < MathF.Abs(ZoomFilterKernel.KaiserWeight(1.5f, 2.0f, 0.0f)));
        Assert.Equal(0.0f, ZoomFilterKernel.KaiserWeight(2.0f), 6);
    }

    [Fact]
    public void KaiserWeights_PassThroughAtIntegerOffsetsAndAreNormalised()
    {
        var atZero = ZoomFilterKernel.KaiserWeights(0.0f);
        Assert.Equal(0.0f, atZero.W0, 6);
        Assert.Equal(1.0f, atZero.W1, 6);
        Assert.Equal(0.0f, atZero.W2, 6);
        Assert.Equal(0.0f, atZero.W3, 6);

        var atHalf = ZoomFilterKernel.KaiserWeights(0.5f);
        Assert.Equal(-0.08267650f, atHalf.W0, 5);
        Assert.Equal(0.58267650f, atHalf.W1, 5);
        Assert.Equal(0.58267650f, atHalf.W2, 5);
        Assert.Equal(1.0f, atHalf.W0 + atHalf.W1 + atHalf.W2 + atHalf.W3, 5);

        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0f;
            var weights = ZoomFilterKernel.KaiserWeights(t);
            Assert.Equal(1.0f, weights.W0 + weights.W1 + weights.W2 + weights.W3, 5);
            var reversed = ZoomFilterKernel.KaiserWeights(1.0f - t);
            AssertClose(reversed.W3, weights.W0);
            AssertClose(reversed.W0, weights.W3);
        }
    }

    [Fact]
    public void KaiserWeight_RejectsInvalidParameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.KaiserWeight(0.5f, radius: 0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.KaiserWeight(0.5f, beta: -1.0f));
    }

    [Theory]
    [InlineData(1.0f, 0)]
    [InlineData(1.5f, 0)]
    [InlineData(1.999f, 0)]
    [InlineData(2.0f, 1)]
    [InlineData(3.0f, 1)]
    [InlineData(3.999f, 1)]
    [InlineData(4.0f, 2)]
    [InlineData(9.0f, 3)]
    public void AreaMipLevel_SelectsTheLevelWhoseTexelFitsTheFootprint(float texelsPerPixel, int expectedLevel)
    {
        Assert.Equal(expectedLevel, ZoomFilterKernel.AreaMipLevel(texelsPerPixel));
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.999f)]
    [InlineData(2.0f)]
    [InlineData(2.5f)]
    [InlineData(3.0f)]
    [InlineData(4.0f)]
    [InlineData(5.5f)]
    [InlineData(8.0f)]
    [InlineData(13.7f)]
    public void AreaEffectiveFootprint_MatchesTheOutputFootprint(float texelsPerPixel)
    {
        Assert.Equal(texelsPerPixel, ZoomFilterKernel.AreaEffectiveFootprint(texelsPerPixel), 4);
    }

    [Fact]
    public void AreaTapOffset_IsZeroAtExactMipBoundaries()
    {
        // At a power-of-two footprint the area filter degenerates to one
        // prefiltered mip texel (which is exactly the box average), so the two
        // bilinear taps coincide.
        Assert.Equal(0.0f, ZoomFilterKernel.AreaTapOffset(1.0f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.AreaTapOffset(2.0f), 6);
        Assert.Equal(0.0f, ZoomFilterKernel.AreaTapOffset(4.0f), 6);
        Assert.Equal(0.25f, ZoomFilterKernel.AreaTapOffset(3.0f), 6);
        Assert.Equal(0.25f, ZoomFilterKernel.AreaTapOffset(1.5f), 6);
        // 2.5 is the middle of mip level 1 (texel 2 <= 2.5 < 4).
        Assert.Equal(0.125f, ZoomFilterKernel.AreaTapOffset(2.5f), 6);
    }

    [Fact]
    public void AreaHelpers_RejectInvalidFootprints()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.AreaMipLevel(0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.AreaMipLevel(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomFilterKernel.AreaTapOffset(-1.0f));
    }

    private static void AssertClose(float expected, float actual)
    {
        Assert.True(
            MathF.Abs(expected - actual) <= 1e-5f,
            $"Expected {expected:R} but got {actual:R}.");
    }
}
