using FluentResults;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Playing;

/// <summary>Records how one attempt to send a playback's notification through one notifier ended.</summary>
public sealed record RecordDelivery(Guid PlaybackId, Delivery Delivery);

public static class RecordDeliveryHandler
{
    public static Result Validate(RecordDelivery command, Playback? playback) =>
        playback is null ? Result.Fail("The playback no longer exists.") : Result.Ok();

    public static DeliveryFinished Handle(RecordDelivery command, [WriteModel(Required = false)] Playback? playback) =>
        new(command.PlaybackId, command.Delivery);
}
