using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Fisher;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using IDocumentSessionOperations = JasperFx.Events.Documents.IDocumentSessionOperations;

namespace Debarr.Tests.Scanning;

/// <summary>
/// A file scan and a detection run beside a library scan's archive of the video files left with no path,
/// so one's write can commit between the archive's read and its write, in either order.
/// </summary>
public sealed class ArchiveRaceTests : IAsyncLifetime
{
    private const string AlphaHash = "2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192";

    private static readonly FileHash Alpha = new(AlphaHash);

    private readonly Interleaver _interleaver = new();
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    private IDocumentStore Store => _host.Store;

    /// <summary>Stores a.mkv as alpha, then deletes it and scans again, so alpha is live with no path, as the next library scan's archive finds it.</summary>
    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options => options.Listeners.Add(_interleaver)));
        _scanner = _host.Services.GetRequiredService<LibraryScanner>();
        Directory.CreateDirectory(Root);
        await TestLibrary.AddRootFolderAsync(Store, Root, CancellationToken);
        var path = Path.Combine(Root, "a.mkv");
        File.WriteAllText(path, "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        File.Delete(path);
        await _scanner.LibraryScanAsync(CancellationToken);
        Assert.Empty(await TestVideoFile.ReadStoredFilePathsAsync(Store, CancellationToken));
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>The file scan read alpha as live, then the archive commits, before the file scan's AddFilePath commits.</summary>
    [Fact]
    public async Task A_file_scan_whose_video_file_is_archived_between_its_archived_check_and_its_add_restores_it_with_the_path()
    {
        var moved = Path.Combine(Root, "moved.mkv");
        File.WriteAllText(moved, "alpha");
        var archived = -1;
        _interleaver.Before(session => AddsPath(session, "moved.mkv"), async () =>
        {
            archived = await _scanner.ArchiveAsync([Alpha], CancellationToken);
            return Result.Ok();
        });

        var videoFile = await _scanner.ScanFileAsync(new LocalPath(moved), CancellationToken);

        Assert.Equal(Alpha, videoFile);
        Assert.Equal(1, archived);
        Assert.False(await IsArchivedAsync(Alpha), "the file scan should restore alpha");
        Assert.Equal(
            [nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(FilePathRemoved), nameof(VideoFileArchived), nameof(VideoFileRestored), nameof(FilePathAdded)],
            await ReadEventTypeNamesAsync(Alpha));
        Assert.Equal([moved], (await TestVideoFile.ReadStoredFilePathsAsync(Store, CancellationToken)).Select(filePath => filePath.Id));
    }

    /// <summary>The archive read alpha's row, then a detection of it commits, before the archive's VideoFileArchived commits.</summary>
    [Fact]
    public async Task An_archive_whose_video_file_a_detection_records_before_it_commits_archives_it()
    {
        var raced = false;
        _interleaver.Before<VideoFileArchived>(() =>
        {
            raced = true;
            return RecordFailedDetectionOfAlphaAsync();
        });

        var archived = await _scanner.ArchiveAsync([Alpha], CancellationToken);

        Assert.True(raced, "the archive should have sent its ArchiveVideoFiles");
        Assert.Equal(1, archived);
        Assert.Equal(
            [nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(FilePathRemoved), nameof(DetectionFailed), nameof(VideoFileArchived)],
            await ReadEventTypeNamesAsync(Alpha));
        Assert.True(await IsArchivedAsync(Alpha));
    }

    private static bool AddsPath(IDocumentSessionOperations session, string fileName) =>
        session.PendingStreams.SelectMany(stream => stream.Events)
            .Any(pending => pending.Data is FilePathAdded added && added.Path.Value.EndsWith(fileName, StringComparison.Ordinal));

    private Task<Result> RecordFailedDetectionOfAlphaAsync() =>
        _host.Runtime.SendCommandAsync(
            new RecordDetection(
                Guid.CreateVersion7(),
                Alpha,
                DetectionOrigin.Queue,
                new LocalPath(Path.Combine(Root, "a.mkv")),
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                1,
                new DetectorOutcome("8.1.2", null, Result.Fail("ffprobe failed"))),
            CancellationToken);

    private async Task<List<string>> ReadEventTypeNamesAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Events.FetchStreamAsync(videoFile.StreamId, token: CancellationToken)).Select(stored => stored.Data.GetType().Name)];
    }

    private async Task<bool> IsArchivedAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return (await session.Events.FetchStreamStateAsync(videoFile.StreamId, CancellationToken))!.IsArchived;
    }
}
