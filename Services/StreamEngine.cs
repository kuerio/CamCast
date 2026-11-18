using System.Diagnostics;
using System.Runtime.InteropServices;
using CamCast.Capture;
using CamCast.Interop;

namespace CamCast.Services;

internal sealed record VideoOptions(int DeviceIndex, int Width, int Height, int Fps, string Backend);

internal sealed class StreamStats
{
    public bool Capturing { get; init; }
    public bool Sending { get; init; }
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public double MeasuredFps { get; init; }
    public int DeclaredFps { get; init; }
    public long VideoFrames { get; init; }
    public long AudioBuffers { get; init; }
    public long ReadFailures { get; init; }
    public int Connections { get; init; }
    public string Backend { get; init; } = string.Empty;
    public string NdiSource { get; init; } = string.Empty;
    public string AudioDescription { get; init; } = string.Empty;
    public double AudioLevel { get; init; }
}

/// <summary>
/// Owns the camera capture, the optional audio capture and the NDI(R) sender.
/// Capture and NDI output can be started independently so the camera preview
/// also works without sending anything to the network.
/// </summary>
internal sealed class StreamEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly object _sendLock = new();

    private WebcamCapture? _video;
    private NdiSender? _sender;
    private AudioCaptureService? _audio;
    private Thread? _videoThread;
    private CancellationTokenSource? _cts;

    private int _fpsN = 30;
    private int _fpsD = 1;

    private long _videoFrames;
    private long _audioBuffers;
    private long _readFailures;
    private long _fpsWindowFrames;
    private long _fpsWindowStart = Stopwatch.GetTimestamp();
    private double _measuredFps;
    private int _lastWidth;
    private int _lastHeight;

    public object PreviewSync { get; } = new();
    public byte[]? PreviewBuffer { get; private set; }
    public int PreviewWidth { get; private set; }
    public int PreviewHeight { get; private set; }
    public int PreviewStride { get; private set; }

    public event Action? PreviewUpdated;
    public event Action<string>? Failed;

    public bool ShowPreview { get; set; } = true;
    public bool IsCapturing { get; private set; }
    public bool IsSending { get; private set; }
    public string NdiSourceName { get; private set; } = string.Empty;
    public string AudioDescription { get; private set; } = string.Empty;

    // -- capture ------------------------------------------------------------

    public void StartCapture(VideoOptions options)
    {
        lock (_gate)
        {
            if (IsCapturing) return;

            var video = new WebcamCapture();
            video.Open(options.DeviceIndex, options.Width, options.Height, options.Fps, options.Backend);

            _video = video;
            _fpsN = options.Fps > 0 ? options.Fps : (int)Math.Round(video.ReportedFps);
            if (_fpsN <= 0) _fpsN = 30;
            _fpsD = 1;
            _lastWidth = video.OutputWidth;
            _lastHeight = video.OutputHeight;

            _cts = new CancellationTokenSource();
            _videoThread = new Thread(() => VideoLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "CamCast video"
            };

            IsCapturing = true;
            _videoThread.Start();

            AppLog.Info($"Kamera geöffnet: {video.SourceWidth}x{video.SourceHeight} " +
                        $"({video.BackendName}), Ausgabe {video.OutputWidth}x{video.OutputHeight} @ {_fpsN} fps");
        }
    }

    public void StopCapture()
    {
        CancellationTokenSource? cts;
        Thread? thread;
        WebcamCapture? video;

        lock (_gate)
        {
            cts = _cts;
            thread = _videoThread;
            video = _video;
            _cts = null;
            _videoThread = null;
            _video = null;
            IsCapturing = false;
        }

        try { cts?.Cancel(); } catch { /* ignore */ }

        if (thread is not null && thread.IsAlive)
            thread.Join(TimeSpan.FromSeconds(3));

        video?.Dispose();
        cts?.Dispose();

        lock (PreviewSync)
        {
            PreviewBuffer = null;
            PreviewWidth = PreviewHeight = PreviewStride = 0;
        }

        if (video is not null)
            AppLog.Info("Kamera geschlossen.");
    }

    private void VideoLoop(CancellationToken token)
    {
        const long previewIntervalTicks = TimeSpan.TicksPerSecond / 12;
        var lastPreview = 0L;
        var consecutiveFailures = 0;

        while (!token.IsCancellationRequested)
        {
            WebcamCapture? video;
            lock (_gate) video = _video;
            if (video is null) break;

            try
            {
                if (!video.TryReadFrame(out var data, out var width, out var height, out var stride))
                {
                    Interlocked.Increment(ref _readFailures);
                    consecutiveFailures++;

                    if (consecutiveFailures == 150)
                        Failed?.Invoke("Die Kamera liefert keine Bilder mehr. Bitte Gerät prüfen.");

                    Thread.Sleep(10);
                    continue;
                }

                consecutiveFailures = 0;
                Interlocked.Increment(ref _videoFrames);
                Interlocked.Increment(ref _fpsWindowFrames);
                _lastWidth = width;
                _lastHeight = height;

                var now = DateTime.UtcNow.Ticks;
                if (ShowPreview && now - lastPreview >= previewIntervalTicks)
                {
                    lastPreview = now;
                    PublishPreview(data, width, height, stride);
                }

                lock (_sendLock)
                {
                    _sender?.SendVideo(data, width, height, stride, _fpsN, _fpsD);
                }
            }
            catch (Exception ex)
            {
                Failed?.Invoke("Videoaufnahme fehlgeschlagen: " + ex.Message);
                break;
            }
        }
    }

    private void PublishPreview(IntPtr data, int width, int height, int stride)
    {
        var length = stride * height;

        lock (PreviewSync)
        {
            if (PreviewBuffer is null || PreviewBuffer.Length < length)
                PreviewBuffer = new byte[length];

            PreviewWidth = width;
            PreviewHeight = height;
            PreviewStride = stride;

            Marshal.Copy(data, PreviewBuffer, 0, length);
        }

        PreviewUpdated?.Invoke();
    }

    // -- NDI output ---------------------------------------------------------

    public void StartNdi(string sourceName, string? group, AudioSourceInfo audioSource)
    {
        lock (_sendLock)
        {
            if (IsSending) return;

            if (!NdiNative.IsInitialized)
                throw new InvalidOperationException("Die NDI-Runtime ist nicht verfügbar.");

            var sender = new NdiSender(sourceName, group);
            _sender = sender;
            NdiSourceName = sourceName;
            IsSending = true;

            AppLog.Info($"NDI-Ausgabe gestartet: \"{sourceName}\"" +
                        (string.IsNullOrWhiteSpace(group) ? string.Empty : $", Gruppe \"{group}\""));
        }

        if (audioSource.Kind == AudioSourceKind.None) return;

        try
        {
            var audio = new AudioCaptureService();
            audio.FrameReady += OnAudioFrame;
            audio.Start(audioSource);

            _audio = audio;
            AudioDescription = audio.Description;
            AppLog.Info("Audio aktiv: " + audio.Description);
        }
        catch (Exception ex)
        {
            AudioDescription = string.Empty;
            AppLog.Warn(ex.Message);
        }
    }

    public void StopNdi()
    {
        NdiSender? sender;
        lock (_sendLock)
        {
            sender = _sender;
            _sender = null;
            IsSending = false;
        }

        var audio = _audio;
        _audio = null;
        if (audio is not null)
        {
            audio.FrameReady -= OnAudioFrame;
            audio.Dispose();
        }

        sender?.Dispose();
        AudioDescription = string.Empty;

        if (sender is not null)
            AppLog.Info("NDI-Ausgabe gestoppt.");
    }

    private unsafe void OnAudioFrame(float[] planar, int channels, int frames, int sampleRate)
    {
        lock (_sendLock)
        {
            var sender = _sender;
            if (sender is null) return;

            fixed (float* p = planar)
                sender.SendAudio((IntPtr)p, channels, frames, sampleRate);
        }

        Interlocked.Increment(ref _audioBuffers);
    }

    // -- status -------------------------------------------------------------

    public StreamStats GetStats()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _fpsWindowStart) / (double)Stopwatch.Frequency;

        if (elapsed >= 0.25)
        {
            var frames = Interlocked.Exchange(ref _fpsWindowFrames, 0);
            _measuredFps = frames / elapsed;
            _fpsWindowStart = now;
        }

        var video = _video;
        var audio = _audio;
        int connections;
        lock (_sendLock) connections = _sender?.Connections ?? 0;

        return new StreamStats
        {
            Capturing = IsCapturing,
            Sending = IsSending,
            OutputWidth = _lastWidth,
            OutputHeight = _lastHeight,
            SourceWidth = video?.SourceWidth ?? 0,
            SourceHeight = video?.SourceHeight ?? 0,
            MeasuredFps = _measuredFps,
            DeclaredFps = _fpsN,
            VideoFrames = Interlocked.Read(ref _videoFrames),
            AudioBuffers = Interlocked.Read(ref _audioBuffers),
            ReadFailures = Interlocked.Read(ref _readFailures),
            Connections = connections,
            Backend = video?.BackendName ?? string.Empty,
            NdiSource = NdiSourceName,
            AudioDescription = AudioDescription,
            AudioLevel = audio?.Level ?? 0
        };
    }

    public void Dispose()
    {
        try { StopNdi(); } catch { /* ignore */ }
        try { StopCapture(); } catch { /* ignore */ }
    }
}
