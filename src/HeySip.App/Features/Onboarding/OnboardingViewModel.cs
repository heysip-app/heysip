using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Features.Settings;
using HeySip.App.Shared;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Settings;

namespace HeySip.App.Features.Onboarding;

/// <summary>The three-step first-run welcome: name, reminder interval, companion.</summary>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    public OnboardingViewModel(SettingsService settings)
    {
        _settings = settings;
        Characters = CharacterCatalog.All.Select(entry => new CharacterOptionViewModel(entry.Id, entry.Name, Choose)).ToList();
        Choose(settings.Current.Character);
        Interval = settings.Current.IntervalMinutes;
        Name = settings.Current.Name;
    }

    public IReadOnlyList<CharacterOptionViewModel> Characters { get; }

    public IReadOnlyList<int> IntervalChoices { get; } = [30, 45, 60, 90];

    /// <summary>Raised once, after the user finishes (or skips) the welcome.</summary>
    public event Action? Completed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameStep), nameof(IsIntervalStep), nameof(IsCharacterStep), nameof(IsLastStep), nameof(CanGoBack), nameof(StepTitle))]
    public partial int Step { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial int Interval { get; set; } = 45;

    public bool IsNameStep => Step == 0;

    public bool IsIntervalStep => Step == 1;

    public bool IsCharacterStep => Step == 2;

    public bool IsLastStep => Step == 2;

    public bool CanGoBack => Step > 0;

    public string StepTitle => Step switch
    {
        0 => "What should I call you?",
        1 => "How often should I nudge you?",
        _ => "Pick your buddy",
    };

    public void ShowIfFirstRun() => IsVisible = !_settings.Current.Onboarded;

    /// <summary>Shows the welcome again from the first step, with the values currently in Settings.</summary>
    public void Restart()
    {
        Name = _settings.Current.Name;
        Interval = _settings.Current.IntervalMinutes;
        Choose(_settings.Current.Character);
        Step = 0;
        IsVisible = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (Step < 2)
        {
            Step++;
        }
        else
        {
            Finish();
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private void Skip() => Finish();

    [RelayCommand]
    private void PickInterval(int minutes) => Interval = minutes;

    private void Choose(string id)
    {
        foreach (var option in Characters)
        {
            option.IsSelected = option.Id == id;
        }
    }

    private void Finish()
    {
        var character = Characters.FirstOrDefault(option => option.IsSelected)?.Id ?? PackId.DefaultCharacter;
        _settings.Update(current => current with
        {
            Name = Name,
            IntervalMinutes = Interval,
            Character = character,
            Onboarded = true,
        });
        IsVisible = false;
        Completed?.Invoke();
    }
}
