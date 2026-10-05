using Debarr.Extensions;
using Microsoft.Reactive.Testing;

namespace Debarr.Tests.Extensions;

public sealed class IObservableExtensionsTests : ReactiveTest
{
    private readonly TestScheduler _scheduler = new();

    [Fact]
    public void An_error_goes_to_the_handler_and_the_stream_completes_in_its_place()
    {
        var failure = new InvalidOperationException("The source failed.");
        var source = _scheduler.CreateHotObservable(OnNext(210, 1), OnNext(220, 2), OnError<int>(230, failure));
        var handled = new List<Exception>();

        var observer = _scheduler.Start(() => source.CompleteOnError(handled.Add));

        Assert.Equal([OnNext(210, 1), OnNext(220, 2), OnCompleted<int>(230)], observer.Messages);
        Assert.Equal([failure], handled);
    }

    [Fact]
    public void A_stream_that_completes_leaves_the_handler_uncalled()
    {
        var source = _scheduler.CreateHotObservable(OnNext(210, 1), OnCompleted<int>(220));
        var handled = new List<Exception>();

        var observer = _scheduler.Start(() => source.CompleteOnError(handled.Add));

        Assert.Equal([OnNext(210, 1), OnCompleted<int>(220)], observer.Messages);
        Assert.Empty(handled);
    }
}
