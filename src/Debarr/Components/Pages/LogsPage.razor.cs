using System.Reactive;
using System.Reactive.Linq;
using Debarr.Hosting;
using MudBlazor;

namespace Debarr.Components.Pages;

/// <summary>The log files and the entries of one, checked for new entries every <see cref="CheckInterval"/> while the page is open.</summary>
public sealed partial class LogsPage(LogFileDirectory logFiles)
{
    private const int LineCount = 1000;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private static readonly LevelFilter[] Filters =
    [
        new("all", "All", LogLevel.Trace),
        new("warnings", "Warnings", LogLevel.Warning),
        new("errors", "Errors", LogLevel.Error),
    ];

    private IReadOnlyList<FileInfo>? _files;
    private FileInfo? _shown;
    private int _lineCount;

    // The shown file's entries, newest first, and those the filter and search keep.
    private IReadOnlyList<Row>? _entries;
    private IReadOnlyList<Row> _shownEntries = [];
    private LevelFilter _filter = Filters[0];
    private string? _search;

    protected override IObservable<Unit> Reloads => Observable.Interval(CheckInterval, Scheduler).Select(_ => Unit.Default);

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var files = logFiles.List();
        if (_files is not null && files.Select(Describe).SequenceEqual(_files.Select(Describe)))
        {
            return;
        }

        _files = files;
        var shown = files.FirstOrDefault(file => file.Name == _shown?.Name) ?? files.FirstOrDefault();
        if (shown is not null && (shown.Name != _shown?.Name || shown.Length != _shown.Length))
        {
            await ShowAsync(shown, cancellationToken);
        }
    }

    private Task ShowAsync(FileInfo file) => ShowAsync(file, CancellationToken.None);

    private async Task ShowAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var lines = await LogFileDirectory.ReadLastLinesAsync(file, LineCount, cancellationToken);
        _shown = file;
        _lineCount = lines.Count;
        _entries = ToRows(LogEntry.Parse(lines)).Reverse().ToList();
        Filter();
    }

    private static (string Name, long Length, DateTime LastWriteTimeUtc) Describe(FileInfo file) => (file.Name, file.Length, file.LastWriteTimeUtc);

    /// <summary>Numbers each entry among the entries equal to it, oldest first, so every row differs from the others.</summary>
    private static IEnumerable<Row> ToRows(IEnumerable<LogEntry> entries)
    {
        var occurrences = new Dictionary<LogEntry, int>();
        foreach (var entry in entries)
        {
            var occurrence = occurrences.GetValueOrDefault(entry);
            occurrences[entry] = occurrence + 1;
            yield return new Row(entry, occurrence);
        }
    }

    private void SelectFilter(string? value)
    {
        _filter = Filters.FirstOrDefault(filter => filter.Value == value) ?? Filters[0];
        Filter();
    }

    private void Search(string? search)
    {
        _search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        Filter();
    }

    private void Filter() =>
        _shownEntries = (_entries ?? []).Where(row => row.Entry.Level >= _filter.MinimumLevel && Matches(row.Entry)).ToList();

    private bool Matches(LogEntry entry) =>
        _search is null
        || entry.Message.Contains(_search, StringComparison.OrdinalIgnoreCase)
        || entry.Source.Contains(_search, StringComparison.OrdinalIgnoreCase)
        || entry.Detail?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true;

    private int CountOf(LevelFilter filter) => (_entries ?? []).Count(row => row.Entry.Level >= filter.MinimumLevel && Matches(row.Entry));

    private bool IsShown(FileInfo file) => file.Name == _shown?.Name;

    private string RowClass(FileInfo file) => IsShown(file) ? "log-file log-file-shown" : "log-file";

    /// <summary>The category's last part, such as LibraryScanner for Debarr.Scanning.LibraryScanner.</summary>
    private static string ShortSource(string source) => source[(source.LastIndexOf('.') + 1)..];

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Fatal",
        _ => "Info",
    };

    private static string LevelIcon(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => Icons.Material.Outlined.BugReport,
        LogLevel.Warning => Icons.Material.Outlined.WarningAmber,
        LogLevel.Error or LogLevel.Critical => Icons.Material.Outlined.ErrorOutline,
        _ => Icons.Material.Outlined.Info,
    };

    private static Color LevelColor(LogLevel level) => level switch
    {
        LogLevel.Warning => Color.Warning,
        LogLevel.Error or LogLevel.Critical => Color.Error,
        _ => Color.Default,
    };

    /// <summary>A level filter: its value, its label, and the lowest level it shows.</summary>
    private sealed record LevelFilter(string Value, string Label, LogLevel MinimumLevel);

    /// <summary>
    /// One entry as a row of the table, with how many equal entries come before it in the file.
    /// The table keys its rows by item, so a row keeps its element, and an open Details, when newer entries arrive.
    /// </summary>
    private sealed record Row(LogEntry Entry, int Occurrence);
}
