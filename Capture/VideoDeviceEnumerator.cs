using Windows.Devices.Enumeration;

namespace CamCast.Capture;

internal sealed record VideoDeviceInfo(int Index, string Name, string Id)
{
    public override string ToString() => $"{Index} – {Name}";
}

/// <summary>
/// Lists video capture devices. Enumeration uses the Windows device API; the
/// index is the position in this list, which is what the OpenCV capture backends
/// expect. The live preview in the UI shows which camera is actually captured.
/// </summary>
internal static class VideoDeviceEnumerator
{
    public static async Task<IReadOnlyList<VideoDeviceInfo>> EnumerateAsync()
    {
        var result = new List<VideoDeviceInfo>();

        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var index = 0;
            foreach (var device in devices)
            {
                if (!device.IsEnabled) continue;
                result.Add(new VideoDeviceInfo(index++, device.Name ?? "Kamera", device.Id ?? string.Empty));
            }
        }
        catch
        {
            // Fall back to a plain index list so the app stays usable.
        }

        if (result.Count == 0)
        {
            for (var i = 0; i < 4; i++)
                result.Add(new VideoDeviceInfo(i, $"Kamera {i}", string.Empty));
        }

        return result;
    }
}
