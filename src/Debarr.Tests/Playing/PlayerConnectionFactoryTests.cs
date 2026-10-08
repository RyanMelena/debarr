using System.Net;
using System.Net.Sockets;
using Debarr.Playing;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace Debarr.Tests.Playing;

public sealed class PlayerConnectionFactoryTests
{
    private static readonly PlayerConnectionFactory Factory = new(TimeProvider.System, NullLogger<KodiPlayerConnection>.Instance);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void A_player_type_with_no_connection_is_refused()
    {
        var exception = Assert.Throws<NotSupportedException>(() => Factory.Create(new Player(Guid.NewGuid(), "Lounge", true, new UnknownEndpoint(), [], [])));

        Assert.Equal("The player endpoint UnknownEndpoint has no connection.", exception.Message);
    }

    [Fact]
    public async Task A_test_returns_the_player_version_and_closes_the_connection()
    {
        await using var server = new FakeKodiServer();

        var test = await Factory.TestAsync(KodiPlayer(server.Port), CancellationToken);

        Assert.Equal("Kodi 21.2 (JSON-RPC 13.5.0)", test.Value);
        await server.WaitForDisconnectionAsync();
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task A_test_against_a_closed_port_fails_with_the_connection_error()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var test = await Factory.TestAsync(KodiPlayer(port), CancellationToken);

        // The socket error's wording comes from the operating system.
        Assert.StartsWith($"Connecting to 127.0.0.1:{port} failed: ", Assert.Single(test.Errors).Message);
    }

    private static Player KodiPlayer(int port) =>
        new(Guid.NewGuid(), "Theater", true, KodiEndpoint.Create("127.0.0.1", port, 60, 10).Value, [], []);

    private sealed record UnknownEndpoint : PlayerEndpoint
    {
        public override PlayerPath ToPlayerPath(string path) => new(path);

        public override Result Validate() => Result.Ok();
    }
}
