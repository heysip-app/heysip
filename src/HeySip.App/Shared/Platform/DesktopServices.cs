using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace HeySip.App.Shared.Platform;

public interface IClipboardService
{
    /// <summary>Puts text on the clipboard; false when no window is available to own it.</summary>
    Task<bool> SetTextAsync(string text);
}

public interface IShellLauncher
{
    /// <summary>Opens a folder in the system file manager; false when it couldn't be opened.</summary>
    bool OpenFolder(string path);

    /// <summary>Opens a web address in the browser; false for anything but https, or when it couldn't be opened.</summary>
    bool OpenUrl(string url);
}

/// <summary>The clipboard and file-manager hooks, kept behind interfaces so tests don't touch the real desktop.</summary>
public sealed class DesktopServices : IClipboardService, IShellLauncher
{
    /// <summary>The window that owns the clipboard. Set once the main window exists.</summary>
    public TopLevel? Owner { get; set; }

    public async Task<bool> SetTextAsync(string text)
    {
        if (Owner?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    public bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public bool OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
