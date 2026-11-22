namespace HdrImageViewer.Rendering;

/// <summary>
/// CPU mirror of the GPU zoom-filter weights in Rendering/Shaders/GainMap.hlsl.
/// Keep the formulas in sync with <c>ComputeCatmullRomWeights</c>,
/// <c>ComputeLanczosWeights</c>, <c>ComputeCubicBCWeights</c> and
/// <c>ComputeKaiserWeights</c>. The four weights correspond to the taps at
/// -1, 0, +1 and +2 texels relative to the floor of the sample position; the
/// first weight receives <c>t + 1</c> in the HLSL functions because the tap
/// distance is measured from the fractional position.
/// </summary>
public static class ZoomFilterKernel
{
    public const int TapCount = 4;

    public const float DefaultLanczosRadius = 2.0f;

    /// <summary>
    /// Mitchell-Netravali cubic parameters (B = C = 1/3) used by the
    /// minification "Mitchell" mode.
    /// </summary>
    public const float MitchellB = 1.0f / 3.0f;

    public const float MitchellC = 1.0f / 3.0f;

    /// <summary>
    /// Robidoux downsampling cubic parameters:
    /// B = 12/(19+9*sqrt(2)), C = 113/(58+216*sqrt(2)).
    /// </summary>
    public const float RobidouxB = 0.37821575509399867f;

    public const float RobidouxC = 0.31089212245300067f;

    /// <summary>
    /// Kaiser-windowed sinc parameters used by the minification "Kaiser2"
    /// mode: radius 2 and beta 3.5 (about 40 dB side lobe attenuation for a
    /// four-tap kernel via the classic Kaiser design rule).
    /// </summary>
    public const float DefaultKaiserRadius = 2.0f;

    public const float DefaultKaiserBeta = 3.5f;

    /// <summary>
    /// Catmull-Rom cubic weights (a = -0.5) for the fractional position
    /// <paramref name="t"/> in [0, 1].
    /// </summary>
    public static (float W0, float W1, float W2, float W3) CatmullRomWeights(float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return (
            (-0.5f * t) + t2 - (0.5f * t3),
            1.0f - (2.5f * t2) + (1.5f * t3),
            (0.5f * t) + (2.0f * t2) - (1.5f * t3),
            (-0.5f * t2) + (0.5f * t3));
    }

    /// <summary>
    /// Keys cubic filter value at <paramref name="x"/> texels from the sample
    /// position for the Mitchell-Netravali parameters
    /// <paramref name="b"/> and <paramref name="c"/>.
    /// </summary>
    public static float CubicBCWeight(float x, float b, float c)
    {
        var ax = MathF.Abs(x);
        if (ax < 1.0f)
        {
            return (((12.0f - (9.0f * b) - (6.0f * c)) * ax * ax * ax)
                + ((-18.0f + (12.0f * b) + (6.0f * c)) * ax * ax)
                + (6.0f - (2.0f * b))) / 6.0f;
        }

        if (ax < 2.0f)
        {
            return ((((-b) - (6.0f * c)) * ax * ax * ax)
                + (((6.0f * b) + (30.0f * c)) * ax * ax)
                + (((-12.0f * b) - (48.0f * c)) * ax)
                + ((8.0f * b) + (24.0f * c))) / 6.0f;
        }

        return 0.0f;
    }

    /// <summary>
    /// Cubic (B, C) weights for the fractional position <paramref name="t"/> in
    /// [0, 1], normalised so a flat field keeps its value.
    /// </summary>
    public static (float W0, float W1, float W2, float W3) CubicBCWeights(float t, float b, float c)
    {
        var weights = (
            CubicBCWeight(1.0f + t, b, c),
            CubicBCWeight(t, b, c),
            CubicBCWeight(1.0f - t, b, c),
            CubicBCWeight(2.0f - t, b, c));
        return Normalize(weights);
    }

    /// <summary>
    /// Mitchell-Netravali (B = C = 1/3) weights for the minification mode.
    /// </summary>
    public static (float W0, float W1, float W2, float W3) MitchellWeights(float t)
    {
        return CubicBCWeights(t, MitchellB, MitchellC);
    }

    /// <summary>
    /// Robidoux downsampling cubic weights for the minification mode.
    /// </summary>
    public static (float W0, float W1, float W2, float W3) RobidouxWeights(float t)
    {
        return CubicBCWeights(t, RobidouxB, RobidouxC);
    }

    /// <summary>
    /// Lanczos windowed-sinc weights for the fractional position
    /// <paramref name="t"/> in [0, 1], normalized to sum to one so flat fields
    /// keep their value.
    /// </summary>
    public static (float W0, float W1, float W2, float W3) LanczosWeights(
        float t,
        float radius = DefaultLanczosRadius)
    {
        var (w0, w1, w2, w3) = (
            LanczosWeight(1.0f + t, radius),
            LanczosWeight(t, radius),
            LanczosWeight(1.0f - t, radius),
            LanczosWeight(2.0f - t, radius));
        return Normalize((w0, w1, w2, w3));
    }

    /// <summary>
    /// Lanczos kernel value at <paramref name="x"/> texels from the sample
    /// position: <c>sinc(x) * sinc(x / radius)</c> inside the support window.
    /// </summary>
    public static float LanczosWeight(float x, float radius = DefaultLanczosRadius)
    {
        if (!(radius > 0.0f) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Lanczos radius must be finite and positive.");
        }

        var ax = MathF.Abs(x);
        if (ax >= radius)
        {
            return 0.0f;
        }

        if (ax < 1e-6f)
        {
            return 1.0f;
        }

        var px = MathF.PI * ax;
        return MathF.Sin(px) / px * (MathF.Sin(px / radius) / (px / radius));
    }

    /// <summary>
    /// Modified Bessel function of the first kind, order 0, using the
    /// Abramowitz &amp; Stegun 9.8.1 / 9.8.2 polynomial approximation (absolute
    /// error below 2e-8). Mirrors the HLSL <c>BesselI0</c>.
    /// </summary>
    public static float BesselI0(float x)
    {
        var ax = MathF.Abs(x);
        if (ax < 3.75f)
        {
            var s = ax / 3.75f;
            s *= s;
            var sum = 0.0045813f;
            sum = (sum * s) + 0.0360768f;
            sum = (sum * s) + 0.2659732f;
            sum = (sum * s) + 1.2067492f;
            sum = (sum * s) + 3.0899424f;
            sum = (sum * s) + 3.5156229f;
            return (sum * s) + 1.0f;
        }

        var u = 3.75f / ax;
        var polynomial = 0.00392377f;
        polynomial = (polynomial * u) - 0.01647633f;
        polynomial = (polynomial * u) + 0.02635537f;
        polynomial = (polynomial * u) - 0.02057706f;
        polynomial = (polynomial * u) + 0.00916281f;
        polynomial = (polynomial * u) - 0.00157565f;
        polynomial = (polynomial * u) + 0.00225319f;
        polynomial = (polynomial * u) + 0.01328592f;
        polynomial = (polynomial * u) + 0.39894228f;
        return MathF.Exp(ax) / MathF.Sqrt(ax) * polynomial;
    }

    /// <summary>
    /// Kaiser-windowed sinc value at <paramref name="x"/> texels from the
    /// sample position: <c>sinc(x) * I0(beta * sqrt(1 - (x/radius)^2)) / I0(beta)</c>
    /// inside the support window.
    /// </summary>
    public static float KaiserWeight(
        float x,
        float radius = DefaultKaiserRadius,
        float beta = DefaultKaiserBeta)
    {
        if (!(radius > 0.0f) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Kaiser radius must be finite and positive.");
        }

        if (!(beta >= 0.0f) || !float.IsFinite(beta))
        {
            throw new ArgumentOutOfRangeException(nameof(beta), beta, "Kaiser beta must be finite and non-negative.");
        }

        var ax = MathF.Abs(x);
        if (ax >= radius)
        {
            return 0.0f;
        }

        var ratio = ax / radius;
        var window = BesselI0(beta * MathF.Sqrt(MathF.Max(1.0f - (ratio * ratio), 0.0f)))
            / BesselI0(beta);
        if (ax < 1e-6f)
        {
            return window;
        }

        var px = MathF.PI * ax;
        return MathF.Sin(px) / px * window;
    }

    /// <summary>
    /// Kaiser-windowed sinc weights for the fractional position
    /// <paramref name="t"/> in [0, 1], normalised to sum to one.
    /// </summary>
    public static (float W0, float W1, float W2, float W3) KaiserWeights(
        float t,
        float radius = DefaultKaiserRadius,
        float beta = DefaultKaiserBeta)
    {
        var weights = (
            KaiserWeight(1.0f + t, radius, beta),
            KaiserWeight(t, radius, beta),
            KaiserWeight(1.0f - t, radius, beta),
            KaiserWeight(2.0f - t, radius, beta));
        return Normalize(weights);
    }

    /// <summary>
    /// Integer mip level used by the "Area" minification mode: the level whose
    /// texel is no larger than the output footprint. Mirrors
    /// <c>SampleTextureMinifiedArea</c> in GainMap.hlsl.
    /// </summary>
    public static int AreaMipLevel(float texelsPerPixel)
    {
        if (!(texelsPerPixel > 0.0f) || !float.IsFinite(texelsPerPixel))
        {
            throw new ArgumentOutOfRangeException(nameof(texelsPerPixel), texelsPerPixel, "Footprint must be finite and positive.");
        }

        return (int)MathF.Floor(MathF.Max(MathF.Log2(MathF.Max(texelsPerPixel, 1.0f)), 0.0f));
    }

    /// <summary>
    /// Half the distance, in mip texels, between the two bilinear taps per axis
    /// used by the "Area" mode. Each bilinear tap spans one mip texel, so
    /// <c>2^level * (1 + 2*offset)</c> equals the output footprint.
    /// </summary>
    public static float AreaTapOffset(float texelsPerPixel)
    {
        var level = AreaMipLevel(texelsPerPixel);
        var mipScale = MathF.Pow(2.0f, level);
        var footprint = Math.Clamp(texelsPerPixel / mipScale, 1.0f, 2.0f);
        return 0.5f * (footprint - 1.0f);
    }

    /// <summary>
    /// Effective area footprint in source pixels: <c>2^level * (1 + 2*offset)</c>.
    /// </summary>
    public static float AreaEffectiveFootprint(float texelsPerPixel)
    {
        var level = AreaMipLevel(texelsPerPixel);
        var mipScale = MathF.Pow(2.0f, level);
        return mipScale * (1.0f + (2.0f * AreaTapOffset(texelsPerPixel)));
    }

    private static (float W0, float W1, float W2, float W3) Normalize(
        (float W0, float W1, float W2, float W3) weights)
    {
        var sum = weights.W0 + weights.W1 + weights.W2 + weights.W3;
        var inverseSum = 1.0f / MathF.Max(sum, 1e-4f);
        return (
            weights.W0 * inverseSum,
            weights.W1 * inverseSum,
            weights.W2 * inverseSum,
            weights.W3 * inverseSum);
    }
}
