using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;

namespace HeySip.App.Shell;

/// <summary>One open tab. The shell owns selection and closing; the tab just reports clicks back to it.</summary>
public sealed partial class TabViewModel(
    string key,
    string title,
    ViewModelBase content,
    bool isClosable,
    Action<TabViewModel> select,
    Action<TabViewModel> close,
    string? glyph = null,
    string? caption = null) : ObservableObject
{
    /// <summary>Identifies what the tab shows, so opening the same thing twice focuses the existing tab.</summary>
    public string Key { get; } = key;

    /// <summary>The name shown in the tooltip. Day tabs rename themselves when "Today" or "Yesterday" moves on.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = title;

    public ViewModelBase Content { get; } = content;

    public bool IsClosable { get; } = isClosable;

    /// <summary>A short mark (such as the day of the month) for tabs that live in the left rail. Null for the fixed pages.</summary>
    public string? Glyph { get; } = glyph;

    /// <summary>A smaller line under the glyph (the month), so two days with the same date number can be told apart.</summary>
    public string? Caption { get; } = caption;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(this);

    [RelayCommand]
    private void Close() => close(this);
}
