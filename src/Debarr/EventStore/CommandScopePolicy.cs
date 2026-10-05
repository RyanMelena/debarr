using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace Debarr.EventStore;

/// <summary>Opens each command's log scope first in its handler, so what its own middleware logs while it waits for a pause or loads its aggregate carries the scope.</summary>
public sealed class CommandScopePolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            chain.Middleware.Insert(0, new MethodCall(typeof(CommandMiddleware), nameof(CommandMiddleware.OpenScope)));
        }
    }
}
