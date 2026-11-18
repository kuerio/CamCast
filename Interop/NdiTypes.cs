using System.Runtime.InteropServices;

namespace CamCast.Interop;

// ---------------------------------------------------------------------------
// Native NDI(R) SDK structures.
//
// Layout is taken from the official SDK header "Processing.NDI.structs.h"
// (MIT licensed header, part of the NDI SDK by Vizrt NDI AB).
// All layouts below are verified against the C definitions for x64.
// ---------------------------------------------------------------------------

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_send_create_t
{
    public byte* p_ndi_name;   // UTF8
    public byte* p_groups;     // UTF8, may be NULL
    public byte clock_video;   // bool (1 byte)
    public byte clock_audio;   // bool (1 byte)
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_video_frame_v2_t
{
    public int xres;
    public int yres;
    public uint FourCC;
    public int frame_rate_N;
    public int frame_rate_D;
    public float picture_aspect_ratio;
    public uint frame_format_type;
    public long timecode;
    public byte* p_data;
    public int line_stride_in_bytes;
    public byte* p_metadata;
    public long timestamp;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_audio_frame_v2_t
{
    public int sample_rate;
    public int no_channels;
    public int no_samples;
    public long timecode;
    public float* p_data;
    public int channel_stride_in_bytes;
    public byte* p_metadata;
    public long timestamp;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_source_t
{
    public byte* p_ndi_name;
    public byte* p_url_address;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_find_create_t
{
    public byte show_local_sources; // bool (1 byte)
    public byte* p_groups;
    public byte* p_extra_ips;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_recv_create_v3_t
{
    public NDIlib_source_t source_to_connect_to;
    public uint color_format;
    public int bandwidth;
    public byte allow_video_fields; // bool (1 byte)
    public byte* p_ndi_recv_name;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_audio_frame_v3_t
{
    public int sample_rate;
    public int no_channels;
    public int no_samples;
    public long timecode;
    public uint FourCC;
    public byte* p_data;
    public int channel_stride_in_bytes;
    public byte* p_metadata;
    public long timestamp;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NDIlib_metadata_frame_t
{
    public int length;
    public long timecode;
    public byte* p_data;
}

internal static class NdiTypes
{
    /// <summary>Builds a little endian FourCC from four ASCII characters.</summary>
    public static uint FourCC(char a, char b, char c, char d) =>
        (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

    public static readonly uint VideoTypeUYVY = FourCC('U', 'Y', 'V', 'Y');
    public static readonly uint VideoTypeBGRA = FourCC('B', 'G', 'R', 'A');
    public static readonly uint VideoTypeBGRX = FourCC('B', 'G', 'R', 'X');

    public const uint FrameFormatProgressive = 1;
    public const uint FrameFormatInterleaved = 0;

    public const long TimecodeSynthesize = long.MaxValue;

    public static int BytesPerPixel(uint fourCC)
    {
        if (fourCC == VideoTypeUYVY) return 2;
        return 4; // BGRA / BGRX
    }
}
