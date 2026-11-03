using System.Runtime.InteropServices;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Win32;

namespace HeySip.App.Shared.Platform;

/// <summary>
/// Treats the user as away after a stretch with no keyboard or mouse input, and as busy while presenting or running a
/// full-screen app (games, slideshows, video calls sharing the screen).
/// </summary>
public sealed class WindowsPresenceProbe(TimeSpan idleThreshold) : IPresenceProbe
{
    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;

    public WindowsPresenceProbe()
        : this(TimeSpan.FromMinutes(5))
    {
    }

    public bool IsAwayOrBusy() => OperatingSystem.IsWindows() && (IsIdle() || IsPresentingOrFullScreen());

    private bool IsIdle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return false;
        }

        var idleMs = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(idleMs) >= idleThreshold;
    }

    private static bool IsPresentingOrFullScreen()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0
                && state is QunsBusy or QunsRunningD3dFullScreen or QunsPresentationMode;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}

/// <summary>Start-with-Windows through the per-user Run key (no admin rights needed).</summary>
public sealed class WindowsStartupRegistration(string executablePath) : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HeySip";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
