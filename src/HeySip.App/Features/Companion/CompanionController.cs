using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.App.Features.Companion;

/// <summary>Owns the companion window: shows it when a reminder is due and applies the user's answer.</summary>
public sealed class CompanionController(
    ReminderService reminders,
    SettingsService settings,
    HydrationService hydration,
    CompanionViewModel viewModel,
    DiagnosticLog log)
{
    private const int EdgeMargin = 8;

    private CompanionWindow? _window;
    private Reminder? _current;

    /// <summary>Set by the app: brings the main window forward on Today when the user clicks the buddy.</summary>
    public Action? OpenMain { get; set; }

    public void Attach()
    {
        viewModel.OpenRequested += () => OpenMain?.Invoke();
        reminders.Due += reminder => Dispatcher.UIThread.Post(() => _ = ShowSafelyAsync(reminder));
        viewModel.Answered += OnAnswered;

        // Choosing another character brings it over to say hi, so the user sees how it looks without a test reminder.
        // The same goes for putting something on or taking it off the character in use.
        var lastCharacter = settings.Current.Character;
        var lastOutfit = settings.Current.Outfit;
        settings.Changed += (_, updated) =>
        {
            var characterChanged = updated.Character != lastCharacter;
            var outfitChanged = !characterChanged && !SameOutfit(updated.Outfit, lastOutfit);
            lastCharacter = updated.Character;
            lastOutfit = updated.Outfit;
            if ((characterChanged || outfitChanged) && updated.Onboarded)
            {
                // Settings may change on any thread; the buddy is only touched on the UI thread.
                Dispatcher.UIThread.Post(() =>
                {
                    if (reminders.ReminderVisible)
                    {
                        // Already on screen: show the new look right there instead of waiting or skipping it.
                        viewModel.ChangeLook(sameCharacter: outfitChanged);
                    }
                    else
                    {
                        reminders.Greet(newLook: outfitChanged);
                    }
                });
            }
        };

        // A drink logged anywhere else (main window, tray) while the companion is asking counts as the answer.
        hydration.DrinkLogged += (_, _) =>
        {
            if (reminders.ReminderVisible)
            {
                viewModel.AnswerExternally();
            }
        };
    }

    private static bool SameOutfit(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

    /// <summary>The companion is fire-and-forget from the timer, so every failure must be caught and logged here.</summary>
    private async Task ShowSafelyAsync(Reminder reminder)
    {
        try
        {
            await ShowAsync(reminder);
        }
        catch (Exception exception)
        {
            log.Error("Companion", "The reminder window failed.", exception);
        }
    }

    private async Task ShowAsync(Reminder reminder)
    {
        if (reminders.ReminderVisible)
        {
            return;
        }

        reminders.ReminderVisible = true;
        _current = reminder;
        try
        {
            var window = _window ??= new CompanionWindow { DataContext = viewModel };
            window.Show();
            Place(window);
            await viewModel.RunAsync(reminder);
        }
        finally
        {
            // Whatever happened, never leave an invisible, always-on-top window sitting on the user's screen.
            _window?.Hide();
            _current = null;
            reminders.ReminderVisible = false;
        }
    }

    private void OnAnswered(ReminderOutcome outcome)
    {
        try
        {
            switch (outcome)
            {
                case ReminderOutcome.Drank:
                    // Logging the drink also answers the reminder (the reminder service listens for it).
                    hydration.LogGlass();
                    break;
                case ReminderOutcome.DrankElsewhere:
                    break;
                default:
                    // A reminder the user asked for ("Remind me now", a test) shouldn't change the real schedule when
                    // it is snoozed or ignored.
                    if (_current is { Manual: false })
                    {
                        reminders.Resolve(outcome);
                    }

                    break;
            }
        }
        catch (Exception exception)
        {
            // For example the log can't be written. The animation should still finish.
            log.Error("Companion", "Couldn't record the answer to a reminder.", exception);
        }
    }

    /// <summary>Docks the window to the bottom-right of the chosen monitor's work area (clear of the taskbar).</summary>
    private void Place(Window window)
    {
        var screens = window.Screens;
        var chosen = screens.All.ElementAtOrDefault(settings.Current.Monitor - 1) ?? screens.Primary ?? screens.All.FirstOrDefault();
        if (chosen is null)
        {
            return;
        }

        var scale = chosen.Scaling;
        var area = chosen.WorkingArea;
        window.Position = new PixelPoint(
            area.Right - (int)((window.Width + EdgeMargin) * scale),
            area.Bottom - (int)((window.Height + EdgeMargin) * scale));
    }
}
