using HeySip.App.Features.Companion;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Scheduling;
using HeySip.Tests.Support;

namespace HeySip.Tests.Ui;

public sealed class CompanionViewModelTests : IDisposable
{
    private readonly CoreHarness _core = new();
    private readonly TaskCompletionSource _promptTimeout = new();

    public void Dispose() => _core.Dispose();

    /// <summary>Animations finish instantly; the 2-minute prompt timeout only elapses when the test says so.</summary>
    private Task Delay(TimeSpan span) => span == CompanionViewModel.PromptTimeout ? _promptTimeout.Task : Task.CompletedTask;

    private CompanionViewModel Create() =>
        new(_core.Settings, new MessageBook(new Random(7)), _core.Hydration, Delay);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the companion.");
    }

    [Fact]
    public async Task Asks_then_celebrates_when_the_user_drinks()
    {
        var viewModel = Create();
        var outcomes = new List<ReminderOutcome>();
        var confetti = 0;
        viewModel.Answered += outcomes.Add;
        viewModel.ConfettiRequested += () => confetti++;

        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, Manual: false));
        await WaitUntil(() => viewModel.AreButtonsVisible);
        Assert.False(viewModel.IsOffstage);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.BubbleText));

        viewModel.DrinkCommand.Execute(null);
        await run;

        Assert.Equal([ReminderOutcome.Drank], outcomes);
        Assert.Equal(1, confetti);
        Assert.True(viewModel.IsOffstage);
        Assert.False(viewModel.IsBubbleVisible);
    }

    [Fact]
    public async Task A_greeting_says_hi_with_no_buttons_and_answers_nothing()
    {
        var viewModel = Create();
        var outcomes = new List<ReminderOutcome>();
        var saidHi = false;
        viewModel.Answered += outcomes.Add;
        viewModel.PropertyChanged += (_, _) => saidHi |= viewModel.IsBubbleVisible && !viewModel.AreButtonsVisible && viewModel.Pose == DripPose.Waving && viewModel.BubbleText.Length > 0;

        await viewModel.RunAsync(new Reminder(EscalationLevel.Calm, Manual: true, Greeting: true));

        Assert.True(saidHi);
        Assert.Empty(outcomes);
        Assert.True(viewModel.IsOffstage);
        Assert.Equal(DripPose.Idle, viewModel.Pose);
    }

    [Fact]
    public async Task Changing_character_during_a_greeting_swaps_it_and_restarts_the_hello()
    {
        var holds = new List<TaskCompletionSource>();
        Task Hold(TimeSpan span)
        {
            if (span != TimeSpan.FromMilliseconds(3200))
            {
                return Task.CompletedTask;
            }

            var hold = new TaskCompletionSource();
            lock (holds) { holds.Add(hold); }
            return hold.Task;
        }

        var viewModel = new CompanionViewModel(_core.Settings, new MessageBook(new Random(7)), _core.Hydration, Hold);
        _core.Settings.Update(s => s with { Character = "heysip.axolotl" });
        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, Manual: true, Greeting: true));
        await WaitUntil(() => { lock (holds) { return holds.Count == 1; } });
        Assert.Equal("heysip.axolotl", viewModel.Character);

        _core.Settings.Update(s => s with { Character = "heysip.duckling" });
        viewModel.ChangeLook();
        Assert.Equal("heysip.duckling", viewModel.Character);
        Assert.True(viewModel.IsBubbleVisible);
        Assert.Equal(DripPose.Waving, viewModel.Pose);

        // The first wait ending doesn't end the visit: the new hello gets its own full turn.
        lock (holds) { holds[0].SetResult(); }
        await WaitUntil(() => { lock (holds) { return holds.Count == 2; } });
        Assert.False(run.IsCompleted);
        lock (holds) { holds[1].SetResult(); }
        await run;
        Assert.True(viewModel.IsOffstage);
    }

    [Fact]
    public async Task Changing_character_during_a_reminder_keeps_the_question_and_buttons()
    {
        var viewModel = Create();
        _core.Settings.Update(s => s with { Character = "heysip.axolotl" });
        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, Manual: false));
        await WaitUntil(() => viewModel.AreButtonsVisible);
        var question = viewModel.BubbleText;

        _core.Settings.Update(s => s with { Character = "heysip.duckling" });
        viewModel.ChangeLook();

        Assert.Equal("heysip.duckling", viewModel.Character);
        Assert.Equal(question, viewModel.BubbleText);
        Assert.True(viewModel.AreButtonsVisible);

        viewModel.SnoozeCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task A_new_outfit_on_the_same_character_asks_how_it_looks_instead_of_saying_hi()
    {
        var viewModel = Create();
        string? said = null;
        viewModel.PropertyChanged += (_, _) => { if (viewModel.IsBubbleVisible && viewModel.BubbleText.Length > 0) { said ??= viewModel.BubbleText; } };

        await viewModel.RunAsync(new Reminder(EscalationLevel.Calm, Manual: true, Greeting: true, NewLook: true));

        Assert.NotNull(said);
        Assert.Matches("(?i)look|style|gear|new|small change", said);
        Assert.DoesNotContain("buddy", said);
    }

    [Fact]
    public void Quick_changes_make_one_greeting_until_it_appears()
    {
        var reminders = new ReminderService(_core.Settings, new ReminderScheduler(_core.Time), new AlwaysAvailable(), _core.Time);
        var seen = new List<Reminder>();
        reminders.Due += seen.Add;

        reminders.Greet();
        reminders.Greet(newLook: true);
        Assert.Single(seen);

        // Once it has appeared and gone, the next change greets again.
        reminders.ReminderVisible = true;
        reminders.ReminderVisible = false;
        reminders.Greet(newLook: true);
        Assert.Equal(2, seen.Count);
        Assert.True(seen[1].NewLook);
    }

    [Fact]
    public void Greet_raises_a_greeting_unless_a_reminder_is_already_showing()
    {
        var reminders = new ReminderService(_core.Settings, new ReminderScheduler(_core.Time), new AlwaysAvailable(), _core.Time);
        var seen = new List<Reminder>();
        reminders.Due += seen.Add;

        reminders.Greet();
        reminders.ReminderVisible = true;
        reminders.Greet();

        var greeting = Assert.Single(seen);
        Assert.True(greeting.Greeting);
        Assert.True(greeting.Manual);
    }

    [Fact]
    public async Task Snoozing_reports_the_outcome_without_confetti()
    {
        var viewModel = Create();
        var outcomes = new List<ReminderOutcome>();
        var confetti = 0;
        viewModel.Answered += outcomes.Add;
        viewModel.ConfettiRequested += () => confetti++;

        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, false));
        await WaitUntil(() => viewModel.AreButtonsVisible);
        viewModel.SnoozeCommand.Execute(null);
        await run;

        Assert.Equal([ReminderOutcome.Snoozed], outcomes);
        Assert.Equal(0, confetti);
    }

    [Fact]
    public async Task An_unanswered_reminder_times_out_as_missed()
    {
        var viewModel = Create();
        var outcomes = new List<ReminderOutcome>();
        viewModel.Answered += outcomes.Add;

        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, false));
        await WaitUntil(() => viewModel.AreButtonsVisible);
        _promptTimeout.SetResult();
        await run;

        Assert.Equal([ReminderOutcome.Missed], outcomes);
    }

    [Fact]
    public async Task Escalated_reminders_look_worried()
    {
        var viewModel = Create();

        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Insistent, false));
        await WaitUntil(() => viewModel.AreButtonsVisible);

        Assert.Equal(DripPose.Worried, viewModel.Pose);
        viewModel.SnoozeCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task Uses_the_name_and_character_from_settings()
    {
        _core.Settings.Update(s => s with { Name = "Alex", Character = "heysip.sunny" });
        var viewModel = Create();

        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, false));
        await WaitUntil(() => viewModel.AreButtonsVisible);

        Assert.Equal("heysip.sunny", viewModel.Character);
        Assert.Contains("Alex", viewModel.BubbleText);
        viewModel.SnoozeCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task A_drink_logged_elsewhere_while_asking_is_accepted_as_the_answer()
    {
        var viewModel = Create();
        var outcomes = new List<ReminderOutcome>();
        var confetti = 0;
        viewModel.Answered += outcomes.Add;
        viewModel.ConfettiRequested += () => confetti++;

        var run = viewModel.RunAsync(new Reminder(EscalationLevel.Calm, Manual: false));
        await WaitUntil(() => viewModel.AreButtonsVisible);
        viewModel.AnswerExternally();
        await run;

        Assert.Equal([ReminderOutcome.DrankElsewhere], outcomes);
        Assert.Equal(1, confetti);
        Assert.True(viewModel.IsOffstage);
    }

    [Fact]
    public void Answering_externally_when_nothing_is_showing_is_harmless()
    {
        var viewModel = Create();

        var failure = Record.Exception(viewModel.AnswerExternally);

        Assert.Null(failure);
    }
}
