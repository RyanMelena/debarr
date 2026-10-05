using System.Diagnostics;
using Bunit;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.EventStore.Probing;
using Debarr.Tests.Extensions;
using Fisher;
using Fisher.Services;
using JasperFx.CodeGeneration;
using JasperFx.Events.Projections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Runtime;

namespace Debarr.Tests.EventStore;

/// <summary>
/// Sends a command from a form through the app's pipeline: its handler decides from its aggregate, Fisher appends the events,
/// an inline projection writes the read model, and the activity feed announces the change.
/// </summary>
public sealed class CommandPipelineTests : BunitContext, IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string VideoFileHash = "2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192";

    private readonly FakeLoggerProvider _logs;
    private readonly DebarrWebApplicationFactory _factory;
    private readonly IServiceScope _appScope;
    private readonly List<ReadModelChanged> _changes = [];
    private readonly IDisposable _feed;
    private readonly CancelBeforeSave _cancelBeforeSave = new();

    private Action<string>? _onLog;

    public CommandPipelineTests()
    {
        _logs = new FakeLoggerProvider(new FakeLogCollector(Options.Create(new FakeLogCollectorOptions { OutputSink = message => _onLog?.Invoke(message) })));
        _factory = new DebarrWebApplicationFactory(services =>
        {
            services.AddSingleton<ILoggerProvider>(_logs);
            services.AddLogging(logging => logging.AddFilter<FakeLoggerProvider>("Debarr", LogLevel.Debug));
            services.ConfigureWolverine(options =>
            {
                options.Discovery.IncludeType(typeof(RenameProbeHandler));

                // The probe's handler has no pre-generated type, so a Release build generates every handler at runtime.
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
            });
            services.ConfigureFisher(options =>
            {
                options.Projections.Add(new ProbeRowProjection(), ProjectionLifecycle.Inline);
                options.Listeners.Add(_cancelBeforeSave);

                // A write lock held elsewhere fails the append after a second, with no retries of Fisher's own.
                options.Connection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 1 }.ToString());
                options.ConfigurePolly(_ => { });
            });
        });
        _appScope = _factory.Services.CreateScope();
        Services.AddFallbackServiceProvider(_appScope.ServiceProvider);
        _feed = _factory.Services.GetRequiredService<ActivityFeed>().Events.Subscribe(activity =>
        {
            if (activity is ReadModelChanged changed)
            {
                lock (_changes)
                {
                    _changes.Add(changed);
                }
            }
        });
    }

    private static CancellationToken CancellationToken => Xunit.TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_factory.DataDirectory, "debarr.db");

    private IDocumentStore Store => _factory.Services.GetRequiredService<IDocumentStore>();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_command_appends_its_events_and_its_read_model_announces_the_change()
    {
        var probe = await StartProbeAsync("Before");
        var form = RenderForm(probe, version: 1);

        await SaveAsync(form, "After");

        await Poll.UntilAsync(async () => await ReadNameAsync(probe) == "After", Timeout);
        Assert.Empty(form.FindAll("#probe-name-error"));
        Assert.Empty(form.FindAll("#probe-form-error"));
        Assert.Equal(2, await VersionOfAsync(probe));
        await Poll.UntilAsync(() => Changes.Any(changed => changed.ReadModel == ProbeRowProjection.ReadModel && changed.Streams.SetEquals([probe.ToString()])));
    }

    [Fact]
    public async Task A_refusal_reaches_the_field_it_refuses_and_appends_nothing()
    {
        var probe = await StartProbeAsync("Before");
        var form = RenderForm(probe, version: 1);
        var changesBefore = ProbeChanges;

        await SaveAsync(form, " ");

        form.WaitForAssertion(() => Assert.Equal("Enter a name.", form.Find("#probe-name-error").TextContent), Timeout);
        Assert.Empty(form.FindAll("#probe-form-error"));
        Assert.Equal("Before", await ReadNameAsync(probe));
        Assert.Equal(1, await VersionOfAsync(probe));
        Assert.Equal(changesBefore, ProbeChanges);
    }

    [Fact]
    public async Task A_version_conflict_reaches_the_form_and_appends_nothing()
    {
        var probe = await StartProbeAsync("Before");
        var stale = RenderForm(probe, version: 1);
        await SaveAsync(RenderForm(probe, version: 1), "First");
        await Poll.UntilAsync(async () => await ReadNameAsync(probe) == "First");

        await SaveAsync(stale, "Second");

        stale.WaitForAssertion(() => Assert.Contains("Try again", stale.Find("#probe-form-error").TextContent), Timeout);
        Assert.Equal("First", await ReadNameAsync(probe));
        Assert.Equal(2, await VersionOfAsync(probe));
    }

    [Fact]
    public async Task A_command_that_runs_out_of_retries_logs_each_retry_at_warning_and_its_failure_at_error()
    {
        var probe = await StartProbeAsync("Before");
        var runtime = _factory.Services.GetRequiredService<IWolverineRuntime>();
        Assert.True((await runtime.SendCommandAsync(new RenameProbe(probe, "First", 1), CancellationToken)).IsSuccess);

        var renamed = await runtime.SendCommandAsync(new RenameProbe(probe, "Second", 1), CancellationToken);

        Assert.Equal(["Another change was saved at the same time, so nothing was saved. Try again."], renamed.Errors.Select(error => error.Message));
        Assert.DoesNotContain(_logs.Collector.GetSnapshot(), log => log.Message.StartsWith("Invocation of ", StringComparison.Ordinal));
        var lines = _logs.Collector.GetSnapshot().Where(log => log.Category == typeof(RenameProbe).FullName && log.Level >= LogLevel.Warning).ToList();
        Assert.Equal(6, lines.Count);
        Assert.All(lines[..5], (retry, index) =>
        {
            Assert.Equal(LogLevel.Warning, retry.Level);
            Assert.Matches($@"^RenameProbe met a write conflict on attempt {index + 1} and decides again on a fresh read\. \S", retry.Message);
        });
        Assert.Equal(LogLevel.Error, lines[5].Level);
        Assert.Matches(@"^RenameProbe failed in \d+ ms\. Another change was saved at the same time, so nothing was saved\. Try again\.$", lines[5].Message);
    }

    [Fact]
    public async Task A_command_cancelled_while_it_waits_to_retry_logs_its_cancellation_and_retries_no_more()
    {
        var probe = await StartProbeAsync("Before");
        var runtime = _factory.Services.GetRequiredService<IWolverineRuntime>();
        Assert.True((await runtime.SendCommandAsync(new RenameProbe(probe, "First", 1), CancellationToken)).IsSuccess);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _onLog = message =>
        {
            if (message.Contains(" met a write conflict on attempt 1 ", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.SendCommandAsync(new RenameProbe(probe, "Second", 1), cancellation.Token));

        Assert.Single(CommandLines, line => line.Level == LogLevel.Warning);
        var line = Assert.Single(CommandLines, line => line.Level == LogLevel.Debug);
        Assert.Matches(@"^RenameProbe was cancelled after \d+ ms\.$", line.Message);
    }

    [Fact]
    public async Task An_exception_reaches_the_form_as_a_failure_that_points_to_the_log()
    {
        var probe = await StartProbeAsync("Before");
        var form = RenderForm(probe, version: 1);

        await SaveAsync(form, RenameProbeHandler.Unexpected);

        form.WaitForAssertion(
            () => Assert.Equal("Nothing was saved. The probe broke. System > Logs has the details.", form.Find("#probe-form-error").TextContent),
            Timeout);
        Assert.Equal("Before", await ReadNameAsync(probe));
        Assert.Contains(
            _logs.Collector.GetSnapshot(),
            log => log is { Level: LogLevel.Error, Exception: InvalidOperationException } && log.Message.StartsWith($"Invocation of {nameof(RenameProbe)} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_command_that_meets_a_held_write_lock_is_retried_once_the_lock_is_released()
    {
        var probe = await StartProbeAsync("Before");
        var form = RenderForm(probe, version: 1);

        await using (var writer = new SqliteConnection(_factory.UnpooledConnectionString))
        {
            await writer.OpenAsync(CancellationToken);
            await using var transaction = writer.BeginTransaction(deferred: false);

            await SaveAsync(form, "After");

            await Poll.UntilAsync(() => CommandLines.Any(line => line.Level == LogLevel.Warning && line.Message.Contains(" met a write conflict on attempt 1 ", StringComparison.Ordinal)), Timeout);
            Assert.Equal("Before", await ReadNameAsync(probe));
        }

        await Poll.UntilAsync(async () => await ReadNameAsync(probe) == "After", Timeout);
        Assert.Empty(form.FindAll("#probe-form-error"));
    }

    [Fact]
    public async Task Sending_a_command_returns_to_the_sender_while_the_command_waits_for_a_held_write_lock()
    {
        var probe = await StartProbeAsync("Before");
        var runtime = _factory.Services.GetRequiredService<IWolverineRuntime>();
        Task<FluentResults.Result> sending;

        await using (var writer = new SqliteConnection(_factory.UnpooledConnectionString))
        {
            await writer.OpenAsync(CancellationToken);
            await using var transaction = writer.BeginTransaction(deferred: false);
            var sendingStarted = Stopwatch.StartNew();

            sending = runtime.SendCommandAsync(new RenameProbe(probe, "After", 1), CancellationToken);

            Assert.InRange(sendingStarted.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            Assert.False(sending.IsCompleted);
        }

        Assert.True((await sending).IsSuccess);
        Assert.Equal("After", await ReadNameAsync(probe));
    }

    [Fact]
    public async Task What_a_command_logs_carries_its_scope()
    {
        var probe = await StartProbeAsync("Before");

        await SaveAsync(RenderForm(probe, version: 1), "After");

        await Poll.UntilAsync(async () => await ReadNameAsync(probe) == "After", Timeout);
        var renaming = Assert.Single(_logs.Collector.GetSnapshot(), log => log.Message == "Renaming the probe to After.");
        Assert.Contains(renaming.Scopes, scope => scope?.ToString()?.StartsWith($"{nameof(RenameProbe)} ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task What_a_handler_logs_before_its_aggregate_loads_carries_the_command_scope()
    {
        var probe = await StartProbeAsync("Before");

        await SaveAsync(RenderForm(probe, version: 1), "After");

        await Poll.UntilAsync(async () => await ReadNameAsync(probe) == "After", Timeout);
        var checking = Assert.Single(_logs.Collector.GetSnapshot(), log => log.Message == "Checking the probe's new name After.");
        Assert.Contains(checking.Scopes, scope => scope?.ToString()?.StartsWith($"{nameof(RenameProbe)} ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_command_logs_one_line_that_names_it_once_it_succeeds()
    {
        var probe = await StartProbeAsync("Before");

        await SaveAsync(RenderForm(probe, version: 1), "After");

        await Poll.UntilAsync(() => CommandLines.Count > 0, Timeout);
        var line = Assert.Single(CommandLines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Matches(@"^RenameProbe succeeded in \d+ ms\.$", line.Message);
    }

    [Fact]
    public async Task A_refused_command_logs_one_line_that_names_it_with_its_error()
    {
        var probe = await StartProbeAsync("Before");

        await SaveAsync(RenderForm(probe, version: 1), " ");

        await Poll.UntilAsync(() => CommandLines.Count > 0, Timeout);
        var line = Assert.Single(CommandLines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Matches(@"^RenameProbe failed in \d+ ms\. Enter a name\.$", line.Message);
    }

    [Fact]
    public async Task A_rebuild_replays_the_read_model_and_removes_a_document_the_replay_cannot_recreate()
    {
        var first = await StartProbeAsync("First");
        var second = await StartProbeAsync("Second");
        var stale = Guid.NewGuid();
        await using (var session = Store.LightweightSession())
        {
            session.Store(new ProbeRow { Id = stale, Name = "Stale" });
            session.Store(new ProbeRow { Id = first, Name = "Changed" });
            await session.SaveChangesAsync(CancellationToken);
        }

        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync(ProbeRowProjection.ReadModel, CancellationToken);

        Assert.Equal("First", await ReadNameAsync(first));
        Assert.Equal("Second", await ReadNameAsync(second));
        Assert.Null(await ReadNameAsync(stale));
    }

    [Fact]
    public async Task A_command_cancelled_while_it_commits_logs_its_cancellation_at_debug_and_appends_nothing()
    {
        var probe = await StartProbeAsync("Before");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _cancelBeforeSave.Cancellation = cancellation;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _factory.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(new RenameProbe(probe, "After", 1), cancellation.Token));

        Assert.Equal("Before", await ReadNameAsync(probe));
        Assert.Equal(1, await VersionOfAsync(probe));
        Assert.DoesNotContain(_logs.Collector.GetSnapshot(), log => log.Level >= LogLevel.Error || log.Exception is OperationCanceledException);
        var line = Assert.Single(CommandLines);
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Matches(@"^RenameProbe was cancelled after \d+ ms\.$", line.Message);
    }

    /// <summary>How many changes of the probe's read model the feed announced.</summary>
    private int ProbeChanges => Changes.Count(changed => changed.ReadModel == ProbeRowProjection.ReadModel);

    [Fact]
    public async Task A_file_path_command_logs_one_debug_line_that_names_its_path_once_it_succeeds()
    {
        var command = new AddFilePath(new FileHash(VideoFileHash), new LocalPath("/media/film.mkv"), new FileStat(1, DateTimeOffset.UnixEpoch), DateTimeOffset.UtcNow);

        Assert.True((await _factory.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(command, CancellationToken)).IsSuccess);

        var line = Assert.Single(_logs.Collector.GetSnapshot(), log => log.GetStructuredStateValue("Command") == nameof(AddFilePath));
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Matches(@"^AddFilePath of /media/film\.mkv succeeded in \d+ ms\.$", line.Message);
    }

    /// <summary>The lines that report a probe command's outcome.</summary>
    private List<FakeLogRecord> CommandLines =>
        [.. _logs.Collector.GetSnapshot().Where(log => log.GetStructuredStateValue("Command") == nameof(RenameProbe))];

    private List<ReadModelChanged> Changes
    {
        get
        {
            lock (_changes)
            {
                return [.. _changes];
            }
        }
    }

    private async Task<Guid> StartProbeAsync(string name)
    {
        var probe = Guid.NewGuid();
        await using var session = Store.LightweightSession();
        session.Events.StartStream<Probe>(probe, new ProbeStarted(name));
        await session.SaveChangesAsync(CancellationToken);
        return probe;
    }

    private IRenderedComponent<ProbeForm> RenderForm(Guid probe, long version) =>
        Render<ProbeForm>(parameters => parameters.Add(form => form.ProbeId, probe).Add(form => form.Version, version));

    private static async Task SaveAsync(IRenderedComponent<ProbeForm> form, string name)
    {
        await form.RaiseInputAsync("#probe-name", name, Timeout);
        await form.RaiseClickAsync("#probe-save", Timeout);
    }

    private async Task<long> VersionOfAsync(Guid probe)
    {
        await using var session = Store.QuerySession();
        return (await session.Events.FetchStreamStateAsync(probe, CancellationToken))!.Version;
    }

    private async Task<string?> ReadNameAsync(Guid probe)
    {
        await using var session = Store.QuerySession();
        return (await session.LoadAsync<ProbeRow>(probe, CancellationToken))?.Name;
    }

    /// <summary>Cancels a command's token once its session is about to write, as a detection pause can while a detection's record commits.</summary>
    private sealed class CancelBeforeSave : IDocumentSessionListener
    {
        public CancellationTokenSource? Cancellation { get; set; }

        public async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            if (Cancellation is { } cancellation)
            {
                Cancellation = null;
                await cancellation.CancelAsync();
            }
        }

        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) => Task.CompletedTask;
    }

    public new async ValueTask DisposeAsync()
    {
        _feed.Dispose();
        await base.DisposeAsync();
        _appScope.Dispose();
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
