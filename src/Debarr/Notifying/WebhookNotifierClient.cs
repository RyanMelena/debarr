using System.Net.Http.Headers;
using System.Text.Json;
using Debarr.Playing;
using FluentResults;

namespace Debarr.Notifying;

/// <summary>Sends one notification as JSON to the configured URL.</summary>
public sealed class WebhookNotifierClient(WebhookSettings settings, IHttpClientFactory httpClientFactory) : INotifierClient
{
    /// <summary>The name of the configured <see cref="HttpClient"/> every webhook delivery uses.</summary>
    public const string HttpClientName = "notifier";

    public async Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(settings.Method.ToHttpMethod(), settings.Url)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(notification))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } },
            },
        };

        foreach (var (name, value) in settings.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? Result.Ok()
                : Result.Fail($"The webhook answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Fail(exception.Message);
        }
    }
}
