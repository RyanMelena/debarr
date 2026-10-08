using System.Collections.Concurrent;
using Debarr.Activity;
using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Hosting;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Debarr.Tests.Scanning;
using Wolverine.Runtime;

namespace Debarr.Tests.EventStore;

public sealed class ReadModelChangeListenerTests : AppTestContext
{
    private readonly List<ReadModelChanged> _changes = [];

    [Fact]
    public async Task A_commit_to_each_aggregate_a_page_folds_announces_a_change_named_for_the_aggregate_and_its_stream()
    {
        using var feed = Subscribe();

        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Library.StreamId, new LibrarySettingsChanged(Library.Default.Settings));
            await session.Events.AppendAtCurrentVersionAsync(DetectionSettings.StreamId, new DetectionSettingsChanged(3, DetectionSettings.Default.PictureMeasurement, 600));
            await session.Events.AppendAtCurrentVersionAsync(Players.StreamId, new PlayerAdded(Guid.NewGuid(), "Theater", true, KodiEndpoint.Create("kodi.local", 9090, 60, 10).Value, [], []));
            await session.Events.AppendAtCurrentVersionAsync(Notifiers.StreamId, new NotifierAdded(Guid.NewGuid(), "Sink", true, WebhookSettings.Create("http://sink.local", WebhookMethod.Post, []).Value));
            await session.Events.AppendAtCurrentVersionAsync(UISettings.StreamId, new UISettingsChanged(UITheme.Dark, DateTimeFormats.Default, false));
            await session.SaveChangesAsync(CancellationToken);
        }

        var videoFile = await TestVideoFile.AddAsync(Store, "/media/film.mkv", CancellationToken);

        await Poll.UntilAsync(() =>
            Changed(nameof(Library), Library.StreamId)
            && Changed(nameof(VideoFile), videoFile.StreamId)
            && Changed(nameof(DetectionSettings), DetectionSettings.StreamId)
            && Changed(nameof(Players), Players.StreamId)
            && Changed(nameof(Notifiers), Notifiers.StreamId)
            && Changed(nameof(UISettings), UISettings.StreamId));
    }

    [Fact]
    public async Task A_commit_to_another_stream_announces_no_change_of_a_folded_aggregate()
    {
        using var feed = Subscribe();
        var libraryScanId = Guid.CreateVersion7();

        await TestLibrary.AppendLibraryScanAsync(Store, libraryScanId, [new LibraryScanStarted(DateTimeOffset.UtcNow)], CancellationToken);

        await Poll.UntilAsync(() => Changed(LibraryScanSummaryRowProjection.ReadModel, libraryScanId));
        Assert.DoesNotContain(Changes, changed => changed.Streams.Contains(libraryScanId.ToString()) && changed.ReadModel == nameof(Library));
    }

    [Fact]
    public async Task A_commit_publishes_each_event_it_appended_in_order()
    {
        var playerId = Guid.NewGuid();
        var added = new PlayerAdded(playerId, "Theater", true, KodiEndpoint.Create("kodi.local", 9090, 60, 10).Value, [], []);
        var renamed = new PlayerRenamed(playerId, "Lounge");
        var committed = new ConcurrentQueue<object>();
        using var subscription = GetAppService<ReadModelChangeListener>().Committed<object>().Subscribe(committed.Enqueue);

        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Players.StreamId, added, renamed);
            await session.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal([added, renamed], committed.Where(data => data is PlayerAdded or PlayerRenamed));
    }

    [Fact]
    public async Task A_subscriber_that_throws_leaves_the_command_succeeded_and_the_failure_logs_under_the_commands_scope()
    {
        using var subscription = GetAppService<ReadModelChangeListener>().ActivityEvents.Subscribe(_ => throw new InvalidOperationException("A subscriber failed."));

        var saved = await GetAppService<IWolverineRuntime>().SendCommandAsync(new SaveUISettings(UITheme.Dark, DateTimeFormats.Default, false), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal(UITheme.Dark, (await ReadStoreAsync(session => UISettings.ReadAsync(session, CancellationToken))).Theme);
        var logFile = Assert.Single(GetAppService<LogFileDirectory>().List());
        var lines = await LogFileDirectory.ReadLastLinesAsync(logFile, 100, CancellationToken);
        Assert.Contains(lines, line => line.Contains("[ERR] Debarr.EventStore.ReadModelChangeListener: A subscriber to the commits failed. (SaveUISettings ", StringComparison.Ordinal));
    }

    private IDisposable Subscribe() =>
        GetAppService<ActivityFeed>().Events.Subscribe(activity =>
        {
            if (activity is ReadModelChanged changed)
            {
                lock (_changes)
                {
                    _changes.Add(changed);
                }
            }
        });

    private bool Changed(string readModel, Guid streamId) =>
        Changes.Any(changed => changed.ReadModel == readModel && changed.Streams.Contains(streamId.ToString()));

    private List<ReadModelChanged> Changes
    {
        get
        {
            lock (_changes)
            {
                return [.. _changes];
            }
        }
    }
}
