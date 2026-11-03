using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.App.Shared.Controls;

public enum DripPose
{
    Idle,
    Drinking,
    Happy,
    Worried,
    Sleepy,
    Waving,
    Sleeping,
    Cheering,
    Thinking,
    Stretching,
    Shivering,
}

/// <summary>
/// The HeySip companion. It draws whichever character it is given, a built-in one by <see cref="Character"/> or any
/// loaded <see cref="Pack"/>, optionally wearing an <see cref="Outfit"/>. The drawing itself lives in
/// <see cref="PreparedCharacter"/>; this control decides when to animate and what to ask it for.
/// Coordinates are in a 100x140 design box scaled to fit the control.
/// </summary>
public sealed class DripControl : Control
{
    private const double DesignWidth = 100;
    private const double DesignHeight = 140;

    public static readonly StyledProperty<DripPose> PoseProperty =
        AvaloniaProperty.Register<DripControl, DripPose>(nameof(Pose));

    public static readonly StyledProperty<string> CharacterProperty =
        AvaloniaProperty.Register<DripControl, string>(nameof(Character), defaultValue: PackId.DefaultCharacter);

    public static readonly StyledProperty<CharacterPack?> PackProperty =
        AvaloniaProperty.Register<DripControl, CharacterPack?>(nameof(Pack));

    public static readonly StyledProperty<IReadOnlyList<AccessoryPack>?> OutfitProperty =
        AvaloniaProperty.Register<DripControl, IReadOnlyList<AccessoryPack>?>(nameof(Outfit));

    public static readonly StyledProperty<bool> IsWalkingProperty =
        AvaloniaProperty.Register<DripControl, bool>(nameof(IsWalking));

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<DripControl, bool>(nameof(IsAnimated), defaultValue: true);

    // Walking needs smooth motion; standing still only breathes and blinks, which looks fine at a fraction of the cost.
    private static readonly TimeSpan WalkingFrame = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan RestingFrame = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _timer = new() { Interval = RestingFrame };

    static DripControl()
    {
        AffectsRender<DripControl>(PoseProperty, CharacterProperty, PackProperty, OutfitProperty, IsWalkingProperty);
    }

    public DripControl()
    {
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    public DripPose Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    /// <summary>The id of the character to draw, such as "heysip.drip". An id that can't be found draws the default one. Ignored while <see cref="Pack"/> is set.</summary>
    public string Character
    {
        get => GetValue(CharacterProperty);
        set => SetValue(CharacterProperty, value);
    }

    /// <summary>A loaded character pack to draw instead of a built-in one.</summary>
    public CharacterPack? Pack
    {
        get => GetValue(PackProperty);
        set => SetValue(PackProperty, value);
    }

    /// <summary>Accessories worn by the character: at most one per slot.</summary>
    public IReadOnlyList<AccessoryPack>? Outfit
    {
        get => GetValue(OutfitProperty);
        set => SetValue(OutfitProperty, value);
    }

    public bool IsWalking
    {
        get => GetValue(IsWalkingProperty);
        set => SetValue(IsWalkingProperty, value);
    }

    /// <summary>When false the mascot is drawn once (no bobbing or blinking); used for icons and thumbnails.</summary>
    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    private TopLevel? _topLevel;
    private string? _preparedId;
    private int _preparedVersion = -1;
    private PreparedCharacter? _prepared;
    private IReadOnlyList<AccessoryPack>? _outfitSource;
    private PreparedAccessory[]? _preparedOutfit;

    /// <summary>True while the animation timer is running; it only runs when the mascot can actually be seen.</summary>
    internal bool IsTimerRunning => _timer.IsEnabled;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        }

        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel = null;
        }

        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == IsWalkingProperty || change.Property == IsAnimatedProperty)
        {
            UpdateTimer();
        }
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty || change.Property == Window.WindowStateProperty)
        {
            UpdateTimer();
        }
    }

    /// <summary>
    /// Animate only while the mascot can be seen: attached, effectively visible, and its window neither hidden to the
    /// tray nor minimized. Faster while walking.
    /// </summary>
    private void UpdateTimer()
    {
        var hiddenByWindow = _topLevel is Window { WindowState: WindowState.Minimized } || _topLevel is { IsVisible: false };
        if (IsAnimated && IsEffectivelyVisible && !hiddenByWindow && this.IsAttachedToVisualTree())
        {
            _timer.Interval = IsWalking ? WalkingFrame : RestingFrame;
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? DesignWidth : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? DesignHeight : availableSize.Height;
        var scale = Math.Min(width / DesignWidth, height / DesignHeight);
        return new Size(DesignWidth * scale, DesignHeight * scale);
    }

    public override void Render(DrawingContext context)
    {
        var character = PreparedCharacterNow();
        var outfit = PreparedOutfit();
        var ticks = Environment.TickCount64;
        var frame = new CharacterFrame(ticks / 1000.0, ticks, IsAnimated, IsWalking);

        character.Draw(context, Bounds.Size, StandardPoseOf(Pose), frame, outfit);
    }

    /// <summary>The character ready to draw. Looked up again only when the id or the catalogue changes, not on every frame.</summary>
    private PreparedCharacter PreparedCharacterNow()
    {
        if (Pack is { } pack)
        {
            return CharacterLibrary.Prepare(pack);
        }

        var id = Character;
        var version = CharacterCatalog.Version;
        if (_prepared is null || id != _preparedId || version != _preparedVersion)
        {
            _prepared = CharacterLibrary.For(id);
            _preparedId = id;
            _preparedVersion = version;
        }

        return _prepared;
    }

    /// <summary>The outfit ready to draw. It is rebuilt only when the outfit changes, not on every animation frame.</summary>
    private PreparedAccessory[]? PreparedOutfit()
    {
        var current = Outfit;
        if (!ReferenceEquals(current, _outfitSource))
        {
            _outfitSource = current;
            _preparedOutfit = current is { Count: > 0 } ? [.. current.Select(CharacterLibrary.Prepare)] : null;
        }

        return _preparedOutfit;
    }

    private static StandardPose StandardPoseOf(DripPose pose) => pose switch
    {
        DripPose.Drinking => StandardPose.Drinking,
        DripPose.Happy => StandardPose.Happy,
        DripPose.Worried => StandardPose.Worried,
        DripPose.Sleepy => StandardPose.Sleepy,
        DripPose.Waving => StandardPose.Waving,
        DripPose.Sleeping => StandardPose.Sleeping,
        DripPose.Cheering => StandardPose.Cheering,
        DripPose.Thinking => StandardPose.Thinking,
        DripPose.Stretching => StandardPose.Stretching,
        DripPose.Shivering => StandardPose.Shivering,
        _ => StandardPose.Idle,
    };
}
