using System.Reactive.Linq;

namespace Debarr.Extensions;

public static class IObservableExtensions
{
    /// <summary>The source's elements, with an error passed to the handler and turned into completion.</summary>
    public static IObservable<T> CompleteOnError<T>(this IObservable<T> source, Action<Exception> handleError) =>
        source.Catch((Exception exception) =>
        {
            handleError(exception);
            return Observable.Empty<T>();
        });
}
