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
    /// <param name="srgb">sRGB channels in 0..1.</param>
    /// <param name="sdrWhiteScale">
    /// Display SDR-white / 80 nits. On an HDR-active display the compositor maps
    /// scRGB 1.0 to 80 nits while the SDR UI is rendered at the user's SDR white
    /// level, so the clear colour has to be scaled by the same factor the tone
    /// mapper uses for scene white; otherwise the surround looks dark grey during
    /// the zoom animation. 1.0 keeps the plain linear value (SDR displays).
    /// </param>
    public static Vector4 ToScRgb(Vector3 srgb, float sdrWhiteScale = 1.0f)
    {
        var scale = float.IsFinite(sdrWhiteScale) ? Math.Clamp(sdrWhiteScale, 1.0f, 12.5f) : 1.0f;
        return new Vector4(
            ToLinear(srgb.X) * scale,
            ToLinear(srgb.Y) * scale,
            ToLinear(srgb.Z) * scale,
            1.0f);
    }

    public static float ToLinear(float channel)
    {
        var value = float.IsFinite(channel) ? Math.Clamp(channel, 0.0f, 1.0f) : 0.0f;
        return value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    public static float ToLinear(byte value) => ToLinear(value / 255.0f);
}
