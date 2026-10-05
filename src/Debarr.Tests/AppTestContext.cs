using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.Health;
using Fisher;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests;

/// <summary>Boots the app on a temporary data directory, appends events to its store, and reads what its projections, aggregates and health checks make of them.</summary>
public abstract class AppTestContext : IAsyncLifetime
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly DebarrWebApplicationFactory _factory = new();

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    protected IDocumentStore Store => GetAppService<IDocumentStore>();

    /// <summary>The booted app's data directory, which holds debarr.db.</summary>
    protected string DataDirectory => _factory.DataDirectory;

    /// <inheritdoc cref="DebarrWebApplicationFactory.UnpooledConnectionString"/>
    protected string UnpooledConnectionString => _factory.UnpooledConnectionString;

    /// <summary>Boots the app, whose empty library runs no library scan at startup.</summary>
    public ValueTask InitializeAsync()
    {
        _ = _factory.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected T GetAppService<T>()
        where T : notnull =>
        _factory.Services.GetRequiredService<T>();

    /// <summary>Runs <paramref name="read"/> on a query session of the event store.</summary>
    protected async Task<T> ReadStoreAsync<T>(Func<IQuerySession, Task<T>> read)
    {
        await using var session = Store.QuerySession();
        return await read(session);
    }

    /// <summary>Clears the read model and its progress, and replays the events into it.</summary>
    protected async Task RebuildAsync(string readModel)
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync(readModel, CancellationToken);
    }

    /// <summary>The stream's version; null when the stream has no events.</summary>
    protected async Task<long?> StreamVersionAsync(Guid streamId)
    {
        await using var session = Store.LightweightSession();
        return (await session.Events.FetchStreamStateAsync(streamId, CancellationToken))?.Version;
    }

    protected THealthCheck HealthCheck<THealthCheck>()
        where THealthCheck : IHealthCheck =>
        _factory.Services.GetServices<IHealthCheck>().OfType<THealthCheck>().Single();

    protected Task<IReadOnlyList<HealthMessage>> CheckAsync<THealthCheck>()
        where THealthCheck : IHealthCheck =>
        HealthCheck<THealthCheck>().CheckAsync(CancellationToken);

    protected Task WaitForMessagesAsync(Func<IReadOnlyList<HealthMessage>, bool> condition) =>
        Poll.UntilAsync(async () => condition(await GetAppService<HealthCheckService>().Messages.FirstAsync().ToTask(CancellationToken)), Timeout);
}
