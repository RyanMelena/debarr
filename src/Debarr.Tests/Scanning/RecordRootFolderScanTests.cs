using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.EventStore;
using Fisher;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace Debarr.Tests.Scanning;

/// <summary>The library scan records a root folder's scan through Wolverine, against the operator removing that root folder at the same time.</summary>
public sealed class RecordRootFolderScanTests : IAsyncLifetime
{
    private static readonly DateTimeOffset AddedAt = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly LocalPath Media = new(Path.Combine(Path.GetTempPath(), "debarr-media"));
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    private readonly Guid _libraryScanId = Guid.CreateVersion7();
    private readonly Interleaver _interleaver = new();
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options => options.Listeners.Add(_interleaver)));
        Assert.True((await SendAsync(new AddRootFolder(Media, AddedAt))).IsSuccess);
        Assert.True((await SendAsync(new StartLibraryScan(_libraryScanId, StartedAt))).IsSuccess);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_root_folder_scan_records_when_the_library_scan_started_on_the_root_folder()
    {
        Assert.True((await SendAsync(new RecordRootFolderScan(_libraryScanId, Media, "The folder is missing."))).IsSuccess);

        Assert.Equal([typeof(LibraryScanStarted), typeof(RootFolderScanned)], await ReadLibraryScanEventTypesAsync());
        Assert.Equal([(Media.Value, (DateTimeOffset?)StartedAt, "The folder is missing.")], await ReadRootFolderRowsAsync());
    }

    [Fact]
    public async Task A_root_folder_scan_that_meets_the_removal_of_its_root_folder_leaves_it_removed()
    {
        _interleaver.Before<RootFolderScanned>(() => SendAsync(new RemoveRootFolder(Media)));

        var recorded = await SendAsync(new RecordRootFolderScan(_libraryScanId, Media, null));

        Assert.True(recorded.IsSuccess);
        Assert.Equal([typeof(LibraryScanStarted), typeof(RootFolderScanned)], await ReadLibraryScanEventTypesAsync());
        Assert.Empty(await ReadRootFolderRowsAsync());
    }

    private async Task<Result> SendAsync(object command) =>
        await _host.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(command, CancellationToken);

    private async Task<List<Type>> ReadLibraryScanEventTypesAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return [.. (await session.Events.FetchStreamAsync(_libraryScanId, token: CancellationToken)).Select(stored => stored.Data.GetType())];
    }

    private async Task<List<(string Path, DateTimeOffset? LastScannedAt, string? ScanError)>> ReadRootFolderRowsAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return [.. (await session.ReadRootFoldersAsync(CancellationToken)).Select(root => (root.RootFolder.Path.Value, root.LastScan?.StartedAt, root.LastScan?.Error))];
    }
}
