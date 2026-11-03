using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Control;

namespace HeySip.App.Shell;

/// <summary>
/// The bar across the top of the window with a message from the control file: "HeySip 0.2.0 is available", or a notice from
/// the project. It shows the most important one that applies and hasn't been dismissed. Nothing is shown when there is none.
/// </summary>
public sealed partial class NoticeBannerViewModel : ViewModelBase
{
    private readonly ControlService _control;
    private readonly IShellLauncher _launcher;
    private ControlNotice? _current;

    public NoticeBannerViewModel(ControlService control, IShellLauncher launcher)
    {
        _control = control;
        _launcher = launcher;
        _control.Changed += () =>
        {
            // The check runs on a background thread; the bar may only change on the UI thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Refresh();
            }
            else
            {
                Dispatcher.UIThread.Post(Refresh);
            }
        };
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool HasLink { get; set; }

    [ObservableProperty]
    public partial string LinkText { get; set; } = "";

    [ObservableProperty]
    public partial bool CanDismiss { get; set; }

    public void Refresh()
    {
        _current = _control.ActiveNotices().FirstOrDefault();
        IsVisible = _current is not null;
        Text = _current?.Text ?? "";
        HasLink = _current?.Link is not null;
        LinkText = _current is { Id: var id } && (id == "unsupported" || id.StartsWith("update-", StringComparison.Ordinal)) ? "Get it" : "Learn more";
        CanDismiss = _current?.Dismissible == true;
    }

    [RelayCommand]
    private void Open()
    {
        if (_current?.Link is { } link)
        {
            _launcher.OpenUrl(link.AbsoluteUri);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (_current is { Dismissible: true } notice)
        {
            _control.Dismiss(notice.Id);
        }

        Refresh();
    }
}
