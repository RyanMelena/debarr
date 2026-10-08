using System.Net.Sockets;
using System.Net;
using Debarr.EventStore;
using Debarr.Health;
using Debarr.Playing;
using Debarr.Tests.Extensions;
using Fisher;
using Wolverine.Runtime;

namespace Debarr.Tests.Playing;

public sealed class PlayerConnectionHealthCheckTests : AppTestContext
{
    [Fact]
    public async Task An_enabled_disconnected_player_is_a_warning_with_its_error_since_it_disconnected()
    {
        await SavePlayerAsync("Bedroom", ClosedPort(), enabled: true);

        IReadOnlyList<HealthMessage> messages = [];
        await Poll.UntilAsync(async () => (messages = await CheckAsync<PlayerConnectionHealthCheck>()).Count > 0, Timeout);

        var message = Assert.Single(messages);
        Assert.Equal((HealthSeverity.Warning, "settings/players"), (message.Severity, message.Href));
        Assert.StartsWith("Bedroom is disconnected, so its playback sends nothing: Connecting to 127.0.0.1:", message.Text);
        Assert.NotNull(message.Since);
    }

    [Fact]
    public async Task A_connected_player_finds_nothing()
    {
        await using var kodi = new FakeKodiServer();
        await SavePlayerAsync("Theater", kodi.Port, enabled: true);
        await kodi.WaitForConnectionAsync();

        Assert.Empty(await CheckAsync<PlayerConnectionHealthCheck>());
    }

    [Fact]
    public async Task A_disabled_player_finds_nothing()
    {
        await SavePlayerAsync("Bedroom", ClosedPort(), enabled: false);

        Assert.Empty(await CheckAsync<PlayerConnectionHealthCheck>());
    }

    [Fact]
    public async Task A_commit_to_the_players_triggers_the_check()
    {
        var triggers = 0;
        using var subscription = HealthCheck<PlayerConnectionHealthCheck>().Triggers.Subscribe(_ => Interlocked.Increment(ref triggers));
        var before = Volatile.Read(ref triggers);

        await using (var session = GetAppService<IDocumentStore>().LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Players.StreamId, new PlayerAdded(Guid.NewGuid(), "Bedroom", false, KodiEndpoint.Create("kodi.local", 9090, 60, 10).Value, [], []));
            await session.SaveChangesAsync(CancellationToken);
        }

        await Poll.UntilAsync(() => Volatile.Read(ref triggers) > before, Timeout);
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private Task SavePlayerAsync(string name, int port, bool enabled) =>
        GetAppService<IWolverineRuntime>().SendCommandAsync(
            new SavePlayer(Guid.NewGuid(), name, enabled, KodiEndpoint.Create("127.0.0.1", port, 60, 10).Value, [], []),
            CancellationToken);
}
