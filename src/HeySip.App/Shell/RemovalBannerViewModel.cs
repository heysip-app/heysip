using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Shell;

/// <summary>
/// "Removed 250 ml (08:15). Undo." A removed drink is gone from the database, so for a few seconds this offers to put it
/// back. Removing another drink replaces the offer; only the most recent removal can be undone.
/// </summary>
public sealed partial class RemovalBannerViewModel : ViewModelBase
{
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(8);

    private readonly HydrationService _hydration;
    private readonly SettingsService _settings;
    private readonly TimeProvider _time;
    private IntakeEntry? _removed;
    private int _generation;

    public RemovalBannerViewModel(HydrationService hydration, SettingsService settings, TimeProvider time)
    {
        _hydration = hydration;
        _settings = settings;
        _time = time;
        _hydration.Removed += (_, entry) => Show(entry);
    }

    /// <summary>Starts the countdown that hides the bar. Replaceable so tests don't wait.</summary>
    public Action<Action, TimeSpan> Schedule { get; set; } =
        (hide, after) => Avalonia.Threading.DispatcherTimer.RunOnce(hide, after);

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [RelayCommand]
    private void Undo()
    {
        if (_removed is { } entry)
        {
            _removed = null;
            _hydration.Restore(entry);
        }

        Dismiss();
    }

    [RelayCommand]
    private void Dismiss()
    {
        _generation++;
        IsVisible = false;
        _removed = null;
    }

    private void Show(IntakeEntry entry)
    {
        var zone = ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time);
        var at = TimeZoneInfo.ConvertTime(entry.At, zone);
        _removed = entry;
        Text = $"Removed {entry.Milliliters} ml from {at:HH:mm}";
        IsVisible = true;

        var generation = ++_generation;
        Schedule(() =>
        {
            if (generation == _generation)
            {
                Dismiss();
            }
        }, Visible);
    }
}
