namespace HeySip.Core.Features.Companion.Store;

/// <summary>Which web addresses the app will talk to: https only, plus plain http to this very computer for trying things out.</summary>
public static class StoreAddress
{
    public static bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    /// <summary>The address in the text, or null when there is none or it isn't one the app may use.</summary>
    public static Uri? Parse(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && IsAllowed(uri) ? uri : null;
}
