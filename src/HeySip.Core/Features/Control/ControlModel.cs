namespace HeySip.Core.Features.Control;

/// <summary>One message to show the user. Only the fields the app understands are kept.</summary>
/// <param name="Link">An https address on a host the app trusts (see <see cref="ControlOptions.LinkAllowed"/>), or null.</param>
/// <param name="MinVersion">Shown only to this app version and newer; null means every version.</param>
/// <param name="MaxVersion">Shown only to this app version and older; null means every version.</param>
/// <param name="Expires">Not shown after this moment; null means it doesn't expire.</param>
/// <param name="Dismissible">False means the user can't hide it (used sparingly, for "this version is no longer supported").</param>
public sealed record ControlNotice(string Id, string Text, Uri? Link, Version? MinVersion, Version? MaxVersion, DateTimeOffset? Expires, bool Dismissible);

/// <summary>Where the online character list is, for the app versions between <paramref name="Min"/> and <paramref name="Max"/> (either may be open).</summary>
/// <param name="Urls">Tried in order: the first one that answers with a good, signed list is used. The rest are fallbacks.</param>
public sealed record CatalogRule(Version? Min, Version? Max, IReadOnlyList<Uri> Urls)
{
    public bool Covers(Version app) => (Min is null || app >= Min) && (Max is null || app <= Max);
}

/// <summary>
/// What the signed control file says. This is the only thing the app is told from outside: every other address (the character list, the
/// update page, links in notices) comes from here. Anything the app doesn't know about is ignored, so the file can grow without breaking old versions.
/// </summary>
/// <param name="IssuedAt">When it was signed. A file older than the one already kept is never used (no going back).</param>
/// <param name="LatestVersion">The newest release. A newer one than this app shows an "update available" notice.</param>
/// <param name="MinSupportedVersion">Versions older than this show a notice that can't be hidden and stop looking online for characters.</param>
/// <param name="UpdateUrl">Where to get the newest version.</param>
/// <param name="Catalog">Where the online list is, by app version. The first rule that covers this app's version is used; none means no online list.</param>
/// <param name="OnlineStore">False switches the online list off for everyone (a kill switch). Reminders are never affected.</param>
/// <param name="Revoked">Pack ids and pack SHA-256 values that must no longer be offered.</param>
public sealed record ControlFile(
    DateTimeOffset IssuedAt,
    Version? LatestVersion,
    Version? MinSupportedVersion,
    Uri? UpdateUrl,
    IReadOnlyList<CatalogRule> Catalog,
    bool OnlineStore,
    IReadOnlyList<ControlNotice> Notices,
    IReadOnlyList<string> Revoked);

/// <param name="Urls">Where the control file may be, tried in order. The signature file sits next to it as <c>control.sig</c>.</param>
/// <param name="PublicKeys">Base64 SubjectPublicKeyInfo (ECDSA P-256) keys. A file signed by any one of them is accepted.</param>
/// <param name="LinkAllowed">Which addresses a notice may link to, besides being https.</param>
public sealed record ControlOptions(IReadOnlyList<Uri> Urls, IReadOnlyList<string> PublicKeys, Func<Uri, bool> LinkAllowed);
