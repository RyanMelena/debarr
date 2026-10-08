using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Wolverine.Runtime;

namespace Debarr.Tests.EventStore;

public sealed class AggregateReadTests : AppTestContext
{
    private static readonly LocalPath Movies = new(Path.Combine(Path.GetTempPath(), "debarr-movies"));
    private static readonly LocalPath Shows = new(Path.Combine(Path.GetTempPath(), "debarr-shows"));
    private static readonly DateTimeOffset AddedAt = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly KodiEndpoint Endpoint = KodiEndpoint.Create("kodi.local", 9090, 60, 10).Value;

    [Fact]
    public async Task Before_their_first_event_the_settings_are_the_defaults_and_there_are_no_players_or_notifiers()
    {
        await using var session = Store.QuerySession();

        Assert.Null(await StreamVersionAsync(DetectionSettings.StreamId));
        var library = await Library.ReadAsync(session, CancellationToken);
        Assert.Equal((0, LibrarySettings.Default), (library.RootFolders.Count, library.Settings));
        Assert.Same(DetectionSettings.Default, await DetectionSettings.ReadAsync(session, CancellationToken));
        Assert.Same(UISettings.Default, await UISettings.ReadAsync(session, CancellationToken));
        Assert.Same(Players.Empty, await Players.ReadAsync(session, CancellationToken));
        Assert.Same(Notifiers.Empty, await Notifiers.ReadAsync(session, CancellationToken));
    }

    [Fact]
    public async Task Each_read_folds_the_saves_on_its_stream()
    {
        var settings = new LibrarySettings(VideoExtensions.Parse("mkv").Value, new ScanInterval(6), false);
        var formats = new DateTimeFormats("yyyy-MM-dd", "dddd, MMMM d yyyy", "HH:mm");
        var theater = new PlayerAdded(Guid.NewGuid(), "Theater", true, Endpoint, [], []);
        var automation = new NotifierAdded(Guid.NewGuid(), "automation", false, WebhookSettings.Create("http://automation.local", WebhookMethod.Post, []).Value);
        await AppendAsync(Library.StreamId, new LibrarySettingsChanged(settings));
        await AppendAsync(DetectionSettings.StreamId, new DetectionSettingsChanged(4, new PictureMeasurement(20, 10, 16, 64), 60));
        await AppendAsync(UISettings.StreamId, new UISettingsChanged(UITheme.Dark, formats, false));
        await AppendAsync(Players.StreamId, theater, new PlayerRenamed(theater.PlayerId, "Cinema"));
        await AppendAsync(Notifiers.StreamId, automation);

        await using var session = Store.QuerySession();

        Assert.Equal(settings, (await Library.ReadAsync(session, CancellationToken)).Settings);
        Assert.Equal(4, (await DetectionSettings.ReadAsync(session, CancellationToken)).SimultaneousDetections);
        Assert.Equal(new UISettings(UITheme.Dark, formats, false), await UISettings.ReadAsync(session, CancellationToken));
        var player = Assert.Single((await Players.ReadAsync(session, CancellationToken)).All);
        Assert.Equal((theater.PlayerId, "Cinema", true, Endpoint), (player.Id, player.Name, player.Enabled, player.Endpoint));
        var notifier = Assert.Single((await Notifiers.ReadAsync(session, CancellationToken)).All);
        Assert.Equal(
            (automation.NotifierId, "automation", false, new Uri("http://automation.local"), WebhookMethod.Post),
            (notifier.Id, notifier.Name, notifier.Enabled, ((WebhookSettings)notifier.Settings).Url, ((WebhookSettings)notifier.Settings).Method));
    }

    [Fact]
    public async Task A_player_saved_with_no_excluded_paths_stored_reads_with_none()
    {
        var theater = new PlayerAdded(Guid.NewGuid(), "Theater", true, Endpoint, [], null);
        await AppendAsync(Players.StreamId, theater, new PlayerChanged(theater.PlayerId, false, Endpoint, [], null));

        await using var session = Store.QuerySession();

        var player = Assert.Single((await Players.ReadAsync(session, CancellationToken)).All);
        Assert.Equal((false, 0), (player.Enabled, player.ExcludedPaths.Count));
    }

    [Fact]
    public async Task The_library_reads_its_root_folders_in_path_order()
    {
        await AppendAsync(Library.StreamId, new RootFolderAdded(Shows, AddedAt), new RootFolderAdded(Movies, AddedAt));

        await using var session = Store.QuerySession();

        Assert.Equal([new RootFolder(Movies, true, AddedAt), new RootFolder(Shows, true, AddedAt)], (await Library.ReadAsync(session, CancellationToken)).RootFolders);
    }

    [Fact]
    public async Task A_video_file_reads_by_folding_its_stream_live_and_archived()
    {
        var result = TestVideoFile.Detected(2.39);
        var videoFile = await TestVideoFile.AddAsync(Store, "/media/film.mkv", CancellationToken, followedBy: film => [new AspectRatioDetected(film, result)]);
        await using (var session = Store.QuerySession())
        {
            var live = await VideoFile.ReadAsync(session, videoFile, CancellationToken);
            Assert.Equal((false, result.Id, "/media/film.mkv"), (live!.Archived, live.CurrentResult?.Id, live.FilePaths.Single().Path.Value));
            Assert.Null(await VideoFile.ReadAsync(session, TestFileHash.For("unknown"), CancellationToken));
        }

        await TestVideoFile.AppendAsync(Store, videoFile, [new FilePathRemoved(videoFile, new LocalPath("/media/film.mkv"), AddedAt)], CancellationToken);
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(new ArchiveVideoFiles([videoFile], AddedAt), CancellationToken)).IsSuccess);

        await using (var session = Store.QuerySession())
        {
            Assert.True((await session.Events.FetchStreamStateAsync(videoFile.StreamId, CancellationToken))!.IsArchived);
            var archived = await VideoFile.ReadAsync(session, videoFile, CancellationToken);
            Assert.Equal((true, result.Id, 0), (archived!.Archived, archived.CurrentResult?.Id, archived.FilePaths.Count));
            Assert.Equal([result.Id], archived.Detections.Select(detection => detection.Id));
        }
    }

    private async Task AppendAsync(Guid streamId, params object[] events)
    {
        await using var session = Store.LightweightSession();
        await session.Events.AppendAtCurrentVersionAsync(streamId, events);
        await session.SaveChangesAsync(CancellationToken);
    }
}
