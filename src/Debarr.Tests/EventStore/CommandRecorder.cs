using System.Collections.Concurrent;
using System.Data.Common;
using Fisher;
using Fisher.Services;

namespace Debarr.Tests.EventStore;

/// <summary>Records the text of each command the event store's sessions run.</summary>
public sealed class CommandRecorder : IFisherLogger, IFisherSessionLogger
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyCollection<string> Commands => [.. _commands];

    public void Clear() => _commands.Clear();

    public IFisherSessionLogger StartSession(IQuerySession session) => this;

    public void OnBeforeExecute(DbCommand command) => _commands.Enqueue(command.CommandText);

    public void LogSuccess(DbCommand command)
    {
    }

    public void LogFailure(DbCommand command, Exception ex)
    {
    }

    public void LogFailure(Exception ex, string message)
    {
    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
    }
}
