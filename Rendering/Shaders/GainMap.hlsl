struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

Texture2D PrimaryTexture : register(t0);
Texture2D GainMapTexture : register(t1);
SamplerState LinearClampSampler : register(s0);
SamplerState PointClampSampler : register(s1);

cbuffer GainMapConstants : register(b0)
{
    float4 GainMapMin;
    float4 GainMapMax;
    float4 Gamma;
    float4 OffsetSdr;
    float4 OffsetHdr;
    float4 GainMapControl;
    float4 SourceEncoding;
    float4 Orientation;
    float4 DisplayMapping;
    float4 HdrCapacity;
    float4 ImageLayout;
    float4 ToneMapInput;
    float4 ToneMapOutput;
    float4 ViewModeParams;
    float4 ViewerTools;
    // x: ZoomInterpolationMode used while magnifying (display larger than
    // source pixels, zoom above 100 %) - 0 = Auto/legacy, 1 = Nearest,
    // 2 = Bilinear, 3 = Bicubic (Catmull-Rom), 4 = Lanczos-2.
    // y: ZoomDownscaleMode used while minifying (zoom below 100 %) - the
    // minification-specific set: 0 = Auto/legacy, 1 = Nearest,
    // 2 = Bilinear, 3 = CatmullRom, 4 = Mitchell, 5 = Robidoux,
    // 6 = Lanczos2, 7 = Kaiser2, 8 = Area. z/w reserved. The active direction
    // is derived in the pixel shader from the texture gradients, so no zoom
    // percentage is passed from the CPU. Keep aligned with
    // Rendering/GainMapShaderConstants.cs and Rendering/ZoomDownscaleMode.cs.
    float4 ZoomFilter;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    float2 positions[3] =
    {
        float2(-1.0f, -1.0f),
        float2(-1.0f, 3.0f),
        float2(3.0f, -1.0f),
    };

    float2 texCoords[3] =
    {
        float2(0.0f, 1.0f),
        float2(0.0f, -1.0f),
        float2(2.0f, 1.0f),
    };

    VertexOutput output;
    output.Position = float4(positions[vertexId], 0.0f, 1.0f);
    output.TexCoord = texCoords[vertexId];
    return output;
}

float2 ApplyOrientation(float2 uv)
{
    float orientation = Orientation.x;
    if (orientation < 1.5f) return uv;
    if (orientation < 2.5f) return float2(1.0f - uv.x, uv.y);
    if (orientation < 3.5f) return float2(1.0f - uv.x, 1.0f - uv.y);
    if (orientation < 4.5f) return float2(uv.x, 1.0f - uv.y);
    if (orientation < 5.5f) return float2(uv.y, uv.x);
    if (orientation < 6.5f) return float2(uv.y, 1.0f - uv.x);
    if (orientation < 7.5f) return float2(1.0f - uv.y, 1.0f - uv.x);
    return float2(1.0f - uv.y, uv.x);
}

float2 ComputeRawImageUv(float2 panelUv)
{
    float2 rawFittedUv = (panelUv - ImageLayout.zw) / max(ImageLayout.xy, 0.0001f);
    return ApplyOrientation(rawFittedUv);
}

float4 FitToImage(float2 panelUv)
{
    float2 fittedUv = (panelUv - ImageLayout.zw) / max(ImageLayout.xy, 0.0001f);
    float inside =
        step(0.0f, fittedUv.x) *
        step(fittedUv.x, 1.0f) *
        step(0.0f, fittedUv.y) *
        step(fittedUv.y, 1.0f);
    return float4(saturate(fittedUv), inside, 0.0f);
}

float4 ComputeCatmullRomWeights(float t)
{
    float t2 = t * t;
    float t3 = t2 * t;
    return float4(
        (-0.5f * t) + t2 - (0.5f * t3),
        1.0f - (2.5f * t2) + (1.5f * t3),
        (0.5f * t) + (2.0f * t2) - (1.5f * t3),
        (-0.5f * t2) + (0.5f * t3));
}

// ZoomInterpolationMode values carried in ZoomFilter.x while magnifying (the
// image is displayed larger than its source pixels, zoom above 100 %).
static const float ZoomFilterNearest = 1.0f;
static const float ZoomFilterBilinear = 2.0f;
static const float ZoomFilterBicubic = 3.0f;
static const float ZoomFilterLanczos = 4.0f;

// ZoomDownscaleMode values carried in ZoomFilter.y while minifying (the image
// is displayed smaller than its source pixels, zoom below 100 %). Numeric
// values are the wire format of Rendering/ZoomDownscaleMode.cs.
static const float ZoomDownscaleNearest = 1.0f;
static const float ZoomDownscaleBilinear = 2.0f;
static const float ZoomDownscaleCatmullRom = 3.0f;
static const float ZoomDownscaleMitchell = 4.0f;
static const float ZoomDownscaleRobidoux = 5.0f;
static const float ZoomDownscaleLanczos2 = 6.0f;
static const float ZoomDownscaleKaiser2 = 7.0f;
static const float ZoomDownscaleArea = 8.0f;

// The custom 4x4 magnification kernels are only used close to 1:1; a larger
// footprint during magnification is not reachable from the dispatcher (it
// switches to the minification set first). Kept as a guard for direct calls.
static const float ZoomCustomKernelMaxTexelsPerPixel = 2.0f;

// --- Minification filter tuning constants --------------------------------
// Mitchell-Netravali cubic: B = C = 1/3, the paper's balanced choice between
// blurring and ringing (Mitchell & Netravali, "Reconstruction Filters in
// Computer Graphics", SIGGRAPH 1988).
static const float MitchellB = 0.33333333333333333f;
static const float MitchellC = 0.33333333333333333f;

// Robidoux cubic: Keys cubic with B = 12/(19+9*sqrt(2)) and
// C = 113/(58+216*sqrt(2)), the parameters Nicolas Robidoux derived for
// downsampling (close to a sharpened Lanczos-2). Same constants as the
// ImageMagick / ImageSharp "Robidoux" filter.
static const float RobidouxB = 0.37821575509399867f;
static const float RobidouxC = 0.31089212245300067f;

// Lanczos-2 used by the minification set (radius 2 keeps the kernel inside the
// four taps of the 4x4 grid).
static const float MinifyLanczosRadius = 2.0f;

// Kaiser-windowed sinc with radius 2. beta = 3.5 follows the classic Kaiser
// design rule beta = 0.5842*(A-21)^0.4 + 0.07886*(A-21) for about 40 dB side
// lobe attenuation with a 4-tap kernel (A = 40 gives beta ~= 3.4). The Kaiser
// window tapers the sinc side lobes less than Lanczos' sinc window, so this
// kernel keeps slightly more passband detail at the cost of a little more
// ringing; large beta values would blur instead.
static const float MinifyKaiserRadius = 2.0f;
static const float MinifyKaiserBeta = 3.5f;

float LanczosWeight(float x, float radius)
{
    float ax = abs(x);
    if (ax >= radius)
    {
        return 0.0f;
    }

    if (ax < 1e-6f)
    {
        return 1.0f;
    }

    const float pi = 3.14159265358979323846f;
    float px = pi * ax;
    return (sin(px) / px) * (sin(px / radius) / (px / radius));
}

float4 ComputeLanczosWeights(float t, float radius)
{
    // The four tap centers sit at distances 1+t, t, 1-t and 2-t from the
    // sample position, matching the tap order of ComputeCatmullRomWeights.
    float4 weights = float4(
        LanczosWeight(1.0f + t, radius),
        LanczosWeight(t, radius),
        LanczosWeight(1.0f - t, radius),
        LanczosWeight(2.0f - t, radius));
    float sum = weights.x + weights.y + weights.z + weights.w;
    return weights * (1.0f / max(sum, 1e-4f));
}

// Keys cubic filters parameterised by (B, C): B = 0, C = 0.5 is Catmull-Rom,
// B = C = 1/3 is Mitchell-Netravali, B = 0.3782, C = 0.3109 is Robidoux.
float CubicBCWeight(float x, float b, float c)
{
    float ax = abs(x);
    if (ax < 1.0f)
    {
        return ((12.0f - (9.0f * b) - (6.0f * c)) * ax * ax * ax
            + (-18.0f + (12.0f * b) + (6.0f * c)) * ax * ax
            + (6.0f - (2.0f * b))) / 6.0f;
    }

    if (ax < 2.0f)
    {
        return ((-b - (6.0f * c)) * ax * ax * ax
            + ((6.0f * b) + (30.0f * c)) * ax * ax
            + ((-12.0f * b) - (48.0f * c)) * ax
            + ((8.0f * b) + (24.0f * c))) / 6.0f;
    }

    return 0.0f;
}

float4 ComputeCubicBCWeights(float t, float b, float c)
{
    // Same tap order as ComputeCatmullRomWeights: distances 1+t, t, 1-t, 2-t.
    return float4(
        CubicBCWeight(1.0f + t, b, c),
        CubicBCWeight(t, b, c),
        CubicBCWeight(1.0f - t, b, c),
        CubicBCWeight(2.0f - t, b, c));
}

// Modified Bessel function of the first kind, order 0, needed by the Kaiser
// window. Abramowitz & Stegun 9.8.1 / 9.8.2 polynomial approximation with an
// absolute error below 2e-8, far below what a display filter needs.
float BesselI0(float x)
{
    float ax = abs(x);
    if (ax < 3.75f)
    {
        float t = ax / 3.75f;
        t *= t;
        return 1.0f + t * (3.5156229f + t * (3.0899424f + t * (1.2067492f
            + t * (0.2659732f + t * (0.0360768f + t * 0.0045813f)))));
    }

    float t = 3.75f / ax;
    return (exp(ax) / sqrt(ax)) * (0.39894228f + t * (0.01328592f + t * (0.00225319f
        + t * (-0.00157565f + t * (0.00916281f + t * (-0.02057706f
        + t * (0.02635537f + t * (-0.01647633f + t * 0.00392377f))))))));
}

float KaiserSincWeight(float x, float radius, float beta, float inverseI0Beta)
{
    float ax = abs(x);
    if (ax >= radius)
    {
        return 0.0f;
    }

    float ratio = ax / radius;
    float window = BesselI0(beta * sqrt(max(1.0f - (ratio * ratio), 0.0f))) * inverseI0Beta;
    if (ax < 1e-6f)
    {
        return window;
    }

    const float pi = 3.14159265358979323846f;
    float px = pi * ax;
    return (sin(px) / px) * window;
}

float4 ComputeKaiserWeights(float t)
{
    float inverseI0Beta = 1.0f / max(BesselI0(MinifyKaiserBeta), 1e-4f);
    float4 weights = float4(
        KaiserSincWeight(1.0f + t, MinifyKaiserRadius, MinifyKaiserBeta, inverseI0Beta),
        KaiserSincWeight(t, MinifyKaiserRadius, MinifyKaiserBeta, inverseI0Beta),
        KaiserSincWeight(1.0f - t, MinifyKaiserRadius, MinifyKaiserBeta, inverseI0Beta),
        KaiserSincWeight(2.0f - t, MinifyKaiserRadius, MinifyKaiserBeta, inverseI0Beta));
    float sum = weights.x + weights.y + weights.z + weights.w;
    return weights * (1.0f / max(sum, 1e-4f));
}

// ---------------------------------------------------------------------------
// Minification (zoom below 100 %): prefiltered downscaling
//
// Downscaling is not interpolation. A destination pixel must average its whole
// source footprint, otherwise source frequencies above the output Nyquist limit
// fold back as moire. The GPU mip chain already gives us a box-prefiltered copy
// of the image in linear light, so the explicit minification filters take their
// taps in *mip texel space*: the LOD is chosen so that one mip texel is about
// one output pixel, which makes a radius-2 kernel (Catmull-Rom, Mitchell,
// Robidoux, Lanczos-2, Kaiser-2) span about two source pixels radius - the
// filter is then evaluated in output-pixel units, the same way CPU resamplers
// such as ImageMagick widen the filter support by 1/scale when reducing. A
// five-tap kernel would be slightly better but the 4x4 grid is a hard budget;
// beyond its support the mip chain keeps providing prefiltering.
// ---------------------------------------------------------------------------

float4 SampleTextureMinifiedKernel(
    Texture2D tex,
    float2 uv,
    float mode,
    float2 texSize,
    float texelsPerPixel)
{
    // One mip texel ~= one output pixel, so the four taps of the radius-2
    // kernels cover about four source pixels per output pixel.
    float lod = max(log2(max(texelsPerPixel, 1.0f)), 0.0f);
    float mipScale = exp2(lod);
    float2 mipSize = max(texSize / mipScale, 1.0f);
    float2 invMipSize = 1.0f / mipSize;
    float2 halfTexel = 0.5f * invMipSize;
    float2 maxCoord = 1.0f - halfTexel;
    float2 pos = (uv * mipSize) - 0.5f;
    float2 basePos = floor(pos);
    float2 f = pos - basePos;

    float4 wx;
    float4 wy;
    if (mode < ZoomDownscaleMitchell - 0.5f)
    {
        wx = ComputeCatmullRomWeights(f.x);
        wy = ComputeCatmullRomWeights(f.y);
    }
    else if (mode < ZoomDownscaleRobidoux - 0.5f)
    {
        wx = ComputeCubicBCWeights(f.x, MitchellB, MitchellC);
        wy = ComputeCubicBCWeights(f.y, MitchellB, MitchellC);
    }
    else if (mode < ZoomDownscaleLanczos2 - 0.5f)
    {
        wx = ComputeCubicBCWeights(f.x, RobidouxB, RobidouxC);
        wy = ComputeCubicBCWeights(f.y, RobidouxB, RobidouxC);
    }
    else if (mode < ZoomDownscaleKaiser2 - 0.5f)
    {
        wx = ComputeLanczosWeights(f.x, MinifyLanczosRadius);
        wy = ComputeLanczosWeights(f.y, MinifyLanczosRadius);
    }
    else
    {
        wx = ComputeKaiserWeights(f.x);
        wy = ComputeKaiserWeights(f.y);
    }

    float wxArr[4] = { wx.x, wx.y, wx.z, wx.w };
    float wyArr[4] = { wy.x, wy.y, wy.z, wy.w };

    float4 kernelSum = 0.0f;
    float4 c00 = 0.0f;
    float4 c10 = 0.0f;
    float4 c01 = 0.0f;
    float4 c11 = 0.0f;

    [unroll]
    for (int jy = 0; jy < 4; jy++)
    {
        float coordY = clamp(
            (basePos.y + ((float)jy - 0.5f)) * invMipSize.y,
            halfTexel.y,
            maxCoord.y);
        float4 rowSum = 0.0f;

        [unroll]
        for (int jx = 0; jx < 4; jx++)
        {
            float coordX = clamp(
                (basePos.x + ((float)jx - 0.5f)) * invMipSize.x,
                halfTexel.x,
                maxCoord.x);
            float4 tap = tex.SampleLevel(LinearClampSampler, float2(coordX, coordY), lod);
            rowSum += tap * wxArr[jx];

            if (jy == 1 && jx == 1) c00 = tap;
            if (jy == 1 && jx == 2) c10 = tap;
            if (jy == 2 && jx == 1) c01 = tap;
            if (jy == 2 && jx == 2) c11 = tap;
        }

        kernelSum += rowSum * wyArr[jy];
    }

    // Clamp to the surrounding 2x2 footprint (the four nearest prefiltered mip
    // texels) to suppress negative-lobe ringing on hard edges. The same clamp
    // exists in the legacy cubic path; at strong minification each clamped tap
    // is already a large box average, so the clamp only removes overshoot.
    float4 min2x2 = min(min(c00, c10), min(c01, c11));
    float4 max2x2 = max(max(c00, c10), max(c01, c11));
    return clamp(kernelSum, min2x2, max2x2);
}

// Box / area minification: average exactly the output pixel footprint. The
// integer mip level provides prefiltered texels no larger than the footprint;
// two bilinear taps per axis then cover the rest of it. With a footprint of one
// texel the taps coincide (100 % zoom), and with two texels they form the
// classic 2x2 box (200 % zoom), so the mode is continuous across mip borders.
float4 SampleTextureMinifiedArea(
    Texture2D tex,
    float2 uv,
    float2 texSize,
    float texelsPerPixel)
{
    float lod = floor(max(log2(max(texelsPerPixel, 1.0f)), 0.0f));
    float mipScale = exp2(lod);
    float2 mipSize = max(texSize / mipScale, 1.0f);
    float2 invMipSize = 1.0f / mipSize;
    float2 halfTexel = 0.5f * invMipSize;
    float2 maxCoord = 1.0f - halfTexel;
    float footprint = clamp(texelsPerPixel / mipScale, 1.0f, 2.0f);
    float tapOffset = 0.5f * (footprint - 1.0f);
    float2 pos = (uv * mipSize) - 0.5f;

    float4 sum = 0.0f;
    [unroll]
    for (int jy = -1; jy <= 1; jy += 2)
    {
        [unroll]
        for (int jx = -1; jx <= 1; jx += 2)
        {
            float2 tapPos = pos + (float2((float)jx, (float)jy) * tapOffset);
            float2 coord = clamp((tapPos + 0.5f) * invMipSize, halfTexel, maxCoord);
            sum += tex.SampleLevel(LinearClampSampler, coord, lod);
        }
    }

    return sum * 0.25f;
}

float4 SampleTextureMinified(
    Texture2D tex,
    float2 uv,
    float2 duvDx,
    float2 duvDy,
    float mode,
    float2 texSize,
    float texelsPerPixel)
{
    float4 filtered = 0.0f;
    if (mode < ZoomDownscaleBilinear - 0.5f)
    {
        // Nearest: point sampling; the gradients still select the mip level.
        filtered = tex.SampleGrad(PointClampSampler, uv, duvDx, duvDy);
    }
    else if (mode < ZoomDownscaleCatmullRom - 0.5f)
    {
        // Bilinear: hardware linear filtering plus mip trilinear minification.
        filtered = tex.SampleGrad(LinearClampSampler, uv, duvDx, duvDy);
    }
    else if (mode > ZoomDownscaleKaiser2 + 0.5f)
    {
        filtered = SampleTextureMinifiedArea(tex, uv, texSize, texelsPerPixel);
    }
    else
    {
        filtered = SampleTextureMinifiedKernel(tex, uv, mode, texSize, texelsPerPixel);
    }

    return filtered;
}

// Explicit user-selected filters. While magnifying these are the historical
// Nearest / Bilinear / Bicubic / Lanczos reconstruction kernels; while
// minifying they are the prefiltered ZoomDownscaleMode set (Nearest, Bilinear,
// CatmullRom, Mitchell, Robidoux, Lanczos2, Kaiser2, Area).
// The historical adaptive path lives in SampleTextureFilteredLegacy below and
// is selected by Auto (0) in either direction so the default rendering is
// unchanged.
float4 SampleTextureFilteredExplicit(
    Texture2D tex,
    float2 uv,
    float2 duvDx,
    float2 duvDy,
    float mode,
    bool minifying)
{
    uint texW = 1;
    uint texH = 1;
    tex.GetDimensions(texW, texH);
    float2 texSize = max(float2((float)texW, (float)texH), 1.0f);
    float2 dxTexels = duvDx * texSize;
    float2 dyTexels = duvDy * texSize;
    float texelsPerPixel = max(length(dxTexels), length(dyTexels));

    float4 filtered = 0.0f;
    if (minifying)
    {
        filtered = SampleTextureMinified(tex, uv, duvDx, duvDy, mode, texSize, texelsPerPixel);
    }
    else if (mode < ZoomFilterBilinear - 0.5f)
    {
        // Nearest: point sampling; the gradients still select the mip level.
        filtered = tex.SampleGrad(PointClampSampler, uv, duvDx, duvDy);
    }
    else
    {
        float4 trilinear = tex.SampleGrad(LinearClampSampler, uv, duvDx, duvDy);
        if (mode < ZoomFilterBicubic - 0.5f)
        {
            // Bilinear: hardware linear magnification plus mip trilinear minification.
            filtered = trilinear;
        }
        else if (texelsPerPixel >= ZoomCustomKernelMaxTexelsPerPixel)
        {
            // Unreachable from the dispatcher (minification uses the dedicated
            // set); kept as a guard for direct callers.
            filtered = trilinear;
        }
        else
        {
            float2 invTexSize = 1.0f / texSize;
            float lod = max(log2(max(texelsPerPixel, 1.0f)), 0.0f);
            float mipScale = exp2(lod);
            float2 halfTexel = (0.5f * mipScale) * invTexSize;
            float2 maxCoord = 1.0f - halfTexel;
            float2 pos = (uv * texSize) - 0.5f;
            float2 basePos = floor(pos);
            float2 f = pos - basePos;

            bool useBicubic = mode < ZoomFilterLanczos - 0.5f;
            float4 wx = useBicubic
                ? ComputeCatmullRomWeights(f.x)
                : ComputeLanczosWeights(f.x, 2.0f);
            float4 wy = useBicubic
                ? ComputeCatmullRomWeights(f.y)
                : ComputeLanczosWeights(f.y, 2.0f);
            float wxArr[4] = { wx.x, wx.y, wx.z, wx.w };
            float wyArr[4] = { wy.x, wy.y, wy.z, wy.w };

            float4 kernelSum = 0.0f;
            float4 c00 = 0.0f;
            float4 c10 = 0.0f;
            float4 c01 = 0.0f;
            float4 c11 = 0.0f;

            [unroll]
            for (int jy = 0; jy < 4; jy++)
            {
                float coordY = clamp(
                    (basePos.y + ((float)jy - 0.5f)) * invTexSize.y,
                    halfTexel.y,
                    maxCoord.y);
                float4 rowSum = 0.0f;

                [unroll]
                for (int jx = 0; jx < 4; jx++)
                {
                    float coordX = clamp(
                        (basePos.x + ((float)jx - 0.5f)) * invTexSize.x,
                        halfTexel.x,
                        maxCoord.x);
                    float4 tap = tex.SampleLevel(LinearClampSampler, float2(coordX, coordY), lod);
                    rowSum += tap * wxArr[jx];

                    if (jy == 1 && jx == 1) c00 = tap;
                    if (jy == 1 && jx == 2) c10 = tap;
                    if (jy == 2 && jx == 1) c01 = tap;
                    if (jy == 2 && jx == 2) c11 = tap;
                }

                kernelSum += rowSum * wyArr[jy];
            }

            // Clamp to the surrounding 2x2 footprint to suppress negative-lobe
            // ringing around hard edges, matching the legacy cubic clamp.
            float4 min2x2 = min(min(c00, c10), min(c01, c11));
            float4 max2x2 = max(max(c00, c10), max(c01, c11));
            filtered = clamp(kernelSum, min2x2, max2x2);
        }
    }

    return filtered;
}

float4 SampleTextureFilteredLegacy(Texture2D tex, float2 uv, float2 duvDx, float2 duvDy, bool allowPixelGrid)
{
    uint texW = 1;
    uint texH = 1;
    tex.GetDimensions(texW, texH);
    float2 texSize = max(float2((float)texW, (float)texH), 1.0f);
    float2 dxTexels = duvDx * texSize;
    float2 dyTexels = duvDy * texSize;
    float texelsPerPixel = max(length(dxTexels), length(dyTexels));

    float4 trilinear = tex.SampleGrad(LinearClampSampler, uv, duvDx, duvDy);
    if (texelsPerPixel >= 1.0f)
    {
        return trilinear;
    }

    float2 invTexSize = 1.0f / texSize;
    float2 halfTexel = 0.5f * invTexSize;
    float2 maxCoord = 1.0f - halfTexel;
    float2 pos = (uv * texSize) - 0.5f;
    float2 basePos = floor(pos);
    float2 f = pos - basePos;

    float4 wx = ComputeCatmullRomWeights(f.x);
    float4 wy = ComputeCatmullRomWeights(f.y);
    float wxArr[4] = { wx.x, wx.y, wx.z, wx.w };
    float wyArr[4] = { wy.x, wy.y, wy.z, wy.w };

    float4 catmull = 0.0f;
    float4 c00 = 0.0f;
    float4 c10 = 0.0f;
    float4 c01 = 0.0f;
    float4 c11 = 0.0f;

    [unroll]
    for (int jy = 0; jy < 4; jy++)
    {
        float coordY = clamp((basePos.y + ((float)jy - 0.5f)) * invTexSize.y, halfTexel.y, maxCoord.y);
        float4 rowSum = 0.0f;

        [unroll]
        for (int jx = 0; jx < 4; jx++)
        {
            float coordX = clamp((basePos.x + ((float)jx - 0.5f)) * invTexSize.x, halfTexel.x, maxCoord.x);
            float4 tap = tex.SampleLevel(LinearClampSampler, float2(coordX, coordY), 0.0f);
            rowSum += tap * wxArr[jx];

            if (jy == 1 && jx == 1) c00 = tap;
            if (jy == 1 && jx == 2) c10 = tap;
            if (jy == 2 && jx == 1) c01 = tap;
            if (jy == 2 && jx == 2) c11 = tap;
        }

        catmull += rowSum * wyArr[jy];
    }

    float4 min2x2 = min(min(c00, c10), min(c01, c11));
    float4 max2x2 = max(max(c00, c10), max(c01, c11));
    catmull = clamp(catmull, min2x2, max2x2);

    float4 magnified = catmull;
    if (allowPixelGrid)
    {
        float2 fSharp = saturate(((f - 0.5f) / max(texelsPerPixel, 0.0001f)) + 0.5f);
        float4 pixelGrid = lerp(lerp(c00, c10, fSharp.x), lerp(c01, c11, fSharp.x), fSharp.y);
        float gridBlend = smoothstep(0.20f, 0.09f, texelsPerPixel);
        magnified = lerp(catmull, pixelGrid, gridBlend);
    }

    float magBlend = smoothstep(1.0f, 0.75f, texelsPerPixel);
    return lerp(trilinear, magnified, magBlend);
}

// Dispatch: the direction is derived from the texture gradients used for mip
// selection. One source texel or more per destination pixel means the image is
// minified (zoom below 100 %); fewer means it is magnified (zoom above 100 %).
// ZoomFilter.x selects the filter while magnifying, ZoomFilter.y while
// minifying. Auto (0) in the selected slot keeps the historical adaptive
// filter, which itself already picks the sharpened magnification path or the
// mip-filtered minification path from the same gradients.
float4 SampleTextureFiltered(Texture2D tex, float2 uv, float2 duvDx, float2 duvDy, bool allowPixelGrid)
{
    uint texW = 1;
    uint texH = 1;
    tex.GetDimensions(texW, texH);
    float2 texSize = max(float2((float)texW, (float)texH), 1.0f);
    float texelsPerPixel = max(length(duvDx * texSize), length(duvDy * texSize));
    bool magnifying = texelsPerPixel < 1.0f;
    float mode = magnifying ? ZoomFilter.x : ZoomFilter.y;

    float4 filtered = 0.0f;
    if (mode > 0.5f)
    {
        filtered = SampleTextureFilteredExplicit(tex, uv, duvDx, duvDy, mode, !magnifying);
    }
    else
    {
        filtered = SampleTextureFilteredLegacy(tex, uv, duvDx, duvDy, allowPixelGrid);
    }

    return filtered;
}

float3 SrgbToLinear(float3 value)
{
    float3 low = value / 12.92f;
    float3 high = pow(max((value + 0.055f) / 1.055f, 0.0f), 2.4f);
    return lerp(high, low, value <= 0.04045f);
}

float3 Rec709ToLinear(float3 value)
{
    float3 low = value / 4.5f;
    float3 high = pow(max((value + 0.099f) / 1.099f, 0.0f), 1.0f / 0.45f);
    return lerp(high, low, value < 0.081f);
}

float3 AdobeRgbToLinear(float3 value)
{
    return pow(max(value, 0.0f), 563.0f / 256.0f);
}

float3 RommRgbToLinear(float3 value)
{
    float3 clamped = max(value, 0.0f);
    float3 low = clamped / 16.0f;
    float3 high = pow(clamped, 1.8f);
    return lerp(high, low, value < 0.03125f);
}

float3 Bt2020ToBt709(float3 value)
{
    return float3(
        (1.660491f * value.r) - (0.587641f * value.g) - (0.072850f * value.b),
        (-0.124550f * value.r) + (1.132900f * value.g) - (0.008349f * value.b),
        (-0.018151f * value.r) - (0.100579f * value.g) + (1.118730f * value.b));
}

float3 P3ToBt709(float3 value)
{
    return float3(
        (1.224940f * value.r) - (0.224940f * value.g),
        (-0.042057f * value.r) + (1.042057f * value.g),
        (-0.019638f * value.r) - (0.078636f * value.g) + (1.098274f * value.b));
}

float3 ProPhotoToBt709(float3 value)
{
    return float3(
        (2.034368f * value.r) - (0.727634f * value.g) - (0.306733f * value.b),
        (-0.228827f * value.r) + (1.231753f * value.g) - (0.002927f * value.b),
        (-0.008558f * value.r) - (0.153268f * value.g) + (1.161827f * value.b));
}

float3 AdobeRgbToBt709(float3 value)
{
    return float3(
        (1.398356f * value.r) - (0.398356f * value.g),
        value.g,
        (-0.042929f * value.g) + (1.042929f * value.b));
}

float3 ConvertGainMapBaseToBt709(float3 value)
{
    float3 converted = value;
    if (GainMapControl.z > 3.5f)
    {
        converted = AdobeRgbToBt709(value);
    }
    else if (GainMapControl.z > 2.5f)
    {
        converted = ProPhotoToBt709(value);
    }
    else if (GainMapControl.z > 1.5f)
    {
        converted = Bt2020ToBt709(value);
    }
    else if (GainMapControl.z > 0.5f)
    {
        converted = P3ToBt709(value);
    }

    return ViewModeParams.w > 0.5f ? max(converted, 0.0f) : converted;
}

float3 HlgToSceneLinear(float3 value)
{
    const float a = 0.17883277f;
    const float b = 0.28466892f;
    const float c = 0.55991073f;
    float3 low = (value * value) / 3.0f;
    float3 high = (exp((value - c) / a) + b) / 12.0f;
    return lerp(high, low, value <= 0.5f);
}

float3 PqToSceneLinear(float3 value)
{
    const float m1 = 2610.0f / 16384.0f;
    const float m2 = 2523.0f / 32.0f;
    const float c1 = 3424.0f / 4096.0f;
    const float c2 = 2413.0f / 128.0f;
    const float c3 = 2392.0f / 128.0f;
    float3 y = pow(max(value, 0.0f), 1.0f / m2);
    float3 nits = 10000.0f * pow(max((y - c1) / max(c2 - (c3 * y), 0.000001f), 0.0f), 1.0f / m1);
    return nits / 80.0f;
}

float CalculateHdrTargetScenePeak()
{
    float result = max(DisplayMapping.x * exp2(max(DisplayMapping.z, 0.0f)), DisplayMapping.x);
    if (DisplayMapping.y > 0.0f)
    {
        result = max(DisplayMapping.y, DisplayMapping.x);
    }

    if (SourceEncoding.x > 1.5f && SourceEncoding.x < 2.5f)
    {
        if (ViewModeParams.x < 0.5f)
        {
            result = 1000.0f / 80.0f;
        }
        else if (DisplayMapping.y <= 0.0f && DisplayMapping.z <= 0.0f)
        {
            result = max(1000.0f / 80.0f, DisplayMapping.x);
        }
    }

    return result;
}

float CalculateHlgSystemGamma(float targetScenePeak)
{
    float targetNits = max(targetScenePeak * 80.0f, 100.0f);
    return clamp(1.2f + (0.42f * log10(targetNits / 1000.0f)), 1.0f, 1.35f);
}

float3 ClampToDisplayPeak(float3 value)
{
    float peak = DisplayMapping.y;
    if (peak > 0.0f)
    {
        float maxC = max(value.r, max(value.g, value.b));
        if (maxC > peak)
        {
            float3 ratioClamped = value * (peak / max(maxC, 0.0001f));
            float minC = max(min(value.r, min(value.g, value.b)), 0.0f);
            float whiteBlend = smoothstep(peak, peak * 3.0f, minC);
            value = lerp(ratioClamped, float3(peak, peak, peak), whiteBlend);
        }
    }

    return value;
}

float3 ApplySdrWhiteScale(float3 value)
{
    return value * max(DisplayMapping.x, 1.0f);
}

float3 ApplyAdaptiveToneMapWithWhiteScale(float3 value, float whiteScale)
{
    if (ToneMapInput.x < 0.5f)
    {
        return ClampToDisplayPeak(value);
    }

    whiteScale = max(whiteScale, 1.0f);
    float virtualTarget = max(ToneMapInput.y, whiteScale);
    float physicalTarget = ToneMapOutput.x > 0.0f ? ToneMapOutput.x : virtualTarget;
    float target = clamp(ToneMapOutput.z, whiteScale, max(physicalTarget, whiteScale));
    float tonePeak = max(ToneMapInput.z, max(virtualTarget, target));
    float contentAvg = max(ToneMapInput.w, 0.0f);
    float globalScale = clamp(ToneMapOutput.w, 0.02f, 1.0f);
    float3 mappedValue = value * globalScale;
    float scaledContentPeak = max(tonePeak * globalScale, target);

    float averageRelativeToWhite = contentAvg / max(whiteScale, 0.0001f);
    float kneeBlend = saturate((averageRelativeToWhite - 0.10f) / 0.70f);
    float kneeFactor = lerp(0.36f, 0.16f, kneeBlend);
    float knee = whiteScale + ((target - whiteScale) * kneeFactor);
    knee = clamp(knee, whiteScale * 0.85f, target * 0.92f);

    float peak = max(max(mappedValue.r, mappedValue.g), mappedValue.b);
    if (peak <= knee)
    {
        return ClampToDisplayPeak(mappedValue);
    }

    float sourceRange = max(scaledContentPeak - knee, 0.0001f);
    float targetRange = max(target - knee, 0.0001f);
    float x = max(peak - knee, 0.0f);
    float denominator = max(1.0f - exp(-sourceRange / targetRange), 0.0001f);
    float mappedPeak = knee + (targetRange * (1.0f - exp(-x / targetRange)) / denominator);
    mappedPeak = min(mappedPeak, target);
    return ClampToDisplayPeak(mappedValue * (mappedPeak / max(peak, 0.0001f)));
}

float3 ApplyAdaptiveToneMap(float3 value)
{
    return ApplyAdaptiveToneMapWithWhiteScale(value, max(DisplayMapping.x, 1.0f));
}

bool IsHlgTransfer()
{
    return SourceEncoding.x > 1.5f && SourceEncoding.x < 2.5f;
}

bool IsPqTransfer()
{
    return SourceEncoding.x > 2.5f && SourceEncoding.x < 3.5f;
}

bool IsLinearScRgbTransfer()
{
    return SourceEncoding.x > 3.5f && SourceEncoding.x < 4.5f;
}

bool IsLinearSceneScRgbTransfer()
{
    return SourceEncoding.x > 4.5f;
}

float GetSingleLayerContentWhiteScale()
{
    float exposure = max(ViewModeParams.z, 0.0f);
    float result = max(DisplayMapping.x, 1.0f);
    if (IsHlgTransfer())
    {
        if (ViewModeParams.x < 0.5f)
        {
            result = max((203.0f / 80.0f) * exposure, 0.0001f);
        }
        else
        {
            float targetPeak = CalculateHdrTargetScenePeak();
            float hlgDiffuseWhite = pow(0.264977f, CalculateHlgSystemGamma(targetPeak)) * targetPeak;
            result = max(hlgDiffuseWhite * exposure, 0.0001f);
        }
    }
    else if (IsPqTransfer() || IsLinearSceneScRgbTransfer())
    {
        result = max((203.0f / 80.0f) * exposure, 0.0001f);
    }
    else if (IsLinearScRgbTransfer())
    {
        float displayScale = SourceEncoding.y <= 0.5f ? max(DisplayMapping.x, 1.0f) : 1.0f;
        result = max(displayScale * exposure, 0.0001f);
    }

    return result;
}

float GetSingleLayerToneMapWhiteScale()
{
    if (ViewModeParams.x < 0.5f)
    {
        return max(DisplayMapping.x, 1.0f);
    }

    return max(GetSingleLayerContentWhiteScale(), 1.0f);
}

float GetSingleLayerSdrPreviewScale()
{
    if (ViewModeParams.x >= 0.5f)
    {
        return 1.0f;
    }

    return max(DisplayMapping.x, 1.0f) / max(GetSingleLayerContentWhiteScale(), 0.0001f);
}

float3 ApplySingleLayerDisplayFitToneMap(float3 value)
{
    if (ToneMapInput.x < 0.5f)
    {
        return ClampToDisplayPeak(value);
    }

    float whiteScale = GetSingleLayerToneMapWhiteScale();
    float virtualTarget = max(ToneMapInput.y, whiteScale);
    float physicalTarget = ToneMapOutput.x > 0.0f ? ToneMapOutput.x : virtualTarget;
    float target = clamp(ToneMapOutput.z, whiteScale, max(physicalTarget, whiteScale));
    float tonePeak = max(ToneMapInput.z, max(virtualTarget, target));
    float midScale = clamp(ToneMapOutput.w, 0.10f, 1.0f);
    float pressure = saturate(1.0f - midScale);
    float3 workingValue = value * midScale;
    float luminance = max(dot(workingValue, float3(0.2126f, 0.7152f, 0.0722f)), 0.0f);
    if (luminance <= 0.000001f)
    {
        return ClampToDisplayPeak(workingValue);
    }

    float midGamma = lerp(1.02f, 1.22f, pressure);
    float mappedLuminance = luminance;
    if (luminance <= whiteScale)
    {
        mappedLuminance = whiteScale * pow(saturate(luminance / max(whiteScale, 0.0001f)), midGamma);
    }
    else
    {
        float targetRange = target - whiteScale;
        if (targetRange <= 0.0001f)
        {
            mappedLuminance = whiteScale;
        }
        else
        {
            float sourceRange = max((tonePeak * midScale) - whiteScale, 0.0001f);
            float excess = max(luminance - whiteScale, 0.0f);
            float compressionRatio = (midGamma * sourceRange) / targetRange;
            float shoulderPower = lerp(2.4f, 1.15f, saturate((compressionRatio - 1.5f) / 10.0f));
            float baseTerm = max(1.0f + ((midGamma * excess) / max(shoulderPower * targetRange, 0.0001f)), 1.0f);
            float mappedExcess = targetRange * (1.0f - pow(baseTerm, -shoulderPower));
            mappedLuminance = min(whiteScale + mappedExcess, target);
        }
    }

    return ClampToDisplayPeak(workingValue * (mappedLuminance / max(luminance, 0.0001f)));
}

float3 ApplySingleLayerToneMap(float3 value)
{
    float3 mapped = ApplyAdaptiveToneMapWithWhiteScale(
        value,
        GetSingleLayerToneMapWhiteScale());
    if (ToneMapOutput.y > 1.5f)
    {
        mapped = ApplySingleLayerDisplayFitToneMap(value);
    }

    return mapped;
}

float3 ApplyHdrOutputMapping(float3 value)
{
    return ApplyAdaptiveToneMap(ApplySdrWhiteScale(value));
}

float3 ApplySdrDisplayAdjustment(float3 value)
{
    return ClampToDisplayPeak(ApplySdrWhiteScale(value));
}

float3 DecodeBaseImageSample(float3 encoded)
{
    float transfer = SourceEncoding.x;
    float3 sceneLinear = 0.0f;
    bool isLinearSceneScRgb = transfer > 4.5f;
    bool isLinearScRgb = transfer > 3.5f && transfer < 4.5f;
    if (Orientation.y > 0.5f)
    {
        if (transfer > 1.5f && transfer < 2.5f)
        {
            float targetPeak = CalculateHdrTargetScenePeak();
            float3 hlgScene = max(encoded, 0.0f);
            float hlgLuma = dot(hlgScene, float3(0.2627f, 0.6780f, 0.0593f));
            sceneLinear = hlgScene * pow(max(hlgLuma, 0.000001f), CalculateHlgSystemGamma(targetPeak) - 1.0f) * targetPeak;
        }
        else
        {
            sceneLinear = encoded;
        }
    }
    else if (isLinearSceneScRgb || isLinearScRgb)
    {
        sceneLinear = encoded;
    }
    else if (transfer > 2.5f)
    {
        sceneLinear = PqToSceneLinear(encoded);
    }
    else if (transfer > 1.5f)
    {
        float targetPeak = CalculateHdrTargetScenePeak();
        float3 hlgScene = max(HlgToSceneLinear(encoded), 0.0f);
        float hlgLuma = dot(hlgScene, float3(0.2627f, 0.6780f, 0.0593f));
        sceneLinear = hlgScene * pow(max(hlgLuma, 0.000001f), CalculateHlgSystemGamma(targetPeak) - 1.0f) * targetPeak;
    }
    else if (transfer > 0.5f && transfer < 1.5f)
    {
        sceneLinear = Rec709ToLinear(encoded);
    }
    else if (SourceEncoding.y > 3.5f)
    {
        sceneLinear = AdobeRgbToLinear(encoded);
    }
    else if (SourceEncoding.y > 2.5f)
    {
        sceneLinear = RommRgbToLinear(encoded);
    }
    else
    {
        sceneLinear = SrgbToLinear(encoded);
    }

    if (SourceEncoding.y > 3.5f)
    {
        sceneLinear = AdobeRgbToBt709(sceneLinear);
        if (ViewModeParams.w > 0.5f)
        {
            sceneLinear = max(sceneLinear, 0.0f);
        }
    }
    else if (SourceEncoding.y > 2.5f)
    {
        sceneLinear = ProPhotoToBt709(sceneLinear);
        if (ViewModeParams.w > 0.5f)
        {
            sceneLinear = max(sceneLinear, 0.0f);
        }
    }
    else if (SourceEncoding.y > 1.5f)
    {
        sceneLinear = Bt2020ToBt709(sceneLinear);
        if (ViewModeParams.w > 0.5f)
        {
            sceneLinear = max(sceneLinear, 0.0f);
        }
    }
    else if (SourceEncoding.y > 0.5f)
    {
        sceneLinear = P3ToBt709(sceneLinear);
        if (ViewModeParams.w > 0.5f)
        {
            sceneLinear = max(sceneLinear, 0.0f);
        }
    }

    if (transfer <= 1.5f)
    {
        return ApplySdrDisplayAdjustment(sceneLinear);
    }

    // Diffuse-white / exposure scale (1.0 = absolute). This only affects
    // single-layer HDR (PQ/HLG/linear scRGB) content.
    sceneLinear *= max(ViewModeParams.z, 0.0f);
    sceneLinear *= GetSingleLayerSdrPreviewScale();

    return ApplySingleLayerToneMap(sceneLinear);
}

float3 DecodeGainMapBaseSample(float3 encoded)
{
    float3 result = encoded;
    if (Orientation.y <= 0.5f)
    {
        if (GainMapControl.z > 3.5f)
        {
            result = AdobeRgbToLinear(encoded);
        }
        else if (GainMapControl.z > 2.5f)
        {
            result = RommRgbToLinear(encoded);
        }
        else if (SourceEncoding.x > 0.5f && SourceEncoding.x < 1.5f)
        {
            result = Rec709ToLinear(encoded);
        }
        else
        {
            result = SrgbToLinear(encoded);
        }
    }

    return result;
}

float CalculateGainMapWeight()
{
    if (HdrCapacity.y <= HdrCapacity.x)
    {
        return saturate(GainMapControl.x);
    }

    float displayHeadroom = DisplayMapping.z;
    return saturate(GainMapControl.x) * saturate((displayHeadroom - HdrCapacity.x) / (HdrCapacity.y - HdrCapacity.x));
}

float CalculateGainMapSceneScale()
{
    float exposureScale = max(ViewModeParams.z, 0.0f);
    if (GainMapControl.y <= 0.5f)
    {
        return (203.0f / 80.0f) * exposureScale;
    }

    return max(DisplayMapping.x, 1.0f) * exposureScale;
}

float3 ApplyGainMapOutputMapping(float3 value)
{
    return ApplyAdaptiveToneMap(value * CalculateGainMapSceneScale());
}

float InterleavedGradientNoise(float2 pixelPos)
{
    float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    return frac(magic.z * frac(dot(floor(pixelPos), magic.xy)));
}

float3 CompositeOverCheckerboard(float3 mapped, float alpha, float2 pixelPos)
{
    alpha = saturate(alpha);
    if (alpha >= 0.999f)
    {
        return mapped;
    }

    if (ViewerTools.z > 0.5f)
    {
        return mapped * alpha;
    }

    float2 cell = floor(pixelPos / 12.0f);
    float parity = fmod(abs(cell.x + cell.y), 2.0f);
    float checkerLinear = parity < 0.5f ? 0.014f : 0.026f;
    float3 background = checkerLinear * max(DisplayMapping.x, 1.0f);
    return lerp(background, mapped, alpha);
}

float3 ApplyOutputDither(float3 color, float2 pixelPos)
{
    if (ViewerTools.z > 0.5f)
    {
        return color;
    }

    float ign = InterleavedGradientNoise(pixelPos);
    float amplitude = (0.5f / 1023.0f) * max(DisplayMapping.x, 1.0f);
    float dither = (ign - 0.5f) * 2.0f * amplitude;
    float luma = dot(max(color, 0.0f), float3(0.2126f, 0.7152f, 0.0722f));
    float fadeNearBlack = saturate(luma * 1023.0f);
    return color + (dither * fadeNearBlack);
}

float4 ApplyViewerTools(float3 mapped, float alpha, float2 panelUv, float2 pixelPos)
{
    clip(panelUv.x - ViewerTools.x);
    clip(ViewerTools.y - panelUv.x);
    float3 composited = CompositeOverCheckerboard(mapped, alpha, pixelPos);
    float3 dithered = ApplyOutputDither(composited, pixelPos);
    return float4(dithered, 1.0f);
}

float4 PSMain(VertexOutput input) : SV_TARGET
{
    float2 rawUv = ComputeRawImageUv(input.TexCoord);
    float2 duvDx = ddx(rawUv);
    float2 duvDy = ddy(rawUv);
    float4 fit = FitToImage(input.TexCoord);
    clip(fit.z - 0.5f);
    float2 uv = ApplyOrientation(fit.xy);

    bool showGainMap = ViewModeParams.x > 2.5f && ViewModeParams.x < 3.5f;
    float4 baseSample = SampleTextureFiltered(PrimaryTexture, uv, duvDx, duvDy, true);
    float3 sdr = DecodeGainMapBaseSample(baseSample.rgb);
    float3 recovery = saturate(SampleTextureFiltered(GainMapTexture, uv, duvDx, duvDy, showGainMap).rgb);

    if (ViewModeParams.x < 0.5f)
    {
        return ApplyViewerTools(ApplySdrDisplayAdjustment(ConvertGainMapBaseToBt709(sdr)), baseSample.a, input.TexCoord, input.Position.xy);
    }

    if (showGainMap)
    {
        return ApplyViewerTools(ApplySdrDisplayAdjustment(SrgbToLinear(recovery)), 1.0f, input.TexCoord, input.Position.xy);
    }

    float3 hdr = 0.0f;
    if (GainMapControl.y > 0.5f)
    {
        float3 gain = saturate(Rec709ToLinear(recovery));
        float headroom = max(GainMapMax.x, 1.0f);
        float effectiveHeadroom = pow(headroom, CalculateGainMapWeight());
        hdr = sdr * (1.0f + ((effectiveHeadroom - 1.0f) * gain));
    }
    else
    {
        float3 logRecovery = pow(recovery, 1.0f / max(Gamma.rgb, 0.0001f));
        float3 logBoost = lerp(GainMapMin.rgb, GainMapMax.rgb, logRecovery);
        hdr = (sdr + OffsetSdr.rgb) * exp2(logBoost * CalculateGainMapWeight()) - OffsetHdr.rgb;
    }

    return ApplyViewerTools(ApplyGainMapOutputMapping(ConvertGainMapBaseToBt709(hdr)), baseSample.a, input.TexCoord, input.Position.xy);
}

float4 BaseImagePSMain(VertexOutput input) : SV_TARGET
{
    float2 rawUv = ComputeRawImageUv(input.TexCoord);
    float2 duvDx = ddx(rawUv);
    float2 duvDy = ddy(rawUv);
    float4 fit = FitToImage(input.TexCoord);
    clip(fit.z - 0.5f);
    float2 uv = ApplyOrientation(fit.xy);

    float4 baseSample = SampleTextureFiltered(PrimaryTexture, uv, duvDx, duvDy, true);
    float3 mapped = DecodeBaseImageSample(baseSample.rgb);
    return ApplyViewerTools(mapped, baseSample.a, input.TexCoord, input.Position.xy);
}

float4 LinearizeSourcePSMain(VertexOutput input) : SV_TARGET
{
    int2 texel = int2(input.Position.xy);
    float4 encodedSample = PrimaryTexture.Load(int3(texel, 0));
    float3 encoded = encodedSample.rgb;
    float transfer = SourceEncoding.x;
    float gamut = SourceEncoding.y;
    float3 linearRgb = encoded;
    if (transfer > 3.5f)
    {
        linearRgb = encoded;
    }
    else if (transfer > 2.5f)
    {
        linearRgb = PqToSceneLinear(encoded);
    }
    else if (transfer > 1.5f)
    {
        linearRgb = max(HlgToSceneLinear(encoded), 0.0f);
    }
    else if (transfer > 0.5f)
    {
        linearRgb = Rec709ToLinear(encoded);
    }
    else if (gamut > 3.5f)
    {
        linearRgb = AdobeRgbToLinear(encoded);
    }
    else if (gamut > 2.5f)
    {
        linearRgb = RommRgbToLinear(encoded);
    }
    else
    {
        linearRgb = SrgbToLinear(encoded);
    }

    return float4(linearRgb, encodedSample.a);
}
