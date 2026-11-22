using System.Numerics;

namespace HdrImageViewer.Rendering;

/// <summary>
/// Colour of the surface behind the image in the viewer. The swap chain clears
/// to the same colour, so the area around a contained image looks identical
/// while a zoom animation runs (viewport-sized swap chain) and after it settles
/// (image-sized host).
/// </summary>
public enum ViewerBackgroundMode
{
    /// <summary>Use the app's theme background colour (default).</summary>
    Window = 0,

    /// <summary>Pure black, e.g. for OLED or photo judging.</summary>
    Black = 1,

    /// <summary>Dark grey (#202020).</summary>
    DarkGray = 2,

    /// <summary>Medium grey (#606060), a neutral surround.</summary>
    MidGray = 3,

    /// <summary>Light grey (#D0D0D0).</summary>
    LightGray = 4,

    /// <summary>Pure white.</summary>
    White = 5,
}

/// <summary>
/// Resolves <see cref="ViewerBackgroundMode"/> to concrete colours: the sRGB
/// value used by the XAML backdrop and the linear scRGB value the D3D swap
/// chain has to clear to.
/// </summary>
public static class ViewerBackgroundPalette
{
    public static ViewerBackgroundMode Normalize(ViewerBackgroundMode mode) =>
        Enum.IsDefined(mode) ? mode : ViewerBackgroundMode.Window;

    /// <summary>
    /// Returns false for <see cref="ViewerBackgroundMode.Window"/>, which has to
    /// be resolved from the current theme by the caller.
    /// </summary>
    public static bool TryGetSrgb(ViewerBackgroundMode mode, out byte r, out byte g, out byte b)
    {
        switch (Normalize(mode))
        {
            case ViewerBackgroundMode.Black:
                (r, g, b) = (0, 0, 0);
                return true;
            case ViewerBackgroundMode.DarkGray:
                (r, g, b) = (32, 32, 32);
                return true;
            case ViewerBackgroundMode.MidGray:
                (r, g, b) = (96, 96, 96);
                return true;
            case ViewerBackgroundMode.LightGray:
                (r, g, b) = (208, 208, 208);
                return true;
            case ViewerBackgroundMode.White:
                (r, g, b) = (255, 255, 255);
                return true;
            default:
                (r, g, b) = (0, 0, 0);
                return false;
        }
    }

    /// <summary>
    /// Converts an sRGB colour to the linear scRGB value used by the swap chain
    /// (R16G16B16A16_FLOAT with RgbFullG10NoneP709). 1.0 is SDR white, so
    /// linearising keeps the clear colour visually equal to the XAML brush.
    /// </summary>
    public static Vector4 ToScRgb(byte r, byte g, byte b)
    {
        return new Vector4(ToLinear(r), ToLinear(g), ToLinear(b), 1.0f);
    }

    public static float ToLinear(byte value)
    {
        var channel = value / 255.0f;
        return channel <= 0.04045f
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }
}
