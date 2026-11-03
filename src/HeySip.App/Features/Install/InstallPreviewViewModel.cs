using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.App.Features.Install;

public enum InstallStage
{
    /// <summary>Showing what the pack is, waiting for Install or Cancel.</summary>
    Preview,

    /// <summary>Installed; offering to use it straight away.</summary>
    Done,

    /// <summary>The pack was refused or couldn't be saved; showing why.</summary>
    Failed,
}

/// <summary>One small picture in the preview: the pack (or the accessory on your character) in a pose.</summary>
public sealed class PreviewTileViewModel(DripPose pose, CharacterPack? pack, string character, IReadOnlyList<AccessoryPack>? outfit, string caption)
{
    public DripPose Pose { get; } = pose;

    /// <summary>The character pack to draw, or null to draw the character with id <see cref="Character"/>.</summary>
    public CharacterPack? Pack { get; } = pack;

    public string Character { get; } = character;

    public IReadOnlyList<AccessoryPack>? Outfit { get; } = outfit;

    public string Caption { get; } = caption;
}

/// <summary>
/// The accessories that come with a character being got: <see cref="All"/> of them, so the card can show the whole set, and
/// <see cref="ToInstall"/>, the ones not on this computer yet.
/// </summary>
public sealed record InstallBundle(IReadOnlyList<PackLoadResult> ToInstall, IReadOnlyList<AccessoryPack> All)
{
    public static InstallBundle None { get; } = new([], []);
}

/// <summary>
/// The card that appears when a pack file is opened. It checks the pack, shows how it will look and what it is, and only
/// installs when the user says so. A pack that fails its checks shows the reason and offers no Install button.
/// </summary>
public sealed partial class InstallPreviewViewModel(SettingsService settings, AppDataLocation location, DiagnosticLog log) : ViewModelBase
{
    private PackLoadResult? _pending;
    private IReadOnlyList<PackLoadResult> _bundled = [];

    /// <summary>Every accessory that comes with the character on the card, installed or not; null when the card came from a file and knows nothing about a set.</summary>
    private IReadOnlyList<string>? _bundleIds;

    /// <summary>The set a brand-new character starts out wearing; null when there is nothing to put on it (an update, a reinstall, no set).</summary>
    private IReadOnlyList<AccessoryPack>? _dressOnInstall;
    private Pack? _installed;

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreview), nameof(IsDone), nameof(IsFailed))]
    public partial InstallStage Stage { get; set; }

    [ObservableProperty]
    public partial string Eyebrow { get; set; } = "";

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>Who made it, which version, how big.</summary>
    [ObservableProperty]
    public partial string Details { get; set; } = "";

    /// <summary>What installing will do to what is installed already, such as an update.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    [ObservableProperty]
    public partial string InstallLabel { get; set; } = "Install";

    [ObservableProperty]
    public partial bool CanInstall { get; set; }

    [ObservableProperty]
    public partial string UseLabel { get; set; } = "";

    public ObservableCollection<PreviewTileViewModel> Tiles { get; } = [];

    /// <summary>The accessories that come with the character, each worn by it. Empty for anything else.</summary>
    public ObservableCollection<PreviewTileViewModel> BundledTiles { get; } = [];

    /// <summary>Whether there are accessories that come with the character, so the "Comes with" heading shows.</summary>
    [ObservableProperty]
    public partial bool HasBundled { get; set; }

    /// <summary>How many more accessories the character works with, such as "Plus 3 more accessories you can add."</summary>
    [ObservableProperty]
    public partial string MoreText { get; set; } = "";

    public bool IsPreview => Stage == InstallStage.Preview;

    public bool IsDone => Stage == InstallStage.Done;

    public bool IsFailed => Stage == InstallStage.Failed;

    /// <summary>What the user is told about a pack from a file: it can only ever be pictures and settings.</summary>
    public string Reassurance { get; } =
        "A pack is only pictures and settings. It can't run programs or reach the internet. Install packs from people you trust.";

    /// <summary>Raised once a pack was installed, after the catalogue has been refreshed.</summary>
    public event Action? Installed;

    /// <summary>
    /// Checks the result of loading a pack and shows the card: the preview if it is fine, the reason if not.
    /// <paramref name="bundle"/> holds the accessories that come with a character; the ones not installed yet are installed with it, before it.
    /// </summary>
    public void Show(PackLoadResult result, string source, long bytes, InstallBundle? bundle = null)
    {
        bundle ??= InstallBundle.None;
        _pending = null;
        _bundled = [];
        _bundleIds = null;
        _dressOnInstall = null;
        _installed = null;
        ClearTiles();

        if (result.Pack is not { } pack)
        {
            ShowError(result.Error ?? "This pack can't be installed.");
            return;
        }

        var accessories = bundle.ToInstall.Where(item => item.Pack is AccessoryPack).ToList();
        if (accessories.Count != bundle.ToInstall.Count)
        {
            ShowError("One of the accessories that comes with this character can't be installed.");
            return;
        }

        _pending = result;
        _bundled = accessories;
        _bundleIds = ReferenceEquals(bundle, InstallBundle.None) ? null : [.. bundle.All.Select(item => item.Header.Id)];
        var plan = PackInstaller.Plan(pack, location.CharactersDirectory);
        var header = pack.Header;

        Eyebrow = pack is CharacterPack ? "New character" : $"New accessory · {OutfitSlots.TitleOf(((AccessoryPack)pack).Slot)}";
        Title = header.Name;
        var author = string.IsNullOrWhiteSpace(header.Author) ? "an unknown author" : header.Author;
        Details = $"By {author} · version {header.Version} · {SizeText(bytes)} · {(header.Tier == PackTier.Premium ? "Premium" : "Free")}"
            + (header.Description.Length > 0 ? $"\n{header.Description}" : "");

        (Note, InstallLabel, CanInstall) = plan.Kind switch
        {
            InstallKind.Update => ($"This updates the version you have ({plan.InstalledVersion}).", "Update", true),
            InstallKind.Reinstall => ("You already have this version. Installing again just puts it back.", "Reinstall", true),
            InstallKind.Older => ($"You already have a newer version ({plan.InstalledVersion}), so this older one would never be used.", "Install", false),
            _ => ($"From {source}", "Install", true),
        };

        // A new character is shown wearing what it will start out wearing; an update or reinstall keeps the user's own choices.
        _dressOnInstall = plan.Kind == InstallKind.New && pack is CharacterPack && bundle.All.Count > 0 ? bundle.All : null;
        foreach (var tile in PreviewTiles.For(pack, settings.Current.Character, _dressOnInstall))
        {
            Tiles.Add(tile);
        }

        if (pack is CharacterPack character)
        {
            foreach (var tile in PreviewTiles.Bundled(character, bundle.All))
            {
                BundledTiles.Add(tile);
            }

            HasBundled = BundledTiles.Count > 0;

            // The accessories already on this computer work on every character, so they are what this one can be given on top.
            var more = CharacterCatalog.Accessories.Count(item => bundle.All.All(own => own.Header.Id != item.Id));
            MoreText = PreviewTiles.MoreText(bundle.All.Count, more);
        }

        ErrorText = "";
        Stage = InstallStage.Preview;
        IsVisible = true;
    }

    /// <summary>Shows why a pack couldn't be used.</summary>
    public void ShowError(string message)
    {
        _pending = null;
        _bundled = [];
        _bundleIds = null;
        _dressOnInstall = null;
        Eyebrow = "Can't install this pack";
        Title = "This file can't be installed";
        Details = "";
        Note = "";
        ErrorText = message.EndsWith('.') ? message : message + ".";
        ClearTiles();
        Stage = InstallStage.Failed;
        IsVisible = true;
    }

    [RelayCommand]
    private void Install()
    {
        if (_pending is not { Pack: { } pack } pending || !CanInstall)
        {
            return;
        }

        // The accessories go in first, so a character is never installed without the set it came with.
        foreach (var accessory in _bundled)
        {
            var accessoryOutcome = PackInstaller.Install(accessory, location.CharactersDirectory);
            if (!accessoryOutcome.Ok)
            {
                var accessoryName = accessory.Pack!.Header.Name;
                log.Warning("Packs", $"Installing {accessory.Pack.Header.Id} with {pack.Header.Id} failed: {accessoryOutcome.Error}");
                CharacterCatalog.Configure(location.CharactersDirectory, log);
                ShowError($"{accessoryName} (it comes with {pack.Header.Name}) couldn't be installed: {accessoryOutcome.Error}");
                return;
            }

            log.Info("Packs", $"Installed {accessory.Pack!.Header.Id} {accessory.Pack.Header.Version} with {pack.Header.Id}.");
        }

        var outcome = PackInstaller.Install(pending, location.CharactersDirectory);
        if (!outcome.Ok)
        {
            log.Warning("Packs", $"Installing {pack.Header.Id} failed: {outcome.Error}");
            CharacterCatalog.Configure(location.CharactersDirectory, log);
            ShowError(outcome.Error ?? "The pack couldn't be installed.");
            return;
        }

        log.Info("Packs", $"Installed {pack.Header.Id} {pack.Header.Version}.");

        // Remember which accessories came with the character, so the outfit picker can list them first. A character from a
        // file knows nothing about this, so what was remembered before is left alone.
        if (pack is CharacterPack && _bundleIds is { } ids)
        {
            PackBundle.Save(location.CharactersDirectory, pack.Header.Id, ids);
        }

        CharacterCatalog.Configure(location.CharactersDirectory, log);

        // A new character starts out wearing its own accessories, unless it already has an outfit of its own.
        if (_dressOnInstall is { } set)
        {
            var id = pack.Header.Id;
            settings.Update(current => current.OutfitOf(id).Count > 0
                ? current
                : PackBundle.DefaultLook(set).Aggregate(current, (dressed, accessory) => dressed.WithAccessory(id, accessory.Slot, accessory.Header.Id)));
        }

        _installed = pack;
        _pending = null;
        _bundled = [];
        _bundleIds = null;
        _dressOnInstall = null;

        Eyebrow = "Installed";
        Title = $"{pack.Header.Name} is ready";
        Note = "";
        Details = "";
        ClearTiles();
        UseLabel = pack is CharacterPack ? "Use it now" : "Wear it now";
        Stage = InstallStage.Done;
        Installed?.Invoke();
    }

    /// <summary>After installing: switch to the new character, or put the new accessory on.</summary>
    [RelayCommand]
    private void UseNow()
    {
        switch (_installed)
        {
            case CharacterPack character:
                settings.Update(current => current with { Character = character.Header.Id });
                break;
            case AccessoryPack accessory:
                settings.Update(current => current.WithAccessory(accessory.Slot, accessory.Header.Id));
                break;
        }

        Close();
    }

    [RelayCommand]
    public void Close()
    {
        IsVisible = false;
        _pending = null;
        _bundled = [];
        _bundleIds = null;
        _dressOnInstall = null;
        _installed = null;
    }

    private void ClearTiles()
    {
        Tiles.Clear();
        BundledTiles.Clear();
        HasBundled = false;
        MoreText = "";
    }

    private static string SizeText(long bytes) => bytes switch
    {
        <= 0 => "size unknown",
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}
