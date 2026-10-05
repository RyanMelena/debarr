using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Debarr.Playing;

/// <summary>The payload Debarr publishes for a playback started event. Declaration order fixes the JSON property order.</summary>
public sealed record Notification
{
    /// <summary>The unique name of the player that started playing.</summary>
    [JsonPropertyName("player")]
    public required string Player { get; init; }

    /// <summary>When Debarr received the playback started event. Serializes as UTC with milliseconds.</summary>
    [JsonPropertyName("occurred_at")]
    [JsonConverter(typeof(UtcTimestampConverter))]
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The snapped aspect ratio.</summary>
    [JsonPropertyName("aspect_ratio")]
    public required double AspectRatio { get; init; }

    [JsonPropertyName("source")]
    public required NotificationAspectRatioSource Source { get; init; }

    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
    }
}
