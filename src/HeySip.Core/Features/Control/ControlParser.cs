using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Control;

/// <summary>
/// Reads the control file. It never throws: anything wrong with the file as a whole gives null, and a bad notice or rule is skipped without
/// hiding the others. Fields it doesn't know are ignored, so a newer file still works with this version.
/// </summary>
public static partial class ControlParser
{
    public const int MaxBytes = 64 * 1024;
    public const int MaxNotices = 10;
    public const int MaxTextLength = 280;
    public const int MaxRevoked = 100;
    public const int MaxIdLength = 64;
    public const int MaxCatalogRules = 10;
    public const int MaxUrlsPerRule = 5;

    // How long each piece of text in the file may be.
    public const int MaxTimestampLength = 40;
    public const int MaxRevokedIdLength = 80;
    public const int MaxVersionTextLength = 20;
    public const int MaxAddressLength = 400;

    private const string KeyIssuedAt = "issuedAt";
    private const string KeyApp = "app";
    private const string KeyNotices = "notices";
    private const string KeyRevoked = "revoked";
    private const string KeyFeatures = "features";
    private const string KeyOnlineStore = "onlineStore";
    private const string KeyLatestVersion = "latestVersion";
    private const string KeyMinSupportedVersion = "minSupportedVersion";
    private const string KeyDownloadUrl = "downloadUrl";
    private const string KeyApi = "api";
    private const string KeyCatalog = "catalog";
    private const string KeyUrls = "urls";
    private const string KeyId = "id";
    private const string KeyText = "text";
    private const string KeyExpires = "expires";
    private const string KeyDismissible = "dismissible";
    private const string KeyLink = "link";
    private const string KeyAppVersions = "appVersions";
    private const string KeyMin = "min";
    private const string KeyMax = "max";

    public static ControlFile? Parse(ReadOnlyMemory<byte> json, Func<Uri, bool> linkAllowed)
    {
        if (json.Length is 0 or > MaxBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = JsonInput.MaxDepth });
            var root = document.RootElement;
            if (!JsonInput.HasFormatVersion(root, JsonInput.SupportedFormatVersion))
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(Text(root, KeyIssuedAt, MaxTimestampLength), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var issuedAt))
            {
                return null;
            }

            var app = Section(root, KeyApp);
            var notices = new List<ControlNotice>();
            if (root.TryGetProperty(KeyNotices, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (notices.Count >= MaxNotices)
                    {
                        break;
                    }

                    if (ReadNotice(item, linkAllowed) is { } notice && notices.All(other => other.Id != notice.Id))
                    {
                        notices.Add(notice);
                    }
                }
            }

            var revoked = new List<string>();
            if (root.TryGetProperty(KeyRevoked, out var revokedList) && revokedList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in revokedList.EnumerateArray())
                {
                    if (revoked.Count < MaxRevoked && item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 and <= MaxRevokedIdLength } value)
                    {
                        revoked.Add(value);
                    }
                }
            }

            // On unless it says plainly "false", so a file that doesn't mention it leaves the list working.
            var features = Section(root, KeyFeatures);
            var onlineStore = features is not { } section || !section.TryGetProperty(KeyOnlineStore, out var flag) || flag.ValueKind != JsonValueKind.False;

            return new ControlFile(
                issuedAt,
                app is { } a ? ReadVersion(a, KeyLatestVersion) : null,
                app is { } b ? ReadVersion(b, KeyMinSupportedVersion) : null,
                app is { } c ? ReadLink(c, KeyDownloadUrl, linkAllowed) : null,
                ReadCatalog(Section(root, KeyApi)),
                onlineStore,
                notices,
                revoked);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Section(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    /// <summary>The rules for where the list is. An address must be https (or plain http to this computer); a rule with none left is skipped.</summary>
    private static IReadOnlyList<CatalogRule> ReadCatalog(JsonElement? api)
    {
        var rules = new List<CatalogRule>();
        if (api is not { } section || !section.TryGetProperty(KeyCatalog, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return rules;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (rules.Count >= MaxCatalogRules)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(KeyUrls, out var urls) || urls.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var addresses = new List<Uri>();
            foreach (var url in urls.EnumerateArray())
            {
                if (addresses.Count < MaxUrlsPerRule && url.ValueKind == JsonValueKind.String && StoreAddress.Parse(url.GetString()) is { } address && address.AbsoluteUri.Length <= MaxAddressLength)
                {
                    addresses.Add(address);
                }
            }

            if (addresses.Count > 0)
            {
                var (min, max) = ReadRange(item);
                rules.Add(new CatalogRule(min, max, addresses));
            }
        }

        return rules;
    }

    private static ControlNotice? ReadNotice(JsonElement item, Func<Uri, bool> linkAllowed)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Text(item, KeyId, MaxIdLength);
        var text = Text(item, KeyText, MaxTextLength)?.Trim();
        if (id is null || !IdPattern().IsMatch(id) || string.IsNullOrEmpty(text))
        {
            return null;
        }

        DateTimeOffset? expires = null;
        if (Text(item, KeyExpires, MaxTimestampLength) is { } expiresText)
        {
            if (!DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return null;
            }

            expires = parsed;
        }

        var dismissible = !item.TryGetProperty(KeyDismissible, out var flag) || flag.ValueKind != JsonValueKind.False;
        var (min, max) = ReadRange(item);
        return new ControlNotice(id, text, ReadLink(item, KeyLink, linkAllowed), min, max, expires, dismissible);
    }

    /// <summary><c>"appVersions": { "min": "0.1.0", "max": "0.9.9" }</c>: either end may be left out.</summary>
    private static (Version? Min, Version? Max) ReadRange(JsonElement element) =>
        Section(element, KeyAppVersions) is { } range ? (ReadVersion(range, KeyMin), ReadVersion(range, KeyMax)) : (null, null);

    private static string? Text(JsonElement element, string name, int maxLength) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= maxLength
            ? text
            : null;

    private static Version? ReadVersion(JsonElement element, string name) =>
        Text(element, name, MaxVersionTextLength) is { } text && Version.TryParse(text, out var version) ? version : null;

    /// <summary>A link must be https with no user info and an address the app trusts; otherwise it is dropped (the notice stays).</summary>
    private static Uri? ReadLink(JsonElement element, string name, Func<Uri, bool> linkAllowed) =>
        Text(element, name, MaxAddressLength) is { } text
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && linkAllowed(uri)
            ? uri
            : null;

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();
}
