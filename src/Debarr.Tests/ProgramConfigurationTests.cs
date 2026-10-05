using System.Text.Json;
using Debarr.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Debarr.Tests;

[CollectionDefinition(nameof(ProgramConfigurationTests), DisableParallelization = true)]
[Collection(nameof(ProgramConfigurationTests))]
public class ProgramConfigurationTests : IAsyncDisposable
{
    private readonly DebarrWebApplicationFactory _factory = new();
    private readonly Dictionary<string, string?> _priorEnvironment = [];

    public ValueTask DisposeAsync()
    {
        foreach (var (name, value) in _priorEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        return _factory.DisposeAsync();
    }

    [Fact]
    public void Defaults_apply_without_config_json()
    {
        var server = _factory.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
        var ffmpeg = _factory.Services.GetRequiredService<IOptions<FfmpegOptions>>().Value;

        Assert.Equal(8080, server.Port);
        Assert.Equal("*", server.BindAddress);
        Assert.Equal("", server.UrlBase);
        Assert.Equal("ffmpeg", ffmpeg.FfmpegPath);
    }

    [Fact]
    public void Config_json_sets_port_and_normalised_url_base()
    {
        WriteConfigJson("""{"Server":{"Port":9090,"UrlBase":"movies/"}}""");

        var server = _factory.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.Equal(9090, server.Port);
        Assert.Equal("/movies", server.UrlBase);
    }

    [Fact]
    public void Data_dir_in_config_json_leaves_the_data_directory_unchanged()
    {
        var elsewhere = Path.Combine(_factory.DataDirectory, "elsewhere");
        WriteConfigJson(JsonSerializer.Serialize(new { App = new { DataDir = elsewhere } }));

        var app = _factory.Services.GetRequiredService<IOptions<AppOptions>>().Value;

        Assert.Equal(_factory.DataDirectory, app.DataDir);
        Assert.True(Directory.Exists(Path.Combine(_factory.DataDirectory, "keys")));
        Assert.False(Directory.Exists(elsewhere));
    }

    [Fact]
    public void Prefixed_environment_variable_overrides_config_json()
    {
        WriteConfigJson("""{"Server":{"Port":9090}}""");
        SetEnvironment("DEBARR__SERVER__PORT", "8181");

        var server = _factory.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.Equal(8181, server.Port);
    }

    [Fact]
    public void Command_line_argument_overrides_prefixed_environment_variable()
    {
        WriteConfigJson("""{"Server":{"Port":9090}}""");
        SetEnvironment("DEBARR__SERVER__PORT", "8181");
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Server:Port", "7070"));

        var server = factory.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.Equal(7070, server.Port);
    }

    [Fact]
    public void Unprefixed_environment_variable_is_ignored()
    {
        SetEnvironment("SERVER__PORT", "6060");

        var server = _factory.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.Equal(8080, server.Port);
    }

    // WebApplicationFactory passes ASPNETCORE_ variables to the app as command-line arguments, so the test checks the app's environment providers.
    [Fact]
    public void Aspnetcore_prefixed_environment_variable_is_ignored()
    {
        SetEnvironment("ASPNETCORE_SERVER__PORT", "6060");

        var configuration = (IConfigurationRoot)_factory.Services.GetRequiredService<IConfiguration>();
        var environmentProviders = configuration.Providers.OfType<EnvironmentVariablesConfigurationProvider>().ToList();

        Assert.NotEmpty(environmentProviders);
        Assert.DoesNotContain(environmentProviders, provider => provider.TryGet("Server:Port", out _));
    }

    [Fact]
    public void Non_numeric_port_fails_startup()
    {
        WriteConfigJson("""{"Server":{"Port":"abc"}}""");

        var exception = Assert.Throws<InvalidOperationException>(() => _factory.Services);

        Assert.Equal("Failed to convert configuration value 'abc' at 'Server:Port' to type 'System.Int32'.", exception.Message);
    }

    [Fact]
    public void Out_of_range_port_fails_startup()
    {
        WriteConfigJson("""{"Server":{"Port":70000}}""");

        var exception = Assert.Throws<OptionsValidationException>(() => _factory.Services);

        Assert.Equal(
            "DataAnnotation validation failed for 'ServerOptions' members: 'Port' with the error: 'The field Port must be between 1 and 65535.'.",
            exception.Message);
    }

    private void WriteConfigJson(string json) =>
        File.WriteAllText(Path.Combine(_factory.DataDirectory, "config.json"), json);

    private void SetEnvironment(string name, string value)
    {
        _priorEnvironment.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }
}
