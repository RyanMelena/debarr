using System.Net;
using System.Net.Sockets;
using Debarr.Notifying;
using Debarr.Tests.Playing;

namespace Debarr.Tests.Notifying;

public sealed class WebhookNotifierClientTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_carries_the_method_the_headers_and_the_snapshot_body()
    {
        using var handler = new CapturingHttpMessageHandler();
        var client = new WebhookNotifierClient(CreateSettings(), new StubHttpClientFactory(handler));

        var result = await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.True(result.IsSuccess);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://automation.lan:9099/debarr", request.RequestUri!.ToString());
        Assert.Equal("application/json; charset=utf-8", request.Content!.Headers.ContentType!.ToString());
        Assert.Equal("Bearer token", Assert.Single(request.Headers.GetValues("Authorization")));
        Assert.Equal("playback", Assert.Single(request.Headers.GetValues("X-Debarr-Source")));
        Assert.Equal(NotificationTests.ReadSnapshot(), body);
    }

    [Fact]
    public async Task The_configured_method_is_used()
    {
        using var handler = new CapturingHttpMessageHandler();
        var settings = CreateSettings(method: WebhookMethod.Put);
        var client = new WebhookNotifierClient(settings, new StubHttpClientFactory(handler));

        await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.Equal(HttpMethod.Put, Assert.Single(handler.Requests).Request.Method);
    }

    [Fact]
    public async Task A_server_error_fails_with_its_status()
    {
        using var handler = new CapturingHttpMessageHandler(HttpStatusCode.InternalServerError);
        var client = new WebhookNotifierClient(CreateSettings(), new StubHttpClientFactory(handler));

        var result = await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal("The webhook answered 500 Internal Server Error.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task A_refused_connection_fails_with_its_message()
    {
        using var handler = new SocketsHttpHandler();
        var settings = CreateSettings($"http://127.0.0.1:{GetClosedPort()}/debarr");
        var client = new WebhookNotifierClient(settings, new StubHttpClientFactory(handler));

        var result = await client.SendAsync(NotificationTests.Sample, CancellationToken);

        Assert.True(result.IsFailed);
        Assert.NotEmpty(Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task A_cancelled_send_throws()
    {
        using var handler = new CapturingHttpMessageHandler();
        var client = new WebhookNotifierClient(CreateSettings(), new StubHttpClientFactory(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(NotificationTests.Sample, new CancellationToken(canceled: true)));
    }

    /// <summary>A loopback port with no listener, so a connection to it is refused.</summary>
    private static int GetClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static WebhookSettings CreateSettings(string url = "http://automation.lan:9099/debarr", WebhookMethod method = WebhookMethod.Post) =>
        WebhookSettings.Create(url, method, [new WebhookHeader("Authorization", "Bearer token"), new WebhookHeader("X-Debarr-Source", "playback")]).Value;
}
