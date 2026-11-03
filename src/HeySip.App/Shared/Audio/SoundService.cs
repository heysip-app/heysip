using Avalonia.Platform;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;

namespace HeySip.App.Shared.Audio;

/// <summary>
/// Decides when a sound plays and which one. A chime when a reminder appears, a gulp when a drink is logged. The user's
/// settings rule (see <see cref="SoundPolicy"/>); the character may bring its own sounds, which replace the built-in ones
/// but never change when they play.
/// </summary>
public sealed class SoundService(SettingsService settings, ReminderService reminders, HydrationService hydration, ISoundOutput output)
{
    private readonly Dictionary<string, byte[]?> _cache = [];
    private readonly Lock _gate = new();

    /// <summary>Starts listening for reminders and drinks. Called once, when the app starts.</summary>
    public void Attach()
    {
        // A reminder that appears while another is still showing does not get a second chime.
        reminders.Due += _ =>
        {
            if (!reminders.ReminderVisible)
            {
                Play(SoundKind.Reminder);
            }
        };

        // Only drinks logged now: filling in a forgotten drink for earlier today is not a sip to celebrate.
        hydration.DrinkLogged += (_, _) => Play(SoundKind.Drank);

        // An installed pack may have been replaced or removed, so what was read from it may be out of date.
        CharacterCatalog.Changed += () =>
        {
            lock (_gate)
            {
                _cache.Clear();
            }
        };
    }

    /// <summary>Plays the sound for a moment, if the user's settings allow it right now. Returns whether it started.</summary>
    public bool Play(SoundKind kind) => PlayCore(kind, respectPause: true);

    /// <summary>
    /// The "test sound" button: plays the reminder chime at the chosen volume so the user can set it. It still obeys the
    /// switch and the volume, but it is the user asking, so a pause doesn't silence it.
    /// </summary>
    public bool PlayTest() => PlayCore(SoundKind.Reminder, respectPause: false);

    private bool PlayCore(SoundKind kind, bool respectPause)
    {
        var current = settings.Current;
        if (!current.SoundEnabled || current.SoundVolume <= 0)
        {
            return false;
        }

        if (respectPause && !SoundPolicy.ShouldPlay(current, reminders.Status()))
        {
            return false;
        }

        var gain = SoundPolicy.Gain(current);

        // The character's own sound first; if it can't be played (it passed the checks but the player can't use it), the
        // built-in one, so a faulty pack never means silence.
        if (OwnWav(kind, current) is { } own && output.Play(own, gain))
        {
            return true;
        }

        return BuiltInWav(kind) is { } builtIn && output.Play(builtIn, gain);
    }

    /// <summary>The character's own sound, if its pack has one that is still good.</summary>
    private byte[]? OwnWav(SoundKind kind, AppSettings current)
    {
        var entry = CharacterCatalog.Find(current.Character);
        var name = kind == SoundKind.Reminder ? entry.Pack.Sounds.Reminder : entry.Pack.Sounds.Drank;
        if (entry.Folder is null || name is null)
        {
            return null;
        }

        var path = Path.Combine(entry.Folder, name.Replace('/', Path.DirectorySeparatorChar));
        return Cached(path, () => ReadPackSound(path, name));
    }

    private byte[]? BuiltInWav(SoundKind kind)
    {
        var fileName = kind == SoundKind.Reminder ? "chime.wav" : "gulp.wav";
        return Cached("builtin:" + fileName, () => ReadBuiltIn(fileName));
    }

    /// <summary>Reads a sound once. A failure is not remembered, so a file that was busy a moment ago is tried again next time.</summary>
    private byte[]? Cached(string key, Func<byte[]?> read)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var wav))
            {
                return wav;
            }

            wav = read();
            if (wav is not null)
            {
                _cache[key] = wav;
            }

            return wav;
        }
    }

    private static byte[]? ReadPackSound(string path, string name)
    {
        try
        {
            var data = File.ReadAllBytes(path);

            // The pack was checked when it was installed; the file could have changed since, so it is checked again.
            return SoundCheck.Validate(name, data) is null ? SoundDecoder.ToWav(name, data) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? ReadBuiltIn(string fileName)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://HeySip/Assets/Sounds/{fileName}"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
