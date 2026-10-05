using System.Text.Json;
using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Debarr.Tests.Playing;

using Microsoft.Data.Sqlite;

namespace Debarr.Tests.EventStore;

public sealed class EventSerializationTests : AppTestContext
{
    [Fact]
    public async Task An_event_stores_an_enum_as_its_name()
    {
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(UISettings.StreamId, new UISettingsChanged(UITheme.Dark, DateTimeFormats.Default, true));
            await session.SaveChangesAsync(CancellationToken);
        }

        await using var query = Store.QuerySession();
        var data = await query.AdvancedSql.QueryAsync<string>("select data from fi_events", CancellationToken);
        Assert.Contains(data, json => json.Contains("\"theme\":\"dark\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stored_event_that_lacks_a_member_fails_to_read_rather_than_reading_a_default()
    {
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(UISettings.StreamId, new UISettingsChanged(UITheme.Dark, DateTimeFormats.Default, true));
            await session.SaveChangesAsync(CancellationToken);
        }

        await using (var connection = new SqliteConnection(UnpooledConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var update = connection.CreateCommand();
            update.CommandText = "update fi_events set data = json_remove(data, '$.showRelativeDates')";
            Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken));
        }

        await using var query = Store.QuerySession();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => query.Events.FetchStreamAsync(UISettings.StreamId, token: CancellationToken));
        Assert.Contains("showRelativeDates", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_standard_ratios_change_reads_back_with_its_ratios_and_recheck_scope()
    {
        var widened = StandardRatios.Create(
            [.. StandardRatios.Default.Ratios.Where(standardRatio => standardRatio.AspectRatio != 2.2), new StandardRatio(2.76, true)],
            0.05).Value;
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(DetectionSettings.StreamId, new StandardRatiosChanged(widened, new RecheckScope.Some([1.78, 2.76])));
            await session.SaveChangesAsync(CancellationToken);
        }

        await using var query = Store.QuerySession();
        var read = (StandardRatiosChanged)Assert.Single(await query.Events.FetchStreamAsync(DetectionSettings.StreamId, token: CancellationToken)).Data;

        Assert.Equal([1.78, 2.76], Assert.IsType<RecheckScope.Some>(read.Recheck).AspectRatios);
        Assert.Equal(widened.MatchTolerance, read.StandardRatios.MatchTolerance);
        Assert.Equal(widened.Ratios, read.StandardRatios.Ratios);
    }

    [Fact]
    public async Task A_playback_reads_back_with_its_file_hash_and_player_ratio()
    {
        var handled = TestPlayback.Handled(localPath: "/media/film.mkv", videoFile: TestFileHash.For("film")) with { PlayerAspectRatio = new AspectRatio(1.78) };
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(handled.PlaybackId, handled);
            await session.SaveChangesAsync(CancellationToken);
        }

        await using var query = Store.QuerySession();
        var read = (PlaybackHandled)Assert.Single(await query.Events.FetchStreamAsync(handled.PlaybackId, token: CancellationToken)).Data;

        Assert.Equal((handled.VideoFile, handled.PlayerAspectRatio), (read.VideoFile, read.PlayerAspectRatio));
    }

    [Fact]
    public async Task A_finished_delivery_reads_back_with_its_outcome_and_a_failures_error()
    {
        var handled = TestPlayback.Handled();
        List<Delivery> deliveries =
        [
            TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Succeeded()),
            TestPlayback.Delivery(TestPlayback.LightsId, "lights", new DeliveryOutcome.Failed("Connection refused.")),
            TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Cancelled()),
        ];
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(handled.PlaybackId, [handled, .. deliveries.Select(delivery => new DeliveryFinished(handled.PlaybackId, delivery))]);
            await session.SaveChangesAsync(CancellationToken);
        }

        await using var query = Store.QuerySession();
        var events = await query.Events.FetchStreamAsync(handled.PlaybackId, token: CancellationToken);

        Assert.Equal(deliveries, events.Skip(1).Select(stored => ((DeliveryFinished)stored.Data).Delivery));
        var data = await query.AdvancedSql.QueryAsync<string>("select data from fi_events", CancellationToken);
        Assert.Contains(data, json => json.Contains("\"outcome\":{\"$type\":\"failed\",\"error\":\"Connection refused.\"}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_notifiers_read_back_with_each_settings_type_and_value()
    {
        var mqtt = MqttSettings.Create("mqtts://broker.lan", "debarr", "user", "secret", "lights/{player}", QualityOfService.ExactlyOnce).Value;
        var webhook = WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Put, [new WebhookHeader("Authorization", "Bearer abc")]).Value;
        var brokerId = Guid.NewGuid();
        var automationId = Guid.NewGuid();
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Notifiers.StreamId, new NotifierAdded(brokerId, "broker", false, mqtt), new NotifierAdded(automationId, "automation", true, webhook));
            await session.SaveChangesAsync(CancellationToken);
        }

        await using var query = Store.QuerySession();
        var notifiers = await Notifiers.ReadAsync(query, CancellationToken);

        Assert.Equal(new Notifier(brokerId, "broker", false, mqtt), notifiers.Find(brokerId));
        var stored = Assert.IsType<WebhookSettings>(notifiers.Find(automationId)!.Settings);
        Assert.Equal((webhook.Url, webhook.Method), (stored.Url, stored.Method));
        Assert.Equal(webhook.Headers, stored.Headers);
        var data = await query.AdvancedSql.QueryAsync<string>("select data from fi_events where type = 'notifier_added'", CancellationToken);
        Assert.Contains("\"broker\":{\"host\":\"broker.lan\",\"port\":8883,\"tls\":true}", data[0], StringComparison.Ordinal);
        Assert.Contains("\"qos\":\"exactlyOnce\"", data[0], StringComparison.Ordinal);
        Assert.Contains("\"url\":\"http://automation.lan/debarr\",\"method\":\"put\"", data[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_video_files_events_read_back_with_their_hashes_stats_ratios_and_samples()
    {
        var videoFile = TestFileHash.For("film");
        var detection = TestVideoFile.Detected(2.39, containerAspectRatio: 1.78, samples: [new CropSample(TimeSpan.FromSeconds(30), new CropBox(1920, 800))]);
        var discovered = new VideoFileDiscovered(videoFile, 42, DateTimeOffset.UnixEpoch);
        var added = new FilePathAdded(
            videoFile,
            new LocalPath("/movies/Film.mkv"),
            new FileStat(42, new DateTimeOffset(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero)),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var saved = new OverrideSaved(new Override(new AspectRatio(2.2), false, "Note", DateTimeOffset.UnixEpoch));
        await TestVideoFile.AppendAsync(Store, videoFile, [discovered, added, new AspectRatioDetected(videoFile, detection), saved], CancellationToken);

        await using var session = Store.QuerySession();
        var events = (await session.Events.FetchStreamAsync(videoFile.StreamId, token: CancellationToken)).Select(stored => stored.Data).ToList();

        Assert.Equal(discovered, events[0]);
        Assert.Equal(added, events[1]);
        var detected = (AspectRatioDetected)events[2];
        Assert.Equal((videoFile, detection.Id, 2.39, 1.78), (detected.VideoFile, detected.Detection.Id, detected.Detection.Result!.RawAspectRatio.Value, detected.Detection.ContainerMetadata!.ContainerAspectRatio.Value));
        Assert.Equal(detection.Result!.Samples, detected.Detection.Result!.Samples);
        Assert.Equal(saved, events[3]);
    }

    [Fact]
    public async Task A_stored_detection_holds_its_outcome_and_no_result_or_error_beside_it()
    {
        var videoFile = TestFileHash.For("film");
        var detected = TestVideoFile.Detected(2.39, containerAspectRatio: 1.78, ffmpegVersion: "7.1");
        var failed = TestVideoFile.Failed("no picture") with { FfmpegVersion = detected.FfmpegVersion, ContainerMetadata = detected.ContainerMetadata };
        await TestVideoFile.AppendAsync(Store, videoFile, [new AspectRatioDetected(videoFile, detected), new DetectionFailed(videoFile, failed)], CancellationToken);

        await using var query = Store.QuerySession();
        var data = await query.AdvancedSql.QueryAsync<string>("select data from fi_events", CancellationToken);

        string[] members = ["id", "origin", "path", "startedAt", "duration", "detectorVersion", "ffmpegVersion", "containerMetadata", "outcome"];
        Assert.All(data, json => Assert.Equal(members, JsonDocument.Parse(json).RootElement.GetProperty("detection").EnumerateObject().Select(member => member.Name)));
    }
}
