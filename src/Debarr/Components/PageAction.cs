using Debarr.Extensions;
using FluentResults;

namespace Debarr.Components;

/// <summary>
/// An action the operator starts on a page or modal: whether it is running, and what its last run returned.
/// A run clears the last run's errors, and an exception the action throws is logged and becomes its failure, so the form keeps what the operator entered.
/// </summary>
public sealed class PageAction
{
    private Result? _lastResult;
    private Dictionary<string, string> _fieldErrors = [];

    public bool Running { get; private set; }

    /// <summary>The last run's failure that refuses no single field; null when it succeeded or refused only fields.</summary>
    public string? Error => _lastResult?.GetFormError();

    /// <summary>The errors of each action that failed, joined; null when none did.</summary>
    public static string? ErrorOf(params ReadOnlySpan<PageAction> actions)
    {
        List<string> errors = [];
        foreach (var action in actions)
        {
            if (action.Error is { } error)
            {
                errors.Add(error);
            }
        }

        return errors.Count > 0 ? string.Join(" ", errors) : null;
    }

    /// <summary>The last run's refusal of the field that edits <paramref name="field"/>; null when the run accepted it or the field changed since.</summary>
    public string? FieldError(string field) => _fieldErrors.GetValueOrDefault(field);

    /// <summary>The <c>Error</c> and <c>ErrorText</c> of the input that edits <paramref name="field"/>, which show its <see cref="FieldError"/> beneath it.</summary>
    public IReadOnlyDictionary<string, object?> FieldErrorAttributes(string field) =>
        new Dictionary<string, object?> { ["Error"] = FieldError(field) is not null, ["ErrorText"] = FieldError(field) };

    /// <summary>Clears the refusal of the field that edits <paramref name="field"/>, once the operator changes it.</summary>
    public void ClearFieldError(string field) => _fieldErrors.Remove(field);

    /// <summary>Clears the refusals of every row of <paramref name="collection"/>, once a row is removed and the rows below it move up.</summary>
    public void ClearRowFieldErrors(string collection) =>
        _fieldErrors = _fieldErrors.Where(error => !error.Key.StartsWith($"{collection}[", StringComparison.Ordinal)).ToDictionary();

    /// <param name="failure">What an exception means for the operator, as a sentence that leads its message, such as The settings were not saved.</param>
    public Task RunAsync(Func<Task> action, ILogger logger, string failure) =>
        RecordAsync(() => action().ToResultAsync(logger, failure));

    /// <inheritdoc cref="RunAsync(Func{Task}, ILogger, string)"/>
    public Task RunAsync<TResult>(Func<Task<TResult>> action, ILogger logger, string failure)
        where TResult : ResultBase =>
        RecordAsync(() => action().ToResultAsync(logger, failure));

    private async Task RecordAsync(Func<Task<Result>> run)
    {
        Running = true;
        _lastResult = null;
        _fieldErrors = [];
        try
        {
            _lastResult = await run();
            _fieldErrors = _lastResult.GetFieldErrors();
        }
        finally
        {
            Running = false;
        }
    }
}
