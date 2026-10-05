using System.Reactive.Concurrency;
using Debarr.Activity;
using Debarr.Appearance;
using Debarr.Components;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Health;
using Debarr.Hosting;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using JasperFx;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Quartz;
using Serilog.Extensions.Logging;
using IScheduler = System.Reactive.Concurrency.IScheduler;

var builder = WebApplication.CreateBuilder(args);

// Serves the _framework and _content assets when the app runs from source outside Development.
builder.WebHost.UseStaticWebAssets();

var sources = builder.Configuration.Sources;

// Environment variables reach settings only through the DEBARR__ prefix.
foreach (var environmentSource in sources.OfType<EnvironmentVariablesConfigurationSource>().ToList())
{
    sources.Remove(environmentSource);
}

builder.Configuration.AddEnvironmentVariables(EnvironmentOverrides.Prefix);
var debarrEnvironment = sources[^1];

// The data directory holds config.json, so it comes from the sources loaded before the file.
var appOptions = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
var dataDir = Path.GetFullPath(appOptions.DataDir);
Directory.CreateDirectory(dataDir);

// The image's TMPDIR is a folder in the data directory, which starts empty when a volume is mounted there.
Directory.CreateDirectory(Path.GetTempPath());

var logFiles = new LogFileDirectory(Path.Combine(dataDir, "logs"));
builder.Services.AddSingleton(logFiles);

var logFileLogger = logFiles.CreateLogger();

// Registered as a plain provider, so the Logging:LogLevel keys filter what reaches the file, as they do the console.
// The container disposes the provider, which closes the file.
builder.Services.AddSingleton<ILoggerProvider>(_ => new SerilogLoggerProvider(logFileLogger, dispose: true));

var hostSettings = new JsonConfigurationSource
{
    Path = Path.Combine(dataDir, "config.json"),
    Optional = true,
    ReloadOnChange = false,
};
hostSettings.ResolveFileProvider();

// config.json sits before DEBARR__ so environment variables override the file.
sources.Insert(sources.IndexOf(debarrEnvironment), hostSettings);

// Command-line arguments override every other source.
foreach (var commandLine in sources.OfType<CommandLineConfigurationSource>().ToList())
{
    sources.Remove(commandLine);
    sources.Add(commandLine);
}

builder.Services.Configure<AppOptions>(options => options.DataDir = dataDir);

builder.Services.AddOptions<ServerOptions>()
    .BindConfiguration(ServerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart()
    .PostConfigure(options => options.UrlBase = ServerOptions.NormalizeUrlBase(options.UrlBase));

builder.Services.AddOptions<FfmpegOptions>()
    .BindConfiguration(FfmpegOptions.SectionName);

builder.Services.AddSingleton<HostSettingsFile>();
builder.Services.AddSingleton<IActivitySource>(services => services.GetRequiredService<HostSettingsFile>());

builder.Services.AddDataProtection()
    .SetApplicationName("debarr")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

var database = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDir, "debarr.db") }.ToString();

builder.Host.ApplyJasperFxExtensions();

builder.Services.AddEventStore(database);

builder.Services.AddSingleton<ActivityFeed>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScanning().AddDetecting().AddPlaying().AddNotifying().AddAppearance();

builder.Services.AddHostedService(services => services.GetRequiredService<DetectionOrchestrator>());
builder.Services.AddHostedService(services => services.GetRequiredService<RootFolderRemover>());
builder.Services.AddHostedService<LibraryStartupService>();
builder.Services.AddHostedService(services => services.GetRequiredService<FolderWatcher>());

// Starts before PlayerConnectionService, so it hears every playback of the connections that service opens.
builder.Services.AddHostedService(services => services.GetRequiredService<PlaybackHandler>());
builder.Services.AddHostedService(services => services.GetRequiredService<PlayerConnectionService>());

// Starts after PlayerConnectionService, whose connection states the player check reads, and before Quartz, so it hears the first scan.
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddSingleton<IActivitySource>(services => services.GetRequiredService<HealthCheckService>());
builder.Services.AddHostedService(services => services.GetRequiredService<HealthCheckService>());

builder.Services.AddQuartz(quartz =>
{
    quartz.UseInMemoryStore();

    // Shutdown cancels the running scans and waits for them to unwind.
    quartz.ConfigureScheduler(options => options.ShutdownJobInterruption = ShutdownJobInterruption.WhenWaitingForJobs);
    quartz.AddScanJobs();
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// Logs an exception from scheduled work, which on a thread pool thread ends the process.
builder.Services.AddSingleton<IScheduler>(services =>
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Debarr.Scheduler");
    return DefaultScheduler.Instance.Catch<Exception>(exception =>
    {
        LogScheduledWorkFailed(logger, exception);
        return true;
    });
});

builder.Services.AddHealthChecks();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// A startup failure still disposes the host, which closes the log file.
await using var app = builder.Build();

var server = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
app.Urls.Add($"http://{server.BindAddress}:{server.Port}");

app.UsePathBase(server.UrlBase);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Follows UsePathBase so endpoint matching sees the path with the URL base removed.
app.UseRouting();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/healthz");
app.MapGet(
    "/system/logs/{fileName}",
    (string fileName, LogFileDirectory logFiles) =>
        logFiles.Find(fileName) is { } file ? Results.File(file.FullName, "text/plain", file.Name) : Results.NotFound());
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

return await app.RunJasperFxCommands(args);

public partial class Program
{
    [LoggerMessage(LogLevel.Error, "Scheduled work failed.")]
    private static partial void LogScheduledWorkFailed(ILogger logger, Exception exception);
}
