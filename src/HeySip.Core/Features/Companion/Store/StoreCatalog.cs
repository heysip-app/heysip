using System.Text.Json;
using System.Text.RegularExpressions;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Companion.Store;

/// <summary>One character or accessory on offer. It is the same shape whether the list is built into the app or comes from a server.</summary>
/// <param name="PackLocation">Where the pack file is, relative to the list or a full address. Null when it can't be fetched (it is built in).</param>
/// <param name="PriceCents">Zero for free.</param>
/// <param name="BuyUrl">Where a paid item is bought. Null when it can't be bought (yet).</param>
/// <param name="Accessories">For a character: the ids of the accessories that come with it. They are installed together with the character, whatever they cost on their own. Always empty for an accessory.</param>
public sealed record StoreEntry(
    string Id,
    PackKind Kind,
    string Name,
    string Author,
    string Version,
    string Description,
    int PriceCents,
    string? PackLocation,
    string? Sha256,
    long Size,
    string? BuyUrl,
    IReadOnlyList<string>? Accessories = null)
{
    public bool IsFree => PriceCents == 0;

    /// <summary>The accessories that come with this character, in the order listed; none for an accessory.</summary>
    public IReadOnlyList<string> Included => Accessories ?? [];
}

/// <summary>The outcome of reading a catalogue: the entries that were fine, and how many were left out.</summary>
/// <param name="Notice">Something worth telling the user although the list loaded, such as that the online part couldn't be reached.</param>
public sealed record StoreCatalogResult(IReadOnlyList<StoreEntry> Entries, int Skipped, string? Error, string? Notice = null)
{
    public bool Ok => Error is null;

    public static StoreCatalogResult Failure(string error) => new([], 0, error);
}

/// <summary>
/// Reads a catalogue: a small JSON file listing what can be installed. Reading never throws. A broken entry is skipped
/// and counted, so one bad line on a server can't empty the whole list.
/// </summary>
public static partial class StoreCatalog
{
    public const int MaxBytes = 512 * 1024;
    public const int MaxEntries = 500;
    public const int MaxPriceCents = 100_000;
    public const int MaxAccessoriesPerCharacter = PackBundle.MaxAccessories;

    // How long each piece of text in an entry may be.
    public const int MaxNameLength = 60;
    public const int MaxAuthorLength = 60;
    public const int MaxDescriptionLength = 400;
    public const int MaxAddressLength = 512;

    /// <summary>A SHA-256 hash written in hex: 2 digits for each of its 32 bytes. The pattern below says the same (a test keeps them equal).</summary>
    public const int Sha256HexLength = 64;

    private const string KeyItems = "items";
    private const string KeyId = "id";
    private const string KeyName = "name";
    private const string KeyVersion = "version";
    private const string KeyKind = "kind";
    private const string KeyPriceCents = "priceCents";
    private const string KeySize = "size";
    private const string KeySha256 = "sha256";
    private const string KeyPack = "pack";
    private const string KeyBuyUrl = "buyUrl";
    private const string KeyAccessories = "accessories";
    private const string KeyAuthor = "author";
    private const string KeyDescription = "description";

    private const string KindCharacter = "character";
    private const string KindAccessory = "accessory";

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();

    public static StoreCatalogResult Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaxBytes)
        {
            return StoreCatalogResult.Failure("The list is too big");
        }

        json = JsonInput.WithoutByteOrderMark(json);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = JsonInput.MaxDepth });
            var root = document.RootElement;
            if (!JsonInput.HasFormatVersion(root, JsonInput.SupportedFormatVersion))
            {
                return StoreCatalogResult.Failure("The list isn't in a format this version understands");
            }

            if (!root.TryGetProperty(KeyItems, out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return StoreCatalogResult.Failure("The list has no items");
            }

            var entries = new List<StoreEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var skipped = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (entries.Count + skipped >= MaxEntries)
                {
                    skipped++;
                    continue;
                }

                if (ReadEntry(item) is { } entry && seen.Add(entry.Id))
                {
                    entries.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }

            return new StoreCatalogResult(entries, skipped, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return StoreCatalogResult.Failure("The list couldn't be read");
        }
    }

    private static StoreEntry? ReadEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Text(item, KeyId, PackLimits.MaxIdLength);
        var name = Text(item, KeyName, MaxNameLength);
        var version = Text(item, KeyVersion, PackLimits.MaxVersionLength);
        var kind = Text(item, KeyKind, PackLimits.MaxKindLength) switch
        {
            KindCharacter => (PackKind?)PackKind.Character,
            KindAccessory => PackKind.Accessory,
            _ => null,
        };

        if (!PackId.IsValid(id) || string.IsNullOrWhiteSpace(name) || kind is null || !PackVersion.IsValid(version))
        {
            return null;
        }

        var price = 0;
        if (item.TryGetProperty(KeyPriceCents, out var priceElement)
            && (priceElement.ValueKind != JsonValueKind.Number || !priceElement.TryGetInt32(out price) || price < 0 || price > MaxPriceCents))
        {
            return null;
        }

        long size = 0;
        if (item.TryGetProperty(KeySize, out var sizeElement)
            && (sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out size) || size < 0 || size > PackLimits.MaxPackBytes))
        {
            return null;
        }

        // A hash that is written but isn't a proper one (too long reads as "missing" below) must not quietly switch the
        // check off: the download would then be installed unverified. So such an entry is left out.
        var sha = Text(item, KeySha256, Sha256HexLength);
        var hashWritten = item.TryGetProperty(KeySha256, out var shaElement) && shaElement.ValueKind != JsonValueKind.Null;
        if (hashWritten && (sha is null || !Sha256Pattern().IsMatch(sha)))
        {
            return null;
        }

        var location = Text(item, KeyPack, MaxAddressLength);
        if (location is not null && (location.Any(char.IsControl) || location.Contains("..", StringComparison.Ordinal)))
        {
            return null;
        }

        var buy = Text(item, KeyBuyUrl, MaxAddressLength);
        if (buy is not null && !(Uri.TryCreate(buy, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            return null;
        }

        // Only a character can come with accessories. One bad id spoils the entry, so a character is never offered half its set.
        IReadOnlyList<string>? accessories = null;
        if (kind == PackKind.Character && item.TryGetProperty(KeyAccessories, out var accessoryList))
        {
            if (ReadAccessoryIds(accessoryList, id!) is not { } ids)
            {
                return null;
            }

            accessories = ids.Count > 0 ? ids : null;
        }

        return new StoreEntry(
            id!, kind.Value, name.Trim(), (Text(item, KeyAuthor, MaxAuthorLength) ?? "").Trim(), version!,
            (Text(item, KeyDescription, MaxDescriptionLength) ?? "").Trim(), price, location, sha?.ToLowerInvariant(), size, buy, accessories);
    }

    private static List<string>? ReadAccessoryIds(JsonElement list, string characterId)
    {
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > MaxAccessoriesPerCharacter)
        {
            return null;
        }

        var ids = new List<string>();
        foreach (var element in list.EnumerateArray())
        {
            var accessoryId = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            if (!PackId.IsValid(accessoryId) || accessoryId == characterId || ids.Contains(accessoryId!))
            {
                return null;
            }

            ids.Add(accessoryId!);
        }

        return ids;
    }

    /// <summary>The text of a property, or null when it is missing, not text, or longer than <paramref name="max"/>.</summary>
    private static string? Text(JsonElement item, string name, int max) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= max
            ? text
            : null;
}

/// <summary>What the user can do with a catalogue entry right now.</summary>
public enum StoreStatus
{
    /// <summary>Comes with the app.</summary>
    Included,

    /// <summary>Installed, and this is the newest version listed.</summary>
    Installed,

    /// <summary>Installed, but the list has a newer version.</summary>
    Update,

    /// <summary>Not installed; can be fetched.</summary>
    Available,

    /// <summary>Not installed and no file is listed for it, so it can't be fetched.</summary>
    Unavailable,
}

public static class StoreStatusOf
{
    public static StoreStatus For(StoreEntry entry, bool builtIn, string? installedVersion)
    {
        if (builtIn)
        {
            return StoreStatus.Included;
        }

        if (installedVersion is not null)
        {
            return entry.PackLocation is not null && PackVersion.IsValid(installedVersion) && PackVersion.Compare(entry.Version, installedVersion) > 0
                ? StoreStatus.Update
                : StoreStatus.Installed;
        }

        return entry.PackLocation is null ? StoreStatus.Unavailable : StoreStatus.Available;
    }
}

/// <summary>Where the list and the pack files come from. The app has one built in; a server would be another.</summary>
public interface IStoreSource
{
    Task<StoreCatalogResult> LoadAsync(CancellationToken cancel);

    /// <summary>The bytes of an entry's <c>.heysippack</c> file. Throws <see cref="IOException"/> when it can't be had.</summary>
    Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel);
}
