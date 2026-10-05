using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.Extensions;
using Debarr.Health;
using Debarr.Hosting;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Fisher;
using FluentResults;
using Microsoft.Extensions.Options;

namespace Debarr.Components.Pages;

public partial class StatusPage(IOptions<AppOptions> appOptions, IDocumentStore store, RootFolderRemover rootFolderRemover, PlayerConnectionService playerConnectionService, HealthCheckService healthCheckService, FfmpegVersions ffmpegVersions)
{
    private const int RecentDeliveryCount = 5;

    // Null until the first reload.
    private List<PlayerRow>? _players;
    private List<NotifierRow> _notifiers = [];
    private IReadOnlyList<RootFolderRow> _roots = [];
    private int _pendingCount;
    private int _failedCount;
    private IReadOnlyList<HealthMessage> _health = [];
    private string? _ffmpegVersion;
    private string? _ffprobeVersion;

    private static string Version =>
        typeof(StatusPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>The version with its commit hash cut to seven characters, such as 1.0.0+3f2a9c1.</summary>
    private static string ShortVersion => Version.Split('+', 2) switch
    {
        [var number, var commit] when commit.Length > 7 => $"{number}+{commit[..7]}",
        _ => Version,
    };

    private static DateTimeOffset StartedAt { get; } = new(System.Diagnostics.Process.GetCurrentProcess().StartTime);

    private static string Runtime => RuntimeInformation.FrameworkDescription;

    private static string OperatingSystem => RuntimeInformation.OSDescription;

    private string DataDirectory => appOptions.Value.DataDir;

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string>
    {
        nameof(Players),
        nameof(Notifiers),
        nameof(Library),
        PlaybackRowProjection.ReadModel,
        NotifierDeliveryProjection.ReadModel,
        HistoryClearProjection.ReadModel,
        LibraryScanSummaryRowProjection.ReadModel,
        MediaRowProjection.ReadModel,
    };

    protected override bool ShowsActivity(ActivityEvent activityEvent) =>
        activityEvent is HealthChangedEvent or PlayerConnectionStateChangedEvent
            || RootFolderRemover.ChangesRunningRootFolderRemoval(activityEvent);

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        _health = await healthCheckService.Messages.FirstAsync().ToTask(cancellationToken);
        _ffmpegVersion = VersionText(await ffmpegVersions.GetFfmpegAsync(cancellationToken));
        _ffprobeVersion = VersionText(await ffmpegVersions.GetFfprobeAsync(cancellationToken));
        var connectionStates = await playerConnectionService.ConnectionStates.FirstAsync().ToTask(cancellationToken);
        await using var session = store.QuerySession();

        var players = (await Players.ReadAsync(session, cancellationToken)).All.OrderBy(player => player.Name, StringComparer.Ordinal).ToList();
        var lastPlaybacks = await session.ReadLastPlaybacksAsync(players.Select(player => player.Id), cancellationToken);
        _players =
        [
            .. players.Select(player => new PlayerRow(
                player.Name,
                player.Enabled,
                player.Enabled ? connectionStates.GetValueOrDefault(player.Id)?.State : null,
                lastPlaybacks.GetValueOrDefault(player.Id))),
        ];

        var notifiers = (await Notifiers.ReadAsync(session, cancellationToken)).All.OrderBy(notifier => notifier.Name, StringComparer.Ordinal);
        _notifiers = [];
        foreach (var notifier in notifiers)
        {
            var deliveries = await session.ReadNewestDeliveriesAsync(notifier.Id, RecentDeliveryCount, cancellationToken);
            _notifiers.Add(new NotifierRow(notifier.Name, notifier.Enabled, deliveries));
        }

        _roots = RootFolderRow.Of(await session.ReadRootFoldersAsync(cancellationToken), rootFolderRemover.RunningRootFolderRemoval);
        var counts = await session.CountMediaRowsByStatusAsync(null, cancellationToken);
        _pendingCount = counts.GetValueOrDefault(VideoFileStatus.Pending);
        _failedCount = counts.GetValueOrDefault(VideoFileStatus.Failed);
    }

    private static string VersionText(Result<string> version) => version.IsSuccess ? version.Value : "Unavailable";

    /// <summary>A configured player, its connection state and its newest playback.</summary>
    /// <param name="State">Null while the player is disabled, and until its connection enters a state.</param>
    private sealed record PlayerRow(string Name, bool Enabled, PlayerConnectionState? State, PlaybackRow? LastPlayback);

    /// <summary>A configured notifier and its newest deliveries, newest first.</summary>
    private sealed record NotifierRow(string Name, bool Enabled, IReadOnlyList<Delivery> Deliveries);
}
