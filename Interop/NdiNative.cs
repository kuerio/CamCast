using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace CamCast.Interop;

// ---------------------------------------------------------------------------
// Dynamically loads the NDI(R) runtime (Processing.NDI.Lib.x64.dll) at runtime
// instead of linking against an import library. This keeps the build free of
// any SDK dependency: only the free NDI runtime has to be installed.
// ---------------------------------------------------------------------------

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate byte DInitialize();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DDestroy();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate IntPtr DSendCreate(NDIlib_send_create_t* settings);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DSendDestroy(IntPtr instance);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DSendVideoV2(IntPtr instance, NDIlib_video_frame_v2_t* frame);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DSendAudioV2(IntPtr instance, NDIlib_audio_frame_v2_t* frame);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate int DSendGetNoConnections(IntPtr instance, uint timeoutMs);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate IntPtr DVersion();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate IntPtr DRecvCreateV3(NDIlib_recv_create_v3_t* createSettings);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate int DRecvCaptureV3(IntPtr instance, NDIlib_video_frame_v2_t* video,
    NDIlib_audio_frame_v3_t* audio, NDIlib_metadata_frame_t* metadata, uint timeoutMs);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DRecvFreeVideoV2(IntPtr instance, NDIlib_video_frame_v2_t* video);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DRecvFreeAudioV3(IntPtr instance, NDIlib_audio_frame_v3_t* audio);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DRecvFreeMetadata(IntPtr instance, NDIlib_metadata_frame_t* metadata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DRecvDestroy(IntPtr instance);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate IntPtr DFindCreateV2(NDIlib_find_create_t* createSettings);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void DFindDestroy(IntPtr instance);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate byte DFindWaitForSources(IntPtr instance, uint timeoutMs);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate IntPtr DFindGetCurrentSources(IntPtr instance, uint* noSources);

internal static unsafe class NdiNative
{
    private const string DllName = "Processing.NDI.Lib.x64.dll";

    private static IntPtr _handle;
    private static bool _initialized;

    private static DInitialize _initialize = null!;
    private static DDestroy _destroy = null!;
    private static DSendCreate _sendCreate = null!;
    private static DSendDestroy _sendDestroy = null!;
    private static DSendVideoV2 _sendVideoV2 = null!;
    private static DSendAudioV2 _sendAudioV2 = null!;
    private static DSendGetNoConnections _sendGetNoConnections = null!;
    private static DVersion _version = null!;
    private static DFindCreateV2 _findCreateV2 = null!;
    private static DFindDestroy _findDestroy = null!;
    private static DFindWaitForSources _findWaitForSources = null!;
    private static DFindGetCurrentSources _findGetCurrentSources = null!;
    private static DRecvCreateV3 _recvCreateV3 = null!;
    private static DRecvCaptureV3 _recvCaptureV3 = null!;
    private static DRecvFreeVideoV2 _recvFreeVideoV2 = null!;
    private static DRecvFreeAudioV3 _recvFreeAudioV3 = null!;
    private static DRecvFreeMetadata _recvFreeMetadata = null!;
    private static DRecvDestroy _recvDestroy = null!;

    public static bool IsLoaded => _handle != IntPtr.Zero;
    public static bool IsInitialized => _initialized;
    public static string LoadedFrom { get; private set; } = "(nicht geladen)";

    public static string GetVersion()
    {
        if (!IsLoaded || _version is null) return "?";
        var ptr = _version();
        return ptr == IntPtr.Zero ? "?" : (Marshal.PtrToStringUTF8(ptr) ?? "?");
    }

    public static bool TryLoad(out string? error)
    {
        if (IsLoaded)
        {
            error = null;
            return _initialized;
        }

        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!File.Exists(path)) continue;
                if (NativeLibrary.TryLoad(path, out var h) && h != IntPtr.Zero)
                {
                    _handle = h;
                    LoadedFrom = path;
                    break;
                }
            }
            catch
            {
                // keep probing
            }
        }

        if (_handle == IntPtr.Zero && NativeLibrary.TryLoad(DllName, out var fromPath) && fromPath != IntPtr.Zero)
        {
            _handle = fromPath;
            LoadedFrom = DllName + " (PATH)";
        }

        if (_handle == IntPtr.Zero)
        {
            error = "NDI-Runtime (Processing.NDI.Lib.x64.dll) wurde nicht gefunden.\n" +
                    "Bitte die kostenlose NDI Runtime installieren: https://ndi.video/tools/";
            return false;
        }

        try
        {
            Bind();
        }
        catch (Exception ex)
        {
            error = "NDI-Runtime gefunden, aber die API konnte nicht geladen werden: " + ex.Message;
            return false;
        }

        if (_initialize() == 0)
        {
            error = "NDIlib_initialize() ist fehlgeschlagen (CPU wird nicht unterstützt?).";
            return false;
        }

        _initialized = true;
        error = null;
        return true;
    }

    public static void Shutdown()
    {
        if (_initialized)
        {
            try { _destroy(); } catch { /* ignore */ }
            _initialized = false;
        }

        if (_handle != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_handle); } catch { /* ignore */ }
            _handle = IntPtr.Zero;
        }
    }

    private static void Bind()
    {
        _initialize = Marshal.GetDelegateForFunctionPointer<DInitialize>(NativeLibrary.GetExport(_handle, "NDIlib_initialize"));
        _destroy = Marshal.GetDelegateForFunctionPointer<DDestroy>(NativeLibrary.GetExport(_handle, "NDIlib_destroy"));
        _sendCreate = Marshal.GetDelegateForFunctionPointer<DSendCreate>(NativeLibrary.GetExport(_handle, "NDIlib_send_create"));
        _sendDestroy = Marshal.GetDelegateForFunctionPointer<DSendDestroy>(NativeLibrary.GetExport(_handle, "NDIlib_send_destroy"));
        _sendVideoV2 = Marshal.GetDelegateForFunctionPointer<DSendVideoV2>(NativeLibrary.GetExport(_handle, "NDIlib_send_send_video_v2"));
        _sendAudioV2 = Marshal.GetDelegateForFunctionPointer<DSendAudioV2>(NativeLibrary.GetExport(_handle, "NDIlib_send_send_audio_v2"));
        _sendGetNoConnections = Marshal.GetDelegateForFunctionPointer<DSendGetNoConnections>(NativeLibrary.GetExport(_handle, "NDIlib_send_get_no_connections"));
        _version = Marshal.GetDelegateForFunctionPointer<DVersion>(NativeLibrary.GetExport(_handle, "NDIlib_version"));
        _findCreateV2 = Marshal.GetDelegateForFunctionPointer<DFindCreateV2>(NativeLibrary.GetExport(_handle, "NDIlib_find_create_v2"));
        _findDestroy = Marshal.GetDelegateForFunctionPointer<DFindDestroy>(NativeLibrary.GetExport(_handle, "NDIlib_find_destroy"));
        _findWaitForSources = Marshal.GetDelegateForFunctionPointer<DFindWaitForSources>(NativeLibrary.GetExport(_handle, "NDIlib_find_wait_for_sources"));
        _findGetCurrentSources = Marshal.GetDelegateForFunctionPointer<DFindGetCurrentSources>(NativeLibrary.GetExport(_handle, "NDIlib_find_get_current_sources"));
        _recvCreateV3 = Marshal.GetDelegateForFunctionPointer<DRecvCreateV3>(NativeLibrary.GetExport(_handle, "NDIlib_recv_create_v3"));
        _recvCaptureV3 = Marshal.GetDelegateForFunctionPointer<DRecvCaptureV3>(NativeLibrary.GetExport(_handle, "NDIlib_recv_capture_v3"));
        _recvFreeVideoV2 = Marshal.GetDelegateForFunctionPointer<DRecvFreeVideoV2>(NativeLibrary.GetExport(_handle, "NDIlib_recv_free_video_v2"));
        _recvFreeAudioV3 = Marshal.GetDelegateForFunctionPointer<DRecvFreeAudioV3>(NativeLibrary.GetExport(_handle, "NDIlib_recv_free_audio_v3"));
        _recvFreeMetadata = Marshal.GetDelegateForFunctionPointer<DRecvFreeMetadata>(NativeLibrary.GetExport(_handle, "NDIlib_recv_free_metadata"));
        _recvDestroy = Marshal.GetDelegateForFunctionPointer<DRecvDestroy>(NativeLibrary.GetExport(_handle, "NDIlib_recv_destroy"));
    }

    /// <summary>
    /// Connects to a source and reports what actually arrives. Used by the
    /// "--probe" diagnostic mode to verify a running stream end to end.
    /// </summary>
    public static unsafe string Probe(string sourceName, int timeoutMs)
    {
        if (!IsInitialized) return "Die NDI-Runtime ist nicht initialisiert.";

        var nameBytes = Encoding.UTF8.GetBytes(sourceName + '\0');

        fixed (byte* pName = nameBytes)
        {
            var settings = new NDIlib_recv_create_v3_t
            {
                source_to_connect_to = new NDIlib_source_t { p_ndi_name = pName, p_url_address = null },
                color_format = 0,   // NDIlib_recv_color_format_BGRX_BGRA
                bandwidth = 100,    // NDIlib_recv_bandwidth_highest
                allow_video_fields = 0,
                p_ndi_recv_name = null
            };

            var receiver = _recvCreateV3(&settings);
            if (receiver == IntPtr.Zero) return "Konnte keinen NDI-Empfänger erstellen.";

            try
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                var video = new NDIlib_video_frame_v2_t();
                var audio = new NDIlib_audio_frame_v3_t();
                var metadata = new NDIlib_metadata_frame_t();

                var videoFrames = 0;
                var audioFrames = 0;
                var width = 0;
                var height = 0;
                uint fourCC = 0;
                double fps = 0;
                var audioInfo = string.Empty;

                while (Environment.TickCount64 < deadline && videoFrames < 5)
                {
                    var frameType = _recvCaptureV3(receiver, &video, &audio, &metadata, 500);

                    switch (frameType)
                    {
                        case 1: // video
                            if (videoFrames == 0)
                            {
                                width = video.xres;
                                height = video.yres;
                                fourCC = video.FourCC;
                                fps = video.frame_rate_D != 0
                                    ? (double)video.frame_rate_N / video.frame_rate_D
                                    : 0;
                            }
                            videoFrames++;
                            _recvFreeVideoV2(receiver, &video);
                            break;

                        case 2: // audio
                            if (audioFrames == 0)
                                audioInfo = $"{audio.sample_rate} Hz, {audio.no_channels} Kanäle, {audio.no_samples} Samples";
                            audioFrames++;
                            _recvFreeAudioV3(receiver, &audio);
                            break;

                        case 3: // metadata
                            _recvFreeMetadata(receiver, &metadata);
                            break;
                    }
                }

                if (videoFrames == 0)
                    return $"Kein Videobild empfangen (Audioframes: {audioFrames}).";

                var result =
                    $"Video OK: {width} x {height}, {fps:0.##} fps, Format {FourCcToString(fourCC)}, {videoFrames} Frames empfangen.";

                result += audioFrames > 0
                    ? $"\nAudio OK: {audioInfo}, {audioFrames} Puffer empfangen."
                    : "\nKein Audio empfangen.";

                return result;
            }
            finally
            {
                _recvDestroy(receiver);
            }
        }
    }

    private static string FourCcToString(uint fourCC)
    {
        var chars = new char[4];
        for (var i = 0; i < 4; i++)
            chars[i] = (char)((fourCC >> (8 * i)) & 0xFF);
        return new string(chars);
    }

    /// <summary>Lists NDI(R) sources currently visible on the network.</summary>
    public static unsafe List<string> ListSources(uint waitMs)
    {
        var result = new List<string>();
        if (!IsInitialized) return result;

        var settings = new NDIlib_find_create_t
        {
            show_local_sources = 1,
            p_groups = null,
            p_extra_ips = null
        };

        var finder = _findCreateV2(&settings);
        if (finder == IntPtr.Zero) return result;

        try
        {
            _findWaitForSources(finder, waitMs);

            uint count;
            var sources = _findGetCurrentSources(finder, &count);

            for (var i = 0; i < count; i++)
            {
                var source = ((NDIlib_source_t*)sources)[i];
                var name = source.p_ndi_name is null ? null : Marshal.PtrToStringUTF8((IntPtr)source.p_ndi_name);
                if (!string.IsNullOrEmpty(name)) result.Add(name!);
            }
        }
        finally
        {
            _findDestroy(finder);
        }

        return result;
    }
    // -- wrappers -----------------------------------------------------------

    public static IntPtr SendCreate(string sourceName, string? group)
    {
        var nameBytes = Encoding.UTF8.GetBytes(sourceName + '\0');
        var groupBytes = Encoding.UTF8.GetBytes((group ?? string.Empty) + '\0');

        fixed (byte* pName = nameBytes)
        fixed (byte* pGroup = groupBytes)
        {
            var desc = new NDIlib_send_create_t
            {
                p_ndi_name = pName,
                p_groups = pGroup,
                clock_video = 0,
                clock_audio = 0
            };
            return _sendCreate(&desc);
        }
    }

    public static void SendDestroy(IntPtr instance)
    {
        if (instance != IntPtr.Zero) _sendDestroy(instance);
    }

    public static void SendVideo(IntPtr instance, IntPtr data, int width, int height,
                                 int stride, uint fourCC, int fpsN, int fpsD)
    {
        if (instance == IntPtr.Zero) return;

        var frame = new NDIlib_video_frame_v2_t
        {
            xres = width,
            yres = height,
            FourCC = fourCC,
            frame_rate_N = fpsN,
            frame_rate_D = fpsD,
            picture_aspect_ratio = 0f,
            frame_format_type = NdiTypes.FrameFormatProgressive,
            timecode = NdiTypes.TimecodeSynthesize,
            p_data = (byte*)data,
            line_stride_in_bytes = stride,
            p_metadata = null,
            timestamp = 0
        };
        _sendVideoV2(instance, &frame);
    }

    /// <summary>Sends planar 32 bit float audio (channel stride = samples * 4).</summary>
    public static void SendAudio(IntPtr instance, IntPtr planarData, int channels, int samples, int sampleRate)
    {
        if (instance == IntPtr.Zero) return;

        var frame = new NDIlib_audio_frame_v2_t
        {
            sample_rate = sampleRate,
            no_channels = channels,
            no_samples = samples,
            timecode = NdiTypes.TimecodeSynthesize,
            p_data = (float*)planarData,
            channel_stride_in_bytes = samples * sizeof(float),
            p_metadata = null,
            timestamp = 0
        };
        _sendAudioV2(instance, &frame);
    }

    public static int GetNoConnections(IntPtr instance, uint timeoutMs = 0)
    {
        if (instance == IntPtr.Zero) return 0;
        return _sendGetNoConnections(instance, timeoutMs);
    }

    // -- discovery of the runtime DLL --------------------------------------

    private static IEnumerable<string> CandidatePaths()
    {
        foreach (var env in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5" })
        {
            var dir = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(dir))
                yield return Path.Combine(dir, DllName);
        }

        // Bundled next to the executable (allowed by the NDI SDK licence).
        yield return Path.Combine(AppContext.BaseDirectory, DllName);

        foreach (var fromRegistry in RegistryPaths())
            yield return fromRegistry;

        foreach (var known in KnownPaths())
            yield return known;
    }

    private static IEnumerable<string> RegistryPaths()
    {
        var roots = new[] { Registry.LocalMachine, Registry.CurrentUser };

        foreach (var root in roots)
        {
            RegistryKey? uninstall = null;
            try { uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"); }
            catch { /* ignore */ }

            if (uninstall is null) continue;

            string[] subKeys;
            try { subKeys = uninstall.GetSubKeyNames(); }
            catch { uninstall.Dispose(); continue; }

            foreach (var sub in subKeys)
            {
                string? displayName = null;
                string? installLocation = null;
                try
                {
                    using var key = uninstall.OpenSubKey(sub);
                    displayName = key?.GetValue("DisplayName") as string;
                    installLocation = key?.GetValue("InstallLocation") as string;
                }
                catch { /* ignore */ }

                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
                    continue;

                if (displayName.Contains("NDI", StringComparison.OrdinalIgnoreCase) &&
                    displayName.Contains("Runtime", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(installLocation, DllName);
                }
            }

            uninstall.Dispose();
        }
    }

    private static IEnumerable<string> KnownPaths()
    {
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        var vendors = new[] { "NDI", "NewTek" };
        var runtimes = new[]
        {
            Path.Combine("NDI 6 Runtime", "v6"),
            Path.Combine("NDI 5 Runtime", "v5"),
            Path.Combine("NDI 4 Runtime", "v4"),
            "NDI 6 Runtime",
            "NDI 5 Runtime"
        };

        foreach (var pf in programFiles)
        {
            if (string.IsNullOrWhiteSpace(pf)) continue;
            foreach (var vendor in vendors)
                foreach (var runtime in runtimes)
                    yield return Path.Combine(pf, vendor, runtime, DllName);
        }
    }
}
