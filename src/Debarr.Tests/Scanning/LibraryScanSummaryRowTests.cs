using Debarr.Scanning;
using Fisher.Linq;

namespace Debarr.Tests.Scanning;

public sealed class LibraryScanSummaryRowTests : AppTestContext
{
    private static readonly Guid Finished = Guid.CreateVersion7();
    private static readonly Guid Interrupted = Guid.CreateVersion7();
    private static readonly Guid Open = Guid.CreateVersion7();

    private static readonly DateTimeOffset FinishedAt = new(2020, 9, 29, 20, 15, 0, 123, TimeSpan.Zero);
    private static readonly DateTimeOffset InterruptedAt = new(2020, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OpenAt = new(2020, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly LibraryScanCounts Counts = new(10, 2, 1, 3, 4);

    private static readonly LocalPath Movies = new(Path.Combine(Path.GetTempPath(), "debarr-movies"));
    private static readonly LocalPath Shows = new(Path.Combine(Path.GetTempPath(), "debarr-shows"));
    private static readonly DateTimeOffset AddedAt = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ScannedAt = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    private static readonly Summary FinishedSummary = new(FinishedAt, FinishedAt.AddSeconds(3.5), TimeSpan.FromSeconds(3.5), Counts, new LibraryScanOutcome.Finished(), true);

    private static readonly Summary InterruptedSummary = new(InterruptedAt, null, null, null, new LibraryScanOutcome.Interrupted(), true);

    private static readonly Summary OpenSummary = new(OpenAt, null, null, null, null, false);

    [Fact]
    public async Task A_scan_has_a_row_from_its_start_completed_by_its_end_or_its_interruption()
    {
        await AppendScansAsync();

        Assert.Equal([FinishedSummary, InterruptedSummary, OpenSummary], await ReadSummariesAsync());
    }

    [Fact]
    public async Task The_newest_scans_read_newest_first_with_the_open_one_and_the_open_ones_read_alone()
    {
        await AppendScansAsync();
        await using var session = Store.QuerySession();

        var newest = (await session.ReadNewestLibraryScansAsync(2, CancellationToken)).Select(row => row.Id);

        Assert.Equal([Open, Interrupted], newest);
        Assert.Equal([Open], (await session.OpenLibraryScans().ToListAsync(CancellationToken)).Select(row => row.Id));
    }

    public static TheoryData<LibraryScanOutcome> EndedOutcomes => [new LibraryScanOutcome.Failed("The folder is missing."), new LibraryScanOutcome.Cancelled()];

    [Theory]
    [MemberData(nameof(EndedOutcomes))]
    public async Task A_failed_or_cancelled_scan_keeps_its_outcome_and_the_counts_of_the_part_that_ran(LibraryScanOutcome outcome)
    {
        var ended = Guid.CreateVersion7();
        await TestLibrary.AppendLibraryScanAsync(
            Store,
            ended,
            [new LibraryScanStarted(FinishedAt), new LibraryScanEnded(FinishedAt.AddMilliseconds(250), new LibraryScanCounts(1, 0, 0, 0, 0), outcome)],
            CancellationToken);

        await using var session = Store.QuerySession();
        var row = await session.LoadAsync<LibraryScanSummaryRow>(ended, CancellationToken);

        Assert.Equal((TimeSpan.FromMilliseconds(250), 1, outcome, true), (row!.Duration, row.Counts?.FilePathsFound, row.Outcome, row.Closed));
    }

    [Fact]
    public async Task A_rebuild_replays_the_summaries()
    {
        await AppendScansAsync();
        await using (var session = Store.LightweightSession())
        {
            var row = await session.LoadAsync<LibraryScanSummaryRow>(Finished, CancellationToken);
            row!.Outcome = new LibraryScanOutcome.Failed("Changed");
            session.Store(row);
            await session.SaveChangesAsync(CancellationToken);
        }

        await RebuildAsync(LibraryScanSummaryRowProjection.ReadModel);

        Assert.Equal([FinishedSummary, InterruptedSummary, OpenSummary], await ReadSummariesAsync());
    }

    [Fact]
    public async Task Each_root_folder_reads_with_whether_it_is_enabled_and_its_newest_library_scan()
    {
        await AppendLibraryAsync(new RootFolderAdded(Movies, AddedAt), new RootFolderAdded(Shows, AddedAt), new RootFolderDisabled(Shows));
        await AppendScanAsync(ScannedAt, new RootFolderScanned(Movies, ScannedAt, "The folder is missing."));
        await AppendScanAsync(ScannedAt.AddHours(1), new RootFolderScanned(Movies, ScannedAt.AddHours(1), null));

        Assert.Equal([(Movies, true, ScannedAt.AddHours(1), null), (Shows, false, null, null)], await ReadRootFoldersAsync());

        await AppendLibraryAsync(new RootFolderEnabled(Shows));
        await AppendScanAsync(ScannedAt, new RootFolderScanned(Shows, ScannedAt, "The folder is missing."));

        Assert.Equal(
            [(Movies, true, ScannedAt.AddHours(1), null), (Shows, true, ScannedAt, "The folder is missing.")],
            await ReadRootFoldersAsync());
    }

    [Fact]
    public async Task A_removed_root_folder_reads_no_more_and_comes_back_unscanned_when_added_again()
    {
        await AppendLibraryAsync(new RootFolderAdded(Movies, AddedAt));
        await AppendScanAsync(ScannedAt, new RootFolderScanned(Movies, ScannedAt, "The folder is missing."));
        await AppendLibraryAsync(new RootFolderRemoved(Movies));

        Assert.Empty(await ReadRootFoldersAsync());

        await AppendLibraryAsync(new RootFolderAdded(Movies, ScannedAt.AddHours(1)));

        Assert.Equal([(Movies, true, null, null)], await ReadRootFoldersAsync());
    }

    [Fact]
    public async Task A_scan_recorded_after_its_root_folder_was_removed_never_brings_the_root_folder_back()
    {
        await AppendLibraryAsync(new RootFolderAdded(Movies, AddedAt), new RootFolderRemoved(Movies));
        await AppendScanAsync(ScannedAt, new RootFolderScanned(Movies, ScannedAt, null));

        Assert.Empty(await ReadRootFoldersAsync());
    }

    [Fact]
    public async Task A_rebuild_keeps_the_root_folders_newest_scans()
    {
        await AppendLibraryAsync(new RootFolderAdded(Movies, AddedAt), new RootFolderAdded(Shows, AddedAt));
        await AppendScanAsync(ScannedAt, new RootFolderScanned(Movies, ScannedAt, null), new RootFolderScanned(Shows, ScannedAt, "The folder is missing."));
        await AppendLibraryAsync(new RootFolderRemoved(Shows));

        await RebuildAsync(LibraryScanSummaryRowProjection.ReadModel);

        Assert.Equal([(Movies, true, ScannedAt, null)], await ReadRootFoldersAsync());
    }

    private async Task AppendScansAsync()
    {
        await TestLibrary.AppendLibraryScanAsync(Store, Finished, [new LibraryScanStarted(FinishedAt), new LibraryScanEnded(FinishedAt.AddSeconds(3.5), Counts, new LibraryScanOutcome.Finished())], CancellationToken);
        await TestLibrary.AppendLibraryScanAsync(Store, Interrupted, [new LibraryScanStarted(InterruptedAt), new LibraryScanInterrupted(OpenAt)], CancellationToken);
        await TestLibrary.AppendLibraryScanAsync(Store, Open, [new LibraryScanStarted(OpenAt)], CancellationToken);
    }

    private async Task AppendLibraryAsync(params object[] events)
    {
        await TestLibrary.AppendAsync(Store, events, CancellationToken);
    }

    /// <summary>Appends a library scan that started at <paramref name="startedAt"/> and recorded <paramref name="scanned"/>.</summary>
    private async Task AppendScanAsync(DateTimeOffset startedAt, params RootFolderScanned[] scanned)
    {
        await TestLibrary.AppendLibraryScanAsync(Store, Guid.CreateVersion7(), [new LibraryScanStarted(startedAt), .. scanned], CancellationToken);
    }

    /// <summary>The rows of the scans the tests appended, in the order they started.</summary>
    private async Task<List<Summary>> ReadSummariesAsync()
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Query<LibraryScanSummaryRow>().Where(row => row.Id == Finished || row.Id == Interrupted || row.Id == Open).ToListAsync(CancellationToken))
            .OrderBy(row => row.StartedAt)
            .Select(row => new Summary(row.StartedAt, row.EndedAt, row.Duration, row.Counts, row.Outcome, row.Closed))];
    }

    private async Task<List<(LocalPath Path, bool Enabled, DateTimeOffset? LastScannedAt, string? ScanError)>> ReadRootFoldersAsync()
    {
        await using var session = Store.QuerySession();
        return [.. (await session.ReadRootFoldersAsync(CancellationToken))
            .Select(root => (root.RootFolder.Path, root.RootFolder.Enabled, root.LastScan?.StartedAt, root.LastScan?.Error))];
    }

    private sealed record Summary(
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        TimeSpan? Duration,
        LibraryScanCounts? Counts,
        LibraryScanOutcome? Outcome,
        bool Closed);
}
