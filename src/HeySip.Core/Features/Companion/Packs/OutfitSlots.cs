namespace HeySip.Core.Features.Companion.Packs;

/// <summary>The four places an accessory can be worn, and the names settings use for them.</summary>
public static class OutfitSlots
{
    public static IReadOnlyList<AccessorySlot> All { get; } = [AccessorySlot.Hat, AccessorySlot.Eyes, AccessorySlot.Neck, AccessorySlot.Held];

    /// <summary>The name of a slot in settings and in pack files: "hat", "eyes", "neck" or "held".</summary>
    public static string KeyOf(AccessorySlot slot) => slot.ToString().ToLowerInvariant();

    public static bool IsSlot(string? key) => All.Any(slot => KeyOf(slot) == key);

    /// <summary>What the slot is called on screen.</summary>
    public static string TitleOf(AccessorySlot slot) => slot switch
    {
        AccessorySlot.Hat => "Hat",
        AccessorySlot.Eyes => "Glasses",
        AccessorySlot.Neck => "Scarf and ties",
        _ => "Holding",
    };
}
