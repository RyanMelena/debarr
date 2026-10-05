namespace Debarr.Tests.Notifying;

/// <summary>Hands out clients over one handler.</summary>
public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
