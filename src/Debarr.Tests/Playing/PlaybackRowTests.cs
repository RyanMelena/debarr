using Debarr.Detecting;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;

namespace Debarr.Tests.Playing;

public sealed class PlaybackRowTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    public static TheoryData<PlaybackOutcome> Outcomes =>
    [
        new PlaybackOutcome.Sent(2.39, NotificationAspectRatioSource.Detected, new Guid("0199a3c4-5b6e-7000-8000-000000000004")),
        new PlaybackOutcome.Sent(2.2, NotificationAspectRatioSource.Manual, null),
        new PlaybackOutcome.Failed("boom"),
        new PlaybackOutcome.NoPlayerAspectRatio(),
    ];

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task A_handled_playback_is_a_document_with_everything_it_recorded_and_its_outcome_reads_back_whole(PlaybackOutcome outcome)
    {
        var handled = new PlaybackHandled(
            Guid.NewGuid(),
            Now,
            TestPlayback.TheaterId,
            "Theater",
            "Arrival",
            new PlayerPath("smb://nas/media/Arrival.mkv"),
            new LocalPath("/media/Arrival.mkv"),
            TestFileHash.For("arrival"),
            new AspectRatio(1.78),
            outcome);

        await TestPlayback.RecordAsync(Store, [handled], CancellationToken);

        var row = await LoadAsync(handled.PlaybackId);
        Assert.Equal(
            (handled.PlaybackId, Now, TestPlayback.TheaterId, "Theater", "Arrival", new PlayerPath("smb://nas/media/Arrival.mkv"), (LocalPath?)new LocalPath("/media/Arrival.mkv"), (FileHash?)TestFileHash.For("arrival"), (AspectRatio?)new AspectRatio(1.78), outcome),
            (row.Id, row.OccurredAt, row.PlayerId, row.PlayerName, row.Title, row.PlayerPath, row.LocalPath, row.VideoFile, row.PlayerAspectRatio, row.Outcome));
        Assert.Empty(row.Deliveries);
    }

    [Fact]
    public async Task A_stream_is_a_document_with_no_local_path_and_no_video_file()
    {
        var handled = TestPlayback.Handled(playerPath: "plugin://plugin.video.youtube/play");

        await TestPlayback.RecordAsync(Store, [handled], CancellationToken);

        var row = await LoadAsync(handled.PlaybackId);
        Assert.Equal(((LocalPath?)null, (FileHash?)null, (PlaybackOutcome)new PlaybackOutcome.Stream()), (row.LocalPath, row.VideoFile, row.Outcome));
    }

    [Fact]
    public async Task Each_finished_delivery_joins_its_playback_in_the_order_it_finished()
    {
        var playback = TestPlayback.Handled();
        var lights = new Delivery(Guid.NewGuid(), TestPlayback.LightsId, "lights", Now, TimeSpan.FromMilliseconds(1234), new DeliveryOutcome.Failed("Connection refused."));
        var automation = new Delivery(Guid.NewGuid(), TestPlayback.AutomationId, "automation", Now, TimeSpan.FromMilliseconds(56), new DeliveryOutcome.Succeeded());

        await TestPlayback.RecordAsync(Store, playback, [lights, automation], CancellationToken);

        var row = await LoadAsync(playback.PlaybackId);
        Assert.Equal([lights, automation], row.Deliveries);
    }

    [Fact]
    public void The_indexed_members_are_worked_out_from_the_playback()
    {
        var row = new PlaybackRow
        {
            PlayerName = "Theater",
            Title = "Heat",
            PlayerPath = new PlayerPath("smb://nas/Media/Heat.mkv"),
            LocalPath = new LocalPath("/Media/Heat.mkv"),
            Outcome = new PlaybackOutcome.Sent(2.39, NotificationAspectRatioSource.Detected, null),
            Deliveries = [TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("Refused."))],
        };
        var stream = new PlaybackRow
        {
            PlayerName = "Theater",
            PlayerPath = new PlayerPath("plugin://Plugin.video/play"),
            Outcome = new PlaybackOutcome.Stream(),
            Deliveries = [TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Cancelled())],
        };

        Assert.Equal(
            (true, true, "theater\nTheater", "heat\nHeat", "/media/heat.mkv\n/Media/Heat.mkv", "heat\n/media/heat.mkv\nsmb://nas/media/heat.mkv"),
            (row.Sent, row.DeliveryFailed, row.PlayerNameKey, row.TitleKey, row.PathKey, row.SearchText));
        Assert.Equal(
            (false, false, (string?)null, "plugin://plugin.video/play\nplugin://Plugin.video/play", "plugin://plugin.video/play"),
            (stream.Sent, stream.DeliveryFailed, stream.TitleKey, stream.PathKey, stream.SearchText));
    }

    [Fact]
    public async Task A_rebuild_replays_every_playback()
    {
        var heat = TestPlayback.Handled(title: "Heat");
        var delivery = TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Succeeded());
        await TestPlayback.RecordAsync(Store, heat, [delivery], CancellationToken);
        await using (var session = Store.LightweightSession())
        {
            var row = await session.LoadAsync<PlaybackRow>(heat.PlaybackId, CancellationToken);
            row!.Title = "Changed";
            row.Deliveries.Clear();
            session.Store(row);
            await session.SaveChangesAsync(CancellationToken);
        }

        await RebuildAsync(PlaybackRowProjection.ReadModel);

        var rebuilt = await LoadAsync(heat.PlaybackId);
        Assert.Equal("Heat", rebuilt.Title);
        Assert.Equal([delivery], rebuilt.Deliveries);
    }

    private async Task<PlaybackRow> LoadAsync(Guid playbackId)
    {
        await using var session = Store.QuerySession();
        return Assert.IsType<PlaybackRow>(await session.LoadAsync<PlaybackRow>(playbackId, CancellationToken));
    }
}
