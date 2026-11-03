namespace HeySip.App.Features.Browse;

/// <summary>
/// Whether paid items can be bought from the app. While it is off, a paid item shows "Coming soon" even if the list gives it
/// a buy address: it can be looked at and tried on, but the app never opens a buy page. Free items, items that are already
/// owned, and accessories that come with a character are not affected.
/// </summary>
/// <remarks>
/// Leave <see cref="EnabledByDefault"/> off until a payment provider (Paddle) is set up and a purchase can be recognised
/// afterwards. To try the buy flow on one computer without changing the build, set <c>HEYSIP_PAYMENTS</c> to <c>on</c>.
/// </remarks>
public sealed record StorePayments(bool Enabled)
{
    /// <summary>Flip this to switch paid purchases on for everyone, once payments are ready.</summary>
    public const bool EnabledByDefault = false;

    public const string OverrideVariable = "HEYSIP_PAYMENTS";

    /// <summary>The switch for this run: the setting, unless <paramref name="overrideValue"/> says "on" or "off" (also 1/0, true/false).</summary>
    public static StorePayments Resolve(string? overrideValue = null) => new(overrideValue?.Trim().ToLowerInvariant() switch
    {
        "on" or "1" or "true" => true,
        "off" or "0" or "false" => false,
        _ => EnabledByDefault,
    });
}
