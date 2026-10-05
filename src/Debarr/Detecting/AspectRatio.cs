using System.Globalization;
using System.Text.Json.Serialization;

namespace Debarr.Detecting;

/// <summary>A raw aspect ratio, before snapping: a finite number greater than 0.</summary>
public readonly record struct AspectRatio
{
    [JsonConstructor]
    public AspectRatio(double value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "An aspect ratio is a finite number greater than 0.");
        }

        Value = value;
    }

    public double Value { get; }

    public static bool IsValid(double value) => value > 0 && double.IsFinite(value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
