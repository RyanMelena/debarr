using Debarr.Hosting;
using Debarr.Playing;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace Debarr.Tests.Hosting;

public partial class LogFileDirectoryTests : IDisposable
{
    private static readonly Func<ILogger, string, IDisposable?> OuterScope = LoggerMessage.DefineScope<string>("Playback on {Player}");
    private static readonly Func<ILogger, string, IDisposable?> InnerScope = LoggerMessage.DefineScope<string>("{Command} 1");

    private readonly string _directory = Directory.CreateTempSubdirectory("debarr-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void List_returns_the_log_files_newest_first()
    {
        Write("debarr-20260926.log", "a");
        Write("debarr-20260927.log", "b");
        Write("other.txt", "c");

        var files = new LogFileDirectory(_directory).List();

        Assert.Equal(["debarr-20260927.log", "debarr-20260926.log"], files.Select(file => file.Name));
    }

    [Fact]
    public void List_is_empty_when_the_directory_is_missing()
    {
        var files = new LogFileDirectory(Path.Combine(_directory, "logs")).List();

        Assert.Empty(files);
    }

    [Fact]
    public void Find_returns_only_a_listed_file()
    {
        Write("config.json", "{}");
        var logs = Directory.CreateDirectory(Path.Combine(_directory, "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "debarr-20260927.log"), "a");
        File.WriteAllText(Path.Combine(logs, "other.txt"), "b");
        var logFiles = new LogFileDirectory(logs);

        Assert.Equal(Path.Combine(logs, "debarr-20260927.log"), logFiles.Find("debarr-20260927.log")?.FullName);
        Assert.Null(logFiles.Find("../config.json"));
        Assert.Null(logFiles.Find("other.txt"));
        Assert.Null(logFiles.Find("debarr-20260101.log"));
    }

    [Fact]
    public void A_line_logged_inside_two_scopes_reaches_the_file_with_both_and_reads_back_as_one_entry()
    {
        var before = DateTimeOffset.Now.AddMilliseconds(-1);
        using (var fileLogger = new LogFileDirectory(_directory).CreateLogger())
        using (var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(new SerilogLoggerProvider(fileLogger))))
        {
            var logger = loggerFactory.CreateLogger("Debarr.Tests");
            using (OuterScope(logger, "Theater"))
            using (InnerScope(logger, "RecordPlayback"))
            {
                LogDelivered(logger, "automation");
            }

            LogDelivered(logger, "webhook");
        }

        var lines = File.ReadAllLines(Assert.Single(new LogFileDirectory(_directory).List()).FullName);

        var entries = LogEntry.Parse(lines);
        Assert.Equal(2, entries.Count);
        Assert.InRange(entries[0].Time, before, DateTimeOffset.Now);
        Assert.Equal(
            (LogLevel.Information, "Debarr.Tests", "Delivered to automation. (Playback on Theater, RecordPlayback 1)", (string?)null),
            (entries[0].Level, entries[0].Source, entries[0].Message, entries[0].Detail));
        Assert.Equal("Delivered to webhook.", entries[1].Message);
    }

    [Fact]
    public void An_enum_reaches_the_file_as_its_name_without_quotes()
    {
        using (var fileLogger = new LogFileDirectory(_directory).CreateLogger())
        using (var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(new SerilogLoggerProvider(fileLogger))))
        {
            LogSent(loggerFactory.CreateLogger("Debarr.Tests"), NotificationAspectRatioSource.Player);
        }

        var entry = Assert.Single(LogEntry.Parse(File.ReadAllLines(Assert.Single(new LogFileDirectory(_directory).List()).FullName)));
        Assert.Equal("Sent from Player.", entry.Message);
    }

    [Fact]
    public async Task ReadLastLinesAsync_returns_the_last_lines_oldest_first()
    {
        var path = Write("debarr-20260927.log", "one\ntwo\nthree\nfour\nfive\n");

        var lines = await LogFileDirectory.ReadLastLinesAsync(new FileInfo(path), 3, TestContext.Current.CancellationToken);

        Assert.Equal(["three", "four", "five"], lines);
    }

    [Fact]
    public async Task ReadLastLinesAsync_reads_a_file_another_stream_is_writing()
    {
        var path = Path.Combine(_directory, "debarr-20260927.log");
        await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await writer.WriteAsync("one\ntwo\nthree\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        var lines = await LogFileDirectory.ReadLastLinesAsync(new FileInfo(path), 2, TestContext.Current.CancellationToken);

        Assert.Equal(["two", "three"], lines);
    }

    [LoggerMessage(LogLevel.Information, "Sent from {Source}.")]
    private static partial void LogSent(ILogger logger, NotificationAspectRatioSource source);

    [LoggerMessage(LogLevel.Information, "Delivered to {Notifier}.")]
    private static partial void LogDelivered(ILogger logger, string notifier);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
