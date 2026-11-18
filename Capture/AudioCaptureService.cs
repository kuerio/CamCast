using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CamCast.Capture;

/// <summary>
/// Captures microphone or system (loopback) audio and delivers planar 32 bit
/// float buffers. The buffer handed to <see cref="FrameReady"/> is reused and
/// only valid while the handler is running.
/// </summary>
internal sealed class AudioCaptureService : IDisposable
{
    private readonly object _gate = new();
    private IWaveIn? _capture;
    private float[] _planar = Array.Empty<float>();

    public event Action<float[], int, int, int>? FrameReady;

    public string Description { get; private set; } = string.Empty;
    public string? LastError { get; private set; }
    public double Level { get; private set; }
    public bool IsRunning => _capture is not null;

    public void Start(AudioSourceInfo source)
    {
        Stop();

        if (source.Kind == AudioSourceKind.None) return;

        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(source.Id);

            IWaveIn capture = source.Kind == AudioSourceKind.Loopback
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);

            lock (_gate)
            {
                _capture = capture;
            }

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();

            Description = $"{capture.WaveFormat.SampleRate} Hz, {capture.WaveFormat.Channels} Kanäle, {capture.WaveFormat.BitsPerSample} Bit";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stop();
            throw new InvalidOperationException("Audioaufnahme konnte nicht gestartet werden: " + ex.Message, ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            LastError = e.Exception.Message;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            IWaveIn? capture;
            lock (_gate)
            {
                capture = _capture;
            }

            if (capture is null) return;

            var format = capture.WaveFormat;
            var frames = AudioFormatConverter.ConvertToPlanarFloat(e.Buffer, e.BytesRecorded, format, ref _planar);
            if (frames <= 0) return;

            Level = AudioFormatConverter.ComputeRms(_planar, format.Channels, frames);

            FrameReady?.Invoke(_planar, format.Channels, frames, format.SampleRate);
        }
        catch
        {
            // never let an audio glitch kill the capture thread
        }
    }

    public void Stop()
    {
        IWaveIn? capture;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
        }

        if (capture is null) return;

        try
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
        }
        catch
        {
            // ignore
        }

        try { capture.Dispose(); } catch { /* ignore */ }

        Level = 0;
    }

    public void Dispose() => Stop();
}
