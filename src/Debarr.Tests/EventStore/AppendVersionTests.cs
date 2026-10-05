using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Fisher;
using FluentResults;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests.EventStore;

/// <summary>
/// What Fisher does with an append to a video file's stream, which decision Q17 rests on:
/// the inline projections fold before the write takes the lock, and only a stated version is checked under it.
/// </summary>
public sealed class AppendVersionTests : IAsyncLifetime
{
    private const string AlphaHash = "2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192";

    private static readonly FileHash Alpha = new(AlphaHash);
    private static readonly LocalPath AlphaPath = new("/media/a.mkv");
    private static readonly LocalPath CopyPath = new("/media/copy of a.mkv");
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FoldHook _folded = new();
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private IDocumentStore Store => _host.Store;

    public async ValueTask InitializeAsync()
    {
        // The guard would refuse the append with no version that pins what the store does with one.
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options =>
        {
            options.Listeners.Remove(options.Listeners.OfType<AppendVersionGuard>().Single());
            options.Projections.Add(_folded, ProjectionLifecycle.Inline);
        }));
        Assert.True((await SendAsync(new AddFilePath(Alpha, AlphaPath, new FileStat(1, Start), Start))).IsSuccess);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_append_with_no_version_whose_fold_meets_a_commit_commits_over_it_and_its_media_row_loses_that_commit()
    {
        _folded.Once<FilePathRemoved>(RecordFailedDetectionOfAlphaAsync);

        await using (var session = Store.LightweightSession())
        {
            session.Events.Append(Alpha.StreamId, new FilePathRemoved(Alpha, AlphaPath, Start));
            await session.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal([nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(DetectionFailed), nameof(FilePathRemoved)], await ReadEventTypeNamesAsync());
        Assert.Null((await LoadRowAsync()).LastFailure);
    }

    [Fact]
    public async Task A_versioned_append_whose_fold_meets_a_commit_fails_and_writes_nothing()
    {
        _folded.Once<FilePathRemoved>(RecordFailedDetectionOfAlphaAsync);

        await using (var session = Store.LightweightSession())
        {
            session.Events.Append(Alpha.StreamId, 2, new FilePathRemoved(Alpha, AlphaPath, Start));
            await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync(CancellationToken));
        }

        Assert.Equal([nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(DetectionFailed)], await ReadEventTypeNamesAsync());
        var row = await LoadRowAsync();
        Assert.NotNull(row.LastFailure);
        Assert.Single(row.FilePaths);
    }

    [Fact]
    public async Task An_append_at_version_0_starts_a_missing_stream()
    {
        var bravo = new FileHash("e4e75575b66d57fcd160508154ba15328b2dd74356fb2c82d3ce75e36679fa47");

        await using (var session = Store.LightweightSession())
        {
            session.Events.Append(bravo.StreamId, 0, new VideoFileDiscovered(bravo, 1, Start));
            await session.SaveChangesAsync(CancellationToken);
        }

        await using var query = Store.QuerySession();
        Assert.Equal(1, (await query.Events.FetchStreamStateAsync(bravo.StreamId, CancellationToken))!.Version);
    }

    [Fact]
    public async Task An_append_at_version_0_to_a_started_stream_fails_and_writes_nothing()
    {
        await using (var session = Store.LightweightSession())
        {
            session.Events.Append(Alpha.StreamId, 0, new FilePathRemoved(Alpha, AlphaPath, Start));
            await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync(CancellationToken));
        }

        Assert.Equal([nameof(VideoFileDiscovered), nameof(FilePathAdded)], await ReadEventTypeNamesAsync());
    }

    [Fact]
    public async Task Two_appends_at_one_version_to_one_stream_commit_together()
    {
        Assert.True((await SendAsync(new AddFilePath(Alpha, CopyPath, new FileStat(1, Start), Start))).IsSuccess);

        await using (var session = Store.LightweightSession())
        {
            session.Events.Append(Alpha.StreamId, 3, new FilePathRemoved(Alpha, AlphaPath, Start));
            session.Events.Append(Alpha.StreamId, 3, new FilePathRemoved(Alpha, CopyPath, Start));
            await session.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal(
            [nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(FilePathAdded), nameof(FilePathRemoved), nameof(FilePathRemoved)],
            await ReadEventTypeNamesAsync());
        Assert.Empty((await LoadRowAsync()).FilePaths);
    }

    [Fact]
    public async Task An_optimistic_append_states_the_version_it_read()
    {
        await using var session = Store.LightweightSession();
        await session.Events.AppendOptimistic(Alpha.StreamId, CancellationToken, new FilePathRemoved(Alpha, AlphaPath, Start));
        Assert.True((await RecordFailedDetectionOfAlphaAsync()).IsSuccess);

        await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync(CancellationToken));
    }

    private Task<Result> SendAsync(object command) => _host.Runtime.SendCommandAsync(command, CancellationToken);

    private Task<Result> RecordFailedDetectionOfAlphaAsync() => SendAsync(new RecordDetection(
        Guid.CreateVersion7(),
        Alpha,
        DetectionOrigin.Queue,
        AlphaPath,
        Start,
        TimeSpan.FromSeconds(1),
        1,
        new DetectorOutcome("8.1.2", null, Result.Fail("ffprobe failed"))));

    private async Task<MediaRow> LoadRowAsync()
    {
        await using var session = Store.QuerySession();
        return (await session.LoadAsync<MediaRow>(Alpha.StreamId, CancellationToken))!;
    }

    private async Task<List<string>> ReadEventTypeNamesAsync()
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Events.FetchStreamAsync(Alpha.StreamId, token: CancellationToken)).Select(stored => stored.Data.GetType().Name)];
    }
}
