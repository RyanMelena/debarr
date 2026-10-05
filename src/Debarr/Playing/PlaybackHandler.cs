using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Notifying;
using Debarr.Scanning;
using Fisher;
using FluentResults;
using Wolverine.Attributes;
using Wolverine.Runtime;

namespace Debarr.Playing;

/// <summary>
/// Turns each playback started event into a recorded playback and at most one notification, from startup to shutdown,
/// and records each delivery with its playback.
/// A player's newer playback cancels the handling of its one before, deliveries in flight included.
/// </summary>
[WolverineIgnore]
public sealed partial class PlaybackHandler : IHostedService, IDisposable
{
    private static readonly Func<ILogger, Guid, string, IDisposable?> PlaybackScope = LoggerMessage.DefineScope<Guid, string>("Playback {PlaybackId} on {Player}");

    private readonly IDocumentStore _store;

    private readonly IWolverineRuntime _runtime;

    private readonly LibraryScanner _libraryScanner;

    private readonly NotificationPublisher _notificationPublisher;

    private readonly ILogger<PlaybackHandler> _logger;

    private readonly IObservable<Unit> _handling;

    private readonly CompositeDisposable _disposables = [];

    public PlaybackHandler(
        PlayerConnectionService playerConnectionService,
        IDocumentStore store,
        IWolverineRuntime runtime,
        LibraryScanner libraryScanner,
        NotificationPublisher notificationPublisher,
        ILogger<PlaybackHandler> logger)
    {
        _store = store;
        _runtime = runtime;
        _libraryScanner = libraryScanner;
        _notificationPublisher = notificationPublisher;
        _logger = logger;

        // Switch cancels the handling of a player's playback when that player starts another.
        _handling = playerConnectionService.PlaybackStarted
            .GroupBy(playbackStarted => playbackStarted.PlayerId)
            .SelectMany(playerPlaybacks => playerPlaybacks
                .Select(playbackStarted => Observable.FromAsync(cancellationToken => HandleAsync(playbackStarted, cancellationToken)))
                .Switch());
    }

    /// <summary>Starts handling playback, before the player connections open.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handling
            .CompleteOnError(LogHandlingStopped)
            .Subscribe()
            .DisposeWith(_disposables);
        return Task.CompletedTask;
    }

    /// <summary>Stops handling playback and cancels the deliveries in flight.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposables.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _disposables.Dispose();

    /// <summary>
    /// Chooses the ratio the playback sends, starts sending its notification, records the playback while the attempts are in flight, and records each delivery as its attempt ends.
    /// Cancelling ends the attempts in flight, and each is recorded as cancelled.
    /// </summary>
    private async Task HandleAsync(PlaybackStartedEvent playbackStarted, CancellationToken cancellationToken)
    {
        var playbackId = Guid.NewGuid();
        using var scope = PlaybackScope(_logger, playbackId, playbackStarted.PlayerName);
        LocalPath? localPath = null;
        VideoFile? videoFile = null;
        PlaybackOutcome outcome;
        try
        {
            if (playbackStarted.PlayerPath.IsStream())
            {
                outcome = new PlaybackOutcome.Stream();
            }
            else
            {
                IReadOnlyList<PathMapping> pathMappings;
                StandardRatios standardRatios;
                await using (var session = _store.QuerySession())
                {
                    pathMappings = (await Players.ReadAsync(session, cancellationToken)).Find(playbackStarted.PlayerId)?.PathMappings ?? [];
                    standardRatios = (await DetectionSettings.ReadAsync(session, cancellationToken)).StandardRatios;
                }

                localPath = playbackStarted.PlayerPath.ToLocalPath(pathMappings);
                videoFile = await IdentifyVideoFileAsync(localPath.Value, cancellationToken);
                outcome = Decide(videoFile?.Override, videoFile?.CurrentResult, playbackStarted.PlayerAspectRatio, standardRatios);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogHandlingFailed(exception, playbackStarted.PlayerName);
            outcome = new PlaybackOutcome.Failed(exception.Message);
        }

        var playback = new RecordPlayback(
            playbackId,
            playbackStarted.OccurredAt,
            playbackStarted.PlayerId,
            playbackStarted.PlayerName,
            playbackStarted.Title,
            playbackStarted.PlayerPath,
            localPath,
            videoFile?.FileHash,
            playbackStarted.PlayerAspectRatio,
            outcome);
        LogPlayback(playback);

        // The attempts are in flight before the playback is recorded, so recording the history adds nothing to the time to deliver.
        var attempts = Playback.NotificationFor(playback.PlayerName, playback.OccurredAt, outcome) is { } notification
            ? await _notificationPublisher.SendAsync(notification, cancellationToken)
            : [];
        if (!await RecordAsync(playback, playback.PlayerName))
        {
            return;
        }

        await foreach (var attempt in Task.WhenEach(attempts))
        {
            await RecordAsync(new RecordDelivery(playback.PlaybackId, await attempt), playback.PlayerName);
        }
    }

    /// <summary>
    /// The ratio a playback of a file sends, or why it sends nothing, from its video file's override and current result and the player's ratio,
    /// both null when the file scan found no video file.
    /// The override is sent as the operator set it, and every other ratio snapped.
    /// </summary>
    public static PlaybackOutcome Decide(Override? @override, Detection? currentResult, AspectRatio? playerAspectRatio, StandardRatios standardRatios) =>
        (@override, currentResult, playerAspectRatio) switch
        {
            ({ DontSend: true }, _, _) => new PlaybackOutcome.DontSend(),
            ({ AspectRatio: { } overrideAspectRatio }, _, _) => new PlaybackOutcome.Sent(overrideAspectRatio.Value, NotificationAspectRatioSource.Manual, null),
            (_, { Result: { } result } current, _) => new PlaybackOutcome.Sent(
                standardRatios.Snap(result.RawAspectRatio).Value,
                result.AspectRatioSource switch
                {
                    AspectRatioSource.FromFile => NotificationAspectRatioSource.Container,
                    AspectRatioSource.Detected => NotificationAspectRatioSource.Detected,
                    _ => throw new ArgumentOutOfRangeException(nameof(currentResult), result.AspectRatioSource, null),
                },
                current.Id),
            (_, _, { } player) => new PlaybackOutcome.Sent(standardRatios.Snap(player).Value, NotificationAspectRatioSource.Player, null),
            _ => new PlaybackOutcome.NoPlayerAspectRatio(),
        };

    /// <summary>
    /// The video file the path holds now, after a file scan hashes changed content again and finds a moved or renamed file's video file.
    /// Null for a path outside every enabled root folder, with an extension outside the list, or with no readable file.
    /// </summary>
    private async Task<VideoFile?> IdentifyVideoFileAsync(LocalPath localPath, CancellationToken cancellationToken)
    {
        if (await _libraryScanner.ScanFileAsync(localPath, cancellationToken) is not { } fileHash)
        {
            return null;
        }

        await using var session = _store.QuerySession();
        return await VideoFile.ReadAsync(session, fileHash, cancellationToken)
            ?? throw new InvalidOperationException($"The file scan found video file {fileHash}, which has no stream.");
    }

    /// <summary>Sends a command that records the playback or a delivery, and logs its failure; true when it succeeded.</summary>
    /// <remarks>The command runs to the end after a newer playback cancels the handling, so a cancelled attempt is recorded.</remarks>
    private async Task<bool> RecordAsync(object command, string playerName)
    {
        Result result;
        try
        {
            result = await _runtime.SendCommandAsync(command, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogRecordThrew(exception, playerName);
            return false;
        }

        if (result.IsFailed)
        {
            LogRecordFailed(playerName, result.GetFormError());
        }

        return result.IsSuccess;
    }

    private void LogPlayback(RecordPlayback playback)
    {
        var path = playback.LocalPath?.Value ?? playback.PlayerPath.Value;
        var title = playback.Title ?? "no title";
        switch (playback.Outcome)
        {
            case PlaybackOutcome.Sent sent:
                LogSent(playback.PlayerName, path, title, sent.AspectRatio, sent.Source);
                break;
            default:
                LogNotSent(playback.PlayerName, path, title, playback.Outcome.GetType().Name);
                break;
        }
    }

    [LoggerMessage(LogLevel.Critical, "Playback handling stopped.")]
    private partial void LogHandlingStopped(Exception exception);

    [LoggerMessage(LogLevel.Error, "Handling playback on {Player} failed.")]
    private partial void LogHandlingFailed(Exception exception, string player);

    [LoggerMessage(LogLevel.Error, "Could not record the playback on {Player}.")]
    private partial void LogRecordThrew(Exception exception, string player);

    [LoggerMessage(LogLevel.Error, "Could not record the playback on {Player}. {Error}")]
    private partial void LogRecordFailed(string player, string? error);

    [LoggerMessage(LogLevel.Information, "{Player} played {Path} ({Title}) and sent {AspectRatio} from {Source}.")]
    private partial void LogSent(string player, string path, string title, double aspectRatio, NotificationAspectRatioSource source);

    [LoggerMessage(LogLevel.Information, "{Player} played {Path} ({Title}) and sent nothing ({Reason}).")]
    private partial void LogNotSent(string player, string path, string title, string reason);
}
