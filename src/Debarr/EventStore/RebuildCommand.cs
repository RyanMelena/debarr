using System.Diagnostics;
using Fisher;
using JasperFx.CommandLine;
using JasperFx.Events.Projections;

namespace Debarr.EventStore;

[Description("Applies the database schema and rebuilds the read models from the events, with the app stopped", Name = "rebuild")]
public sealed partial class RebuildCommand : JasperFxAsyncCommand<RebuildInput>
{
    /// <summary>How long one read model may take to rebuild; a large library's History takes minutes, past the daemon's default.</summary>
    private static readonly TimeSpan RebuildTimeout = TimeSpan.FromHours(1);

    public RebuildCommand()
    {
        Usage("Rebuild every read model");
        Usage("Rebuild one read model").Arguments(input => input.ReadModel!);
    }

    // Program disposes the host once the command returns, which closes the log file.
    public override async Task<bool> Execute(RebuildInput input)
    {
        var host = input.BuildHost();
        return await RebuildAsync(
            host.Services.GetRequiredService<IDocumentStore>(),
            host.Services.GetRequiredService<ILogger<RebuildCommand>>(),
            input.ReadModel,
            CancellationToken.None);
    }

    /// <summary>Applies the schema and rebuilds <paramref name="readModel"/>, or every read model when it is null.</summary>
    /// <returns>False when no read model has that name, before anything is written.</returns>
    public static async Task<bool> RebuildAsync(IDocumentStore store, ILogger logger, string? readModel, CancellationToken cancellationToken)
    {
        var readModels = store.Options.Projections.All.OfType<ProjectionBase>().Select(projection => projection.Name).Order(StringComparer.Ordinal).ToList();
        if (readModel is not null && !readModels.Contains(readModel))
        {
            LogUnknownReadModel(logger, readModel, string.Join(", ", readModels));
            return false;
        }

        await store.ApplyAllConfiguredChangesToDatabaseAsync(cancellationToken);

        using var daemon = await store.BuildProjectionDaemonAsync();
        foreach (var name in readModel is null ? readModels : [readModel])
        {
            var startedAt = Stopwatch.GetTimestamp();
            await daemon.RebuildProjectionAsync(name, RebuildTimeout, cancellationToken);
            LogRebuilt(logger, name, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }

        return true;
    }

    [LoggerMessage(LogLevel.Information, "Rebuilt {ReadModel} in {DurationMs} ms.")]
    private static partial void LogRebuilt(ILogger logger, string readModel, long durationMs);

    [LoggerMessage(LogLevel.Error, "There is no read model named {ReadModel}. The read models are {ReadModels}.")]
    private static partial void LogUnknownReadModel(ILogger logger, string readModel, string readModels);
}
