using Debarr.Detecting;
using Debarr.Health;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher;

namespace Debarr.Tests.Scanning;

public sealed class RootFolderHealthCheckTests : AppTestContext
{
    [Fact]
    public async Task A_library_with_no_enabled_root_is_a_warning_that_links_to_library_settings()
    {
        await TestLibrary.AddRootFolderAsync(GetAppService<IDocumentStore>(), Path.Combine(Path.GetTempPath(), "debarr-disabled"), CancellationToken, enabled: false);

        var message = Assert.Single(await CheckAsync<RootFolderHealthCheck>());

        Assert.Equal((HealthSeverity.Warning, "settings/library"), (message.Severity, message.Href));
        Assert.StartsWith("No root folder is enabled", message.Text);
    }

    [Fact]
    public async Task An_enabled_root_whose_scan_failed_is_an_error_since_that_scan()
    {
        var scannedAt = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var missing = Path.Combine(Path.GetTempPath(), "debarr-missing");
        await TestLibrary.AddRootFolderAsync(GetAppService<IDocumentStore>(), missing, CancellationToken, scannedAt: scannedAt, scanError: "The folder is missing.");

        var message = Assert.Single(await CheckAsync<RootFolderHealthCheck>());

        Assert.Equal(
            (HealthSeverity.Error, $"Root folder {missing} can't be scanned: The folder is missing.", scannedAt),
            (message.Severity, message.Text, message.Since));
    }

    [Fact]
    public async Task The_check_runs_again_when_a_commit_changes_the_library_or_a_library_scan()
    {
        var store = GetAppService<IDocumentStore>();
        var root = Path.Combine(Path.GetTempPath(), "debarr-missing");
        await WaitForMessagesAsync(messages => messages.Any(message => message.Text.StartsWith("No root folder is enabled", StringComparison.Ordinal)));

        await TestLibrary.AddRootFolderAsync(store, root, CancellationToken);
        await WaitForMessagesAsync(messages => !messages.Any(message => message.Href == "settings/library"));

        var scannedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(
            store,
            Guid.CreateVersion7(),
            [new LibraryScanStarted(scannedAt), new RootFolderScanned(new LocalPath(root), scannedAt, "The folder is missing.")],
            CancellationToken);
        await WaitForMessagesAsync(messages => messages.Any(message => message.Text == $"Root folder {root} can't be scanned: The folder is missing."));
    }

    [Fact]
    public async Task An_enabled_root_that_scanned_finds_nothing()
    {
        await TestLibrary.AddRootFolderAsync(GetAppService<IDocumentStore>(), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), CancellationToken, scannedAt: DateTimeOffset.UtcNow);

        Assert.Empty(await CheckAsync<RootFolderHealthCheck>());
    }

    [Fact]
    public async Task File_paths_left_under_a_removed_root_folder_are_a_warning_that_sends_to_scan_now_until_they_are_gone()
    {
        var store = GetAppService<IDocumentStore>();
        var removed = Path.Combine(Path.GetTempPath(), "debarr-removed");
        await TestLibrary.AddRootFolderAsync(store, removed, CancellationToken);
        var videoFile = await TestVideoFile.AddAsync(store, [Path.Combine(removed, "a.mkv"), Path.Combine(removed, "b.mkv")], CancellationToken);
        await TestLibrary.AppendAsync(store, [new RootFolderRemoved(new LocalPath(removed))], CancellationToken);

        var message = Assert.Single(await CheckAsync<RootFolderHealthCheck>(), message => message.Href == "system/tasks");

        Assert.Equal(
            (HealthSeverity.Warning, $"2 file paths remain under root folder {removed}, which was removed. Scan Now removes them.", "Fix in System > Tasks"),
            (message.Severity, message.Text, message.LinkText));

        await TestVideoFile.AppendAsync(
            store,
            videoFile,
            [new FilePathRemoved(videoFile, new LocalPath(Path.Combine(removed, "a.mkv")), DateTimeOffset.UtcNow), new FilePathRemoved(videoFile, new LocalPath(Path.Combine(removed, "b.mkv")), DateTimeOffset.UtcNow)],
            CancellationToken);
        await WaitForMessagesAsync(messages => !messages.Any(message => message.Href == "system/tasks"));
    }

    [Fact]
    public async Task A_file_path_left_under_a_disabled_root_folder_is_a_warning()
    {
        var store = GetAppService<IDocumentStore>();
        var disabled = Path.Combine(Path.GetTempPath(), "debarr-disabled");
        await TestLibrary.AddRootFolderAsync(store, disabled, CancellationToken, enabled: false);
        await TestVideoFile.AddAsync(store, Path.Combine(disabled, "a.mkv"), CancellationToken);

        var message = Assert.Single(await CheckAsync<RootFolderHealthCheck>(), message => message.Href == "system/tasks");

        Assert.Equal($"1 file path remains under root folder {disabled}, which is disabled. Scan Now removes it.", message.Text);
    }

    [Fact]
    public async Task File_paths_under_an_enabled_root_folder_inside_a_removed_one_are_no_warning()
    {
        var store = GetAppService<IDocumentStore>();
        var removed = Path.Combine(Path.GetTempPath(), "debarr-media");
        var inside = Path.Combine(removed, "movies");
        await TestLibrary.AddRootFolderAsync(store, removed, CancellationToken);
        await TestLibrary.AppendAsync(store, [new RootFolderRemoved(new LocalPath(removed))], CancellationToken);
        await TestLibrary.AddRootFolderAsync(store, inside, CancellationToken);
        await TestVideoFile.AddAsync(store, Path.Combine(inside, "a.mkv"), CancellationToken);

        Assert.DoesNotContain(await CheckAsync<RootFolderHealthCheck>(), message => message.Href == "system/tasks");
    }

    [Fact]
    public async Task File_paths_under_a_root_folder_whose_removal_is_waiting_or_running_are_no_warning()
    {
        var store = GetAppService<IDocumentStore>();
        var removed = Path.Combine(Path.GetTempPath(), "debarr-removing");
        await TestLibrary.AddRootFolderAsync(store, removed, CancellationToken);
        await TestVideoFile.AddAsync(store, Path.Combine(removed, "a.mkv"), CancellationToken);
        await TestLibrary.AppendAsync(store, [new RootFolderRemoved(new LocalPath(removed))], CancellationToken);

        using (await GetAppService<LibraryScanner>().PauseScansAsync(CancellationToken))
        {
            await GetAppService<RootFolderRemover>().EnqueueAsync(new LocalPath(removed), CancellationToken);

            Assert.DoesNotContain(await CheckAsync<RootFolderHealthCheck>(), message => message.Href == "system/tasks");
        }

        await Poll.UntilAsync(() => GetAppService<RootFolderRemover>().RunningRootFolderRemoval is null);
        Assert.DoesNotContain(await CheckAsync<RootFolderHealthCheck>(), message => message.Href == "system/tasks");
    }
}
