namespace Debarr.Extensions;

public static class StringExtensions
{
    public static string ToCaseInsensitiveThenOrdinalSortKey(this string value) => value.ToLowerInvariant() + '\n' + value;
}
