using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Nodes;
using Debarr.Activity;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;

namespace Debarr.Hosting;

/// <summary>
/// Reads and saves config.json in the data directory, which holds the host settings that Settings &gt; General edits.
/// Saved values apply from the next start.
/// </summary>
public sealed class HostSettingsFile(IOptions<AppOptions> app) : IActivitySource
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // The startup configuration provider accepts comments and trailing commas, so reading back accepts them too.
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // One save at a time, which also keeps the subject's calls one at a time.
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Subject<ActivityEvent> _subject = new();

    public string Path { get; } = System.IO.Path.Combine(app.Value.DataDir, "config.json");

    /// <summary>A <see cref="HostSettingsSavedEvent"/> after each save.</summary>
    public IObservable<ActivityEvent> ActivityEvents => _subject.AsObservable();

    /// <summary>The configuration the next start reads.</summary>
    public IConfiguration BuildSavedConfiguration(IConfiguration runningConfiguration)
    {
        var file = new JsonConfigurationSource { Path = Path, Optional = true, ReloadOnChange = false };
        file.ResolveFileProvider();

        return new ConfigurationBuilder()
            .AddConfiguration(runningConfiguration)
            .Add(file)
            .AddEnvironmentVariables(EnvironmentOverrides.Prefix)
            .Build();
    }

    /// <summary>Sets each configuration key, given in Section:Key form, and keeps every other key the file holds.</summary>
    public async Task SaveAsync(IReadOnlyDictionary<string, JsonNode> values, CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadAsync(cancellationToken);

            foreach (var (key, value) in values)
            {
                Set(root, key, value);
            }

            var directory = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(directory);

            // The rename is atomic within the directory, so a reader sees either the old file or the whole new one.
            var temporaryPath = System.IO.Path.Combine(directory, $"config.json.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(WriteOptions), cancellationToken);
                File.Move(temporaryPath, Path, overwrite: true);
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }

            _subject.OnNext(new HostSettingsSavedEvent());
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return new JsonObject();
        }

        await using var stream = File.OpenRead(Path);
        var node = await JsonNode.ParseAsync(stream, documentOptions: ReadOptions, cancellationToken: cancellationToken);

        // Any other JSON value at the root is for the operator to fix, so the cast throws.
        return (JsonObject)node!;
    }

    private static void Set(JsonObject root, string key, JsonNode value)
    {
        var segments = key.Split(':');
        var parent = root;

        foreach (var segment in segments[..^1])
        {
            if (parent[segment] is JsonObject section)
            {
                parent = section;
            }
            else
            {
                section = new JsonObject();
                parent[segment] = section;
                parent = section;
            }
        }

        // A node has one parent, so the caller keeps the node it passed.
        parent[segments[^1]] = value.DeepClone();
    }
}
