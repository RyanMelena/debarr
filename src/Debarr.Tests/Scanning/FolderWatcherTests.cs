using Debarr.EventStore;
using Debarr.Scanning;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Quartz;

namespace Debarr.Tests.Scanning;

public sealed class FolderWatcherTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerKey ImmediateTriggerKey = new("immediate", "library-scan");

    private readonly FakeTimeProvider _timeProvider = new(Start);
    private readonly FakeLoggerProvider _logs = new();
    private TestHost _host = null!;
    private FolderWatcher _watcher = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(
            services => services
                .AddSingleton<TimeProvider>(_timeProvider)
                .AddQuartz(quartz => quartz.UseTimeProvider(_timeProvider)),
            logging => logging.AddProvider(_logs).AddFilter<FakeLoggerProvider>("Debarr", LogLevel.Information));
        _watcher = _host.Services.GetRequiredService<FolderWatcher>();
        Directory.CreateDirectory(Root);

        await TestLibrary.AddRootFolderAsync(_host.Services.GetRequiredService<IDocumentStore>(), Root, CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _watcher.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_second_change_in_a_folder_pushes_its_scan_back()
    {
        var subfolder = Directory.CreateDirectory(Path.Combine(Root, "sub")).FullName;
        await StartWatchingAsync();
        var triggerKey = new TriggerKey(subfolder, "folder-scan");

        File.WriteAllText(Path.Combine(subfolder, "a.mkv"), "alpha");
        await Poll.UntilAsync(async () => (await _host.Scheduler.GetTrigger(triggerKey, CancellationToken))?.NextFireTimeUtc == Start.AddSeconds(10));

        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        File.WriteAllText(Path.Combine(subfolder, "b.mkv"), "bravo");

        await Poll.UntilAsync(async () => (await _host.Scheduler.GetTrigger(triggerKey, CancellationToken))?.NextFireTimeUtc == Start.AddSeconds(14));
    }

    [Fact]
    public async Task A_folder_scan_covers_only_the_folder_that_changed()
    {
        var inner = Directory.CreateDirectory(Path.Combine(Root, "sub", "inner")).FullName;
        await StartWatchingAsync();

        File.WriteAllText(Path.Combine(inner, "a.mkv"), "alpha");

        await Poll.UntilAsync(async () => (await ReadScheduledFolderScansAsync()).SequenceEqual([inner]));
    }

    [Fact]
    public async Task A_file_moved_between_folders_scans_both_folders()
    {
        var source = Directory.CreateDirectory(Path.Combine(Root, "x")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(Root, "y")).FullName;
        File.WriteAllText(Path.Combine(source, "a.mkv"), "alpha");
        await StartWatchingAsync();

        File.Move(Path.Combine(source, "a.mkv"), Path.Combine(destination, "a.mkv"));

        await Poll.UntilAsync(async () => (await ReadScheduledFolderScansAsync()).SequenceEqual([source, destination]));
    }

    [Fact]
    public async Task A_file_renamed_in_its_folder_scans_that_folder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Root, "x")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.mkv"), "alpha");
        await StartWatchingAsync();

        File.Move(Path.Combine(folder, "a.mkv"), Path.Combine(folder, "b.mkv"));

        await Poll.UntilAsync(async () => (await ReadScheduledFolderScansAsync()).SequenceEqual([folder]));
    }

    [Fact]
    public async Task A_watcher_error_runs_a_library_scan()
    {
        await StartWatchingAsync();

        // Deleting the watched folder fails the watcher's read of its changes.
        Directory.Delete(Root, recursive: true);

        await Poll.UntilAsync(async () => await _host.Scheduler.Exists(ImmediateTriggerKey, CancellationToken));
    }

    [Fact]
    public async Task A_disabled_watch_holds_no_watchers()
    {
        await StartWatchingAsync();
        var settings = await ReadSettingsAsync();

        await _host.Runtime.SendCommandAsync(
            new ChangeLibrarySettings(settings.VideoExtensions.ToString(), settings.ScanInterval.Hours, false),
            CancellationToken);
        await WatchingAsync([]);
        File.WriteAllText(Path.Combine(Root, "a.mkv"), "alpha");

        await AssertNoFolderScanScheduledAsync();
    }

    [Fact]
    public async Task An_added_root_is_watched_beside_the_others()
    {
        var other = Directory.CreateDirectory(Path.Combine(_host.DataDirectory, "other")).FullName;
        var subfolder = Directory.CreateDirectory(Path.Combine(other, "sub")).FullName;
        await StartWatchingAsync();

        await _host.Runtime.SendCommandAsync(new AddRootFolder(new LocalPath(other), Start), CancellationToken);
        await WatchingAsync([other, Root]);
        File.WriteAllText(Path.Combine(subfolder, "a.mkv"), "alpha");

        await Poll.UntilAsync(async () => (await ReadScheduledFolderScansAsync()).Contains(subfolder));
    }

    [Fact]
    public async Task A_disabled_root_is_no_longer_watched()
    {
        await StartWatchingAsync();

        await _host.Runtime.SendCommandAsync(new SetRootFolderEnabled(new LocalPath(Root), false), CancellationToken);
        await WatchingAsync([]);
        File.WriteAllText(Path.Combine(Root, "a.mkv"), "alpha");

        await AssertNoFolderScanScheduledAsync();
    }

    [Fact]
    public async Task Stopping_closes_every_watcher()
    {
        await StartWatchingAsync();

        await _watcher.StopAsync(CancellationToken);
        File.WriteAllText(Path.Combine(Root, "a.mkv"), "alpha");

        await AssertNoFolderScanScheduledAsync();
    }

    /// <summary>Starts the watcher and waits until the root's watcher runs.</summary>
    private async Task StartWatchingAsync()
    {
        await _watcher.StartAsync(CancellationToken);
        await WatchingAsync([Root]);
    }

    /// <summary>Waits until the watcher's latest log line says it watches exactly these root folders, which it writes once their watchers run.</summary>
    private Task WatchingAsync(IReadOnlyList<string> rootPaths)
    {
        var expected = rootPaths.Count == 0 ? "Watching no root folder." : $"Watching {string.Join(", ", rootPaths)}.";
        return Poll.UntilAsync(() => _logs.Collector.GetSnapshot()
            .Where(record => record.Category == typeof(FolderWatcher).FullName && record.Message.StartsWith("Watching", StringComparison.Ordinal))
            .LastOrDefault()?.Message == expected);
    }

    private async Task AssertNoFolderScanScheduledAsync()
    {
        // Long enough for a watcher's event to schedule a scan.
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        Assert.Empty(await ReadScheduledFolderScansAsync());
    }

    private async Task<LibrarySettings> ReadSettingsAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return (await Library.ReadAsync(session, CancellationToken)).Settings;
    }

    /// <summary>The folders with a waiting folder scan, in ordinal order.</summary>
    private async Task<List<string>> ReadScheduledFolderScansAsync() =>
        (await _host.Scheduler.GetTriggersOfJob(FolderScanJob.Key, CancellationToken))
            .Select(trigger => trigger.Key.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
}
