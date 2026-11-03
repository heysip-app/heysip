using Avalonia.Platform;
using HeySip.App.Shared;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.App.Shared.Controls;

/// <summary>A character the user can choose: a built-in one, or one installed from a pack.</summary>
/// <param name="Accessories">The ids of the accessories that came with it when it was installed from the list. None for a built-in character or one from a file.</param>
internal sealed record CharacterEntry(string Id, string Name, CharacterPack Pack, bool BuiltIn, string? Folder = null, IReadOnlyList<string>? Accessories = null)
{
    public IReadOnlyList<string> Included => Accessories ?? [];
}

/// <summary>An accessory the user can wear: a built-in one, or one installed from a pack.</summary>
internal sealed record AccessoryEntry(string Id, string Name, AccessorySlot Slot, AccessoryPack Pack, bool BuiltIn);

/// <summary>
/// Every character and accessory available right now: the ones built into the app, then any installed packs. Anything
/// that needs to turn an id (from settings) into something drawable goes through here. A character id that can't be
/// found (an uninstalled pack, say) becomes the default character, and an accessory that can't be found is simply not worn.
/// </summary>
internal static class CharacterCatalog
{
    private static readonly string[] BuiltInIds =
        ["heysip.drip", "heysip.minty", "heysip.sunny", "heysip.blossom", "heysip.axolotl", "heysip.duckling"];

    private static readonly string[] BuiltInAccessoryIds =
    [
        "heysip.party-hat", "heysip.crown", "heysip.beanie",
        "heysip.round-glasses", "heysip.sunglasses",
        "heysip.scarf", "heysip.bow-tie",
        "heysip.balloon", "heysip.flower",
    ];

    private static readonly PackLoadOptions BuiltInOptions = new(new Version(1, 0), BuiltIn: true);
    private static readonly Lock Gate = new();

    private sealed record Snapshot(IReadOnlyList<CharacterEntry> Characters, IReadOnlyList<AccessoryEntry> Accessories);

    private static Snapshot? _snapshot;
    private static string? _installedDirectory;
    private static DiagnosticLog? _log;

    /// <summary>Raised after the lists changed (a pack was installed or removed), so pickers can rebuild.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<CharacterEntry> All => Current.Characters;

    public static IReadOnlyList<AccessoryEntry> Accessories => Current.Accessories;

    public static CharacterEntry Default => All[0];

    /// <summary>Goes up every time the lists are rebuilt, so something that keeps a result can tell when it is out of date.</summary>
    public static int Version { get; private set; }

    private static Snapshot Current
    {
        get
        {
            lock (Gate)
            {
                return _snapshot ??= Build();
            }
        }
    }

    public static bool Contains(string? id) => All.Any(entry => entry.Id == id);

    /// <summary>The character with this id, or the default one when there is none.</summary>
    public static CharacterEntry Find(string? id) => All.FirstOrDefault(entry => entry.Id == id) ?? Default;

    public static AccessoryEntry? FindAccessory(string? id) => Accessories.FirstOrDefault(entry => entry.Id == id);

    public static IReadOnlyList<AccessoryEntry> AccessoriesFor(AccessorySlot slot) =>
        [.. Accessories.Where(entry => entry.Slot == slot)];

    /// <summary>
    /// The accessories a saved outfit means, one per slot. One that isn't installed, or whose pack is for a different slot
    /// than the one it was saved under, is left out.
    /// </summary>
    public static IReadOnlyList<AccessoryPack> Wearing(IReadOnlyDictionary<string, string> outfit)
    {
        var worn = new List<AccessoryPack>();
        foreach (var slot in OutfitSlots.All)
        {
            if (outfit.TryGetValue(OutfitSlots.KeyOf(slot), out var id) && FindAccessory(id) is { } entry && entry.Slot == slot)
            {
                worn.Add(entry.Pack);
            }
        }

        return worn;
    }

    /// <summary>Tells the catalogue where installed packs live, and reads them. Call again to pick up changes.</summary>
    public static void Configure(string? installedDirectory, DiagnosticLog? log = null)
    {
        lock (Gate)
        {
            _installedDirectory = installedDirectory;
            _log = log ?? _log; // a rescan that doesn't pass a log keeps the one it had
            _snapshot = null;
            Version++;
        }

        Changed?.Invoke();
    }

    private static Snapshot Build()
    {
        var characters = BuiltInIds.Select(id => LoadBuiltIn<CharacterPack>(id, "Characters"))
            .Select(pack => new CharacterEntry(pack.Header.Id, pack.Header.Name, pack, BuiltIn: true))
            .ToList();
        var accessories = BuiltInAccessoryIds.Select(id => LoadBuiltIn<AccessoryPack>(id, "Accessories"))
            .Select(pack => new AccessoryEntry(pack.Header.Id, pack.Header.Name, pack.Slot, pack, BuiltIn: true))
            .ToList();

        if (_installedDirectory is { } directory)
        {
            var installed = PackStore.Scan(directory, new PackLoadOptions(AppVersion.Current), _log);

            // A built-in id can't be replaced by an installed pack.
            characters.AddRange(installed.Where(item => item.Pack is CharacterPack)
                .Where(item => characters.All(entry => entry.Id != item.Pack.Header.Id))
                .OrderBy(item => item.Pack.Header.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new CharacterEntry(item.Pack.Header.Id, item.Pack.Header.Name, (CharacterPack)item.Pack, BuiltIn: false, item.Folder,
                    PackBundle.Read(directory, item.Pack.Header.Id) is { Count: > 0 } bundle ? bundle : null)));

            accessories.AddRange(installed.Select(item => item.Pack).OfType<AccessoryPack>()
                .Where(pack => accessories.All(entry => entry.Id != pack.Header.Id))
                .OrderBy(pack => pack.Header.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(pack => new AccessoryEntry(pack.Header.Id, pack.Header.Name, pack.Slot, pack, BuiltIn: false)));
        }

        return new Snapshot(characters, accessories);
    }

    private static T LoadBuiltIn<T>(string id, string folder)
        where T : Pack
    {
        using var stream = AssetLoader.Open(new Uri($"avares://HeySip/Assets/{folder}/{id}.json"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var result = PackLoader.FromFiles(new Dictionary<string, byte[]> { ["pack.json"] = buffer.ToArray() }, BuiltInOptions);

        // A built-in pack that doesn't load is a bug in the app, not something the user can fix; say exactly what is wrong.
        return result.Pack as T
            ?? throw new InvalidOperationException($"The built-in pack {id} is invalid: {result.Error ?? "it is the wrong kind of pack"}");
    }
}
