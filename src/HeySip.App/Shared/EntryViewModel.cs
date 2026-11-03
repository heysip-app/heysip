using CommunityToolkit.Mvvm.Input;
using HeySip.Core.Features.Hydration;

namespace HeySip.App.Shared;

/// <summary>One logged drink as a row: time, amount and a remove button. Used by the Today page, the log tree and day tabs.</summary>
public sealed class EntryViewModel(IntakeEntry entry, TimeZoneInfo zone, Action<long> remove) : ViewModelBase
{
    public string Time { get; } = TimeZoneInfo.ConvertTime(entry.At, zone).ToString("HH:mm");

    public string Amount { get; } = $"{entry.Milliliters} ml";

    public System.Windows.Input.ICommand RemoveCommand { get; } = new RelayCommand(() => remove(entry.Id));
}
