using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CamCast.Interop;
using CamCast.Services;

namespace CamCast;

public partial class App : Application
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupArgs = e.Args;

        base.OnStartup(e);

        NdiNative.TryLoad(out var loadError);
        if (loadError is not null)
            AppLog.Warn("NDI-Runtime: " + loadError);
        else
            AppLog.Info($"NDI-Runtime geladen ({NdiNative.LoadedFrom}), Version {NdiNative.GetVersion()}");

        // Headless diagnostics modes, useful for troubleshooting the network setup.
        if (HasArg("--list-sources"))
        {
            RunListSources();
            Shutdown();
            return;
        }

        if (HasArg("--probe"))
        {
            RunProbe();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Unbehandelte Ausnahme: " + args.Exception);
            MessageBox.Show(args.Exception.Message, "CamCast – Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NdiNative.Shutdown();
        base.OnExit(e);
    }

    private static bool HasArg(string value) =>
        StartupArgs.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

    private static void RunListSources()
    {
        if (!NdiNative.IsInitialized)
        {
            Write("Die NDI-Runtime ist nicht verfügbar.");
            return;
        }

        Write($"NDI {NdiNative.GetVersion()} – suche Quellen (3 s) ...");

        var sources = NdiNative.ListSources(3000);
        if (sources.Count == 0)
        {
            Write("Keine NDI-Quellen gefunden.");
            return;
        }

        foreach (var source in sources)
            Write("QUELLE: " + source);
    }

    private static void RunProbe()
    {
        if (!NdiNative.IsInitialized)
        {
            Write("Die NDI-Runtime ist nicht verfügbar.");
            return;
        }

        var name = ValueOfArg("--probe");

        if (string.IsNullOrWhiteSpace(name))
        {
            Write("Suche Quelle ...");
            var sources = NdiNative.ListSources(3000);
            if (sources.Count == 0)
            {
                Write("Keine NDI-Quellen gefunden.");
                return;
            }

            name = sources[0];
        }

        Write($"Empfange von \"{name}\" (max. 15 s) ...");
        Write(NdiNative.Probe(name!, 15000));
    }

    private static string? ValueOfArg(string key)
    {
        for (var i = 0; i < StartupArgs.Length - 1; i++)
        {
            if (string.Equals(StartupArgs[i], key, StringComparison.OrdinalIgnoreCase))
                return StartupArgs[i + 1];
        }

        return null;
    }

    private static bool _consoleAttached;

    private static void Write(string line)
    {
        if (!_consoleAttached)
        {
            _consoleAttached = true;
            try { AttachConsole(AttachParentProcess); } catch { /* ignore */ }
        }

        try { Console.WriteLine(line); } catch { /* no console attached */ }

        try
        {
            var file = Path.Combine(Path.GetTempPath(), "camcast-sources.txt");
            File.AppendAllText(file, line + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
