# Zoom Interaction Handoff

This note records the current zoom state and an older experimental branch that
lived outside the current project folder. It is intentionally kept separate from
the implementation because the experimental zoom code is not ready to merge.

## Current Viewer

`HdrSwapChainHost` is a `PreviewSurface` sibling that stretches to the visible
viewport. `ImageSurface` remains the scroll extent for pan, fallback, crop, and
Live Photo chrome.

- Wheel and touchpad input update `_zoomScale` and the logical `ImageSurface`
  size used by `ScrollViewer`.
- During the zoom animation, `SwapChainZoomPreviewTransform` scales the
  current HDR frame around the pointer. GPU `ImageLayout` is committed after
  the gesture settles so the preview stays at 16 ms even when redraw is busy.
- Shader `ImageLayout` maps the viewport onto the logical image; pan and the
  settled zoom call `RedrawAsync` without `ResizeBuffers`.
- `ResizeBuffers` runs only when the physical preview size or DPI changes.
- Do not assign `HdrSwapChainHost.Width`/`Height` from the zoomed image size.
  `SwapChainPanel` has no natural size; a 0×0 host presents a blank frame.

Precision touchpad and touchscreen gestures use `ImageScroller`'s native zoom
and pan. Native `ZoomFactor` is included in the renderer's visible image layout
during a gesture, then baked into the logical image size and reset to 1 on
completion. Starting native manipulation clears any pending wheel anchor so
two-finger panning cannot be snapped back to an older offset. Mouse wheel commands
are handled before the scroller's default wheel behavior; Ctrl always zooms.

## Experimental Branch Summary

The newer directory tried to remove the temporary XAML `ScaleTransform` and
instead drive zoom through a hybrid of XAML layout changes and DXGI
`MatrixTransform`:

- Add `_appliedZoom`, `_fitWidth`, `_fitHeight`, and a serialized zoom worker.
- Use `ApplyPanelStretch()` for zoom-out when the existing swap-chain buffer is
  large enough.
- Use `ResizeBuffers` for zoom-in.
- Preserve a zoom matrix across DPI transform updates.

That branch documents its own open bug: wheel-down after zooming in does not
reliably return to the previous size, and rapid wheel input can leave the
visual surface and D3D buffer out of sync. For that reason it was not merged.

## Recommended Fix Direction

The best long-term fix is to stop making `SwapChainPanel` the interactive zoom
surface. A more stable architecture would render the HDR result into an
offscreen FP16 target, display it through a normal XAML surface during
interaction, and re-render sharply after zoom settles. That matches how common
photo viewers separate smooth interaction from final high-quality rendering.

If keeping the current architecture, avoid multiple sources of truth. Choose
one authority for zoom state, buffer size, and scroll offsets, then make resize
events ignore stale commits with an epoch/token instead of matching dimensions.

## Merge Guidance

Safe to reuse from the experimental directory:

- cache byte-budget logic;
- crop export code;
- small UI state improvements;
- documentation.

Do not blindly merge:

- `EnsureZoomWorkerAsync`;
- `_appliedZoom` / `_fitWidth` / `_fitHeight` layout authority;
- `ApplyPanelStretch`, `ApplyZoomPreviewScale`, `ResetZoomPreviewScale`;
- pre-resize present and `Present(0)` timing changes.

## Windows Photos Parity (1.0.47 work)

The bottom-bar mode button (left of the zoom percentage) now toggles between
fit-to-window and 100 % actual size, matching Windows Photos. The mode lives in
`_isActualSizeZoom` (`HomePage.xaml.cs`) alongside `_isFitZoom` / `_isFillZoom`:

- Fit state -> click goes to actual size (`ApplyActualSizeAsync`, which loads
  full resolution first). Any other zoom level -> click fits the window.
  `Ctrl+1` still jumps to actual size and `Ctrl+0` (bare `0`) to fit.
- The actual-size mode is pinned to physical pixels: after a window resize or
  DPI change `TryCalculateImageSurfaceTargetSize` recomputes the scale from
  `CalculateActualSizeZoomScale()` instead of carrying a fit-relative
  `_zoomScale`, so the percentage stays at 100 % and 1 source pixel stays
  1 physical pixel. The icon/tooltip/automation name follow the state
  (inward brackets while fitted, outward arrows otherwise, action in the tooltip).
- Mouse wheel and the +/- buttons / keyboard now use a 10 % step
  (`Presentation/ViewerZoom.cs`, `StepFactor = 1.1`), matching the measured
  Windows Photos behavior: one wheel notch 100 % -> 110 % -> 121 % -> 133 %,
  and a +/- click 100 % -> 110 %. The previous wheel factor was 1.06 and the
  previous button factor 1.25. `ViewerZoomTests` pins the math.

Measured Windows Photos reference (this machine, Photos 2026): wheel notch is
multiplicative x1.1 in both plain-wheel and Ctrl+wheel zoom, the mode button
alternates fit <-> 100 % exactly, and the +/- buttons step by roughly +10
percentage points in the 10–160 % range (coarser +25 % steps above 200 %, which
is intentionally not copied). Photos also clamps wheel zoom-out at fit; the
viewer keeps allowing wheel zoom-out below fit through the existing 1 % floor.

## Dedicated Downscale (Minification) Filters (1.2.3 work)

Zoom-out no longer shares the upscale interpolation set. `ZoomDownscaleMode`
(`Rendering/ZoomDownscaleMode.cs`) adds minification-specific choices: Area,
Mitchell, Robidoux, Lanczos2 and Kaiser2 next to Auto/Nearest/Bilinear/CatmullRom.
Zoom-in still uses `ZoomInterpolationMode` and its five options unchanged.

- The shader takes the minification kernels' taps in mip-texel space
  (`lod = max(log2(texelsPerPixel), 0)`), so a radius-2 kernel pre-filters about
  two source pixels per output pixel; Area averages the exact output footprint
  from an integer mip level. `Auto` still calls the untouched legacy path.
- The setting is stored as `ZoomDownscaleMode` (new JSON key). v1.2.2 files with
  `"ZoomInterpolationDownscale": 0..4` and pre-split files with a single
  `"ZoomInterpolation": N` both migrate by filter name (Bicubic -> CatmullRom,
  Lanczos -> Lanczos2) and the legacy keys are dropped on the first save.
- Algorithm trade-offs, kernel parameters and the measured comparison are in
  `docs/DOWNSCALING_FILTERS.md`.


