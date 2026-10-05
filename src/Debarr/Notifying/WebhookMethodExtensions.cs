namespace Debarr.Notifying;

public static class WebhookMethodExtensions
{
    public static HttpMethod ToHttpMethod(this WebhookMethod method) => method switch
    {
        WebhookMethod.Post => HttpMethod.Post,
        WebhookMethod.Put => HttpMethod.Put,
        WebhookMethod.Patch => HttpMethod.Patch,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };
}
