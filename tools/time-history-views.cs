#!/usr/bin/env dotnet
// Times every History view on a copy of a data directory's debarr.db, as PlaybackRowQuery runs them, and writes each view's query plan.
//   dotnet run tools/time-history-views.cs -- <debarr.db> <label> noclear <out dir> [runs]   every outcome filter, player, search, sort, direction, first and last page
//   dotnet run tools/time-history-views.cs -- <debarr.db> <label> clear <out dir> [runs]     the same after a history clear halfway through the playbacks, appended when the database has none
// Writes <out dir>/<label>-<phase>.tsv (the median and slowest of the runs after one warm-up, in milliseconds) and <out dir>/<label>-<phase>-plans.txt.
#:project ../src/Debarr/Debarr.csproj
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using Debarr.Playing;
using Fisher;
using Fisher.Linq;
using JasperFx;
using Microsoft.Data.Sqlite;
using Weasel.Core;

var db = args[0];
var label = args[1];
var phase = args[2];
var outDir = args[3];
var runs = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 5;

const int PageSize = 50;
Directory.CreateDirectory(outDir);
var timings = Path.Combine(outDir, $"{label}-{phase}.tsv");
var plans = Path.Combine(outDir, $"{label}-{phase}-plans.txt");
File.WriteAllText(timings, "label\tphase\toutcome\tplayer\tsearch\tsort\tdir\tpage\tkind\tmedian\tmax\truns\n");
File.WriteAllText(plans, "");

var store = DocumentStore.For(options =>
{
    options.Connection(new SqliteConnectionStringBuilder { DataSource = db }.ToString());
    options.ConfigureSerialization(EnumStorage.AsString, Casing.CamelCase);
    options.AutoCreateSchemaObjects = AutoCreate.None;
    PlaybackRowProjection.AddTo(options);
    HistoryClearProjection.AddTo(options);
});

var ct = CancellationToken.None;
if (phase == "clear")
{
    await using var check = store.QuerySession();
    if (await check.ReadHistoryClearedAtAsync(ct) == DateTimeOffset.MinValue)
    {
        var first = await check.Query<PlaybackRow>().OrderBy(row => row.OccurredAt).Select(row => row.OccurredAt).FirstAsync(ct);
        var last = await check.Query<PlaybackRow>().OrderByDescending(row => row.OccurredAt).Select(row => row.OccurredAt).FirstAsync(ct);
        var midpoint = first + ((last - first) / 2);
        await using var session = store.LightweightSession();
        session.Events.Append(ClearHistoryHandler.HistoryStreamId, 0, new HistoryCleared(midpoint));
        await session.SaveChangesAsync(ct);
        Console.WriteLine($"Cleared at {midpoint:O}");
    }
}

DateTimeOffset clearedAt;
List<string> players;
await using (var session = store.QuerySession())
{
    clearedAt = await session.ReadHistoryClearedAtAsync(ct);
    players = [.. await session.ReadPlayerNamesAsync(ct)];
}
Console.WriteLine($"{label} {phase}: cleared at {clearedAt:O}; players {string.Join(", ", players)}; runs {runs}");

PlaybackRowFilter?[] outcomes = [null, PlaybackRowFilter.Sent, PlaybackRowFilter.NotSent, PlaybackRowFilter.DeliveryFailed];
string?[] searches = [null, "tv", "film"];
string?[] playerFilters = [null, .. players];
var sorts = Enum.GetValues<PlaybackRowSort>();
bool[] directions = [false, true];
var total = Stopwatch.StartNew();
var views = 0;

foreach (var search in searches)
{
    foreach (var player in playerFilters)
    {
        var scope = new PlaybackRowScope(player, search);
        PlaybackRowCounts counts = null!;
        var (countsMedian, countsMax) = await TimeAsync(async () => counts = await Read(session => session.CountPlaybackRowsByOutcomeAsync(scope, ct)));
        Write(null, player, search, "-", "-", "-", "counts", countsMedian, countsMax);
        foreach (var outcome in outcomes)
        {
            var lastPage = Math.Max(((counts[outcome] + PageSize - 1) / PageSize) - 1, 0);
            foreach (var sort in sorts)
            {
                foreach (var descending in directions)
                {
                    var view = new PlaybackRowView(outcome, player, search, sort, descending);
                    foreach (var (pageName, page) in new[] { ("first", 0), ("last", lastPage) })
                    {
                        var (median, max) = await TimeAsync(() => Read(session => session.ReadHistoryPageAsync(view, counts, page, PageSize, ct)));
                        Write(outcome, player, search, sort.ToString(), descending ? "desc" : "asc", pageName, "page", median, max);
                        views++;
                    }

                    await using var session = store.QuerySession();
                    var plan = await session.Query<PlaybackRow>().HistoryPage(view, clearedAt).Skip(lastPage * PageSize).Take(PageSize).ExplainAsync(ct);
                    File.AppendAllText(plans, $"{Key(outcome, player, search, sort.ToString(), descending ? "desc" : "asc")}\n{string.Join("\n", plan.Steps.Select(step => "  " + step.Detail))}\n");
                }
            }
        }

        Console.WriteLine($"  scope player={player ?? "-"} search={search ?? "-"} done after {total.Elapsed.TotalSeconds:F0}s ({views} views)");
    }
}

Console.WriteLine($"Done: {views} page reads in {total.Elapsed.TotalSeconds:F0}s; {timings}; {plans}");
await store.DisposeAsync();

async Task<T> Read<T>(Func<IQuerySession, Task<T>> read)
{
    await using var session = store.QuerySession();
    return await read(session);
}

async Task<(double Median, double Max)> TimeAsync<T>(Func<Task<T>> read)
{
    await read();
    var times = new List<double>();
    for (var run = 0; run < runs; run++)
    {
        var watch = Stopwatch.StartNew();
        await read();
        times.Add(watch.Elapsed.TotalMilliseconds);
    }

    times.Sort();
    return (times[runs / 2], times[^1]);
}

static string Key(PlaybackRowFilter? outcome, string? player, string? search, string sort, string dir) =>
    $"{outcome?.ToString() ?? "All"} | {player ?? "every player"} | {(search is null ? "no search" : $"\"{search}\"")} | {sort} {dir}";

void Write(PlaybackRowFilter? outcome, string? player, string? search, string sort, string dir, string page, string kind, double median, double max)
{
    var line = string.Join('\t', label, phase, outcome?.ToString() ?? "All", player ?? "-", search ?? "-", sort, dir, page, kind, median.ToString("F2", CultureInfo.InvariantCulture), max.ToString("F2", CultureInfo.InvariantCulture), runs);
    File.AppendAllText(timings, line + "\n");
}
