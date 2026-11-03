namespace HeySip.Core.Features.Settings;

/// <summary>Registers the app to start when the user signs in. Platform-specific implementations live in the App.</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }

    bool IsEnabled();

    void SetEnabled(bool enabled);
}

public sealed class UnsupportedStartupRegistration : IStartupRegistration
{
    public bool IsSupported => false;

    public bool IsEnabled() => false;

    public void SetEnabled(bool enabled)
    {
    }
}
