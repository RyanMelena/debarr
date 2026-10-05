using System.Text.RegularExpressions;
using Debarr.Playing;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Fisher.Linq;
using Fisher;

namespace Debarr.Tests.Playing;

public sealed class PlaybackRowQueryTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    private static readonly PlaybackRowView All = new(null, null, null, PlaybackRowSort.Time, true);

    [Fact]
    public async Task Each_outcome_filter_lists_and_counts_its_playbacks()
    {
        await RecordAsync(TestPlayback.Handled(playerPath: "/media/delivered.mkv", occurredAt: Now), Delivered());
        await RecordAsync(TestPlayback.Handled(playerPath: "plugin://plugin.video.youtube/play", occurredAt: Now.AddMinutes(1)));
        await RecordAsync(TestPlayback.Handled(playerPath: "/media/failed.mkv", occurredAt: Now.AddMinutes(2)), Delivered(), Failed());
        await RecordAsync(TestPlayback.Handled(playerPath: "/media/cancelled.mkv", occurredAt: Now.AddMinutes(3)), Cancelled());

        Assert.Equal(["cancelled", "failed", "play", "delivered"], await ReadNamesAsync(All));
        Assert.Equal(["cancelled", "failed", "delivered"], await ReadNamesAsync(All with { Outcome = PlaybackRowFilter.Sent }));
        Assert.Equal(["play"], await ReadNamesAsync(All with { Outcome = PlaybackRowFilter.NotSent }));
        Assert.Equal(["failed"], await ReadNamesAsync(All with { Outcome = PlaybackRowFilter.DeliveryFailed }));
        Assert.Equal([4, 3, 1, 1], await CountEachFilterAsync(All));
    }

    [Fact]
    public async Task The_player_filter_keeps_the_playbacks_that_recorded_that_name_and_the_names_are_listed_once_each()
    {
        await RecordAsync(TestPlayback.Handled("Theater", playerPath: "/media/first.mkv", occurredAt: Now));
        await RecordAsync(TestPlayback.Handled("Bedroom", playerPath: "/media/bedroom.mkv", occurredAt: Now.AddMinutes(1), playerId: TestPlayback.BedroomId));
        await RecordAsync(TestPlayback.Handled("Theater", playerPath: "plugin://plugin.video.youtube/second", occurredAt: Now.AddMinutes(2)));
        await RecordAsync(TestPlayback.Handled("theater", playerPath: "/media/renamed.mkv", occurredAt: Now.AddMinutes(3)));

        Assert.Equal(["second", "first"], await ReadNamesAsync(All with { Player = "Theater" }));
        Assert.Equal([2, 1, 1, 0], await CountEachFilterAsync(All with { Player = "Theater" }));
        await using var session = Store.QuerySession();
        Assert.Equal(["Bedroom", "Theater", "theater"], await session.ReadPlayerNamesAsync(CancellationToken));
    }

    [Theory]
    [InlineData(PlaybackRowSort.Time, new[] { "a", "b", "B", "c" })]
    [InlineData(PlaybackRowSort.Player, new[] { "B", "a", "b", "c" })]
    [InlineData(PlaybackRowSort.Title, new[] { "c", "B", "b", "a" })]
    [InlineData(PlaybackRowSort.Path, new[] { "a", "B", "b", "c" })]
    public async Task Each_sort_orders_both_ways_ignoring_case_and_breaks_ties_by_time_in_the_same_direction(PlaybackRowSort sort, string[] ascending)
    {
        await RecordAsync(TestPlayback.Handled("Theater", title: "Zulu", localPath: "/media/a.mkv", occurredAt: Now));
        await RecordAsync(TestPlayback.Handled("Theater", title: "Heat", localPath: "/media/b.mkv", occurredAt: Now.AddMinutes(1)));
        await RecordAsync(TestPlayback.Handled("bedroom", title: "arrival", playerPath: "/media/B.mkv", occurredAt: Now.AddMinutes(2), playerId: TestPlayback.BedroomId));
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/c.mkv", occurredAt: Now.AddMinutes(3)));

        Assert.Equal(ascending, await ReadNamesAsync(new PlaybackRowView(null, null, null, sort, false)));
        Assert.Equal(ascending.Reverse(), await ReadNamesAsync(new PlaybackRowView(null, null, null, sort, true)));
    }

    [Fact]
    public async Task Under_a_player_filter_the_player_sort_is_the_time_order()
    {
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/b.mkv", occurredAt: Now.AddMinutes(1)));
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/a.mkv", occurredAt: Now));
        await RecordAsync(TestPlayback.Handled("Bedroom", localPath: "/media/c.mkv", occurredAt: Now.AddMinutes(2), playerId: TestPlayback.BedroomId));

        Assert.Equal(["a", "b"], await ReadNamesAsync(new PlaybackRowView(null, "Theater", null, PlaybackRowSort.Player, false)));
        Assert.Equal(["b", "a"], await ReadNamesAsync(new PlaybackRowView(null, "Theater", null, PlaybackRowSort.Player, true)));
    }

    [Fact]
    public async Task A_page_reads_its_rows_counts_every_match_and_past_the_end_reads_the_last_page()
    {
        foreach (var index in Enumerable.Range(0, 7))
        {
            await RecordAsync(TestPlayback.Handled(localPath: $"/media/film-{index}.mkv", occurredAt: Now.AddMinutes(index)));
        }

        await using var session = Store.QuerySession();
        var view = All with { Descending = false };
        var (totalCount, rows) = await ReadPageAsync(session, view, 1, 3);
        var (_, lastRows) = await ReadPageAsync(session, view, 5, 3);

        Assert.Equal(7, totalCount);
        Assert.Equal(["film-3", "film-4", "film-5"], rows.Select(Name));
        Assert.Equal(["film-6"], lastRows.Select(Name));
    }

    [Theory]
    [InlineData("heat", new[] { "Heat" })]
    [InlineData("FILMS", new[] { "Heat" })]
    [InlineData("youtube", new[] { "Clip" })]
    [InlineData(" Clip ", new[] { "Clip" })]
    [InlineData("100%", new string[0])]
    [InlineData(" ", new[] { "Clip", "Heat" })]
    [InlineData("EA", new[] { "Heat" })]
    [InlineData("p", new[] { "Clip" })]
    public async Task A_search_matches_the_title_the_local_path_or_the_player_path_ignoring_case(string search, string[] expected)
    {
        await RecordAsync(TestPlayback.Handled(title: "Heat", playerPath: "smb://nas/media/Heat.mkv", localPath: @"\\nas\media\Films\Heat.mkv"));
        await RecordAsync(TestPlayback.Handled(title: "Clip", playerPath: "plugin://plugin.video.youtube/play"));

        await using var session = Store.QuerySession();
        var (count, rows) = await ReadPageAsync(session, All with { Search = search }, 0, 50);

        Assert.Equal(expected, rows.Select(row => row.Title!).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Length, count);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\"")]
    [InlineData("a%b")]
    [InlineData("a_b")]
    [InlineData("a\"b")]
    [InlineData("*")]
    [InlineData("a*b")]
    public async Task A_search_treats_percent_underscore_quotes_and_asterisks_as_text(string term)
    {
        await RecordAsync(TestPlayback.Handled(localPath: $"/media/{term}.mkv", occurredAt: Now));
        await RecordAsync(TestPlayback.Handled(localPath: "/media/axb.mkv", occurredAt: Now.AddMinutes(1)));
        await RecordAsync(TestPlayback.Handled(localPath: "/media/plain.mkv", occurredAt: Now.AddMinutes(2)));

        Assert.Equal([term], await ReadNamesAsync(All with { Search = term }));
    }

    [Fact]
    public async Task The_filter_counts_count_among_the_playbacks_the_player_and_search_match()
    {
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/films/a.mkv"), Failed());
        await RecordAsync(TestPlayback.Handled("Theater", playerPath: "plugin://films/b"));
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/shows/c.mkv"));
        await RecordAsync(TestPlayback.Handled("Bedroom", localPath: "/media/films/d.mkv", playerId: TestPlayback.BedroomId));

        Assert.Equal([2, 1, 1, 1], await CountEachFilterAsync(All with { Player = "Theater", Search = "films" }));
        Assert.Equal([3, 2, 1, 1], await CountEachFilterAsync(All with { Search = "films" }));
        Assert.Equal([4, 3, 1, 1], await CountEachFilterAsync(All));
        Assert.Equal([4, 3, 1, 1], await CountEachFilterAsync(All with { Outcome = PlaybackRowFilter.DeliveryFailed }));
    }

    [Fact]
    public async Task Under_a_search_of_three_or_more_characters_every_view_reads_what_fishers_trigram_search_reads_before_and_after_a_clear()
    {
        await RecordAsync(TestPlayback.Handled("Theater", title: "Heat", playerPath: "/storage/heat.mkv", localPath: "/media/films/heat.mkv", occurredAt: Now), Delivered());
        await RecordAsync(TestPlayback.Handled("Theater", title: "heat", playerPath: "smb://nas/Films/heat (1995).mkv", occurredAt: Now.AddMinutes(1)), Delivered(), Failed());
        await RecordAsync(TestPlayback.Handled("Theater", playerPath: "plugin://plugin.video.films/clip", occurredAt: Now.AddMinutes(2)));
        await RecordAsync(TestPlayback.Handled("Bedroom", title: "Arrival", playerPath: "/storage/arrival.mkv", localPath: "/media/Films/arrival.mkv", occurredAt: Now.AddMinutes(3), playerId: TestPlayback.BedroomId), Cancelled());
        await RecordAsync(TestPlayback.Handled("Bedroom", title: "Zulu", playerPath: "/storage/zulu.mkv", localPath: "/media/shows/zulu.mkv", occurredAt: Now.AddMinutes(4), playerId: TestPlayback.BedroomId), Failed());
        await RecordAsync(TestPlayback.Handled("theater", title: "Films of 1995", playerPath: "/storage/a.mkv", localPath: "/media/a.mkv", occurredAt: Now.AddMinutes(5)), Failed());
        await RecordAsync(TestPlayback.Handled("Theater", playerPath: "/storage/zulu.mkv", localPath: "/media/films/zulu.mkv", occurredAt: Now.AddMinutes(6)), Delivered());
        await RecordAsync(TestPlayback.Handled("Theater", title: "Arrival", playerPath: "plugin://plugin.video.youtube/films", occurredAt: Now.AddMinutes(7)));
        await RecordAsync(TestPlayback.Handled("Bedroom", title: "heat", playerPath: "/storage/heat.mkv", localPath: "/media/films/Heat.mkv", occurredAt: Now.AddMinutes(8), playerId: TestPlayback.BedroomId), Delivered());

        await AssertEveryViewReadsWhatFishersTrigramSearchReadsAsync(DateTimeOffset.MinValue);
        Assert.Equal(new PlaybackRowCounts(8, 6, 2, 2), await ReadStoreAsync(session => session.CountPlaybackRowsByOutcomeAsync(new PlaybackRowScope(null, "film"), CancellationToken)));

        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(ClearHistoryHandler.HistoryStreamId, new HistoryCleared(Now.AddMinutes(3)));
            await session.SaveChangesAsync(CancellationToken);
        }

        await AssertEveryViewReadsWhatFishersTrigramSearchReadsAsync(Now.AddMinutes(3));
        Assert.Equal(new PlaybackRowCounts(4, 3, 1, 1), await ReadStoreAsync(session => session.CountPlaybackRowsByOutcomeAsync(new PlaybackRowScope(null, "film"), CancellationToken)));
    }

    [Fact]
    public async Task Each_players_last_playback_is_its_newest()
    {
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/second.mkv", occurredAt: Now.AddMinutes(2)));
        await RecordAsync(TestPlayback.Handled("Theater", localPath: "/media/first.mkv", occurredAt: Now.AddMinutes(1)));
        await RecordAsync(TestPlayback.Handled("Bedroom", localPath: "/media/bedroom.mkv", occurredAt: Now, playerId: TestPlayback.BedroomId));

        await using var session = Store.QuerySession();
        var lastPlaybacks = await session.ReadLastPlaybacksAsync([TestPlayback.TheaterId, TestPlayback.BedroomId, Guid.NewGuid()], CancellationToken);

        Assert.Equal(
            [(TestPlayback.TheaterId, "second"), (TestPlayback.BedroomId, "bedroom")],
            lastPlaybacks.Select(lastPlayback => (lastPlayback.Key, Name(lastPlayback.Value))));
    }

    [Fact]
    public async Task A_video_files_playbacks_come_newest_first()
    {
        var film = TestFileHash.For("film");
        await RecordAsync(TestPlayback.Handled(localPath: "/media/first.mkv", videoFile: film, occurredAt: Now));
        await RecordAsync(TestPlayback.Handled(localPath: "/media/second.mkv", videoFile: film, occurredAt: Now.AddMinutes(1)));
        await RecordAsync(TestPlayback.Handled(localPath: "/media/other.mkv", videoFile: TestFileHash.For("other"), occurredAt: Now.AddMinutes(2)));
        await RecordAsync(TestPlayback.Handled(playerPath: "plugin://plugin.video.youtube/play", occurredAt: Now.AddMinutes(3)));

        await using var session = Store.QuerySession();

        Assert.Equal(["second", "first"], (await session.ReadVideoFilePlaybacksAsync(film, CancellationToken)).Select(Name));
    }

    [Fact]
    public async Task Every_history_view_reads_its_page_through_an_index()
    {
        await using var session = Store.QuerySession();
        List<string> unindexed = [];
        foreach (var view in Views())
        {
            var page = await session.Query<PlaybackRow>().HistoryPage(view, DateTimeOffset.MinValue).Skip(50).Take(50).ExplainAsync(CancellationToken);
            if (!page.UsesIndex)
            {
                unindexed.Add($"{view}: {page}");
            }
        }

        Assert.True(unindexed.Count == 0, string.Join("\n\n", unindexed));
    }

    [Fact]
    public async Task Each_sort_under_an_outcome_filter_or_a_player_filter_reads_its_own_index_in_order()
    {
        await using var session = Store.QuerySession();
        List<string> sorted = [];
        foreach (var view in Views().Where(view => view.Outcome is null || view.Player is null))
        {
            var page = await session.Query<PlaybackRow>().HistoryPage(view, DateTimeOffset.MinValue).Skip(50).Take(50).ExplainAsync(CancellationToken);
            if (!ReadsIndexInOrder(page, HistoryIndex(view)!))
            {
                sorted.Add($"{view}: {page}");
            }
        }

        Assert.True(sorted.Count == 0, string.Join("\n\n", sorted));
    }

    [Fact]
    public async Task Under_a_search_of_three_or_more_characters_every_view_reads_its_index_in_order_and_probes_the_matches()
    {
        await using var session = Store.QuerySession();
        List<string> unprobed = [];
        foreach (var view in Views().Select(view => view with { Search = "heat" }))
        {
            var page = await session.Query<PlaybackRow>().HistoryPage(view, DateTimeOffset.MinValue).Skip(50).Take(50).ExplainAsync(CancellationToken);
            var readsInOrder = HistoryIndex(view) is { } index ? ReadsIndexInOrder(page, index) : ReadsAnIndexInOrder(page);
            if (!readsInOrder || !ProbesTheTrigramMatches(page.Steps.Select(step => step.Detail)))
            {
                unprobed.Add($"{view}: {page}");
            }
        }

        Assert.True(unprobed.Count == 0, string.Join("\n\n", unprobed));
    }

    [Fact]
    public async Task A_search_of_three_or_more_characters_is_fishers_trigram_search_with_the_rowid_kept_from_driving_the_read()
    {
        await using var session = Store.QuerySession();
        var shown = await session.ShownPlaybackRowsAsync(CancellationToken);
        var trigramSearch = session.ToSql(shown.Where(row => row.NgramSearch("heat")));
        var trigramMatch = trigramSearch[trigramSearch.IndexOf("rowid in (", StringComparison.Ordinal)..];

        Assert.Equal(
            trigramSearch.Replace(trigramMatch, $"(+{trigramMatch})", StringComparison.Ordinal),
            session.ToSql(shown.Matching(All with { Search = "heat" })));
    }

    [Fact]
    public async Task A_shorter_search_reads_the_time_index_in_order()
    {
        await using var session = Store.QuerySession();
        var page = await session.Query<PlaybackRow>().HistoryPage(All with { Search = "x1" }, DateTimeOffset.MinValue).Take(50).ExplainAsync(CancellationToken);

        Assert.True(ReadsIndexInOrder(page, "ix_playback_row_history_by_time"), page.ToString());
    }

    [Theory]
    [InlineData(null, null, "SCAN fi_doc_playbackrow USING COVERING INDEX ix_playback_row_history_outcome_counts")]
    [InlineData("Theater", null, "SEARCH fi_doc_playbackrow USING COVERING INDEX ix_playback_row_history_player_outcome_counts (<expr>=?)")]
    [InlineData(null, "heat", "SCAN fi_doc_playbackrow USING COVERING INDEX ix_playback_row_history_outcome_counts")]
    [InlineData("Theater", "heat", "SEARCH fi_doc_playbackrow USING COVERING INDEX ix_playback_row_history_player_outcome_counts (<expr>=?)")]
    public async Task The_outcome_counts_read_one_covering_index_with_and_without_a_player_and_a_search(string? player, string? search, string read)
    {
        await using var session = Store.QuerySession();
        var shown = await session.ShownPlaybackRowsAsync(CancellationToken);

        var steps = (await shown.OutcomeGroups(new PlaybackRowScope(player, search)).ExplainAsync(CancellationToken)).Steps.Select(step => step.Detail).ToList();

        Assert.Equal(read, steps[0]);
        Assert.DoesNotContain(steps, step => step.Contains("TEMP B-TREE", StringComparison.Ordinal));
        Assert.Equal(search is not null, ProbesTheTrigramMatches(steps));
    }

    [Fact]
    public async Task The_player_names_the_last_playbacks_and_a_video_files_playbacks_read_an_index()
    {
        await using var session = Store.QuerySession();
        var shown = await session.ShownPlaybackRowsAsync(CancellationToken);
        var videoFileId = TestFileHash.For("film").StreamId;

        var playerNames = await shown.PlayerNames().ExplainAsync(CancellationToken);
        var lastPlayback = await shown.PlayerPlaybacksNewestFirst(TestPlayback.TheaterId).Take(1).ExplainAsync(CancellationToken);
        var videoFile = await shown.Where(row => row.VideoFileId == videoFileId).OrderByDescending(row => row.OccurredAt).ExplainAsync(CancellationToken);

        Assert.True(playerNames.UsesIndex, playerNames.ToString());
        Assert.True(ReadsIndexInOrder(lastPlayback, "ix_playback_row_player_playbacks"), lastPlayback.ToString());
        Assert.True(ReadsIndexInOrder(videoFile, "ix_playback_row_video_file_playbacks"), videoFile.ToString());
    }

    private async Task AssertEveryViewReadsWhatFishersTrigramSearchReadsAsync(DateTimeOffset clearedAt)
    {
        await using var session = Store.QuerySession();
        List<string> expected = [];
        List<string> read = [];
        foreach (var view in from search in new[] { "film", " HEAT ", "t (1" } from view in Views() select view with { Search = search })
        {
            var searched = session.Query<PlaybackRow>().Where(row => row.NgramSearch(view.Search!.Trim()));
            var rows = await searched.HistoryPage(view with { Search = null }, clearedAt).ToListAsync(CancellationToken);
            List<int> counts = [];
            foreach (var outcome in new PlaybackRowFilter?[] { null, PlaybackRowFilter.Sent, PlaybackRowFilter.NotSent, PlaybackRowFilter.DeliveryFailed })
            {
                counts.Add(await searched.ShownAfter(clearedAt).Matching(view with { Outcome = outcome, Search = null }).CountAsync(CancellationToken));
            }

            var pages = rows.Chunk(2).ToList();
            var lastPage = pages.LastOrDefault() ?? [];
            expected.Add(Describe(view, rows.Count, rows, pages.ElementAtOrDefault(1) ?? lastPage, lastPage, new PlaybackRowCounts(counts[0], counts[1], counts[2], counts[3])));

            var (count, page) = await ReadPageAsync(session, view, 0, 50);
            var (_, secondPage) = await ReadPageAsync(session, view, 1, 2);
            var (_, pastTheEnd) = await ReadPageAsync(session, view, 50, 2);
            read.Add(Describe(view, count, page, secondPage, pastTheEnd, await session.CountPlaybackRowsByOutcomeAsync(view.Scope, CancellationToken)));
        }

        Assert.Equal(expected, read);
    }

    private static string Describe(PlaybackRowView view, int count, IEnumerable<PlaybackRow> page, IEnumerable<PlaybackRow> secondPage, IEnumerable<PlaybackRow> lastPage, PlaybackRowCounts counts)
    {
        static string Minutes(IEnumerable<PlaybackRow> rows) => string.Join(' ', rows.Select(row => (row.OccurredAt - Now).TotalMinutes));

        return $"{view}: {count} [{Minutes(page)}] second [{Minutes(secondPage)}] last [{Minutes(lastPage)}] {counts}";
    }

    private static IEnumerable<PlaybackRowView> Views() =>
        from outcome in new PlaybackRowFilter?[] { null, PlaybackRowFilter.Sent, PlaybackRowFilter.NotSent, PlaybackRowFilter.DeliveryFailed }
        from player in new[] { null, "Theater" }
        from sort in Enum.GetValues<PlaybackRowSort>()
        from inReverse in new[] { false, true }
        select new PlaybackRowView(outcome, player, null, sort, inReverse);

    private static bool ReadsAnIndexInOrder(QueryPlan plan) =>
        plan.UsesIndex && !plan.Steps.Any(step => step.Detail.Contains("TEMP B-TREE", StringComparison.Ordinal));

    private static bool ReadsIndexInOrder(QueryPlan plan, string index) =>
        ReadsAnIndexInOrder(plan) && plan.Steps.Any(step => Regex.IsMatch(step.Detail, $@"\bINDEX {index}\b"));

    /// <summary>The index a view's page reads in order; null under both an outcome filter and a player filter.</summary>
    private static string? HistoryIndex(PlaybackRowView view) => (view.Outcome, view.Player, view.Sort) switch
    {
        (null, null, PlaybackRowSort.Time) => "ix_playback_row_history_by_time",
        (null, null, PlaybackRowSort.Player) => "ix_playback_row_history_by_player_name",
        (null, null, PlaybackRowSort.Title) => "ix_playback_row_history_by_title",
        (null, null, PlaybackRowSort.Path) => "ix_playback_row_history_by_path",
        (PlaybackRowFilter.Sent or PlaybackRowFilter.NotSent, null, PlaybackRowSort.Time) => "ix_playback_row_history_sent_by_time",
        (PlaybackRowFilter.Sent or PlaybackRowFilter.NotSent, null, PlaybackRowSort.Player) => "ix_playback_row_history_sent_by_player_name",
        (PlaybackRowFilter.Sent or PlaybackRowFilter.NotSent, null, PlaybackRowSort.Title) => "ix_playback_row_history_sent_by_title",
        (PlaybackRowFilter.Sent or PlaybackRowFilter.NotSent, null, PlaybackRowSort.Path) => "ix_playback_row_history_sent_by_path",
        (PlaybackRowFilter.DeliveryFailed, null, PlaybackRowSort.Time) => "ix_playback_row_history_delivery_failed_by_time",
        (PlaybackRowFilter.DeliveryFailed, null, PlaybackRowSort.Player) => "ix_playback_row_history_delivery_failed_by_player_name",
        (PlaybackRowFilter.DeliveryFailed, null, PlaybackRowSort.Title) => "ix_playback_row_history_delivery_failed_by_title",
        (PlaybackRowFilter.DeliveryFailed, null, PlaybackRowSort.Path) => "ix_playback_row_history_delivery_failed_by_path",
        (null, not null, PlaybackRowSort.Time or PlaybackRowSort.Player) => "ix_playback_row_history_player_by_time",
        (null, not null, PlaybackRowSort.Title) => "ix_playback_row_history_player_by_title",
        (null, not null, PlaybackRowSort.Path) => "ix_playback_row_history_player_by_path",
        _ => null,
    };

    private static bool ProbesTheTrigramMatches(IEnumerable<string> steps) =>
        steps.Any(step => step.StartsWith("LIST SUBQUERY", StringComparison.Ordinal))
        && steps.Any(step => step.StartsWith($"SCAN {PlaybackRowQuery.FullTextTable} VIRTUAL TABLE INDEX", StringComparison.Ordinal))
        && !steps.Any(step => step.Contains("INTEGER PRIMARY KEY", StringComparison.Ordinal));

    private static Delivery Delivered() => TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Succeeded());

    private static Delivery Failed() => TestPlayback.Delivery(TestPlayback.LightsId, "lights", new DeliveryOutcome.Failed("Refused."));

    private static Delivery Cancelled() => TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Cancelled());

    private Task RecordAsync(PlaybackHandled playback, params Delivery[] deliveries) =>
        TestPlayback.RecordAsync(Store, playback, deliveries, CancellationToken);

    private async Task<List<int>> CountEachFilterAsync(PlaybackRowView view)
    {
        await using var session = Store.QuerySession();
        var counts = await session.CountPlaybackRowsByOutcomeAsync(view.Scope, CancellationToken);
        return [.. new PlaybackRowFilter?[] { null, PlaybackRowFilter.Sent, PlaybackRowFilter.NotSent, PlaybackRowFilter.DeliveryFailed }.Select(outcome => counts[outcome])];
    }

    private async Task<List<string>> ReadNamesAsync(PlaybackRowView view)
    {
        await using var session = Store.QuerySession();
        return [.. (await ReadPageAsync(session, view, 0, 50)).Rows.Select(Name)];
    }

    private static string Name(PlaybackRow row) => Path.GetFileNameWithoutExtension(row.Path.Replace('\\', '/'));

    private static async Task<(int Count, IReadOnlyList<PlaybackRow> Rows)> ReadPageAsync(IQuerySession session, PlaybackRowView view, int page, int pageSize)
    {
        var counts = await session.CountPlaybackRowsByOutcomeAsync(view.Scope, CancellationToken);
        return (counts[view.Outcome], await session.ReadHistoryPageAsync(view, counts, page, pageSize, CancellationToken));
    }
}
