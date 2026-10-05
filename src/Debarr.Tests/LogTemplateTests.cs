using System.Reflection;
using System.Text.RegularExpressions;
using Debarr.Detecting;
using Microsoft.Extensions.Logging;

namespace Debarr.Tests;

/// <summary>Every log message and scope template in the app follows the one template style.</summary>
public sealed partial class LogTemplateTests
{
    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Type[] AppTypes = typeof(FileHash).Assembly.GetTypes();

    [Fact]
    public void Every_log_message_is_whole_sentences_with_pascal_case_placeholders()
    {
        var messages = AppTypes
            .SelectMany(type => type.GetMethods(AllMembers))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(logMethod => logMethod.Attribute is not null)
            .Select(logMethod => (Name: $"{logMethod.Method.DeclaringType!.Name}.{logMethod.Method.Name}", logMethod.Attribute!.Message))
            .ToList();

        Assert.True(messages.Count > 20, $"Found only {messages.Count} log methods.");
        Assert.Empty(messages.Where(message => !IsSentences(message.Message) || !HasPascalCasePlaceholders(message.Message)));
    }

    [Fact]
    public void Every_scope_is_a_noun_phrase_with_pascal_case_placeholders()
    {
        var scopes = AppTypes
            .SelectMany(type => type.GetFields(AllMembers))
            .Where(field => field.IsStatic && field.FieldType.IsGenericType && field.FieldType.GetGenericArguments() is [var first, .., var last]
                && first == typeof(ILogger) && last == typeof(IDisposable))
            .Select(field => (Name: $"{field.DeclaringType!.Name}.{field.Name}", Template: ReadScopeTemplate((Delegate)field.GetValue(null)!)))
            .ToList();

        Assert.Equal(3, scopes.Count);
        Assert.Empty(scopes.Where(scope => !NounPhrase().IsMatch(scope.Template) || !HasPascalCasePlaceholders(scope.Template)));
    }

    /// <summary>
    /// Sentences that start with a capital or a placeholder and end with a period;
    /// an {Error} or {Failure} placeholder, which holds sentences of its own, can stand last as a sentence.
    /// </summary>
    private static bool IsSentences(string message) => Sentences().IsMatch(message);

    private static bool HasPascalCasePlaceholders(string template) =>
        Placeholder().Matches(template).All(placeholder => PascalCase().IsMatch(placeholder.Groups[1].Value));

    /// <summary>The template a scope delegate opens its scope with, read from the scope's state.</summary>
    private static string ReadScopeTemplate(Delegate scope)
    {
        var recorder = new ScopeRecorder();
        var arguments = scope.GetType().GetGenericArguments()[1..^1].Select(type => type.IsValueType ? Activator.CreateInstance(type) : null);
        scope.DynamicInvoke([recorder, .. arguments]);
        return recorder.State!.Single(pair => pair.Key == "{OriginalFormat}").Value as string ?? "";
    }

    [GeneratedRegex(@"^([A-Z{].*\.|([A-Z{].*\. )?\{(Error|Failure)\})$")]
    private static partial Regex Sentences();

    [GeneratedRegex(@"^[A-Z{][^.]*[^.\s]$")]
    private static partial Regex NounPhrase();

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalCase();

    /// <summary>A logger that keeps the state of the scope it opens.</summary>
    private sealed class ScopeRecorder : ILogger
    {
        public IReadOnlyList<KeyValuePair<string, object?>>? State { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            State = (IReadOnlyList<KeyValuePair<string, object?>>)state;
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
