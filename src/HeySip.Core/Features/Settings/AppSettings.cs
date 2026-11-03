using System.Text.Json.Serialization;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Core.Features.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Everything the user can configure. Immutable; <see cref="SettingsService.Update"/> swaps the whole value.</summary>
public sealed record AppSettings
{
    /// <summary>The settings-file layout this build writes. Raise it (and add a step to <see cref="SettingsMigrations"/>) when a setting changes meaning.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>Which layout the file was written with; 0 means it was written before versions existed.</summary>
    public int SchemaVersion { get; init; }

    public string Name { get; init; } = "";

    public int IntervalMinutes { get; init; } = 45;

    public int SnoozeMinutes { get; init; } = 10;

    public TimeOnly ActiveStart { get; init; } = new(10, 0);

    public TimeOnly ActiveEnd { get; init; } = new(23, 0);

    public DayOfWeek[] ActiveDays { get; init; } = Enum.GetValues<DayOfWeek>();

    /// <summary>
    /// The days that count as the weekend, whichever they are for this user: none, one, two or more. The schedule presets
    /// that skip the weekend ("Office", "Weekends off") leave these days out. Saturday and Sunday until changed.
    /// </summary>
    public DayOfWeek[] WeekendDays { get; init; } = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>
    /// What each character wears: character id, then at most one accessory per slot, as slot name ("hat", "eyes", "neck",
    /// "held") to the accessory pack's id. Each character keeps its own outfit. A character that wears nothing has no entry.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Outfits { get; init; } = [];

    /// <summary>The outfit one file layout 2 kept for everybody. Only read so <see cref="SettingsMigrations"/> can move it into <see cref="Outfits"/>; never written.</summary>
    [JsonPropertyName("Outfit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LegacyOutfit { get; init; }

    /// <summary>What the character in use wears: slot name to accessory id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Outfit => OutfitOf(Character);

    /// <summary>What this character wears, whether or not it is the one in use.</summary>
    public IReadOnlyDictionary<string, string> OutfitOf(string character) =>
        Outfits.TryGetValue(character, out var outfit) ? outfit : NoOutfit;

    private static readonly IReadOnlyDictionary<string, string> NoOutfit = new Dictionary<string, string>();

    /// <summary>IANA/Windows time zone id used for active hours and day boundaries; null means the machine's zone.</summary>
    public string? TimeZoneId { get; init; }

    public int DailyGoalMilliliters { get; init; } = 2000;

    public int GlassMilliliters { get; init; } = 250;

    /// <summary>Ignored reminders come back sooner and more insistently.</summary>
    public bool Escalate { get; init; } = true;

    /// <summary>Once today's goal is met, reminders come three times less often and never escalate.</summary>
    public bool RemindLessAfterGoal { get; init; } = true;

    /// <summary>Hold reminders while the user is idle, presenting, or in a full-screen app.</summary>
    public bool PauseWhenAway { get; init; } = true;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>The id of the chosen character's pack, such as <c>heysip.drip</c>. Before layout 2 this was one of four names.</summary>
    public string Character { get; init; } = PackId.DefaultCharacter;

    public MessageStyle Messages { get; init; } = MessageStyle.Friendly;

    /// <summary>1-based monitor number the companion appears on; 1 is the primary display.</summary>
    public int Monitor { get; init; } = 1;

    /// <summary>Play a chime when a reminder appears and a gulp when a drink is logged.</summary>
    public bool SoundEnabled { get; init; } = true;

    /// <summary>How loud, from 0 to 100. Gentle by default: reminders should nudge, not startle.</summary>
    public int SoundVolume { get; init; } = 40;

    /// <summary>Whether the Characters page may look on the internet for new characters. Only matters once an online list is set up.</summary>
    public bool OnlineCharacters { get; init; } = true;

    public bool CloseToTray { get; init; } = true;

    public bool StartWithOs { get; init; }

    public bool Onboarded { get; init; }

    /// <summary>Clamps every value into a safe range so hand-edited or corrupt files can't break scheduling.</summary>
    public AppSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        Name = TrimName(Name),
        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 720),
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 120),
        DailyGoalMilliliters = Math.Clamp(DailyGoalMilliliters, 250, 10_000),
        GlassMilliliters = Math.Clamp(GlassMilliliters, 50, 2_000),
        Monitor = Math.Max(1, Monitor),
        SoundVolume = Math.Clamp(SoundVolume, 0, 100),
        // An empty list is a real choice (no reminders on any day); only a missing list falls back to every day.
        ActiveDays = ((DayOfWeek[]?)ActiveDays)?.Distinct().Order().ToArray() ?? Enum.GetValues<DayOfWeek>(),
        // As above, an empty list is a real choice (a week with no weekend); only a missing list falls back to Saturday and Sunday.
        WeekendDays = ((DayOfWeek[]?)WeekendDays)?.Distinct().Order().ToArray() ?? [DayOfWeek.Saturday, DayOfWeek.Sunday],
        Character = PackId.IsValid(Character) ? Character : PackId.DefaultCharacter,
        Outfits = CleanOutfits(Outfits),
        LegacyOutfit = null,
        Messages = Enum.IsDefined(Messages) ? Messages : MessageStyle.Friendly,
        Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
    };

    /// <summary>Value equality that compares the lists and the outfit by content (record equality would compare the references).</summary>
    public bool SameAs(AppSettings other) =>
        this with { ActiveDays = other.ActiveDays, WeekendDays = other.WeekendDays, Outfits = other.Outfits } == other
        && ActiveDays.SequenceEqual(other.ActiveDays)
        && WeekendDays.SequenceEqual(other.WeekendDays)
        && Outfits.Count == other.Outfits.Count
        && Outfits.All(item => other.Outfits.TryGetValue(item.Key, out var theirs)
            && item.Value.Count == theirs.Count
            && item.Value.All(slot => theirs.TryGetValue(slot.Key, out var id) && id == slot.Value));

    /// <summary>These settings with one slot of the character in use changed: an accessory id puts it on, null takes the slot's accessory off.</summary>
    public AppSettings WithAccessory(AccessorySlot slot, string? accessoryId) => WithAccessory(Character, slot, accessoryId);

    /// <summary>These settings with one slot of the given character changed, whether or not it is the one in use.</summary>
    public AppSettings WithAccessory(string character, AccessorySlot slot, string? accessoryId)
    {
        var outfit = new Dictionary<string, string>(OutfitOf(character));
        var key = OutfitSlots.KeyOf(slot);
        if (accessoryId is null)
        {
            outfit.Remove(key);
        }
        else
        {
            outfit[key] = accessoryId;
        }

        var outfits = new Dictionary<string, Dictionary<string, string>>(Outfits);
        if (outfit.Count == 0)
        {
            outfits.Remove(character);
        }
        else
        {
            outfits[character] = outfit;
        }

        return this with { Outfits = outfits };
    }

    /// <summary>Only real characters and slots with valid pack ids stay, so a hand-edited or corrupt file can't break an outfit. A character left wearing nothing is dropped.</summary>
    private static Dictionary<string, Dictionary<string, string>> CleanOutfits(Dictionary<string, Dictionary<string, string>>? outfits) =>
        (outfits ?? [])
            .Where(item => PackId.IsValid(item.Key))
            .Select(item => (Character: item.Key, Outfit: CleanOutfit(item.Value)))
            .Where(item => item.Outfit.Count > 0)
            .ToDictionary(item => item.Character, item => item.Outfit);

    private static Dictionary<string, string> CleanOutfit(Dictionary<string, string>? outfit) =>
        (outfit ?? [])
            .Where(item => OutfitSlots.IsSlot(item.Key) && PackId.IsValid(item.Value))
            .ToDictionary(item => item.Key, item => item.Value);

    private static string TrimName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length > 24 ? trimmed[..24] : trimmed;
    }
}
