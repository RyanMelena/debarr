using Debarr.EventStore;
using Debarr.Playing;

namespace Debarr.Tests.Playing;

public sealed class RecordDeliveryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 20, 15, 0, TimeSpan.Zero);

    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await TestHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_delivery_of_a_playback_that_does_not_exist_is_refused_and_writes_nothing()
    {
        var playbackId = Guid.NewGuid();
        var delivery = new Delivery(Guid.NewGuid(), TestPlayback.AutomationId, "automation", Now, TimeSpan.FromMilliseconds(12), new DeliveryOutcome.Succeeded());

        var recorded = await _host.Runtime.SendCommandAsync(new RecordDelivery(playbackId, delivery), CancellationToken);

        Assert.Equal("The playback no longer exists.", Assert.Single(recorded.Errors).Message);
        await using var session = _host.Store.QuerySession();
        Assert.Null(await session.Events.FetchStreamStateAsync(playbackId, CancellationToken));
    }
}
