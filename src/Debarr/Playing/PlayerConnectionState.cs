namespace Debarr.Playing;

/// <summary>The state of a player connection: connecting for the first time, connected, or disconnected and retrying.</summary>
public abstract record PlayerConnectionState
{
    private PlayerConnectionState(DateTimeOffset since) => Since = since;

    /// <summary>When the connection entered the state.</summary>
    public DateTimeOffset Since { get; }

    /// <summary>The first attempt to connect since the connection opened.</summary>
    public sealed record Connecting(DateTimeOffset Since) : PlayerConnectionState(Since);

    /// <param name="Version">The player's name and version, as it reports them.</param>
    public sealed record Connected(string Version, DateTimeOffset Since) : PlayerConnectionState(Since);

    /// <summary>The connection was lost or never made, and it keeps retrying until it connects.</summary>
    /// <param name="Error">Why the latest attempt failed.</param>
    /// <param name="Since">When the connection was lost, or when the first attempt failed, kept across retries.</param>
    /// <param name="RetryAt">When the next attempt starts; null while an attempt runs.</param>
    public sealed record Disconnected(string Error, DateTimeOffset Since, DateTimeOffset? RetryAt) : PlayerConnectionState(Since);
}
