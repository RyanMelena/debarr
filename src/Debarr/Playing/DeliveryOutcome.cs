using System.Text.Json.Serialization;

namespace Debarr.Playing;

/// <summary>How a delivery attempt ended.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Succeeded), "succeeded")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(Cancelled), "cancelled")]
public abstract record DeliveryOutcome
{
    private DeliveryOutcome()
    {
    }

    /// <summary>The notifier accepted the notification.</summary>
    public sealed record Succeeded : DeliveryOutcome;

    /// <summary>The notifier refused it, the transport failed, or the attempt timed out.</summary>
    /// <param name="Error">Why the attempt failed.</param>
    public sealed record Failed(string Error) : DeliveryOutcome;

    /// <summary>A newer playback on the same player cancelled the attempt.</summary>
    public sealed record Cancelled : DeliveryOutcome;
}
