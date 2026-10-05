using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.EventStore;
using Fisher;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace Debarr.Tests.Detecting;

/// <summary>
/// Scans, detections and the operator append to one video file's stream at once.
/// Each test lets one command load the video file, commits another command's events before the first one's write, and checks that neither is lost.
/// </summary>
public sealed class VideoFileConcurrencyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);
    private static readonly FileHash Film = TestFileHash.For("film");
    private static readonly LocalPath Movies = new("/movies/Film.mkv");
    private static readonly LocalPath Backup = new("/backup/Film.mkv");
    private static readonly FileStat Stat = new(1, Now.AddDays(-1));

    private readonly Interleaver _interleaver = new();
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options => options.Listeners.Add(_interleaver)));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Two_scans_that_discover_one_video_file_at_once_both_record_their_paths()
    {
        _interleaver.Before<VideoFileDiscovered>(() => SendAsync(new AddFilePath(Film, Backup, Stat, Now)));

        var added = await SendAsync(new AddFilePath(Film, Movies, Stat, Now));

        Assert.True(added.IsSuccess);
        Assert.Equal([typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(FilePathAdded)], await ReadEventTypesAsync());
        Assert.Equal([Backup.Value, Movies.Value], await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_detection_that_meets_a_scan_of_its_video_file_is_recorded_after_it()
    {
        await SendAsync(new AddFilePath(Film, Movies, Stat, Now));
        _interleaver.Before<AspectRatioDetected>(() => SendAsync(new AddFilePath(Film, Backup, Stat, Now)));

        var recorded = await SendAsync(new RecordDetection(
            Guid.CreateVersion7(),
            Film,
            DetectionOrigin.Queue,
            Movies,
            Now,
            TimeSpan.FromSeconds(9),
            1,
            new DetectorOutcome("8.1.2", null, Result.Ok(DetectionResult.FromFile(new AspectRatio(1.78))))));

        Assert.True(recorded.IsSuccess);
        Assert.Equal(
            [typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(FilePathAdded), typeof(AspectRatioDetected)],
            await ReadEventTypesAsync());
    }

    [Fact]
    public async Task An_override_save_that_meets_a_detection_is_saved_after_it()
    {
        await SendAsync(new AddFilePath(Film, Movies, Stat, Now));
        _interleaver.Before<OverrideSaved>(() => SendAsync(new RecordDetection(
            Guid.CreateVersion7(),
            Film,
            DetectionOrigin.Queue,
            Movies,
            Now,
            TimeSpan.FromSeconds(9),
            1,
            new DetectorOutcome("8.1.2", null, Result.Fail("ffprobe failed")))));

        var saved = await SendAsync(new SaveOverride(Film, 2.39, false, null));

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            [typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(DetectionFailed), typeof(OverrideSaved)],
            await ReadEventTypesAsync());
    }

    [Fact]
    public async Task An_archive_that_meets_a_path_added_to_its_video_file_keeps_the_video_file_live()
    {
        await SendAsync(new AddFilePath(Film, Movies, Stat, Now));
        await SendAsync(new RemoveFilePath(Film, Movies, Now));
        _interleaver.Before<VideoFileArchived>(() => SendAsync(new AddFilePath(Film, Backup, Stat, Now)));

        var archived = await SendAsync(new ArchiveVideoFiles([Film], Now));

        Assert.True(archived.IsSuccess);
        Assert.Equal(
            [typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(FilePathRemoved), typeof(FilePathAdded)],
            await ReadEventTypesAsync());
        Assert.False(await IsArchivedAsync());
        Assert.Equal([Backup.Value], await ReadFilePathsAsync());
    }

    [Fact]
    public async Task An_archive_archives_the_stream_and_keeps_every_event_and_detection_row()
    {
        await SendAsync(new AddFilePath(Film, Movies, Stat, Now));
        await SendAsync(Detection());
        await SendAsync(new RemoveFilePath(Film, Movies, Now));

        var archived = await SendAsync(new ArchiveVideoFiles([Film], Now));

        Assert.True(archived.IsSuccess);
        Assert.True(await IsArchivedAsync());
        Assert.Equal(
            [typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(AspectRatioDetected), typeof(FilePathRemoved), typeof(VideoFileArchived)],
            await ReadEventTypesAsync());
        Assert.Empty(await TestVideoFile.ReadMediaRowsAsync(_host.Services.GetRequiredService<IDocumentStore>(), CancellationToken));
        Assert.Single(await TestVideoFile.ReadDetectionsAsync(_host.Services.GetRequiredService<IDocumentStore>(), Film, CancellationToken));
    }

    [Fact]
    public async Task A_detection_of_an_archived_video_file_is_discarded()
    {
        await ArchiveAsync();

        var recorded = await SendAsync(Detection());

        Assert.True(recorded.IsSuccess);
        Assert.Equal(typeof(VideoFileArchived), (await ReadEventTypesAsync())[^1]);
    }

    [Fact]
    public async Task A_detection_whose_video_file_is_archived_before_its_commit_is_discarded()
    {
        await SendAsync(new AddFilePath(Film, Movies, Stat, Now));
        await SendAsync(new RemoveFilePath(Film, Movies, Now));
        _interleaver.Before<AspectRatioDetected>(() => SendAsync(new ArchiveVideoFiles([Film], Now)));

        var recorded = await SendAsync(Detection());

        Assert.True(recorded.IsSuccess);
        Assert.True(await IsArchivedAsync());
        Assert.Equal(typeof(VideoFileArchived), (await ReadEventTypesAsync())[^1]);
    }

    [Fact]
    public async Task A_restore_that_a_crash_cut_short_after_the_unarchive_completes_on_the_next_path()
    {
        await ArchiveAsync();
        await SendAsync(new UnarchiveVideoFile(Film));

        var added = await SendAsync(new AddFilePath(Film, Backup, Stat, Now.AddDays(1)));

        Assert.True(added.IsSuccess);
        Assert.Equal([typeof(VideoFileArchived), typeof(VideoFileRestored), typeof(FilePathAdded)], (await ReadEventTypesAsync())[^3..]);
        Assert.Equal(Now.AddDays(1), Assert.Single(await TestVideoFile.ReadMediaRowsAsync(_host.Services.GetRequiredService<IDocumentStore>(), CancellationToken)).FilePaths.Single().FirstSeenAt);
    }

    private async Task ArchiveAsync()
    {
        await SendAsync(new AddFilePath(Film, Movies, Stat, Now));
        await SendAsync(new RemoveFilePath(Film, Movies, Now));
        Assert.True((await SendAsync(new ArchiveVideoFiles([Film], Now))).IsSuccess);
    }

    private static RecordDetection Detection() => new(
        Guid.CreateVersion7(),
        Film,
        DetectionOrigin.Queue,
        Movies,
        Now,
        TimeSpan.FromSeconds(9),
        1,
        new DetectorOutcome("8.1.2", null, Result.Ok(DetectionResult.FromFile(new AspectRatio(1.78)))));

    private async Task<bool> IsArchivedAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return (await session.Events.FetchStreamStateAsync(Film.StreamId, CancellationToken))!.IsArchived;
    }

    private async Task<Result> SendAsync(object command) =>
        await _host.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(command, CancellationToken);

    private async Task<List<Type>> ReadEventTypesAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return [.. (await session.Events.FetchStreamAsync(Film.StreamId, token: CancellationToken)).Select(stored => stored.Data.GetType())];
    }

    private async Task<List<string>> ReadFilePathsAsync() =>
        [.. (await TestVideoFile.ReadStoredFilePathsAsync(_host.Services.GetRequiredService<IDocumentStore>(), CancellationToken)).Where(filePath => filePath.VideoFile == Film).Select(filePath => filePath.Id)];
}
