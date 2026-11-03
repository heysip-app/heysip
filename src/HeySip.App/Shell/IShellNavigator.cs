namespace HeySip.App.Shell;

/// <summary>What the command palette (and anything else that drives the window) may ask the shell to do.</summary>
public interface IShellNavigator
{
    void Navigate(ShellPage page);

    void OpenDay(DateOnly day);

    /// <summary>Opens Settings, filtered to <paramref name="search"/>; null or empty shows every setting.</summary>
    void OpenSettings(string? search);

    /// <summary>Asks for a character or accessory file and shows what is in it, ready to install.</summary>
    void InstallFromFile();

    /// <summary>Shows the first-run welcome again.</summary>
    void ShowWelcome();

    void CloseCurrentTab();

    void NextTab();

    void PreviousTab();
}
