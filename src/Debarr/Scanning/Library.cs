using Fisher;
using FluentResults;

namespace Debarr.Scanning;

/// <summary>The root folders, and how Debarr scans them.</summary>
public sealed record Library(IReadOnlyList<RootFolder> RootFolders, LibrarySettings Settings)
{
    /// <summary>The one stream the library's events are appended to.</summary>
    public static readonly Guid StreamId = new("5d0c7f3a-94e1-4b6d-b2a8-3e7f1c9d4a60");

    /// <summary>The library before its first event, which a missing stream folds to.</summary>
    public static Library Default { get; } = new([], LibrarySettings.Default);

    /// <summary>The library, with its root folders in path order.</summary>
    public static async Task<Library> ReadAsync(IQuerySession session, CancellationToken cancellationToken)
    {
        var library = await session.Events.FetchLatest<Library>(StreamId, cancellationToken) ?? Default;
        return library with { RootFolders = [.. library.RootFolders.OrderBy(rootFolder => rootFolder.Path.Value, StringComparer.Ordinal)] };
    }

    /// <summary>The stream's id, which the event store keys the aggregate on.</summary>
    public Guid Id => StreamId;

    /// <summary>The root folders removed and not added again, whose file paths a removal cut short can leave.</summary>
    public IReadOnlyList<LocalPath> RemovedRootFolders { get; init; } = [];

    public RootFolder? FindRootFolder(LocalPath path) => RootFolders.FirstOrDefault(rootFolder => rootFolder.Path == path);

    public static Library Create(RootFolderAdded added) => Default.Apply(added);

    public static Library Create(LibrarySettingsChanged changed) => Default.Apply(changed);

    public Library Apply(RootFolderAdded added) => this with
    {
        RootFolders = [.. RootFolders, new RootFolder(added.Path, true, added.AddedAt)],
        RemovedRootFolders = [.. RemovedRootFolders.Where(path => path != added.Path)],
    };

    public Library Apply(RootFolderEnabled enabled) => WithRootFolder(enabled.Path, rootFolder => rootFolder with { Enabled = true });

    public Library Apply(RootFolderDisabled disabled) => WithRootFolder(disabled.Path, rootFolder => rootFolder with { Enabled = false });

    public Library Apply(RootFolderRemoved removed) => this with
    {
        RootFolders = [.. RootFolders.Where(rootFolder => rootFolder.Path != removed.Path)],
        RemovedRootFolders = [.. RemovedRootFolders.Where(path => path != removed.Path), removed.Path],
    };

    public Library Apply(LibrarySettingsChanged changed) => this with { Settings = changed.Settings };

    private Library WithRootFolder(LocalPath path, Func<RootFolder, RootFolder> change) =>
        this with { RootFolders = [.. RootFolders.Select(rootFolder => rootFolder.Path == path ? change(rootFolder) : rootFolder)] };
}

/// <summary>A folder the operator adds, which library scans read everything under while it is enabled.</summary>
/// <param name="Path">The canonical local path, with no trailing separator.</param>
public sealed record RootFolder(LocalPath Path, bool Enabled, DateTimeOffset AddedAt);

/// <summary>Which files are video files, when library scans run, and whether folders are watched.</summary>
public sealed record LibrarySettings(VideoExtensions VideoExtensions, ScanInterval ScanInterval, bool WatchFolders)
{
    public static LibrarySettings Default { get; } = new(VideoExtensions.Default, ScanInterval.Default, true);
}

/// <summary>How many hours pass between scheduled library scans; off switches the schedule off.</summary>
/// <param name="Hours">Null when the schedule is off.</param>
public readonly record struct ScanInterval(int? Hours)
{
    public static ScanInterval Default { get; } = new(12);

    public static ScanInterval Off { get; } = new(null);

    /// <summary>The interval, or a field error for fewer than 1 hour.</summary>
    public static Result<ScanInterval> Create(int? hours) =>
        hours is < 1
            ? Result.Fail(new FieldError("ScanIntervalHours", "Enter 1 or more, or 0 to switch the scheduled scan off."))
            : new ScanInterval(hours);
}
