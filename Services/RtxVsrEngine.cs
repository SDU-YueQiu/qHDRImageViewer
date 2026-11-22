// RtxVsrEngine.cs
//
// Self-contained, dependency-free C# P/Invoke wrapper around NVIDIA RTX Video
// Super Resolution (the "VideoSuperRes" effect of the NVIDIA Video Effects /
// VFX SDK). It is the reusable part of the qHDRImageViewer VSR feasibility spike.
//
// Facts this wrapper is built on (verified on an RTX 3060 Laptop, driver 621.14,
// VFX SDK 1.3.0.0):
//   * The effect selector is "VideoSuperRes".
//   * 8-bit BGRA + NVCV_CPU_PINNED NvCVImage buffers work directly; no CUDA
//     tensor / DLPack marshalling is required by the caller.
//   * QualityLevel 19 = HIGHBITRATE_ULTRA (best for clean, high-bitrate or
//     lossless images: it does not smear already-clean detail).
//   * NvVFX_Load() costs ~3 s once; after that arbitrary input sizes/aspect
//     ratios can be processed with NvVFX_Run() (~10-100 ms) without reloading.
//   * Changing QualityLevel after Load() selects a different model. On the
//     VFX 1.3.0.0 runtime tested here the switch is applied by the next Run
//     (10-30 ms, output identical to an explicit Load); an explicit NvVFX_Load()
//     costs 2.5-3.7 s. This wrapper reloads explicitly by default (safe across
//     VFX versions / per NVIDIA docs) and exposes AutoReloadOnQualityChange to
//     skip the reload on runtimes that switch implicitly.
//   * The destination image size is chosen by the caller, not fixed at 2x:
//     1.5x and single-pass 4x destinations were verified with full coverage.
//     The model is designed around 2x; two sequential 2x passes give ~1.65x
//     more detail than one 4x destination run.
//
// DLL handling: the six DLLs are loaded by full path with LoadLibraryEx after
// registering their directory via AddDllDirectory/SetDefaultDllDirectories.
// A legacy SetDllDirectory + LOAD_WITH_ALTERED_SEARCH_PATH fallback is included
// because the NVIDIA feature DLL is loaded dynamically by NVVideoEffects.dll
// and therefore needs the directory in the process DLL search path.
//
// The file has no NuGet / System.Drawing / Windows App SDK dependency; it only
// uses the BCL and kernel32. Target: .NET 8+ / net10.0, Windows x64,
// AllowUnsafeBlocks = true.
//
// License note: this file contains only NVIDIA API declarations (facts) and
// original code; the NVIDIA runtime DLLs it loads are NOT included and must be
// obtained under NVIDIA's own license (see REPORT.md). Do not redistribute the
// NVIDIA DLLs from this repository.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HdrImageViewer.Models;

namespace HdrImageViewer.Services;

/// <summary>How the engine registered the NVIDIA DLL directory with the loader.</summary>
public enum VsrDllSearchStrategy
{
    /// <summary>Register the directory with AddDllDirectory (preferred, scoped).</summary>
    AddDllDirectory = 1,

    /// <summary>Legacy process-wide SetDllDirectory + LOAD_WITH_ALTERED_SEARCH_PATH.</summary>
    SetDllDirectory = 2,
}

/// <summary>Exception thrown by <see cref="RtxVsrEngine"/> with the native NvCV_Status code.</summary>
public sealed class VsrException : Exception
{
    public VsrException(string message, int nativeStatus = 0) : base(message)
    {
        NativeStatus = nativeStatus;
    }

    /// <summary>Native NvCV_Status (0 = success, negative = error).</summary>
    public int NativeStatus { get; }
}

/// <summary>
/// Thread-safe (serialized) wrapper for one NVIDIA VSR effect instance.
/// Create once (per process), call <see cref="Initialize"/> on a background
/// thread, then call <see cref="Process"/> from any thread. Call <see cref="Dispose"/>
/// when shutting down.
/// </summary>
public sealed unsafe class RtxVsrEngine : IDisposable
{
    // ---------------------------------------------------------------------
    // Public configuration / quality table
    // ---------------------------------------------------------------------

    /// <summary>Default quality for a photo viewer: 19 = HIGHBITRATE_ULTRA.</summary>
    public const int DefaultQuality = 19;

    /// <summary>Highest QualityLevel value defined by VFX SDK 1.3 (STREAMING_ULTRA).</summary>
    public const int MaxQualityLevel = 23;

    /// <summary>QualityLevel names, from NVIDIA VFX SDK 1.3 documentation.</summary>
    public static string QualityName(int level) => level switch
    {
        0 => "BICUBIC",
        1 => "LOW",
        2 => "MEDIUM",
        3 => "HIGH",
        4 => "ULTRA",
        8 => "DENOISE_LOW",
        9 => "DENOISE_MEDIUM",
        10 => "DENOISE_HIGH",
        11 => "DENOISE_ULTRA",
        12 => "DEBLUR_LOW",
        13 => "DEBLUR_MEDIUM",
        14 => "DEBLUR_HIGH",
        15 => "DEBLUR_ULTRA",
        16 => "HIGHBITRATE_LOW",
        17 => "HIGHBITRATE_MEDIUM",
        18 => "HIGHBITRATE_HIGH",
        19 => "HIGHBITRATE_ULTRA",
        21 => "STREAMING_MEDIUM",
        23 => "STREAMING_ULTRA",
        _ => $"LEVEL_{level}",
    };

    /// <summary>
    /// True for same-resolution modes (denoise 8-11, deblur 12-15): output size
    /// must equal input size. All other modes upscale.
    /// </summary>
    public static bool IsSameResolutionMode(int level) => level is >= 8 and <= 15;

    /// <summary>
    /// Maps the application-level <see cref="RtxVsrMode"/> + quality tier to the
    /// native NVIDIA VFX SDK 1.3 QualityLevel. The official table documented for
    /// the VideoSuperRes effect (docs.nvidia.com/maxine/vfx/latest/Filters/
    /// VideoSuperResolution.html) is:
    /// <code>
    ///   0             VSR_BICUBIC                                    (not exposed)
    ///   1-4           VSR_LOW / MEDIUM / HIGH / ULTRA                (upscaling)
    ///   5-7           reserved
    ///   8-11          DENOISE_LOW / MEDIUM / HIGH / ULTRA            (output == input size)
    ///   12-15         DEBLUR_LOW / MEDIUM / HIGH / ULTRA             (output == input size)
    ///   16-19         HIGHBITRATE_LOW / MEDIUM / HIGH / ULTRA        (upscaling)
    ///   20            reserved
    ///   21 / 23       STREAMING_MEDIUM / STREAMING_ULTRA             (Ampere+, video only;
    ///                                                                 not exposed by the app)
    ///   22            reserved
    /// </code>
    /// The app intentionally exposes only the four photo-meaningful families above.
    /// </summary>
    public static int QualityLevelFor(RtxVsrMode mode, RtxVsrQualityPreset preset)
    {
        var tier = preset switch
        {
            RtxVsrQualityPreset.Low => 0,
            RtxVsrQualityPreset.Medium => 1,
            RtxVsrQualityPreset.High => 2,
            RtxVsrQualityPreset.Ultra => 3,
            // Defensive only: the service normalizes presets before calling.
            _ => 3,
        };

        var firstLevel = mode switch
        {
            RtxVsrMode.Standard => 1,
            RtxVsrMode.Denoise => 8,
            RtxVsrMode.Deblur => 12,
            RtxVsrMode.HighBitrate => 16,
            // Defensive only: fall back to the photo-friendly default (level 19).
            _ => 16,
        };

        return firstLevel + tier;
    }

    /// <summary>
    /// True when <paramref name="mode"/> must be processed at the source
    /// resolution (Denoise / Deblur): the VFX SDK requires the destination image
    /// to have exactly the input dimensions for these quality levels, so the
    /// caller must not apply the viewer's 2x/4x upscaling preference.
    /// </summary>
    public static bool IsSameResolutionMode(RtxVsrMode mode) =>
        mode is RtxVsrMode.Denoise or RtxVsrMode.Deblur;

    /// <summary>Quality groups useful for a UI combo box.</summary>
    public static readonly (string Name, int[] Values)[] QualityGroups =
    [
        ("Upscale (video-tuned)", [1, 2, 3, 4]),
        ("Denoise (same resolution)", [8, 9, 10, 11]),
        ("Deblur (same resolution)", [12, 13, 14, 15]),
        ("High-bitrate upscale (clean sources)", [16, 17, 18, 19]),
        ("Streaming upscale (Ampere+)", [21, 23]),
    ];

    /// <summary>
    /// Windows DLL file names required by the VSR effect. All six must sit in
    /// the same directory (or be otherwise resolvable by the Windows loader).
    /// </summary>
    public static readonly string[] RequiredDlls =
    [
        "cudart64_12.dll",
        "NVCVImage.dll",
        "nvngxruntime.dll",
        "NVVideoEffects.dll",
        "nvVFXVideoSuperRes.dll",
        "nvngx_vsr.dll",
    ];

    /// <summary>
    /// Optional explicit DLL directory. If null, <see cref="ResolveLibDirectory"/>
    /// searches known locations. Set before <see cref="Initialize"/>.
    /// </summary>
    public string? LibDirectory { get; set; }

    /// <summary>
    /// Set to force a particular loader strategy (diagnostics only). Auto is null.
    /// </summary>
    public static VsrDllSearchStrategy? ForcedDllSearchStrategy { get; set; }

    /// <summary>Hard cap on output pixel count (default 36 MP, matching LatentFolio).</summary>
    public long MaxOutputPixels { get; set; } = 36_000_000;

    /// <summary>Effect strength 0..1; 1.0 preserves the full enhancement (default).</summary>
    public float Strength { get; set; } = 1.0f;

    /// <summary>
    /// If true (default), a QualityLevel change is followed by an explicit
    /// NvVFX_Load() on the next Process call. Diagnostic-only: set false to
    /// observe whether the installed VFX runtime reloads implicitly.
    /// </summary>
    public bool AutoReloadOnQualityChange { get; set; } = true;

    // ---------------------------------------------------------------------
    // Public state / diagnostics
    // ---------------------------------------------------------------------

    /// <summary>True when the 6 DLLs exist and Initialize succeeded.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>True after a successful one-time NvVFX_Load().</summary>
    public bool IsLoaded => _loaded;

    /// <summary>Human-readable status (English), safe to show in a UI/log.</summary>
    public string Status { get; private set; } = "not initialized";

    /// <summary>Directory the NVIDIA DLLs are loaded from (empty until resolved).</summary>
    public string LoadedLibDirectory { get; private set; } = "";

    /// <summary>Loader strategy that actually worked.</summary>
    public VsrDllSearchStrategy? DllSearchStrategyUsed { get; private set; }

    /// <summary>VFX SDK version returned by NvVFX_GetVersion (0 when unknown).</summary>
    public uint SdkVersion { get; private set; }

    /// <summary>Time of the one-time NvVFX_Load() in milliseconds.</summary>
    public double LoadMs { get; private set; }

    /// <summary>Time of the most recent implicit re-Load after a quality change (ms).</summary>
    public double LastReloadMs { get; private set; }

    /// <summary>Wall-clock time of the most recent NvVFX_Run() (ms).</summary>
    public double LastRunMs { get; private set; }

    /// <summary>Wall-clock time of the most recent Process() call (ms, incl. copies).</summary>
    public double LastProcessMs { get; private set; }

    /// <summary>Number of successful NvVFX_Load() calls.</summary>
    public int LoadCount { get; private set; }

    /// <summary>Number of successful Process() calls.</summary>
    public int ProcessedCount { get; private set; }

    /// <summary>Row pitch (bytes) of the most recently allocated pinned input buffer.</summary>
    public int InputPitch => _src != null ? _src->pitch : 0;

    /// <summary>Row pitch (bytes) of the most recently allocated pinned output buffer.</summary>
    public int OutputPitch => _dst != null ? _dst->pitch : 0;

    /// <summary>Currently selected QualityLevel (may differ from the loaded model when a reload is pending).</summary>
    public int QualityLevel { get; private set; } = DefaultQuality;

    /// <summary>True when the selected QualityLevel still needs NvVFX_Load() before Process().</summary>
    public bool ReloadPending => _needsReload;

    /// <summary>
    /// Diagnostics read from the effect (MaxInputWidth/Height, Scale, ...).
    /// Values are filled during <see cref="Initialize"/>; "n/a (code)" when the
    /// selector is not supported by this effect.
    /// </summary>
    public IReadOnlyDictionary<string, string> EffectParameters => _effectParameters;

    /// <summary>Result of one Process() call: BGRA bytes plus output dimensions.</summary>
    public sealed record Result(
        byte[] Bgra,
        int Width,
        int Height,
        double RunMs,
        double TotalMs,
        bool BuffersResized,
        bool Reloaded)
    {
        /// <summary>Output pixel count.</summary>
        public long PixelCount => (long)Width * Height;
    }

    // ---------------------------------------------------------------------
    // Native constants / structures
    // ---------------------------------------------------------------------

    // NvCVImage_PixelFormat (nvCVImage.h)
    private const int NVCV_BGRA = 7;
    // NvCVImage_ComponentType
    private const int NVCV_U8 = 1;
    // layout / memory space
    private const int NVCV_CHUNKY = 0;
    private const int NVCV_CPU_PINNED = 2;
    private const int NVCV_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvCVImage
    {
        public uint width;
        public uint height;
        public int pitch;
        public int pixelFormat;
        public int componentType;
        public byte pixelBytes;
        public byte componentBytes;
        public byte numComponents;
        public byte planar;
        public byte gpuMem;
        public byte colorspace;
        public byte reserved0;
        public byte reserved1;
        public IntPtr pixels;
        public IntPtr deletePtr;
        public IntPtr deleteProc;
        public ulong bufferBytes;
    }

    // nvVideoEffects.h function signatures
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnGetVersion(out uint version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnCreateEffect([MarshalAs(UnmanagedType.LPUTF8Str)] string selector, out IntPtr effect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FnDestroyEffect(IntPtr effect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnSetU32(IntPtr effect, [MarshalAs(UnmanagedType.LPUTF8Str)] string param, uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnSetF32(IntPtr effect, [MarshalAs(UnmanagedType.LPUTF8Str)] string param, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnSetImage(IntPtr effect, [MarshalAs(UnmanagedType.LPUTF8Str)] string param, NvCVImage* image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnGetU32(IntPtr effect, [MarshalAs(UnmanagedType.LPUTF8Str)] string param, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnGetString(IntPtr effect, [MarshalAs(UnmanagedType.LPUTF8Str)] string param, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnLoad(IntPtr effect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnRun(IntPtr effect, int async);

    // nvCVImage.h function signatures
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FnImageAlloc(NvCVImage* image, uint width, uint height, int format, int type,
        uint layout, uint memSpace, uint alignment);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FnImageDealloc(NvCVImage* image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr FnGetErrorString(int code);

    // kernel32 loader API
    private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
    private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool RemoveDllDirectory(IntPtr cookie);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    // ---------------------------------------------------------------------
    // Instance state
    // ---------------------------------------------------------------------

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initLock = new();
    private readonly List<IntPtr> _moduleHandles = [];
    private readonly List<string> _loadNotes = [];
    private readonly Dictionary<string, string> _effectParameters = new(StringComparer.Ordinal);
    private IntPtr _addDllCookie = IntPtr.Zero;
    private bool _legacySearchPathInstalled;
    private bool _disposed;

    private FnGetVersion _getVersion = null!;
    private FnCreateEffect _createEffect = null!;
    private FnDestroyEffect _destroyEffect = null!;
    private FnSetU32 _setU32 = null!;
    private FnSetF32 _setF32 = null!;
    private FnSetImage _setImage = null!;
    private FnGetU32 _getU32 = null!;
    private FnGetString _getString = null!;
    private FnLoad _load = null!;
    private FnRun _run = null!;
    private FnImageAlloc _imageAlloc = null!;
    private FnImageDealloc _imageDealloc = null!;
    private FnGetErrorString? _getErrorString;

    private IntPtr _effect = IntPtr.Zero;
    private NvCVImage* _src;
    private NvCVImage* _dst;
    private uint _srcWidth;
    private uint _srcHeight;
    private uint _dstWidth;
    private uint _dstHeight;
    private bool _loaded;
    private bool _needsReload;
    private int _nativeQuality = DefaultQuality;

    // ---------------------------------------------------------------------
    // Static discovery / capability probe
    // ---------------------------------------------------------------------

    /// <summary>
    /// Cheap availability probe: only checks that the six DLL files exist.
    /// It does NOT load any DLL, create a CUDA context or pay the ~3 s model
    /// load cost. Safe to call on the UI thread.
    /// </summary>
    public static bool IsAvailable(out string libDirectory, out string detail)
    {
        var searched = new List<string>();
        foreach (var dir in CandidateLibDirs())
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            searched.Add(dir);
            if (!Directory.Exists(dir)) continue;
            var missing = RequiredDlls.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
            if (missing.Count == 0)
            {
                libDirectory = dir;
                detail = "NVIDIA VFX DLLs found in " + dir;
                return true;
            }
        }

        libDirectory = "";
        detail = "NVIDIA VFX DLLs not found (need " + string.Join(", ", RequiredDlls) +
                 "). Searched: " + (searched.Count == 0 ? "<none>" : string.Join("; ", searched));
        return false;
    }

    /// <summary>Returns the first directory that contains all six DLLs, or null.</summary>
    public static string? ResolveLibDirectory()
        => IsAvailable(out var dir, out _) ? dir : null;

    /// <summary>
    /// Directories searched for the six NVIDIA DLLs, in priority order.
    /// The explicit QHDR_VSR_DIR environment variable wins, then app-local
    /// "vsr", then per-user caches (qHdrImageViewer, older build folders and the
    /// LatentFolio cache), then VFX SDK locations and finally common
    /// ComfyUI / nvvfx installs.
    /// </summary>
    public static IEnumerable<string> CandidateLibDirs()
    {
        var explicitDir = Environment.GetEnvironmentVariable("QHDR_VSR_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir)) yield return explicitDir;

        yield return Path.Combine(AppContext.BaseDirectory, "vsr");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "qHdrImageViewer", "vsr");
        yield return Path.Combine(localAppData, "HdrImageViewer", "vsr");
        yield return Path.Combine(localAppData, "qHDRImageViewer", "vsr");
        yield return Path.Combine(localAppData, "LatentFolio", "vsr");

        var vfxRoot = Environment.GetEnvironmentVariable("VFX_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(vfxRoot))
            yield return Path.Combine(vfxRoot, "bin");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation", "Video Effects SDK", "bin");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "NVIDIA Corporation", "Video Effects SDK", "bin");

        // ComfyUI + nvvfx: the NVIDIA python wheel bundles all VFX DLLs.
        foreach (var drive in new[] { "C:", "D:", "E:", "F:" })
        {
            foreach (var root in new[] { @"\Recreation\ComfyUI-aki-v3", @"\ComfyUI", @"\AI", @"\ComfyUI_windows_portable" })
            {
                var comfy = drive + root;
                if (!Directory.Exists(comfy)) continue;
                yield return Path.Combine(comfy, @"python\Lib\site-packages\nvvfx\libs");
                yield return Path.Combine(comfy, @"python_embeded\Lib\site-packages\nvvfx\libs");
            }
        }
    }

    /// <summary>GPU information gathered by the cheap nvidia-smi probe.</summary>
    public sealed record GpuInfo(
        string Name,
        string DriverVersion,
        string ComputeCapability,
        string MemoryTotal,
        int ComputeCapabilityNumber)
    {
        /// <summary>Turing (7.5) or newer, per the VFX SDK 1.3 changelog.</summary>
        public bool TuringOrNewer => ComputeCapabilityNumber >= 75;

        /// <summary>Ampere (8.0) or newer - required by streaming quality levels 21/23.</summary>
        public bool AmpereOrNewer => ComputeCapabilityNumber >= 80;
    }

    /// <summary>
    /// Cheap RTX-capability probe: checks the files, then runs nvidia-smi
    /// (one process, typically &lt; 200 ms) to read GPU name, driver and compute
    /// capability. Does NOT load the VFX DLLs or the model. Blocking call: run
    /// it off the UI thread or cache the result.
    /// </summary>
    public static bool TryProbeGpu(out GpuInfo? gpu, out string detail)
    {
        gpu = null;
        var smi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        if (!File.Exists(smi))
        {
            detail = "nvidia-smi.exe not found - no NVIDIA driver installed";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(smi)
            {
                Arguments = "--query-gpu=name,driver_version,compute_cap,memory.total --format=csv,noheader",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                detail = "failed to start nvidia-smi";
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                detail = "nvidia-smi timed out";
                return false;
            }

            var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (line == null)
            {
                detail = "nvidia-smi returned no GPU row (driver/GPU issue)";
                return false;
            }

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
            {
                detail = "unexpected nvidia-smi output: " + line;
                return false;
            }

            var ccText = parts[2];
            int cc = 0;
            if (double.TryParse(ccText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ccDouble))
            {
                cc = (int)Math.Round(ccDouble * 10);
            }

            gpu = new GpuInfo(
                parts[0],
                parts.Length > 1 ? parts[1] : "",
                ccText,
                parts.Length > 3 ? parts[3] : "",
                cc);

            detail = $"{gpu.Name}, driver {gpu.DriverVersion}, compute capability {gpu.ComputeCapability}";
            if (!gpu.TuringOrNewer)
                detail += " - NOT Turing+; VSR is expected to fail with NVCV_ERR_UNSUPPORTEDGPU (-17)";
            else if (!gpu.AmpereOrNewer)
                detail += " - Ampere+ recommendations (quality 21/23) may not be available";
            return true;
        }
        catch (Exception ex)
        {
            detail = "nvidia-smi probe failed: " + ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // Initialize / load
    // ---------------------------------------------------------------------

    /// <summary>
    /// Loads the six DLLs, creates the VideoSuperRes effect, configures it for
    /// 8-bit BGRA input, allocates pinned warm-up images and calls NvVFX_Load()
    /// exactly once. Expected to take ~3 s: call from a background thread.
    /// Returns false (with <see cref="Status"/> set) instead of throwing for
    /// normal "not available" conditions.
    /// </summary>
    /// <param name="warmupWidth">Input width used for the load-time warm-up buffer.</param>
    /// <param name="warmupHeight">Input height used for the load-time warm-up buffer.</param>
    /// <param name="warmupRun">If true, also performs one throw-away Run so the first
    /// real Process() does not pay lazy CUDA/kernel setup.</param>
    public bool Initialize(int warmupWidth = 512, int warmupHeight = 512, bool warmupRun = false)
    {
        lock (_initLock)
        {
            if (IsInitialized) return true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (warmupWidth < 64 || warmupHeight < 64)
                throw new ArgumentOutOfRangeException(nameof(warmupWidth), "warm-up size must be >= 64x64");

            var total = Stopwatch.StartNew();
            try
            {
                var libDir = LibDirectory;
                if (string.IsNullOrWhiteSpace(libDir))
                {
                    if (!IsAvailable(out libDir, out var availability))
                    {
                        Status = availability;
                        return false;
                    }
                }
                else
                {
                    var missing = RequiredDlls.Where(f => !File.Exists(Path.Combine(libDir, f))).ToList();
                    if (missing.Count > 0)
                    {
                        LoadedLibDirectory = libDir;
                        Status = "DLL directory is missing: " + string.Join(", ", missing);
                        return false;
                    }
                }

                LoadedLibDirectory = libDir;
                if (!LoadNativeLibraries(libDir, out var loadError))
                {
                    Status = "DLL load failed: " + loadError;
                    return false;
                }

                BindExports();
                LoadVersion();

                // Create the effect.
                int status = _createEffect("VideoSuperRes", out _effect);
                if (status != NVCV_SUCCESS || _effect == IntPtr.Zero)
                {
                    Status = "NvVFX_CreateEffect(\"VideoSuperRes\") failed: " + DescribeStatus(status);
                    return false;
                }

                // Configure: 8-bit RGBA/BGRA path, quality, strength.
                QualityLevel = DefaultQuality;
                _nativeQuality = QualityLevel;
                Check(_setU32(_effect, "QualityLevel", (uint)QualityLevel), "NvVFX_SetU32(QualityLevel)");
                Check(_setU32(_effect, "ImageEncodingMode", 0), "NvVFX_SetU32(ImageEncodingMode)"); // 0 = 8-bit
                Check(_setF32(_effect, "Strength", Strength), "NvVFX_SetF32(Strength)");

                ReadEffectParameters();

                // Allocate pinned CPU buffers for the one-time warm-up load.
                _src = AllocateImage(warmupWidth, warmupHeight, out var srcStatus);
                if (_src == null) throw StatusToException("NvCVImage_Alloc(input)", srcStatus);
                _dst = AllocateImage(warmupWidth * 2, warmupHeight * 2, out var dstStatus);
                if (_dst == null) throw StatusToException("NvCVImage_Alloc(output)", dstStatus);
                _srcWidth = (uint)warmupWidth;
                _srcHeight = (uint)warmupHeight;
                _dstWidth = (uint)(warmupWidth * 2);
                _dstHeight = (uint)(warmupHeight * 2);

                Check(_setImage(_effect, "SrcImage0", _src), "NvVFX_SetImage(SrcImage0)");
                Check(_setImage(_effect, "DstImage0", _dst), "NvVFX_SetImage(DstImage0)");

                var loadWatch = Stopwatch.StartNew();
                status = _load(_effect);
                loadWatch.Stop();
                if (status != NVCV_SUCCESS)
                {
                    Status = "NvVFX_Load failed: " + DescribeStatus(status);
                    return false;
                }

                _loaded = true;
                LoadCount = 1;
                LoadMs = loadWatch.Elapsed.TotalMilliseconds;
                ReadEffectParameters(); // MaxInputWidth/Height are usually readable after Load

                IsInitialized = true;
                if (warmupRun)
                {
                    var black = new byte[warmupWidth * warmupHeight * 4];
                    Process(black, warmupWidth, warmupHeight, QualityLevel, 2);
                    Status = $"ready: quality {QualityName(QualityLevel)}, loaded in {LoadMs:F0} ms (warm-up run done)";
                }
                else
                {
                    Status = $"ready: quality {QualityName(QualityLevel)}, loaded in {LoadMs:F0} ms";
                }

                Status += $", total init {total.Elapsed.TotalMilliseconds:F0} ms";
                return true;
            }
            catch (Exception ex)
            {
                Status = "initialization failed: " + ex.Message;
                CleanupNative();
                return false;
            }
        }
    }

    private void LoadVersion()
    {
        try
        {
            if (_getVersion(out var version) == NVCV_SUCCESS) SdkVersion = version;
        }
        catch
        {
            // Version query is diagnostic only.
        }
    }

    private void BindExports()
    {
        IntPtr vfx = IntPtr.Zero;
        IntPtr cv = IntPtr.Zero;

        // Identify the module handles by base file name.
        foreach (var handle in _moduleHandles)
        {
            var name = GetModuleFileName(handle);
            if (name.EndsWith("NVVideoEffects.dll", StringComparison.OrdinalIgnoreCase)) vfx = handle;
            else if (name.EndsWith("NVCVImage.dll", StringComparison.OrdinalIgnoreCase)) cv = handle;
        }

        if (vfx == IntPtr.Zero || cv == IntPtr.Zero)
            throw new VsrException("NVVideoEffects.dll / NVCVImage.dll module handle not found");

        _getVersion = BindDelegate<FnGetVersion>(vfx, "NvVFX_GetVersion");
        _createEffect = BindDelegate<FnCreateEffect>(vfx, "NvVFX_CreateEffect");
        _destroyEffect = BindDelegate<FnDestroyEffect>(vfx, "NvVFX_DestroyEffect");
        _setU32 = BindDelegate<FnSetU32>(vfx, "NvVFX_SetU32");
        _setF32 = BindDelegate<FnSetF32>(vfx, "NvVFX_SetF32");
        _setImage = BindDelegate<FnSetImage>(vfx, "NvVFX_SetImage");
        _getU32 = BindDelegate<FnGetU32>(vfx, "NvVFX_GetU32");
        _getString = BindDelegate<FnGetString>(vfx, "NvVFX_GetString");
        _load = BindDelegate<FnLoad>(vfx, "NvVFX_Load");
        _run = BindDelegate<FnRun>(vfx, "NvVFX_Run");
        _imageAlloc = BindDelegate<FnImageAlloc>(cv, "NvCVImage_Alloc");
        _imageDealloc = BindDelegate<FnImageDealloc>(cv, "NvCVImage_Dealloc");

        var errorStringPtr = GetProcAddress(cv, "NvCV_GetErrorStringFromCode");
        if (errorStringPtr != IntPtr.Zero)
            _getErrorString = Marshal.GetDelegateForFunctionPointer<FnGetErrorString>(errorStringPtr);
    }

    private static T BindDelegate<T>(IntPtr module, string exportName) where T : Delegate
    {
        var address = GetProcAddress(module, exportName);
        if (address == IntPtr.Zero)
            throw new VsrException($"export {exportName} not found in module {GetModuleFileName(module)}");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static string GetModuleFileName(IntPtr module)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetModuleFileNameW(module, buffer, (uint)buffer.Capacity);
        return length == 0 ? "?" : buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetModuleFileNameW")]
    private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder fileName, uint size);

    private bool LoadNativeLibraries(string libDir, out string error)
    {
        _moduleHandles.Clear();
        _loadNotes.Clear();
        error = "";

        var forced = ForcedDllSearchStrategy;
        if (forced != VsrDllSearchStrategy.SetDllDirectory)
        {
            if (TryLoadWithAddDllDirectory(libDir, out var addError))
            {
                DllSearchStrategyUsed = VsrDllSearchStrategy.AddDllDirectory;
                return true;
            }

            _loadNotes.Add("AddDllDirectory strategy failed: " + addError);
            UnloadModules();
            RemoveAddDllDirectory();
            if (forced == VsrDllSearchStrategy.AddDllDirectory)
            {
                error = addError;
                return false;
            }
        }

        if (forced == VsrDllSearchStrategy.AddDllDirectory)
        {
            error = "forced AddDllDirectory strategy but it was not attempted";
            return false;
        }

        if (TryLoadWithSetDllDirectory(libDir, out var legacyError))
        {
            DllSearchStrategyUsed = VsrDllSearchStrategy.SetDllDirectory;
            return true;
        }

        _loadNotes.Add("SetDllDirectory strategy failed: " + legacyError);
        UnloadModules();
        error = string.Join(" | ", _loadNotes);
        return false;
    }

    private bool TryLoadWithAddDllDirectory(string libDir, out string error)
    {
        error = "";
        try
        {
            const uint defaultDirs = LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS;
            if (!SetDefaultDllDirectories(defaultDirs))
            {
                error = $"SetDefaultDllDirectories failed (Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }

            var cookie = AddDllDirectory(libDir);
            if (cookie == IntPtr.Zero)
            {
                error = $"AddDllDirectory failed (Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }
            _addDllCookie = cookie;

            const uint flags = LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                               | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS
                               | LOAD_LIBRARY_SEARCH_USER_DIRS;
            return TryLoadAllModules(libDir, flags, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryLoadWithSetDllDirectory(string libDir, out string error)
    {
        error = "";
        try
        {
            if (!SetDllDirectory(libDir))
            {
                error = $"SetDllDirectory failed (Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }
            _legacySearchPathInstalled = true;
            return TryLoadAllModules(libDir, LOAD_WITH_ALTERED_SEARCH_PATH, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryLoadAllModules(string libDir, uint flags, out string error)
    {
        error = "";
        foreach (var dll in RequiredDlls)
        {
            var fullPath = Path.Combine(libDir, dll);
            if (!File.Exists(fullPath))
            {
                error = "missing file " + fullPath;
                return false;
            }

            var handle = LoadLibraryExW(fullPath, IntPtr.Zero, flags);
            if (handle == IntPtr.Zero)
            {
                var win32 = Marshal.GetLastWin32Error();
                error = $"LoadLibraryExW({dll}) failed (Win32 {win32}: {new System.ComponentModel.Win32Exception(win32).Message})";
                return false;
            }
            _moduleHandles.Add(handle);
        }
        return true;
    }

    private void UnloadModules()
    {
        foreach (var handle in _moduleHandles)
        {
            if (handle != IntPtr.Zero) FreeLibrary(handle);
        }
        _moduleHandles.Clear();
    }

    private void RemoveAddDllDirectory()
    {
        if (_addDllCookie != IntPtr.Zero)
        {
            RemoveDllDirectory(_addDllCookie);
            _addDllCookie = IntPtr.Zero;
        }
    }

    // ---------------------------------------------------------------------
    // Process
    // ---------------------------------------------------------------------

    /// <summary>
    /// Runs VSR on one BGRA8888 image (top-down rows) and returns the result
    /// scaled by <paramref name="scale"/> in both dimensions. The effect handle
    /// is reused: changing width/height/aspect ratio only reallocates the pinned
    /// buffers, it does not reload the model.
    /// </summary>
    /// <param name="bgra">Input pixels, top-down, B,G,R,A byte order, length >= width*4*height.</param>
    /// <param name="width">Input width in pixels.</param>
    /// <param name="height">Input height in pixels.</param>
    /// <param name="qualityLevel">-1 keeps the current quality; otherwise the new
    /// QualityLevel (model reload is done automatically before the run).</param>
    /// <param name="scale">Output = input * scale. 2 is the canonical 2x VSR path;
    /// the installed VFX 1.3 runtime also accepts other destination sizes (e.g.
    /// 4x in a single pass - the spike verified full destination coverage).</param>
    /// <exception cref="VsrException">Native failure (with NvCV_Status code).</exception>
    public Result Process(byte[] bgra, int width, int height, int qualityLevel = -1, int scale = 2)
    {
        if (scale is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(scale), "scale must be 1..8");
        long outW = (long)width * scale;
        long outH = (long)height * scale;
        if (outW > 16384 || outH > 16384)
            throw new VsrException($"output {outW}x{outH} exceeds the 16384 px engine limit");
        return ProcessCore(bgra, width, height, (int)outW, (int)outH, qualityLevel);
    }

    /// <summary>
    /// Runs VSR with an explicit destination size (the effect generates into
    /// whatever destination dimensions the caller sets; the VFX SDK documents
    /// that the output aspect ratio is unrestricted). This allows one-pass 1.5x
    /// or 4x output for a viewer that does not want exactly 2x. Quality is
    /// designed around the native 2x scale.
    /// </summary>
    public Result ProcessToSize(byte[] bgra, int width, int height,
        int outputWidth, int outputHeight, int qualityLevel = -1)
        => ProcessCore(bgra, width, height, outputWidth, outputHeight, qualityLevel);

    private Result ProcessCore(byte[] bgra, int width, int height,
        int outputWidth, int outputHeight, int qualityLevel)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "width/height must be positive");
        if (outputWidth <= 0 || outputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputWidth), "output dimensions must be positive");
        if (qualityLevel != -1 && !IsKnownQuality(qualityLevel))
            throw new ArgumentOutOfRangeException(nameof(qualityLevel), $"unsupported QualityLevel {qualityLevel}");
        if (bgra.Length < (long)width * 4 * height)
            throw new ArgumentException($"input buffer too small: need {width * 4L * height} bytes", nameof(bgra));

        long outW = outputWidth;
        long outH = outputHeight;
        if (outW * outH > MaxOutputPixels)
            throw new VsrException($"output would be {outW}x{outH} = {outW * outH} pixels, above MaxOutputPixels={MaxOutputPixels}");
        if (outW > 16384 || outH > 16384)
            throw new VsrException($"output {outW}x{outH} exceeds 16384 px limit");

        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsInitialized || !_loaded) throw new VsrException("engine is not initialized");

            var totalWatch = Stopwatch.StartNew();
            bool reloaded = false;
            bool resized = false;

            // Quality selection: remember the requested value, apply it to the
            // effect, and (unless disabled for diagnostics) reload the model.
            if (qualityLevel != -1 && qualityLevel != QualityLevel)
            {
                QualityLevel = qualityLevel;
                _needsReload = true;
            }

            if (_needsReload)
            {
                if (_nativeQuality != QualityLevel)
                {
                    Check(_setU32(_effect, "QualityLevel", (uint)QualityLevel), "NvVFX_SetU32(QualityLevel)");
                    _nativeQuality = QualityLevel;
                }

                if (AutoReloadOnQualityChange)
                {
                    var reloadWatch = Stopwatch.StartNew();
                    Check(_load(_effect), "NvVFX_Load (after QualityLevel change)");
                    reloadWatch.Stop();
                    LastReloadMs = reloadWatch.Elapsed.TotalMilliseconds;
                    LoadCount++;
                    reloaded = true;
                    _needsReload = false;
                }
                // If AutoReloadOnQualityChange is false the SetU32 above is still
                // applied but the Run below is attempted without Load; this is the
                // diagnostic path that shows whether the runtime reloads implicitly.
            }

            // Resize pinned buffers only when dimensions actually changed.
            var newW = (uint)width;
            var newH = (uint)height;
            var newOutW = (uint)outW;
            var newOutH = (uint)outH;
            if (_src == null || _srcWidth != newW || _srcHeight != newH)
            {
                ResizeImage(ref _src, newW, newH, out var srcStatus);
                if (srcStatus != NVCV_SUCCESS) throw StatusToException("NvCVImage_Alloc(input)", srcStatus);
                _srcWidth = newW;
                _srcHeight = newH;
                resized = true;
            }
            if (_dst == null || _dstWidth != newOutW || _dstHeight != newOutH)
            {
                ResizeImage(ref _dst, newOutW, newOutH, out var dstStatus);
                if (dstStatus != NVCV_SUCCESS) throw StatusToException("NvCVImage_Alloc(output)", dstStatus);
                _dstWidth = newOutW;
                _dstHeight = newOutH;
                resized = true;
            }

            // Copy the input into the pinned buffer, honouring the native pitch.
            int srcPitch = _src->pitch;
            if (srcPitch < width * 4) throw new VsrException($"unexpected input pitch {srcPitch} for width {width}");
            fixed (byte* input = bgra)
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(input + (long)y * rowBytes,
                        (byte*)_src->pixels + (long)y * srcPitch,
                        srcPitch, rowBytes);
                }
            }

            Check(_setImage(_effect, "SrcImage0", _src), "NvVFX_SetImage(SrcImage0)");
            Check(_setImage(_effect, "DstImage0", _dst), "NvVFX_SetImage(DstImage0)");

            var runWatch = Stopwatch.StartNew();
            int status = _run(_effect, 0); // 0 = synchronous
            runWatch.Stop();
            if (status != NVCV_SUCCESS) throw StatusToException("NvVFX_Run", status);
            LastRunMs = runWatch.Elapsed.TotalMilliseconds;

            // Copy the output out of the pinned buffer (row by row, pitch-aware).
            var output = new byte[(long)outW * 4 * outH];
            int dstPitch = _dst->pitch;
            if (dstPitch < outW * 4) throw new VsrException($"unexpected output pitch {dstPitch} for width {outW}");
            fixed (byte* dst = output)
            {
                int rowBytes = (int)outW * 4;
                for (int y = 0; y < outH; y++)
                {
                    Buffer.MemoryCopy((byte*)_dst->pixels + (long)y * dstPitch,
                        dst + (long)y * rowBytes,
                        rowBytes, rowBytes);
                }
            }

            ProcessedCount++;
            if (_needsReload && reloaded) _needsReload = false;
            totalWatch.Stop();
            LastProcessMs = totalWatch.Elapsed.TotalMilliseconds;
            return new Result(output, (int)outW, (int)outH, LastRunMs, LastProcessMs, resized, reloaded);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Change the QualityLevel used by subsequent Process() calls when the
    /// qualityLevel argument is left at -1. The model is reloaded lazily on the
    /// next Process() call (a few seconds), so this method returns immediately.
    /// </summary>
    public void SetQuality(int level)
    {
        if (!IsKnownQuality(level)) throw new ArgumentOutOfRangeException(nameof(level), $"unsupported QualityLevel {level}");
        if (level == QualityLevel) return;
        QualityLevel = level;
        _needsReload = true;
    }

    private static bool IsKnownQuality(int level) => level is 0 or 1 or 2 or 3 or 4
        or >= 8 and <= 19 or 21 or 23;

    private NvCVImage* AllocateImage(int width, int height, out int status)
    {
        var image = (NvCVImage*)NativeMemory.AllocZeroed((nuint)sizeof(NvCVImage));
        status = _imageAlloc(image, (uint)width, (uint)height, NVCV_BGRA, NVCV_U8,
            NVCV_CHUNKY, NVCV_CPU_PINNED, 0);
        if (status != NVCV_SUCCESS)
        {
            NativeMemory.Free(image);
            return null;
        }
        return image;
    }

    private void ResizeImage(ref NvCVImage* image, uint width, uint height, out int status)
    {
        if (image != null)
        {
            _imageDealloc(image);
            NativeMemory.Free(image);
            image = null;
        }
        var allocated = AllocateImage((int)width, (int)height, out status);
        image = allocated;
    }

    // ---------------------------------------------------------------------
    // Diagnostics
    // ---------------------------------------------------------------------

    private void ReadEffectParameters()
    {
        if (_effect == IntPtr.Zero) return;
        TryReadU32("MaxInputWidth");
        TryReadU32("MaxInputHeight");
        TryReadU32("MaxNumberStreams");
        TryReadU32("Scale");
        TryReadU32("StrengthLevels");
        TryReadU32("InputWidth");
        TryReadU32("InputHeight");

        try
        {
            if (_getString(_effect, "Info", out var infoPtr) == NVCV_SUCCESS && infoPtr != IntPtr.Zero)
            {
                var info = Marshal.PtrToStringUTF8(infoPtr);
                if (!string.IsNullOrWhiteSpace(info)) _effectParameters["Info"] = info!;
            }
        }
        catch
        {
            // optional
        }
    }

    private void TryReadU32(string parameter)
    {
        try
        {
            int status = _getU32(_effect, parameter, out var value);
            _effectParameters[parameter] = status == NVCV_SUCCESS ? value.ToString() : $"n/a ({status})";
        }
        catch
        {
            _effectParameters[parameter] = "n/a (exception)";
        }
    }

    /// <summary>Returns a readable name + text for a native NvCV_Status code.</summary>
    public string DescribeStatus(int status)
    {
        var name = status switch
        {
            0 => "NVCV_SUCCESS",
            -1 => "NVCV_ERR_GENERAL",
            -2 => "NVCV_ERR_UNIMPLEMENTED",
            -3 => "NVCV_ERR_MEMORY",
            -4 => "NVCV_ERR_EFFECT",
            -5 => "NVCV_ERR_SELECTOR",
            -6 => "NVCV_ERR_BUFFER",
            -7 => "NVCV_ERR_PARAMETER",
            -8 => "NVCV_ERR_MISMATCH",
            -9 => "NVCV_ERR_PIXELFORMAT",
            -10 => "NVCV_ERR_MODEL",
            -11 => "NVCV_ERR_LIBRARY",
            -12 => "NVCV_ERR_INITIALIZATION",
            -13 => "NVCV_ERR_FILE",
            -14 => "NVCV_ERR_FEATURENOTFOUND",
            -15 => "NVCV_ERR_MISSINGINPUT",
            -16 => "NVCV_ERR_RESOLUTION",
            -17 => "NVCV_ERR_UNSUPPORTEDGPU",
            -18 => "NVCV_ERR_WRONGGPU",
            -19 => "NVCV_ERR_UNSUPPORTEDDRIVER",
            -20 => "NVCV_ERR_MODELDEPENDENCIES",
            -21 => "NVCV_ERR_PARSE",
            -22 => "NVCV_ERR_MODELSUBSTITUTION",
            -23 => "NVCV_ERR_READ",
            -24 => "NVCV_ERR_WRITE",
            -25 => "NVCV_ERR_PARAMREADONLY",
            -26 => "NVCV_ERR_TRT_ENQUEUE",
            -27 => "NVCV_ERR_TRT_BINDINGS",
            -28 => "NVCV_ERR_TRT_CONTEXT",
            -29 => "NVCV_ERR_TRT_INFER",
            -30 => "NVCV_ERR_TRT_ENGINE",
            -31 => "NVCV_ERR_NPP",
            -32 => "NVCV_ERR_CONFIG",
            -33 => "NVCV_ERR_TOOSMALL",
            -34 => "NVCV_ERR_TOOBIG",
            -35 => "NVCV_ERR_WRONGSIZE",
            -36 => "NVCV_ERR_OBJECTNOTFOUND",
            -37 => "NVCV_ERR_SINGULAR",
            -38 => "NVCV_ERR_NOTHINGRENDERED",
            -39 => "NVCV_ERR_CONVERGENCE",
            -40 => "NVCV_ERR_EOF",
            -41 => "NVCV_ERR_VERSION_MISMATCH",
            -1003 => "NVCV_ERR_CUDA_INIT",
            -1002 => "NVCV_ERR_CUDA_MEMORY",
            -1012 => "NVCV_ERR_CUDA_PITCH",
            -1035 => "NVCV_ERR_CUDA_DRIVER",
            -1801 => "NVCV_ERR_CUDA_UNSUPPORTED",
            -1999 => "NVCV_ERR_CUDA",
            _ => $"NvCV_Status({status})",
        };

        string nativeText = "";
        try
        {
            if (_getErrorString != null)
            {
                var ptr = _getErrorString(status);
                if (ptr != IntPtr.Zero) nativeText = Marshal.PtrToStringAnsi(ptr) ?? "";
            }
        }
        catch
        {
            // optional
        }

        return nativeText.Length > 0 ? $"{status} ({name}) \"{nativeText}\"" : $"{status} ({name})";
    }

    private void Check(int status, string operation)
    {
        if (status != NVCV_SUCCESS) throw StatusToException(operation, status);
    }

    private VsrException StatusToException(string operation, int status)
        => new($"{operation} failed: {DescribeStatus(status)}", status);

    /// <summary>Loader fallback notes (diagnostics).</summary>
    public IReadOnlyList<string> LoadNotes => _loadNotes;

    // ---------------------------------------------------------------------
    // Dispose
    // ---------------------------------------------------------------------

    /// <summary>
    /// Releases the effect, pinned buffers, DLL directory registration and
    /// module handles. Blocks until a running Process() completes.
    /// </summary>
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            CleanupNative();
            Status = "disposed";
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void CleanupNative()
    {
        if (_effect != IntPtr.Zero)
        {
            try { _destroyEffect(_effect); } catch { /* native teardown best effort */ }
            _effect = IntPtr.Zero;
        }
        if (_src != null)
        {
            try { _imageDealloc(_src); } catch { /* ignore */ }
            NativeMemory.Free(_src);
            _src = null;
        }
        if (_dst != null)
        {
            try { _imageDealloc(_dst); } catch { /* ignore */ }
            NativeMemory.Free(_dst);
            _dst = null;
        }

        UnloadModules();
        RemoveAddDllDirectory();
        if (_legacySearchPathInstalled)
        {
            try { SetDllDirectory(null); } catch { /* ignore */ }
            _legacySearchPathInstalled = false;
        }

        _loaded = false;
        IsInitialized = false;
    }
}
