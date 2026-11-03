using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using HeySip.App.Shared.Platform;

namespace HeySip.App.Shell;

/// <summary>
/// Shown instead of the app when it can't open its data (a locked or damaged database, a read-only folder).
/// Built in code because it must work when nothing else, including the composition root, can be trusted.
/// </summary>
public sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(string reason, string dataFolder, string details, Action? retry = null)
    {
        Title = "HeySip couldn't start";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var copied = new TextBlock { Opacity = 0.7, IsVisible = false, Text = "Copied. You can paste it into a bug report." };

        var copy = new Button { Content = "Copy details" };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(details);
                copied.IsVisible = true;
            }
        };

        var open = new Button { Content = "Open data folder" };
        open.Click += (_, _) => new DesktopServices().OpenFolder(dataFolder);

        var tryAgain = new Button { Content = "Try again", IsVisible = retry is not null };
        tryAgain.Click += (_, _) => retry?.Invoke();

        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "HeySip couldn't open its data", FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Your drink history and settings are stored on this computer, and HeySip couldn't read them just now. "
                        + "Nothing has been deleted. Closing other copies of HeySip or restarting your computer often fixes this.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontStyle = FontStyle.Italic },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { tryAgain, copy, open, close } },
                copied,
            },
        };
    }
}
