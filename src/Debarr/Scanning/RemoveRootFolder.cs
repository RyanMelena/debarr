using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

public sealed record RemoveRootFolder(LocalPath Path)
{
    /// <summary>The library's stream, which Wolverine loads the aggregate from.</summary>
    public Guid LibraryId => Library.StreamId;
}

public static partial class RemoveRootFolderHandler
{
    /// <summary>Removes the root folder, and does nothing when it is already gone.</summary>
    public static IReadOnlyList<object> Handle(RemoveRootFolder command, [WriteModel(Required = false)] Library? library) =>
        library?.FindRootFolder(command.Path) is null ? [] : [new RootFolderRemoved(command.Path)];

    /// <summary>Starts the removal of the root folder's file paths once it is removed.</summary>
    /// <param name="library">The library as the handler decided from, before the commit.</param>
    public static async Task AfterCommitAsync(
        RemoveRootFolder command,
        [WriteModel(Required = false)] Library? library,
        RootFolderRemover rootFolderRemover,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (library?.FindRootFolder(command.Path) is null)
        {
            return;
        }

        try
        {
            await rootFolderRemover.EnqueueAsync(command.Path, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRemovalFailed(logger, exception, command.Path.Value);
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not start the removal of the file paths under {RootFolder}.")]
    private static partial void LogRemovalFailed(ILogger logger, Exception exception, string rootFolder);
}
