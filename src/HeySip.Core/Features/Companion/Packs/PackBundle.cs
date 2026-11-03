using System.Text.Json;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>
/// Remembers which accessories came with an installed character, in <c>characters\&lt;id&gt;\bundle.json</c>. It sits beside the
/// version folders, so the scan ignores it, a new version of the character keeps it, and uninstalling the character deletes
/// it with everything else. Both ends are best effort: it only decides which accessories are listed as "included".
/// </summary>
public static class PackBundle
{
    public const string FileName = "bundle.json";

    /// <summary>The most accessories one character can come with.</summary>
    public const int MaxAccessories = 16;

    private const int MaxFileBytes = 8 * 1024;

    /// <summary>Writes the list; an empty list removes it, so a character that no longer comes with accessories says so.</summary>
    public static void Save(string directory, string characterId, IReadOnlyList<string> accessoryIds)
    {
        if (!PackId.IsValid(characterId))
        {
            return;
        }

        var folder = Path.Combine(directory, characterId);
        var path = Path.Combine(folder, FileName);
        try
        {
            if (accessoryIds.Count == 0)
            {
                File.Delete(path);
                return;
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(new { accessories = accessoryIds }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The accessories are installed either way; they would just be listed with the extras.
        }
    }

    /// <summary>
    /// What a character wears when it is new: the first accessory of the set for each slot, since only one fits in a slot.
    /// The order follows the slots (hat, glasses, scarf, held); within a slot, the order the set lists them in.
    /// </summary>
    public static IReadOnlyList<AccessoryPack> DefaultLook(IReadOnlyList<AccessoryPack> accessories) =>
        [.. OutfitSlots.All.Select(slot => accessories.FirstOrDefault(accessory => accessory.Slot == slot)).OfType<AccessoryPack>()];

    /// <summary>The ids saved for this character, or none when there is no file or it isn't what was written.</summary>
    public static IReadOnlyList<string> Read(string directory, string characterId)
    {
        if (!PackId.IsValid(characterId))
        {
            return [];
        }

        var path = Path.Combine(directory, characterId, FileName);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes)
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("accessories", out var list)
                || list.ValueKind != JsonValueKind.Array
                || list.GetArrayLength() > MaxAccessories)
            {
                return [];
            }

            var ids = new List<string>();
            foreach (var element in list.EnumerateArray())
            {
                var id = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                if (!PackId.IsValid(id) || ids.Contains(id!))
                {
                    return [];
                }

                ids.Add(id!);
            }

            return ids;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
