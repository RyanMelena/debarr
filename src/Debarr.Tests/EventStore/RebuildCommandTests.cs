using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace Debarr.Tests.EventStore;

/// <summary>Rebuilds the read models of a store whose documents disagree with its events.</summary>
public sealed class RebuildCommandTests : IAsyncDisposable
{
    private const string MediaRowTable = "fi_doc_mediarow";
    private const string StoredFilePathTable = StoredFilePathQuery.TableName;

    private static readonly string[] ReadModels = ["HistoryClear", "LibraryScanSummaryRow", "MediaRow", "NotifierDelivery", "PlaybackRow", "StoredFilePath"];

    private readonly DebarrWebApplicationFactory _factory = new();
    private readonly FakeLogger _logger = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private IDocumentStore Store => _factory.Services.GetRequiredService<IDocumentStore>();

    [Fact]
    public async Task Rebuilding_every_read_model_makes_each_document_what_the_events_produce()
    {
        var replayed = await SeedWithWrongDocumentsAsync();

        Assert.True(await RebuildCommand.RebuildAsync(Store, _logger, null, CancellationToken));

        Assert.Equal(replayed.MediaRows, await ReadDocumentsAsync(MediaRowTable));
        Assert.Equal(replayed.StoredFilePaths, await ReadDocumentsAsync(StoredFilePathTable));
        var log = _logger.Collector.GetSnapshot().Select(record => record.Message).ToList();
        Assert.All(ReadModels, readModel => Assert.Contains(log, message => message.StartsWith($"Rebuilt {readModel} in ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Rebuilding_one_read_model_leaves_the_others_as_they_were()
    {
        var replayed = await SeedWithWrongDocumentsAsync();
        var storedFilePaths = await ReadDocumentsAsync(StoredFilePathTable);

        Assert.True(await RebuildCommand.RebuildAsync(Store, _logger, MediaRowProjection.ReadModel, CancellationToken));

        Assert.Equal(replayed.MediaRows, await ReadDocumentsAsync(MediaRowTable));
        Assert.NotEqual(replayed.StoredFilePaths, storedFilePaths);
        Assert.Equal(storedFilePaths, await ReadDocumentsAsync(StoredFilePathTable));
    }

    [Fact]
    public async Task An_unknown_read_model_fails_naming_the_read_models_and_leaves_the_documents_as_they_were()
    {
        await SeedWithWrongDocumentsAsync();
        var mediaRows = await ReadDocumentsAsync(MediaRowTable);
        var storedFilePaths = await ReadDocumentsAsync(StoredFilePathTable);

        Assert.False(await RebuildCommand.RebuildAsync(Store, _logger, "Nope", CancellationToken));

        Assert.Equal($"There is no read model named Nope. The read models are {string.Join(", ", ReadModels)}.", _logger.LatestRecord.Message);
        Assert.Equal(mediaRows, await ReadDocumentsAsync(MediaRowTable));
        Assert.Equal(storedFilePaths, await ReadDocumentsAsync(StoredFilePathTable));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>
    /// Appends three detected video files, then makes their documents wrong:
    /// a Media row and a stored file path for a file with no events, and one file's Media row and stored file path missing.
    /// </summary>
    /// <returns>The documents the projections wrote from the events, before they were made wrong.</returns>
    private async Task<(IReadOnlyList<(string, string)> MediaRows, IReadOnlyList<(string, string)> StoredFilePaths)> SeedWithWrongDocumentsAsync()
    {
        List<FileHash> videoFiles = [];
        foreach (var path in new[] { "/media/a.mkv", "/media/b.mkv", "/media/c.mkv" })
        {
            videoFiles.Add(await TestVideoFile.AddAsync(Store, path, CancellationToken, followedBy: videoFile => [new AspectRatioDetected(videoFile, TestVideoFile.Detected(2.39))]));
        }

        var replayed = (await ReadDocumentsAsync(MediaRowTable), await ReadDocumentsAsync(StoredFilePathTable));

        var stale = TestFileHash.For("stale");
        await using var session = Store.LightweightSession();
        session.Store(new MediaRow { Id = stale.StreamId, FileHash = stale, Size = 1, FirstSeenAt = DateTimeOffset.UnixEpoch });
        session.Store(new StoredFilePath { Id = "/media/stale.mkv", VideoFile = stale, Stat = TestVideoFile.Stat });
        session.Delete<MediaRow>(videoFiles[1].StreamId);
        session.Delete<StoredFilePath>("/media/b.mkv");
        await session.SaveChangesAsync(CancellationToken);

        return replayed;
    }

    /// <summary>Every document in the table as its id and JSON, in id order, as stored.</summary>
    private async Task<IReadOnlyList<(string, string)>> ReadDocumentsAsync(string table)
    {
        var connectionString = new SqliteConnectionStringBuilder(_factory.UnpooledConnectionString) { Mode = SqliteOpenMode.ReadOnly };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync(CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select id, data from {table} order by id";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        List<(string, string)> documents = [];
        while (await reader.ReadAsync(CancellationToken))
        {
            documents.Add((reader.GetString(0), reader.GetString(1)));
        }

        return documents;
    }
}
