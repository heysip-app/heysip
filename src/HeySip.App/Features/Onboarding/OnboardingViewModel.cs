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

    public int NameMaxLength => SettingsLimits.NameMaxLength;

    public IReadOnlyList<int> IntervalChoices { get; } = [30, SettingsLimits.IntervalMinutes.Default, 60, 90];

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
    public partial int Interval { get; set; } = SettingsLimits.IntervalMinutes.Default;

    // The steps of the welcome, in order.
    private const int NameStep = 0;
    private const int IntervalStep = 1;
    private const int CharacterStep = 2;
    private const int FirstStep = NameStep;
    private const int LastStep = CharacterStep;

    public bool IsNameStep => Step == NameStep;

    public bool IsIntervalStep => Step == IntervalStep;

    public bool IsCharacterStep => Step == CharacterStep;

    public bool IsLastStep => Step == LastStep;

    public bool CanGoBack => Step > FirstStep;

    public string StepTitle => Step switch
    {
        NameStep => "What should I call you?",
        IntervalStep => "How often should I nudge you?",
        _ => "Pick your buddy",
    };

    public void ShowIfFirstRun() => IsVisible = !_settings.Current.Onboarded;

    /// <summary>Shows the welcome again from the first step, with the values currently in Settings.</summary>
    public void Restart()
    {
        Name = _settings.Current.Name;
        Interval = _settings.Current.IntervalMinutes;
        Choose(_settings.Current.Character);
        Step = FirstStep;
        IsVisible = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (Step < LastStep)
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
        if (Step > FirstStep)
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
