namespace HdrImageViewer.Presentation;

/// <summary>
/// Zoom step constants shared by the viewer input paths.
/// </summary>
/// <remarks>
/// The step factor matches Windows Photos: one mouse-wheel notch (or one
/// zoom-in/zoom-out command) changes the displayed zoom by about 10 %, which
/// avoids the oversized jumps of a 25 % step and the sluggish feel of a 6 %
/// step. Wheel deltas are normalized to 120 units per notch so high-resolution
/// touchpads still produce a proportional, continuous zoom.
/// </remarks>
internal static class ViewerZoom
{
    public const int WheelDeltaPerNotch = 120;

    public const double StepFactor = 1.1;

    public static double InvertedStepFactor => 1.0 / StepFactor;

    public static double CalculateWheelFactor(int wheelDelta)
    {
        return wheelDelta == 0
            ? 1.0
            : Math.Pow(StepFactor, wheelDelta / (double)WheelDeltaPerNotch);
    }
}
