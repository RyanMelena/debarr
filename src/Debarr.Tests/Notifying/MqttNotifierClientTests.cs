using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Debarr.Notifying;
using Debarr.Tests.Playing;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Debarr.Tests.Notifying;

public sealed class MqttNotifierClientTests : IAsyncLifetime
{
    private readonly int _port = GetFreePort();
    private readonly List<ValidatingConnectionEventArgs> _connections = [];
    private readonly List<MqttApplicationMessage> _messages = [];
    private readonly MqttServer _server;

    public MqttNotifierClientTests()
    {
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(_port)
            .Build();
        _server = new MqttServerFactory().CreateMqttServer(options);
        _server.ValidatingConnectionAsync += arguments =>
        {
            lock (_connections)
            {
                _connections.Add(arguments);
            }

            if (arguments.Password == "wrong")
            {
                arguments.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            }

            return Task.CompletedTask;
        };
        _server.InterceptingPublishAsync += arguments =>
        {
            lock (_messages)
            {
                _messages.Add(arguments.ApplicationMessage);
            }

            return Task.CompletedTask;
        };
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _server.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task The_notification_is_published_to_the_player_topic_with_the_snapshot_payload()
    {
        var client = new MqttNotifierClient(CreateSettings(), new MqttClientFactory());

        var result = await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.True(result.IsSuccess);
        var connection = Assert.Single(_connections);
        Assert.Equal(("debarr-test", "automation", "secret"), (connection.ClientId, connection.UserName, connection.Password));
        var message = Assert.Single(_messages);
        Assert.Equal("home/theater/playback", message.Topic);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, message.QualityOfServiceLevel);
        Assert.False(message.Retain);
        Assert.Equal(Encoding.UTF8.GetBytes(NotificationTests.ReadSnapshot()), message.Payload.ToArray());
    }

    [Fact]
    public async Task A_broker_that_refuses_the_credentials_fails_the_delivery()
    {
        var settings = CreateSettings(password: "wrong");
        var client = new MqttNotifierClient(settings, new MqttClientFactory());

        var result = await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal("The broker refused the connection: BadUserNameOrPassword.", Assert.Single(result.Errors).Message);
        Assert.Empty(_messages);
    }

    [Fact]
    public async Task A_refused_connection_fails_with_its_message()
    {
        var settings = CreateSettings(port: GetFreePort());
        var client = new MqttNotifierClient(settings, new MqttClientFactory());

        var result = await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.True(result.IsFailed);
        Assert.NotEmpty(Assert.Single(result.Errors).Message);
    }

    private MqttSettings CreateSettings(int? port = null, string password = "secret") =>
        MqttSettings.Create($"mqtt://127.0.0.1:{port ?? _port}", "debarr-test", "automation", password, "home/{player}/playback", QualityOfService.ExactlyOnce).Value;

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
