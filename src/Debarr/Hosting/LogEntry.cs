using System.Globalization;
using System.Text.RegularExpressions;

namespace Debarr.Hosting;

/// <summary>One entry of a log file: its first line's time, level, source and message, and the lines that follow it.</summary>
/// <param name="Source">The logger's category, such as Debarr.Scanning.LibraryScanner; empty when the entry has none.</param>
/// <param name="Detail">The lines after the first, such as an exception's stack trace; null when there are none.</param>
public sealed partial record LogEntry(DateTimeOffset Time, LogLevel Level, string Source, string Message, string? Detail)
{
    /// <summary>
    /// The log entries in lines Serilog wrote, oldest first.
    /// A line that starts no entry belongs to the entry before it, and lines before the first entry are left out.
    /// </summary>
    public static IReadOnlyList<LogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        var detail = new List<string>();

        foreach (var line in lines)
        {
            var match = FirstLine().Match(line);
            if (!match.Success)
            {
                if (entries.Count > 0)
                {
                    detail.Add(line);
                }

                continue;
            }

            CompleteLastEntry();
            entries.Add(new LogEntry(
                DateTimeOffset.ParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
                match.Groups["level"].Value switch
                {
                    "VRB" => LogLevel.Trace,
                    "DBG" => LogLevel.Debug,
                    "WRN" => LogLevel.Warning,
                    "ERR" => LogLevel.Error,
                    "FTL" => LogLevel.Critical,
                    _ => LogLevel.Information,
                },
                match.Groups["source"].Value,
                match.Groups["message"].Value,
                null));
        }

        CompleteLastEntry();
        return entries;

        void CompleteLastEntry()
        {
            // Serilog ends an exception with a newline, which leaves a blank last line.
            while (detail.Count > 0 && string.IsNullOrWhiteSpace(detail[^1]))
            {
                detail.RemoveAt(detail.Count - 1);
            }

            if (detail.Count > 0)
            {
                entries[^1] = entries[^1] with { Detail = string.Join('\n', detail) };
                detail.Clear();
            }
        }
    }

    /// <summary>The first line of an entry, as the output template writes it: time, level, source and message.</summary>
    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<level>[A-Z]{3})\] (?:(?<source>[^\s:]*): )?(?<message>.*)$")]
    private static partial Regex FirstLine();
}
