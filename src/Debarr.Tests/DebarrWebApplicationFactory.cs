using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Debarr.Tests;

/// <summary>Boots the app against a temporary data directory that disposal deletes, with the services <paramref name="configureServices"/> replaces.</summary>
public sealed class DebarrWebApplicationFactory(Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    // Program hands the host to the JasperFx command line, which otherwise reads the test runner's arguments as a command.
    static DebarrWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

    public string DataDirectory { get; } = TestDataDirectory.Create();

    /// <summary>The database's connection string with pooling off, so a connection a test opens on it closes the file when it is disposed.</summary>
    public string UnpooledConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "debarr.db"), Pooling = false }.ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseSetting("App:DataDir", DataDirectory)
            .ConfigureServices(services =>
            {
                // Quartz keeps one scheduler per name in the process, and tests run in parallel.
                services.Configure<QuartzSchedulerOptions>(options => options.InstanceName = Guid.NewGuid().ToString());
                configureServices?.Invoke(services);
            });

    // Program's thread disposes the host too, and can still be closing the log file.
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await TestDataDirectory.DeleteAsync(DataDirectory);
    }
}
