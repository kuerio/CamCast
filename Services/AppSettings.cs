using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CamCast.Services;

internal sealed class AppSettings
{
    public int VideoDeviceIndex { get; set; }
    public string VideoDeviceName { get; set; } = string.Empty;
    public string Backend { get; set; } = "DirectShow";

    /// <summary>0 = native camera resolution.</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>0 = automatic (use the camera frame rate).</summary>
    public int Fps { get; set; }

    public string AudioSourceId { get; set; } = string.Empty;
    public string AudioSourceName { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    public bool AutoStart { get; set; }
    public bool AutoStreamOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowPreview { get; set; } = true;

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CamCast");

    private static string FilePath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Einstellungen konnten nicht gelesen werden: " + ex.Message);
        }

        var settings = new AppSettings();
        settings.SourceName = DefaultSourceName();
        return settings;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Einstellungen konnten nicht gespeichert werden: " + ex.Message);
        }
    }

    public static string DefaultSourceName()
    {
        var machine = Environment.MachineName;
        return $"{machine} (Webcam)";
    }
}

internal static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CamCast";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (key is null) return;

            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                try { exe = Process.GetCurrentProcess().MainModule?.FileName; }
                catch { /* ignore */ }
            }

            if (string.IsNullOrWhiteSpace(exe))
            {
                AppLog.Warn("Autostart konnte nicht gesetzt werden (Programmpfad unbekannt).");
                return;
            }

            key.SetValue(ValueName, $"\"{exe}\" --minimized");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Autostart konnte nicht geändert werden: " + ex.Message);
        }
    }
}
