using System.Linq.Expressions;

namespace Debarr.Extensions;

public static class IQueryableExtensions
{
    /// <summary>Sorts by the key in the given direction.</summary>
    public static IOrderedQueryable<T> OrderBy<T, TKey>(this IQueryable<T> source, Expression<Func<T, TKey>> keySelector, bool descending) =>
        descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);
}
