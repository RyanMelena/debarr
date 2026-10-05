using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Quartz;

namespace Debarr.Tests.Scanning;

public sealed class LibraryStartupServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerKey ScheduledTriggerKey = new("scheduled", "library-scan");
    private static readonly TriggerKey ImmediateTriggerKey = new("immediate", "library-scan");

    private readonly string _movies = Path.Combine(Path.GetTempPath(), "debarr-movies");
    private readonly FakeTimeProvider _timeProvider = new(Start);
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _host = await TestHost.StartAsync(services => services
            .AddSingleton<TimeProvider>(_timeProvider)
            .AddSingleton<LibraryStartupService>()
            .AddQuartz(quartz => quartz.UseTimeProvider(_timeProvider)));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Startup_schedules_the_first_scan_one_interval_later_and_runs_a_library_scan_now()
    {
        await TestLibrary.AddRootFolderAsync(_host.Services.GetRequiredService<IDocumentStore>(), _movies, CancellationToken);

        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);

        var scheduled = await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken);
        Assert.Equal(Start.AddHours(12), scheduled!.NextFireTimeUtc);
        Assert.True(await _host.Scheduler.Exists(ImmediateTriggerKey, CancellationToken));
    }

    [Fact]
    public async Task Startup_schedules_the_first_scan_and_runs_no_library_scan_when_the_library_has_no_enabled_root_folder_and_no_video_file()
    {
        await TestLibrary.AddRootFolderAsync(_host.Services.GetRequiredService<IDocumentStore>(), _movies, CancellationToken, enabled: false);

        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);

        Assert.NotNull(await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken));
        Assert.False(await _host.Scheduler.Exists(ImmediateTriggerKey, CancellationToken));
    }

    [Fact]
    public async Task Startup_runs_a_library_scan_now_when_the_library_has_a_video_file_and_no_root_folder()
    {
        var videoFile = TestFileHash.For("film");
        await TestVideoFile.AppendAsync(_host.Services.GetRequiredService<IDocumentStore>(), videoFile, [new VideoFileDiscovered(videoFile, 1, Start)], CancellationToken);

        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);

        Assert.True(await _host.Scheduler.Exists(ImmediateTriggerKey, CancellationToken));
    }

    [Fact]
    public async Task Startup_schedules_the_first_scan_one_saved_interval_later_and_none_while_the_schedule_is_off()
    {
        await TestLibrary.AppendAsync(
            _host.Services.GetRequiredService<IDocumentStore>(),
            [new LibrarySettingsChanged(LibrarySettings.Default with { ScanInterval = new ScanInterval(3) })],
            CancellationToken);
        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);
        var scheduled = await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken);

        await TestLibrary.AppendAsync(
            _host.Services.GetRequiredService<IDocumentStore>(),
            [new LibrarySettingsChanged(LibrarySettings.Default with { ScanInterval = ScanInterval.Off })],
            CancellationToken);
        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);

        Assert.Equal(Start.AddHours(3), scheduled!.NextFireTimeUtc);
        Assert.Null(await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken));
    }

    [Fact]
    public async Task Startup_interrupts_each_open_library_scan_before_it_queues_its_own()
    {
        var store = _host.Services.GetRequiredService<IDocumentStore>();
        await TestLibrary.AddRootFolderAsync(store, _movies, CancellationToken);
        var open = Guid.CreateVersion7();
        var ended = Guid.CreateVersion7();
        await TestLibrary.AppendLibraryScanAsync(store, open, [new LibraryScanStarted(Start.AddHours(-1))], CancellationToken);
        await TestLibrary.AppendLibraryScanAsync(
            store,
            ended,
            [new LibraryScanStarted(Start.AddHours(-2)), new LibraryScanEnded(Start.AddHours(-2), new LibraryScanCounts(1, 0, 0, 0, 0), new LibraryScanOutcome.Finished())],
            CancellationToken);
        var listener = new QueuedScanListener(() => ReadEventTypesAsync(open));
        _host.Scheduler.ListenerManager.AddSchedulerListener(listener);

        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);
        await _host.Services.GetRequiredService<LibraryStartupService>().StartAsync(CancellationToken);

        Assert.Equal([typeof(LibraryScanStarted), typeof(LibraryScanInterrupted)], listener.OpenScanWhenQueued);
        Assert.Equal([typeof(LibraryScanStarted), typeof(LibraryScanInterrupted)], await ReadEventTypesAsync(open));
        Assert.Equal([typeof(LibraryScanStarted), typeof(LibraryScanEnded)], await ReadEventTypesAsync(ended));
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        Assert.IsType<LibraryScanOutcome.Interrupted>((await session.LoadAsync<LibraryScanSummaryRow>(open, CancellationToken))!.Outcome);
    }

    private async Task<List<Type>> ReadEventTypesAsync(Guid streamId)
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return [.. (await session.Events.FetchStreamAsync(streamId, token: CancellationToken)).Select(stored => stored.Data.GetType())];
    }

    /// <summary>Reads the open scan's events when startup first queues its library scan.</summary>
    private sealed class QueuedScanListener(Func<Task<List<Type>>> readOpenScan) : ISchedulerListener
    {
        public List<Type>? OpenScanWhenQueued { get; private set; }

        public async ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            if (trigger.Key.Equals(ImmediateTriggerKey) && OpenScanWhenQueued is null)
            {
                OpenScanWhenQueued = await readOpenScan();
            }
        }
    }
}
