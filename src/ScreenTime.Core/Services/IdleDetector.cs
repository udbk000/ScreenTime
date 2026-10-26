using System.Runtime.InteropServices;
using ScreenTime.Core.Abstractions;

namespace ScreenTime.Core.Services;

/// <summary>
/// Erkennt Benutzerinaktivität über die letzte Maus- oder Tastatureingabe (Win32 GetLastInputInfo)
/// sowie aktive Video-/Audiowiedergabe über die Windows Core Audio API (IAudioMeterInformation).
/// </summary>
public sealed class IdleDetector : IIdleDetector
{
    public TimeSpan GetIdleDuration()
    {
        var lastInput = new LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
        };

        if (!GetLastInputInfo(ref lastInput))
        {
            return TimeSpan.Zero;
        }

        uint currentTick = (uint)Environment.TickCount;
        uint elapsedMs = unchecked(currentTick - lastInput.dwTime);
        return TimeSpan.FromMilliseconds(elapsedMs);
    }

    /// <summary>
    /// Prüft über das Windows Core Audio Peak-Meter, ob gerade Ton (z. B. Video-Stream, Vorlesung, Musik/Film)
    /// ausgegeben wird. Wenn ja, zählt die Bildschirmzeit weiter als aktiv, auch ohne Maus-/Tastatureingabe.
    /// </summary>
    public bool IsMediaOrStreamingActive()
    {
        try
        {
            var clsidEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"); // MMDeviceEnumerator
            var type = Type.GetTypeFromCLSID(clsidEnumerator);
            if (type is null)
            {
                return false;
            }

            object? obj = Activator.CreateInstance(type);
            if (obj is not IMMDeviceEnumerator enumerator)
            {
                return false;
            }

            // eRender = 0, eMultimedia = 1
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice? device);
            if (hr != 0 || device is null)
            {
                return false;
            }

            var iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"); // IAudioMeterInformation
            hr = device.Activate(ref iidMeter, 23, nint.Zero, out object? meterObj);
            if (hr != 0 || meterObj is not IAudioMeterInformation meter)
            {
                return false;
            }

            hr = meter.GetPeakValue(out float peak);
            return hr == 0 && peak > 0.0005f;
        }
        catch
        {
            return false;
        }
    }

    public bool IsIdle(int thresholdSeconds, out TimeSpan currentIdleDuration)
    {
        currentIdleDuration = GetIdleDuration();
        int effectiveThreshold = Math.Max(15, thresholdSeconds);
        return currentIdleDuration.TotalSeconds >= effectiveThreshold;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out nint ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
    }
}
