using System.Globalization;

namespace HeySip.Core.SharedKernel;

/// <summary>Sizes in bytes, and how they are written for people.</summary>
public static class ByteSize
{
    public const int Kilobyte = 1024;
    public const int Megabyte = Kilobyte * Kilobyte;

    private const string OneDecimal = "0.#";

    /// <summary>For example "12.5 KB". Used where a file's size is shown.</summary>
    public static string InKilobytes(long bytes) =>
        $"{((double)bytes / Kilobyte).ToString(OneDecimal, CultureInfo.CurrentCulture)} KB";

    /// <summary>The size in bytes, kilobytes or megabytes, whichever reads best.</summary>
    public static string Friendly(long bytes) => bytes switch
    {
        < Kilobyte => $"{bytes} bytes",
        < Megabyte => InKilobytes(bytes),
        _ => $"{((double)bytes / Megabyte).ToString(OneDecimal, CultureInfo.CurrentCulture)} MB",
    };
}
