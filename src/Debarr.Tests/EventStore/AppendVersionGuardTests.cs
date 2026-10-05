using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;

namespace Debarr.Tests.EventStore;

public sealed class AppendVersionGuardTests : IAsyncLifetime
{
    private static readonly FileHash Alpha = new("2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192");
    private static readonly LocalPath AlphaPath = new("/media/a.mkv");
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync();
        Assert.True((await _host.Runtime.SendCommandAsync(new AddFilePath(Alpha, AlphaPath, new FileStat(1, Start), Start), CancellationToken)).IsSuccess);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_append_that_states_no_version_is_refused_and_writes_nothing()
    {
        await using (var session = _host.Store.LightweightSession())
        {
            session.Events.Append(Alpha.StreamId, new FilePathRemoved(Alpha, AlphaPath, Start));
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveChangesAsync(CancellationToken));
            Assert.Equal($"An append to stream {Alpha.StreamId} states no expected version.", refused.Message);
        }

        await using var query = _host.Store.QuerySession();
        Assert.Equal(2, (await query.Events.FetchStreamStateAsync(Alpha.StreamId, CancellationToken))!.Version);
        Assert.Single((await query.LoadAsync<MediaRow>(Alpha.StreamId, CancellationToken))!.FilePaths);
    }
}
