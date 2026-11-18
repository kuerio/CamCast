using OpenCvSharp;

namespace CamCast.Capture;

/// <summary>
/// Opens a webcam through OpenCV and delivers frames converted to BGRA,
/// which is a format the NDI(R) SDK accepts directly for sending.
/// </summary>
internal sealed class WebcamCapture : IDisposable
{
    private VideoCapture? _capture;
    private Mat? _bgr;
    private Mat? _bgra;
    private Mat? _scaled;

    public bool IsOpen => _capture is { } capture && capture.IsOpened();

    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public double ReportedFps { get; private set; } = 30;
    public string BackendName { get; private set; } = string.Empty;

    /// <summary>Target size for the outgoing stream. 0 means "native camera size".</summary>
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }

    public void Open(int deviceIndex, int requestedWidth, int requestedHeight, int requestedFps, string backend)
    {
        Close();

        var order = backend switch
        {
            "DirectShow" => new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY },
            "Media Foundation" => new[] { VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.ANY },
            _ => new[] { VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.ANY }
        };

        Exception? lastError = null;

        foreach (var api in order)
        {
            VideoCapture? candidate = null;
            try
            {
                candidate = new VideoCapture();
                if (!candidate.Open(deviceIndex, api))
                {
                    candidate.Dispose();
                    continue;
                }

                if (requestedWidth > 0) candidate.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
                if (requestedHeight > 0) candidate.Set(VideoCaptureProperties.FrameHeight, requestedHeight);
                if (requestedFps > 0) candidate.Set(VideoCaptureProperties.Fps, requestedFps);
                candidate.Set(VideoCaptureProperties.BufferSize, 1);

                var width = (int)Math.Round(candidate.Get(VideoCaptureProperties.FrameWidth));
                var height = (int)Math.Round(candidate.Get(VideoCaptureProperties.FrameHeight));
                var fps = candidate.Get(VideoCaptureProperties.Fps);

                if (width <= 0 || height <= 0)
                {
                    candidate.Dispose();
                    continue;
                }

                _capture = candidate;
                SourceWidth = width;
                SourceHeight = height;
                ReportedFps = fps is > 0.1 and < 1000 ? fps : 30;
                BackendName = api.ToString();

                OutputWidth = requestedWidth > 0 ? requestedWidth : width;
                OutputHeight = requestedHeight > 0 ? requestedHeight : height;

                _bgr = new Mat();
                _bgra = new Mat();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                candidate?.Dispose();
            }
        }

        throw new InvalidOperationException(
            $"Kamera {deviceIndex} konnte nicht geöffnet werden." +
            (lastError is null ? string.Empty : " " + lastError.Message));
    }

    /// <summary>
    /// Reads the next frame as BGRA. The returned pointer stays valid until the
    /// next call, so it must be consumed before calling this again.
    /// </summary>
    public bool TryReadFrame(out IntPtr data, out int width, out int height, out int stride)
    {
        data = IntPtr.Zero;
        width = height = stride = 0;

        if (_capture is null || _bgr is null || _bgra is null) return false;
        if (!_capture.Read(_bgr) || _bgr.Empty()) return false;

        Cv2.CvtColor(_bgr, _bgra, ColorConversionCodes.BGR2BGRA);

        var mat = _bgra;

        if (OutputWidth > 0 && OutputHeight > 0 &&
            (mat.Width != OutputWidth || mat.Height != OutputHeight))
        {
            _scaled ??= new Mat();
            var downscaling = OutputWidth * OutputHeight < mat.Width * mat.Height;
            var interpolation = downscaling ? InterpolationFlags.Area : InterpolationFlags.Linear;
            Cv2.Resize(mat, _scaled, new Size(OutputWidth, OutputHeight), 0, 0, interpolation);
            mat = _scaled;
        }

        width = mat.Width;
        height = mat.Height;
        stride = (int)mat.Step();
        data = mat.Data;
        return true;
    }

    public void Close()
    {
        try { _capture?.Release(); } catch { /* ignore */ }
        _capture?.Dispose();
        _bgr?.Dispose();
        _bgra?.Dispose();
        _scaled?.Dispose();

        _capture = null;
        _bgr = null;
        _bgra = null;
        _scaled = null;
    }

    public void Dispose() => Close();
}
