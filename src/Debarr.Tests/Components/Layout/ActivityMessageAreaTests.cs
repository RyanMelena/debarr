using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using Bunit;
using Debarr.Activity;
using Debarr.Appearance;
using Debarr.Components.Layout;
using Debarr.Detecting;
using Debarr.Hosting;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Activity;
using Debarr.Tests.Detecting;
using JasperFx.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Reactive.Testing;
using MudBlazor.Services;

namespace Debarr.Tests.Components.Layout;

public class ActivityMessageAreaTests : BunitContext
{
    private readonly Subject<ActivityEvent> _source = new();
    private readonly TestScheduler _scheduler = new();
    private readonly FakeTimeProvider _timeProvider = new();

    public ActivityMessageAreaTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(new ActivityFeed([new TestActivitySource(_source)]));
        Services.AddSingleton<IScheduler>(_scheduler);
        Services.AddSingleton<TimeProvider>(_timeProvider);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void A_burst_of_saves_shows_one_message_for_five_seconds_that_grows_in_and_shrinks_away()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(new HostSettingsSavedEvent());
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
        _source.OnNext(new HostSettingsSavedEvent());
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
        _source.OnNext(new HostSettingsSavedEvent());
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        var message = Assert.Single(cut.FindAll("#activity-messages .activity-message"));
        Assert.Equal("Host settings saved. Restart Debarr to apply them.", message.TextContent.Trim());
        Assert.Contains("motion-enter", message.ClassList);

        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks + TimeSpan.FromSeconds(4.9).Ticks);
        Assert.DoesNotContain("motion-leave", Assert.Single(cut.FindAll("#activity-messages .activity-message")).ClassList);

        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks + TimeSpan.FromSeconds(5).Ticks);
        Assert.Contains("motion-leave", Assert.Single(cut.FindAll("#activity-messages .activity-message")).ClassList);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#activity-messages .activity-message")));
    }

    [Fact]
    public void Committed_saves_show_one_message_for_each_kind_of_setting()
    {
        var now = DateTimeOffset.UnixEpoch;
        var playerId = Guid.NewGuid();
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(Committed(new RootFolderAdded(new LocalPath("/media"), now)));
        _source.OnNext(Committed(new RootFolderDisabled(new LocalPath("/media"))));
        _source.OnNext(Committed(new LibrarySettingsChanged(Library.Default.Settings)));
        _source.OnNext(Committed(new DetectionSettingsChanged(3, DetectionSettings.Default.PictureMeasurement, 600)));
        _source.OnNext(Committed(new PlayerAdded(playerId, "Theater", true, KodiEndpoint.Create("kodi.lan", 9090, 60, 10).Value, [], [])));
        _source.OnNext(Committed(new PlayerRenamed(playerId, "Lounge")));
        _source.OnNext(Committed(new PlayerRemoved(playerId)));
        _source.OnNext(Committed(new NotifierRenamed(Guid.NewGuid(), "Broker")));
        _source.OnNext(Committed(new NotifierRemoved(Guid.NewGuid())));
        _source.OnNext(Committed(new UISettingsChanged(UITheme.Dark, DateTimeFormats.Default, false)));
        _source.OnNext(Committed(new OverrideSaved(new Override(null, true, null, now))));
        _source.OnNext(Committed(new HistoryCleared(now)));
        _source.OnNext(Committed(new DetectionResultCleared(now)));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.Equal(
            ["Library settings saved.", "Detection settings saved.", "Player saved.", "Player removed.", "Notifier saved.", "Notifier removed.", "UI settings saved.", "Override saved.", "History cleared."],
            cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));
    }

    [Fact]
    public void A_burst_of_scan_events_shows_one_message_for_each_kind()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(new ScanStartedEvent(null));
        _source.OnNext(Committed(new VideoFileDiscovered(TestFileHash.For("1"), 1, DateTimeOffset.UnixEpoch)));
        _source.OnNext(Committed(AddedPath("1", "/media/a.mkv")));
        _source.OnNext(Committed(AddedPath("2", "/media/b.mkv")));
        _source.OnNext(Committed(AddedPath("2", "/media/c.mkv")));
        _source.OnNext(Committed(new FilePathRemoved(TestFileHash.For("3"), new LocalPath("/media/d.mkv"), DateTimeOffset.UnixEpoch)));
        _source.OnNext(new ScanFinishedEvent("/media/x", TimeSpan.FromSeconds(1), null, false));
        _source.OnNext(new ScanFinishedEvent("/media/y", TimeSpan.FromSeconds(1), null, false));
        _source.OnNext(Committed(new VideoFileArchived(DateTimeOffset.UnixEpoch)));
        _source.OnNext(new ScanFinishedEvent(null, TimeSpan.FromSeconds(65), null, false));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.Equal(
            ["Hashed 3 files.", "Removed 1 file path.", "Scanned 2 folders.", "Archived 1 video file.", "Library scan finished in 1 min 5 s."],
            cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));
    }

    [Fact]
    public void A_scan_a_scan_pause_cancelled_shows_cancelled_for_the_library_and_nothing_for_a_folder()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(new ScanFinishedEvent("/media/x", TimeSpan.FromSeconds(1), null, true));
        _source.OnNext(new ScanFinishedEvent(null, TimeSpan.FromSeconds(3), null, true));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.Equal(["Library scan cancelled."], cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));
    }

    [Fact]
    public void Detections_show_one_message_for_those_finished_naming_a_lone_file()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(new DetectionStartedEvent(TestFileHash.For("1"), new LocalPath("/media/Heat (1995).mkv")));
        _source.OnNext(new DetectionStartedEvent(TestFileHash.For("2"), new LocalPath("/media/Alien (1979).mkv")));
        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("1"), new LocalPath("/media/Heat (1995).mkv")));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.Equal(
            ["Finished detecting Heat (1995).mkv."],
            cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));

        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("2"), new LocalPath("/media/Alien (1979).mkv")));
        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("3"), new LocalPath("/media/Ran (1985).mkv")));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(2).Ticks);

        Assert.Equal(
            ["Finished detecting Heat (1995).mkv.", "Finished detecting 2 files."],
            cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));
    }

    [Fact]
    public void Removing_a_root_folder_names_the_removal_when_it_starts_and_when_it_ends()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(Committed(new RootFolderRemoved(new LocalPath("/media/old"))));
        _source.OnNext(new RootFolderRemovalStartedEvent("/media/old", DateTimeOffset.UnixEpoch));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.Equal(
            ["Removing root folder /media/old."],
            cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));

        _source.OnNext(new RootFolderRemovalFinishedEvent("/media/old", null));
        foreach (var _ in Enumerable.Range(0, 4000))
        {
            _source.OnNext(Committed(new VideoFileArchived(DateTimeOffset.UnixEpoch)));
        }

        _scheduler.AdvanceTo(TimeSpan.FromSeconds(2).Ticks);

        Assert.Equal(
            ["Removing root folder /media/old.", "Removed root folder /media/old.", "Archived 4,000 video files."],
            cut.FindAll("#activity-messages .mud-alert").Select(message => message.TextContent.Trim()));
    }

    [Fact]
    public void A_removal_that_did_not_finish_shows_as_an_error_that_says_how_to_remove_the_rest()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(new RootFolderRemovalFinishedEvent("/media/old", "Another change was saved at the same time, so nothing was saved. Try again."));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        var alert = cut.Find("#activity-messages .mud-alert");
        Assert.Equal("Removing root folder /media/old did not finish. Run Scan Now on System > Tasks to remove the rest.", alert.TextContent.Trim());
        Assert.Contains("mud-alert-text-error", alert.ClassList);
    }

    [Fact]
    public void A_message_with_a_path_wraps_after_its_separators()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(Committed(new RootFolderRemoved(new LocalPath("/media/a-long-folder/another-long-folder"))));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        var message = cut.Find("#activity-messages .mud-alert .path-text");
        Assert.Equal(4, message.QuerySelectorAll("wbr").Length);
        Assert.Contains("mud-alert-text-info", cut.Find("#activity-messages .mud-alert").ClassList);
    }

    [Fact]
    public void A_message_a_later_batch_repeats_shows_once_for_five_seconds_from_that_batch()
    {
        var cut = Render<ActivityMessageArea>();

        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("1"), new LocalPath("/media/a.mkv")));
        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("2"), new LocalPath("/media/b.mkv")));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);
        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("3"), new LocalPath("/media/c.mkv")));
        _source.OnNext(new DetectionFinishedEvent(TestFileHash.For("4"), new LocalPath("/media/d.mkv")));
        _scheduler.AdvanceTo(TimeSpan.FromSeconds(2).Ticks);

        var message = Assert.Single(cut.FindAll("#activity-messages .activity-message"));
        Assert.Equal("Finished detecting 2 files.", message.TextContent.Trim());

        _scheduler.AdvanceTo(TimeSpan.FromSeconds(6).Ticks);
        Assert.DoesNotContain("motion-leave", Assert.Single(cut.FindAll("#activity-messages .activity-message")).ClassList);

        _scheduler.AdvanceTo(TimeSpan.FromSeconds(7).Ticks);
        Assert.Contains("motion-leave", Assert.Single(cut.FindAll("#activity-messages .activity-message")).ClassList);
    }

    private static FilePathAdded AddedPath(string content, string path) =>
        new(TestFileHash.For(content), new LocalPath(path), new FileStat(1, DateTimeOffset.UnixEpoch), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static CommittedEvent Committed<TEvent>(TEvent data)
        where TEvent : notnull =>
        new(new Event<TEvent>(data));
}
