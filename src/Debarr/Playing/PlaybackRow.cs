using System.Text.Json.Serialization;
using Debarr.Extensions;
using Debarr.Detecting;
using Debarr.Scanning;
using Fisher;
using Fisher.Linq;
using Fisher.Projections;
using Fisher.Storage.FullText;
using JasperFx.Events.Projections;

namespace Debarr.Playing;

/// <summary>
/// A row keeps the player's name as it was, and outlives its player, its video file and its detection.
/// The members an index or a query names are worked out from its other members and written into the document beside them.
/// Two rows are equal when they hold the same playback, so a table that keys its rows by item keeps each row's element when its deliveries change.
/// </summary>
public sealed class PlaybackRow : IEquatable<PlaybackRow>
{
    public Guid Id { get; set; }

    /// <summary>When Debarr received the event; the notification's occurred_at.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public Guid PlayerId { get; set; }

    /// <summary>The player's name at playback.</summary>
    public string PlayerName { get; set; } = "";

    public string? Title { get; set; }

    public PlayerPath PlayerPath { get; set; }

    /// <summary>The translated path; null for a stream, or when handling failed before translating it.</summary>
    public LocalPath? LocalPath { get; set; }

    /// <summary>Null when the path is outside the library.</summary>
    public FileHash? VideoFile { get; set; }

    public AspectRatio? PlayerAspectRatio { get; set; }

    public PlaybackOutcome Outcome { get; set; } = default!;

    public List<Delivery> Deliveries { get; set; } = [];

    public Guid? VideoFileId => VideoFile?.StreamId;

    public bool Sent => Outcome is PlaybackOutcome.Sent;

    public bool DeliveryFailed => Deliveries.Any(delivery => delivery.Outcome is DeliveryOutcome.Failed);

    /// <summary>The outcomes as their number, since the store writes an enum as its name and the outcome counts group by a number.</summary>
    public int OutcomeGroup => (int)Outcomes;

    public PlaybackRowOutcomes Outcomes =>
        (Sent ? PlaybackRowOutcomes.Sent : PlaybackRowOutcomes.None) | (DeliveryFailed ? PlaybackRowOutcomes.DeliveryFailed : PlaybackRowOutcomes.None);

    public string PlayerNameKey => PlayerName.ToCaseInsensitiveThenOrdinalSortKey();

    public string? TitleKey => Title?.ToCaseInsensitiveThenOrdinalSortKey();

    public string PathKey => Path.ToCaseInsensitiveThenOrdinalSortKey();

    public string SearchText => string.Join('\n', new[] { Title, LocalPath?.Value, PlayerPath.Value }.OfType<string>().Select(text => text.ToLowerInvariant()));

    [JsonIgnore]
    public string Path => LocalPath?.Value ?? PlayerPath.Value;

    [JsonIgnore]
    public Guid? DetectionId => (Outcome as PlaybackOutcome.Sent)?.DetectionId;

    public bool Equals(PlaybackRow? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as PlaybackRow);

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>Whether a playback sent a notification and whether one of its deliveries failed.</summary>
[Flags]
public enum PlaybackRowOutcomes
{
    None = 0,
    Sent = 1,
    DeliveryFailed = 2,
}

public enum PlaybackRowSort
{
    Time,
    Player,
    Title,
    Path,
}

public enum PlaybackRowFilter
{
    Sent,
    NotSent,
    DeliveryFailed,
}

public sealed record PlaybackRowView(PlaybackRowFilter? Outcome, string? Player, string? Search, PlaybackRowSort Sort, bool Descending)
{
    public PlaybackRowScope Scope => new(Player, Search);
}

/// <summary>The history's player filter and search, which narrow every outcome's count.</summary>
public sealed record PlaybackRowScope(string? Player, string? Search);

public sealed record PlaybackRowCounts(int All, int Sent, int NotSent, int DeliveryFailed)
{
    public int this[PlaybackRowFilter? outcome] => outcome switch
    {
        PlaybackRowFilter.Sent => Sent,
        PlaybackRowFilter.NotSent => NotSent,
        PlaybackRowFilter.DeliveryFailed => DeliveryFailed,
        _ => All,
    };

    public static PlaybackRowCounts FromOutcomeGroups(IReadOnlyCollection<(int OutcomeGroup, int Count)> groups)
    {
        int CountWith(PlaybackRowOutcomes outcome) => groups.Where(group => ((PlaybackRowOutcomes)group.OutcomeGroup).HasFlag(outcome)).Sum(group => group.Count);

        var all = groups.Sum(group => group.Count);
        var sent = CountWith(PlaybackRowOutcomes.Sent);
        return new PlaybackRowCounts(all, sent, all - sent, CountWith(PlaybackRowOutcomes.DeliveryFailed));
    }
}

public sealed class PlaybackRowProjection : SingleStreamProjection<PlaybackRow, Guid>
{
    public const string ReadModel = nameof(PlaybackRow);

    public PlaybackRowProjection() => Name = ReadModel;

    public static void AddTo(StoreOptions options)
    {
        options.Schema.For<PlaybackRow>()
            .Index(row => row.OccurredAt, name: "ix_playback_row_history_by_time")
            .Index([row => row.PlayerNameKey, row => row.OccurredAt], name: "ix_playback_row_history_by_player_name")
            .Index([row => row.TitleKey, row => row.OccurredAt], name: "ix_playback_row_history_by_title")
            .Index([row => row.PathKey, row => row.OccurredAt], name: "ix_playback_row_history_by_path")
            .Index([row => row.Sent, row => row.OccurredAt], name: "ix_playback_row_history_sent_by_time")
            .Index([row => row.Sent, row => row.PlayerNameKey, row => row.OccurredAt], name: "ix_playback_row_history_sent_by_player_name")
            .Index([row => row.Sent, row => row.TitleKey, row => row.OccurredAt], name: "ix_playback_row_history_sent_by_title")
            .Index([row => row.Sent, row => row.PathKey, row => row.OccurredAt], name: "ix_playback_row_history_sent_by_path")
            .Index(row => row.OccurredAt, name: "ix_playback_row_history_delivery_failed_by_time", predicate: row => row.DeliveryFailed)
            .Index([row => row.PlayerNameKey, row => row.OccurredAt], name: "ix_playback_row_history_delivery_failed_by_player_name", predicate: row => row.DeliveryFailed)
            .Index([row => row.TitleKey, row => row.OccurredAt], name: "ix_playback_row_history_delivery_failed_by_title", predicate: row => row.DeliveryFailed)
            .Index([row => row.PathKey, row => row.OccurredAt], name: "ix_playback_row_history_delivery_failed_by_path", predicate: row => row.DeliveryFailed)
            .Index([row => row.PlayerName, row => row.OccurredAt], name: "ix_playback_row_history_player_by_time")
            .Index([row => row.PlayerName, row => row.TitleKey, row => row.OccurredAt], name: "ix_playback_row_history_player_by_title")
            .Index([row => row.PlayerName, row => row.PathKey, row => row.OccurredAt], name: "ix_playback_row_history_player_by_path")
            .Index([row => row.OutcomeGroup, row => row.OccurredAt], name: "ix_playback_row_history_outcome_counts")
            .Index([row => row.PlayerName, row => row.OutcomeGroup, row => row.OccurredAt], name: "ix_playback_row_history_player_outcome_counts")
            .Index([row => row.PlayerId, row => row.OccurredAt], name: "ix_playback_row_player_playbacks")
            .Index([row => row.VideoFileId, row => row.OccurredAt], name: "ix_playback_row_video_file_playbacks")
            .FullTextIndex(FullTextTokenizer.Trigram, row => row.SearchText);
        options.Projections.Add(new PlaybackRowProjection(), ProjectionLifecycle.Inline);
    }

    public static PlaybackRow Create(PlaybackHandled handled) => new()
    {
        Id = handled.PlaybackId,
        OccurredAt = handled.OccurredAt,
        PlayerId = handled.PlayerId,
        PlayerName = handled.PlayerName,
        Title = handled.Title,
        PlayerPath = handled.PlayerPath,
        LocalPath = handled.LocalPath,
        VideoFile = handled.VideoFile,
        PlayerAspectRatio = handled.PlayerAspectRatio,
        Outcome = handled.Outcome,
    };

    public static void Apply(DeliveryFinished finished, PlaybackRow row) => row.Deliveries.Add(finished.Delivery);
}

public static class PlaybackRowQuery
{
    /// <summary>The shortest search the trigram index takes; a shorter one scans the search text.</summary>
    public const int TrigramLength = 3;

    /// <summary>The trigram index's table, which Fisher names for the document type.</summary>
    public const string FullTextTable = "fi_fts_playbackrow";

    /// <summary>
    /// The playbacks the trigram index matches the phrase in, as Fisher's NgramSearch writes it with a unary plus on the rowid,
    /// so SQLite reads the playbacks through the view's index in its order and probes each one in the list of matches.
    /// </summary>
    private const string TrigramMatch = $"+rowid in (select rowid from {FullTextTable} where {FullTextTable} match ?)";

    public static async Task<int> CountShownPlaybackRowsAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        await (await session.ShownPlaybackRowsAsync(cancellationToken)).CountAsync(cancellationToken);

    public static async Task<PlaybackRowCounts> CountPlaybackRowsByOutcomeAsync(this IQuerySession session, PlaybackRowScope scope, CancellationToken cancellationToken) =>
        PlaybackRowCounts.FromOutcomeGroups(await (await session.ShownPlaybackRowsAsync(cancellationToken)).OutcomeGroups(scope).ToListAsync(cancellationToken));

    public static async Task<IReadOnlyList<string>> ReadPlayerNamesAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        await (await session.ShownPlaybackRowsAsync(cancellationToken)).PlayerNames().ToListAsync(cancellationToken);

    /// <summary>The view's page, or its last page when <paramref name="counts"/>, the view's counts, hold fewer playbacks than the page needs.</summary>
    public static async Task<IReadOnlyList<PlaybackRow>> ReadHistoryPageAsync(
        this IQuerySession session,
        PlaybackRowView view,
        PlaybackRowCounts counts,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var clearedAt = await session.ReadHistoryClearedAtAsync(cancellationToken);
        var lastPage = Math.Max(((counts[view.Outcome] + pageSize - 1) / pageSize) - 1, 0);
        return await session.Query<PlaybackRow>().HistoryPage(view, clearedAt).Skip(Math.Min(page, lastPage) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
    }

    public static async Task<Dictionary<Guid, PlaybackRow>> ReadLastPlaybacksAsync(this IQuerySession session, IEnumerable<Guid> playerIds, CancellationToken cancellationToken)
    {
        var shown = await session.ShownPlaybackRowsAsync(cancellationToken);
        var lastPlaybacks = new Dictionary<Guid, PlaybackRow>();
        foreach (var playerId in playerIds)
        {
            if (await shown.PlayerPlaybacksNewestFirst(playerId).FirstOrDefaultAsync(cancellationToken) is { } lastPlayback)
            {
                lastPlaybacks[playerId] = lastPlayback;
            }
        }

        return lastPlaybacks;
    }

    public static async Task<IReadOnlyList<PlaybackRow>> ReadVideoFilePlaybacksAsync(this IQuerySession session, FileHash videoFile, CancellationToken cancellationToken)
    {
        var videoFileId = videoFile.StreamId;
        return await (await session.ShownPlaybackRowsAsync(cancellationToken))
            .Where(row => row.VideoFileId == videoFileId)
            .OrderByDescending(row => row.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public static async Task<IQueryable<PlaybackRow>> ShownPlaybackRowsAsync(this IQuerySession session, CancellationToken cancellationToken) =>
        session.Query<PlaybackRow>().ShownAfter(await session.ReadHistoryClearedAtAsync(cancellationToken));

    /// <remarks>SQLite reads a grouped count from the index alone when the group's value is an aggregate, so the value is the group's largest, which is its key.</remarks>
    public static IQueryable<(int OutcomeGroup, int Count)> OutcomeGroups(this IQueryable<PlaybackRow> rows, PlaybackRowScope scope) =>
        rows.Matching(scope)
            .GroupBy(row => row.OutcomeGroup)
            .Select(group => ValueTuple.Create(group.Max(row => row.OutcomeGroup), group.Count()));

    public static IQueryable<PlaybackRow> ShownAfter(this IQueryable<PlaybackRow> rows, DateTimeOffset clearedAt) =>
        rows.Where(row => row.OccurredAt > clearedAt);

    public static IQueryable<PlaybackRow> Matching(this IQueryable<PlaybackRow> rows, PlaybackRowView view) =>
        (view.Outcome switch
        {
            PlaybackRowFilter.Sent => rows.Where(row => row.Sent),
            PlaybackRowFilter.NotSent => rows.Where(row => !row.Sent),
            PlaybackRowFilter.DeliveryFailed => rows.Where(row => row.DeliveryFailed),
            _ => rows,
        }).Matching(view.Scope);

    public static IQueryable<PlaybackRow> Matching(this IQueryable<PlaybackRow> rows, PlaybackRowScope scope)
    {
        var matching = scope.Player is { } player ? rows.Where(row => row.PlayerName == player) : rows;
        var term = scope.Search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return matching;
        }

        if (term.Length >= TrigramLength)
        {
            // One FTS5 phrase, quoted with each quotation mark doubled, so FTS5's syntax characters in the term match as text.
            var phrase = '"' + term.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
            return matching.Where(row => row.MatchesSql(TrigramMatch, phrase));
        }

        var lowercased = term.ToLowerInvariant();
        return matching.Where(row => row.SearchText.Contains(lowercased));
    }

    public static IQueryable<PlaybackRow> HistoryPage(this IQueryable<PlaybackRow> rows, PlaybackRowView view, DateTimeOffset clearedAt)
    {
        // SQLite checks a negated bound inside an index and never seeks on it, so the view's filter and sort choose the index, which reads the page in order.
        var matching = rows.Where(row => !(row.OccurredAt <= clearedAt)).Matching(view);
        var descending = view.Descending;
        return view.Sort switch
        {
            PlaybackRowSort.Player when view.Player is null => matching.OrderBy(row => row.PlayerNameKey, descending).ThenBy(row => row.OccurredAt, descending),
            PlaybackRowSort.Title => matching.OrderBy(row => row.TitleKey, descending).ThenBy(row => row.OccurredAt, descending),
            PlaybackRowSort.Path => matching.OrderBy(row => row.PathKey, descending).ThenBy(row => row.OccurredAt, descending),
            _ => matching.OrderBy(row => row.OccurredAt, descending),
        };
    }

    public static IQueryable<string> PlayerNames(this IQueryable<PlaybackRow> rows) =>
        rows.Select(row => row.PlayerName).Distinct().OrderBy(playerName => playerName);

    public static IQueryable<PlaybackRow> PlayerPlaybacksNewestFirst(this IQueryable<PlaybackRow> rows, Guid playerId) =>
        rows.Where(row => row.PlayerId == playerId).OrderByDescending(row => row.OccurredAt);
}
