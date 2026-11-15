namespace HeySip.Core.Features.Control;

/// <summary>
/// The ids of the notices the app makes itself (as opposed to those in the control file). The service creates them and the
/// banner recognises them, so both read the same names.
/// </summary>
public static class NoticeIds
{
    /// <summary>This version is older than the minimum the control file supports.</summary>
    public const string Unsupported = "unsupported";

    private const string UpdatePrefix = "update-";

    /// <summary>How many parts of a version (major.minor.build) are written in notices and ids.</summary>
    public const int VersionParts = 3;

    /// <summary>The id of the "a newer version is available" notice for that version, so dismissing one doesn't hide the next.</summary>
    public static string Update(Version latest) => UpdatePrefix + latest.ToString(VersionParts);

    public static bool IsUpdate(string id) => id.StartsWith(UpdatePrefix, StringComparison.Ordinal);

    /// <summary>Notices whose action is to download a new version.</summary>
    public static bool IsUpgradePrompt(string id) => id == Unsupported || IsUpdate(id);
}
