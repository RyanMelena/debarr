using System.Text.Json.Serialization;

namespace Debarr.Playing;

/// <summary>Where a notification's aspect ratio came from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationAspectRatioSource>))]
public enum NotificationAspectRatioSource
{
    /// <summary>The video file's override.</summary>
    [JsonStringEnumMemberName("manual")]
    Manual,

    /// <summary>The video file's detection result.</summary>
    [JsonStringEnumMemberName("detected")]
    Detected,

    /// <summary>The ratio the container reports.</summary>
    [JsonStringEnumMemberName("container")]
    Container,

    /// <summary>The ratio the player reports.</summary>
    [JsonStringEnumMemberName("player")]
    Player,
}
