using Microsoft.Win32;
using ScreenTime.Core.Abstractions;

namespace ScreenTime.Core.Services;

/// <summary>
/// Verwaltet den optionalen Autostart der Anwendung mit Windows über die Benutzer-Registry.
/// </summary>
public sealed class StartupManager
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "ScreenTime";
    private readonly IAppLogger _logger;

    public StartupManager(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            return key?.GetValue(AppRegistryName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Autostart-Status konnte nicht geprüft werden: {ex.Message}");
            return false;
        }
    }

    public void SetStartupEnabled(bool enabled, string? executablePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                string exe = executablePath ?? Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    key.SetValue(AppRegistryName, $"\"{exe}\" --minimized");
                    _logger.Info("Autostart mit Windows aktiviert.");
                }
            }
            else
            {
                if (key.GetValue(AppRegistryName) is not null)
                {
                    key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
                    _logger.Info("Autostart mit Windows deaktiviert.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Fehler beim Ändern der Autostart-Einstellung", ex);
        }
    }
}
