using Debarr.EventStore;
using Debarr.Scanning;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Debarr.Tests.EventStore;

/// <summary>Every command decides again on a fresh read when another commit lands on its stream between its read and its write.</summary>
public sealed class RetryRuleTests : IAsyncLifetime
{
    private readonly Interleaver _interleaver = new();
    private readonly FakeLoggerProvider _logs = new();
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _host = await TestHost.StartAsync(
            services => services.ConfigureFisher(options => options.Listeners.Add(_interleaver)),
            logging => logging.AddProvider(_logs));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_command_whose_stream_another_command_appends_to_before_it_writes_decides_again_and_both_commit()
    {
        var first = Directory.CreateDirectory(Path.Combine(_host.DataDirectory, "first"));
        var second = Directory.CreateDirectory(Path.Combine(_host.DataDirectory, "second"));
        _interleaver.Before<RootFolderAdded>(() => _host.Runtime.SendCommandAsync(new AddRootFolder(new LocalPath(second.FullName), DateTimeOffset.UtcNow), CancellationToken));

        var added = await _host.Runtime.SendCommandAsync(new AddRootFolder(new LocalPath(first.FullName), DateTimeOffset.UtcNow), CancellationToken);

        Assert.True(added.IsSuccess);
        await using var session = _host.Store.QuerySession();
        var rootFolders = (await session.Events.FetchStreamAsync(Library.StreamId, token: CancellationToken)).Select(stored => stored.Data).OfType<RootFolderAdded>();
        Assert.Equal([second.FullName, first.FullName], rootFolders.Select(added => added.Path.Value));
    }

    [Fact]
    public async Task A_retried_write_conflict_logs_one_warning_and_wolverine_logs_nothing()
    {
        var existing = Directory.CreateDirectory(Path.Combine(_host.DataDirectory, "existing"));
        Assert.True((await _host.Runtime.SendCommandAsync(new AddRootFolder(new LocalPath(existing.FullName), DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);
        var first = Directory.CreateDirectory(Path.Combine(_host.DataDirectory, "first"));
        var second = Directory.CreateDirectory(Path.Combine(_host.DataDirectory, "second"));
        _interleaver.Before<RootFolderAdded>(() => _host.Runtime.SendCommandAsync(new AddRootFolder(new LocalPath(second.FullName), DateTimeOffset.UtcNow), CancellationToken));

        Assert.True((await _host.Runtime.SendCommandAsync(new AddRootFolder(new LocalPath(first.FullName), DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);

        var logs = _logs.Collector.GetSnapshot();
        Assert.DoesNotContain(logs, log => log.Message.StartsWith("Invocation of ", StringComparison.Ordinal));
        var retry = Assert.Single(logs, log => log.Level == LogLevel.Warning && log.Category == typeof(AddRootFolder).FullName);
        Assert.Matches(@"^AddRootFolder met a write conflict on attempt 1 and decides again on a fresh read\. \S", retry.Message);
        Assert.DoesNotContain(logs, log => log.Level >= LogLevel.Error);
    }
}
