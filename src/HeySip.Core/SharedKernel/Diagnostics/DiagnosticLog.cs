using System.Globalization;
using System.Text;

namespace HeySip.Core.SharedKernel.Diagnostics;

/// <summary>
/// A small local log file, one per day, kept for a week. It records start-up and errors only (never drink amounts or
/// the user's name) and never throws: a log that can't be written must not take the app down.
/// </summary>
public sealed class DiagnosticLog(TimeProvider time, string directory)
{
    public const int KeepDays = 7;

    private const string FilePrefix = "heysip-";

    private readonly Lock _gate = new();
    private DateOnly _prunedOn;

    public string Directory => directory;

    public void Info(string area, string message) => Write("INFO", area, message, null);

    public void Warning(string area, string message, Exception? exception = null) => Write("WARN", area, message, exception);

    public void Error(string area, string message, Exception? exception = null) => Write("ERROR", area, message, exception);

    /// <summary>The last <paramref name="lines"/> lines across today's and yesterday's files, oldest first.</summary>
    public IReadOnlyList<string> Tail(int lines = 80)
    {
        try
        {
            var today = FileFor(time.GetLocalNow());
            var yesterday = FileFor(time.GetLocalNow().AddDays(-1));
            var all = new List<string>();
            lock (_gate)
            {
                foreach (var file in new[] { yesterday, today }.Where(File.Exists))
                {
                    all.AddRange(File.ReadAllLines(file));
                }
            }

            return all.Count <= lines ? all : all.GetRange(all.Count - lines, lines);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Write(string level, string area, string message, Exception? exception)
    {
        var now = time.GetLocalNow();
        var text = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
            .Append(' ').Append(level.PadRight(5))
            .Append(' ').Append(area)
            .Append(": ").AppendLine(message);
        if (exception is not null)
        {
            foreach (var line in exception.ToString().Split('\n'))
            {
                text.Append("    ").AppendLine(line.TrimEnd('\r'));
            }
        }

        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(directory);
                File.AppendAllText(FileFor(now), text.ToString());
                var today = DateOnly.FromDateTime(now.DateTime);
                if (_prunedOn != today)
                {
                    _prunedOn = today;
                    Prune(now);
                }
            }
        }
        catch (Exception ioProblem) when (ioProblem is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write: drop the entry.
        }
    }

    private string FileFor(DateTimeOffset moment) =>
        Path.Combine(directory, $"{FilePrefix}{moment:yyyyMMdd}.log");

    private void Prune(DateTimeOffset now)
    {
        var oldest = FileFor(now.AddDays(-(KeepDays - 1)));
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, FilePrefix + "*.log"))
        {
            if (string.CompareOrdinal(Path.GetFileName(file), Path.GetFileName(oldest)) < 0)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
