using FluentResults;
using Microsoft.Extensions.Logging;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Tests.EventStore.Probing;

/// <param name="Version">The probe's version when the form loaded it.</param>
public sealed record RenameProbe(Guid ProbeId, string Name, long Version);

public static partial class RenameProbeHandler
{
    /// <summary>The name that makes <see cref="Handle"/> throw.</summary>
    public const string Unexpected = "Unexpected";

    /// <summary>Runs before the probe is loaded and holds what <see cref="Finally"/> ends, as a handler that holds a pause does.</summary>
    public static CancellationTokenSource Before(RenameProbe command, ILogger logger)
    {
        LogChecking(logger, command.Name);
        return new CancellationTokenSource();
    }

    public static void Finally(CancellationTokenSource checking) => checking.Dispose();

    /// <summary>Refuses a blank name beneath its field.</summary>
    public static Result Validate(RenameProbe command, Probe probe) =>
        string.IsNullOrWhiteSpace(command.Name) ? Result.Fail(new FieldError(nameof(RenameProbe.Name), "Enter a name.")) : Result.Ok();

    public static ProbeRenamed Handle(RenameProbe command, [WriteModel] Probe probe, ILogger logger)
    {
        LogRenaming(logger, command.Name);
        return command.Name == Unexpected ? throw new InvalidOperationException("The probe broke.") : new ProbeRenamed(command.Name);
    }

    [LoggerMessage(LogLevel.Information, "Checking the probe's new name {Name}.")]
    private static partial void LogChecking(ILogger logger, string name);

    [LoggerMessage(LogLevel.Information, "Renaming the probe to {Name}.")]
    private static partial void LogRenaming(ILogger logger, string name);
}
