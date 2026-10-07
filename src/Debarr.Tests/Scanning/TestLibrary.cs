using Debarr.Scanning;
using Debarr.Tests.Extensions;
using Fisher;

namespace Debarr.Tests.Scanning;

/// <summary>Root folders, library events and library scans for tests, appended to their streams as the app appends them.</summary>
public static class TestLibrary
{
    /// <summary>
    /// Adds a root folder at <paramref name="path"/>, disabled when asked, with a finished library scan that started at <paramref name="scannedAt"/> when one is given.
    /// It was added at <paramref name="addedAt"/>, or at the Unix epoch, before every scan.
    /// </summary>
    public static async Task AddRootFolderAsync(
        IDocumentStore store,
        string path,
        CancellationToken cancellationToken,
        bool enabled = true,
        DateTimeOffset? scannedAt = null,
        string? scanError = null,
        DateTimeOffset? addedAt = null)
    {
        var rootFolder = new LocalPath(path);
        List<object> events = [new RootFolderAdded(rootFolder, addedAt ?? DateTimeOffset.UnixEpoch)];
        if (!enabled)
        {
            events.Add(new RootFolderDisabled(rootFolder));
        }

        await AppendAsync(store, events, cancellationToken);
        if (scannedAt is { } startedAt)
        {
            await AppendLibraryScanAsync(store, Guid.CreateVersion7(), [new LibraryScanStarted(startedAt), new RootFolderScanned(rootFolder, startedAt, scanError), new LibraryScanEnded(startedAt, new LibraryScanCounts(0, 0, 0, 0, 0), new LibraryScanOutcome.Finished())], cancellationToken);
        }
    }

    public static async Task AppendAsync(IDocumentStore store, IEnumerable<object> events, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        await session.Events.AppendAtCurrentVersionAsync(Library.StreamId, events.ToArray());
        await session.SaveChangesAsync(cancellationToken);
    }

    public static async Task AppendLibraryScanAsync(IDocumentStore store, Guid libraryScanId, IEnumerable<object> events, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        await session.Events.AppendAtCurrentVersionAsync(libraryScanId, events.ToArray());
        await session.SaveChangesAsync(cancellationToken);
    }
}
