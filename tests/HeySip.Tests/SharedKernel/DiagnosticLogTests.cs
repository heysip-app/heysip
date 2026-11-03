using HeySip.Core.SharedKernel.Diagnostics;
using HeySip.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.SharedKernel;

public sealed class DiagnosticLogTests : IDisposable
{
    private readonly TempData _data = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly DiagnosticLog _log;

    public DiagnosticLogTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _log = new DiagnosticLog(_time, Path.Combine(_data.Root, "logs"));
    }

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Entries_are_written_with_level_area_and_message()
    {
        _log.Info("App", "started");
        _log.Warning("Settings", "careful");

        var lines = _log.Tail();

        Assert.Equal(2, lines.Count);
        Assert.Contains("INFO  App: started", lines[0]);
        Assert.Contains("WARN  Settings: careful", lines[1]);
        Assert.StartsWith("2026-03-10T12:00:00", lines[0]);
    }

    [Fact]
    public void Exceptions_are_logged_with_their_stack_trace()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException exception)
        {
            _log.Error("UI", "it broke", exception);
        }

        var text = string.Join('\n', _log.Tail());
        Assert.Contains("ERROR UI: it broke", text);
        Assert.Contains("InvalidOperationException: boom", text);
        Assert.Contains("at HeySip.Tests", text);
    }

    [Fact]
    public void Tail_returns_only_the_most_recent_lines()
    {
        for (var i = 0; i < 50; i++)
        {
            _log.Info("App", $"line {i}");
        }

        var tail = _log.Tail(5);

        Assert.Equal(5, tail.Count);
        Assert.Contains("line 49", tail[^1]);
        Assert.Contains("line 45", tail[0]);
    }

    [Fact]
    public void Each_day_gets_its_own_file_and_old_files_are_pruned()
    {
        var logs = Path.Combine(_data.Root, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "heysip-20260101.log"), "ancient");
        File.WriteAllText(Path.Combine(logs, "heysip-20260309.log"), "yesterday");
        File.WriteAllText(Path.Combine(logs, "notes.txt"), "not a log; must be left alone");

        _log.Info("App", "today");

        Assert.False(File.Exists(Path.Combine(logs, "heysip-20260101.log")));
        Assert.True(File.Exists(Path.Combine(logs, "heysip-20260309.log")));
        Assert.True(File.Exists(Path.Combine(logs, "heysip-20260310.log")));
        Assert.True(File.Exists(Path.Combine(logs, "notes.txt")));
    }

    [Fact]
    public void A_log_that_cannot_be_written_never_throws()
    {
        var blocked = Path.Combine(_data.Root, "blocked");
        File.WriteAllText(blocked, "this is a file where the log folder should be");
        var log = new DiagnosticLog(_time, blocked);

        var exception = Record.Exception(() =>
        {
            log.Error("App", "nowhere to go", new IOException("x"));
            log.Tail();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void An_app_left_running_for_weeks_keeps_pruning_old_files()
    {
        var logs = Path.Combine(_data.Root, "logs");
        _log.Info("App", "day one");
        Assert.True(File.Exists(Path.Combine(logs, "heysip-20260310.log")));

        _time.Advance(TimeSpan.FromDays(10));
        _log.Info("App", "ten days later, same process");

        Assert.False(File.Exists(Path.Combine(logs, "heysip-20260310.log")));
        Assert.True(File.Exists(Path.Combine(logs, "heysip-20260320.log")));
    }
}
