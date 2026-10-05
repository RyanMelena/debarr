using Wolverine.Persistence.EventSourcing;

namespace Debarr.Playing;

/// <summary>Clears History of every playback so far, and keeps their events.</summary>
public sealed record ClearHistory
{
    /// <summary>The history's stream, which Wolverine loads the history clear from.</summary>
    public Guid HistoryClearId => ClearHistoryHandler.HistoryStreamId;
}

public static class ClearHistoryHandler
{
    /// <summary>The one stream every clear of the history is appended to.</summary>
    public static readonly Guid HistoryStreamId = new("5f1c3e2a-7d4b-4c8e-9a61-2b0d8e4f7c13");

    public static HistoryCleared Handle(ClearHistory command, [WriteModel(Required = false)] HistoryClear? historyClear, TimeProvider timeProvider) =>
        new(timeProvider.GetUtcNow());
}
