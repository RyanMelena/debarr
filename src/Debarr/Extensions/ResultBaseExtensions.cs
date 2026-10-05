using FluentResults;

namespace Debarr.Extensions;

public static class ResultBaseExtensions
{
    /// <summary>The message of each field error, by the property it refuses, the first for each.</summary>
    public static Dictionary<string, string> GetFieldErrors(this ResultBase result) =>
        result.Errors
            .OfType<FieldError>()
            .DistinctBy(error => error.Field)
            .ToDictionary(error => error.Field, error => error.Message);

    /// <summary>The messages of the errors that refuse no single field, joined; null when there are none.</summary>
    public static string? GetFormError(this ResultBase result) =>
        result.Errors.Where(error => error is not FieldError).Select(error => error.Message).ToList() is { Count: > 0 } messages
            ? string.Join(" ", messages)
            : null;
}
