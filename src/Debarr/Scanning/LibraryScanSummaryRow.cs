using System.Text.Json.Serialization;
using Fisher;
using Fisher.Linq;
using Fisher.Projections;
using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Debarr.Scanning;

/// <summary>
/// What one library scan did, as System &gt; Tasks shows it, and the root folders it covered.
/// The counts cover the part of the scan that ran.
/// </summary>
public sealed class LibraryScanSummaryRow
{
    /// <summary>The library scan's stream.</summary>
    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the scan is open, and for an interrupted scan.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Null while the scan is open, and for an interrupted scan.</summary>
    public LibraryScanCounts? Counts { get; set; }

    /// <summary>How the scan ended; null while it is open.</summary>
    public LibraryScanOutcome? Outcome { get; set; }

    /// <summary>Each root folder the scan covered, in the order it finished them.</summary>
    public List<LibraryScanSummaryRowRootFolder> RootFolders { get; set; } = [];

    /// <summary>Ended or interrupted.</summary>
    public bool Closed => Outcome is not null;

    /// <summary>Null while the scan is open, and for an interrupted scan.</summary>
    [JsonIgnore]
    public TimeSpan? Duration => EndedAt - StartedAt;
}

/// <summary>A root folder a library scan covered, and the scan's error for it; null when the scan read it.</summary>
public sealed record LibraryScanSummaryRowRootFolder(string Path, string? Error);

/// <summary>Writes a document for each library scan when it starts, adds each root folder it covers, completes it when the scan ends or is interrupted, and keeps every one.</summary>
public sealed class LibraryScanSummaryRowProjection : SingleStreamProjection<LibraryScanSummaryRow, Guid>
{
    public const string ReadModel = nameof(LibraryScanSummaryRow);

    public LibraryScanSummaryRowProjection() => Name = ReadModel;

    /// <summary>Registers the projection inline, with an index for the open scans, and one for the newest scans and a root folder's newest scan.</summary>
    public static void AddTo(StoreOptions options)
    {
        options.Schema.For<LibraryScanSummaryRow>()
            .Index([row => row.Closed, row => row.StartedAt])
            .Index(row => row.StartedAt);
        options.Projections.Add(new LibraryScanSummaryRowProjection(), ProjectionLifecycle.Inline);
    }

    public static LibraryScanSummaryRow Create(IEvent<LibraryScanStarted> started) => new() { Id = started.StreamId, StartedAt = started.Data.StartedAt };

    public static void Apply(RootFolderScanned scanned, LibraryScanSummaryRow row) =>
        row.RootFolders.Add(new LibraryScanSummaryRowRootFolder(scanned.Path.Value, scanned.Error));

    public static void Apply(LibraryScanEnded ended, LibraryScanSummaryRow row) =>
        (row.EndedAt, row.Counts, row.Outcome) = (ended.EndedAt, ended.Counts, ended.Outcome);

    public static void Apply(LibraryScanInterrupted interrupted, LibraryScanSummaryRow row) =>
        row.Outcome = new LibraryScanOutcome.Interrupted();
}

/// <summary>The reads of the library scan summaries: the newest scans, the open ones, and each root folder's newest scan.</summary>
public static class LibraryScanSummaryRowQuery
{
    /// <summary>The library scans, open or closed, newest first, up to <paramref name="count"/>.</summary>
    public static Task<IReadOnlyList<LibraryScanSummaryRow>> ReadNewestLibraryScansAsync(this IQuerySession session, int count, CancellationToken cancellationToken) =>
        session.Query<LibraryScanSummaryRow>()
            .OrderByDescending(row => row.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    /// <summary>The library scan running now, the newest one still open; null when none is running.</summary>
    public static Task<LibraryScanSummaryRow?> ReadOpenLibraryScanAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        session.OpenLibraryScans().FirstOrDefaultAsync(cancellationToken);

    /// <summary>The library scans still open, newest first, which a scan the process stopped before its end leaves.</summary>
    public static IQueryable<LibraryScanSummaryRow> OpenLibraryScans(this IQuerySession session) =>
        session.Query<LibraryScanSummaryRow>()
            .Where(row => !row.Closed)
            .OrderByDescending(row => row.StartedAt);

    /// <summary>
    /// The library's root folders in path order, each with the newest library scan that covered it since it was added; null until one does.
    /// </summary>
    public static async Task<IReadOnlyList<(RootFolder RootFolder, RootFolderScanned? LastScan)>> ReadRootFoldersAsync(
        this IQuerySession session,
        CancellationToken cancellationToken)
    {
        var library = await Library.ReadAsync(session, cancellationToken);
        var rootFolders = new List<(RootFolder, RootFolderScanned?)>(library.RootFolders.Count);
        foreach (var rootFolder in library.RootFolders)
        {
            var path = rootFolder.Path.Value;
            var addedAt = rootFolder.AddedAt;
            var lastScan = await session.Query<LibraryScanSummaryRow>()
                .Where(row => row.StartedAt >= addedAt && row.RootFolders.Any(scanned => scanned.Path == path))
                .OrderByDescending(row => row.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            rootFolders.Add((rootFolder, lastScan is null ? null : new RootFolderScanned(rootFolder.Path, lastScan.StartedAt, lastScan.RootFolders.Last(scanned => scanned.Path == path).Error)));
        }

        return rootFolders;
    }
}
