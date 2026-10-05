using Debarr.Hosting;
using Microsoft.Extensions.Logging;

namespace Debarr.Tests.Hosting;

public class LogEntryTests
{
    [Fact]
    public void A_log_line_gives_its_time_level_source_and_message()
    {
        string[] lines = ["2026-09-27 19:55:07.857 -05:00 [WRN] Debarr.Playing.PlayerConnectionService: Bedroom disconnected: refused"];

        var entry = Assert.Single(LogEntry.Parse(lines));

        Assert.Equal(new DateTimeOffset(2026, 9, 27, 19, 55, 7, 857, TimeSpan.FromHours(-5)), entry.Time);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Debarr.Playing.PlayerConnectionService", entry.Source);
        Assert.Equal("Bedroom disconnected: refused", entry.Message);
        Assert.Null(entry.Detail);
    }

    [Fact]
    public void Lines_that_start_no_entry_join_the_entry_before_and_lines_before_the_first_entry_are_left_out()
    {
        string[] lines =
        [
            "   at Debarr.Earlier()",
            "2026-09-27 19:55:07.857 -05:00 [ERR] Debarr.Scanning.LibraryScanner: Could not record the scan. The device is not ready.",
            "System.IO.IOException: The device is not ready.",
            "   at Debarr.Scanning.LibraryScanner.LibraryScanAsync()",
            "",
            "2026-09-27 19:55:08.001 -05:00 [INF] Microsoft.Hosting.Lifetime: Application is shutting down...",
        ];

        var entries = LogEntry.Parse(lines);

        Assert.Equal([LogLevel.Error, LogLevel.Information], entries.Select(entry => entry.Level));
        Assert.Equal("System.IO.IOException: The device is not ready.\n   at Debarr.Scanning.LibraryScanner.LibraryScanAsync()", entries[0].Detail);
        Assert.Null(entries[1].Detail);
    }
}
