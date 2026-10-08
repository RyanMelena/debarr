using System.Collections.Concurrent;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Debarr.Tests.Extensions;
using Debarr.Tests.Notifying;
using Debarr.Tests.Scanning;
using Fisher.Linq;
using Fisher;
using FluentResults;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Quartz;
using Wolverine.Runtime;

namespace Debarr.Tests.Playing;

public sealed class PlaybackHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 18, 30, 0, 125, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly FakeKodiServer _theater = new();
    private readonly FakeKodiServer _bedroom = new();
    private readonly CapturingHttpMessageHandler _webhook = new();
    private readonly DetectorDouble _detector = new() { Respond = _ => DetectorDouble.Failed("Released.") };
    private readonly ConcurrentQueue<object> _events = new();
    private readonly Interleaver _interleaver = new();
    private readonly FakeLoggerProvider _logs = new();
    private TaskCompletionSource _gate = CompletedGate();
    private int _held;
    private string _connectionString = null!;
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;
    private PlayerConnectionService _playerConnectionService = null!;
    private PlaybackHandler _handler = null!;
    private IDisposable? _subscription;
    private readonly Guid _theaterId = TestPlayback.TheaterId;
    private readonly Guid _bedroomId = TestPlayback.BedroomId;
    private readonly Guid _automationId = TestPlayback.AutomationId;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    /// <summary>The webhook bodies the notifier received.</summary>
    private ConcurrentQueue<string> Bodies { get; } = new();

    /// <summary>The webhook bodies cancelled while held.</summary>
    private ConcurrentQueue<string> Cancelled { get; } = new();

    /// <summary>How many webhook requests arrived while held.</summary>
    private int Held => Volatile.Read(ref _held);

    public async ValueTask InitializeAsync()
    {
        _webhook.Respond = RespondAsync;
        _host = await TestHost.StartAsync(
            services =>
            {
                services
                    .AddSingleton<TimeProvider>(_timeProvider)
                    .AddSingleton<IAspectRatioDetector>(_detector)
                    .ConfigureFisher(options => options.Listeners.Add(_interleaver))
                    .AddHttpClient(WebhookNotifierClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _webhook);
            },
            logging => logging.AddProvider(_logs));
        _connectionString = _host.UnpooledConnectionString;
        _scanner = _host.Services.GetRequiredService<LibraryScanner>();
        _playerConnectionService = _host.Services.GetRequiredService<PlayerConnectionService>();
        _handler = _host.Services.GetRequiredService<PlaybackHandler>();
        Directory.CreateDirectory(Root);

        await TestLibrary.AddRootFolderAsync(_host.Services.GetRequiredService<IDocumentStore>(), Root, CancellationToken);
        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>();
        Assert.True((await runtime.SendCommandAsync(
            new SavePlayer(_theaterId, "Theater", true, KodiEndpoint.Create("127.0.0.1", _theater.Port, 60, 10).Value, [new PathMappingEntry("smb://nas/media", Root)], []),
            CancellationToken)).IsSuccess);
        Assert.True((await runtime.SendCommandAsync(
            new SavePlayer(_bedroomId, "Bedroom", true, KodiEndpoint.Create("127.0.0.1", _bedroom.Port, 60, 10).Value, [], []),
            CancellationToken)).IsSuccess);
        Assert.True((await runtime.SendCommandAsync(
            new SaveNotifier(_automationId, "automation", true, WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Post, []).Value),
            CancellationToken)).IsSuccess);

        _subscription = _host.Services.GetRequiredService<ReadModelChangeListener>().Committed<object>()
            .Subscribe(_events.Enqueue);
        var connected = _playerConnectionService.ActivityEvents
            .OfType<PlayerConnectionStateChangedEvent>()
            .Where(change => change.State is PlayerConnectionState.Connected)
            .Take(2)
            .ToList()
            .ToTask(CancellationToken);
        await _handler.StartAsync(CancellationToken);
        await _playerConnectionService.StartAsync(CancellationToken);
        await connected.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Release();
        _detector.ReleaseAll();
        await _playerConnectionService.StopAsync(CancellationToken.None);
        await _handler.StopAsync(CancellationToken.None);
        _subscription?.Dispose();
        await _host.Services.GetRequiredService<DetectionOrchestrator>().StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
        await _theater.DisposeAsync();
        await _bedroom.DisposeAsync();
    }

    [Fact]
    public async Task A_detection_result_sends_its_snapped_ratio_through_the_path_mapping()
    {
        var path = Write("Movies/Arrival.mkv");
        var videoFile = await SetCurrentResultAsync(path, 2.3964, AspectRatioSource.Detected);

        var playback = await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 1.7778);

        Assert.Equal(Sent(2.39, NotificationAspectRatioSource.Detected, await DetectionIdAsync()), playback.Outcome);
        Assert.Equal(
            (Now, _theaterId, "Theater", "Arrival (2016)", "smb://nas/media/Movies/Arrival.mkv", Path.Combine(Root, "Movies", "Arrival.mkv"), (FileHash?)videoFile, (double?)1.7778),
            (playback.OccurredAt, playback.PlayerId, playback.PlayerName, playback.Title, playback.PlayerPath.Value, playback.LocalPath?.Value, playback.VideoFile, playback.PlayerAspectRatio?.Value));
        var handled = Assert.Single(_events.OfType<PlaybackHandled>());
        Assert.Equal((_theaterId, playback.Id), (handled.PlayerId, handled.PlaybackId));
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Any());
        Assert.Equal(["""{"player":"Theater","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":2.39,"source":"detected"}"""], Bodies);
        var delivery = Assert.Single(Assert.Single(await StoredPlaybacksAsync()).Deliveries);
        Assert.Equal(("automation", new DeliveryOutcome.Succeeded()), (delivery.NotifierName, delivery.Outcome));
        var finished = Assert.Single(_events.OfType<DeliveryFinished>());
        Assert.Equal((playback.Id, delivery.NotifierId), (finished.PlaybackId, finished.Delivery.NotifierId));
    }

    [Fact]
    public async Task What_a_playback_logs_carries_its_scope_through_its_delivery_and_its_record_commands()
    {
        Write("Movies/Arrival.mkv");

        var playback = await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 2.3975);
        await Poll.UntilAsync(() => CommandLine(nameof(RecordDelivery)) is not null);

        var scope = $"Playback {playback.Id} on Theater";
        var delivered = Assert.Single(_logs.Collector.GetSnapshot(), log => log.Message.StartsWith("Delivered to automation in ", StringComparison.Ordinal));
        Assert.Contains(delivered.Scopes, state => state?.ToString() == scope);
        Assert.Contains(CommandLine(nameof(RecordPlayback))!.Scopes, state => state?.ToString() == scope);
        Assert.Contains(CommandLine(nameof(RecordDelivery))!.Scopes, state => state?.ToString() == scope);
    }

    [Fact]
    public async Task A_plugin_or_pvr_stream_sends_nothing()
    {
        var plugin = await PlayAsync(_theater, "plugin://plugin.video.youtube/play/?video_id=abc", 1.7778);
        var pvr = await PlayAsync(_theater, "pvr://channels/tv/All%20channels/1.pvr", 1.7778);
        var file = await PlayAsync(_theater, "/elsewhere/Arrival.mkv", 1.7778);

        Assert.Equal(
            [
                ((PlaybackOutcome)new PlaybackOutcome.Stream(), "plugin://plugin.video.youtube/play/?video_id=abc", (string?)null),
                ((PlaybackOutcome)new PlaybackOutcome.Stream(), "pvr://channels/tv/All channels/1.pvr", null),
            ],
            [(plugin.Outcome, plugin.PlayerPath.Value, plugin.LocalPath?.Value), (pvr.Outcome, pvr.PlayerPath.Value, pvr.LocalPath?.Value)]);
        Assert.Equal(Sent(1.78, NotificationAspectRatioSource.Player), file.Outcome);
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Any());
        Assert.Equal(["""{"player":"Theater","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":1.78,"source":"player"}"""], Bodies);
    }

    [Fact]
    public async Task A_failure_sends_nothing_and_the_next_playback_is_handled()
    {
        Write("Movies/Arrival.mkv");
        await SetMediaRowsRefusedAsync(true);

        var failed = await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 1.7778);
        await SetMediaRowsRefusedAsync(false);
        var handled = await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 1.7778);

        Assert.Contains("The Media row was refused.", Assert.IsType<PlaybackOutcome.Failed>(failed.Outcome).Error, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Root, "Movies", "Arrival.mkv"), failed.LocalPath?.Value);
        Assert.Equal(Sent(1.78, NotificationAspectRatioSource.Player), handled.Outcome);
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Any());
        Assert.Equal(["""{"player":"Theater","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":1.78,"source":"player"}"""], Bodies);
    }

    [Fact]
    public async Task A_playback_completes_while_a_library_scan_and_a_detection_are_each_held_mid_way()
    {
        _detector.Holds = true;
        var detecting = Path.GetFullPath(Write("Movies/Detecting.mkv"));
        var detectingVideoFile = (await _scanner.ScanFileAsync(new LocalPath(detecting), CancellationToken))!.Value;
        var orchestrator = _host.Services.GetRequiredService<DetectionOrchestrator>();
        Assert.True((await orchestrator.DetectNowAsync(detectingVideoFile, CancellationToken)).IsSuccess);
        await Poll.UntilAsync(() => _detector.Running == 1);

        var scanning = Path.GetFullPath(Write("Movies/Scanning.mkv"));
        var scanHeld = new TaskCompletionSource();
        var releaseScan = new TaskCompletionSource();
        _interleaver.Before(
            session => session.PendingStreams.SelectMany(stream => stream.Events).Any(pending => pending.Data is FilePathAdded { Path.Value: var path } && path == scanning),
            async () =>
            {
                scanHeld.SetResult();
                await releaseScan.Task;
                return Result.Ok();
            });
        var libraryScan = _scanner.LibraryScanAsync(CancellationToken);
        await scanHeld.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

        Write("Movies/Arrival.mkv");
        var playback = await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 2.3975);

        Assert.Equal(Sent(2.39, NotificationAspectRatioSource.Player), playback.Outcome);
        Assert.NotNull(playback.VideoFile);
        Assert.False(libraryScan.IsCompleted);
        Assert.Equal(1, _detector.Running);

        releaseScan.SetResult();
        await libraryScan.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        _detector.ReleaseAll();
        await Poll.UntilAsync(() => orchestrator.RunningDetections.Count == 0);
        Assert.Equal(
            [Path.Combine(Root, "Movies", "Arrival.mkv"), detecting, scanning],
            (await TestVideoFile.ReadStoredFilePathsAsync(_host.Services.GetRequiredService<IDocumentStore>(), CancellationToken)).Select(filePath => filePath.Id));
        Assert.NotNull((await TestVideoFile.ReadMediaRowAsync(_host.Services.GetRequiredService<IDocumentStore>(), detectingVideoFile, CancellationToken))?.LastFailure);
    }

    [Fact]
    public async Task A_newer_playback_cancels_the_delivery_in_flight_for_its_player_alone()
    {
        Hold();

        await PlayAsync(_theater, "/elsewhere/First.mkv", 1.3333);
        await Poll.UntilAsync(() => Held == 1);
        await PlayAsync(_bedroom, "/elsewhere/Bedroom.mkv", 1.85);
        await Poll.UntilAsync(() => Held == 2);
        await PlayAsync(_theater, "/elsewhere/Second.mkv", 2.3975);
        await Poll.UntilAsync(() => Held == 3 && Cancelled.Count == 1);
        Release();

        await Poll.UntilAsync(() => Bodies.Count == 2);
        Assert.Equal(["""{"player":"Theater","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":1.33,"source":"player"}"""], Cancelled);
        Assert.Equal(
            [
                """{"player":"Bedroom","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":1.85,"source":"player"}""",
                """{"player":"Theater","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":2.39,"source":"player"}""",
            ],
            Bodies.Order());
        // The cancelled delivery is stored and reported too.
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Count() == 3);
        Assert.Equal(
            [("/elsewhere/Bedroom.mkv", new DeliveryOutcome.Succeeded()), ("/elsewhere/First.mkv", new DeliveryOutcome.Cancelled()), ("/elsewhere/Second.mkv", new DeliveryOutcome.Succeeded())],
            (await StoredPlaybacksAsync())
                .Select(playback => (LocalPath: playback.LocalPath?.Value, Assert.Single(playback.Deliveries).Outcome))
                .OrderBy(playback => playback.LocalPath, StringComparer.Ordinal));

        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        Assert.Equal(
            [(_theaterId, "/elsewhere/Second.mkv"), (_bedroomId, "/elsewhere/Bedroom.mkv")],
            (await session.ReadLastPlaybacksAsync([_theaterId, _bedroomId], CancellationToken))
                .Select(lastPlayback => (lastPlayback.Key, lastPlayback.Value.LocalPath?.Value))
                .OrderBy(lastPlayback => lastPlayback.Key == _bedroomId));
    }

    [Fact]
    public async Task A_playback_during_a_scan_and_a_running_detection_completes_without_waiting_on_either()
    {
        await using (var session = _host.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(
                DetectionSettings.StreamId,
                new DetectionSettingsChanged(1, DetectionSettings.Default.PictureMeasurement, DetectionSettings.Default.TimeoutSeconds));
            await session.SaveChangesAsync(CancellationToken);
        }

        var detecting = Path.GetFullPath(Write("Movies/Detecting.mkv"));
        await _scanner.ScanFileAsync(new LocalPath(detecting), CancellationToken);
        Write("Movies/Arrival.mkv");
        var scanHold = new ScanHold();
        _host.Scheduler.ListenerManager.AddJobListener(scanHold, [Matchers.Key(LibraryScanJob.Key)]);
        _detector.Holds = true;
        await _host.Scheduler.Start(CancellationToken);
        await _host.Services.GetRequiredService<DetectionOrchestrator>().StartAsync(CancellationToken);
        await _host.Scheduler.TriggerJob(LibraryScanJob.Key, null, CancellationToken);
        await scanHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        await Poll.UntilAsync(() => _detector.Running == 1);

        var playback = await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 2.3975);

        Assert.Equal(Sent(2.39, NotificationAspectRatioSource.Player), playback.Outcome);
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Any());
        Assert.Equal(["""{"player":"Theater","occurred_at":"2026-09-26T18:30:00.125Z","aspect_ratio":2.39,"source":"player"}"""], Bodies);
        Assert.Equal((1, false), (_detector.Running, scanHold.Released.Task.IsCompleted));
        scanHold.Released.SetResult();
    }

    [Fact]
    public async Task Storing_the_history_adds_nothing_to_the_time_to_deliver()
    {
        await using var writeLock = new SqliteConnection(_connectionString);
        await writeLock.OpenAsync(CancellationToken);
        await using (var begin = writeLock.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync(CancellationToken);
        }

        var playing = PlayAsync(_theater, "/elsewhere/Arrival.mkv", 2.3975);
        await Poll.UntilAsync(() => !Bodies.IsEmpty);
        var handledWhileTheDatabaseWasLocked = playing.IsCompleted || _events.OfType<DeliveryFinished>().Any();
        await using (var rollback = writeLock.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync(CancellationToken);
        }

        await playing;
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Any());

        Assert.False(handledWhileTheDatabaseWasLocked);
        Assert.Equal(new DeliveryOutcome.Succeeded(), Assert.Single(Assert.Single(await StoredPlaybacksAsync()).Deliveries).Outcome);
    }

    [Fact]
    public async Task Clear_history_hides_every_playback_so_far_and_keeps_their_events()
    {
        var arrival = await PlayAsync(_theater, "/elsewhere/Arrival.mkv", 2.3975);
        await PlayAsync(_bedroom, "/elsewhere/Heat.mkv", 1.85);
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Count() == 2);

        var cleared = await _host.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(new ClearHistory(), CancellationToken);

        Assert.True(cleared.IsSuccess);
        Assert.Equal((0, 0, 0), await CountHistoryRowsAsync());
        await using (var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession())
        {
            Assert.Equal(2, (await session.Events.FetchStreamStateAsync(arrival.Id, CancellationToken))!.Version);
        }

        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var ran = await PlayAsync(_theater, "/elsewhere/Ran.mkv", 1.85);
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Count() == 3);
        Assert.Equal((1, 1, 1), await CountHistoryRowsAsync());
        Assert.Equal("/elsewhere/Ran.mkv", ran.LocalPath?.Value);
    }

    [Fact]
    public async Task Removing_a_player_or_a_notifier_and_archiving_a_video_file_keep_their_playbacks()
    {
        var path = Write("Movies/Arrival.mkv");
        await SetCurrentResultAsync(path, 2.3964, AspectRatioSource.Detected);
        await PlayAsync(_theater, "smb://nas/media/Movies/Arrival.mkv", 1.7778);
        await Poll.UntilAsync(() => _events.OfType<DeliveryFinished>().Any());

        Assert.True((await _host.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(new RemovePlayer(_theaterId), CancellationToken)).IsSuccess);
        Assert.True((await _host.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(new RemoveNotifier(_automationId), CancellationToken)).IsSuccess);
        File.Delete(path);
        await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _scanner.LibraryScanAsync(CancellationToken);
        Assert.Empty(await TestVideoFile.ReadMediaRowsAsync(_host.Services.GetRequiredService<IDocumentStore>(), CancellationToken));

        var playback = Assert.Single(await StoredPlaybacksAsync());
        Assert.Equal(("Theater", (double?)2.39), (playback.PlayerName, (playback.Outcome as PlaybackOutcome.Sent)?.AspectRatio));
        Assert.Equal("automation", Assert.Single(playback.Deliveries).NotifierName);
    }

    private static PlaybackOutcome Sent(double aspectRatio, NotificationAspectRatioSource source, Guid? detectionId = null) =>
        new PlaybackOutcome.Sent(aspectRatio, source, detectionId);

    /// <summary>Plays the file on the player and returns its stored playback.</summary>
    private async Task<PlaybackRow> PlayAsync(FakeKodiServer player, string file, double? playerAspectRatio)
    {
        var streamDetails = playerAspectRatio is { } aspectRatio ? $$$""","streamdetails":{"video":[{"aspect":{{{aspectRatio}}}}]}""" : "";
        player.Respond("Player.GetItem", $$$"""{"item":{"type":"movie","title":"Arrival","year":2016,"file":"{{{file}}}"{{{streamDetails}}}}}""");
        var handledCount = _events.OfType<PlaybackHandled>().Count();

        await player.SendRawAsync("""{"jsonrpc":"2.0","method":"Player.OnPlay","params":{"data":{"item":{"type":"movie"},"player":{"playerid":1}},"sender":"xbmc"}}""");

        await Poll.UntilAsync(() => _events.OfType<PlaybackHandled>().Count() > handledCount, TimeSpan.FromSeconds(10));
        var playbackId = _events.OfType<PlaybackHandled>().Last().PlaybackId;
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return (await session.LoadAsync<PlaybackRow>(playbackId, CancellationToken))!;
    }

    /// <summary>The line that reports the outcome of the command named <paramref name="command"/>; null until it is logged.</summary>
    private FakeLogRecord? CommandLine(string command) =>
        _logs.Collector.GetSnapshot().SingleOrDefault(log => log.GetStructuredStateValue("Command") == command);

    private async Task<IReadOnlyList<PlaybackRow>> StoredPlaybacksAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return await (await session.ShownPlaybackRowsAsync(CancellationToken)).ToListAsync(CancellationToken);
    }

    private async Task<(int Playbacks, int Deliveries, int LastPlaybacks)> CountHistoryRowsAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return (
            await session.CountShownPlaybackRowsAsync(CancellationToken),
            (await session.ReadNewestDeliveriesAsync(_automationId, int.MaxValue, CancellationToken)).Count,
            (await session.ReadLastPlaybacksAsync([_theaterId, _bedroomId], CancellationToken)).Count);
    }

    private async Task<Guid?> DetectionIdAsync()
    {
        var store = _host.Services.GetRequiredService<IDocumentStore>();
        var filePath = Assert.Single(await TestVideoFile.ReadStoredFilePathsAsync(store, CancellationToken));
        return Assert.Single(await TestVideoFile.ReadDetectionsAsync(store, filePath.VideoFile, CancellationToken)).Id;
    }

    private string Write(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
        return path;
    }

    /// <summary>Scans the file and records a detection result for its video file.</summary>
    private async Task<FileHash> SetCurrentResultAsync(string path, double rawAspectRatio, AspectRatioSource source)
    {
        var videoFile = (await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken))!.Value;
        await TestVideoFile.AppendAsync(
            _host.Services.GetRequiredService<IDocumentStore>(),
            videoFile,
            [new AspectRatioDetected(videoFile, TestVideoFile.Detected(rawAspectRatio, source, path, startedAt: Now))],
            CancellationToken);
        return videoFile;
    }

    /// <summary>Makes every write of a new Media row fail while refused.</summary>
    private async Task SetMediaRowsRefusedAsync(bool refused)
    {
        var table = TestVideoFile.MediaRowTableName(_host.Services.GetRequiredService<IDocumentStore>());
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = refused
            ? $"create trigger refuse_media_row before insert on {table} begin select raise(abort, 'The Media row was refused.'); end"
            : "drop trigger refuse_media_row";
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    /// <summary>Holds every webhook request that arrives until <see cref="Release"/>.</summary>
    private void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Release() => _gate.TrySetResult();

    /// <summary>Answers the webhook, waiting while held and recording the body as cancelled or received.</summary>
    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, string? body, CancellationToken cancellationToken)
    {
        var gate = _gate;
        if (!gate.Task.IsCompleted)
        {
            Interlocked.Increment(ref _held);
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Enqueue(body!);
                throw;
            }
        }

        Bodies.Enqueue(body!);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static TaskCompletionSource CompletedGate()
    {
        var gate = new TaskCompletionSource();
        gate.SetResult();
        return gate;
    }

    /// <summary>Holds the library scan job before it runs, while it occupies the scan execution group.</summary>
    private sealed class ScanHold : IJobListener
    {
        public string Name => nameof(ScanHold);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Released.Task.WaitAsync(cancellationToken);
        }

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
