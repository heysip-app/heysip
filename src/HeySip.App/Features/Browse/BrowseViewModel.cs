using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Features.Install;
using HeySip.App.Shared;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.App.Features.Browse;

/// <summary>The two kinds of thing the browser lists, one tab each.</summary>
public enum BrowseTab
{
    Characters,
    Accessories,
}

/// <summary>Cards shown together. A title is only set for a slot with several options, because only one of them can be worn.</summary>
public sealed record BrowseGroup(string Title, IReadOnlyList<StoreCardViewModel> Cards)
{
    public bool HasTitle => Title.Length > 0;
}

/// <summary>"Installed" or "Not installed", with the cards under it.</summary>
public sealed record BrowseSection(string Title, IReadOnlyList<BrowseGroup> Groups);

/// <summary>Where an item stands for the user: what they can do about it, in the words the cards use.</summary>
public enum StoreItemState
{
    /// <summary>On this computer and ready to use: it comes with the app, or is installed.</summary>
    Owned,

    /// <summary>Free and not installed yet; it can be got.</summary>
    Free,

    /// <summary>Paid and not owned; it can be bought, and tried on in the meantime.</summary>
    Paid,

    /// <summary>Paid and not owned, and there is nowhere to buy it yet; it can only be looked at.</summary>
    ComingSoon,

    /// <summary>Not installed and there is no file for it, so it can't be got.</summary>
    Unavailable,
}

/// <summary>An accessory shown on a character's page: the character wearing it, and the card that says what you can do about it.</summary>
public sealed record ExtraAccessory(StoreCardViewModel Card, PreviewTileViewModel Tile);

/// <summary>One character or accessory in the browser: how it looks, who made it, and the one thing you can do with it now.</summary>
public sealed partial class StoreCardViewModel(StoreEntry entry, Action<StoreCardViewModel> open, Action<StoreCardViewModel> act, bool paymentsEnabled) : ObservableObject
{
    public StoreEntry Entry { get; } = entry;

    /// <summary>Whether a buy page can be opened for this item: payments are on, and the list says where.</summary>
    public bool CanBuy => paymentsEnabled && Entry.BuyUrl is not null;

    public string Name => Entry.Name;

    public string Byline => Entry.Author.Length > 0 ? $"By {Entry.Author}" : "By an unknown author";

    public string Description => Entry.Description;

    public bool IsCharacter => Entry.Kind == PackKind.Character;

    public string KindText => IsCharacter ? "Character" : "Accessory";

    public string PriceText => Entry.IsFree ? "Free" : $"${Entry.PriceCents / 100}.{Entry.PriceCents % 100:00}";

    public bool IsPaid => !Entry.IsFree;

    /// <summary>Whether the owner can use it right now: it comes with the app or is installed.</summary>
    public bool IsOwned => Status is StoreStatus.Included or StoreStatus.Installed or StoreStatus.Update;

    public string StatusText => Status switch
    {
        StoreStatus.Included => "Included",
        StoreStatus.Installed => "Installed",
        StoreStatus.Update => "Update available",
        _ => "",
    };

    public StoreItemState State => Status switch
    {
        StoreStatus.Included or StoreStatus.Installed or StoreStatus.Update => StoreItemState.Owned,
        StoreStatus.Available => Entry.IsFree ? StoreItemState.Free : CanBuy ? StoreItemState.Paid : StoreItemState.ComingSoon,
        _ => StoreItemState.Unavailable,
    };

    /// <summary>The state in a word or two, for the card: Included, Owned, Free, a price, "$2.50 · Coming soon", or Not available.</summary>
    public string StateText => State switch
    {
        StoreItemState.Owned => Status == StoreStatus.Included ? "Included" : "Owned",
        StoreItemState.Free => "Free",
        StoreItemState.Paid => PriceText,
        StoreItemState.ComingSoon => $"{PriceText} · Coming soon",
        _ => "Not available",
    };

    /// <summary>A paid item that isn't owned: it can be looked at and tried on, but not worn.</summary>
    public bool IsLocked => State is StoreItemState.Paid or StoreItemState.ComingSoon;

    /// <summary>Said on the item's page while it is locked.</summary>
    public string LockNote => State switch
    {
        StoreItemState.Paid => "This is a preview. You can wear it once you've bought it.",
        StoreItemState.ComingSoon => "This is a preview. It can't be bought yet.",
        _ => "",
    };

    public string ActionLabel => Status switch
    {
        StoreStatus.Included or StoreStatus.Installed => IsInUse ? "In use" : IsCharacter ? "Use it" : "Wear it",
        StoreStatus.Update => "Update",
        StoreStatus.Available => Entry.IsFree ? "Get" : CanBuy ? $"Buy {PriceText}" : "Coming soon",
        _ => "Not available",
    };

    public bool CanAct => !IsBusy && Status switch
    {
        StoreStatus.Included or StoreStatus.Installed => !IsInUse,
        StoreStatus.Update => true,
        StoreStatus.Available => Entry.IsFree || CanBuy,
        _ => false,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwned), nameof(StatusText), nameof(ActionLabel), nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(State), nameof(StateText), nameof(IsLocked), nameof(LockNote))]
    public partial StoreStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(CanAct))]
    public partial bool IsInUse { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct), nameof(ActionLabel))]
    public partial bool IsBusy { get; set; }

    /// <summary>A short problem shown under the button, such as a failed download.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = "";

    /// <summary>What to draw: the character or accessory itself. Null until it has been read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterPack), nameof(Outfit), nameof(HasPreview))]
    public partial Pack? Pack { get; set; }

    /// <summary>The character to draw an accessory on: the one in use.</summary>
    [ObservableProperty]
    public partial string BaseCharacter { get; set; } = PackId.DefaultCharacter;

    [ObservableProperty]
    public partial IReadOnlyList<PreviewTileViewModel> Tiles { get; set; } = [];

    /// <summary>For a character: the accessories that come with it, each worn by it. Empty for anything else.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBundled))]
    public partial IReadOnlyList<PreviewTileViewModel> BundledTiles { get; set; } = [];

    /// <summary>For a character: how many more accessories it works with, such as "Plus 3 more accessories you can add".</summary>
    [ObservableProperty]
    public partial string MoreText { get; set; } = "";

    public bool HasBundled => BundledTiles.Count > 0;

    /// <summary>For a character: some of the other accessories, each worn by it, with what you can do about each. Owned ones come first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtras))]
    public partial IReadOnlyList<ExtraAccessory> Extras { get; set; } = [];

    public bool HasExtras => Extras.Count > 0;

    public CharacterPack? CharacterPack => Pack as CharacterPack;

    public IReadOnlyList<AccessoryPack>? Outfit => Pack is AccessoryPack accessory ? [accessory] : null;

    /// <summary>A character is drawn from its own pack; the picture behind an accessory is the character you use.</summary>
    public string DrawnCharacter => IsCharacter ? PackId.DefaultCharacter : BaseCharacter;

    public bool HasPreview => Pack is not null;

    partial void OnBaseCharacterChanged(string value) => OnPropertyChanged(nameof(DrawnCharacter));

    [RelayCommand]
    private void Open() => open(this);

    [RelayCommand]
    private void Act() => act(this);
}

/// <summary>
/// The page where characters and accessories are found: a grid, a page for each, and one button that does the sensible
/// thing. Getting something shows the same install card as opening a file, so nothing is installed without a preview.
/// Where the list comes from is an <see cref="IStoreSource"/>, so a server can replace the built-in list later.
/// </summary>
public sealed partial class BrowseViewModel : ViewModelBase, IDisposable
{
    private readonly StorePayments _payments;
    private readonly IStoreSource _source;
    private readonly SettingsService _settings;
    private readonly IShellLauncher _launcher;
    private readonly DiagnosticLog _log;
    private readonly List<StoreCardViewModel> _all = [];
    private readonly Action _stopListening;
    private CancellationTokenSource? _loading;

    public BrowseViewModel(IStoreSource source, SettingsService settings, IShellLauncher launcher, DiagnosticLog log, StorePayments payments)
    {
        _payments = payments;
        _source = source;
        _settings = settings;
        _launcher = launcher;
        _log = log;

        // An install or removal changes what each card offers. Listen weakly, and stop for good when disposed.
        var self = new WeakReference<BrowseViewModel>(this);
        Action? onCatalogChanged = null;
        onCatalogChanged = () =>
        {
            if (!self.TryGetTarget(out var page))
            {
                CharacterCatalog.Changed -= onCatalogChanged;
            }
            else if (Dispatcher.UIThread.CheckAccess())
            {
                page.RefreshStatus();
            }
            else
            {
                Dispatcher.UIThread.Post(page.RefreshStatus);
            }
        };
        CharacterCatalog.Changed += onCatalogChanged;

        EventHandler<AppSettings> onSettingsChanged = (_, _) => RefreshStatus();
        _settings.Changed += onSettingsChanged;

        _stopListening = () =>
        {
            CharacterCatalog.Changed -= onCatalogChanged;
            _settings.Changed -= onSettingsChanged;
        };
    }

    /// <summary>Stops listening for changes and cancels what is loading. Whatever owns the page calls this when the app ends.</summary>
    public void Dispose()
    {
        _stopListening();
        _loading?.Cancel();
    }

    /// <summary>Set by the shell: shows the install card for a downloaded pack, and the accessories that come with it, if any.</summary>
    public Action<PackLoadResult, string, long, InstallBundle>? ShowInstall { get; set; }

    /// <summary>Everything in the list, both kinds, in the order it is shown.</summary>
    public ObservableCollection<StoreCardViewModel> Cards { get; } = [];

    /// <summary>What the current tab shows: Installed, then Not installed. A section with nothing in it is left out.</summary>
    public ObservableCollection<BrowseSection> Sections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCharactersTab), nameof(IsAccessoriesTab))]
    public partial BrowseTab Tab { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetail), nameof(IsList))]
    public partial StoreCardViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsTabEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsEmpty), nameof(IsTabEmpty))]
    public partial string ErrorText { get; set; } = "";

    /// <summary>Said above the grid when the list loaded but not quite fully, such as when the online part couldn't be reached.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    public bool IsCharactersTab => Tab == BrowseTab.Characters;

    public bool IsAccessoriesTab => Tab == BrowseTab.Accessories;

    public bool IsDetail => Selected is not null;

    public bool IsList => Selected is null;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>Nothing to show for this filter, and nothing still loading or failed.</summary>
    public bool IsEmpty => !IsLoading && !HasError && Cards.Count == 0;

    /// <summary>Nothing to show on this tab, and nothing still loading or failed.</summary>
    public bool IsTabEmpty => !IsLoading && !HasError && Sections.Count == 0;

    partial void OnTabChanged(BrowseTab value) => BuildSections();

    [RelayCommand]
    private void ShowCharacters() => Tab = BrowseTab.Characters;

    [RelayCommand]
    private void ShowAccessories() => Tab = BrowseTab.Accessories;

    [RelayCommand]
    private void Back() => Selected = null;

    /// <summary>Reads the list again. Called each time the page is opened, so it is never out of date.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        _loading?.Cancel();
        var cancel = _loading = new CancellationTokenSource();

        IsLoading = true;
        ErrorText = "";
        Notice = "";
        try
        {
            var result = await _source.LoadAsync(cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            if (!result.Ok)
            {
                _log.Warning("Browse", $"The character list couldn't be read: {result.Error}");
                ErrorText = "The list of characters couldn't be loaded.";
                return;
            }

            if (result.Skipped > 0)
            {
                _log.Warning("Browse", $"{result.Skipped} entries in the character list were skipped because they were invalid.");
            }

            Notice = result.Notice ?? "";
            var openedId = Selected?.Entry.Id;
            _all.Clear();
            _all.AddRange(result.Entries.Select(entry => new StoreCardViewModel(entry, Open, Act, _payments.Enabled)));
            Selected = _all.FirstOrDefault(card => card.Entry.Id == openedId);
            RefreshStatus();
            ApplyOrder();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (!cancel.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }

        await LoadPreviewsAsync(cancel.Token);
    }

    /// <summary>What each card says and offers, from what is installed and in use now.</summary>
    public void RefreshStatus()
    {
        var current = _settings.Current;
        foreach (var card in _all)
        {
            var entry = card.Entry;
            if (entry.Kind == PackKind.Character)
            {
                var owned = CharacterCatalog.All.FirstOrDefault(item => item.Id == entry.Id);
                card.Status = StoreStatusOf.For(entry, owned?.BuiltIn == true, owned?.Pack.Header.Version);
                card.IsInUse = owned is not null && current.Character == entry.Id;
                card.Pack ??= owned?.Pack;
            }
            else
            {
                var owned = CharacterCatalog.FindAccessory(entry.Id);
                card.Status = StoreStatusOf.For(entry, owned?.BuiltIn == true, owned?.Pack.Header.Version);
                card.IsInUse = owned is not null && current.Outfit.GetValueOrDefault(OutfitSlots.KeyOf(owned.Slot)) == entry.Id;
                card.Pack ??= owned?.Pack;
            }

            card.BaseCharacter = CharacterCatalog.Find(current.Character).Id;
            RebuildTiles(card);
        }

        // Installing or removing something moves its card between the two sections.
        BuildSections();
    }

    private void ApplyOrder()
    {
        Cards.Clear();
        // What can be got or updated comes first; the rest keep the order of the list.
        foreach (var card in _all.OrderBy(card => card.Status switch
        {
            StoreStatus.Update => 0,
            StoreStatus.Available => 1,
            StoreStatus.Unavailable => 3,
            _ => 2,
        }))
        {
            Cards.Add(card);
        }

        BuildSections();
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// The pictures on a card's page. A character also gets the accessories that come with it, each worn by it, and a line
    /// saying how many more accessories it works with. Whatever of the set isn't loaded yet is left out until it arrives.
    /// </summary>
    private void RebuildTiles(StoreCardViewModel card)
    {
        if (card.Pack is not CharacterPack character)
        {
            card.Tiles = card.Pack is { } other ? PreviewTiles.For(other, card.BaseCharacter) : [];
            return;
        }

        // A character you don't have yet is shown wearing what it will start out wearing; one you have is shown as it is.
        var bundled = card.Entry.Included.Select(AccessoryPackOf).OfType<AccessoryPack>().ToList();
        card.Tiles = PreviewTiles.For(character, card.BaseCharacter, card.IsOwned ? null : bundled);
        card.BundledTiles = PreviewTiles.Bundled(character, bundled);

        // The other accessories, tried on this character. Ones you can wear now come first, then the rest in the list's order.
        // An accessory whose picture hasn't arrived yet is left out until it does.
        var others = _all.Where(other => !other.IsCharacter && !card.Entry.Included.Contains(other.Entry.Id)).ToList();
        card.Extras =
        [
            .. others
                .Where(other => other.Pack is AccessoryPack)
                .OrderBy(other => other.IsOwned ? 0 : 1)
                .Take(MaxExtrasShown)
                .Select(other => new ExtraAccessory(other, new PreviewTileViewModel(DripPose.Idle, character, PackId.DefaultCharacter, [(AccessoryPack)other.Pack!], other.Name))),
        ];
        card.MoreText = PreviewTiles.MoreText(card.Entry.Included.Count, others.Count);
    }

    /// <summary>How many extras a character's page shows; the rest are in the Accessories tab.</summary>
    internal const int MaxExtrasShown = 8;

    private AccessoryPack? AccessoryPackOf(string id) =>
        CharacterCatalog.FindAccessory(id)?.Pack ?? _all.FirstOrDefault(card => card.Entry.Id == id)?.Pack as AccessoryPack;

    /// <summary>Sorts the current tab into Installed and Not installed.</summary>
    private void BuildSections()
    {
        var wantCharacters = Tab == BrowseTab.Characters;
        var inTab = Cards.Where(card => card.IsCharacter == wantCharacters).ToList();

        Sections.Clear();
        AddSection("Installed", [.. inTab.Where(card => card.IsOwned)], groupBySlot: !wantCharacters);
        // The list doesn't say which slot an accessory is for until it is installed, so these can't be grouped yet.
        AddSection("Not installed", [.. inTab.Where(card => !card.IsOwned)], groupBySlot: false);
        OnPropertyChanged(nameof(IsTabEmpty));
    }

    private void AddSection(string title, List<StoreCardViewModel> cards, bool groupBySlot)
    {
        if (cards.Count == 0)
        {
            return;
        }

        var groups = new List<BrowseGroup>();
        if (groupBySlot)
        {
            // Only a slot with several options gets a heading: you can wear one of them, not all.
            var bySlot = cards.GroupBy(card => CharacterCatalog.FindAccessory(card.Entry.Id)?.Slot).ToList();
            foreach (var slot in OutfitSlots.All)
            {
                if (bySlot.FirstOrDefault(group => group.Key == slot) is { } options && options.Count() > 1)
                {
                    groups.Add(new BrowseGroup(OutfitSlots.TitleOf(slot), [.. options]));
                }
            }

            var rest = cards.Where(card => !groups.Any(group => group.Cards.Contains(card))).ToList();
            if (rest.Count > 0)
            {
                groups.Add(new BrowseGroup("", rest));
            }
        }
        else
        {
            groups.Add(new BrowseGroup("", cards));
        }

        Sections.Add(new BrowseSection(title, groups));
    }

    private void Open(StoreCardViewModel card) => Selected = card;

    private void Act(StoreCardViewModel card)
    {
        switch (card.Status)
        {
            case StoreStatus.Included or StoreStatus.Installed:
                Use(card);
                break;
            case StoreStatus.Update:
                _ = GetAsync(card);
                break;
            case StoreStatus.Available when card.Entry.IsFree:
                _ = GetAsync(card);
                break;
            case StoreStatus.Available when card.CanBuy: // never while payments are off, even if the list has an address
                if (!_launcher.OpenUrl(card.Entry.BuyUrl!))
                {
                    card.Message = "The page couldn't be opened.";
                }

                break;
        }
    }

    /// <summary>Switches to a character, or puts an accessory on.</summary>
    private void Use(StoreCardViewModel card)
    {
        if (card.Entry.Kind == PackKind.Character)
        {
            _settings.Update(current => current with { Character = card.Entry.Id });
        }
        else if (CharacterCatalog.FindAccessory(card.Entry.Id) is { } accessory)
        {
            _settings.Update(current => current.WithAccessory(accessory.Slot, accessory.Id));
        }
    }

    /// <summary>Downloads an item and shows the install card for it. Nothing is installed until the user presses Install there.</summary>
    private async Task GetAsync(StoreCardViewModel card)
    {
        if (card.IsBusy)
        {
            return;
        }

        card.IsBusy = true;
        card.Message = "";
        try
        {
            var bytes = await _source.FetchPackAsync(card.Entry, CancellationToken.None);
            var (result, problem) = Check(card.Entry, bytes);
            if (problem is not null)
            {
                card.Message = problem; // it isn't what the list promised, so it never reaches the install card
                return;
            }

            var (bundle, bundledBytes, bundleProblem) = await FetchBundledAsync(card.Entry);
            if (bundleProblem is not null)
            {
                card.Message = bundleProblem; // the character is never offered without the set it comes with
                return;
            }

            ShowInstall?.Invoke(result, "the HeySip character list", bytes.Length + bundledBytes, bundle);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            _log.Warning("Browse", $"Downloading {card.Entry.Id} failed: {exception.Message}");
            card.Message = "It couldn't be downloaded. Check your connection and try again.";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    /// <summary>
    /// Downloads and checks the accessories that come with a character and aren't installed yet. It is all or nothing: if one
    /// is missing from the list or fails a check, none are returned and <c>Problem</c> says why.
    /// </summary>
    private async Task<(InstallBundle Bundle, long Bytes, string? Problem)> FetchBundledAsync(StoreEntry character)
    {
        var toInstall = new List<PackLoadResult>();
        var all = new List<AccessoryPack>();
        long total = 0;
        foreach (var id in character.Included)
        {
            if (CharacterCatalog.FindAccessory(id) is { } have)
            {
                all.Add(have.Pack); // already installed, or built in
                continue;
            }

            var entry = _all.Select(card => card.Entry).FirstOrDefault(item => item.Id == id && item.Kind == PackKind.Accessory);
            if (entry?.PackLocation is null)
            {
                return (InstallBundle.None, 0, "An accessory that comes with it isn't available right now, so it can't be installed.");
            }

            var bytes = await _source.FetchPackAsync(entry, CancellationToken.None);
            var (result, problem) = Check(entry, bytes);
            if (problem is null && result.Pack is not AccessoryPack)
            {
                problem = result.Error ?? "It isn't an accessory.";
            }

            if (problem is not null)
            {
                return (InstallBundle.None, 0, $"An accessory that comes with it couldn't be used. {problem}");
            }

            toInstall.Add(result);
            all.Add((AccessoryPack)result.Pack!);
            total += bytes.Length;
        }

        return (new InstallBundle(toInstall, all), total, null);
    }

    /// <summary>
    /// Makes sure what arrived is what the list promised: the size, the checksum when there is one, and that it really is
    /// the item that was asked for (<c>Problem</c> says what is wrong when it isn't). Then it goes through every pack check,
    /// as a pack from a file does; a pack that fails those comes back as a failed <c>Result</c>.
    /// </summary>
    internal static (PackLoadResult Result, string? Problem) Check(StoreEntry entry, byte[] bytes)
    {
        if (bytes.Length > PackLimits.MaxPackBytes)
        {
            return (PackLoadResult.Failure("too big"), "The download was too big, so it was thrown away.");
        }

        if (entry.Sha256 is { } expected && !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expected, StringComparison.Ordinal))
        {
            return (PackLoadResult.Failure("checksum"), "The download wasn't what the list promised, so it was thrown away.");
        }

        using var stream = new MemoryStream(bytes);
        var result = PackLoader.FromZip(stream, new PackLoadOptions(AppVersion.Current));
        if (result.Pack is { } pack && (pack.Header.Id != entry.Id || pack.Header.Version != entry.Version))
        {
            return (PackLoadResult.Failure("different item"), "The download wasn't the item the list described, so it was thrown away.");
        }

        return (result, null);
    }

    /// <summary>Reads the pictures of items that aren't installed, one at a time, so cards fill in as they arrive.</summary>
    private async Task LoadPreviewsAsync(CancellationToken cancel)
    {
        foreach (var card in _all.Where(card => card.Pack is null && card.Entry.PackLocation is not null).ToList())
        {
            try
            {
                var bytes = await _source.FetchPackAsync(card.Entry, cancel);
                if (cancel.IsCancellationRequested)
                {
                    return;
                }

                if (Check(card.Entry, bytes).Result.Pack is { } pack)
                {
                    card.Pack = pack;
                    RebuildTiles(card);

                    // Every character can show this accessory now: the ones it comes with, and the ones it is tried on.
                    if (!card.IsCharacter)
                    {
                        foreach (var character in _all.Where(other => other.IsCharacter))
                        {
                            RebuildTiles(character);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException)
            {
                _log.Warning("Browse", $"The picture of {card.Entry.Id} couldn't be loaded: {exception.Message}");
            }
        }
    }
}
