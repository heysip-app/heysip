using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HeySip.App.Shared.Platform;

namespace HeySip.App.Shell;

public partial class MainWindow : Window
{
    /// <summary>The share of the usable screen a window may take, so it never opens with its edges off the screen.</summary>
    private const double ScreenShare = 0.92;

    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();

        // A pack file can be dropped anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.PickPackFile = PickPackFileAsync;
                shell.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ShellViewModel.Current))
                    {
                        FocusPage(shell);
                    }
                };
            }
        };
    }

    /// <summary>
    /// After a page change, keyboard focus moves to the new page, so Tab starts from its first control and a screen
    /// reader announces where the user landed. It stays put while the welcome or the palette is up.
    /// </summary>
    private void FocusPage(ShellViewModel shell) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && !shell.Onboarding.IsVisible && !shell.Palette.IsOpen)
            {
                PageHost.Focus();
            }
        });

    /// <summary>
    /// The size to open at: the preferred size, but no bigger than the usable part of the screen (in device-independent
    /// units, so a laptop at 150% or 200% scaling gets a smaller window instead of one that spills off the screen), and
    /// never below the window's minimum.
    /// </summary>
    public static Size FitSize(Size preferred, Size minimum, PixelRect usableArea, double scaling)
    {
        if (usableArea.Width <= 0 || usableArea.Height <= 0 || scaling <= 0)
        {
            return preferred;
        }

        var width = Math.Min(preferred.Width, usableArea.Width / scaling * ScreenShare);
        var height = Math.Min(preferred.Height, usableArea.Height / scaling * ScreenShare);
        return new Size(Math.Max(minimum.Width, width), Math.Max(minimum.Height, height));
    }

    private void FitToScreen()
    {
        if (Screens.Primary is not { } screen)
        {
            return;
        }

        var fitted = FitSize(new Size(Width, Height), new Size(MinWidth, MinHeight), screen.WorkingArea, screen.Scaling);
        Width = fitted.Width;
        Height = fitted.Height;
    }

    private async Task<string?> PickPackFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a character or accessory",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("HeySip packs") { Patterns = ["*" + PendingPack.Extension] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static string? PackPathIn(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath())
            .FirstOrDefault(path => path is not null && (path.EndsWith(PendingPack.Extension, StringComparison.OrdinalIgnoreCase) || Directory.Exists(path)));

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = PackPathIn(e) is null ? DragDropEffects.None : DragDropEffects.Copy;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (PackPathIn(e) is { } path && DataContext is ShellViewModel shell)
        {
            shell.OpenPackFile(path);
            e.Handled = true;
        }
    }

    /// <summary>True once the app is really shutting down, so closing the window closes it instead of hiding it.</summary>
    public bool IsQuitting { get; set; }

    /// <summary>When true (close-to-tray), the close button hides the window; when false it quits the app.</summary>
    public bool HideOnClose { get; set; } = true;

    /// <summary>Called when the user closes the window and close-to-tray is off. Must shut the whole app down.</summary>
    public Action? QuitRequested { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // When Windows is signing out or shutting down, or the app is already ending, the window must really close; hiding it
        // instead would make HeySip hold up the shutdown or survive the sign-out.
        var systemIsClosingIt = e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown;
        if (!IsQuitting && !systemIsClosingIt)
        {
            if (HideOnClose)
            {
                e.Cancel = true;
                Hide();
            }
            else if (QuitRequested is not null)
            {
                // The app runs with explicit shutdown (it lives in the tray), so closing the last window would otherwise
                // leave a process with no window and no way to reopen one.
                e.Cancel = true;
                QuitRequested();
            }
        }

        base.OnClosing(e);
    }
}
