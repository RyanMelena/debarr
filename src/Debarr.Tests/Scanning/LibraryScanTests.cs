using Debarr.Scanning;
using JasperFx.Events;

namespace Debarr.Tests.Scanning;

public sealed class LibraryScanTests
{
    private static readonly DateTimeOffset AddedAt = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly Guid LibraryScanId = Guid.CreateVersion7();

    private static readonly LocalPath Media = new(Path.Combine(Path.GetTempPath(), "debarr-media"));

    private static readonly DateTimeOffset StartedAt = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    private static readonly LibraryScanCounts Counts = new(10, 2, 1, 3, 4);

    private static readonly Library WithMedia = Library.Default.Apply(new RootFolderAdded(Media, AddedAt));

    [Fact]
    public void A_library_scan_starts_its_own_stream_open_from_when_it_started()
    {
        var started = StartLibraryScanHandler.Handle(new StartLibraryScan(LibraryScanId, StartedAt));

        Assert.Equal(LibraryScanId, started.StreamId);
        var scanStarted = Assert.IsType<LibraryScanStarted>(Assert.Single(started.Events));
        Assert.Equal(new LibraryScan(LibraryScanId, StartedAt, false), LibraryScan.Create(new Event<LibraryScanStarted>(scanStarted) { StreamId = LibraryScanId }));
    }

    [Fact]
    public void A_root_folder_scan_records_when_the_library_scan_started_and_its_error()
    {
        var recorded = RecordRootFolderScanHandler.Handle(
            new RecordRootFolderScan(LibraryScanId, Media, "The folder is missing."),
            new LibraryScan(LibraryScanId, StartedAt, false),
            WithMedia);

        Assert.Equal([new RootFolderScanned(Media, StartedAt, "The folder is missing.")], recorded);
    }

    [Fact]
    public void A_root_folder_scan_records_nothing_after_the_library_scan_ended()
    {
        var ended = new LibraryScan(LibraryScanId, StartedAt, false).Apply(new LibraryScanEnded(StartedAt.AddMinutes(1), Counts, new LibraryScanOutcome.Finished()));

        Assert.Empty(RecordRootFolderScanHandler.Handle(new RecordRootFolderScan(LibraryScanId, Media, null), ended, WithMedia));
    }

    [Fact]
    public void A_root_folder_scan_records_nothing_for_a_root_folder_removed_while_the_scan_ran()
    {
        var open = new LibraryScan(LibraryScanId, StartedAt, false);

        Assert.Empty(RecordRootFolderScanHandler.Handle(new RecordRootFolderScan(LibraryScanId, Media, null), open, WithMedia.Apply(new RootFolderRemoved(Media))));
        Assert.Empty(RecordRootFolderScanHandler.Handle(new RecordRootFolderScan(LibraryScanId, Media, null), open, null));
    }

    [Fact]
    public void A_library_scan_ends_once_with_its_counts_and_outcome()
    {
        var open = new LibraryScan(LibraryScanId, StartedAt, false);
        var command = new EndLibraryScan(LibraryScanId, StartedAt.AddMinutes(1), Counts, new LibraryScanOutcome.Cancelled());

        var ended = Assert.IsType<LibraryScanEnded>(Assert.Single(EndLibraryScanHandler.Handle(command, open)));

        Assert.Equal(new LibraryScanEnded(StartedAt.AddMinutes(1), Counts, new LibraryScanOutcome.Cancelled()), ended);
        Assert.Empty(EndLibraryScanHandler.Handle(command, open.Apply(ended)));
    }

    [Fact]
    public void An_open_library_scan_is_interrupted_once_and_then_records_nothing()
    {
        var open = new LibraryScan(LibraryScanId, StartedAt, false);
        var command = new InterruptLibraryScan(LibraryScanId, StartedAt.AddDays(1));

        var interrupted = Assert.IsType<LibraryScanInterrupted>(Assert.Single(InterruptLibraryScanHandler.Handle(command, open)));
        var closed = open.Apply(interrupted);

        Assert.Equal(new LibraryScanInterrupted(StartedAt.AddDays(1)), interrupted);
        Assert.Empty(InterruptLibraryScanHandler.Handle(command, closed));
        Assert.Empty(RecordRootFolderScanHandler.Handle(new RecordRootFolderScan(LibraryScanId, Media, null), closed, WithMedia));
        Assert.Empty(EndLibraryScanHandler.Handle(new EndLibraryScan(LibraryScanId, StartedAt.AddDays(1), Counts, new LibraryScanOutcome.Finished()), closed));
    }

    [Fact]
    public void A_library_scan_that_ended_is_never_interrupted()
    {
        var ended = new LibraryScan(LibraryScanId, StartedAt, false).Apply(new LibraryScanEnded(StartedAt.AddMinutes(1), Counts, new LibraryScanOutcome.Finished()));

        Assert.Empty(InterruptLibraryScanHandler.Handle(new InterruptLibraryScan(LibraryScanId, StartedAt.AddDays(1)), ended));
    }

    [Fact]
    public void A_library_scan_whose_start_was_never_recorded_records_nothing()
    {
        Assert.Empty(RecordRootFolderScanHandler.Handle(new RecordRootFolderScan(LibraryScanId, Media, null), null, WithMedia));
        Assert.Empty(EndLibraryScanHandler.Handle(new EndLibraryScan(LibraryScanId, StartedAt, Counts, new LibraryScanOutcome.Finished()), null));
        Assert.Empty(InterruptLibraryScanHandler.Handle(new InterruptLibraryScan(LibraryScanId, StartedAt), null));
    }
}
