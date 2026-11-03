namespace HeySip.App.Shared;

/// <summary>The version of this copy of HeySip, as packs see it when they ask for a minimum version.</summary>
public static class AppVersion
{
    public static Version Current { get; } = typeof(AppVersion).Assembly.GetName().Version ?? new Version(1, 0);
}
