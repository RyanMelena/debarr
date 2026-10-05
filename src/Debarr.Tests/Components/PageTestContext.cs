using System.Runtime.CompilerServices;
using Bunit;
using Debarr.Components.Layout;
using Debarr.Components.Pages.Settings;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Playing;
using Fisher;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Wolverine.Runtime;

namespace Debarr.Tests.Components;

/// <summary>
/// Renders a page inside the layout, as the router does, against the app booted on a temporary data directory.
/// A scope of the app's container supplies every service the page injects, and MudBlazor's services come from the test's own.
/// </summary>
public abstract class PageTestContext : BunitContext, IAsyncLifetime
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly DebarrWebApplicationFactory _factory;
    private readonly IServiceScope _appScope;
    private readonly List<(BunitContext Tab, IServiceScope Scope)> _otherTabs = [];

    /// <param name="configureAppServices">Replaces services of the booted app, such as its aspect ratio detector.</param>
    protected PageTestContext(Action<IServiceCollection>? configureAppServices = null)
    {
        _factory = new DebarrWebApplicationFactory(configureAppServices);
        _appScope = _factory.Services.CreateScope();
        Services.AddMudServices();
        Services.AddFallbackServiceProvider(_appScope.ServiceProvider);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected static CancellationToken CancellationToken => Xunit.TestContext.Current.CancellationToken;

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

    /// <summary>Resolves a service from the booted app.</summary>
    protected T GetAppService<T>()
        where T : notnull =>
        _factory.Services.GetRequiredService<T>();

    /// <summary>Records each playback in the history, as the app records them.</summary>
    protected Task SeedPlaybacksAsync(params IEnumerable<PlaybackHandled> playbacks) =>
        TestPlayback.RecordAsync(GetAppService<IDocumentStore>(), playbacks, CancellationToken);

    /// <summary>Records the playback with its deliveries in the history.</summary>
    protected Task SeedPlaybackAsync(PlaybackHandled playback, params IEnumerable<Delivery> deliveries) =>
        TestPlayback.RecordAsync(GetAppService<IDocumentStore>(), playback, deliveries, CancellationToken);

    /// <summary>Adds a video file at <paramref name="path"/> with a current result that read <paramref name="detectionPath"/>, the same path by default.</summary>
    protected async Task<(FileHash VideoFile, Detection Detection)> SeedVideoFileAsync(
        string path,
        double rawAspectRatio,
        double? overrideAspectRatio = null,
        bool dontSend = false,
        string? detectionPath = null)
    {
        var now = DateTimeOffset.UtcNow;
        var store = GetAppService<IDocumentStore>();
        var detection = TestVideoFile.Detected(rawAspectRatio, path: detectionPath ?? path, startedAt: now);
        var videoFile = await TestVideoFile.AddAsync(
            store,
            path,
            CancellationToken,
            hashedAt: now,
            followedBy: videoFile => overrideAspectRatio is not null || dontSend
                ? [new AspectRatioDetected(videoFile, detection), new OverrideSaved(Override.Create(overrideAspectRatio, dontSend, null, now).Value)]
                : [new AspectRatioDetected(videoFile, detection)]);
        return (videoFile, detection);
    }

    /// <summary>Adds a video file detected at 1.78 for a one-byte file under the data directory's media folder, which a detection can read.</summary>
    protected async Task<FileHash> SeedFileAsync(string name)
    {
        var path = Path.Combine(DataDirectory, "media", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [0], CancellationToken);
        var store = GetAppService<IDocumentStore>();
        return await TestVideoFile.AddAsync(
            store,
            [path],
            CancellationToken,
            stat: FileStat.From(new FileInfo(path)),
            followedBy: videoFile => [new AspectRatioDetected(videoFile, TestVideoFile.Detected(1.78, path: path))]);
    }

    /// <summary>Appends <paramref name="events"/> to the video file's stream.</summary>
    protected Task AppendToVideoFileAsync(FileHash videoFile, params object[] events) =>
        TestVideoFile.AppendAsync(GetAppService<IDocumentStore>(), videoFile, events, CancellationToken);

    protected async Task SendChangeDetectionSettingsAsync(Func<ChangeDetectionSettings, ChangeDetectionSettings> change)
    {
        DetectionSettings settings;
        await using (var session = GetAppService<IDocumentStore>().QuerySession())
        {
            settings = await DetectionSettings.ReadAsync(session, CancellationToken);
        }

        var command = change(DetectionSettingsForm.FromSettings(settings).ToChangeDetectionSettings());
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(command, CancellationToken)).IsSuccess);
    }

    protected async Task WhileRootFolderRemovalRunsAsync(string rootFolder, Action whileRunning)
    {
        await using var connection = new SqliteConnection(UnpooledConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var writeLockTheRemovalWaitsOn = connection.BeginTransaction();

        await GetAppService<RootFolderRemover>().EnqueueAsync(new LocalPath(rootFolder), CancellationToken);
        whileRunning();
    }

    protected async Task ClearHistoryAsync() =>
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(new ClearHistory(), CancellationToken)).IsSuccess);

    /// <summary>Another browser tab on the booted app, with a scope of the app's container of its own, which <see cref="RenderPage"/> renders in.</summary>
    protected BunitContext OpenAnotherTab()
    {
        var tab = new BunitContext();
        var scope = _factory.Services.CreateScope();
        tab.Services.AddMudServices();
        tab.Services.AddFallbackServiceProvider(scope.ServiceProvider);
        tab.JSInterop.Mode = JSRuntimeMode.Loose;
        _otherTabs.Add((tab, scope));
        return tab;
    }

    /// <summary>Renders <typeparamref name="TPage"/> as the layout's body, at <paramref name="uri"/> when one is given, in <paramref name="tab"/> or this test's own.</summary>
    protected IRenderedComponent<UISettingsProvider> RenderPage<TPage>(string? uri = null, IReadOnlyDictionary<string, object?>? parameters = null, BunitContext? tab = null)
        where TPage : IComponent
    {
        var context = tab ?? this;
        if (uri is not null)
        {
            context.Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
        }

        RenderFragment body = builder =>
        {
            builder.OpenComponent<TPage>(0);
            foreach (var (name, value) in parameters ?? new Dictionary<string, object?>())
            {
                builder.AddComponentParameter(1, name, value);
            }

            builder.CloseComponent();
        };
        return context.Render<UISettingsProvider>(provider => provider.AddChildContent<MainLayout>(layout => layout.Add(main => main.Body, body)));
    }

    /// <summary>Counts each URL change from now on, a replaced entry included.</summary>
    protected StrongBox<int> CountNavigations()
    {
        var count = new StrongBox<int>();
        Services.GetRequiredService<NavigationManager>().LocationChanged += (_, _) => count.Value++;
        return count;
    }

    public new async ValueTask DisposeAsync()
    {
        foreach (var (tab, scope) in _otherTabs)
        {
            await tab.DisposeAsync();
            scope.Dispose();
        }

        await base.DisposeAsync();
        _appScope.Dispose();
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
