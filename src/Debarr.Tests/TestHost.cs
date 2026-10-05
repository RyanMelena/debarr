using Debarr.Appearance;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Fisher;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Wolverine.Runtime;

namespace Debarr.Tests;

/// <summary>
/// The app's services on a temporary data directory, registered as Program.cs registers them.
/// A test adds the hosted services it exercises, replaces the services it doubles, and starts <see cref="Scheduler"/> when its triggers should fire.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    private readonly IHost _host;

    private TestHost(IHost host, string dataDirectory, IScheduler scheduler)
    {
        _host = host;
        DataDirectory = dataDirectory;
        Scheduler = scheduler;
    }

    public string DataDirectory { get; }

    /// <summary>The database's connection string with pooling off, so a connection a test opens on it closes the file when it is disposed.</summary>
    public string UnpooledConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "debarr.db"), Pooling = false }.ToString();

    public IServiceProvider Services => _host.Services;

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    public IWolverineRuntime Runtime => Services.GetRequiredService<IWolverineRuntime>();

    /// <summary>The Quartz scheduler over the scan jobs, which disposal shuts down after any running job.</summary>
    public IScheduler Scheduler { get; }

    /// <summary>Builds and starts the host.</summary>
    /// <param name="configure">Adds to or replaces the app's registrations, such as a fake clock, a detector double, a Fisher listener or a hosted service.</param>
    /// <param name="logging">Adds the test's logger provider.</param>
    public static async Task<TestHost> StartAsync(Action<IServiceCollection>? configure = null, Action<ILoggingBuilder>? logging = null)
    {
        var dataDirectory = TestDataDirectory.Create();
        var builder = Host.CreateApplicationBuilder();
        logging?.Invoke(builder.Logging);
        builder.Services
            .AddEventStore(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "debarr.db") }.ToString())
            .AddSingleton(TimeProvider.System)
            .AddScanning().AddDetecting().AddPlaying().AddNotifying().AddAppearance()
            .AddQuartz(quartz =>
            {
                quartz.UseInMemoryStore();
                quartz.ConfigureScheduler(options =>
                {
                    options.ShutdownJobInterruption = ShutdownJobInterruption.WhenWaitingForJobs;

                    // Quartz keeps one scheduler per name in the process, and tests run in parallel.
                    options.InstanceName = Guid.NewGuid().ToString();
                });
                quartz.AddScanJobs();
            });
        configure?.Invoke(builder.Services);

        var host = builder.Build();
        var cancellationToken = TestContext.Current.CancellationToken;
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        await host.StartAsync(cancellationToken);
        return new TestHost(host, dataDirectory, scheduler);
    }

    public async ValueTask DisposeAsync()
    {
        await Scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
        await TestDataDirectory.DeleteAsync(DataDirectory);
    }
}
