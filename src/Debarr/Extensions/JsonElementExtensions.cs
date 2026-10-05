using System.Text.Json;

namespace Debarr.Extensions;

public static class JsonElementExtensions
{
    /// <summary>The property named <paramref name="name"/>, or null when <paramref name="element"/> is not an object or has none.</summary>
    public static JsonElement? GetPropertyOrDefault(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    /// <summary>The property's string value, or null when it is missing, empty or holds another kind.</summary>
    public static string? GetNonEmptyStringOrDefault(this JsonElement element, string name) =>
        element.GetPropertyOrDefault(name) is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text ? text : null;

    /// <summary>The property's value as an <see cref="int"/>, or null when it is missing or holds no whole number.</summary>
    public static int? GetInt32OrDefault(this JsonElement element, string name) =>
        element.GetPropertyOrDefault(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number)
            ? number
            : null;
}
