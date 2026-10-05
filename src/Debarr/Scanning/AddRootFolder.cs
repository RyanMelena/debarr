using Debarr.Extensions;
using FluentResults;
using Quartz;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <param name="Path">The canonical local path of a folder that exists, with no trailing separator.</param>
public sealed record AddRootFolder(LocalPath Path, DateTimeOffset AddedAt)
{
    /// <summary>The library's stream, which Wolverine loads the aggregate from.</summary>
    public Guid LibraryId => Library.StreamId;

    /// <summary>The command that adds the typed folder at its canonical form, or a refusal on <see cref="Path"/> of a relative path or a missing folder.</summary>
    public static Result<AddRootFolder> Parse(string typedPath, DateTimeOffset addedAt)
    {
        typedPath = typedPath.Trim();
        if (!System.IO.Path.IsPathFullyQualified(typedPath))
        {
            return Result.Fail(new FieldError(nameof(Path), "Enter a full path."));
        }

        var folder = new DirectoryInfo(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(typedPath)));
        return folder.Exists
            ? new AddRootFolder(new LocalPath(folder.FullName), addedAt)
            : Result.Fail(new FieldError(nameof(Path), $"Debarr cannot find the folder {folder.FullName}."));
    }
}

public static partial class AddRootFolderHandler
{
    /// <summary>Refuses a folder that is the same as, inside or contains another root folder, enabled or not, and names the other root folder in the refusal of <see cref="AddRootFolder.Path"/>.</summary>
    public static Result Validate(AddRootFolder command, Library? library)
    {
        var folder = new DirectoryInfo(command.Path.Value);
        foreach (var other in (library ?? Library.Default).RootFolders.Select(rootFolder => new DirectoryInfo(rootFolder.Path.Value)))
        {
            var inside = folder.IsSameOrUnder(other);
            var contains = other.IsSameOrUnder(folder);
            if (inside && contains)
            {
                return Result.Fail(new FieldError(nameof(command.Path), $"{other.FullName} is already a root folder."));
            }

            if (inside || contains)
            {
                return Result.Fail(new FieldError(nameof(command.Path), $"{folder.FullName} {(inside ? "is inside" : "contains")} the root folder {other.FullName}."));
            }
        }

        return Result.Ok();
    }

    public static RootFolderAdded Handle(AddRootFolder command, [WriteModel(Required = false)] Library? library) => new(command.Path, command.AddedAt);

    /// <summary>Runs a library scan once the root folder is added.</summary>
    public static async Task AfterCommitAsync(AddRootFolder command, ISchedulerFactory schedulerFactory, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await LibraryScanJob.TriggerNowAsync(await schedulerFactory.GetScheduler(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogScanFailed(logger, exception, command.Path.Value);
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not start a library scan after adding {RootFolder}.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception, string rootFolder);
}
