using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Fisher.Linq;
using Fisher;
using JasperFx.Events.Projections;

namespace Debarr.Tests.EventStore;

public sealed class ProjectionRebuildTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, StaleDocument> StaleDocuments = new()
    {
        [HistoryClearProjection.ReadModel] = StaleDocument.Of(new HistoryClear { Id = Guid.NewGuid(), ClearedAt = Now }),
        [LibraryScanSummaryRowProjection.ReadModel] = StaleDocument.Of(new LibraryScanSummaryRow { Id = Guid.CreateVersion7(), StartedAt = Now }),
        [MediaRowProjection.ReadModel] = StaleDocument.Of(new MediaRow { Id = TestFileHash.For("stale").StreamId, FileHash = TestFileHash.For("stale"), Size = 1, FirstSeenAt = Now }),
        [NotifierDeliveryProjection.ReadModel] = StaleDocument.Of(new NotifierDelivery { Id = Guid.NewGuid(), PlaybackId = Guid.NewGuid(), NotifierId = Guid.NewGuid(), NotifierName = "automation", StartedAt = Now }),
        [PlaybackRowProjection.ReadModel] = StaleDocument.Of(new PlaybackRow { Id = Guid.NewGuid(), OccurredAt = Now, PlayerName = "Theater", PlayerPath = new PlayerPath("/media/stale.mkv"), Outcome = new PlaybackOutcome.Stream() }),
        [StoredFilePathProjection.ReadModel] = StaleDocument.Of(new StoredFilePath { Id = "/media/stale.mkv", VideoFile = TestFileHash.For("stale"), Stat = TestVideoFile.Stat }),
    };

    public static TheoryData<string> ReadModels => [.. StaleDocuments.Keys];

    [Fact]
    public void Every_read_model_has_a_stale_document()
    {
        var readModels = Store.Options.Projections.All.OfType<ProjectionBase>().Select(projection => projection.Name);

        Assert.Equal(readModels.Order(), StaleDocuments.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(ReadModels))]
    public async Task A_rebuild_removes_a_document_the_replay_cannot_recreate(string readModel)
    {
        var stale = StaleDocuments[readModel];
        await using (var session = Store.LightweightSession())
        {
            // Fisher's rebuild clears nothing in a store with no events, so the store holds one that no projection reads.
            await session.Events.AppendAtCurrentVersionAsync(UISettings.StreamId, new UISettingsChanged(UITheme.Dark, DateTimeFormats.Default, false));
            stale.Store(session);
            await session.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal(1, await ReadStoreAsync(session => stale.CountAsync(session, CancellationToken)));

        await RebuildAsync(readModel);

        Assert.Equal(0, await ReadStoreAsync(session => stale.CountAsync(session, CancellationToken)));
    }

    private sealed record StaleDocument(Action<IDocumentSession> Store, Func<IQuerySession, CancellationToken, Task<int>> CountAsync)
    {
        public static StaleDocument Of<T>(T document)
            where T : notnull =>
            new(session => session.Store(document), (session, cancellationToken) => session.Query<T>().CountAsync(cancellationToken));
    }
}
