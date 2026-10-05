using System.Text.Json.Serialization;
using Debarr.EventStore;
using Debarr.Extensions;
using Fisher;
using Fisher.Linq;
using Fisher.Projections;
using Fisher.Storage.FullText;
using JasperFx.Events.Projections;

namespace Debarr.Detecting;

/// <summary>
/// The members an index or a query names are worked out from its other members and written into the document beside them.
/// Two rows are equal when they hold the same video file, so a table that keys its rows by item keeps each row's element when its values change.
/// </summary>
public sealed class MediaRow : IEquatable<MediaRow>
{
    /// <summary>The video file's stream.</summary>
    public Guid Id { get; set; }

    public FileHash FileHash { get; set; }

    /// <summary>The version of the video file's stream the row was folded at, which an append decided from the row states.</summary>
    public long Version { get; set; }

    public long Size { get; set; }

    /// <summary>When a scan first hashed the file, which orders the detection queue and Media's First Seen column.</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Most recently hashed first, then by path.</summary>
    public List<FilePath> FilePaths { get; set; } = [];

    /// <summary>Null until a detection succeeds.</summary>
    public MediaRowResult? CurrentResult { get; set; }

    /// <summary>The last failed detection since the current result; null when there is none.</summary>
    public MediaRowFailure? LastFailure { get; set; }

    /// <summary>Null until the first save.</summary>
    public Override? Override { get; set; }

    /// <summary>When a file path last left the video file; null while none has.</summary>
    public DateTimeOffset? LastPathRemovedAt { get; set; }

    [JsonIgnore]
    public VideoFileStatus Status => VideoFile.StatusOf(Override, CurrentResult?.Source, LastFailure is not null);

    /// <summary>The status as its enum value, which Fisher groups and orders by.</summary>
    public int StatusOrder => (int)Status;

    /// <summary>Whether Media lists the row.</summary>
    public bool HasFilePath => FilePaths.Count > 0;

    /// <summary>Pending with a file path, the detection queue's predicate.</summary>
    public bool InDetectionQueue => HasFilePath && CurrentResult is null && LastFailure is null;

    /// <summary>The smallest of the paths' keys, which Media sorts by and breaks every other sort's ties on; null with no file path.</summary>
    public string? PathKey => FilePaths.Count == 0 ? null : FilePaths.Select(filePath => filePath.Path.Value.ToCaseInsensitiveThenOrdinalSortKey()).Min(StringComparer.Ordinal);

    /// <summary>What Media's ratio column sorts by: nothing with Don't Send, otherwise the override's ratio, otherwise the current result's raw ratio.</summary>
    public double? SortAspectRatio => Override switch
    {
        { DontSend: true } => null,
        { AspectRatio: { } aspectRatio } => aspectRatio.Value,
        _ => CurrentResult?.RawAspectRatio.Value,
    };

    public double? Confidence => CurrentResult?.Confidence;

    public int? CurrentResultDetectorVersion => CurrentResult?.DetectorVersion;

    /// <summary>Every path lowercased, one per line, which the trigram index covers.</summary>
    public string PathsText => string.Join('\n', FilePaths.Select(filePath => filePath.Path.Value.ToLowerInvariant()));

    /// <summary>The file paths in Media's order, the one the row sorts by first.</summary>
    [JsonIgnore]
    public IEnumerable<FilePath> FilePathsByPath => FilePaths.OrderBy(filePath => filePath.Path.Value.ToCaseInsensitiveThenOrdinalSortKey(), StringComparer.Ordinal);

    public bool Equals(MediaRow? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as MediaRow);

    public override int GetHashCode() => Id.GetHashCode();
}

/// <param name="ContainerMetadata">Null when ffprobe read none.</param>
public sealed record MediaRowResult(
    AspectRatioSource Source,
    AspectRatio RawAspectRatio,
    double Confidence,
    ContainerMetadata? ContainerMetadata,
    int DetectorVersion,
    string? FfmpegVersion)
{
    /// <summary>The detection's result; null for a failure.</summary>
    public static MediaRowResult? From(Detection detection) => detection.Result is { } result
        ? new(result.AspectRatioSource, result.RawAspectRatio, result.Confidence, detection.ContainerMetadata, detection.DetectorVersion, detection.FfmpegVersion)
        : null;
}

/// <summary>The last failure's error.</summary>
public sealed record MediaRowFailure(string Error)
{
    public static MediaRowFailure From(Detection detection) => new(detection.Error ?? "");
}

/// <summary>A column Media sorts by.</summary>
public enum MediaRowSort
{
    Path,
    AspectRatio,
    Confidence,
    Status,
    FirstSeen,
}

/// <summary>What Media shows: a status, or every status when null; a search, or every row when blank; a sort and its direction.</summary>
public sealed record MediaRowView(VideoFileStatus? Status, string? Search, MediaRowSort Sort, bool Descending);

/// <summary>Folds each video file's stream into its Media row: its discovery, its file paths, its detections and its override.</summary>
public sealed class MediaRowProjection : SingleStreamProjection<MediaRow, Guid>
{
    public const string ReadModel = nameof(MediaRow);

    public const string DetectionQueueIndex = "ix_media_row_detection_queue";

    public const string DetectorVersionIndex = "ix_media_row_detector_version";

    public MediaRowProjection() => Name = ReadModel;

    /// <summary>
    /// Registers the projection inline, with an index for each of Media's sorts, alone and under the status filter,
    /// the detection queue's partial index, the detector version's index, and the trigram search.
    /// </summary>
    public static void AddTo(StoreOptions options)
    {
        // Every Media query tests HasFilePath, so every Media index starts with it and ends with PathKey, the tie-break.
        options.Schema.For<MediaRow>()
            .Index([row => row.HasFilePath, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.StatusOrder, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.SortAspectRatio, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.Confidence, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.FirstSeenAt, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.StatusOrder, row => row.SortAspectRatio, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.StatusOrder, row => row.Confidence, row => row.PathKey])
            .Index([row => row.HasFilePath, row => row.StatusOrder, row => row.FirstSeenAt, row => row.PathKey])
            .Index(row => row.FirstSeenAt, name: DetectionQueueIndex, predicate: row => row.InDetectionQueue)
            .Index(row => row.CurrentResultDetectorVersion, name: DetectorVersionIndex)
            .FullTextIndex(FullTextTokenizer.Trigram, row => row.PathsText);
        options.Projections.Add(new MediaRowProjection(), ProjectionLifecycle.Inline);
    }

    public static MediaRow Create(VideoFileDiscovered discovered) => new()
    {
        Id = discovered.FileHash.StreamId,
        FileHash = discovered.FileHash,
        Size = discovered.Size,
        FirstSeenAt = discovered.FirstSeenAt,
    };

    public static void Apply(FilePathAdded added, MediaRow row) => row.FilePaths = VideoFile.WithFilePath(row.FilePaths, added);

    /// <summary>Removes the file path and records when the video file last lost one.</summary>
    public static void Apply(FilePathRemoved removed, MediaRow row)
    {
        row.FilePaths = VideoFile.WithoutFilePath(row.FilePaths, removed.Path);
        row.LastPathRemovedAt = removed.RemovedAt;
    }

    /// <summary>The result becomes the current result and clears the last failure.</summary>
    public static void Apply(AspectRatioDetected detected, MediaRow row) => (row.CurrentResult, row.LastFailure) = (MediaRowResult.From(detected.Detection), null);

    public static void Apply(DetectionResultConverted converted, MediaRow row) => (row.CurrentResult, row.LastFailure) = (MediaRowResult.From(converted.Detection), null);

    /// <summary>The failure becomes the last failure beside the current result.</summary>
    public static void Apply(DetectionFailed failed, MediaRow row) => row.LastFailure = MediaRowFailure.From(failed.Detection);

    public static void Apply(DetectionResultCleared cleared, MediaRow row) => (row.CurrentResult, row.LastFailure) = (null, null);

    public static void Apply(OverrideSaved saved, MediaRow row) => row.Override = saved.Override;

    /// <summary>An archived video file leaves Media, the detection queue and every page that reads its row.</summary>
    public static bool ShouldDelete(VideoFileArchived archived) => true;

    /// <summary>The row of a restored video file, with the state it was archived with; its file path follows.</summary>
    public static MediaRow Create(VideoFileRestored restored) => new()
    {
        Id = restored.FileHash.StreamId,
        FileHash = restored.FileHash,
        Size = restored.Size,
        FirstSeenAt = restored.FirstSeenAt,
        CurrentResult = restored.CurrentResult is { } currentResult ? MediaRowResult.From(currentResult) : null,
        LastFailure = restored.LastFailure is { } lastFailure ? MediaRowFailure.From(lastFailure) : null,
        Override = restored.Override,
    };
}

public static class MediaRowQuery
{
    /// <summary>The shortest search the trigram index takes; a shorter one scans the paths.</summary>
    public const int TrigramLength = 3;

    /// <summary>How many ids one read loads, which stays under SQLite's parameter limit.</summary>
    private const int LoadBatchSize = 500;

    /// <summary>Whether Media lists any video file.</summary>
    public static Task<bool> AnyListedMediaRowAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        session.Query<MediaRow>().AnyAsync(row => row.HasFilePath, cancellationToken);

    /// <summary>Whether the library holds any video file, with a file path or without one.</summary>
    public static Task<bool> AnyMediaRowAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        session.Query<MediaRow>().AnyAsync(cancellationToken);

    /// <summary>The number of listed video files with each status among those the search matches; a status with none is absent.</summary>
    public static async Task<Dictionary<VideoFileStatus, int>> CountMediaRowsByStatusAsync(this IQuerySession session, string? search, CancellationToken cancellationToken)
    {
        var counts = await session.Query<MediaRow>()
            .Listed(search)
            .GroupBy(row => row.StatusOrder)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(count => (VideoFileStatus)count.Status, count => count.Count);
    }

    /// <summary>The rows the view matches, counted, and the page of them in its order.</summary>
    /// <param name="page">Counted from 0.</param>
    public static async Task<(int TotalCount, IReadOnlyList<MediaRow> Rows)> ReadMediaPageAsync(
        this IQuerySession session,
        MediaRowView view,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = session.Query<MediaRow>();
        return (
            await rows.Matching(view).CountAsync(cancellationToken),
            await rows.MediaPage(view).Skip(page * pageSize).Take(pageSize).ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Requests for the newest video files in the detection queue, up to <paramref name="count"/>, leaving out <paramref name="running"/>,
    /// each with its most recently hashed path.
    /// </summary>
    public static async Task<IReadOnlyList<DetectionRequest>> ReadDetectionQueueAsync(
        this IQuerySession session,
        int count,
        IReadOnlyCollection<FileHash> running,
        CancellationToken cancellationToken)
    {
        var queued = await session.Query<MediaRow>()
            .DetectionQueue([.. running.Select(fileHash => fileHash.StreamId)])
            .Take(count)
            .ToListAsync(cancellationToken);
        return [.. queued.Select(row => VideoFile.DetectionRequestFor(row.FileHash, row.FilePaths, DetectionOrigin.Queue)!)];
    }

    /// <summary>Each video file whose current result holds the container metadata ffprobe read, for a standard ratios change to re-check.</summary>
    public static Task<IReadOnlyList<MediaRow>> ReadWithContainerMetadataAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        session.Query<MediaRow>().Where(row => row.CurrentResult!.ContainerMetadata != null).ToListAsync(cancellationToken);

    /// <summary>The streams of the video files with a current result or a last failure, which Re-detect All clears, at the versions their rows were folded at.</summary>
    public static Task<IReadOnlyList<StreamVersion>> ReadVersionsWithResultOrFailureAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        session.Query<MediaRow>()
            .Where(row => row.CurrentResult != null || row.LastFailure != null)
            .Select(row => new StreamVersion(row.Id, row.Version))
            .ToListAsync(cancellationToken);

    /// <summary>How many current results a detector older than <paramref name="detectorVersion"/> made.</summary>
    public static Task<int> CountResultsBeforeAsync(this IQuerySession session, int detectorVersion, CancellationToken cancellationToken) =>
        session.Query<MediaRow>().ResultsBefore(detectorVersion).CountAsync(cancellationToken);

    /// <summary>The video files with no file path whose last path left before <paramref name="lastPathRemovedBefore"/>, which a library scan archives.</summary>
    public static async Task<IReadOnlyList<FileHash>> ReadArchiveCandidatesAsync(this IQuerySession session, DateTimeOffset lastPathRemovedBefore, CancellationToken cancellationToken)
    {
        var candidates = await session.Query<MediaRow>()
            .Where(row => !row.HasFilePath && row.LastPathRemovedAt < lastPathRemovedBefore)
            .ToListAsync(cancellationToken);
        return [.. candidates.Select(row => row.FileHash)];
    }

    /// <summary>The Media rows of the video files among <paramref name="fileHashes"/> that have one.</summary>
    public static async Task<IReadOnlyList<MediaRow>> LoadMediaRowsAsync(this IQuerySession session, IEnumerable<FileHash> fileHashes, CancellationToken cancellationToken)
    {
        List<MediaRow> rows = [];
        foreach (var batch in fileHashes.Distinct().Select(fileHash => fileHash.StreamId).Chunk(LoadBatchSize))
        {
            rows.AddRange(await session.LoadManyAsync<MediaRow>(cancellationToken, batch));
        }

        return rows;
    }

    /// <summary>The rows Media lists that the search matches; every listed row when it is blank.</summary>
    public static IQueryable<MediaRow> Listed(this IQueryable<MediaRow> rows, string? search)
    {
        var listed = rows.Where(row => row.HasFilePath);
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return listed;
        }

        if (term.Length >= TrigramLength)
        {
            return listed.Where(row => row.NgramSearch(term));
        }

        var lowercased = term.ToLowerInvariant();
        return listed.Where(row => row.PathsText.Contains(lowercased));
    }

    /// <summary>The rows the view's status and search match.</summary>
    public static IQueryable<MediaRow> Matching(this IQueryable<MediaRow> rows, MediaRowView view)
    {
        var listed = rows.Listed(view.Search);
        if (view.Status is not { } status)
        {
            return listed;
        }

        var statusOrder = (int)status;
        return listed.Where(row => row.StatusOrder == statusOrder);
    }

    /// <summary>
    /// The view's rows in its order, each sort's ties broken by the path key in the same direction.
    /// Under a status filter, the status sort is the path key's order.
    /// </summary>
    public static IQueryable<MediaRow> MediaPage(this IQueryable<MediaRow> rows, MediaRowView view)
    {
        var matching = rows.Matching(view);
        var descending = view.Descending;
        return view.Sort switch
        {
            MediaRowSort.AspectRatio => matching.OrderBy(row => row.SortAspectRatio, descending).ThenBy(row => row.PathKey, descending),
            MediaRowSort.Confidence => matching.OrderBy(row => row.Confidence, descending).ThenBy(row => row.PathKey, descending),
            MediaRowSort.Status when view.Status is null => matching.OrderBy(row => row.StatusOrder, descending).ThenBy(row => row.PathKey, descending),
            MediaRowSort.FirstSeen => matching.OrderBy(row => row.FirstSeenAt, descending).ThenBy(row => row.PathKey, descending),
            _ => matching.OrderBy(row => row.PathKey, descending),
        };
    }

    /// <summary>The detection queue, newest first, through its partial index, leaving out the video files whose streams are <paramref name="running"/>.</summary>
    /// <remarks>The running video files are matched by stream id, a Guid Fisher sends as a query parameter, and SQLite still reads the partial index alone.</remarks>
    public static IQueryable<MediaRow> DetectionQueue(this IQueryable<MediaRow> rows, IReadOnlyCollection<Guid> running) =>
        rows.Where(row => row.InDetectionQueue && !running.Contains(row.Id)).OrderByDescending(row => row.FirstSeenAt);

    /// <summary>The video files whose current result a detector older than <paramref name="detectorVersion"/> made.</summary>
    public static IQueryable<MediaRow> ResultsBefore(this IQueryable<MediaRow> rows, int detectorVersion) =>
        rows.Where(row => row.CurrentResultDetectorVersion < detectorVersion);
}
