using System.Net;
using System.Text.Json;
using Debarr.Detecting;
using Debarr.Health;
using Debarr.Playing;
using Debarr.Scanning;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Debarr.Tests;

public partial class ProgramTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/settings/general")]
    [InlineData("/system/status")]
    [InlineData("/system/logs")]
    public async Task Boots_and_serves_each_page(string path)
    {
        await using var factory = new DebarrWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Startup_creates_the_database_in_wal_mode_with_the_default_settings_and_restarts_on_it()
    {
        await using var factory = new DebarrWebApplicationFactory();
        factory.CreateClient().Dispose();
        await using var restarted = factory.WithWebHostBuilder(_ => { });
        restarted.CreateClient().Dispose();

        await using var session = restarted.Services.GetRequiredService<IDocumentStore>().QuerySession();
        var journalMode = Assert.Single(await session.AdvancedSql.QueryAsync<string>("select journal_mode from pragma_journal_mode()", TestContext.Current.CancellationToken));
        var library = (await Library.ReadAsync(session, TestContext.Current.CancellationToken)).Settings;
        var detection = await DetectionSettings.ReadAsync(session, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(factory.DataDirectory, "debarr.db")));
        Assert.Equal("wal", journalMode);
        Assert.Equal("mkv mp4 m4v avi mov ts m2ts wmv webm mpg mpeg", library.VideoExtensions.ToString());
        Assert.Equal(DetectionSettings.Default, detection);
    }

    [Fact]
    public async Task The_processes_start_in_order_and_the_scheduler_starts_after_them()
    {
        string[] startOrder =
        [
            nameof(DetectionOrchestrator), nameof(RootFolderRemover), nameof(LibraryStartupService), nameof(FolderWatcher),
            nameof(PlaybackHandler), nameof(PlayerConnectionService), nameof(HealthCheckService), "QuartzHostedService",
        ];
        await using var factory = new DebarrWebApplicationFactory();

        var hostedServices = factory.Services.GetServices<IHostedService>().Select(hostedService => hostedService.GetType().Name);

        Assert.Equal(startOrder, hostedServices.Where(startOrder.Contains));
    }

    [Fact]
    public async Task The_chapters_register_one_health_check_of_each_kind()
    {
        await using var factory = new DebarrWebApplicationFactory();

        var kinds = factory.Services.GetServices<IHealthCheck>().Select(healthCheck => healthCheck.Kind).Order();

        Assert.Equal(Enum.GetValues<HealthCheckKind>(), kinds);
    }

    [Fact]
    public void No_project_references_entity_framework_core()
    {
        using var dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Debarr.Tests.deps.json")));
        var libraries = dependencies.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name).ToList();

        Assert.Contains(libraries, library => library.StartsWith("Fisher/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, library => library.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_base_href_carries_the_url_base()
    {
        await using var factory = new DebarrWebApplicationFactory();
        using var urlBaseFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("Server:UrlBase", "/movies"));
        using var client = urlBaseFactory.CreateClient();

        var page = await client.GetStringAsync("/movies/", TestContext.Current.CancellationToken);

        Assert.Contains("<base href=\"/movies/\"", page);
    }

    [Fact]
    public async Task The_health_endpoint_returns_healthy()
    {
        await using var factory = new DebarrWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(Path.Combine(factory.DataDirectory, "keys")));
        Assert.False(File.Exists(Path.Combine(factory.DataDirectory, "config.json")));
    }

    [Fact]
    public async Task A_log_line_reaches_the_log_file_in_the_data_directory()
    {
        await using var factory = new DebarrWebApplicationFactory();
        var marker = Guid.NewGuid().ToString();

        LogMarker(factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Debarr.Tests"), LogLevel.Information, marker);

        var lines = (await ReadLogFileAsync(factory)).Split('\n', StringSplitOptions.TrimEntries);
        Assert.Contains(lines, line => line.EndsWith($"[INF] Debarr.Tests: {marker}.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_configured_log_level_filters_the_log_file()
    {
        await using var factory = new DebarrWebApplicationFactory();

        // Settings > General saves the level to config.json, which overrides appsettings.json.
        await File.WriteAllTextAsync(
            Path.Combine(factory.DataDirectory, "config.json"),
            """{"Logging":{"LogLevel":{"Default":"Warning"}}}""",
            TestContext.Current.CancellationToken);
        var hidden = $"hidden-{Guid.NewGuid()}";
        var shown = $"shown-{Guid.NewGuid()}";

        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Debarr.Tests");
        LogMarker(logger, LogLevel.Information, hidden);
        LogMarker(logger, LogLevel.Warning, shown);

        var content = await ReadLogFileAsync(factory);
        Assert.Contains(shown, content);
        Assert.DoesNotContain(hidden, content);
    }

    [Fact]
    public async Task A_boot_logs_none_of_the_chatter_of_wolverine_and_quartz_and_keeps_their_warnings()
    {
        await using var factory = new DebarrWebApplicationFactory();
        var hidden = $"hidden-{Guid.NewGuid()}";
        var shown = $"shown-{Guid.NewGuid()}";

        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Wolverine.Runtime.WolverineRuntime");
        LogMarker(logger, LogLevel.Information, hidden);
        LogMarker(logger, LogLevel.Warning, shown);

        var lines = (await ReadLogFileAsync(factory)).Split('\n', StringSplitOptions.TrimEntries);
        Assert.Contains(lines, line => line.Contains("[INF] Microsoft.Hosting.Lifetime: Application started.", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("[INF] Wolverine.", StringComparison.Ordinal) || line.Contains("[INF] Quartz.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith($"[WRN] Wolverine.Runtime.WolverineRuntime: {shown}.", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(hidden, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_logs_page_lists_the_files_and_shows_the_newest()
    {
        await using var factory = new DebarrWebApplicationFactory();
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString();
        LogMarker(factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Debarr.Tests"), LogLevel.Information, marker);

        var page = await client.GetStringAsync("/system/logs", TestContext.Current.CancellationToken);

        Assert.Contains(Path.GetFileName(GetLogFilePath(factory)), page);
        Assert.Contains(marker, page);
    }

    [Fact]
    public async Task The_current_log_file_downloads_and_other_names_are_not_found()
    {
        await using var factory = new DebarrWebApplicationFactory();
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString();
        LogMarker(factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Debarr.Tests"), LogLevel.Information, marker);
        var fileName = Path.GetFileName(GetLogFilePath(factory));

        var response = await client.GetAsync($"/system/logs/{fileName}", TestContext.Current.CancellationToken);
        var database = await client.GetAsync("/system/logs/debarr.db", TestContext.Current.CancellationToken);
        var config = await client.GetAsync("/system/logs/..%2Fconfig.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(marker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(HttpStatusCode.NotFound, database.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, config.StatusCode);
    }

    private static string GetLogFilePath(DebarrWebApplicationFactory factory) =>
        Assert.Single(Directory.GetFiles(Path.Combine(factory.DataDirectory, "logs"), "debarr-*.log"));

    // The file sink holds the file open for writing.
    private static async Task<string> ReadLogFileAsync(DebarrWebApplicationFactory factory)
    {
        await using var stream = new FileStream(GetLogFilePath(factory), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [LoggerMessage("{Marker}.")]
    private static partial void LogMarker(ILogger logger, LogLevel level, string marker);
}
