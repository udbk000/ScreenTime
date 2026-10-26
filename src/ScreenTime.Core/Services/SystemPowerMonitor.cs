using Microsoft.Win32;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Services;

/// <summary>
/// Überwacht Sitzungssperre, Abmeldung und Ruhezustand über SystemEvents.
/// </summary>
public sealed class SystemPowerMonitor : ISystemPowerMonitor
{
    private readonly IAppLogger _logger;
    private bool _disposed;

    public bool IsSessionLockedOrSuspended { get; private set; }

    public event EventHandler<InterruptionReason>? SessionInterrupted;
    public event EventHandler? SessionResumed;

    public SystemPowerMonitor(IAppLogger logger)
    {
        _logger = logger;
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            _logger.Warn($"SystemEvents konnten nicht abonniert werden: {ex.Message}");
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                IsSessionLockedOrSuspended = true;
                _logger.Info($"Sitzung unterbrochen ({e.Reason}). Pausiere Erfassung.");
                SessionInterrupted?.Invoke(this, InterruptionReason.SessionLock);
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                IsSessionLockedOrSuspended = false;
                _logger.Info($"Sitzung fortgesetzt ({e.Reason}).");
                SessionResumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            IsSessionLockedOrSuspended = true;
            _logger.Info("System wechselt in den Energiesparmodus. Pausiere Erfassung.");
            SessionInterrupted?.Invoke(this, InterruptionReason.SystemSuspend);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            IsSessionLockedOrSuspended = false;
            _logger.Info("System aus Energiesparmodus aufgewacht.");
            SessionResumed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
