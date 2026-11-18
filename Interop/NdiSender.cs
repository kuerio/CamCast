namespace CamCast.Interop;

/// <summary>Thin wrapper around an NDI(R) sender instance.</summary>
internal sealed class NdiSender : IDisposable
{
    private IntPtr _instance;

    public string SourceName { get; }
    public bool IsRunning => _instance != IntPtr.Zero;
    public int Connections => NdiNative.GetNoConnections(_instance);

    public NdiSender(string sourceName, string? group)
    {
        SourceName = sourceName;
        _instance = NdiNative.SendCreate(sourceName, group);
        if (_instance == IntPtr.Zero)
            throw new InvalidOperationException(
                "Der NDI-Sender konnte nicht erstellt werden (NDIlib_send_create lieferte NULL).");
    }

    public void SendVideo(IntPtr bgraData, int width, int height, int stride, int fpsN, int fpsD)
        => NdiNative.SendVideo(_instance, bgraData, width, height, stride, NdiTypes.VideoTypeBGRA, fpsN, fpsD);

    /// <param name="planarData">Planar 32 bit float, channel stride = samples * 4.</param>
    public void SendAudio(IntPtr planarData, int channels, int samples, int sampleRate)
        => NdiNative.SendAudio(_instance, planarData, channels, samples, sampleRate);

    public void Dispose()
    {
        var instance = Interlocked.Exchange(ref _instance, IntPtr.Zero);
        NdiNative.SendDestroy(instance);
    }
}
