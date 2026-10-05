using Serilog;
using Serilog.Core;

namespace Debarr.Hosting;

/// <summary>The directory that holds the daily log files, named debarr-yyyyMMdd.log.</summary>
public sealed class LogFileDirectory(string path)
{
    private const string SearchPattern = "debarr-*.log";

    public string Path { get; } = path;

    /// <summary>The path the file sink rolls from, which gains the date before the extension.</summary>
    public string RollingFilePath => System.IO.Path.Combine(Path, "debarr-.log");

    public Logger CreateLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With<LogScopesEnricher>()
            .WriteTo.File(
                RollingFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:l}{Scopes}{NewLine}{Exception}")
            .CreateLogger();

    /// <summary>The log files, newest first, or none when the directory is missing.</summary>
    public IReadOnlyList<FileInfo> List()
    {
        var directory = new DirectoryInfo(Path);
        if (!directory.Exists)
        {
            return [];
        }

        // The date in each name sorts the files by age.
        return [.. directory.EnumerateFiles(SearchPattern).OrderByDescending(file => file.Name, StringComparer.Ordinal)];
    }

    /// <summary>The listed log file with this name, or null when the name is none of them.</summary>
    public FileInfo? Find(string fileName) =>
        List().FirstOrDefault(file => string.Equals(file.Name, fileName, StringComparison.Ordinal));

    /// <summary>The file's last <paramref name="count"/> lines, oldest first.</summary>
    public static async Task<IReadOnlyList<string>> ReadLastLinesAsync(FileInfo file, int count, CancellationToken cancellationToken)
    {
        // The file sink holds the current file open for writing and deletes the oldest past the retained count.
        await using var stream = new FileStream(
            file.FullName,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        using var reader = new StreamReader(stream);

        var lines = new Queue<string>(count);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (lines.Count == count)
            {
                lines.Dequeue();
            }

            lines.Enqueue(line);
        }

        return [.. lines];
    }
}
