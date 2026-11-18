using Windows.Media.Capture;
using Windows.Media.MediaProperties;

namespace CamCast.Capture;

/// <summary>A video format the camera reports as supported.</summary>
internal sealed record CameraFormat(int Width, int Height, IReadOnlyList<double> Fps)
{
    public long Pixels => (long)Width * Height;
}

/// <summary>
/// Asks a camera (through Windows Media Foundation) which video formats it really
/// supports. Results are cached per device id, because initialising the capture
/// device takes a moment.
/// </summary>
internal static class CameraFormats
{
    private static readonly Dictionary<string, IReadOnlyList<CameraFormat>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the supported formats in descending resolution order, or
    /// <c>null</c> when the device cannot be queried (e.g. it is in use).
    /// </summary>
    public static async Task<IReadOnlyList<CameraFormat>?> TryQueryAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        if (Cache.TryGetValue(deviceId, out var cached)) return cached;

        MediaCapture? capture = null;

        try
        {
            capture = new MediaCapture();

            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MediaCategory = MediaCategory.Media
            };

            await capture.InitializeAsync(settings);

            var properties = capture.VideoDeviceController
                .GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord);

            var formats = properties
                .OfType<VideoEncodingProperties>()
                .Where(p => p.Width > 0 && p.Height > 0)
                .GroupBy(p => ((int)p.Width, (int)p.Height))
                .Select(group => new CameraFormat(
                    group.Key.Item1,
                    group.Key.Item2,
                    group.Select(p => ToFps(p.FrameRate))
                         .Where(f => f > 0)
                         .Distinct()
                         .OrderByDescending(f => f)
                         .ToList()))
                .OrderByDescending(f => f.Pixels)
                .ThenByDescending(f => f.Width)
                .ToList();

            if (formats.Count == 0) return null;

            Cache[deviceId] = formats;
            return formats;
        }
        catch
        {
            return null;
        }
        finally
        {
            capture?.Dispose();
        }
    }

    private static double ToFps(MediaRatio? ratio) =>
        ratio is null || ratio.Denominator == 0
            ? 0
            : (double)ratio.Numerator / ratio.Denominator;
}
