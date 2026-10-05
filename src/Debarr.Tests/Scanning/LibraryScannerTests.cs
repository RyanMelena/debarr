using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher;
using Fisher.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Scanning;

public sealed class LibraryScannerTests : IAsyncLifetime
{
    // SHA-256 of the size as a little-endian int64, then the content.
    private const string AlphaHash = "2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192";
    private const string BravoHash = "e4e75575b66d57fcd160508154ba15328b2dd74356fb2c82d3ce75e36679fa47";
    private const string UppercaseAlphaHash = "40a79da4a6b1d8271cb9e49a4f6b14303eb5722a9a10896ab14430f13e8e0ff1";
    private const string LateHash = "913798ecb1e5d180c95ca845ef16a40d00327489e31698ddcffd2db8545e9b33";

    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly LibraryScanOutcome Finished = new LibraryScanOutcome.Finished();
    private static readonly DateTime WrittenAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _timeProvider = new(Start);
    private readonly ConcurrentQueue<object> _committed = new();
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;
    private IDisposable _subscription = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    private IDocumentStore Store => _host.Store;

    public async ValueTask InitializeAsync()
    {
        // The scans run on the fake clock and Quartz on the real one, which fires the scan jobs.
        _host = await TestHost.StartAsync(services =>
            services.AddSingleton(provider => ActivatorUtilities.CreateInstance<LibraryScanner>(provider, (TimeProvider)_timeProvider)));
        _scanner = _host.Services.GetRequiredService<LibraryScanner>();
        _subscription = _host.Services.GetRequiredService<ReadModelChangeListener>().Committed<object>().Subscribe(_committed.Enqueue);
        Directory.CreateDirectory(Root);

        await TestLibrary.AddRootFolderAsync(Store, Root, CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_library_scan_stores_each_file_with_a_video_extension_under_the_roots()
    {
        Write("a.mkv", "alpha");
        Write(Path.Combine("sub", "b.MP4"), "bravo");
        Write("notes.txt", "notes");
        Write("noextension", "none");

        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal(
            [("a.mkv", AlphaHash), (Path.Combine("sub", "b.MP4"), BravoHash)],
            await ReadFilePathsAsync());
        Assert.Equal(
            [(AlphaHash, 5L, Start), (BravoHash, 5L, Start)],
            await ReadVideoFilesAsync());
    }

    [Fact]
    public async Task A_hardlink_and_a_copy_share_one_video_file()
    {
        var original = Write("a.mkv", "alpha");
        CreateHardLink(Path.Combine(Root, "link.mkv"), original);
        File.Copy(original, Path.Combine(Root, "copy.mkv"));

        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal(
            [("a.mkv", AlphaHash), ("copy.mkv", AlphaHash), ("link.mkv", AlphaHash)],
            await ReadFilePathsAsync());
        Assert.Equal([(AlphaHash, 5L, Start)], await ReadVideoFilesAsync());
    }

    [Fact]
    public async Task A_renamed_file_keeps_its_video_file()
    {
        var original = Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        var videoFile = await ReadVideoFileAsync("a.mkv");

        File.Move(original, Path.Combine(Root, "renamed.mkv"));
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([("renamed.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal(videoFile, await ReadVideoFileAsync("renamed.mkv"));
        Assert.Equal([(AlphaHash, 5L, Start)], await ReadVideoFilesAsync());
    }

    [Fact]
    public async Task A_changed_file_gets_a_new_video_file_and_the_old_one_is_kept()
    {
        Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);

        Write("a.mkv", "ALPHA", WrittenAt.AddHours(1));
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([("a.mkv", UppercaseAlphaHash)], await ReadFilePathsAsync());
        Assert.Equal(
            [(AlphaHash, 5L, Start), (UppercaseAlphaHash, 5L, Start.AddHours(1))],
            await ReadVideoFilesAsync());
        Assert.Equal([typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(FilePathRemoved)], await ReadEventTypesAsync(new FileHash(AlphaHash)));
    }

    [Fact]
    public async Task A_file_whose_size_and_modification_time_held_is_not_hashed_again()
    {
        Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);

        Write("a.mkv", "ALPHA");
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([("a.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal([(AlphaHash, 5L, Start)], await ReadVideoFilesAsync());
        Assert.Single(_committed.OfType<FilePathAdded>());
    }

    [Fact]
    public async Task A_second_library_scan_of_an_unchanged_library_appends_nothing_to_its_video_files()
    {
        Write("a.mkv", "alpha");
        CreateHardLink(Path.Combine(Root, "link.mkv"), Write("b.mkv", "bravo"));
        await _scanner.LibraryScanAsync(CancellationToken);
        var appended = await CountVideoFileEventsAsync();

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal(5, appended);
        Assert.Equal(appended, await CountVideoFileEventsAsync());
        Assert.Equal(
            [(Start, TimeSpan.Zero, new LibraryScanCounts(3, 3, 2, 0, 0), Finished), (Start.AddHours(1), TimeSpan.Zero, new LibraryScanCounts(3, 0, 0, 0, 0), Finished)],
            await ReadLibraryScanSummariesAsync());
    }

    [Fact]
    public async Task A_deleted_file_loses_its_file_path_and_its_video_file_is_kept()
    {
        Write("a.mkv", "alpha");
        var deleted = Write("b.mkv", "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);

        File.Delete(deleted);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([("a.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal(
            [(AlphaHash, 5L, Start), (BravoHash, 5L, Start)],
            await ReadVideoFilesAsync());
    }

    [Fact]
    public async Task A_video_file_a_library_scan_leaves_with_no_path_is_archived_at_the_end_of_the_next_library_scan()
    {
        Write("a.mkv", "alpha");
        var deleted = Write("b.mkv", "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);
        File.Delete(deleted);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);
        var leftWithNoPath = (await ReadVideoFilesAsync()).Select(videoFile => videoFile.FileHash);
        var committedBefore = _committed.Count;

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([AlphaHash, BravoHash], leftWithNoPath);
        Assert.Equal([AlphaHash], (await ReadVideoFilesAsync()).Select(videoFile => videoFile.FileHash));
        Assert.Equal(typeof(VideoFileArchived), (await ReadEventTypesAsync(new FileHash(BravoHash)))[^1]);
        Assert.True(await IsArchivedAsync(new FileHash(BravoHash)));
        Assert.Single(CommittedSince(committedBefore).OfType<VideoFileArchived>());
    }

    [Fact]
    public async Task A_file_moved_between_folders_is_never_archived_while_a_library_scan_finds_it_before_the_next_one_ends()
    {
        var original = Write(Path.Combine("x", "a.mkv"), "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        var outside = Path.Combine(_host.DataDirectory, "moving.mkv");
        File.Move(original, outside);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Directory.CreateDirectory(Path.Combine(Root, "y"));
        File.Move(outside, Path.Combine(Root, "y", "a.mkv"));
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([(Path.Combine("y", "a.mkv"), AlphaHash)], await ReadFilePathsAsync());
        Assert.DoesNotContain(typeof(VideoFileArchived), await ReadEventTypesAsync(new FileHash(AlphaHash)));
    }

    [Fact]
    public async Task A_library_scan_removes_the_file_paths_under_no_enabled_root_folder_and_archives_their_video_files()
    {
        Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        await TestLibrary.AppendAsync(Store, [new RootFolderDisabled(new LocalPath(Root))], CancellationToken);
        var committedBefore = _committed.Count;

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Empty(await ReadFilePathsAsync());
        Assert.Empty(await ReadVideoFilesAsync());
        Assert.True(await IsArchivedAsync(new FileHash(AlphaHash)));
        Assert.Single(CommittedSince(committedBefore).OfType<VideoFileArchived>());
        Assert.Equal(new LibraryScanCounts(0, 0, 0, 1, 1), (await ReadLibraryScanSummariesAsync())[^1].Counts);
    }

    [Fact]
    public async Task A_library_scan_stores_what_it_found_hashed_deleted_and_archived()
    {
        var original = Write("a.mkv", "alpha");
        var deleted = Write("b.mkv", "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);

        File.Delete(deleted);
        File.Copy(original, Path.Combine(Root, "copy.mkv"));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await _scanner.LibraryScanAsync(CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal(
            [
                (Start, TimeSpan.Zero, new LibraryScanCounts(2, 2, 2, 0, 0), Finished),
                (Start.AddMilliseconds(1), TimeSpan.Zero, new LibraryScanCounts(2, 1, 0, 1, 0), Finished),
                (Start.AddMilliseconds(2), TimeSpan.Zero, new LibraryScanCounts(2, 0, 0, 0, 1), Finished),
            ],
            await ReadLibraryScanSummariesAsync());
    }

    [Fact]
    public async Task Copies_hashed_at_the_same_time_share_one_video_file()
    {
        for (var content = 0; content < 8; content++)
        {
            Write($"{content}-a.mkv", $"content {content}");
            Write(Path.Combine("copies", $"{content}-b.mkv"), $"content {content}");
        }

        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([(Start, TimeSpan.Zero, new LibraryScanCounts(16, 16, 8, 0, 0), Finished)], await ReadLibraryScanSummariesAsync());
        Assert.Equal(16, (await ReadFilePathsAsync()).Count);
        Assert.Equal(8, (await ReadVideoFilesAsync()).Count);
    }

    [Fact]
    public async Task A_failed_library_scan_records_its_error_in_its_summary_and_on_the_root_folder()
    {
        Write("a.mkv", "alpha");
        await RefuseFilePathsAsync();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => _scanner.LibraryScanAsync(CancellationToken));

        Assert.Contains("The scan was refused.", failure.Message);
        Assert.Equal([(Start, TimeSpan.Zero, new LibraryScanCounts(0, 0, 0, 0, 0), new LibraryScanOutcome.Failed(failure.Message))], await ReadLibraryScanSummariesAsync());
        Assert.Equal((Start, failure.Message), await ReadRootFolderScanAsync());
    }

    [Fact]
    public async Task A_cancelled_library_scan_ends_cancelled_with_the_counts_of_what_ran()
    {
        Write("a.mkv", "alpha");
        using var cancellation = new CancellationTokenSource();
        using var subscription = _host.Services.GetRequiredService<ReadModelChangeListener>().Committed<FilePathAdded>().Subscribe(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scanner.LibraryScanAsync(cancellation.Token));

        Assert.Equal([(Start, TimeSpan.Zero, new LibraryScanCounts(1, 1, 1, 0, 0), new LibraryScanOutcome.Cancelled())], await ReadLibraryScanSummariesAsync());
        Assert.Equal((null, null), await ReadRootFolderScanAsync());
    }

    [Fact]
    public async Task Removing_a_root_folder_archives_its_video_files_at_once_and_a_hardlink_under_another_root_folder_keeps_its_live()
    {
        var other = Path.Combine(_host.DataDirectory, "other");
        Directory.CreateDirectory(other);
        await TestLibrary.AddRootFolderAsync(Store, other, CancellationToken);
        var original = Write("a.mkv", "alpha");
        CreateHardLink(Path.Combine(other, "a-link.mkv"), original);
        Write("b.mkv", "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);
        var committedBefore = _committed.Count;

        await TestLibrary.AppendAsync(Store, [new RootFolderRemoved(new LocalPath(Root))], CancellationToken);
        var archived = await _host.Services.GetRequiredService<RootFolderRemover>().RemoveAsync(new DirectoryInfo(Root), CancellationToken);

        Assert.Equal(1, archived);
        Assert.Equal([(Path.Combine("..", "other", "a-link.mkv"), AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal([AlphaHash], (await ReadVideoFilesAsync()).Select(videoFile => videoFile.FileHash));
        Assert.True(await IsArchivedAsync(new FileHash(BravoHash)));
        Assert.False(await IsArchivedAsync(new FileHash(AlphaHash)));
        Assert.Single(CommittedSince(committedBefore).OfType<VideoFileArchived>());
    }

    [Fact]
    public async Task Re_adding_a_root_folder_restores_each_video_file_with_its_result_override_and_detections_and_a_new_first_seen_time()
    {
        Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        var alpha = new FileHash(AlphaHash);
        var result = TestVideoFile.Detected(2.39);
        var failure = TestVideoFile.Failed("no crop detected in 12 samples");
        var @override = new Override(new AspectRatio(2.2), false, "Kept", Start);
        await TestVideoFile.AppendAsync(
            Store,
            alpha,
            [new AspectRatioDetected(alpha, result), new DetectionFailed(alpha, failure), new OverrideSaved(@override)],
            CancellationToken);
        await TestLibrary.AppendAsync(Store, [new RootFolderDisabled(new LocalPath(Root))], CancellationToken);
        await _host.Services.GetRequiredService<RootFolderRemover>().RemoveAsync(new DirectoryInfo(Root), CancellationToken);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await TestLibrary.AppendAsync(Store, [new RootFolderEnabled(new LocalPath(Root))], CancellationToken);
        await _scanner.LibraryScanAsync(CancellationToken);

        var restored = await TestVideoFile.ReadVideoFileAsync(Store, alpha, CancellationToken);
        Assert.Equal(
            (Start, result.Id, failure.Id, @override, Start.AddHours(1)),
            (restored!.FirstSeenAt, restored.CurrentResult?.Id, restored.LastFailure?.Id, restored.Override, restored.FilePaths.Single().FirstSeenAt));
        Assert.False(await IsArchivedAsync(alpha));
        Assert.Equal(2, (await TestVideoFile.ReadDetectionsAsync(Store, alpha, CancellationToken)).Count);
        Assert.Equal(new LibraryScanCounts(1, 1, 1, 0, 0), (await ReadLibraryScanSummariesAsync())[^1].Counts);
    }

    [Fact]
    public async Task A_missing_root_keeps_its_file_paths()
    {
        Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);

        Directory.Move(Root, Root + "-unmounted");
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal([("a.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal([(AlphaHash, 5L, Start)], await ReadVideoFilesAsync());
    }

    [Fact]
    public async Task A_missing_root_stores_its_scan_error_until_a_library_scan_reads_it()
    {
        await _scanner.LibraryScanAsync(CancellationToken);
        var read = await ReadRootFolderScanAsync();

        Directory.Move(Root, Root + "-unmounted");
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);
        var missing = await ReadRootFolderScanAsync();

        Directory.Move(Root + "-unmounted", Root);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);
        var readAgain = await ReadRootFolderScanAsync();

        Assert.Equal((Start, null), read);
        Assert.Equal((Start.AddHours(1), "The folder is missing."), missing);
        Assert.Equal((Start.AddHours(2), null), readAgain);
    }

    [Fact]
    public async Task A_folder_scan_of_the_whole_root_records_nothing_on_the_root_folder()
    {
        Write("a.mkv", "alpha");
        await _scanner.ScanFolderAsync(new DirectoryInfo(Root), CancellationToken);
        var read = await ReadRootFolderScanAsync();

        Directory.Delete(Root, recursive: true);
        await _scanner.ScanFolderAsync(new DirectoryInfo(Root), CancellationToken);

        Assert.Equal((null, null), read);
        Assert.Equal((null, null), await ReadRootFolderScanAsync());
        Assert.Empty(await ReadLibraryScanSummariesAsync());
    }

    [Fact]
    public async Task A_folder_scan_under_a_root_keeps_the_root_scan_state()
    {
        Write(Path.Combine("sub", "a.mkv"), "alpha");

        await _scanner.ScanFolderAsync(new DirectoryInfo(Path.Combine(Root, "sub")), CancellationToken);

        Assert.Equal([("sub" + Path.DirectorySeparatorChar + "a.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal((null, null), await ReadRootFolderScanAsync());
    }

    [Fact]
    public async Task A_new_file_path_is_first_seen_when_a_scan_finds_it_and_a_rename_is_a_new_file_path()
    {
        var original = Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        var found = await ReadFilePathFirstSeenAtAsync("a.mkv");

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);
        var foundAgain = await ReadFilePathFirstSeenAtAsync("a.mkv");

        File.Move(original, Path.Combine(Root, "renamed.mkv"));
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal(Start, found);
        Assert.Equal(Start, foundAgain);
        Assert.Equal(Start.AddHours(2), await ReadFilePathFirstSeenAtAsync("renamed.mkv"));
    }

    [Fact]
    public async Task A_disabled_root_is_not_scanned()
    {
        Write("a.mkv", "alpha");
        await TestLibrary.AppendAsync(Store, [new RootFolderDisabled(new LocalPath(Root))], CancellationToken);

        await _scanner.LibraryScanAsync(CancellationToken);
        await _scanner.ScanFolderAsync(new DirectoryInfo(Root), CancellationToken);
        var videoFileId = await _scanner.ScanFileAsync(new LocalPath(Path.Combine(Root, "a.mkv")), CancellationToken);

        Assert.Null(videoFileId);
        Assert.Empty(await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_folder_scan_archives_nothing()
    {
        var deleted = Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        File.Delete(deleted);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.LibraryScanAsync(CancellationToken);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.ScanFolderAsync(new DirectoryInfo(Root), CancellationToken);

        Assert.Equal([(AlphaHash, 5L, Start)], await ReadVideoFilesAsync());
    }

    [Fact]
    public async Task A_folder_scan_changes_only_the_file_paths_under_its_folder()
    {
        var deletedInside = Write(Path.Combine("x", "a.mkv"), "alpha");
        var deletedOutside = Write(Path.Combine("y", "b.mkv"), "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);

        File.Delete(deletedInside);
        File.Delete(deletedOutside);
        Write(Path.Combine("x", "nested", "late.mkv"), "late");
        Write(Path.Combine("y", "outside.mkv"), "ALPHA");
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.ScanFolderAsync(new DirectoryInfo(Path.Combine(Root, "x")), CancellationToken);

        Assert.Equal(
            [(Path.Combine("x", "nested", "late.mkv"), LateHash), (Path.Combine("y", "b.mkv"), BravoHash)],
            await ReadFilePathsAsync());
        Assert.Equal([AlphaHash, LateHash, BravoHash], (await ReadVideoFilesAsync()).Select(videoFile => videoFile.FileHash));
    }

    [Fact]
    public async Task A_folder_scan_of_a_deleted_folder_deletes_its_file_paths()
    {
        Write(Path.Combine("x", "a.mkv"), "alpha");
        Write("b.mkv", "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);

        Directory.Delete(Path.Combine(Root, "x"), recursive: true);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _scanner.ScanFolderAsync(new DirectoryInfo(Path.Combine(Root, "x")), CancellationToken);

        Assert.Equal([("b.mkv", BravoHash)], await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_folder_scan_of_a_sibling_that_shares_the_root_name_as_a_prefix_does_nothing()
    {
        var sibling = Root + "2";
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "a.mkv"), "alpha");

        await _scanner.ScanFolderAsync(new DirectoryInfo(sibling), CancellationToken);
        var videoFileId = await _scanner.ScanFileAsync(new LocalPath(Path.Combine(sibling, "a.mkv")), CancellationToken);

        Assert.Null(videoFileId);
        Assert.Empty(await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_file_scan_stores_a_new_file_and_returns_its_video_file()
    {
        var path = Write("a.mkv", "alpha");

        var videoFileId = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        Assert.Equal(await ReadVideoFileAsync("a.mkv"), videoFileId);
        Assert.Equal([("a.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Equal([nameof(VideoFileDiscovered), $"{nameof(FilePathAdded)} {path}"], Describe(_committed));
    }

    [Fact]
    public async Task A_file_scan_of_an_unchanged_file_appends_nothing_and_hashes_nothing()
    {
        var path = Write("a.mkv", "alpha");
        var first = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);
        var appended = await CountVideoFileEventsAsync();

        _timeProvider.Advance(TimeSpan.FromHours(1));
        var second = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(appended, await CountVideoFileEventsAsync());
        Assert.Equal(Start, await ReadFilePathHashedAtAsync("a.mkv"));
        Assert.Single(_committed.OfType<FilePathAdded>());
    }

    [Fact]
    public async Task A_file_scan_matches_a_stored_file_path_written_with_the_alternate_separator()
    {
        var path = Write("a.mkv", "alpha");
        var first = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        var second = await _scanner.ScanFileAsync(new LocalPath(Root + Path.AltDirectorySeparatorChar + "a.mkv"), CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal([("a.mkv", AlphaHash)], await ReadFilePathsAsync());
        Assert.Single(_committed.OfType<FilePathAdded>());
    }

    [Fact]
    public async Task A_file_scan_of_a_changed_file_moves_its_path_to_a_new_video_file()
    {
        var path = Write("a.mkv", "alpha");
        var first = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        Write("a.mkv", "ALPHA", WrittenAt.AddHours(1));
        var second = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        Assert.NotEqual(first, second);
        Assert.Equal(await ReadVideoFileAsync("a.mkv"), second);
        Assert.Equal([("a.mkv", UppercaseAlphaHash)], await ReadFilePathsAsync());
        Assert.Equal([typeof(VideoFileDiscovered), typeof(FilePathAdded), typeof(FilePathRemoved)], await ReadEventTypesAsync(first!.Value));
    }

    [Fact]
    public async Task A_file_scan_of_a_missing_file_deletes_its_file_path()
    {
        var path = Write("a.mkv", "alpha");
        var videoFileId = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        File.Delete(path);
        var afterDelete = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        Assert.Null(afterDelete);
        Assert.Empty(await ReadFilePathsAsync());
        Assert.Equal([(AlphaHash, 5L, Start)], await ReadVideoFilesAsync());
        Assert.Equal(new FilePathRemoved(videoFileId!.Value, new LocalPath(path), Start), _committed.Last());
    }

    [Fact]
    public async Task A_file_scan_of_another_extension_stores_nothing()
    {
        var path = Write("a.txt", "alpha");

        var videoFileId = await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken);

        Assert.Null(videoFileId);
        Assert.Empty(await ReadFilePathsAsync());
    }

    [Fact]
    public async Task Concurrent_file_scans_of_one_new_file_store_it_once()
    {
        var paths = Enumerable.Range(0, 10).Select(number => Write($"{number:D2}.mkv", $"content {number:D2}")).ToList();

        foreach (var path in paths)
        {
            var videoFileIds = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => _scanner.ScanFileAsync(new LocalPath(path), CancellationToken), CancellationToken)));
            Assert.Single(videoFileIds.Distinct());
        }

        Assert.Equal(10, (await ReadFilePathsAsync()).Count);
        Assert.Equal(10, (await ReadVideoFilesAsync()).Count);
        Assert.Equal(10, _committed.OfType<FilePathAdded>().Count());
        Assert.Equal(10, _committed.OfType<VideoFileDiscovered>().Count());
    }

    [Fact]
    public async Task A_library_scan_keeps_a_path_that_a_file_scan_found_while_it_ran()
    {
        var gone = Write("gone.mkv", "bravo");
        var late = Write("late.mkv", "late");
        Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        File.Delete(gone);
        File.Delete(late);
        Write("b.mkv", "ALPHA");
        _timeProvider.Advance(TimeSpan.FromHours(1));

        // Runs inside the library scan's event for b.mkv, after its enumeration started and before its deletes.
        // late.mkv exists only during the file scan, which finds it unchanged and so publishes nothing into the held event.
        using var duringScan = _host.Services.GetRequiredService<ReadModelChangeListener>()
            .Committed<FilePathAdded>()
            .Where(added => added.Path.Value.EndsWith("b.mkv", StringComparison.Ordinal))
            .Take(1)
            .Subscribe(_ =>
            {
                _timeProvider.Advance(TimeSpan.FromMinutes(1));
                Write("late.mkv", "late");
                _scanner.ScanFileAsync(new LocalPath(late), CancellationToken).GetAwaiter().GetResult();
                File.Delete(late);
            });
        await _scanner.LibraryScanAsync(CancellationToken);

        Assert.Equal(
            [("a.mkv", AlphaHash), ("b.mkv", UppercaseAlphaHash), ("late.mkv", LateHash)],
            await ReadFilePathsAsync());
    }

    [Fact]
    public async Task A_scan_commits_what_each_file_it_hashes_changes_and_nothing_for_an_unchanged_one()
    {
        var original = Write("a.mkv", "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        var afterNewFile = Describe(CommittedSince(0));

        var link = Path.Combine(Root, "link.mkv");
        CreateHardLink(link, original);
        var mark = _committed.Count;
        await _scanner.LibraryScanAsync(CancellationToken);
        var afterLink = Describe(CommittedSince(mark));

        File.Delete(link);
        File.SetLastWriteTimeUtc(original, WrittenAt.AddHours(1));
        mark = _committed.Count;
        await _scanner.LibraryScanAsync(CancellationToken);
        var afterTouch = Describe(CommittedSince(mark));

        mark = _committed.Count;
        await _scanner.LibraryScanAsync(CancellationToken);
        var afterNoChange = Describe(CommittedSince(mark));

        Assert.Equal([nameof(VideoFileDiscovered), $"{nameof(FilePathAdded)} {original}"], afterNewFile);
        Assert.Equal([$"{nameof(FilePathAdded)} {link}"], afterLink);
        Assert.Equal([$"{nameof(FilePathAdded)} {original}", $"{nameof(FilePathRemoved)} {link}"], afterTouch);
        Assert.Empty(afterNoChange);
    }

    /// <summary>The video file events among the committed events, each as its type's name and its path.</summary>
    private static List<string> Describe(IEnumerable<object> committed) =>
    [
        .. committed.Select(stored => stored switch
        {
            FilePathAdded added => $"{nameof(FilePathAdded)} {added.Path.Value}",
            FilePathRemoved removed => $"{nameof(FilePathRemoved)} {removed.Path.Value}",
            VideoFileDiscovered => nameof(VideoFileDiscovered),
            _ => null,
        }).OfType<string>(),
    ];

    /// <summary>The events committed since the count of committed events was <paramref name="mark"/>.</summary>
    private IEnumerable<object> CommittedSince(int mark) => _committed.Skip(mark);

    [Fact]
    public async Task A_library_scan_job_publishes_its_start_and_its_end()
    {
        var scanEvents = await RunScanJobAsync(() => LibraryScanJob.TriggerNowAsync(_host.Scheduler, CancellationToken));

        Assert.Equal([new ScanStartedEvent(null), new ScanFinishedEvent(null, TimeSpan.Zero, null, false)], scanEvents);
    }

    [Fact]
    public async Task A_folder_scan_job_publishes_its_folder()
    {
        var folder = Path.Combine(Root, "x");

        var scanEvents = await RunScanJobAsync(() => FolderScanJob.ScheduleAsync(_host.Scheduler, folder, TimeSpan.Zero, CancellationToken));

        Assert.Equal([new ScanStartedEvent(folder), new ScanFinishedEvent(folder, TimeSpan.Zero, null, false)], scanEvents);
    }

    [Fact]
    public async Task A_failed_scan_job_publishes_its_error()
    {
        Write("a.mkv", "alpha");
        await RefuseFilePathsAsync();

        var scanEvents = await RunScanJobAsync(() => LibraryScanJob.TriggerNowAsync(_host.Scheduler, CancellationToken));

        Assert.Equal(new ScanStartedEvent(null), scanEvents[0]);
        var finished = Assert.IsType<ScanFinishedEvent>(Assert.Single(scanEvents.Skip(1)));
        Assert.Equal((string?)null, finished.Folder);
        Assert.Contains("The scan was refused.", finished.Error);
    }

    [Fact]
    public void Subscribers_share_one_job_listener_that_the_last_to_leave_removes()
    {
        var listenersWithNoSubscriber = _host.Scheduler.ListenerManager.GetJobListeners().Count;
        var first = _scanner.ActivityEvents.Subscribe(_ => { });
        var listenersWithOneSubscriber = _host.Scheduler.ListenerManager.GetJobListeners().Count;
        var second = _scanner.ActivityEvents.Subscribe(_ => { });
        var listenersWithTwoSubscribers = _host.Scheduler.ListenerManager.GetJobListeners().Count;

        second.Dispose();
        first.Dispose();

        Assert.Equal(listenersWithNoSubscriber + 1, listenersWithOneSubscriber);
        Assert.Equal(listenersWithOneSubscriber, listenersWithTwoSubscribers);
        Assert.Equal(listenersWithNoSubscriber, _host.Scheduler.ListenerManager.GetJobListeners().Count);
    }

    /// <summary>Starts the scheduler, schedules one scan job, and returns its scan events with the duration zeroed.</summary>
    private async Task<List<ActivityEvent>> RunScanJobAsync(Func<Task> schedule)
    {
        var scanEvents = new ConcurrentQueue<ActivityEvent>();
        using var subscription = _scanner.ActivityEvents
            .Where(activityEvent => activityEvent is ScanStartedEvent or ScanFinishedEvent)
            .Subscribe(scanEvents.Enqueue);
        await _host.Scheduler.Start(CancellationToken);

        await schedule();

        await Poll.UntilAsync(() => scanEvents.OfType<ScanFinishedEvent>().Any());
        return scanEvents
            .Select(activityEvent => activityEvent is ScanFinishedEvent finished ? finished with { Duration = TimeSpan.Zero } : activityEvent)
            .ToList();
    }

    private string Write(string relativePath, string content, DateTime? writtenAt = null)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, writtenAt ?? WrittenAt);
        return path;
    }

    private async Task<List<(string RelativePath, string FileHash)>> ReadFilePathsAsync()
    {
        var filePaths = await TestVideoFile.ReadStoredFilePathsAsync(Store, CancellationToken);

        return filePaths
            .Select(filePath => (Path.GetRelativePath(Root, filePath.Id), filePath.VideoFile.Value))
            .OrderBy(filePath => filePath.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<(string FileHash, long Size, DateTimeOffset FirstSeenAt)>> ReadVideoFilesAsync()
    {
        var videoFiles = await TestVideoFile.ReadMediaRowsAsync(Store, CancellationToken);

        return videoFiles
            .Select(videoFile => (FileHash: videoFile.FileHash.Value, videoFile.Size, videoFile.FirstSeenAt))
            .OrderBy(videoFile => videoFile.FileHash, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<FileHash> ReadVideoFileAsync(string relativePath)
    {
        var filePath = (await TestVideoFile.ReadStoredFilePathAsync(Store, Path.Combine(Root, relativePath), CancellationToken))!;
        return filePath.VideoFile;
    }

    private async Task<DateTimeOffset> ReadFilePathHashedAtAsync(string relativePath) => (await ReadMediaRowFilePathAsync(relativePath)).HashedAt;

    /// <summary>The path's entry in its video file's Media row.</summary>
    private async Task<FilePath> ReadMediaRowFilePathAsync(string relativePath)
    {
        var path = new LocalPath(Path.Combine(Root, relativePath));
        var row = (await TestVideoFile.ReadMediaRowAsync(Store, await ReadVideoFileAsync(relativePath), CancellationToken))!;
        return row.FilePaths.Single(filePath => filePath.Path == path);
    }

    /// <summary>The types of the events on the video file's stream, oldest first.</summary>
    private async Task<List<Type>> ReadEventTypesAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Events.FetchStreamAsync(videoFile.StreamId, token: CancellationToken)).Select(stored => stored.Data.GetType())];
    }

    /// <summary>How many events the streams of the video files in the library hold.</summary>
    private async Task<int> CountVideoFileEventsAsync()
    {
        var count = 0;
        foreach (var videoFile in await TestVideoFile.ReadMediaRowsAsync(Store, CancellationToken))
        {
            count += (await ReadEventTypesAsync(videoFile.FileHash)).Count;
        }

        return count;
    }

    private async Task<DateTimeOffset> ReadFilePathFirstSeenAtAsync(string relativePath) => (await ReadMediaRowFilePathAsync(relativePath)).FirstSeenAt;

    private async Task<(DateTimeOffset? LastScannedAt, string? ScanError)> ReadRootFolderScanAsync()
    {
        await using var session = Store.QuerySession();
        var lastScan = Assert.Single(await session.ReadRootFoldersAsync(CancellationToken)).LastScan;
        return (lastScan?.StartedAt, lastScan?.Error);
    }

    /// <summary>Every library scan's summary, oldest first.</summary>
    private async Task<List<(DateTimeOffset StartedAt, TimeSpan? Duration, LibraryScanCounts? Counts, LibraryScanOutcome? Outcome)>> ReadLibraryScanSummariesAsync()
    {
        await using var session = Store.QuerySession();
        var summaries = await session.Query<LibraryScanSummaryRow>().ToListAsync(CancellationToken);

        return summaries
            .OrderBy(summary => summary.StartedAt)
            .Select(summary => (summary.StartedAt, summary.Duration, summary.Counts, summary.Outcome))
            .ToList();
    }

    private async Task<bool> IsArchivedAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return (await session.Events.FetchStreamStateAsync(videoFile.StreamId, CancellationToken))!.IsArchived;
    }

    /// <summary>Makes every write of a new file path fail, as a full disk would.</summary>
    private async Task RefuseFilePathsAsync()
    {
        await using var connection = new SqliteConnection(_host.UnpooledConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"create trigger refuse_file_path before insert on {StoredFilePathQuery.TableName} begin select raise(abort, 'The scan was refused.'); end";
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static void CreateHardLink(string path, string existingPath)
    {
        var created = OperatingSystem.IsWindows()
            ? CreateHardLinkW(path, existingPath, IntPtr.Zero)
            : Link(existingPath, path) == 0;

        if (!created)
        {
            throw new IOException($"Could not link {path} to {existingPath}.", Marshal.GetLastPInvokeError());
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existingPath, string path);
}
