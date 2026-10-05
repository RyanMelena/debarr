using Fisher;
using Fisher.Projections;
using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Debarr.Playing;

/// <summary>When History was last cleared; the history's read models show only what came after it.</summary>
public sealed class HistoryClear
{
    /// <summary>The history's stream.</summary>
    public Guid Id { get; set; }

    public DateTimeOffset ClearedAt { get; set; }
}

/// <summary>Keeps the time of the newest clear in one document on the history's stream.</summary>
public sealed class HistoryClearProjection : SingleStreamProjection<HistoryClear, Guid>
{
    public const string ReadModel = nameof(HistoryClear);

    public HistoryClearProjection() => Name = ReadModel;

    public static void AddTo(StoreOptions options) => options.Projections.Add(new HistoryClearProjection(), ProjectionLifecycle.Inline);

    public static HistoryClear Create(IEvent<HistoryCleared> cleared) => new() { Id = cleared.StreamId, ClearedAt = cleared.Data.ClearedAt };

    public static void Apply(HistoryCleared cleared, HistoryClear clear) => clear.ClearedAt = cleared.ClearedAt;
}

public static class HistoryClearQuery
{
    /// <summary>When History was last cleared; the earliest time before the first clear.</summary>
    public static async Task<DateTimeOffset> ReadHistoryClearedAtAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        (await session.LoadAsync<HistoryClear>(ClearHistoryHandler.HistoryStreamId, cancellationToken))?.ClearedAt ?? DateTimeOffset.MinValue;
}
