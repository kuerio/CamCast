using System.IO;

namespace CamCast.Services;

/// <summary>Writes log lines to the UI and to %APPDATA%\CamCast\camcast.log.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();

    public static event Action<string>? LineAdded;

    public static string LogFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CamCast",
        "camcast.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("FEHLER", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        try { LineAdded?.Invoke(line); } catch { /* ignore */ }

        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);

                var info = new FileInfo(LogFile);
                if (info.Exists && info.Length > 2_000_000)
                    File.Delete(LogFile);

                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never break the app
        }
    }
}
