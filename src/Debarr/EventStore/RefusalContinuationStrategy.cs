using FluentResults;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using Wolverine.Configuration;
using Wolverine.Middleware;
using Wolverine.Runtime;

namespace Debarr.EventStore;

/// <summary>
/// Stops a command whose handler's <c>Validate</c> refuses it, and replies with the refusal.
/// A <c>Validate</c> that returns a <see cref="Result{TValue}"/> hands its value to the handler's later methods.
/// </summary>
public sealed class RefusalContinuationStrategy : IContinuationStrategy
{
    public bool TryFindContinuationHandler(IChain chain, MethodCall call, out Frame? frame)
    {
        var refusal = call.Method.Name == "Validate" ? call.Creates.FirstOrDefault(variable => variable.VariableType.CanBeCastTo<ResultBase>()) : null;
        frame = refusal is null ? null : new RefusalFrame(refusal);
        return frame is not null;
    }

    private sealed class RefusalFrame : AsyncFrame
    {
        private readonly Variable _validated;
        private readonly Variable? _value;
        private Variable? _context;

        public RefusalFrame(Variable validated)
        {
            _validated = validated;
            uses.Add(validated);
            if (validated.VariableType.IsGenericType && validated.VariableType.GetGenericTypeDefinition() == typeof(Result<>))
            {
                _value = new Variable(validated.VariableType.GetGenericArguments()[0], this);
            }
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _context = chain.FindVariable(typeof(MessageContext));
            yield return _context;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.Write(
                $"if (await {typeof(CommandReply).FullNameInCode()}.{nameof(CommandReply.RefuseAsync)}({_context!.Usage}, {_validated.Usage})) return;");
            if (_value is not null)
            {
                writer.Write($"var {_value.Usage} = {_validated.Usage}.{nameof(Result<object>.Value)};");
            }

            Next?.GenerateCode(method, writer);
        }
    }
}
