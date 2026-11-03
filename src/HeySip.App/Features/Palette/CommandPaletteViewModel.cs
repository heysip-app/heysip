using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.Palette;

/// <summary>One thing the palette can do. Hidden when <see cref="IsAvailable"/> says it doesn't apply right now.</summary>
public sealed record PaletteCommand(
    string Id,
    string Category,
    string Title,
    Action Run,
    string Shortcut = "",
    string Keywords = "",
    Func<bool>? IsAvailable = null)
{
    public bool Available => IsAvailable?.Invoke() ?? true;

    /// <summary>What a typed query is matched against: the category, the title and any extra words.</summary>
    public string SearchText => $"{Category} {Title} {Keywords}";
}

/// <summary>A row in the palette's result list.</summary>
public sealed partial class PaletteItemViewModel(PaletteCommand command, Action<PaletteItemViewModel> run) : ObservableObject
{
    public PaletteCommand Command { get; } = command;

    public string Category => Command.Category;

    public string Title => Command.Title;

    public string Shortcut => Command.Shortcut;

    public bool HasShortcut => Command.Shortcut.Length > 0;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Run() => run(this);
}

/// <summary>
/// The command palette: type to filter, arrow keys to choose, Enter to run, Escape to close. The command list is rebuilt
/// each time it opens, so availability (for example "Resume" only while paused) is always current.
/// </summary>
public sealed partial class CommandPaletteViewModel(
    Func<IReadOnlyList<PaletteCommand>> provider,
    Func<string, IEnumerable<PaletteCommand>>? queryProvider = null) : ViewModelBase
{
    public ObservableCollection<PaletteItemViewModel> Results { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    /// <summary>Raised when the highlighted row changes, so the view can scroll it into sight.</summary>
    public event Action<int>? SelectionMoved;

    private IReadOnlyList<PaletteCommand> _commands = [];

    public void Open()
    {
        _commands = provider().Where(command => command.Available).ToList();
        Query = "";
        Filter();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    public void MoveNext() => Move(+1);

    [RelayCommand]
    public void MovePrevious() => Move(-1);

    /// <summary>Runs the highlighted command. The palette closes first, so a command may open something else.</summary>
    [RelayCommand]
    public void RunSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            Run(Results[SelectedIndex]);
        }
    }

    partial void OnQueryChanged(string value) => Filter();

    partial void OnSelectedIndexChanged(int value) => Highlight(value);

    private void Highlight(int index)
    {
        for (var i = 0; i < Results.Count; i++)
        {
            Results[i].IsSelected = i == index;
        }

        SelectionMoved?.Invoke(index);
    }

    private void Run(PaletteItemViewModel item)
    {
        IsOpen = false;
        item.Command.Run();
    }

    private void Move(int direction)
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = (SelectedIndex + direction + Results.Count) % Results.Count;
    }

    private void Filter()
    {
        var ranked = string.IsNullOrWhiteSpace(Query)
            ? _commands.Select((command, order) => (command, score: 0, order))
            : _commands
                .Select((command, order) => (command, score: FuzzyMatcher.Score(Query, command.SearchText), order))
                .Where(match => match.score.HasValue)
                .Select(match => (match.command, score: match.score!.Value, match.order))
                .OrderByDescending(match => match.score)
                .ThenBy(match => match.order);

        Results.Clear();

        // Commands made from what was typed, such as a date to jump to, come before the fuzzy matches.
        foreach (var command in string.IsNullOrWhiteSpace(Query) ? [] : queryProvider?.Invoke(Query.Trim()) ?? [])
        {
            Results.Add(new PaletteItemViewModel(command, Run));
        }

        foreach (var (command, _, _) in ranked)
        {
            Results.Add(new PaletteItemViewModel(command, Run));
        }

        HasNoResults = Results.Count == 0;

        // The list was rebuilt, so the first row must be highlighted again even if the index was already 0.
        var alreadyFirst = SelectedIndex == 0;
        SelectedIndex = 0;
        if (alreadyFirst)
        {
            Highlight(0);
        }
    }
}
