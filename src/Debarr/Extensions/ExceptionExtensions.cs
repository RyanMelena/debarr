using JasperFx;
using JasperFx.Events;
using ArchivedStreamException = Fisher.Exceptions.ArchivedStreamException;

namespace Debarr.Extensions;

public static class ExceptionExtensions
{
    /// <summary>True when a command's write met another command's append, stream start, archive or write lock.</summary>
    public static bool IsWriteConflict(this Exception exception) =>
        exception is ConcurrencyException or ExistingStreamIdCollisionException or ArchivedStreamException or StreamLockedException;
}
