using FluentResults;

namespace Debarr;

/// <summary>A refusal of the value in one field of a form, which the form shows beneath that field.</summary>
/// <param name="field">The name of the property the field edits, such as <c>Name</c>.</param>
public sealed class FieldError(string field, string message) : Error(message)
{
    public string Field { get; } = field;
}
