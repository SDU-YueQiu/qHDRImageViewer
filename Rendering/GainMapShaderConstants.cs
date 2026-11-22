using System.Numerics;
using System.Runtime.InteropServices;

namespace HdrImageViewer.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct GainMapShaderConstants
{
    // Keep this layout matched with the HLSL GainMapConstants cbuffer.
    public Vector4 GainMapMin;
    public Vector4 GainMapMax;
    public Vector4 Gamma;
    public Vector4 OffsetSdr;
    public Vector4 OffsetHdr;
    // x: explicit gain-map weight, y: Apple HDRGainMap semantics flag, z: SDR base gamut.
    public Vector4 GainMapControl;
    // x: base transfer enum, y: BT.2020-to-scRGB/P709 conversion flag.
    public Vector4 SourceEncoding;
    public Vector4 Orientation;
    // x: SDR white scene scale, y: max scene clamp, z: target boost stops, w: full-frame scene limit.
    public Vector4 DisplayMapping;
    public Vector4 HdrCapacity;
    public Vector4 ImageLayout;
    // x: enabled, y: virtual target peak, z: tone-map source peak, w: content average.
    public Vector4 ToneMapInput;
    // x: physical target peak, y: tone mode, z: adaptive target peak, w: global/midtone scale.
    public Vector4 ToneMapOutput;
    // x: GainmapViewMode, y: HdrHeadroomMode, z: reference-white exposure, w: ColorGamutMappingMode.
    public Vector4 ViewModeParams;
    // x/y: visible horizontal interval; z: offscreen analysis/probe flag; w: reserved.
    public Vector4 ViewerTools;
    // x: ZoomInterpolationMode used when magnifying (zoom above 100 %),
    // y: ZoomDownscaleMode used when minifying (zoom below 100 %).
    // Magnification codes: 0 = Auto/legacy, 1 = Nearest, 2 = Bilinear,
    // 3 = Bicubic (Catmull-Rom), 4 = Lanczos-2.
    // Minification codes: 0 = Auto/legacy, 1 = Nearest, 2 = Bilinear,
    // 3 = CatmullRom, 4 = Mitchell, 5 = Robidoux, 6 = Lanczos2, 7 = Kaiser2,
    // 8 = Area. z/w: reserved for filter tuning.
    public Vector4 ZoomFilter;

    /// <summary>
    /// Packs the direction-specific zoom interpolation modes into the shader
    /// constant: x = upscale mode (display larger than source pixels), y =
    /// downscale/minification mode (display smaller than source pixels), z/w
    /// unused. The shader derives the active direction from the texture
    /// gradients, so no zoom percentage has to be sent to the GPU.
    /// </summary>
    public static Vector4 CreateZoomFilter(
        ZoomInterpolationMode upscaleMode,
        ZoomDownscaleMode downscaleMode)
    {
        return new Vector4(
            (float)(int)upscaleMode,
            (float)ZoomDownscaleModes.ToShaderCode(downscaleMode),
            0.0f,
            0.0f);
    }
}
