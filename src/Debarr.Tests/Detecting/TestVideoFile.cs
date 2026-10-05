using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Extensions;
using Fisher.Linq;
using Fisher;
using Weasel.Sqlite.Tables;

namespace Debarr.Tests.Detecting;

/// <summary>Video files, their file paths and their detections for tests, appended to their streams as the app appends them.</summary>
public static class TestVideoFile
{
    /// <summary>A one-byte file's stat, which a file path carries unless a test gives one.</summary>
    public static readonly FileStat Stat = new(1, DateTimeOffset.UnixEpoch);

    /// <summary>Appends <paramref name="events"/> to the video file's stream.</summary>
    public static async Task AppendAsync(IDocumentStore store, FileHash videoFile, IEnumerable<object> events, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        await session.Events.AppendAtCurrentVersionAsync(videoFile.StreamId, events.ToArray());
        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Discovers a video file with a file path at each of <paramref name="paths"/>, as a scan that hashed them at <paramref name="hashedAt"/> would,
    /// with the hash <see cref="TestFileHash.For"/> gives the first path unless one is given.
    /// The events <paramref name="followedBy"/> makes from the hash commit with the discovery, so a file seeded with a result never waits in the detection queue.
    /// </summary>
    public static async Task<FileHash> AddAsync(
        IDocumentStore store,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken,
        FileHash? fileHash = null,
        FileStat? stat = null,
        DateTimeOffset? hashedAt = null,
        Func<FileHash, IEnumerable<object>>? followedBy = null)
    {
        var videoFile = fileHash ?? TestFileHash.For(paths[0]);
        var fileStat = stat ?? Stat;
        var at = hashedAt ?? DateTimeOffset.UtcNow;
        await AppendAsync(
            store,
            videoFile,
            [new VideoFileDiscovered(videoFile, fileStat.Size, at), .. paths.Select(path => new FilePathAdded(videoFile, new LocalPath(path), fileStat, at, at)), .. followedBy?.Invoke(videoFile) ?? []],
            cancellationToken);
        return videoFile;
    }

    /// <inheritdoc cref="AddAsync(IDocumentStore, IReadOnlyList{string}, CancellationToken, FileHash?, FileStat?, DateTimeOffset?, Func{FileHash, IEnumerable{object}}?)"/>
    public static Task<FileHash> AddAsync(
        IDocumentStore store,
        string path,
        CancellationToken cancellationToken,
        FileHash? fileHash = null,
        DateTimeOffset? hashedAt = null,
        Func<FileHash, IEnumerable<object>>? followedBy = null) =>
        AddAsync(store, [path], cancellationToken, fileHash, hashedAt: hashedAt, followedBy: followedBy);

    /// <summary>The video file folded from its stream; null when it has no stream.</summary>
    public static async Task<VideoFile?> ReadVideoFileAsync(IDocumentStore store, FileHash videoFile, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return await session.Events.AggregateStreamAsync<VideoFile>(videoFile.StreamId, token: cancellationToken);
    }

    /// <summary>The video file's detections, folded from its stream, newest first.</summary>
    public static async Task<IReadOnlyList<Detection>> ReadDetectionsAsync(IDocumentStore store, FileHash videoFile, CancellationToken cancellationToken) =>
        [.. ((await ReadVideoFileAsync(store, videoFile, cancellationToken))?.Detections ?? []).OrderByDescending(detection => detection.StartedAt)];

    /// <summary>The path's stored file path; null when the path has none.</summary>
    public static async Task<StoredFilePath?> ReadStoredFilePathAsync(IDocumentStore store, string path, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return await session.LoadAsync<StoredFilePath>(path, cancellationToken);
    }

    /// <summary>Every stored file path, in path order.</summary>
    public static async Task<IReadOnlyList<StoredFilePath>> ReadStoredFilePathsAsync(IDocumentStore store, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return [.. (await session.Query<StoredFilePath>().ToListAsync(cancellationToken)).OrderBy(row => row.Id, StringComparer.Ordinal)];
    }

    /// <summary>The video file's Media row; null when it has none.</summary>
    public static async Task<MediaRow?> ReadMediaRowAsync(IDocumentStore store, FileHash videoFile, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return await session.LoadAsync<MediaRow>(videoFile.StreamId, cancellationToken);
    }

    /// <summary>Every video file's Media row.</summary>
    public static async Task<IReadOnlyList<MediaRow>> ReadMediaRowsAsync(IDocumentStore store, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return await session.Query<MediaRow>().ToListAsync(cancellationToken);
    }

    /// <summary>The table the store keeps the Media rows in, as its document mapping names it.</summary>
    public static string MediaRowTableName(IDocumentStore store) =>
        ((DocumentStore)store).Database.BuildFeatureSchemas()
            .Single(schema => schema.StorageType == typeof(MediaRow))
            .Objects.OfType<Table>()
            .Single()
            .Identifier.Name;

    /// <summary>A queued detection with a result, read from <paramref name="path"/>.</summary>
    public static Detection Detected(
        double rawAspectRatio,
        AspectRatioSource source = AspectRatioSource.Detected,
        string? path = "/media/film.mkv",
        double? containerAspectRatio = null,
        DateTimeOffset? startedAt = null,
        int detectorVersion = 1,
        double confidence = 0.9,
        IReadOnlyList<CropSample>? samples = null,
        DetectionOrigin origin = DetectionOrigin.Queue,
        string? ffmpegVersion = null) =>
        new(
            Guid.CreateVersion7(),
            origin,
            path is null ? null : new LocalPath(path),
            startedAt ?? DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(1),
            detectorVersion,
            ffmpegVersion,
            containerAspectRatio is { } ratio ? new ContainerMetadata(new AspectRatio(ratio), 1920, 1080, "h264", null) : null,
            new DetectionOutcome.Succeeded(new DetectionResult(source, new AspectRatio(rawAspectRatio), confidence, samples ?? [])));

    /// <summary>A queued detection that failed with <paramref name="error"/>, read from <paramref name="path"/>.</summary>
    public static Detection Failed(string error, string? path = "/media/film.mkv", DateTimeOffset? startedAt = null) =>
        new(
            Guid.CreateVersion7(),
            DetectionOrigin.Queue,
            path is null ? null : new LocalPath(path),
            startedAt ?? DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(1),
            1,
            null,
            null,
            new DetectionOutcome.Failed(error));
}
