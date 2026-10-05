using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using Debarr.Activity;
using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.Extensions;
using Debarr.Hosting;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components.Layout;

/// <summary>The area at the bottom of the navigation that shows activity events as short-lived messages, gathered into one batch per second, which grow in and shrink away.</summary>
public sealed partial class ActivityMessageArea(ActivityFeed activityFeed, IScheduler scheduler) : ComponentBase, IDisposable
{
    private static readonly TimeSpan MessageLifetime = TimeSpan.FromSeconds(5);

    private readonly CompositeDisposable _disposables = [];

    // A new list on each change, so the motion list sees which messages came and went.
    private IReadOnlyList<Message> _messages = [];

    protected override void OnInitialized()
    {
        activityFeed.Events
            .Buffer(TimeSpan.FromSeconds(1), scheduler)
            .Select(Describe)
            .Where(texts => texts.Count > 0)
            .Select((texts, batch) => texts.Select(text => new Message(batch, text.Text, text.Severity)).ToList())
            .SelectMany(batch => Observable
                .Return<Action>(() => _messages = WithBatchReplacingRepeatedTexts(_messages, batch))
                .Concat(Observable.Timer(MessageLifetime, scheduler).Select<long, Action>(_ => () => _messages = [.. _messages.Except(batch)])))
            .Select(change => Observable.FromAsync(() => InvokeAsync(() =>
            {
                change();
                StateHasChanged();
            })))
            .Concat()
            .CompleteOnError(exception => _ = DispatchExceptionAsync(exception))
            .Subscribe()
            .DisposeWith(_disposables);
    }

    private static List<Message> WithBatchReplacingRepeatedTexts(IReadOnlyList<Message> showing, List<Message> batch) =>
    [
        .. showing.Select(message => batch.FirstOrDefault(fresh => fresh.Text == message.Text) ?? message),
        .. batch.Where(fresh => showing.All(message => message.Text != fresh.Text)),
    ];

    /// <summary>
    /// One message for each kind of event in the batch, so a burst of hashes or folder scans shows once.
    /// Work that starts shows among the running work, so only its outcome becomes a message.
    /// </summary>
    private static IReadOnlyList<(string Text, Severity Severity)> Describe(IList<ActivityEvent> batch) => batch
        .Select<ActivityEvent, (string Text, Severity Severity)?>(activityEvent => activityEvent switch
        {
            ScanFinishedEvent { Folder: null, Error: not null } finished => ($"Library scan failed: {finished.Error}", Severity.Error),
            ScanFinishedEvent { Error: not null } finished => ($"Scan of {finished.Folder} failed: {finished.Error}", Severity.Error),
            RootFolderRemovalFinishedEvent { Error: not null } finished =>
                ($"Removing root folder {finished.RootFolder} did not finish. Run Scan Now on System > Tasks to remove the rest.", Severity.Error),
            _ => DescribeOutcome(activityEvent, batch) is { } text ? (text, Severity.Info) : null,
        })
        .OfType<(string Text, Severity Severity)>()
        .Distinct()
        .ToList();

    private static string? DescribeOutcome(ActivityEvent activityEvent, IList<ActivityEvent> batch) => activityEvent switch
    {
        HostSettingsSavedEvent => "Host settings saved. Restart Debarr to apply them.",
        CommittedEvent { Event.Data: FilePathAdded } => CountMessage(CountCommitted<FilePathAdded>(batch), "Hashed {0} file", "Hashed {0} files"),
        CommittedEvent { Event.Data: FilePathRemoved } => CountMessage(CountCommitted<FilePathRemoved>(batch), "Removed {0} file path", "Removed {0} file paths"),
        CommittedEvent { Event.Data: VideoFileArchived } => CountMessage(CountCommitted<VideoFileArchived>(batch), "Archived {0} video file", "Archived {0} video files"),
        CommittedEvent committed => Describe(committed.Event.Data),
        ScanFinishedEvent { Folder: null, Cancelled: true } => "Library scan cancelled.",
        ScanFinishedEvent { Folder: null } finished => $"Library scan finished in {finished.Duration.ToDisplayText()}.",
        ScanFinishedEvent { Cancelled: true } => null,
        ScanFinishedEvent => CountMessage(batch.OfType<ScanFinishedEvent>().Count(scan => scan is { Folder: not null, Error: null, Cancelled: false }), "Scanned {0} folder", "Scanned {0} folders"),
        DetectionFinishedEvent => FileMessage(batch.OfType<DetectionFinishedEvent>().Select(finished => finished.Path.Value).ToList(), "Finished detecting"),
        RootFolderRemovalFinishedEvent finished => $"Removed root folder {finished.RootFolder}.",
        _ => null,
    };

    private static string? Describe(object storedEvent) => storedEvent switch
    {
        RootFolderAdded or RootFolderEnabled or RootFolderDisabled or LibrarySettingsChanged => "Library settings saved.",
        RootFolderRemoved removed => $"Removing root folder {removed.Path.Value}.",
        DetectionSettingsChanged or StandardRatiosChanged => "Detection settings saved.",
        PlayerAdded or PlayerChanged or PlayerRenamed => "Player saved.",
        PlayerRemoved => "Player removed.",
        NotifierAdded or NotifierChanged or NotifierRenamed => "Notifier saved.",
        NotifierRemoved => "Notifier removed.",
        UISettingsChanged => "UI settings saved.",
        OverrideSaved => "Override saved.",
        HistoryCleared => "History cleared.",
        _ => null,
    };

    private static int CountCommitted<TEvent>(IList<ActivityEvent> batch) =>
        batch.OfType<CommittedEvent>().Count(committed => committed.Event.Data is TEvent);

    /// <summary>The action and the file's name for one file, such as "Started detecting Heat.mkv.", or the action and the count for several.</summary>
    private static string FileMessage(IReadOnlyList<string> paths, string action) =>
        paths.Count == 1 ? $"{action} {Path.GetFileName(paths[0])}." : CountMessage(paths.Count, action + " {0} file", action + " {0} files");

    private static string CountMessage(int count, string singular, string plural) =>
        string.Format(CultureInfo.InvariantCulture, count == 1 ? singular : plural, count.ToCountText()) + ".";

    /// <summary>One message, from the <paramref name="Batch"/>th batch that made any.</summary>
    private sealed record Message(int Batch, string Text, Severity Severity);

    public void Dispose() => _disposables.Dispose();
}
