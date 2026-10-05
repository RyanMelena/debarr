using System.Text.Json.Serialization;
using Fisher;
using FluentResults;

namespace Debarr.Notifying;

/// <summary>Every destination Debarr publishes notifications to, and the ids of the notifiers removed.</summary>
public sealed record Notifiers(IReadOnlyList<Notifier> All, IReadOnlyList<Guid> RemovedIds)
{
    /// <summary>The one stream every notifier's events are appended to.</summary>
    public static readonly Guid StreamId = new("7e2d5c19-a84b-4b3e-9f16-c0a7d3e5b842");

    /// <summary>The notifiers before their first event.</summary>
    public static Notifiers Empty { get; } = new([], []);

    public static async Task<Notifiers> ReadAsync(IQuerySession session, CancellationToken cancellationToken) =>
        await session.Events.FetchLatest<Notifiers>(StreamId, cancellationToken) ?? Empty;

    /// <summary>The stream's id, which the event store keys the aggregate on.</summary>
    public Guid Id => StreamId;

    public Notifier? Find(Guid id) => All.FirstOrDefault(notifier => notifier.Id == id);

    public bool WasRemoved(Guid id) => RemovedIds.Contains(id);

    public static Notifiers Create(NotifierAdded added) => Empty.Apply(added);

    public Notifiers Apply(NotifierAdded added) =>
        this with { All = [.. All, new Notifier(added.NotifierId, added.Name, added.Enabled, added.Settings)] };

    public Notifiers Apply(NotifierChanged changed) =>
        WithNotifier(changed.NotifierId, notifier => notifier with { Enabled = changed.Enabled, Settings = changed.Settings });

    public Notifiers Apply(NotifierRenamed renamed) => WithNotifier(renamed.NotifierId, notifier => notifier with { Name = renamed.Name });

    public Notifiers Apply(NotifierRemoved removed) =>
        this with { All = [.. All.Where(notifier => notifier.Id != removed.NotifierId)], RemovedIds = [.. RemovedIds, removed.NotifierId] };

    private Notifiers WithNotifier(Guid id, Func<Notifier, Notifier> change) =>
        this with { All = [.. All.Select(notifier => notifier.Id == id ? change(notifier) : notifier)] };
}

/// <summary>A destination Debarr publishes notifications to: its unique name, whether it is enabled, and its type's settings.</summary>
public sealed record Notifier(Guid Id, string Name, bool Enabled, NotifierSettings Settings);

/// <summary>A notifier type's own settings.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(MqttSettings), "mqtt")]
[JsonDerivedType(typeof(WebhookSettings), "webhook")]
public abstract record NotifierSettings
{
    /// <summary>A field error for each value outside its bounds.</summary>
    public abstract Result Validate();

    /// <summary>The settings, or a field error for each value outside its bounds.</summary>
    protected static Result<TSettings> Validated<TSettings>(TSettings settings)
        where TSettings : NotifierSettings =>
        settings.Validate() is { IsFailed: true } refused ? refused.ToResult<TSettings>() : settings;
}

/// <summary>An MQTT notifier's broker, credentials, topic and QoS.</summary>
public sealed record MqttSettings : NotifierSettings
{
    public const string DefaultTopicTemplate = "debarr/player/{player}/playback";

    public const QualityOfService DefaultQos = QualityOfService.AtLeastOnce;

    [JsonConstructor]
    private MqttSettings(MqttBrokerAddress broker, string? clientId, string? username, string? password, string topicTemplate, QualityOfService qos)
    {
        Broker = broker;
        ClientId = clientId;
        Username = username;
        Password = password;
        TopicTemplate = topicTemplate;
        Qos = qos;
    }

    public MqttBrokerAddress Broker { get; }

    /// <summary>Null lets MQTTnet generate one.</summary>
    public string? ClientId { get; }

    public string? Username { get; }

    public string? Password { get; }

    /// <summary>{player} is the only variable.</summary>
    public string TopicTemplate { get; }

    public QualityOfService Qos { get; }

    /// <summary>The settings with the broker an mqtt or mqtts URL names, or a field error for each value outside its bounds.</summary>
    public static Result<MqttSettings> Create(string url, string? clientId, string? username, string? password, string topicTemplate, QualityOfService qos) =>
        Validated(new MqttSettings(MqttBrokerAddress.Parse(url).ValueOrDefault, clientId, username, password, topicTemplate, qos));

    public override Result Validate()
    {
        var errors = new List<IError>();
        if (string.IsNullOrEmpty(Broker.Host) || Broker.Port is < 1 or > 65535)
        {
            errors.Add(new FieldError("Url", "Enter an mqtt or mqtts URL of a host and an optional port."));
        }

        if (string.IsNullOrWhiteSpace(TopicTemplate))
        {
            errors.Add(new FieldError(nameof(TopicTemplate), "Enter the topic template."));
        }

        if (!Enum.IsDefined(Qos))
        {
            errors.Add(new FieldError(nameof(Qos), "Enter 0, 1 or 2."));
        }

        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }
}

/// <summary>How often the broker delivers a message, with each level's MQTT number as its value.</summary>
public enum QualityOfService
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2,
}

/// <summary>A webhook notifier's URL, method and headers.</summary>
public sealed record WebhookSettings : NotifierSettings
{
    public const WebhookMethod DefaultMethod = WebhookMethod.Post;

    [JsonConstructor]
    private WebhookSettings(Uri url, WebhookMethod method, IReadOnlyList<WebhookHeader> headers)
    {
        Url = url;
        Method = method;
        Headers = headers;
    }

    /// <summary>An absolute http or https URL.</summary>
    public Uri Url { get; }

    public WebhookMethod Method { get; }

    public IReadOnlyList<WebhookHeader> Headers { get; }

    /// <summary>The settings, or a field error for each value outside its bounds.</summary>
    public static Result<WebhookSettings> Create(string url, WebhookMethod method, IReadOnlyList<WebhookHeader> headers) =>
        Validated(new WebhookSettings(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null!, method, headers));

    /// <summary>
    /// Refuses a URL that isn't an absolute http or https URL, a method outside POST, PUT and PATCH,
    /// and a header with no name or with a name another header has, compared ignoring case as HTTP compares them, beneath its row.
    /// </summary>
    public override Result Validate()
    {
        var errors = new List<IError>();
        if (Url is not { IsAbsoluteUri: true, Scheme: "http" or "https" })
        {
            errors.Add(new FieldError(nameof(Url), "Enter an absolute http or https URL."));
        }

        if (!Enum.IsDefined(Method))
        {
            errors.Add(new FieldError(nameof(Method), "Choose POST, PUT or PATCH."));
        }

        for (var index = 0; index < Headers.Count; index++)
        {
            var name = Headers[index].Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new FieldError(HeaderNameField(index), "Enter the header name."));
            }
            else if (Headers.Where((other, otherIndex) => otherIndex != index && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)).Any())
            {
                errors.Add(new FieldError(HeaderNameField(index), $"{name} is set twice."));
            }
        }

        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }

    /// <summary>The field that edits the name of the header at <paramref name="index"/>.</summary>
    public static string HeaderNameField(int index) => $"{nameof(Headers)}[{index}].{nameof(WebhookHeader.Name)}";
}

/// <summary>The HTTP method a webhook sends with.</summary>
public enum WebhookMethod
{
    Post,
    Put,
    Patch,
}

/// <summary>One header a webhook sends with each notification.</summary>
public sealed record WebhookHeader(string Name, string Value);
