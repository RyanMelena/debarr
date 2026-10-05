using System.Collections.Concurrent;
using System.Reactive.Linq;
using Debarr.Activity;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Quartz;

namespace Debarr.Tests.Scanning;

public sealed class LibraryCommandTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerKey ScheduledTriggerKey = new("scheduled", "library-scan");

    private readonly FakeTimeProvider _timeProvider = new(Start);
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_saved_interval_schedules_the_next_scan_one_new_interval_after_the_save()
    {
        await StartHostAsync(fakeTime: true);

        _timeProvider.Advance(TimeSpan.FromHours(3));
        await _host.Runtime.SendCommandAsync(With(await ReadSettingsAsync(), scanIntervalHours: 6), CancellationToken);

        var scheduled = await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken);
        Assert.Equal(Start.AddHours(9), scheduled!.NextFireTimeUtc);
    }

    [Fact]
    public async Task A_saved_schedule_switched_off_removes_the_scheduled_scan()
    {
        await StartHostAsync(fakeTime: true);
        await LibraryScanJob.ScheduleAsync(_host.Scheduler, new ScanInterval(12), CancellationToken);

        await _host.Runtime.SendCommandAsync(With(await ReadSettingsAsync(), scanIntervalHours: null), CancellationToken);

        Assert.Null(await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken));
        Assert.Equal(ScanInterval.Off, (await ReadSettingsAsync()).ScanInterval);
    }

    [Fact]
    public async Task A_save_that_keeps_the_interval_leaves_the_scheduled_scan_alone()
    {
        await StartHostAsync(fakeTime: true);
        await LibraryScanJob.ScheduleAsync(_host.Scheduler, new ScanInterval(12), CancellationToken);

        _timeProvider.Advance(TimeSpan.FromHours(3));
        await _host.Runtime.SendCommandAsync(With(await ReadSettingsAsync(), scanIntervalHours: 12) with { WatchFolders = false }, CancellationToken);

        var scheduled = await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken);
        Assert.Equal(Start.AddHours(12), scheduled!.NextFireTimeUtc);
        Assert.False((await ReadSettingsAsync()).WatchFolders);
    }

    [Fact]
    public async Task A_refused_save_writes_nothing_and_leaves_the_scheduled_scan_alone()
    {
        await StartHostAsync(fakeTime: true);
        await LibraryScanJob.ScheduleAsync(_host.Scheduler, new ScanInterval(12), CancellationToken);
        _timeProvider.Advance(TimeSpan.FromHours(3));
        var saved = await _host.Runtime.SendCommandAsync(With(await ReadSettingsAsync(), scanIntervalHours: 6) with { VideoExtensions = " , " }, CancellationToken);

        Assert.Equal([nameof(ChangeLibrarySettings.VideoExtensions)], saved.Errors.OfType<FieldError>().Select(error => error.Field));
        Assert.Equal(LibrarySettings.Default, await ReadSettingsAsync());
        Assert.Equal(Start.AddHours(12), (await _host.Scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken))!.NextFireTimeUtc);
    }

    [Fact]
    public async Task An_added_root_gets_a_library_scan_that_records_its_scan()
    {
        await StartHostAsync(fakeTime: false);
        await _host.Scheduler.Start(CancellationToken);
        var file = Write(Path.Combine("sub", "a.mkv"), "alpha");

        await AddRootFolderAsync();

        await Poll.UntilAsync(HasLibraryScanRecordedTheRootFolderAsync);
        Assert.Equal([file], await ReadFilePathsAsync());
    }

    [Fact]
    public async Task An_enabled_root_gets_a_library_scan_that_records_its_scan()
    {
        await StartHostAsync(fakeTime: false);
        var root = await AddDisabledRootFolderAsync();
        await _host.Scheduler.Start(CancellationToken);
        var file = Write("a.mkv", "alpha");

        await _host.Runtime.SendCommandAsync(new SetRootFolderEnabled(root, true), CancellationToken);

        await Poll.UntilAsync(HasLibraryScanRecordedTheRootFolderAsync);
        Assert.Equal([file], await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_disabled_root_loses_its_file_paths_and_its_video_files_are_archived()
    {
        await StartHostAsync(fakeTime: false);
        await _host.Scheduler.Start(CancellationToken);
        var file = Write("a.mkv", "alpha");
        var root = await AddRootFolderAsync();
        await Poll.UntilAsync(async () => (await ReadFilePathsAsync()).SequenceEqual([file]));

        await _host.Runtime.SendCommandAsync(new SetRootFolderEnabled(root, false), CancellationToken);

        await Poll.UntilAsync(async () => await CountVideoFilesAsync() == 0);
        Assert.Empty(await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_removed_root_loses_its_file_paths_in_the_background_under_running_work_and_its_video_files_are_archived()
    {
        await StartHostAsync(fakeTime: false);
        await _host.Scheduler.Start(CancellationToken);
        var file = Write("a.mkv", "alpha");
        var root = await AddRootFolderAsync();
        await Poll.UntilAsync(async () => (await ReadFilePathsAsync()).SequenceEqual([file]));
        var store = _host.Store;
        var videoFile = Assert.Single(await TestVideoFile.ReadMediaRowsAsync(store, CancellationToken)).Id;
        var remover = _host.Services.GetRequiredService<RootFolderRemover>();
        var removal = new ConcurrentQueue<(ActivityEvent Event, RootFolderRemovalStartedEvent? Running)>();
        using var subscription = remover.ActivityEvents
            .Subscribe(activityEvent => removal.Enqueue((activityEvent, remover.RunningRootFolderRemoval)));

        await _host.Runtime.SendCommandAsync(new RemoveRootFolder(root), CancellationToken);
        await Poll.UntilAsync(() => removal.Count == 2);

        var (started, runningAtStart) = removal.First();
        Assert.Equal(Root, Assert.IsType<RootFolderRemovalStartedEvent>(started).RootFolder);
        Assert.Same(started, runningAtStart);
        Assert.Equal((new RootFolderRemovalFinishedEvent(Root, null), null), removal.Last());
        Assert.Empty(await ReadFilePathsAsync());
        Assert.Equal(0, await CountVideoFilesAsync());
        await using (var session = store.QuerySession())
        {
            Assert.True((await session.Events.FetchStreamStateAsync(videoFile, CancellationToken))!.IsArchived);
        }

        Assert.Empty(await ReadRootFolderPathsAsync());
    }

    [Fact]
    public async Task A_queued_removal_counts_as_running_from_when_it_was_queued_while_no_removal_runs()
    {
        await StartHostAsync(fakeTime: true);
        var remover = ActivatorUtilities.CreateInstance<RootFolderRemover>(_host.Services);

        await remover.EnqueueAsync(new LocalPath(Root), CancellationToken);

        Assert.Equal(new RootFolderRemovalStartedEvent(Root, Start), remover.RunningRootFolderRemoval);
    }

    [Fact]
    public async Task A_command_whose_follow_up_work_fails_still_succeeds()
    {
        await StartHostAsync(fakeTime: true);
        await _host.Scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken);
        var root = new LocalPath(Root);

        Result[] sent =
        [
            await _host.Runtime.SendCommandAsync(new AddRootFolder(root, Start), CancellationToken),
            await _host.Runtime.SendCommandAsync(new SetRootFolderEnabled(root, false), CancellationToken),
            await _host.Runtime.SendCommandAsync(new SetRootFolderEnabled(root, true), CancellationToken),
            await _host.Runtime.SendCommandAsync(With(LibrarySettings.Default, scanIntervalHours: 6), CancellationToken),
            await _host.Runtime.SendCommandAsync(new RemoveRootFolder(root), CancellationToken),
        ];

        Assert.All(sent, result => Assert.True(result.IsSuccess));
        Assert.Equal(6, (await ReadSettingsAsync()).ScanInterval.Hours);
        Assert.Empty(await ReadRootFolderPathsAsync());
    }

    /// <summary>Starts the host, on the fake clock when <paramref name="fakeTime"/> is set, and creates the root folder.</summary>
    private async Task StartHostAsync(bool fakeTime)
    {
        _host = await TestHost.StartAsync(services =>
        {
            services
                .AddHostedService(provider => provider.GetRequiredService<RootFolderRemover>())
                .AddQuartz(quartz => quartz.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromMilliseconds(100)));
            if (fakeTime)
            {
                services
                    .AddSingleton<TimeProvider>(_timeProvider)
                    .AddQuartz(quartz => quartz.UseTimeProvider(_timeProvider));
            }
        });
        Directory.CreateDirectory(Root);
    }

    private async Task<LocalPath> AddRootFolderAsync()
    {
        var root = new LocalPath(Root);
        Assert.True((await _host.Runtime.SendCommandAsync(new AddRootFolder(root, DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);
        return root;
    }

    private async Task<LocalPath> AddDisabledRootFolderAsync()
    {
        await TestLibrary.AddRootFolderAsync(_host.Store, Root, CancellationToken, enabled: false);
        return new LocalPath(Root);
    }

    private static ChangeLibrarySettings With(LibrarySettings settings, int? scanIntervalHours) =>
        new(settings.VideoExtensions.ToString(), scanIntervalHours, settings.WatchFolders);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<LibrarySettings> ReadSettingsAsync()
    {
        await using var session = CreateSession();
        return (await Library.ReadAsync(session, CancellationToken)).Settings;
    }

    private async Task<List<string>> ReadFilePathsAsync() =>
        [.. (await TestVideoFile.ReadStoredFilePathsAsync(_host.Store, CancellationToken)).Select(filePath => filePath.Id)];

    private async Task<List<string>> ReadRootFolderPathsAsync()
    {
        await using var session = CreateSession();
        return [.. (await Library.ReadAsync(session, CancellationToken)).RootFolders.Select(root => root.Path.Value)];
    }

    /// <summary>Whether a library scan ended and recorded its scan on the root folder.</summary>
    private async Task<bool> HasLibraryScanRecordedTheRootFolderAsync()
    {
        await using var session = CreateSession();
        return (await session.ReadNewestLibraryScansAsync(1, CancellationToken)) is [{ Closed: true }]
            && (await session.ReadRootFoldersAsync(CancellationToken)).Any(root => root.RootFolder.Path.Value == Root && root.LastScan is not null);
    }

    private async Task<int> CountVideoFilesAsync() =>
        (await TestVideoFile.ReadMediaRowsAsync(_host.Store, CancellationToken)).Count;

    private IQuerySession CreateSession() => _host.Store.QuerySession();
}
