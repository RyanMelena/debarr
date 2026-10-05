#!/usr/bin/env dotnet
// Times Re-detect All, or a standard ratios change that turns every detected result into one from the file, on a data directory's debarr.db,
// while scan workers rewrite the stat of the files the command reaches, and counts the command's attempts and the workers' commits.
//   dotnet run tools/time-library-wide-commands.cs -- <debarr.db> redetect|recheck [workers]
// Run it on a copy: the command and the workers write to the database.
#:project ../src/Debarr/Debarr.csproj
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Fisher;
using Fisher.Linq;
using Fisher.Services;
using JasperFx.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Wolverine.Runtime;
using IDocumentSessionOperations = JasperFx.Events.Documents.IDocumentSessionOperations;

var db = args[0];
var command = args[1];
var workers = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 4;

var attempts = new AttemptCounter();
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Services
    .AddEventStore(new SqliteConnectionStringBuilder { DataSource = db }.ToString())
    .AddSingleton(TimeProvider.System)
    .AddScanning().AddDetecting().AddPlaying().AddNotifying().AddAppearance()
    .AddQuartz(quartz =>
    {
        quartz.UseInMemoryStore();
        quartz.AddScanJobs();
    })
    .ConfigureFisher(options => options.Listeners.Add(attempts));
using var host = builder.Build();
await host.StartAsync();
var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
var store = host.Services.GetRequiredService<IDocumentStore>();

List<(FileHash VideoFile, FilePath FilePath)> reached;
object libraryWide;
await using (var session = store.QuerySession())
{
    var rows = await session.Query<MediaRow>().Where(row => row.CurrentResult != null || row.LastFailure != null).ToListAsync();
    reached = [.. rows.Where(row => row.FilePaths.Count > 0).Select(row => (row.FileHash, row.FilePaths[0]))];
    var settings = await DetectionSettings.ReadAsync(session, CancellationToken.None);
    libraryWide = command == "redetect"
        ? new RedetectAll()
        : new ChangeDetectionSettings(
            settings.SimultaneousDetections,
            settings.PictureMeasurement.SampleCount,
            settings.PictureMeasurement.SkipStartAndEndPercent,
            settings.TimeoutSeconds,
            settings.PictureMeasurement.BlackLevelSdr,
            settings.PictureMeasurement.BlackLevelHdr,
            [.. settings.StandardRatios.Ratios.Select(standardRatio => standardRatio with { ChecksPicture = false })],
            settings.StandardRatios.MatchTolerance);
    Console.WriteLine($"{rows.Count} video files with a result or a failure, {reached.Count} with a file path");
}

using var stop = new CancellationTokenSource();
var commits = 0;
var next = -1;
var scan = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
{
    while (!stop.IsCancellationRequested)
    {
        var (videoFile, filePath) = reached[Interlocked.Increment(ref next) % reached.Count];
        var stat = new FileStat(filePath.Stat.Size, DateTimeOffset.UtcNow);
        if ((await runtime.SendCommandAsync(new AddFilePath(videoFile, filePath.Path, stat, DateTimeOffset.UtcNow), CancellationToken.None)).IsSuccess)
        {
            Interlocked.Increment(ref commits);
        }
    }
})).ToList();

await Task.Delay(TimeSpan.FromSeconds(workers > 0 ? 2 : 0));
var startedAt = Stopwatch.GetTimestamp();
var result = await runtime.SendCommandAsync(libraryWide, CancellationToken.None);
var elapsed = Stopwatch.GetElapsedTime(startedAt);
await stop.CancelAsync();
await Task.WhenAll(scan);

Console.WriteLine($"{command}\tworkers {workers}\t{(result.IsSuccess ? "succeeded" : "failed: " + string.Join(" ", result.Errors.Select(error => error.Message)))}\t{elapsed.TotalSeconds:F1} s\tattempts {attempts.Count}\tscan commits {commits}");
await host.StopAsync();

/// <summary>Counts the commits that clear or convert a current result, one per attempt of the library-wide command.</summary>
sealed class AttemptCounter : IDocumentSessionListener
{
    private int _count;

    public int Count => _count;

    public Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        if (((IDocumentSessionOperations)session).PendingStreams.SelectMany(stream => stream.Events).Any(pending => pending.Data is DetectionResultCleared or DetectionResultConverted))
        {
            Interlocked.Increment(ref _count);
        }

        return Task.CompletedTask;
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) => Task.CompletedTask;
}
