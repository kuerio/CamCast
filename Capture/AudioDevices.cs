using NAudio.CoreAudioApi;

namespace CamCast.Capture;

internal enum AudioSourceKind
{
    None,
    Microphone,
    Loopback
}

internal sealed record AudioSourceInfo(AudioSourceKind Kind, string Id, string Name)
{
    public override string ToString() => Name;

    public static AudioSourceInfo None { get; } = new(AudioSourceKind.None, string.Empty, "(kein Audio)");
}

internal static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioSourceInfo> Enumerate()
    {
        var result = new List<AudioSourceInfo> { AudioSourceInfo.None };

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    result.Add(new AudioSourceInfo(AudioSourceKind.Microphone, device.ID, "Mikrofon: " + device.FriendlyName));
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                    result.Add(new AudioSourceInfo(AudioSourceKind.Loopback, device.ID, "Systemaudio: " + device.FriendlyName));
            }
        }
        catch
        {
            // No audio devices / no access: keep the "none" entry.
        }

        return result;
    }
}
