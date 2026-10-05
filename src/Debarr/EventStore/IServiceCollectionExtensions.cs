using System.Text.Json.Serialization.Metadata;
using Debarr.Activity;
using Fisher;
using JasperFx.CodeGeneration;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Fisher;
using Wolverine.Middleware;
using Wolverine.Runtime.Handlers;

namespace Debarr.EventStore;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddFoldedAggregate<TAggregate>(this IServiceCollection services) =>
        services.AddSingleton(new FoldedAggregate(typeof(TAggregate)));

    /// <summary>Adds Fisher on the database, with the listener that announces read model changes, and Wolverine to run commands against it.</summary>
    public static IServiceCollection AddEventStore(this IServiceCollection services, string connectionString)
    {
        services.AddFisher(provider =>
            {
                var options = new StoreOptions();
                options.Connection(connectionString);

                // An enum stored by name keeps its meaning when its members are reordered.
                // The serializer leaves out a null, so a nullable constructor parameter may be missing and reads as null.
                options.ConfigureSerialization(
                    EnumStorage.AsString,
                    Casing.CamelCase,
                    configure: serializerOptions =>
                    {
                        serializerOptions.RespectRequiredConstructorParameters = true;
                        serializerOptions.TypeInfoResolver = (serializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                            .WithAddedModifier(typeInfo =>
                            {
                                foreach (var property in typeInfo.Properties.Where(property => property.IsRequired && property.IsSetNullable))
                                {
                                    property.IsRequired = false;
                                }
                            });
                    });

                options.Listeners.Add(new AppendVersionGuard());
                options.Listeners.Add(new ReadModelChangeListener(
                    options.Projections,
                    [.. provider.GetServices<FoldedAggregate>().Select(folded => folded.Aggregate)],
                    provider.GetRequiredService<ILogger<ReadModelChangeListener>>()));
                return options;
            })
            .ApplyAllDatabaseChangesOnStartup()
            .IntegrateWithWolverine();
        services.AddSingleton(provider => provider.GetRequiredService<StoreOptions>().Listeners.OfType<ReadModelChangeListener>().Single());
        services.AddSingleton<IActivitySource>(provider => provider.GetRequiredService<ReadModelChangeListener>());

        return services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(IServiceCollectionExtensions).Assembly;

            // The event store is one SQLite file, which one process owns.
            options.Durability.Mode = DurabilityMode.Solo;
            options.Policies.AutoApplyTransactions();
            options.Policies.Add<CommandScopePolicy>();
            options.Policies.AddMiddleware(typeof(CommandMiddleware));
            options.CodeGeneration.AddContinuationStrategy<RefusalContinuationStrategy>();

            options.OnException(exception => exception is not OperationCanceledException)
                .CustomAction(CommandReply.FailAsync, "Reply with a failed result", InvokeResult.Stop);

#if DEBUG
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
#else
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
#endif
        });
    }
}
