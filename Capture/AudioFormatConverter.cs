using NAudio.Wave;

namespace CamCast.Capture;

/// <summary>
/// Converts whatever WASAPI delivers (float / 16 / 24 / 32 bit PCM) into planar
/// 32 bit float, which is the layout the NDI(R) SDK expects for audio frames.
/// </summary>
internal static class AudioFormatConverter
{
    private static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");

    private enum SampleKind
    {
        Unknown,
        Float32,
        Pcm16,
        Pcm24,
        Pcm32,
        Pcm8
    }

    /// <summary>Returns the number of frames written, 0 on unsupported input.</summary>
    public static int ConvertToPlanarFloat(byte[] input, int byteCount, WaveFormat format, ref float[] planar)
    {
        var channels = format.Channels;
        var blockAlign = format.BlockAlign;

        if (channels <= 0 || blockAlign <= 0 || byteCount <= 0) return 0;

        var frames = Math.Min(byteCount / blockAlign, input.Length / blockAlign);
        if (frames <= 0) return 0;

        var kind = ResolveKind(format);
        var bytesPerSample = format.BitsPerSample / 8;
        if (kind == SampleKind.Unknown || bytesPerSample <= 0) return 0;

        var needed = frames * channels;
        if (planar.Length < needed) planar = new float[needed];

        for (var frame = 0; frame < frames; frame++)
        {
            var frameOffset = frame * blockAlign;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = frameOffset + channel * bytesPerSample;
                planar[channel * frames + frame] = ReadSample(input, offset, kind);
            }
        }

        return frames;
    }

    public static double ComputeRms(float[] planar, int channels, int frames)
    {
        var count = channels * frames;
        if (count <= 0) return 0;

        double sum = 0;
        for (var i = 0; i < count; i++)
            sum += planar[i] * (double)planar[i];

        return Math.Sqrt(sum / count);
    }

    private static SampleKind ResolveKind(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            return SampleKind.Float32;

        if (format.Encoding == WaveFormatEncoding.Pcm)
        {
            return format.BitsPerSample switch
            {
                8 => SampleKind.Pcm8,
                16 => SampleKind.Pcm16,
                24 => SampleKind.Pcm24,
                32 => SampleKind.Pcm32,
                _ => SampleKind.Unknown
            };
        }

        if (format.Encoding == WaveFormatEncoding.Extensible && format is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == SubTypeIeeeFloat && format.BitsPerSample == 32)
                return SampleKind.Float32;

            if (extensible.SubFormat == SubTypePcm)
            {
                return format.BitsPerSample switch
                {
                    8 => SampleKind.Pcm8,
                    16 => SampleKind.Pcm16,
                    24 => SampleKind.Pcm24,
                    32 => SampleKind.Pcm32,
                    _ => SampleKind.Unknown
                };
            }
        }

        return SampleKind.Unknown;
    }

    private static float ReadSample(byte[] buffer, int offset, SampleKind kind)
    {
        switch (kind)
        {
            case SampleKind.Float32:
                return BitConverter.ToSingle(buffer, offset);

            case SampleKind.Pcm16:
                return BitConverter.ToInt16(buffer, offset) / 32768f;

            case SampleKind.Pcm24:
            {
                var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                return value / 8388608f;
            }

            case SampleKind.Pcm32:
                return BitConverter.ToInt32(buffer, offset) / 2147483648f;

            case SampleKind.Pcm8:
                return (buffer[offset] - 128) / 128f;

            default:
                return 0f;
        }
    }
}
