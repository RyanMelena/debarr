---
title: Reactive Extensions
description: "The rules for how Debarr creates, shares, schedules, subscribes to and tests IObservable<T> streams with System.Reactive 7, and which streams Debarr has."
size: "about 21 KB"
read_when:
  - "Writing or changing code that creates, shares, subscribes to or tests an IObservable<T>: a stream, a subject, a Blazor component's subscription, or the activity feed."
  - "Reviewing such code against the rules."
skip_when:
  - "The change touches no IObservable<T>, such as a command, a handler, a projection, a read model query, markup or styles."
  - "Only which streams Debarr has matters: read the section Debarr's streams alone."
sections:
  - "When Rx fits: Rx against Task, IAsyncEnumerable<T> and Channel<T>."
  - "The contract: the order and threading of observer calls."
  - "Creating a stream: the adapter for each kind of source, Observable.Create and FromEvent among them."
  - "Subjects: a last resort that needs the operator's agreement, and the cases where one is allowed."
  - "Sharing a stream: hot and cold streams, Publish and RefCount, Replay."
  - "Threads and schedulers: when to add concurrency, and the injected IScheduler."
  - "Subscribing: error handling with CompleteOnError, async work with FromAsync and Switch or Concat, and disposal."
  - "Blazor components: subscribing in OnInitialized, narrowing, InvokeAsync and Dispose."
  - "Debarr's streams: each stream Debarr has, who owns it, and how the activity feed composes them."
  - "Testing: virtual time with TestScheduler."
  - "Sources: the guidance these rules come from."
find: 'grep -n "^## " docs/reactive-extensions.md'
---

# Reactive Extensions

Debarr's live behaviour runs on `IObservable<T>` with System.Reactive 7: player notifications, library changes, work in progress, and the activity feed that pages and activity messages follow.
These rules come from the Rx.NET maintainers' guidance, the Rx source, and the other sources listed at the end.
Code that creates, shares, schedules or subscribes to an observable follows them, and a design that breaks one needs the operator's agreement first.

## When Rx fits

- Rx models push-based streams of events that start outside the code that handles them, such as player notifications, filesystem changes, process output and job lifecycle, and the domain events derived from them.
- A single result is a `Task`, a sequence the consumer pulls is an `IAsyncEnumerable<T>`, and a work queue that must slow its producer down is a `Channel<T>`.
- Rx keeps the code declarative: a type that reacts to another type's events declares, once, a pipeline over that type's stream that says what each event leads to.
  The other type never calls it to apply a change, and a lock, a `SerialDisposable` or a refresh method that an operator such as `Switch` covers is replaced by that operator.
- Rx has no back pressure beyond a source waiting for `OnNext` to return.
  A consumer that cannot keep up lowers the rate with `Sample`, `Throttle` or `Buffer`, or the stream becomes a channel.

## The contract

- A stream calls its observer in the order `OnNext* (OnError | OnCompleted)?`, and nothing follows `OnError` or `OnCompleted`.
- A stream makes one call at a time to each observer, and waits for each call to return before making the next.
- A stream releases its resources when it terminates, so disposing a finished subscription is optional.
- Once `Dispose` returns, the observer receives nothing more.
  Calls can still arrive while `Dispose` runs, and the stream may go silent without saying how it stopped, so unsubscribing is no way to watch something shut down.
- Streams built from Rx factory methods and operators keep the contract.
  Code that calls `OnNext` itself, inside `Observable.Create` or on a subject, keeps it by hand.

## Creating a stream

A stream is built from the source that produces its notifications, with the first adapter that fits.

| Source | Adapter |
|---|---|
| An existing `IObservable<T>`, such as CliWrap's `Observe()` | Use it as it is. |
| Notifications that follow from another stream, such as a scan started event from a job event | A pipeline of operators over that stream. |
| The outcome of one call that only its caller needs | No stream: the method returns a `Task` or `Task<T>`, and the caller awaits it. |
| A .NET event with an `EventHandler`-style delegate, such as `FileSystemWatcher.Changed` | `Observable.FromEventPattern` with add and remove delegates. |
| An event whose handlers return `Task`, such as MQTTnet's `DisconnectedAsync` | `Observable.Create`, adding a handler that forwards to the observer and returns `Task.CompletedTask`, and removing it on dispose. |
| A callback that can be registered and removed, such as a Quartz.NET job listener | `Observable.Create`, registering the callback on subscribe and removing it in the disposable it returns. |
| A resource that lives as long as the subscription, such as a player connection whose StreamJsonRpc methods forward its notifications | `Observable.Create` with an async delegate that opens the resource, forwards until its token is cancelled, and disposes the resource. |
| One asynchronous operation | `Observable.FromAsync(cancellationToken => ...)`, which runs once per subscription and cancels on unsubscribe. |
| Time | `Observable.Timer` or `Observable.Interval` with the injected scheduler. |
| Asynchronous work repeated on an interval that nothing else subscribes to, such as the detection queue check | No stream: a `PeriodicTimer` over the injected `TimeProvider`, in a loop that awaits each run before the next tick. |
| Notifications that a type's own method originates, when no row above fits | A private subject, with the operator's agreement, as *Subjects* describes. |

```csharp
IObservable<FileSystemEventArgs> changes = Observable
    .FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
        handler => watcher.Changed += handler,
        handler => watcher.Changed -= handler)
    .Select(eventPattern => eventPattern.EventArgs);

IObservable<MqttClientDisconnectedEventArgs> disconnections = Observable.Create<MqttClientDisconnectedEventArgs>(observer =>
{
    Task OnDisconnected(MqttClientDisconnectedEventArgs arguments)
    {
        observer.OnNext(arguments);
        return Task.CompletedTask;
    }

    client.DisconnectedAsync += OnDisconnected;
    return () => client.DisconnectedAsync -= OnDisconnected;
});
```

- Never implement `IObservable<T>` or `IObserver<T>`.
  `Observable.Create` and the `Subscribe` overloads handle the contract's edge cases, such as a call that arrives after unsubscribing.
- A member that returns a stream never returns null; it returns `Observable.Empty<T>()` or `Observable.Never<T>()`.
- A `FromEventPattern` stream is created where the code would otherwise write `+=` and `-=`, never inside a query.
  It adds and removes its handler on the `SynchronizationContext` that was current when it was created, and all its subscribers share that handler.
- `Observable.Create` with an async delegate completes the stream when the task completes, reports a fault as `OnError`, and cancels its token on unsubscribe.
  It drops calls made after unsubscribing, so work that ignores the token is wasted.
- Arguments are validated before `Observable.Create`, so subscribing never throws.
- A callback that can fire on two threads at once is serialized where its stream is created: `Merge` serializes the streams it merges, and `Synchronize` serializes one.
- A failure the source recovers from is data, not `OnError`, because `OnError` ends the stream for every subscriber.
  `FileSystemWatcher.Error` becomes its own stream, and `OnError` carries only a failure that ends the source.
- Custom operators compose existing operators and live in `Extensions/IObservableExtensions.cs`.

## Subjects

- A subject is the last resort.
  The Rx.NET maintainers recommend against subjects in most cases, and Dave Sexton calls them "the 'mutable variables' of the Rx world".
- Every subject needs the operator's agreement before it is written, whether it is a `Subject<T>`, a `BehaviorSubject<T>`, a `ReplaySubject<T>` or another `ISubject<T>`.
  A design that fits the criteria below still needs that agreement.
- A subject fits only when all of these hold:
  - nothing observable, and nothing that converts to an observable, carries the notifications;
  - the stream must be hot;
  - the stream's scope is one type;
  - that type has no event for the same notifications.
- In Debarr that is a type whose own work originates the notifications, such as `LibraryScanner` reporting a scan a scan pause stops, or a callback Debarr implements that has no event or observable form, such as Fisher's session listener.
  Fisher reads its session listeners from the store's options once per session, so an `Observable.Create` that registers a listener per subscription would race the sessions opening and miss those already open, and the listener keeps one subject.
- A subject whose owner calls `OnNext` after work that must not fail catches and logs a subscriber's exception around the call.
  `ReadModelChangeListener` publishes after the commit has ended, and an exception from it would turn a committed command into a failed reply.
- A subject is never a shared bus that other types push into.
  A type exposes only the streams it originates.
- The subject is a private field, exposed with `AsObservable()` so that no caller can cast it back and push into it.
- `Subject<T>` passes concurrent `OnNext` calls on concurrently, so its owner serializes them, with a lock or `Subject.Synchronize`.
- A value that changes over time and that a late subscriber needs at once is a `BehaviorSubject<T>` owned by the type, or a derived stream shared with `Replay(1)`.

## Sharing a stream

- A cold stream starts its source again for each subscriber, so two subscribers to an `Observable.Create` connection open two connections.
  A hot stream shares one source, and a late subscriber misses what came before it.
- `Publish().RefCount()` shares a cold stream's source, connecting on the first subscriber and disconnecting after the last.
  `RefCount(disconnectDelay)` keeps it connected through a short gap between subscribers.
- A type that decides the lifetime itself, such as a connection held from startup to shutdown, calls `Publish()` and `Connect()`, and disposes what `Connect()` returned when the lifetime ends.
- Share only when the source's side effects or cost must happen once.
  `FromEventPattern` streams and subjects are hot already.
- Every buffer is bounded: `Replay` takes a count or a window, and so does `ReplaySubject<T>`.

## Threads and schedulers

- Rx is free-threaded: an operator runs on the thread that delivered its input.
  Only an operator given a scheduler, `ObserveOn` or `SubscribeOn` introduces concurrency.
- Concurrency is added only when it noticeably improves the app.
- Only the top-level subscriber, the code that calls `Subscribe` and knows what its callback needs, makes scheduling decisions.
  A source, a service or the activity feed never applies `ObserveOn` or `SubscribeOn`.
- `ObserveOn` is for an observer that must run in one execution context, and it sits immediately before `Subscribe`.
  It schedules a work item for every notification, and behind it an observer that throws ends its subscription with no trace.
  A Blazor component uses `InvokeAsync` instead.
- Every time-based operator takes the injected `IScheduler`: `Sample`, `Throttle`, `Buffer` and `Window` over a time span, `Timeout`, `Delay`, `Timer` and `Interval`.
  Rx 7 measures time through `IScheduler` rather than `TimeProvider`, and tests pass a `TestScheduler`.
- The app registers one `IScheduler`, `DefaultScheduler` wrapped with a `Scheduler.Catch` that logs, because an exception thrown on a thread pool thread ends the process.
- `EventLoopScheduler` gives work one dedicated thread, `NewThreadScheduler` suits work that runs for seconds, and `ThreadPoolScheduler` is a legacy type that new code avoids.
- `Synchronize` repairs a source that breaks the one-call-at-a-time rule, where that source is first obtained.
  A stream built from Rx operators needs none.
- Rx.NET's `Throttle` waits for a quiet period, as a debounce does, while `Sample` takes the latest value at a fixed rate.

## Subscribing

- A query that ends in `Subscribe` handles its errors, with `CompleteOnError` as its last operator or an `onError` handler, unless the stream cannot fail.
  `CompleteOnError` passes the error to a handler and completes the stream, so the `Subscribe()` after it takes no arguments.
  `Subscribe` with no `onError` rethrows an error on the thread that delivered it, and on a thread pool thread that ends the process.
- An observer's callback returns quickly and never blocks.
  The source waits for it, and so does everything merged with that source, and `Sample` and `Buffer` hold their lock while they call it.
- An observer never throws.
  Rx does not turn an exception from `OnNext` into `OnError`: it unwinds into the code that produced the notification, and the observers after it miss that notification.
  The callback catches its own exceptions and routes them, to `DispatchExceptionAsync` in a component and to the log in a service.
- An async lambda never goes to `Subscribe`.
  It becomes `async void`: runs overlap, nothing awaits them, and their exceptions escape Rx.
  The async work goes into the pipeline as a stream from `Observable.FromAsync`, combined the way the work needs:
  - `Switch()` when only the latest run matters, such as a reload that a newer event makes stale, because it cancels the run in progress;
  - `Concat()` when every item needs its own run, in order and one at a time, which buffers items while a run is busy;
  - `SelectMany` when runs may overlap.
- A stream is never blocked on with `First`, `Last`, `Single`, `Wait` or `ForEach`.
  `FirstAsync()`, `ToTask(cancellationToken)` and `ForEachAsync(onNext, cancellationToken)` are awaited instead.
  Awaiting a stream returns its last element once it completes and throws if it completes empty, so only a stream that completes is awaited.
- Side effects go in `Subscribe`, or in `Do` when they belong in the middle of a query, and the other operators stay free of them.
  The exception is reporting in a `Catch` handler, as `CompleteOnError` does: the handler reports the error it recovers from, to the log or to `DispatchExceptionAsync`, since the report and the recovery are one step.
- Work inside one `Observable.FromAsync` recovers from its own failures with `try` and `catch` in its method, where the failure happens, rather than with `Catch` in the query.
  Its `catch` skips cancellation with `when (!cancellationToken.IsCancellationRequested)`, since an unsubscribe that cancels the work is no failure.
- A subscriber that lives shorter than its stream, such as a page, a circuit or a player connection, disposes its subscription, because the stream keeps the observer reachable until then.
  `CompositeDisposable`, `SerialDisposable` and `TakeUntil` tie several subscriptions to one lifetime.
- A type holds what it disposes in one field, `private readonly CompositeDisposable _disposables = [];`, whether that is one subscription or several, and its `Dispose` calls `_disposables.Dispose()`.
  It adds each disposable where it creates it, with Rx's `DisposeWith(_disposables)`, from `System.Reactive.Disposables.Fluent`, which returns the disposable it adds.
  A cancellation that ends with the type goes in as a `CancellationDisposable`, whose `Dispose` cancels its token and leaves the token readable.
- A service that subscribes for the app's lifetime does so in a hosted service and disposes the subscription when the host stops.

## Blazor components

- A component subscribes in `OnInitialized` and disposes the subscription in `Dispose`.
- It narrows the stream before it renders: `Where`, then `Sample` or `Buffer` with the injected scheduler.
- Its render work runs through `InvokeAsync`, which moves it onto the circuit's synchronization context, and calls `StateHasChanged` there.
- It passes exceptions from its callbacks, and from `CompleteOnError`, to `DispatchExceptionAsync`, so the error boundary shows them.
- A component that renders again on the activity feed or on a timer inherits `LiveComponentBase`, which holds this subscription.
  One on a timer overrides its `Reloads` with `Observable.Interval` over the injected scheduler.
- It subscribes to streams that services own, and creates no `FromEventPattern` stream itself, since one created there would capture the circuit's synchronization context.

```csharp
ActivityFeed.Events
    .Where(IsShownHere)
    .Sample(TimeSpan.FromMilliseconds(250), scheduler)
    .Select(_ => Observable.FromAsync(cancellationToken => InvokeAsync(() => ReloadAsync(cancellationToken))))
    .Switch()
    .CompleteOnError(exception => _ = DispatchExceptionAsync(exception))
    .Subscribe()
    .DisposeWith(_disposables);
```

`ReloadAsync` catches its own exceptions and passes them to `DispatchExceptionAsync`, so a failed reload never ends the subscription.

## Debarr's streams

- The type that owns a source owns its stream, built with the adapters above: `FolderWatcher` from its `FileSystemWatcher` events, each `KodiPlayerConnection` from its connection's notifications, and the Quartz.NET scheduler from a job listener through `ObserveJobs`, an extension on its `IScheduler` that emits a `JobEvent` for each start and end of the jobs a matcher selects, and from a scheduler listener through `ObserveSchedule`, which emits a `ScheduleChanged` for each scheduling and unscheduling of the triggers a matcher selects.
- A type that does work for each event of another stream runs the work as one async method through `Observable.FromAsync`, and combines the runs with the operator the work needs, as *Subscribing* describes.
  `PlaybackHandler` handles each playback started event with `Switch` for each player, so a player's newer playback cancels the handling of its one before, and subscribes from startup to shutdown.
  Each write the method commits, a cancelled delivery among them, reaches subscribers as a committed event from `ReadModelChangeListener`.
- `NotificationPublisher.SendAsync` starts every attempt of one notification and returns them as tasks, and `PlaybackHandler` stores the playback while they are in flight and each delivery as its task ends, with `Task.WhenEach`.
- `LibraryScanner`'s scan started and finished events follow its jobs, so it maps the job events from `ObserveJobs` to its own, and it merges the library scan's schedule changes from `ObserveSchedule`, on which *System > Tasks* reads the next run again.
- Only events that are the outcome of a type's own method come from a private subject, as *Subjects* describes: a scan a scan pause stops in `LibraryScanner`, a root folder removal's start and end in `RootFolderRemover`, and a detection started or finished in `DetectionOrchestrator`.
- Back-end code subscribes to the stream of the type it depends on, and the activity feed serves the UI alone.
  It subscribes at startup, before its source starts, and treats an event as a trigger and the database as the record, so a late or merged event delays work without losing it.
  `PlayerConnectionService` reads a player on each committed event that adds, changes, renames or removes it, and `FolderWatcher` reads the root folders on each `ChangesTo(nameof(Library))`, each with `Switch` so a newer event replaces the read and the watchers or connection before it.
- `ReadModelChangeListener` publishes, from one private subject, each event a commit appended as a `CommittedEvent`, and then a `ReadModelChanged` for each read model the commit changed.
  `Committed<TEvent>()` is the data of the committed events of one type, and the activity message area describes saved settings and a cleared History from the committed events.
  A page reloads on the changes to the read models it names, and each health check triggers on `ChangesTo` for the read models it reads, merged with the runtime stream it reads when it has one, such as `PlayerConnectionService.ConnectionStates`.
- A type whose events belong in the activity feed implements `IActivitySource`.
- `ActivityFeed` composes those streams rather than collecting pushes.
  Its `Events` merges every registered activity source's stream with `Observable.Merge`, and no type pushes into it.
- `Merge` delivers one notification at a time, so the feed needs no `Synchronize`, and it applies no scheduler.
- Each stream that feeds the activity feed is hot, so a page that subscribes never opens a connection or starts work.
- Each stream that feeds the activity feed never terminates, and reports a failure as an activity event.
  `Merge` ends on the first `OnError` from any input, which would silence every page at once.
- Subscribers take what they need from the feed: a page filters and samples it, and the activity message area batches it.
- A player connection is one cold stream, and each subscription opens its own connection to the player.
  The stream carries the connection's state changes and the player's playback started events, and disposing the subscription closes the connection.
  `PlayerConnectionService` holds one subscription per enabled player, and each committed change to a player switches that player to a new subscription.

## Testing

- Rx logic is tested in virtual time with `TestScheduler` from `Microsoft.Reactive.Testing`, never with real delays.
- Code under test takes its `IScheduler`, and the test passes a `TestScheduler` and advances it with `AdvanceBy` or `AdvanceTo`.
- `CreateHotObservable` and `CreateColdObservable` script a source with `ReactiveTest.OnNext(ticks, value)`, and `CreateObserver<T>()` records what arrives in `Messages`.
- An adapter's tests check that it forwards the source's notifications, removes its handler or closes its resource on dispose, and turns a recoverable failure into data.

## Sources

- [Intro to Rx](https://github.com/dotnet/reactive/tree/main/Rx.NET/Documentation/IntroToRx), 2nd edition, by Ian Griffiths and Lee Campbell for Rx.NET 6: the contract and subscription lifetime (*Key Types*), adapters and subjects (*Creating Observable Sequences*), threading and the top-level subscriber rule (*Scheduling and Threading*), `await` and `AsObservable` (*Leaving Rx's World*), `Publish` and `RefCount` (*Publishing Operators*), virtual time (*Testing Rx*), and *Appendix C: Usage Guidelines*.
- [Rx Design Guidelines](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/Rx%20Design%20Guidelines.pdf), Microsoft, 2010: the contract (§4), `ObserveOn` as late as possible (§5.5), `Synchronize` only to repair a source (§5.8), `Publish` to share side effects (§5.10), and avoiding introduced concurrency (§6.12).
- [Rx 7.0 release notes](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/ReleaseHistory/Rx.v7.md).
- [Rx 6.1 release notes](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/ReleaseHistory/Rx.v6.md): `DisposeWith` for adding to a `CompositeDisposable` ([#2178](https://github.com/dotnet/reactive/pull/2178)).
- [`FromEvent.cs` in Rx 7.0.0](https://github.com/dotnet/reactive/blob/rxnet-v7.0.0/Rx.NET/Source/src/System.Reactive/Linq/Observable/FromEvent.cs): how `FromEventPattern` captures the synchronization context and shares its handler, and where to create it.
- [The Observable Contract](http://reactivex.io/documentation/contract.html), ReactiveX.
- [To Use Subject Or Not To Use Subject?](https://www.davesexton.com/blog/post/To-Use-Subject-Or-Not-To-Use-Subject.aspx), Dave Sexton, 2013.
- [Ana Betts on async work inside `Subscribe`](https://github.com/reactiveui/ReactiveUI/discussions/3710), ReactiveUI, 2024.
- [Async lambda pitfalls](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/async-lambda-pitfalls), Microsoft.
- [Blazor synchronization context](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context), Microsoft: `InvokeAsync`, `DispatchExceptionAsync`, and unsubscribing in `Dispose`.
- [CliWrap's push-based event stream](https://github.com/Tyrrrz/CliWrap#push-based-event-stream).
