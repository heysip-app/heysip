namespace HeySip.Core.Features.Companion.Packs;

/// <summary>The size and shape limits that keep a pack small, safe and cheap to draw.</summary>
public static class PackLimits
{
    public const int FormatVersion = 1;

    public const int MaxPackBytes = 1024 * 1024;
    public const int MaxUnpackedBytes = 2 * 1024 * 1024;
    public const int MaxFiles = 8;
    public const int MaxJsonBytes = 256 * 1024;
    public const int MaxJsonDepth = 8;
    public const int MaxSoundBytes = 200 * 1024;
    public const double MaxSoundSeconds = 5;

    public const int MaxShapesPerList = 64;
    public const int MaxShapesTotal = 512;
    public const int MaxPathChars = 4096;
    public const int MaxPathSegments = 256;
    public const double MaxPathNumber = 1000;

    public const int MaxPaletteColors = 16;
    public const int MaxNameLength = 32;

    // The design box is 100 by 140; things may stick out a little (hats, clouds).
    public const double MinX = -50;
    public const double MaxX = 150;
    public const double MinY = -50;
    public const double MaxY = 190;

    // Face shapes and accessories are drawn relative to an anchor.
    public const double MaxRelative = 150;

    public const double MaxStrokeWidth = 30;
    public const double MaxRadius = 150;
}
