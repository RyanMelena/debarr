using Debarr.Detecting;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;

namespace Debarr.Tests.Playing;

public sealed class RecordPlaybackTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    private static readonly RecordPlayback Sent = new(
        Guid.NewGuid(),
        Now,
        TestPlayback.TheaterId,
        "Theater",
        "Arrival",
        new PlayerPath("smb://nas/media/Arrival.mkv"),
        new LocalPath("/media/Arrival.mkv"),
        TestFileHash.For("arrival"),
        new AspectRatio(1.78),
        new PlaybackOutcome.Sent(2.39, NotificationAspectRatioSource.Container, new Guid("0199a3c4-5b6e-7000-8000-000000000004")));

    [Fact]
    public void A_handled_playback_starts_its_stream_with_its_outcome()
    {
        var started = RecordPlaybackHandler.Handle(Sent);

        Assert.Equal(Sent.PlaybackId, started.StreamId);
        var playback = Playback.Create(Assert.IsType<PlaybackHandled>(Assert.Single(started.Events)));
        Assert.Equal(("Theater", "Arrival", Sent.Outcome), (playback.PlayerName, playback.Title, playback.Outcome));
        Assert.Equal((Sent.VideoFile, Sent.LocalPath), (playback.VideoFile, playback.LocalPath));
        Assert.Empty(playback.Deliveries);
    }

    [Fact]
    public void Each_finished_delivery_is_added_to_its_playback()
    {
        var playback = StartedPlayback();
        var delivered = new Delivery(Guid.NewGuid(), TestPlayback.AutomationId, "automation", Now, TimeSpan.FromMilliseconds(12), new DeliveryOutcome.Succeeded());
        var failed = new Delivery(Guid.NewGuid(), TestPlayback.LightsId, "lights", Now, TimeSpan.FromSeconds(5), new DeliveryOutcome.Failed("Timed out after 5 s."));

        playback = playback
            .Apply(RecordDeliveryHandler.Handle(new RecordDelivery(Sent.PlaybackId, delivered), playback))
            .Apply(RecordDeliveryHandler.Handle(new RecordDelivery(Sent.PlaybackId, failed), playback));

        Assert.Equal([delivered, failed], playback.Deliveries);
    }

    private static Playback StartedPlayback() =>
        Playback.Create((PlaybackHandled)RecordPlaybackHandler.Handle(Sent).Events.Single());
}
