using System.Reactive;
using System.Reactive.Linq;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Health;
using Fisher;

namespace Debarr.Scanning;

/// <summary>
/// Finds a library with no enabled root, each enabled root whose last scan failed,
/// and each disabled or removed root folder whose file paths a removal cut short left behind.
/// </summary>
public sealed class RootFolderHealthCheck(IDocumentStore store, ReadModelChangeListener readModelChangeListener, RootFolderRemover rootFolderRemover) : IHealthCheck
{
    private const string Href = "settings/library";
    private const string LinkText = "Fix in Settings > Library";

    public HealthCheckKind Kind => HealthCheckKind.RootFolder;

    public IObservable<Unit> Triggers { get; } = readModelChangeListener
        .ChangesTo(nameof(Library), LibraryScanSummaryRowProjection.ReadModel, StoredFilePathProjection.ReadModel)
        .Select(_ => Unit.Default)
        .Merge(rootFolderRemover.ActivityEvents.OfType<RootFolderRemovalFinishedEvent>().Select(_ => Unit.Default));

    public async Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var library = await Library.ReadAsync(session, cancellationToken);
        var leftBehind = await ReadLeftBehindAsync(session, library, cancellationToken);
        var roots = (await session.ReadRootFoldersAsync(cancellationToken)).Where(root => root.RootFolder.Enabled).ToList();

        List<HealthMessage> messages = roots.Count == 0
            ? [new HealthMessage(HealthSeverity.Warning, "No root folder is enabled, so Debarr has no video files to detect.", Href, LinkText)]
            :
            [
                .. roots
                    .Where(root => root.LastScan?.Error is not null)
                    .Select(root => new HealthMessage(HealthSeverity.Error, $"Root folder {root.RootFolder.Path.Value} can't be scanned: {root.LastScan!.Error}", Href, LinkText, root.LastScan.StartedAt)),
            ];
        return [.. messages, .. leftBehind];
    }

    /// <summary>
    /// A warning for each disabled or removed root folder that still holds file paths outside every enabled root folder,
    /// unless its removal is queued or running.
    /// </summary>
    private async Task<List<HealthMessage>> ReadLeftBehindAsync(IQuerySession session, Library library, CancellationToken cancellationToken)
    {
        var enabled = library.RootFolders.Where(root => root.Enabled).Select(root => new DirectoryInfo(root.Path.Value)).ToList();
        IEnumerable<(LocalPath Path, string Was)> candidates =
        [
            .. library.RootFolders.Where(root => !root.Enabled).Select(root => (root.Path, "is disabled")),
            .. library.RemovedRootFolders.Select(path => (path, "was removed")),
        ];

        List<HealthMessage> messages = [];
        foreach (var (path, was) in candidates)
        {
            var folder = new DirectoryInfo(path.Value);
            if (enabled.Any(folder.IsSameOrUnder))
            {
                continue;
            }

            var count = await session.CountUnderAsync(folder.FullName, cancellationToken);
            foreach (var inside in enabled.Where(root => root.IsSameOrUnder(folder)))
            {
                count -= await session.CountUnderAsync(inside.FullName, cancellationToken);
            }

            // Read last, so a removal queued since the counts were read leaves no warning.
            if (count > 0 && !rootFolderRemover.IsRemoving(path))
            {
                var text = count == 1
                    ? $"1 file path remains under root folder {path.Value}, which {was}. Scan Now removes it."
                    : $"{count.ToCountText()} file paths remain under root folder {path.Value}, which {was}. Scan Now removes them.";
                messages.Add(new HealthMessage(HealthSeverity.Warning, text, "system/tasks", "Fix in System > Tasks"));
            }
        }

        return messages;
    }
}
