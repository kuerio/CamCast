using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CamCast.Capture;
using CamCast.Interop;
using CamCast.Services;

namespace CamCast;

public partial class MainWindow : Window
{
    private sealed record ResolutionOption(string Label, int Width, int Height)
    {
        public override string ToString() => Label;
    }

    private sealed record FpsOption(string Label, int Value)
    {
        public override string ToString() => Label;
    }

    private static readonly ResolutionOption AutomaticResolution =
        new("Automatisch (Kamera-Standard)", 0, 0);

    private readonly StreamEngine _engine = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _statsTimer;

    private IReadOnlyList<VideoDeviceInfo> _cameras = Array.Empty<VideoDeviceInfo>();
    private IReadOnlyList<AudioSourceInfo> _audioSources = Array.Empty<AudioSourceInfo>();
    private ResolutionOption[] _resolutions = Array.Empty<ResolutionOption>();
    private FpsOption[] _fpsOptions = Array.Empty<FpsOption>();
    private string[] _backends = Array.Empty<string>();

    private IReadOnlyList<CameraFormat>? _cameraFormats;
    private bool _suppressCameraChanged;

    private WriteableBitmap? _previewBitmap;
    private System.Windows.Forms.NotifyIcon? _tray;
    private IntPtr _iconHandle;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();

        PopulateStaticCombos();

        _engine.PreviewUpdated += OnPreviewUpdated;
        _engine.Failed += OnEngineFailed;

        AppLog.LineAdded += OnLogLine;

        CmbCamera.SelectionChanged += OnCameraSelectionChanged;

        ChkAutoStart.Checked += (_, _) => AutoStartService.SetEnabled(true);
        ChkAutoStart.Unchecked += (_, _) => AutoStartService.SetEnabled(false);
        ChkPreview.Checked += (_, _) => _engine.ShowPreview = true;
        ChkPreview.Unchecked += (_, _) => _engine.ShowPreview = false;

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statsTimer.Tick += (_, _) => UpdateStatus();
        _statsTimer.Start();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // ------------------------------------------------------------------ setup

    private void PopulateStaticCombos()
    {
        _resolutions = BuildStaticResolutions();
        CmbResolution.ItemsSource = _resolutions;
        CmbResolution.SelectedIndex = 0;

        _fpsOptions = new[]
        {
            new FpsOption("Automatisch", 0),
            new FpsOption("15 fps", 15),
            new FpsOption("24 fps", 24),
            new FpsOption("25 fps", 25),
            new FpsOption("30 fps", 30),
            new FpsOption("50 fps", 50),
            new FpsOption("60 fps", 60)
        };
        CmbFps.ItemsSource = _fpsOptions;
        CmbFps.SelectedIndex = 0;

        _backends = new[] { "DirectShow", "Media Foundation" };
        CmbBackend.ItemsSource = _backends;
        CmbBackend.SelectedIndex = 0;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            _cameras = await VideoDeviceEnumerator.EnumerateAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Kamera-Enumeration fehlgeschlagen: " + ex.Message);
        }

        CmbCamera.ItemsSource = _cameras;

        _audioSources = AudioDeviceEnumerator.Enumerate();
        CmbAudio.ItemsSource = _audioSources;
        CmbAudio.SelectedIndex = 0;

        ApplyNonCameraSettingsToUi();

        // Kamera aus den Einstellungen wählen, ohne das Format-Neuladen auszulösen.
        _suppressCameraChanged = true;
        if (_cameras.Count > 0)
        {
            var camera = _cameras.FirstOrDefault(c =>
                             !string.IsNullOrWhiteSpace(_settings.VideoDeviceName) &&
                             c.Name == _settings.VideoDeviceName)
                         ?? _cameras[0];
            CmbCamera.SelectedItem = camera;
        }
        _suppressCameraChanged = false;

        await ReloadResolutionsAsync(_settings.Width, _settings.Height);

        SetupTray();

        if (!NdiNative.IsInitialized)
        {
            TxtState.Text = "NDI-Runtime fehlt";
            AppLog.Error("Die NDI-Runtime ist nicht verfügbar. Bitte installieren: https://ndi.video/tools/");
        }

        var minimized = HasArg("--minimized");
        if (minimized) HideToTray();

        if (_settings.AutoStreamOnLaunch || HasArg("--stream"))
            await StartStreamAsync();
    }

    private static bool HasArg(string value) =>
        App.StartupArgs.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

    private void ApplyNonCameraSettingsToUi()
    {
        CmbFps.SelectedItem =
            _fpsOptions.FirstOrDefault(f => f.Value == _settings.Fps) ?? _fpsOptions[0];

        CmbBackend.SelectedItem =
            _backends.Contains(_settings.Backend) ? _settings.Backend : _backends[0];

        if (!string.IsNullOrWhiteSpace(_settings.AudioSourceId))
        {
            var audio = _audioSources.FirstOrDefault(a => a.Id == _settings.AudioSourceId);
            if (audio is not null) CmbAudio.SelectedItem = audio;
        }

        TxtSourceName.Text = string.IsNullOrWhiteSpace(_settings.SourceName)
            ? AppSettings.DefaultSourceName()
            : _settings.SourceName;

        TxtGroup.Text = _settings.Group;
        ChkPreview.IsChecked = _settings.ShowPreview;
        ChkAutoStart.IsChecked = AutoStartService.IsEnabled();
        ChkAutoStream.IsChecked = _settings.AutoStreamOnLaunch;
        ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;

        _engine.ShowPreview = _settings.ShowPreview;
    }

    // ------------------------------------------------------ Kamera / Auflösungen

    private async void OnCameraSelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressCameraChanged) return;

        var current = CmbResolution.SelectedItem as ResolutionOption;
        await ReloadResolutionsAsync(current?.Width ?? 0, current?.Height ?? 0);
    }

    /// <summary>
    /// Füllt die Auflösungsliste mit den Formaten, die die gewählte Kamera wirklich
    /// meldet. Kann das Gerät nicht abgefragt werden, dient die Standardliste als
    /// Rückfallebene.
    /// </summary>
    private async Task ReloadResolutionsAsync(int preferWidth, int preferHeight)
    {
        var device = CmbCamera.SelectedItem as VideoDeviceInfo;
        _cameraFormats = null;

        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            SetResolutionOptions(BuildStaticResolutions(), preferWidth, preferHeight);
            SetResolutionHint("Geräteformate nicht verfügbar – Standardliste wird verwendet.");
            return;
        }

        SetResolutionHint("Formate der Kamera werden abgefragt …");
        CmbResolution.IsEnabled = false;

        try
        {
            _cameraFormats = await CameraFormats.TryQueryAsync(device.Id);
        }
        catch
        {
            _cameraFormats = null;
        }
        finally
        {
            CmbResolution.IsEnabled = true;
        }

        if (_cameraFormats is null || _cameraFormats.Count == 0)
        {
            SetResolutionOptions(BuildStaticResolutions(), preferWidth, preferHeight);
            SetResolutionHint("Kamera meldet keine Formate – Standardliste wird verwendet.");
            AppLog.Warn($"Kamera \"{device.Name}\": Formate konnten nicht abgefragt werden, Standardliste aktiv.");
            return;
        }

        var options = new List<ResolutionOption> { AutomaticResolution };
        options.AddRange(_cameraFormats.Select(f =>
            new ResolutionOption(ResolutionLabel(f.Width, f.Height), f.Width, f.Height)));

        SetResolutionOptions(options, preferWidth, preferHeight);

        var max = _cameraFormats[0];
        SetResolutionHint($"{_cameraFormats.Count} vom Gerät gemeldete Auflösungen " +
                          $"(max. {max.Width} × {max.Height}).");
        AppLog.Info($"Kamera \"{device.Name}\": {_cameraFormats.Count} Formate gemeldet " +
                    $"(max. {max.Width} × {max.Height}).");
    }

    private void SetResolutionOptions(IEnumerable<ResolutionOption> options, int preferWidth, int preferHeight)
    {
        _resolutions = options.ToArray();
        CmbResolution.ItemsSource = _resolutions;

        var match = _resolutions.FirstOrDefault(r => r.Width == preferWidth && r.Height == preferHeight)
                    ?? _resolutions.FirstOrDefault(r => r.Width == 0 && r.Height == 0)
                    ?? _resolutions[0];

        CmbResolution.SelectedItem = match;
    }

    private static ResolutionOption[] BuildStaticResolutions() => new[]
    {
        AutomaticResolution,
        new ResolutionOption("640 × 360", 640, 360),
        new ResolutionOption("640 × 480", 640, 480),
        new ResolutionOption("1280 × 720 (HD)", 1280, 720),
        new ResolutionOption("1920 × 1080 (Full HD)", 1920, 1080),
        new ResolutionOption("2560 × 1440 (QHD)", 2560, 1440),
        new ResolutionOption("3840 × 2160 (4K)", 3840, 2160)
    };

    private static string ResolutionLabel(int width, int height)
    {
        var suffix = (width, height) switch
        {
            (3840, 2160) => " (4K)",
            (2560, 1440) => " (QHD)",
            (1920, 1080) => " (Full HD)",
            (1280, 720) => " (HD)",
            _ => string.Empty
        };

        return $"{width} × {height}{suffix}";
    }

    private void SetResolutionHint(string text)
    {
        if (TxtResolutionHint is not null) TxtResolutionHint.Text = text;
    }

    private void CaptureSettingsFromUi()
    {
        var camera = CmbCamera.SelectedItem as VideoDeviceInfo;
        _settings.VideoDeviceIndex = camera?.Index ?? 0;
        _settings.VideoDeviceName = camera?.Name ?? string.Empty;

        var resolution = CmbResolution.SelectedItem as ResolutionOption;
        _settings.Width = resolution?.Width ?? 0;
        _settings.Height = resolution?.Height ?? 0;

        var fps = CmbFps.SelectedItem as FpsOption;
        _settings.Fps = fps?.Value ?? 0;

        _settings.Backend = CmbBackend.SelectedItem as string ?? _backends[0];

        var audio = CmbAudio.SelectedItem as AudioSourceInfo;
        _settings.AudioSourceId = audio?.Id ?? string.Empty;
        _settings.AudioSourceName = audio?.Name ?? string.Empty;

        _settings.SourceName = TxtSourceName.Text.Trim();
        _settings.Group = TxtGroup.Text.Trim();
        _settings.ShowPreview = ChkPreview.IsChecked == true;
        _settings.AutoStreamOnLaunch = ChkAutoStream.IsChecked == true;
        _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
        _settings.AutoStart = ChkAutoStart.IsChecked == true;
    }

    // ------------------------------------------------------------------ actions

    private VideoOptions BuildVideoOptions()
    {
        CaptureSettingsFromUi();
        return new VideoOptions(
            _settings.VideoDeviceIndex,
            _settings.Width,
            _settings.Height,
            _settings.Fps,
            _settings.Backend);
    }

    private async Task<bool> EnsureCaptureAsync()
    {
        if (_engine.IsCapturing) return true;

        var options = BuildVideoOptions();

        try
        {
            await Task.Run(() => _engine.StartCapture(options));
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Die Kamera konnte nicht geöffnet werden:\n\n" + ex.Message);
            return false;
        }
    }

    private async Task StartStreamAsync()
    {
        if (!NdiNative.IsInitialized)
        {
            ShowError("Die NDI-Runtime wurde nicht gefunden.\n\n" +
                      "Bitte die kostenlose NDI Runtime installieren: https://ndi.video/tools/");
            return;
        }

        if (_engine.IsSending) return;

        if (!await EnsureCaptureAsync())
        {
            UpdateButtonState();
            return;
        }

        var sourceName = TxtSourceName.Text.Trim();
        if (string.IsNullOrWhiteSpace(sourceName)) sourceName = AppSettings.DefaultSourceName();
        var group = TxtGroup.Text.Trim();
        var audio = CmbAudio.SelectedItem as AudioSourceInfo ?? AudioSourceInfo.None;

        try
        {
            _engine.StartNdi(sourceName, group, audio);
        }
        catch (Exception ex)
        {
            ShowError("Die NDI-Ausgabe konnte nicht gestartet werden:\n\n" + ex.Message);
        }

        UpdateButtonState();
    }

    private void StopEverything()
    {
        _engine.StopNdi();
        _engine.StopCapture();
        UpdateButtonState();
    }

    private async void OnStartClick(object sender, RoutedEventArgs e) => await StartStreamAsync();

    private void OnStopClick(object sender, RoutedEventArgs e) => StopEverything();

    private async void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsSending) return;

        if (_engine.IsCapturing)
        {
            _engine.StopCapture();
        }
        else
        {
            await EnsureCaptureAsync();
        }

        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        var capturing = _engine.IsCapturing;
        var sending = _engine.IsSending;

        BtnPreview.Content = capturing && !sending ? "Vorschau stoppen" : "Vorschau starten";
        BtnPreview.IsEnabled = !sending;
        BtnStart.IsEnabled = !sending;
        BtnStop.IsEnabled = capturing || sending;
    }

    // ------------------------------------------------------------------ status

    private void UpdateStatus()
    {
        StreamStats stats;
        try { stats = _engine.GetStats(); }
        catch { return; }

        TxtState.Text = stats.Sending
            ? (stats.Connections > 0 ? $"Sendet – {stats.Connections} Empfänger" : "Sendet – kein Empfänger")
            : stats.Capturing ? "Nur Vorschau" : "Gestoppt";

        TxtResolution.Text = stats.OutputWidth > 0
            ? $"{stats.OutputWidth} × {stats.OutputHeight}  (Kamera {stats.SourceWidth} × {stats.SourceHeight})"
            : "–";

        TxtFps.Text = stats.Capturing
            ? $"{stats.MeasuredFps:0.0} fps  (angekündigt {stats.DeclaredFps})"
            : "–";

        TxtConnections.Text = stats.Connections.ToString();
        TxtBackend.Text = string.IsNullOrEmpty(stats.Backend) ? "–" : stats.Backend;
        TxtAudio.Text = string.IsNullOrEmpty(stats.AudioDescription) ? "Kein Audio" : stats.AudioDescription;
        PrgAudioLevel.Value = Math.Clamp(stats.AudioLevel * 3.0, 0.0, 1.0);
        TxtFrames.Text = stats.VideoFrames.ToString("N0");
        TxtAudioBuffers.Text = stats.AudioBuffers.ToString("N0");
        TxtReadFailures.Text = stats.ReadFailures.ToString("N0");
    }

    private void OnPreviewUpdated()
    {
        var engine = _engine;

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                lock (engine.PreviewSync)
                {
                    var buffer = engine.PreviewBuffer;
                    var width = engine.PreviewWidth;
                    var height = engine.PreviewHeight;
                    var stride = engine.PreviewStride;

                    if (buffer is null || width <= 0 || height <= 0 || stride <= 0) return;

                    if (_previewBitmap is null ||
                        _previewBitmap.PixelWidth != width ||
                        _previewBitmap.PixelHeight != height)
                    {
                        _previewBitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                        PreviewImage.Source = _previewBitmap;
                    }

                    _previewBitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride * height, 0);
                }
            }
            catch
            {
                // preview is best effort
            }
        }, DispatcherPriority.Background);
    }

    private void OnEngineFailed(string message)
    {
        AppLog.Error(message);

        Dispatcher.InvokeAsync(() =>
        {
            if (!_engine.IsSending)
                _engine.StopCapture();

            UpdateButtonState();
        });
    }

    private void OnLogLine(string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (LogBox.Text.Length > 120_000)
                LogBox.Text = LogBox.Text[60_000..];

            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void ShowError(string message)
    {
        AppLog.Error(message.Replace(Environment.NewLine, " "));
        System.Windows.MessageBox.Show(this, message, "CamCast", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnNdiLinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }

        e.Handled = true;
    }

    // ------------------------------------------------------------------ tray

    private void SetupTray()
    {
        _iconHandle = CreateIconHandle();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Fenster anzeigen", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Stream starten", null, async (_, _) =>
        {
            RestoreFromTray();
            await StartStreamAsync();
        });
        menu.Items.Add("Stream stoppen", null, (_, _) => StopEverything());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApplication());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.FromHandle(_iconHandle),
            Text = "CamCast – Webcam als NDI-Quelle",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        try
        {
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                _iconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            // window icon is optional
        }
    }

    private static IntPtr CreateIconHandle()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);

            using var body = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 31, 111, 235));
            graphics.FillEllipse(body, 1, 1, 30, 30);

            using var lens = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            graphics.FillEllipse(lens, 9, 9, 14, 14);

            using var ring = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 31, 111, 235), 2.5f);
            graphics.DrawEllipse(ring, 9, 9, 14, 14);
        }

        return bitmap.GetHicon();
    }

    private void HideToTray()
    {
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && ChkMinimizeToTray.IsChecked == true && _tray is not null)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        Shutdown();
    }

    private void Shutdown()
    {
        _statsTimer.Stop();

        AppLog.LineAdded -= OnLogLine;
        _engine.PreviewUpdated -= OnPreviewUpdated;
        _engine.Failed -= OnEngineFailed;
        _engine.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        CaptureSettingsFromUi();
        _settings.Save();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
