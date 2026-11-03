using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Control;

namespace HeySip.App.Features.Control;

/// <summary>
/// The only things built into the app about the outside world: where the signed control file is, and which keys to trust. Everything
/// else (the online list, the update page, links in notices) is told by that file. Installed copies can't be updated to new values here,
/// so these are chosen once and kept. See <c>docs/remote-control.md</c>.
/// </summary>
public static class ControlLocation
{
    /// <summary>Where the signed control file is. <c>control.sig</c> sits next to it. Add a second address on another host here when there is one.</summary>
    public static readonly string[] Addresses = ["https://heysip-app.github.io/heysip-config/v1/control.json"];

    /// <summary>
    /// The public halves of the signing keys: the main one and an offline backup. A file signed by either is accepted. Only the
    /// public parts are here; the private keys live outside every repository.
    /// </summary>
    public static readonly string[] PublicKeys =
    [
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+vCuyvAhV2yAUocGfj9bGc62AKBa4DnQcMUUQ5Q/PKIy0PWGb/kxneOb++IoHyvpTye20iXkDq7hDCI/PN609Q==",
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEXQltOyk5DRobAdmU4NnXBcIFVgraoo41iIskcHP2ZqYQ/7rCNAXP5XcccitrkomZx0VSyGiF6HjV4l2FgX9CYQ==",
    ];

    /// <summary>For trying a server out: this address replaces <see cref="Addresses"/>. The signature is still required.</summary>
    public const string OverrideVariable = "HEYSIP_CONTROL_URL";

    private static readonly Func<Uri, bool> OwnLinks = LinkRule(Addresses.Select(address => new Uri(address)));

    public static ControlOptions Options(string? overrideValue = null)
    {
        var urls = StoreAddress.Parse(overrideValue) is { } custom
            ? [custom]
            : Addresses.Select(address => new Uri(address)).ToArray();
        return new ControlOptions(urls, PublicKeys, LinkRule(urls));
    }

    /// <summary>Notices link to the project's own pages: the host the control file is on, and (for GitHub Pages) that organization on github.com.</summary>
    public static bool IsOwnLink(Uri uri) => OwnLinks(uri);

    /// <summary>
    /// Which addresses a notice may link to, worked out from where the control file is, so no second address needs to be built in: the same
    /// host, and when that is <c>NAME.github.io</c>, also <c>github.com/NAME/...</c>. Always https.
    /// </summary>
    public static Func<Uri, bool> LinkRule(IEnumerable<Uri> controlAddresses)
    {
        const string Pages = ".github.io";
        var hosts = controlAddresses.Select(address => address.Host).ToArray();
        var organizations = hosts.Where(host => host.EndsWith(Pages, StringComparison.OrdinalIgnoreCase)).Select(host => host[..^Pages.Length]).ToArray();
        return uri =>
            uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && (hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
                || (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                    && organizations.Any(name => uri.AbsolutePath.StartsWith($"/{name}/", StringComparison.OrdinalIgnoreCase))));
    }
}
