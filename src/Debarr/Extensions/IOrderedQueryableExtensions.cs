using System.Linq.Expressions;

namespace Debarr.Extensions;

public static class IOrderedQueryableExtensions
{
    /// <summary>Breaks ties by the key in the given direction.</summary>
    public static IOrderedQueryable<T> ThenBy<T, TKey>(this IOrderedQueryable<T> source, Expression<Func<T, TKey>> keySelector, bool descending) =>
        descending ? source.ThenByDescending(keySelector) : source.ThenBy(keySelector);
}
