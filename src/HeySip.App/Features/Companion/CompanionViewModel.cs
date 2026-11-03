using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;

namespace HeySip.App.Features.Companion;

/// <summary>
/// One reminder, start to finish: walk in, ask, wait for an answer (or give up), react, walk out.
/// All timing goes through <c>delay</c> so tests can run the whole sequence instantly.
/// </summary>
public sealed partial class CompanionViewModel(
    SettingsService settings,
    MessageBook messages,
    HydrationService hydration,
    Func<TimeSpan, Task>? delay = null) : ViewModelBase
{
    public static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan GreetingTime = TimeSpan.FromMilliseconds(3200);

    private static readonly TimeSpan WalkTime = TimeSpan.FromMilliseconds(1150);

    private readonly Func<TimeSpan, Task> _delay = delay ?? Task.Delay;
    private TaskCompletionSource<ReminderOutcome>? _answer;
    private bool _greeting;
    private bool _helloSaid;
    private int _helloVersion;

    [ObservableProperty]
    public partial bool IsOffstage { get; set; } = true;

    [ObservableProperty]
    public partial bool IsWalking { get; set; }

    [ObservableProperty]
    public partial bool IsFacingRight { get; set; }

    [ObservableProperty]
    public partial bool IsBubbleVisible { get; set; }

    [ObservableProperty]
    public partial bool AreButtonsVisible { get; set; }

    [ObservableProperty]
    public partial string BubbleText { get; set; } = "";

    [ObservableProperty]
    public partial DripPose Pose { get; set; }

    [ObservableProperty]
    public partial string Character { get; set; } = PackId.DefaultCharacter;

    /// <summary>What the character wears, from the outfit in settings.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AccessoryPack> Outfit { get; set; } = [];

    /// <summary>Asks the view to throw confetti.</summary>
    public event Action? ConfettiRequested;

    /// <summary>Raised the moment the user answers (or the reminder times out), before the exit animation.</summary>
    public event Action<ReminderOutcome>? Answered;

    /// <summary>Raised when the user clicks the buddy or its bubble, to bring the main window forward.</summary>
    public event Action? OpenRequested;

    public void RequestOpen() => OpenRequested?.Invoke();

    public async Task RunAsync(Reminder reminder)
    {
        var current = settings.Current;
        Character = current.Character;
        Outfit = CharacterCatalog.Wearing(current.Outfit);
        _answer = new TaskCompletionSource<ReminderOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        _greeting = reminder.Greeting;
        _helloSaid = false;
        await WalkInAsync();
        if (reminder.Greeting)
        {
            // Just a hello: nothing to answer, so nothing is logged and the schedule is left alone.
            Pose = DripPose.Waving;
            Say(Hello(current, reminder.NewLook), withButtons: false);
            _helloSaid = true;
            int seen;
            do
            {
                // Another character chosen meanwhile restarts the hello, so the last choice always gets its full turn.
                seen = _helloVersion;
                await _delay(GreetingTime);
            }
            while (seen != _helloVersion);

            _greeting = false;
            await WalkOutAsync();
            Pose = DripPose.Idle;
            _answer = null;
            return;
        }

        Pose = reminder.Level == EscalationLevel.Calm ? DripPose.Waving : DripPose.Worried;
        Say(messages.Prompt(current.Messages, current.Name, reminder.Level), withButtons: true);

        var outcome = await Task.WhenAny(_answer.Task, _delay(PromptTimeout)) == _answer.Task
            ? _answer.Task.Result
            : ReminderOutcome.Missed;
        _answer = null;
        AreButtonsVisible = false;
        Answered?.Invoke(outcome);

        switch (outcome)
        {
            case ReminderOutcome.Drank:
            case ReminderOutcome.DrankElsewhere:
                await CelebrateAsync(current);
                break;
            case ReminderOutcome.Snoozed:
                Pose = DripPose.Idle;
                Say(messages.Snoozed(current.Name, current.SnoozeMinutes), withButtons: false);
                await _delay(TimeSpan.FromMilliseconds(1400));
                break;
            default:
                Pose = DripPose.Sleepy;
                Say("Maybe later…", withButtons: false);
                await _delay(TimeSpan.FromMilliseconds(1200));
                break;
        }

        await WalkOutAsync();
        Pose = DripPose.Idle;
    }

    /// <summary>
    /// The character in use changed while the buddy is on screen. It becomes the new character and outfit in place; a greeting
    /// also starts over with a hello from the new one, while a reminder keeps its question and buttons.
    /// </summary>
    public void ChangeLook(bool sameCharacter = false)
    {
        var current = settings.Current;
        Character = current.Character;
        Outfit = CharacterCatalog.Wearing(current.Outfit);
        if (_greeting && _helloSaid)
        {
            Pose = DripPose.Waving;
            Say(Hello(current, sameCharacter), withButtons: false);
            _helloVersion++;
        }
    }

    /// <summary>A new character says hi; the same character in a new outfit asks how it looks.</summary>
    private string Hello(AppSettings current, bool newLook) =>
        newLook ? messages.NewLook(current.Messages, current.Name) : messages.Hello(current.Messages, current.Name);

    [RelayCommand]
    private void Drink() => _answer?.TrySetResult(ReminderOutcome.Drank);

    /// <summary>The user logged a drink somewhere else while this reminder was showing; treat it as the answer.</summary>
    public void AnswerExternally() => _answer?.TrySetResult(ReminderOutcome.DrankElsewhere);

    [RelayCommand]
    private void Snooze() => _answer?.TrySetResult(ReminderOutcome.Snoozed);

    private async Task CelebrateAsync(AppSettings current)
    {
        var goalReached = hydration.GoalReachedToday();
        Pose = DripPose.Drinking;
        Say(goalReached
            ? messages.GoalReached(current.Messages, current.Name)
            : messages.Cheer(current.Messages, current.Name), withButtons: false);
        await _delay(TimeSpan.FromMilliseconds(950));
        Pose = goalReached ? DripPose.Cheering : DripPose.Happy; // jumping for joy is for the goal; an ordinary sip gets a smile
        ConfettiRequested?.Invoke();
        await _delay(TimeSpan.FromMilliseconds(1100));
        Say(messages.Goodbye(current.Name), withButtons: false);
        await _delay(TimeSpan.FromMilliseconds(700));
    }

    private async Task WalkInAsync()
    {
        IsBubbleVisible = false;
        AreButtonsVisible = false;
        IsFacingRight = false;
        Pose = DripPose.Idle;
        IsOffstage = true;
        await _delay(TimeSpan.FromMilliseconds(60));
        IsWalking = true;
        IsOffstage = false;
        await _delay(WalkTime);
        IsWalking = false;
    }

    private async Task WalkOutAsync()
    {
        IsBubbleVisible = false;
        IsFacingRight = true;
        IsWalking = true;
        IsOffstage = true;
        await _delay(WalkTime);
        IsWalking = false;
        IsFacingRight = false;
    }

    private void Say(string text, bool withButtons)
    {
        BubbleText = text;
        AreButtonsVisible = withButtons;
        IsBubbleVisible = true;
    }
}
