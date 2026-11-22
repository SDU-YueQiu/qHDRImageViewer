using System.Numerics;
using HdrImageViewer.Rendering;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class GainMapShaderConstantsTests
{
    [Fact]
    public void CreateZoomFilter_PacksUpscaleInXAndDownscaleInY()
    {
        var packed = GainMapShaderConstants.CreateZoomFilter(
            ZoomInterpolationMode.Lanczos,
            ZoomDownscaleMode.Kaiser2);

        Assert.Equal(4.0f, packed.X);
        Assert.Equal(7.0f, packed.Y);
        Assert.Equal(0.0f, packed.Z);
        Assert.Equal(0.0f, packed.W);
    }

    [Fact]
    public void CreateZoomFilter_DefaultsToAutoForBothDirections()
    {
        var packed = GainMapShaderConstants.CreateZoomFilter(
            ZoomInterpolationMode.Auto,
            ZoomDownscaleMode.Auto);

        Assert.Equal(Vector4.Zero, packed);
    }

    [Theory]
    [InlineData(ZoomDownscaleMode.Auto, 0.0f)]
    [InlineData(ZoomDownscaleMode.Nearest, 1.0f)]
    [InlineData(ZoomDownscaleMode.Bilinear, 2.0f)]
    [InlineData(ZoomDownscaleMode.CatmullRom, 3.0f)]
    [InlineData(ZoomDownscaleMode.Mitchell, 4.0f)]
    [InlineData(ZoomDownscaleMode.Robidoux, 5.0f)]
    [InlineData(ZoomDownscaleMode.Lanczos2, 6.0f)]
    [InlineData(ZoomDownscaleMode.Kaiser2, 7.0f)]
    [InlineData(ZoomDownscaleMode.Area, 8.0f)]
    public void CreateZoomFilter_PacksEveryDownscaleShaderCodeInY(
        ZoomDownscaleMode mode,
        float expectedShaderCode)
    {
        var packed = GainMapShaderConstants.CreateZoomFilter(
            ZoomInterpolationMode.Auto,
            mode);

        Assert.Equal(expectedShaderCode, packed.Y);
        Assert.Equal(0.0f, packed.X);
    }

    [Fact]
    public void ZoomFilter_KeepsItsCbufferOffset()
    {
        // ZoomFilter is the 16th float4 in the GainMapConstants cbuffer. The
        // HLSL layout must stay in sync with this struct.
        Assert.Equal(16 * 16, System.Runtime.InteropServices.Marshal.SizeOf<GainMapShaderConstants>());
        Assert.Equal(
            15 * 16,
            System.Runtime.InteropServices.Marshal.OffsetOf<GainMapShaderConstants>(
                nameof(GainMapShaderConstants.ZoomFilter)).ToInt32());
    }
}
