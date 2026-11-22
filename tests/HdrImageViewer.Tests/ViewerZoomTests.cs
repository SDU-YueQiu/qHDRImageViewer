using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ViewerZoomTests
{
    [Fact]
    public void WheelNotch_ZoomsByWindowsPhotosStep()
    {
        Assert.Equal(1.1, ViewerZoom.CalculateWheelFactor(ViewerZoom.WheelDeltaPerNotch), 12);
    }

    [Fact]
    public void WheelNotches_CompoundMultiplicatively()
    {
        var twoNotches = ViewerZoom.CalculateWheelFactor(ViewerZoom.WheelDeltaPerNotch * 2);
        Assert.Equal(1.21, twoNotches, 12);
    }

    [Fact]
    public void WheelDown_InvertsTheUpwardStep()
    {
        var down = ViewerZoom.CalculateWheelFactor(-ViewerZoom.WheelDeltaPerNotch);
        Assert.Equal(ViewerZoom.InvertedStepFactor, down, 12);
        Assert.Equal(1.0, down * ViewerZoom.StepFactor, 12);
    }

    [Fact]
    public void PartialWheelDelta_ScalesExponentially()
    {
        var halfNotch = ViewerZoom.CalculateWheelFactor(ViewerZoom.WheelDeltaPerNotch / 2);
        Assert.Equal(Math.Sqrt(ViewerZoom.StepFactor), halfNotch, 12);
        Assert.Equal(1.0, ViewerZoom.CalculateWheelFactor(0), 12);
    }
}
