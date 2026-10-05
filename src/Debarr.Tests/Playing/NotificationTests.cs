using System.Text;
using System.Text.Json;
using Debarr.Playing;

namespace Debarr.Tests.Playing;

public sealed class NotificationTests
{
    public static readonly Notification Sample = new()
    {
        Player = "theater",
        OccurredAt = new DateTimeOffset(2026, 9, 14, 19, 4, 11, 482, TimeSpan.Zero),
        AspectRatio = 2.39,
        Source = NotificationAspectRatioSource.Detected,
    };

    public static string ReadSnapshot() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Playing", "Snapshots", "notification.json"));

    [Fact]
    public void A_notification_serializes_to_the_snapshot_bytes()
    {
        Assert.Equal(Encoding.UTF8.GetBytes(ReadSnapshot()), JsonSerializer.SerializeToUtf8Bytes(Sample));
    }

    [Fact]
    public void A_whole_second_writes_three_zero_fractional_digits()
    {
        var notification = Sample with { OccurredAt = new DateTimeOffset(2026, 9, 14, 19, 4, 11, TimeSpan.Zero) };

        Assert.Contains("\"occurred_at\":\"2026-09-14T19:04:11.000Z\"", JsonSerializer.Serialize(notification));
    }

    [Fact]
    public void A_time_with_an_offset_writes_its_utc_time()
    {
        var notification = Sample with { OccurredAt = new DateTimeOffset(2026, 9, 14, 21, 4, 11, 482, TimeSpan.FromHours(2)) };

        Assert.Contains("\"occurred_at\":\"2026-09-14T19:04:11.482Z\"", JsonSerializer.Serialize(notification));
    }

    [Theory]
    [InlineData("Manual", "\"source\":\"manual\"")]
    [InlineData("Detected", "\"source\":\"detected\"")]
    [InlineData("Container", "\"source\":\"container\"")]
    [InlineData("Player", "\"source\":\"player\"")]
    public void Each_source_writes_its_lowercase_name(string source, string expected)
    {
        var notification = Sample with { Source = Enum.Parse<NotificationAspectRatioSource>(source) };

        Assert.Contains(expected, JsonSerializer.Serialize(notification));
    }
}
