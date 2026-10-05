using System.Text.Json.Nodes;
using Debarr.Activity;
using Debarr.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Debarr.Tests.Hosting;

public class HostSettingsFileTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("debarr-").FullName;

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    [Fact]
    public async Task Save_keeps_keys_it_does_not_name()
    {
        Write("""{"Ffmpeg":{"FfmpegPath":"/usr/bin/ffmpeg"},"Unknown":{"Keep":"me"}}""");
        var file = CreateFile();

        await file.SaveAsync(
            new Dictionary<string, JsonNode> { ["Server:Port"] = JsonValue.Create(8181) },
            TestContext.Current.CancellationToken);

        var saved = Read();
        Assert.Equal("me", (string?)saved["Unknown"]!["Keep"]);
        Assert.Equal("/usr/bin/ffmpeg", (string?)saved["Ffmpeg"]!["FfmpegPath"]);
        Assert.Equal(8181, (int)saved["Server"]!["Port"]!);
    }

    [Fact]
    public async Task Save_creates_a_missing_file()
    {
        var file = CreateFile();

        await file.SaveAsync(
            new Dictionary<string, JsonNode> { ["Server:BindAddress"] = JsonValue.Create("127.0.0.1") },
            TestContext.Current.CancellationToken);

        var saved = Read().AsObject();
        Assert.Equal("Server", Assert.Single(saved).Key);
        var server = saved["Server"]!.AsObject();
        Assert.Equal("BindAddress", Assert.Single(server).Key);
        Assert.Equal("127.0.0.1", (string?)server["BindAddress"]);
    }

    [Fact]
    public async Task Save_replaces_one_key_of_an_existing_section()
    {
        Write("""{"Server":{"BindAddress":"*","Port":8080}}""");
        var file = CreateFile();

        await file.SaveAsync(
            new Dictionary<string, JsonNode> { ["Server:Port"] = JsonValue.Create(9090) },
            TestContext.Current.CancellationToken);

        var saved = Read();
        Assert.Equal("*", (string?)saved["Server"]!["BindAddress"]);
        Assert.Equal(9090, (int)saved["Server"]!["Port"]!);
    }

    [Fact]
    public async Task Save_writes_every_key_of_a_multi_section_call()
    {
        var file = CreateFile();

        await file.SaveAsync(
            new Dictionary<string, JsonNode>
            {
                ["Server:Port"] = JsonValue.Create(8181),
                ["Server:UrlBase"] = JsonValue.Create("/movies"),
                ["Logging:LogLevel:Default"] = JsonValue.Create("Debug"),
            },
            TestContext.Current.CancellationToken);

        var saved = Read();
        Assert.Equal(8181, (int)saved["Server"]!["Port"]!);
        Assert.Equal("/movies", (string?)saved["Server"]!["UrlBase"]);
        Assert.Equal("Debug", (string?)saved["Logging"]!["LogLevel"]!["Default"]);
    }

    [Fact]
    public async Task Save_reads_back_the_json_the_startup_provider_accepts()
    {
        Write("""
            {
              // The operator's own note.
              "Server": { "BindAddress": "127.0.0.1", },
            }
            """);
        var file = CreateFile();

        await file.SaveAsync(
            new Dictionary<string, JsonNode> { ["Server:Port"] = JsonValue.Create(8181) },
            TestContext.Current.CancellationToken);

        var saved = Read();
        Assert.Equal("127.0.0.1", (string?)saved["Server"]!["BindAddress"]);
        Assert.Equal(8181, (int)saved["Server"]!["Port"]!);
    }

    [Fact]
    public async Task Save_emits_one_host_settings_saved_event()
    {
        var file = CreateFile();
        var received = new List<ActivityEvent>();
        using var subscription = file.ActivityEvents.Subscribe(received.Add);

        await file.SaveAsync(
            new Dictionary<string, JsonNode> { ["Server:Port"] = JsonValue.Create(8181) },
            TestContext.Current.CancellationToken);

        Assert.IsType<HostSettingsSavedEvent>(Assert.Single(received));
    }

    [Fact]
    public async Task Saved_configuration_takes_the_file_value_over_the_running_value()
    {
        var file = CreateFile();
        await file.SaveAsync(
            new Dictionary<string, JsonNode> { ["Ffmpeg:FfmpegPath"] = JsonValue.Create("/usr/bin/ffmpeg") },
            TestContext.Current.CancellationToken);
        var running = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ffmpeg:FfmpegPath"] = "ffmpeg",
                ["Ffmpeg:FfprobePath"] = "/opt/ffprobe",
            })
            .Build();

        var saved = file.BuildSavedConfiguration(running);

        Assert.Equal("/usr/bin/ffmpeg", saved["Ffmpeg:FfmpegPath"]);
        Assert.Equal("/opt/ffprobe", saved["Ffmpeg:FfprobePath"]);
    }

    private HostSettingsFile CreateFile() =>
        new(Options.Create(new AppOptions { DataDir = _dataDirectory }));

    private void Write(string json) =>
        File.WriteAllText(Path.Combine(_dataDirectory, "config.json"), json);

    private JsonNode Read() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(_dataDirectory, "config.json")))!;
}
