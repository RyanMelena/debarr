using System.Collections.Concurrent;
using System.Net;

namespace Debarr.Tests.Notifying;

/// <summary>Records each request with its body, and answers with <see cref="Respond"/>, which answers <paramref name="status"/> until a test replaces it.</summary>
public sealed class CapturingHttpMessageHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public ConcurrentQueue<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _, _) => Task.FromResult(new HttpResponseMessage(status));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request, body));
        return await Respond(request, body, cancellationToken);
    }
}
