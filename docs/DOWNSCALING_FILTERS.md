# Downscaling Filters (Minification)

This document records the dedicated minification filter set that replaced the
shared upscale/downscale interpolation enum for zoom-out (< 100 %) rendering.
It covers why minification needs different filters than magnification, the exact
kernels and their tuning constants, how they are implemented in
`Rendering/Shaders/GainMap.hlsl`, the settings migration, and the measured
quality comparison.

External material linked at the bottom is used as **reference only**; the
implementation here is derived from the formulas and was validated with the
project's own renderer.

## Why Minification Is Not Just "Interpolation"

Magnification reconstructs a continuous signal between known samples; the job is
to invent plausible detail and avoid looking blocky or soft. Minification must
do the opposite: an output pixel covers many source pixels, so the source signal
has to be **prefiltered** before it is sampled. Without prefiltering, every
source frequency above the output Nyquist limit folds back into the image as
moiré, jagged edges, shimmering checkerboards and moving patterns while panning.
No reconstruction kernel can remove aliasing after it happened.

Consequences for the filter design:

- **Support widths differ.** A magnification kernel is evaluated on the source
  grid (4 taps around the sample). A minification kernel must be evaluated in
  *output pixel* units, so a radius-2 kernel covers two source pixels per output
  pixel. CPU resamplers such as ImageMagick implement this by dividing the filter
  support by the scale factor when reducing.
- **Ringing vs. sharpness.** Negative-lobe kernels (Catmull-Rom, Lanczos, Kaiser)
  keep edges crisp but can overshoot; all-positive kernels (Mitchell-Robidoux
  family, box/area) suppress ringing at the cost of some softness. Below 100 %
  the acceptable ringing is lower than during magnification, because the
  overshoot is produced by already-averaged samples.
- **Energy conservation.** For pixel-scale patterns (checkerboards, noise, fabric,
  foliage) the filter must not create or destroy average brightness. A box/area
  average is exactly energy preserving; sinc-like filters nearly are, but their
  negative lobes lose a little contrast.
- **Gamma / linear light.** Averaging encoded sRGB values darkens the result.
  The viewer uploads HDR content as linear float and SDR content as
  `R8G8B8A8_UNorm_SRgb`; the GPU decodes sRGB during sampling/mip generation, so
  both the hardware mip chain and the explicit kernels average in linear light.
  This is why the filters look correct at strong reduction without an extra
  shader conversion.
- **The mip chain is a free prefilter.** `GenerateMips` builds box-filtered mip
  levels (2×2 average, linear-light for sRGB). The dedicated filters therefore
  take their taps from a suitable mip level instead of from mip 0, which both
  pre-filters the output footprint and keeps the cost at 16 texture fetches.

## Dedicated Filter Set

`Rendering/ZoomDownscaleMode.cs` (new enum, also the shader wire format sent in
`ZoomFilter.y`):

| Code | Mode | Kernel / parameter | Character | Best for |
| ---: | --- | --- | --- | --- |
| 0 | Auto | legacy adaptive mip path | unchanged default | users who never touch the setting |
| 1 | Nearest | point sample + gradient mip | aliased, crisp pixels | pixel art inspection, debugging |
| 2 | Bilinear | hardware bilinear + trilinear mip | soft, mildly aliased | fast general purpose |
| 3 | CatmullRom | Keys cubic B = 0, C = 0.5 | sharpest cubic, visible lobes | detailed photos, UI screenshots |
| 4 | Mitchell | Mitchell-Netravali B = C = 1/3 | balanced, low ringing | general photos, mixed content |
| 5 | Robidoux | Keys cubic B = 0.3782, C = 0.3109 | sharp but calm, tuned for reduction | downscaled photos, wallpapers |
| 6 | Lanczos2 | sinc(x)·sinc(x/2), radius 2 | sharp, classic lobes | general high-quality reduction |
| 7 | Kaiser2 | sinc(x)·Kaiser window, radius 2, β = 3.5 | sharper than Lanczos-2, slightly stronger lobes | crisp reduction of detailed images |
| 8 | Area | exact output-footprint box average | no negative lobes, energy conserving | checkerboards, noise, fabric, text |

The upscale dropdown keeps its historical five options
(`ZoomInterpolationMode`: Auto / Nearest / Bilinear / Bicubic / Lanczos) and the
same behaviour.

### Kernel formulas

For the fractional position `f` in a mip texel, the four taps sit at distances
`1+f`, `f`, `1-f`, `2-f` from the sample position (the same tap order as the
upscale kernels). All weights are normalised to sum to one.

Keys cubic family (Catmull-Rom, Mitchell, Robidoux):

```text
k(x) = ((12-9B-6C)|x|³ + (-18+12B+6C)|x|² + (6-2B)) / 6            |x| < 1
     = ((-B-6C)|x|³ + (6B+30C)|x|² + (-12B-48C)|x| + (8B+24C)) / 6  1 <= |x| < 2
     = 0                                                          otherwise
```

- Catmull-Rom: B = 0, C = 0.5 — interpolatory, sharp, first negative lobe
  `-0.0625` at half a texel.
- Mitchell-Netravali: B = C = 1/3 — the paper's balanced choice; not
  interpolatory (centre weight 8/9), negative lobe shrinks to `-0.03472`.
- Robidoux: B = 12/(19+9√2) = 0.37821575509399867,
  C = 113/(58+216√2) = 0.31089212245300067. These are the parameters Nicolas
  Robidoux derived for downsampling and the constants used by ImageMagick and
  ImageSharp for their "Robidoux" filter; ImageMagick's source comment describes
  it as a "Keys cubic close to Lanczos2D sharpened". It keeps more passband than
  Mitchell with smaller side lobes than Catmull-Rom.

Lanczos-2 (radius 2):

```text
k(x) = sinc(x) * sinc(x/2),  |x| < 2
```

Kaiser-windowed sinc (radius 2, β = 3.5):

```text
k(x) = sinc(x) * I0(β·sqrt(1-(x/2)²)) / I0(β),  |x| < 2
```

`I0` is the order-0 modified Bessel function of the first kind, evaluated with
the Abramowitz & Stegun 9.8.1/9.8.2 polynomial approximation (absolute error
below 2e-8). β = 3.5 comes from the classic Kaiser design rule
`β = 0.5842·(A-21)^0.4 + 0.07886·(A-21)` for about A = 40 dB side-lobe
attenuation with a four-tap kernel (A = 40 gives β ≈ 3.4). Compared to the
Lanczos-2 sinc window, the Kaiser window tapers the side lobes less at three
quarters of the radius, so the kernel keeps slightly more passband detail and
has slightly stronger lobes (kernel value `-0.0823` at distance 1.5 versus
`-0.0637` for Lanczos-2). Higher β would blur and ring less; lower β would
approach an unwindowed truncated sinc, which rings much more. β = 3.5 also stays
below 3.75, so the window always uses the polynomial branch of the `I0`
approximation (the exponential branch is only needed for larger arguments).

Area / box: the output footprint is `fp` source pixels (fp = texels per pixel).
The integer mip level `L = floor(log2(fp))` has texel size `2^L ∈ [fp/2, fp]`;
two bilinear taps per axis at distance `d = 0.5·(fp/2^L - 1)` mip texels then
cover `2^L·(1+2d) = fp` source pixels. At `fp = 2^k` the taps coincide and the
filter degenerates to the mip texel itself (exactly the box average of the
footprint), and at `fp = 2^(k+1)` it becomes the 2×2 texel box, so the mode is
continuous across mip boundaries.

## GPU Implementation

`GainMap.hlsl` derives the direction from the texture gradients
(`fp = max(|duvDx·size|, |duvDy·size|)`; `fp >= 1` is minification). `ZoomFilter.x`
selects the upscale mode, `ZoomFilter.y` the downscale mode, and Auto (0) still
calls the untouched `SampleTextureFilteredLegacy` path. The pixel shader does not
receive a zoom percentage.

For the cubic/sinc modes the taps are taken **in mip-texel space**:

```text
lod      = max(log2(fp), 0)          // one mip texel ≈ one output pixel
mipSize  = textureSize / 2^lod
pos      = uv * mipSize - 0.5        // continuous mip texel coordinate
basePos  = floor(pos); f = pos - basePos
tap uv   = clamp((basePos + (j - 0.5)) / mipSize, halfTexel, 1 - halfTexel)
weights  = kernel evaluated at distances 1+f, f, 1-f, 2-f
```

Because one mip texel is about one output pixel, the radius-2 kernel spans about
two source pixels per output pixel, which is the same "support in output pixels"
rule CPU resamplers use. The 4×4 tap budget is a hard limit (a six- or
eight-tap kernel would be slightly sharper); beyond the kernel support the mip
chain keeps prefiltering each tap. `SampleLevel` with the clamped coordinate
keeps the filter stable at the image border, and D3D clamps the requested LOD to
the available mip chain, so extreme zoom-out cannot sample past the 1×1 level.

Anti-ringing clamp: for the cubic/sinc modes the filtered result is clamped to
the minimum/maximum of the four nearest mip taps (the 2×2 footprint in the
middle of the 4×4 grid). At strong minification each of those taps is already a
box average, so the clamp only removes negative-lobe overshoot. The CPU analysis
of the same model measured roughly 3–5 % edge overshoot without the clamp and 0 %
with it, at the cost of a small amount of acutance; the same clamp already
existed in the legacy cubic path.

Stability at exactly 100 %: `fp == 1` dispatches to the downscale slot. CatmullRom
and Lanczos2/Kaiser2 are interpolatory, so at aligned texel centres the weights
are `[0,1,0,0]` and the output is a pass-through; Area degenerates to the mip-0
bilinear sample; all weight sums are guarded with `1/max(sum, 1e-4)` so no
division by zero or NaN is reachable, and all kernels have compact support with
finite constants.

Limitations:

- **Isotropic only.** A single LOD from the larger gradient is used, so the
  filter is correct for the viewer's uniform zoom but not for strong anisotropic
  reduction; a true anisotropic filter would need several taps along the minor
  axis.
- **No true EWA / no arbitrary filter size.** Elliptical weighted average
  resampling needs per-pixel kernel tables; this implementation is a fixed 4×4
  grid (Area uses 4 bilinear taps) for a desktop viewer.
- **Mip chain quality is the floor.** D3D `GenerateMips` is a box filter; the
  explicit kernels can only sharpen or smooth that prefiltered data. Fractional
  LOD blends two mip levels, which adds a small amount of extra softness compared
  with convolving the original image with a properly scaled kernel.
- **CPU-side parity is approximate.** `Rendering/ZoomFilterKernel.cs` mirrors the
  weight functions for tests, not the sampling itself.

## Settings And Migration

The persisted setting is **`ZoomDownscaleMode`** (new JSON key) of type
`ZoomDownscaleMode`; the upscale setting keeps its name and type. Migration runs
in `AppSettingsService.NormalizeSettings` and handles two legacy forms:

| Legacy form | Written by | Downscale mapping |
| --- | --- | --- |
| `"ZoomInterpolation": N` | before the 1.2.2 upscale/downscale split | Auto→Auto, Nearest→Nearest, Bilinear→Bilinear, Bicubic→CatmullRom, Lanczos→Lanczos2 |
| `"ZoomInterpolationDownscale": N` (old `ZoomInterpolationMode`) | v1.2.2 | same by-name mapping |

Both legacy members are nullable, read only while loading, mapped onto the new
enum **by filter name** and then cleared, so they can never be written back; the
first save from this build emits `"ZoomDownscaleMode": N` and drops the old keys.
The single pre-split value is applied to both directions first, then the more
specific v1.2.2 downscale value overrides the downscale direction if a
hand-edited file contains both. The registry-driven settings page renders the
downscale choices in this order: Auto, Area, Bilinear, CatmullRom, Mitchell,
Robidoux, Lanczos2, Kaiser2, Nearest.

## Verification

The measurements below come from the real renderer (portable build,
`PrintWindow` screenshots through UI Automation) at real minification ratios, and
from the CPU model in `artifacts/verify/downscale/analysis/kernel_analysis.py`
that reproduces the HLSL sampling model (mip chain, trilinear LOD, 4 taps,
anti-ringing clamp).

### CPU model (1D, `fp` = source pixels per output pixel)

Measured at `fp = 2`; larger `fp` values show the same ordering.

| Filter | detail @ 0.2 cyc/out-px | visible moiré | overshoot, no clamp (fp = 3) | overshoot, clamped (fp = 3) |
| --- | ---: | ---: | ---: | ---: |
| Area | 0.818 | 0.214 | 0.000 | 0.000 |
| CatmullRom | 0.911 | 0.228 | 0.039 | 0.000 |
| Mitchell | 0.855 | 0.220 | 0.004 | 0.000 |
| Robidoux | 0.847 | 0.219 | 0.001 | 0.000 |
| Lanczos2 | 0.910 | 0.228 | 0.048 | 0.000 |
| Kaiser2 (β = 3.5) | 0.930 | 0.231 | 0.054 | 0.000 |

(Detail is the normalised amplitude at 0.2 cycles per output pixel; moiré is the
maximum response above the output Nyquist that folds below 0.15 cycles per output
pixel. The model and its raw output are in
`artifacts/verify/downscale/analysis/`.)

The ordering matches the intent of the set: Kaiser2 > CatmullRom ≈ Lanczos2 >
Mitchell > Robidoux > Area for detail retention, while Area always has zero
overshoot and the lowest aliasing/moiré, and Mitchell/Robidoux are the balanced
choices in between.

### Real renderer

Test images, screenshots and the raw numbers live in
`artifacts/verify/downscale/`. Setup:

- Portable Release build of this tree (`artifacts/verify/downscale/portable/win-x64`),
  published without touching `artifacts/portable`.
- 4096x4096 sRGB test images: 1-px checkerboard, radial sin(r²) zone plate, a
  band-limited chirp whose four quadrants are all above the output Nyquist limit,
  a dark noisy photo-like image with hard structures, and a step/bar pattern.
  The images are generated in a dark sRGB range because the capture is an
  SDR-desktop screenshot of an HDR swap chain (display SDR-white gain); avoiding
  clipping keeps the cross-filter comparison valid.
- Zoom set through the `ZoomSlider` UI Automation range value (50 % = 2:1
  minification, 25 % = 4:1), screenshots via `PrintWindow`, centre crop of the
  rendered image, sRGB decoded back to linear light before measuring.
- Metrics: checkerboard luma standard deviation (lower = less residual pattern),
  chirp luma standard deviation (lower = better suppression of frequencies above
  the output Nyquist limit), zone-plate standard deviation after a strong
  low-pass (lower = less low-frequency moiré), mean gradient magnitude on the
  detailed image (higher = sharper), and step-profile overshoot/undershoot plus
  the 10–90 % width (ringing/sharpness).
- Extra captures at 37 % and 61 % (non-power-of-two ratios) show checkerboard and
  step behaviour where the footprint is not an exact mip multiple.
- `Auto` in both directions was verified pixel-identical to the released
  pre-change v1.2.2 portable build at 50 % and 25 %.

The measured table is in `artifacts/verify/downscale/RESULTS.md` together with
the settings-UI migration screenshots and the persisted JSON files.

## References

Consulted as background; not instructions:

- ImageMagick resize filter documentation: <https://imagemagick.org/Usage/resize/>
- ImageMagick `MagickCore/resize.c` (Robidoux constants and filter windows):
  <https://android.googlesource.com/platform/external/ImageMagick/+/100b8d9c4f78f07a52dac4aec46abfe4c2013196%5E%21/MagickCore/resize.c>
- "BC-splines with 2C+B=1 are optimal for EWA resampling" (Robidoux parameters
  B = 12/(19+9√2), C = 113/(58+216√2)):
  <https://imagemagick.net/discourse-server/viewtopic.php?p=112047>
- Mitchell & Netravali, "Reconstruction Filters in Computer Graphics",
  SIGGRAPH 1988: <https://dl.acm.org/doi/10.1145/378456.378514>
- Kaiser window background: <https://en.wikipedia.org/wiki/Kaiser_window>
- Abramowitz & Stegun, *Handbook of Mathematical Functions*, 9.8 (I0 polynomial
  approximation)
- OpenImageIO dev list, "Review: improve MIPmap generation quality with good
  filters": <https://lists.aswf.io/g/oiio-dev/message/6762>
- Vulkanised 2024 resampling talk (custom mip pyramids, filter kernels):
  <https://vulkan.org/user/pages/09.events/vulkanised-2024/vulkanised-2024-arkadiusz-lachowicz-devsh-2.pdf>
- StackOverflow, "Does it make sense to use own mipmap creation algorithm for
  OpenGL textures?": <https://stackoverflow.com/questions/831893/does-it-make-sense-to-use-own-mipmap-creation-algorithm-for-opengl-textures>
- ezEngine `ezImageFilterSincWithKaiserWindow` documentation (Kaiser beta range):
  <https://ezengine.github.io/api-docs/d1/d42/classez_image_filter_sinc_with_kaiser_window.htm>
