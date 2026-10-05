---
title: Critter Stack reference
description: "What Fisher, Wolverine and their integration offer Debarr beyond commands, as checked against the package versions below: projection types, document storage and its indexes, EF Core projection storage, the hooks after a commit, the channels that carry messages out of the store, Wolverine's runtime hooks and local queues, and what the spikes of tasks 53, 54 and 58 and tasks 60 and 84 measured."
size: "about 45 KB"
read_when:
  - "Choosing how a projection is stored, or how an event or message reaches code outside the command that appended it."
  - "Building, indexing or archiving a document read model, or reading an aggregate by folding its stream, or reading what the spikes of tasks 53, 54 and 58 and tasks 60 and 84 found, such as History's timings against its query budget."
  - "Upgrading Fisher or Wolverine: compare packages below with the versions the app restores."
skip_when:
  - "Sending a command through SendCommandAsync, or writing a handler, Validate or Handle, which AGENTS.md covers."
  - "The rules for observables are wanted: they are in reactive-extensions.md."
sections:
  - "Projection types: what each Fisher projection type writes, whether it folds state or raises side effects, and how it rebuilds."
  - "Document storage: what Fisher's LINQ translates, duplicated fields, declared indexes and full-text search."
  - "EF Core as projection storage: ProjectToEfCore, its constraints, and what its rebuild and a changed shape need."
  - "Hooks after a commit: the session listener, AppendObserver, the daemon change listener and a handler's AfterCommitAsync."
  - "Messages out of the store: projection side effects, event forwarding, daemon subscriptions and event append tracking."
  - "Wolverine runtime hooks: Tracker, IWireTap, IWolverineObserver, IMessageTracker, tracing and SignalR."
  - "Local queues: ordering, parallelism, circuit breakers, and pausing a listener at run time."
  - "What task 53 found: pause, restart, running work, order, cost and forwarded commits on a local queue, each with its tests."
  - "What task 54 found: Media and History timed as flat tables, documents and EF Core entities on the large library, archiving and rebuilds, each with its tests, and how decision Q14's documents are indexed."
  - "What task 58 found: the flat tables timed against documents and folded aggregates on the large library, composites, multi-stream contention, archiving and rebuilds, each with its tests, and where decision Q15 puts each read model."
  - "What task 60 found: playback's fold of its video file, and History, Status and a notifier's deliveries on the PlaybackRow and NotifierDelivery documents, timed on the large library, with the plans and indexes that keep task 38's times."
  - "What task 84 found: every History view timed on the large library with and without PlaybackRow's title and path indexes, against the History query budget, with the views that miss it either way."
  - "What task 94 found: Re-detect All and a standard ratios change timed on the large library, alone and beside writing scan workers, and why they run inside a scan pause."
  - "Open questions: none."
  - "Checking a new version: how to check the entries again after an upgrade."
  - "Sources."
find: 'grep -n "^## " docs/critter-stack.md'
checked: 2026-10-01
packages:
  Fisher: 1.14.0
  Fisher.EntityFrameworkCore: 1.14.0
  WolverineFx.Fisher: 6.43.0
  WolverineFx: 6.43.0
  JasperFx.Events: 2.76.1
  Weasel.Sqlite: 9.38.0
tasks: [53, 54, 55, 57, 58, 59, 60, 84, 90, 94]
---

# Critter Stack reference

What Fisher, Wolverine and their integration offer Debarr beyond commands: projection types, document storage, the hooks that fire after a commit, the channels that carry messages out of the store, and the Wolverine runtime hooks and local queue controls.
Tasks 53, 54 and 58 started from it, and each recorded here what it proved or disproved, in *What task 53 found*, *What task 54 found* and *What task 58 found*.

It was checked on the date and against the package versions its front-matter lists: Fisher and WolverineFx.Fisher as `src/Directory.Packages.props` pins them, Fisher.EntityFrameworkCore as task 54 restored it, and WolverineFx and JasperFx.Events as the app restores them.
Each entry was read from the packages' XML documentation and confirmed by reflection over the assemblies, unless it says otherwise.
Minor versions of Fisher have shipped breaking changes, so check an entry again after an upgrade, following *Checking a new version*.

## Projection types

| Type | Writes | Folds state | `RaiseSideEffects` | Rebuild | Debarr |
|---|---|---|---|---|---|
| `FlatTableProjection` | A plain table, through declarative column mappings: `Map`, `SetValue`, `Increment`, `Delete` | No: each event maps to columns, and the projection never sees the row | No | Cleared through `IPublishesTables`, a special case, since its `PublishedTypes` is empty | History's read models until task 60 moves them to documents; none from decision Q15 |
| `SingleStreamProjection<TDoc, TId>` | A document per stream | Yes, through `Apply` methods on the current document | Yes | The standard document path | Media and History, from decision Q14; the library scan summaries and the history clear, from decision Q15 |
| `MultiStreamProjection<TDoc, TId>` | A document per key, grouped from events of many streams | Yes | Yes | The standard document path | The file paths, from decision Q15 |
| `EventProjection` | Whatever each event's method writes | No | No | Clears the document types it publishes, and those `DeleteViewTypeOnTeardown<T>()` names | None |
| `Projections.Snapshot<T>` | The aggregate itself as a document per stream, which `FetchLatest` then reads | Yes, through the aggregate's own methods | No | The standard document path | None |
| `CompositeProjectionFor` | Its stages' documents, each stage seeing what earlier ones wrote | Yes | Inherited from each stage | One pass over the events for every stage | None |
| An aggregation projection with `ProjectToEfCore` | EF Core entities in place of documents | Yes | Inherited from the projection type; untested on EF Core storage | Clears the table `ProjectToEfCore` names, and nothing else | None; task 54 measured it |
| `EfCoreEventProjection<T>` | EF Core entities, Fisher documents or both, per event | No | No | Not checked | None |

- A flat table is created by Fisher's migration with the store's other tables, and its name folds the store's schema in, since SQLite has no schemas.
- `RaiseSideEffects(operations, slice)` runs for an aggregation projection in continuous mode, never during a rebuild.
  It runs for an inline projection only when `EventStoreOptions.EnableSideEffectsOnInlineProjections` is true, which is off by default.
- A rebuild of a store with no events clears nothing, so a document stored without events stays; `ProjectionRebuildTests` appends one event no projection reads for that reason, and `rebuild` on an empty store leaves stray documents, which the app never writes.
- An aggregation projection removes an archived stream's document with a static `ShouldDelete(TEvent)` returning true, or an `Apply` returning null.
  `DeleteEvent<TEvent>()` removes it too, but a rebuild of a stream archived and then restored, which replays the delete and the restore in one slice, leaves the document deleted (task 54).
- An inline `MultiStreamProjection` reads its document before the commit takes SQLite's write lock, so two commits that fold into one document at once each write what they read, and the later one undoes the earlier one's change.
  It is safe only when each event writes the whole document, as a file path keyed by its path does.
  `UseOptimisticConcurrency` on the document fails every commit, since the projection's write expects no version (task 58).
- An inline `SingleStreamProjection` reads its document, and numbers the new events from the stream's version, before the commit takes the write lock too.
  An append that expects a version fails when another commit lands in between.
  An append that expects none commits over it: the document loses the other commit's change and keeps the version read ahead of the lock, one behind the stream, until the stream's next event folds it again.
  Decision Q17 has every append state its version for this reason, and `AppendVersionTests` pins both outcomes, an append at 0 starting a missing stream and failing on a started one, two appends at one version to one stream committing together, and `AppendOptimistic` stating the version it read.
- An `EventProjection`'s methods are dispatched by a source generator, and the class must be `partial`.
  `Project(IEvent<T>, IDocumentSession)` runs; `Project(IEvent<T>, IDocumentOperations)` builds, and the generator never calls it (task 58).
- A composite is always asynchronous: an inline append writes none of its documents, and only the async daemon or a rebuild runs it.
  A composite registered beside the same projection types inline rebuilds them in one pass, clearing and replaying their documents, while the inline projections keep writing between rebuilds; it runs at no other time while no async daemon runs (task 58).
- `Snapshot<T>` sets the document's `Id`, so an aggregate whose `Id` is get-only refuses every append to its stream with `Property set method not found` (task 58).
- Live aggregation stores nothing: `AggregateStreamAsync<T>` and `FetchLatest<T>` fold the stream's events through the aggregate's `Create` and `Apply` methods on each read, an archived stream too.

## Document storage

Fisher is a document database on SQLite in the shape of Marten: each document type gets a table of JSON documents and metadata columns.

- **Querying.** `IQuerySession.Query<T>()` is a LINQ provider, with `ToSql` and `ExplainAsync` to see the SQL and whether it uses an index (`plan.UsesIndex`, `plan.Steps`), a count that ignores paging (`CountIgnoringPagingAsync`), query statistics, cursor paging (`CursorPageAsync`), and joins.
- **What the LINQ translates**, checked by tasks 54 and 58:
  - `Where`, `Select`, `Distinct`, `OrderBy`, `ThenBy`, `Take`, `Skip`, `Count` and `Any` over a child collection, which reads it through `json_each`.
  - `GroupBy` followed by a `Select` of the key and its aggregates.
  - `SelectMany` is refused, so a query can't list a document's child collection as rows.
  - An enum stored by name, as `EnumStorage.AsString` stores it, can't be ordered or grouped; an integer member that copies it can.
  - `Contains`, `StartsWith` and `EndsWith` are ordinal and case-sensitive, through `instr` and `substr`, so `%` and `_` are literal; `Contains` with `StringComparison.OrdinalIgnoreCase` wraps the member in `lower()`, and every `Contains` scans the table.
  - A case-insensitive `OrderBy`, through `ToLower()` or a comparer, is refused; Fisher's strings guide stores a lowercased member to sort by.
  - A null test reads the JSON even on a duplicated member, so no index serves it; a `bool` member that says the same does.
  - `string.Compare`, `string.CompareOrdinal` and `CompareTo` are refused, and `StartsWith` on the id reads the whole table, so a range of ids is read through `AdvancedSql`.
- **SQL by hand.** `IQuerySession.AdvancedSql.QueryAsync<T>` runs SQL on the session's connection and reads each row as a document, a scalar, or a JSON column deserialized to `T`; `QueryAsync<T1, T2>` and `QueryAsync<T1, T2, T3>` read two or three per row.
  A document's table is `fi_doc_` and its type's name lowercased, and `SelectFieldsFor<T>()` names the columns a document is read from, `data` for a plain one.
- **A fragment by hand.** `MatchesSql(sql, parameters)` adds raw SQL with `?` placeholders to a LINQ `Where`, and Fisher writes the rest of the query: its filters, sort, paging and the document read (task 60).
  `NgramSearch` emits `rowid in (select rowid from fi_fts_<type> where fi_fts_<type> match ?)`, from which SQLite drives the query, reading and sorting every match; written as `+rowid in (...)`, the unary plus makes SQLite walk the sort's index and probe the match list instead.
- **Indexes.** `DocumentMappingExpression<T>` configures a document type, as Fisher's [indexing guide](https://fisher.jasperfx.net/documents/indexing/) lays out:
  - `Duplicate(x => x.Member)` adds a VIRTUAL generated column and an index on it, so nothing writes the column and it never drifts from the JSON; `[DuplicateField]` does the same on the member.
    Use it when SQL written by hand names the column.
  - `Index(x => x.Member)` adds an expression index and no column; `Index([x => (object?)x.A, x => (object?)x.B])`, or `[Index(IndexName = ...)]` on each member, a composite index in that order; `Index(x => x.Member, name: ..., predicate: x => ...)` a partial index; `UniqueIndex` a unique one.
  - SQLite uses an expression index only when the query's expression matches the index's, and Fisher builds both from the member's locator; a timestamp's is wrapped in `strftime`, so SQL written by hand with a bare `json_extract` misses it.
  - An index sorts its columns ascending, so a sort whose second term is descending sorts that term in a temporary B-tree.
  - `FullTextIndex(tokenizer, members)` adds an external-content FTS5 table kept in step by triggers, one per document type, over the document's own members and never a child collection.
    `FullTextTokenizer.Trigram` with `NgramSearch(term)` matches anywhere inside a word, ignores case, and refuses a term under three characters.
    `CheckFullTextIndexAsync` and `RebuildFullTextIndexAsync` under `AdvancedOperations` repair it after a write that bypassed the triggers.
  - `SoftDeleted`, `UseOptimisticConcurrency`, `UseNumericRevisions` and `ForeignKey`.
  - A member added to `Duplicate` or `Index` reaches the table when the store applies its changes on startup, with no migration of Debarr's own (task 54).
- **Writes outside projections.** `Patch<T>`, `DeleteWhere<T>`, `HardDeleteWhere<T>` and `UndoDeleteWhere<T>`.

## EF Core as projection storage

Fisher.EntityFrameworkCore stores an ordinary aggregation projection's documents as EF Core entities:

```csharp
options.ProjectToEfCore<TDoc, TId, TContext>("table_name", () => new TContext(...));
options.Projections.Add<SomeProjection>(ProjectionLifecycle.Inline);
```

- `ProjectToEfCore` is called before the projection that produces `TDoc` is registered; the other order gives the type two tables, and Fisher checks it.
- The context builds on a connection of its own, `UseSqlite(connectionString)`, to read each slice's current entity, and every write waits in EF's change tracker until `SaveChangesAsync` runs inside Fisher's transaction on Fisher's connection.
  A context that wrote on its own connection would block on SQLite's one write lock from inside the transaction holding it.
- EF Core creates the table, through a migration or `EnsureCreated`; Fisher's migration leaves it alone, and so does `CompletelyRemoveAllAsync`.
- The storage is not thread-safe, so the runner applies slices one at a time.
- `identitySetter.EfCoreContext<TDoc, TId, TContext>()` hands an `Apply` method the batch's context, to reach beyond its own entity; it returns null during live aggregation.
- `EfCoreEventProjection<T>` is the per-event form, handed one context per batch.
- What task 54 found:
  - A rebuild deletes the rows of the table `ProjectToEfCore` names, and an owned collection's rows go only through a foreign key that cascades.
  - EF Core's `CreateTables` leaves a table in an older shape as it is, and the projection then fails with `no such column`.
    Weasel's `MigrateAsync` over `Table` definitions that match the EF model brings it in line, but those definitions are kept in step with the model by hand.
    Tables added to `StoreOptions.ExtendedSchemaObjects` weren't created by `ApplyAllConfiguredChangesToDatabaseAsync`; the host's `ApplyAllDatabaseChangesOnStartup` wasn't tried.
  - A query over an owned collection joins its owner; a second context that maps the owned table as an entity of its own reads it alone.
  - Rebuilding Media this way took 88 s, against 24 s as documents.

## Hooks after a commit

| Hook | Fires | Carries | Notes |
|---|---|---|---|
| `IDocumentSessionListener.AfterCommitAsync(session, IChangeSet, token)` | After each session commit | The commit's events and documents | `ReadModelChangeListener` uses it to publish each committed event and the read models it changed. It derives from JasperFx's `IDocumentCommitListener`, and `AsSessionListener()` adapts the store-agnostic form. It never fires for the async daemon's projection batches. |
| `EventStoreOptions.AppendObserver` | Best-effort, after each successful commit | `IReadOnlyList<IEvent>` | An `Action`, one slot. Neither Wolverine.Fisher nor Wolverine sets it. |
| `IDaemonChangeListener.AfterCommitAsync(token)` | After the async daemon commits a batch | Nothing | For work that must happen once per durable batch. |
| A handler's `AfterCommit` or `AfterCommitAsync` method, or one marked `[WolverineAfterCommit]` (Wolverine) | After the command's commit and outbox flush, inside the command's log scope; never when `Validate` refuses or the commit throws | What the handler's other methods can take: the command, the `[WriteModel]` aggregate as the handler decided from it before the commit, the value of the `Result<T>` its `Validate` returned, the events `Handle` returned, the logger and services | The library's slices use it to reschedule the library scan, run a scan and queue a root folder removal. An exception from it reaches `SendCommandAsync` as a failure although the events committed, so each method catches and logs its own. |
| `IDocumentSessionListener.BeforeSaveChangesAsync` | Before the commit | The session's pending changes | `session.PendingChanges.Streams` holds the events about to be appended. |

## Messages out of the store

| Channel | Turned on by | Delivery | Notes |
|---|---|---|---|
| Projection side effects | `RaiseSideEffects` calling `slice.PublishMessage(message)` or `PublishMessage(message, metadata)`; inline projections also need `EnableSideEffectsOnInlineProjections` | Through `EventStoreOptions.MessageOutbox` | Fisher has no bus: its default outbox drops every message. An `IMessageOutbox` makes an `IMessageBatch` per session, which publishes in `BeforeCommitAsync` (transactional) or `AfterCommitAsync` (after the commit). Wolverine.Fisher supplies `FisherToWolverineOutbox`. Aggregation projections only. |
| Event forwarding | `IntegrateWithWolverine(integration => integration.UseFastEventForwarding = true)` | Out of the session's enrolled outbox on `SaveChangesAsync`, with no ordering guarantee | Wolverine handlers receive the events after the commit. `SubscribeToEvent<T>()` only defines a transformation, `TransformedTo(...)`, and registers nothing alone (GH-4310). In Solo mode, forwarded to a buffered local queue, it writes no envelope rows (task 53). |
| Daemon subscription to Wolverine | `PublishEventsToWolverine(name, relay => relay.PublishEvent<T>())`, or `relay.PublishEvent<T>((event, bus) => ...)` | Ordered and at-least-once, with progress kept in the store | Needs the async daemon. `relay.IncludeArchivedEvents` exists. |
| Daemon subscription to handlers | `ProcessEventsWithWolverineHandlersInStrictOrder(name, options => ...)` | Strict order, at-least-once | Needs the async daemon. |
| Custom daemon subscription | `SubscribeToEvents(IWolverineSubscription)`, or Fisher's `ISubscription` | Each `EventRange` with a session in the batch's transaction | Writes through the session are exactly-once against the store; anything outside it is at-least-once, and belongs in the returned `IDaemonChangeListener`. |
| Event append tracking | `opts.Tracking.EnableEventAppendTracking = true` | `IWolverineObserver.EventsAppended(events)`, before the commit | Read from `PendingChanges` by `NotifyObserverOfAppendedEvents`, for CritterWatch. |

## Wolverine runtime hooks

Wolverine sends a message to the handlers registered at startup; it has no API through which an observer subscribes and unsubscribes at run time.
A bridge to an `IObservable<T>` for a Blazor circuit therefore ends in a subject Debarr owns, fed by one of these hooks.

| Hook | What it is | What it carries | Fit |
|---|---|---|---|
| `IWolverineRuntime.Tracker`, a `WolverineTracker` | An `IObservable<IWolverineEvent>` | Only `ListenerState` (an endpoint's `ListeningStatus`: `Accepting`, `TooBusy`, `Stopped`, `Unknown`, `GloballyLatched`, `Paused`) and `AgentAssignmentsChanged` | A queue's state, such as paused by its circuit breaker. `WaitForListenerStatusAsync` waits for one. |
| `IWireTap` | A singleton Wolverine calls with `RecordSuccessAsync(envelope)` after a message is handled at a listening endpoint or sent from a sending one, and `RecordFailureAsync(envelope, exception)` once error handling gives up | The `Envelope`, whose `Message` is the message | The public hook that sees every handled message. Turned on per endpoint with `UseWireTap()`, a keyed service with `UseWireTap(key)`, or for every local queue with `opts.Policies.AllLocalQueues(queue => queue.UseWireTap())`. It must never throw. A message reaches it only through a handler, since a message with no handler has no route. A local queue's message reaches it twice, when it is sent and after its handler ends, as one `Envelope` whose `Attempts` is 0 at the send (task 53). |
| `IWolverineObserver`, `IWolverineRuntime.Observer` | A settable slot | Leadership, agents, circuit breakers tripping and resetting, back pressure, persisted counts, metrics, causation, and `EventsAppended` | One slot, meant for CritterWatch. |
| `IWolverineRuntime.MessageTracking`, an `IMessageTracker` | Callbacks for each envelope: sent, received, execution started and finished, succeeded, failed | The `Envelope` | Read-only, and every implementation is internal. Test tracking attaches through an internal `ActiveSession`. |
| Tracing | `opts.Tracking`: `HandlerExecutionDiagnosticsEnabled`, `DeserializationSpanEnabled`, `OutboxDiagnosticsEnabled`, `EnableMessageCausationTracking` | `System.Diagnostics` activities and events, all off by default | Observability, with no message payloads. |
| SignalR transport | `opts.UseSignalR()`, and a publishing rule such as `MessagesImplementing<T>().ToSignalR()` | Messages to browsers as CloudEvents | The Critter Stack's pattern for a live browser UI; Blazor Server components already run on the server. |

## Local queues

A listener's processing is set on its configuration, such as `opts.LocalQueue("name")` or `opts.Policies.AllLocalQueues(...)`:

- `Sequential()` for one message at a time, `MaximumParallelMessages(n)` for up to n.
- `PartitionProcessingByGroupId(PartitionSlots.Three)` (or `Five`, `Seven`, `Nine`) for order within a group, with the group set as `DeliveryOptions.GroupId` when a message is published; `ListenWithStrictOrdering(name)` for strict order.
- `BufferedInMemory()`, the default for a local queue, or `UseDurableInbox()`, which stores each message until it is handled.
- `ProcessInline()` handles a message on the publisher's call.
- `CircuitBreaking(...)` and `CircuitBreakerOptions` pause a listener while failures pass a threshold: `MinimumThreshold`, `FailurePercentageThreshold`, `TrackingPeriod`, `SamplingPeriod`, `PauseTime`.
- Error policies can pause a listener from a failure: `PauseThenRequeue(time)`, `AndPauseProcessing(time)`.
  On a buffered local queue, `Requeue().AndPauseProcessing(time)` held nothing (task 53).

A listener is reached at run time through `IWolverineRuntime.Endpoints`: `FindListenerCircuit(uri)` returns its `IListenerCircuit`, and `FindListeningAgent(uri)` its `IListeningAgent`.

- `IListenerCircuit`: `PauseAsync(time)`, `PauseWithDrainAsync(time)`, `StartAsync()`, `RestartAsync(force)`, `Status`, `QueueCount`.
  Both pauses take a duration.
  `PauseWithDrainAsync` handles every buffered message before it returns, so it waits for the queue to empty rather than holding it; `PauseAsync` may skip the drain when called from inside the handler pipeline.
- `IListeningAgent` adds `StopAndDrainAsync()`, `MarkAsTooBusyAndStopReceivingAsync()`, `LatchPermanently()`, `LaneDepth` and `LastQueueActivityAt`.

A buffered local queue's circuit is a `BufferedLocalQueue`, whose `PauseAsync` and `PauseWithDrainAsync` hold nothing, and `FindListeningAgent` returns null for it (task 53).

`ScheduleAsync(message, time)` schedules a message, and nothing replaces a scheduled message by key.

## What task 53 found

Task 53's spike ran detections as messages on a Wolverine local queue, and bridged them to the activity feed through the wire tap, on WolverineFx 6.43.0 and Fisher 1.14.0.
Its tests are in `src/Debarr.Tests/Spike/` on the branch `task53-wolverine-process-spike`, which is never merged, and each one asserts what it observed.
Every timing was taken after a warm-up message, since Dynamic code generation builds each handler on its first message.
The spike met its stop rule on pause and restart, and decision Q13 keeps `DetectionOrchestrator`.
`EventStoreOptions.AppendObserver` and `CircuitBreaking` were not tried.

| Question | Finding | Tests |
|---|---|---|
| Pause | No public call holds a local queue. `PauseAsync` and `PauseWithDrainAsync` on its `BufferedLocalQueue` return at once and leave the status `Accepting`, and a message published into a one-minute pause ran 14 ms later. An error policy's `Requeue().AndPauseProcessing(2 s)` retried the failed message at once and ran the next one 14 ms later. `FindListeningAgent` returns null for a local queue, so `StopAndDrainAsync` is out of reach. A pause cancels no running handler either, so cancelling the running detections would need a registry of Debarr's own. | `Pause_*` |
| Restart | With no durable inbox, stopping the host cancels the running handler's token, which the wire tap records as a failure, and drops the buffered messages, so a refill at startup is needed and is enough. A local queue is first in, first out: a file published behind ten others ran last, where the detection queue starts the newest file next. Two messages for one file run at once on a parallel queue. `PartitionProcessingByGroupId` with the file as the group keeps them apart, with a fixed 3, 5, 7 or 9 slots in place of the simultaneous detections. A changed `MaximumParallelMessages` never reached the started listener, even after `RestartAsync(true)`. | `Restart_*`, `Order_a_file_published_behind_a_backlog_runs_after_the_backlog` |
| Running work | The wire tap records a local message when it is sent and after its handler ends, and nothing when the handler starts, so the running detections would still need a list of Debarr's own. | `Running_work_*`, `Probe_the_local_queue_circuit_and_the_tapped_envelopes` |
| Order | A message a handler publishes through its `IMessageBus` is sent once the handler ends, so a detection's start can't come from inside its handler, while a `MessageBus` made from `IWolverineRuntime` sends at once. A handler for a marker interface receives every message that implements it. On a `Sequential()` queue the tap records messages in the order they are handled, and each file's start before its end. | `Order_*`, `Probe_the_local_queue_circuit_and_the_tapped_envelopes` |
| Cost | From a handler ending to the tap, a median of 181 µs and a p95 of 269 µs for the handled message, and 274 µs and 386 µs for the message it returns, handled on a second queue, against 8.5 µs and 24 µs through a synchronized subject. A burst of 45,000 messages through a tapped queue took 3.5 s, 79 µs each, nearly all of it in `PublishAsync`. | `Cost_*` |
| Commits through the same bridge | `UseFastEventForwarding` delivered every event of 500 commands, each appending one `HistoryCleared` in place of a library scan's commands, to a buffered local queue with the tap. It wrote no rows to `wolverine_outgoing_envelopes`, `wolverine_incoming_envelopes` or `wolverine_dead_letters`, where a durable inbox, as the control, wrote 500. A command took between 10.6 and 11.5 ms over three runs, with and without forwarding. | `FisherForwardingSpikeTests` |

## What task 54 found

Task 54's spike timed Media and History stored three ways, on Fisher 1.14.0, Fisher.EntityFrameworkCore 1.14.0 and Weasel.Sqlite 9.38.0, and decision Q14 follows from it.
Its tests are in `src/Debarr.Tests/Spike/` on the branch `task54-projection-storage-spike`, which is never merged and starts from `task54-video-files-flat-table-wip`.
`LargeLibraryImport` appends the rows of `.dev/large-base` as 388,907 events: 45,000 video files, 50,000 file paths, 100,000 detections, and 100,000 playbacks with 93,005 deliveries.
`MediaTimingSpikeTests` copies them for each storage, rebuilds its read models, and times each query, the median of 7 runs after one, in a Debug build with SQLite's default page cache.
A time is the query's alone; task 38's times are whole pages.

| Media | Review | Flat tables | Documents | EF Core |
|---|---|---|---|---|
| Rows | | A file path | A video file | A file path |
| Open: status counts, count, first page | 10 ms | 229, 23 and 152 ms | 3.0, 0.2 and 0.5 ms | 2.4, 0.3 and 0.6 ms |
| Failed filter: count, first page | 3 ms | 243 and 249 ms | 0.2 and 0.5 ms | 0.3 and 0.8 ms |
| Search: status counts, first page | 28 ms | 14 and 14 ms | 2.8 and 1.6 ms by trigram; 98 ms by `Contains` | 26 and 11 ms |
| Sort by ratio, first page | 8 ms | 166 ms | 2.8 ms | 1.5 ms |
| Sort by status, first page | | 163 ms | 0.4 ms | 1.0 ms |
| Rebuild | 32 s | 36 s | 24 s | 88 s |
| 1,000 video files appended, 50 to a session | | 3.6 s | 1.9 s | 2.7 s |

- **Flat tables** are `video_file_row` with its VIRTUAL generated status and `file_path_row`, as the WIP branch builds them, joined into `MediaRow`.
  Every count and sort on a file path reads its video file's row through `ix_video_file_row_file_hash`.
  A 64 MB page cache brought the status counts to 60 ms, and an index on the path with `COLLATE NOCASE` the first page to under 1 ms; the counts stay a join.
  The detection rows, which the detail page reads, took 34 s more to rebuild.
- **Documents** are one `MediaDocument` per video file, from a `SingleStreamProjection`, holding its file paths and indexed as *Indexes for decision Q14* lays out.
  They list video files, since `SelectMany` is refused: listing file paths through `AdvancedSql` over `json_each` took 96 ms for a page.
- **EF Core** is the same projection through `ProjectToEfCore`, whose fold copies its video file's status, result, override and failure onto each owned file path row, read by a context that maps the path table alone.
  It needs an EF Core write context and Weasel tables kept in step with it, as *EF Core as projection storage* says.

| History | Flat tables | Documents |
|---|---|---|
| Open: count, player names | 4.6 and 14.4 ms | 0.2 and 6.2 ms |
| Open: sent, not sent and delivery failed counts | 46, 20 and 25 ms | 3.4, 0.5 and 2.8 ms |
| First page, newest first, with deliveries | 1.7 ms | 0.5 ms |
| *Delivery Failed* filter, first page | 28 ms | 0.6 ms |
| Player filter, first page | 4.4 ms | 0.6 ms |
| Sort by player, first page | 46 ms | 0.6 ms, or 174 ms with the time descending |
| Search, count | 72 ms | 1.9 ms by trigram |
| Last playback summaries | 0.5 ms from their table | 0.9 ms, one query per player |
| Rebuild | 66 s for three tables | 50 s |

The documents were first timed with a duplicated field per member and nothing else: Media's search took 273 ms, History's sent count 223 ms and its search 376 ms, its last playback summaries 464 ms, and Media's status sort was refused.
The indexes below brought each to the times above.

| Question | Finding | Tests |
|---|---|---|
| Queries | As documents, every Media query and History's open, filters, search and summaries take 7 ms or less, and all but History's player names 3 ms or less. The flat tables miss three of the review's four Media times by 20 times or more, from the join, and meet its search time. | `MediaTimingSpikeTests` |
| Query features | *Document storage* lists what Fisher's LINQ translates. A lowercased member replaces the case-insensitive sort, trigram search replaces the escaped `LIKE`, and it matched the same playbacks as History's `LIKE` for each term tried, and an integer member grouped by `GroupBy` replaces the status counts. | `FisherLinqProbeTests`, `MediaTimingSpikeTests` |
| History | A `PlaybackDocument` holding its deliveries replaces the playback, delivery and last playback summary tables, and its queries on open take about 14 ms where the tables' take about 111 ms. | `MediaTimingSpikeTests.History_documents` |
| Rebuild | A document rebuild removes a document the replay can't recreate. A member added to `Duplicate` reaches the table when the store reopens, and the rebuild fills it. With `ProjectToEfCore`, a changed shape needs Weasel's migration, as *EF Core as projection storage* says. | `ArchivingAndRebuildSpikeTests` |
| Archiving | `ShouldDelete(VideoFileArchived)` removes the document, a rebuild leaves the archived stream out, and `VideoFileRestored` brings the document back, through a rebuild too. `DeleteEvent<VideoFileArchived>()` loses the restored document on a rebuild. `UnArchiveStream` has to commit before the restore is appended, since the session that unarchives a stream refuses an append to it with `ArchivedStreamException`. | `ArchivingAndRebuildSpikeTests`, `ArchiveDeletionProbeTests` |

### Indexes for decision Q14

Each document type keeps the store's `EnumStorage.AsString` for its JSON, and indexes members that Fisher can order:

- **Media** (`MediaDocument` in the spike, `MediaRow` in task 55): the spike indexed the status as an integer member, `StatusOrder`, alone and with the path key; the ratio Media sorts by, `SortAspectRatio`; the first path lowercased, `PathKey`, for the path sort; and a trigram full-text index over every path lowercased, one per line, `PathsText`.
  Task 55 built `MediaRow`'s indexes in `MediaRowProjection.AddTo`, and `MediaRowQueryTests` checks each query's plan:
  - Every member an index names is a get-only property worked out from the stored facts. Weasel's serializer writes it into the JSON, and Fisher's LINQ and indexes read it there.
  - `PathKey` is the smallest path lowercased, a newline, and then the path itself, so one member sorts by path ignoring case and breaks ties.
  - Eight composites serve Media's five sorts, alone and under a status filter. Each starts with `HasFilePath`, a `bool` every Media query tests, and ends with `PathKey`, and a sort's tie-break runs in the sort's direction, so no plan sorts in a temporary B-tree.
    Under a status filter the status sort reads `(HasFilePath, StatusOrder, PathKey)` ordered by `PathKey`, since SQLite won't treat the filter's equality as fixing the sort.
  - The detection queue reads a partial index on `FirstSeenAt` where `InDetectionQueue`, a `bool`. A queue check that finds nothing reads that index alone.
    It leaves out the running video files by stream id in the same query, and still reads that index alone; Fisher's LINQ refuses a `FileHash` as a parameter.
  - An index on `CurrentResultDetectorVersion` serves *Settings > Detection*'s count.
  - `NgramSearch` wraps its term in quotes, so FTS5's syntax characters are literal. A term under three characters searches `PathsText` with `Contains`.
  - On the large library's events, every Media view's second page and the status counts take 6 ms or less, in a Debug build.
    The `MediaRow` rebuild takes 90 s, against 24 s with the spike's index set and 15 s with none.
    With only the path, status and ratio composites the rebuild takes 27 s, but the confidence and *Added* sorts take 140 to 250 ms over the whole library or the detected files.
- **History** (`PlaybackDocument` in the spike, `PlaybackRow` in task 60, which *What task 60 found* extends): `OccurredAt`; `Sent`, a `bool` in place of a null test on the sent ratio; `PlayerName`; the composites `(PlayerId, OccurredAt)` for the last playback summaries, `(PlayerName, OccurredAt)` for the player filter and `(PlayerNameKey, OccurredAt)` for the player sort; `OccurredAt` partial on `DeliveryFailed`, a `bool` the fold sets; and a trigram full-text index over the title, the local path and the player path.

## What task 58 found

Task 58's spike timed the read models decision Q14 left as flat tables, stored as documents or read by folding their aggregate, on Fisher 1.14.0, and decision Q15 follows from it.
Its tests are in `src/Debarr.Tests/Spike/` on the branch `task58-flat-table-documents-spike`, which is never merged, with `decisions.tsv`, the trail of each step and its evidence.
`LargeLibraryImport`, task 54's adapted to today's events, appends the large library's 45,000 video files, 50,000 file paths and 100,000 detections, its 3 root folders, 200 library scans that each cover them, and 50 saves of each settings aggregate, the players and the notifiers.
`FlatTableSpikeTests` rebuilds them into today's flat tables and into documents, and times each reader's query as task 54 did, the median of 7 runs after one, in a Debug build.
`FisherProbeTests` checks each projection type on small stores.
A time varied by up to a third between runs, so a range is the spread over three runs.

| Read | Flat tables | Documents or a folded aggregate |
|---|---|---|
| A scan's lookup of one path | 0.50 to 0.57 ms | 0.17 to 0.20 ms, by id |
| A library scan's paths under its largest root folder, 31,914 | 102 to 108 ms | 100 to 115 ms through an id range reading three columns; 131 to 155 ms reading whole documents; 196 to 428 ms through `StartsWith` |
| The paths under no root folder | 2.5 to 8.9 ms | 2.0 to 17 ms through id ranges; 10 to 28 ms through `StartsWith` |
| *Settings > Library*'s count under each root folder | 6.7 to 8.8 ms | 2.5 to 3.0 ms through id ranges; 12 to 15 ms through `StartsWith` |
| The video file page's detections, for the file with the most, 9 | 0.55 ms | 0.41 to 0.69 ms folding its stream; 0.33 to 0.40 ms from a document per file; 1.25 to 1.33 ms from a document per detection |
| The same for a file with 200 detections | | 3.5 to 4.5 ms folding its stream; 2.0 to 2.2 ms from a document per file |
| The root folders, each with its newest scan | 0.41 to 0.50 ms | 1.9 to 2.0 ms: the library folded, and for each root folder the newest summary that lists it |
| Status's newest ended library scan | 0.52 to 0.63 ms | 0.23 to 0.70 ms |
| *System > Tasks*'s library scans | 0.79 ms | 0.56 to 1.94 ms |
| Startup's open library scans | 0.42 ms | 0.81 to 1.36 ms |
| The library, the detection settings, the UI settings, the players or the enabled notifiers | 0.38 to 1.01 ms each | 0.45 to 1.16 ms each, folding a stream of 50 saves |
| 1,000 video files appended, 50 to a session | 2.8 to 3.3 s | 2.9 to 3.5 s with decision Q15's documents; 3.4 to 4.5 s with every shape the spike built |

| Rebuild | Flat tables | Documents |
|---|---|---|
| `MediaRow` | 92.1 s | 92.7 s |
| The file paths | 8.7 s | 8.3 s |
| The detections | 34.1 s | None, since decision Q15 folds the stream; 15.6 s as a document per file, 33.6 s as a document per detection |
| Each other read model | 0.1 to 0.2 s | 0.2 to 0.4 s for the library scan summaries and the history clear |
| Everything | 135.7 s | 101.6 s with decision Q15's documents |
| A composite of `MediaRow`, the file paths and a document per file | | 122.7 s, against 116.6 s for the three apart; 44.3 s without `MediaRow`'s indexes |

Where decision Q15 puts each read model:

- **File paths**: a `FilePathRow` document keyed by its path, from a `MultiStreamProjection` over the video file streams with `Identity` on `FilePathAdded` and `FilePathRemoved`, and `ShouldDelete(FilePathRemoved)`.
  Each `FilePathAdded` writes the whole document, so commits that touch one path at once lose nothing.
  A document per video file would hold its paths in a child collection, which `MediaRow` already lists and no index reaches, so a scan couldn't look a path up in it.
- **Detections**: no read model.
  The video file page folds the file's stream, which holds every detection with its crop samples, as it already does for an archived file.
- **Root folders**: the library's, folded, each with the newest library scan summary that lists it.
  Each summary lists the root folders its scan covered, with their errors.
  The library has to record when each root folder was added, so a root folder added again starts afresh.
- **Library scan summaries**: a `LibraryScanSummaryRow` document per library scan, from a `SingleStreamProjection`, with its `LibraryScanOutcome`, null while it is open, and a `bool` `Closed` worked out from it, indexed on `(Closed, StartedAt)` and on `StartedAt`.
- **Library, detection and UI settings, players and notifiers**: folded from their singleton stream with `FetchLatest`, since each aggregate already holds what its pages read.
  A stored `Snapshot<T>` would need a settable `Id` on the aggregate, and would serve a stream grown long enough to need it.
- **History clear**: a document on the history stream, from a `SingleStreamProjection`, read by id in 0.12 ms.
- **Vector projections**: no read model searches by similarity.

| Question | Finding | Tests |
|---|---|---|
| Scans | A scan looks a path up by its document's id, and reads a folder's paths through `AdvancedSql` over the range of ids from the folder and a separator up to the folder and the next character, the range the flat table's query uses, reading the id, the video file and the stat. That matches the flat table; Fisher's LINQ refuses the range, and its `StartsWith` reads the whole table in two to four times the time. A folder scan reads its folder's range. | `FlatTableSpikeTests`, `FisherProbeTests.Path_range_queries_translate` |
| Detections | `MediaRow` already lists a file's paths in `HashedAt` order for the detection runner. Folding the stream gives the page every detection, which it sorts newest first, in 0.4 to 0.7 ms, with its crop samples from the events. | `FlatTableSpikeTests.Documents`, `FisherProbeTests.Live_aggregation_of_a_video_file_with_many_detections` |
| Every other reader | Each settings page, Status, *System > Tasks*, the health checks, `PlayerConnectionService`, `PlaybackHandler` and `NotificationPublisher` read a folded aggregate, which they filter and sort in memory, or a summary query whose `bool` and timestamp translate and read an index. A notifier's newest deliveries are History's, which task 60 moves. Media's check for an empty library reads `MediaRow`'s `HasFilePath`. | `FlatTableSpikeTests.Documents` |
| Writes | A scan's burst through decision Q15's documents takes at most 9% longer than through the flat tables. | `FlatTableSpikeTests` |
| Rebuild | Each document projection's rebuild removes a document the replay can't recreate and replays the rest. The whole store rebuilds in 102 s against 136 s, since the detections are no longer a read model. | `FisherProbeTests.Each_document_projections_rebuild_removes_a_document_the_replay_cannot_recreate`, `FlatTableSpikeTests` |
| Archiving | A video file loses its paths before it is archived, so its file path documents are gone already, and a restore's `FilePathAdded` brings them back, through a rebuild too. Today's detection rows stay on an archive, a rebuild while the file is archived drops them, and a restore leaves the live file's page with none until the next rebuild. A document per file, deleted with `ShouldDelete`, comes back from `VideoFileRestored` with only the current result and the last failure. A document per detection, from an `EventProjection` that deletes the file's documents on `VideoFileArchived`, comes back with none, since a rebuild replays the delete. Folding the stream keeps every detection through each step. | `FisherProbeTests.Archiving_and_restoring_the_detection_rows_today`, `FisherProbeTests.Archiving_and_restoring_the_detection_documents` |
| Contention | A root folder document from a `MultiStreamProjection` over the library and the library scans lost the enable or the scan in 13 to 14 of 20 rounds of concurrent commits, and none when they ran one after the other; the flat table lost none. With `UseOptimisticConcurrency` every commit failed. A path moving between two video files keeps one document naming the right file, inline and after a rebuild. | `FisherProbeTests.Concurrent_appends_to_two_streams_that_fold_into_one_multi_stream_document_lose_no_update`, `FisherProbeTests.A_path_that_moves_between_video_files_keeps_one_document_through_a_rebuild` |
| Composites | Inline, a composite writes nothing. Registered beside the same projection types inline, its rebuild clears and replays their documents and the inline projections keep writing afterwards, but with `MediaRow`'s indexes it is slower than the separate rebuilds. Each of these needs its read model current when a command commits, and an asynchronous read model breaks it: the library scanner's lookups of paths and of `MediaRow`, the detection queue, the `LoadAsync` on `MediaRow` of *Re-detect All*, a standard ratios change and an archive, playback's file scan, and every page through `ReadModelChanged`, which a daemon batch never raises. A later stage needs no `MediaRow` to find an archived file's paths, since they are gone before it is archived. Decision Q14 stands. | `FisherProbeTests.A_composite_is_asynchronous_so_an_inline_append_leaves_its_documents_unwritten`, `FisherProbeTests.A_composite_rebuild_beside_inline_projections_clears_and_replays_their_documents`, `FlatTableSpikeTests.Composite` |
| What goes | `DebarrDbContext` with its 9 value converters and its comparer, `DebarrDbContextExtensions`, `InitialCreate` and its model snapshot, the EF Core queries in `IQueryableExtensions`, `Data/`, the packages Microsoft.EntityFrameworkCore.Sqlite, Microsoft.EntityFrameworkCore.Design, EFCore.NamingConventions and Fisher.EntityFrameworkCore, which no code uses today, the `dotnet-ef` tool, `regenerate-initial-create.sh`, and the clash between Fisher's and EF Core's async operators. 32 files in the app and 34 in the tests open the context. The documents add an index set for the library scan summaries, SQL by hand for the scans' id ranges, which a test pins to Fisher's table, and a `ReadModelChanged` for each aggregate a page folds. | `FlatTableSpikeTests` |

## What task 60 found

Task 60 timed playback's fold of its video file, and History's reads on the `PlaybackRow` documents, on Fisher 1.14.0.
The harness is in `src/Debarr.Tests/Spike/` on the branch `task60-large-library-timings`, which is never merged and skips unless `DEBARR_SPIKE_TIMINGS` is set.
`LargeLibraryImport` appends the large library's video files and task 54's 100,000 playbacks with 93,005 deliveries through the app's own store, so every inline projection and index is today's, in about 4.7 minutes.
Each time is the median of 7 reads after one, each in a fresh query session, as a range over three runs, in a Debug build; a pair is with no clear and after a clear halfway through the playbacks.

| Read | Time |
|---|---|
| Playback's video file: the file with the most detections, 9, folded against its `MediaRow` | 1.38 to 1.52 ms against 0.83 to 0.88 ms |
| The same for a synthetic file with 200 detections, each with its crop samples | 11.2 to 12.7 ms; 4.5 to 5.2 ms without samples |
| History opens: the count, the player names, the outcome counts and the first page | 27 to 47 ms |
| Each of the 64 views, every outcome filter with and without a player, each sort both ways: page and counts | 37 ms or less, 23 ms at the median |
| A search for a term in 96% of the playbacks, "film": page and counts | 139 to 185 ms, against about 2.4 s through `NgramSearch` |
| A search for a term in 6% of them, and in 5: page and counts | 32 to 41 ms, and 15 to 27 ms |
| A two-character search, "tv", which reads every shown playback: page and counts | 317 to 996 ms, about half of it a second count that History's page no longer runs |
| A notifier's newest deliveries, from `NotifierDelivery`, for a notifier with many and with none | 0.8 to 1.1 ms; 422 to 457 ms for one with none from the playback documents through `json_each` |
| Each player's last playback, and a video file's playbacks | 1.5 ms, and 0.9 ms |
| Rebuild: `PlaybackRow`, and `NotifierDelivery` | 263.5 s, against 50 s for the spike's index set; 18.5 s |

On the running app, with the large library and nothing writing, History opens in a median of 238 ms over 11 opens, its views change in 72 to 134 ms, Status opens in 109 to 129 ms and *Settings > Notifiers* in 105 ms, timed by `time-pages.py` as task 38 timed them.

- **Filtered sorts.** With only an index per filter, a filter that keeps most playbacks sorted them all in a temporary B-tree: 364 to 511 ms for *Sent* with a name sort, 240 to 278 ms for one player.
  A composite per filter and sort serves each, `(Sent, PlayerNameKey, OccurredAt)` and its like, and partial composites on `DeliveryFailed`.
  Debarr never runs `ANALYZE`, so SQLite took the clear's range on `OccurredAt` as more selective than the filter and read the time index; History's page writes the bound as `!(OccurredAt <= clearedAt)`, which SQLite checks inside an index and never seeks on, and the filter and sort choose the index.
- **Counts.** One grouped count on `OutcomeGroup`, an `int` worked out from `Sent` and `DeliveryFailed`, reads `(OutcomeGroup, OccurredAt)` or `(PlayerName, OutcomeGroup, OccurredAt)` alone, but only when it selects `Max(OutcomeGroup)` in place of the group's key; with the key in the select, SQLite reads every document.
  A grouped count over a member read from the JSON took 525 to 686 ms for "film".
- **Search.** `NgramSearch` drives the query from the match list, reading and sorting every match: 852 to 932 ms for the page of "film".
  `MatchesSql` with `+rowid in (...)` walks the sort's index and probes the list: 27 ms for that page, and at most 8 ms for a rare term against 1 ms; a test pins the fragment to the SQL `NgramSearch` writes.
- **A notifier's deliveries.** No index reaches a delivery inside a playback document, so a notifier with no delivery walked every shown playback.
  `NotifierDelivery` is a document per delivery, which its one `DeliveryFinished` writes whole, so no commit merges fields from two streams.
- **Rebuild.** `PlaybackRow`'s 19 indexes and its trigram index take its rebuild from 141.5 s with the first 9 to 263.5 s.

## What task 84 found

Task 84 timed every History view on the large library's 100,000 playbacks twice: with `PlaybackRow`'s 19 indexes, and with its eight title and path indexes dropped, which leaves the time, player, outcome and trigram indexes.
The bar is the History query budget beside decision Q14 in [rewrite-plan.md](rewrite-plan.md): a sort or page read completes in 100 ms or less server-side.
`tools/time-history-views.cs` is the harness.
It opens a copy of the database with Fisher's schema changes off, so the copy's own indexes decide the plans, and runs each view as `PlaybackRowQuery` runs it, the page in one query session and the outcome counts in another.
A view is an outcome filter (none, *Sent*, *Not Sent*, *Delivery Failed*), a player (none, or one of the four), a search (none, "tv" at two characters, "film" at 96% of the playbacks), a sort, a direction and a page (the first and the last): 960 page reads and 15 counts per run, each the median of 5 reads after one, in a Debug build.
A range below runs from the fastest view in its row to the slowest.

| View, no search | With the 19 indexes | Without the title and path indexes |
|---|---|---|
| Time or player sort under no filter, an outcome filter or a player filter: first page; last page | 1 to 8 ms; 2 to 11 ms | 1 to 3 ms; 2 to 8 ms |
| Title or path sort under no filter: first page; last page | 2 to 3 ms; 9 to 16 ms | 628 to 646 ms; 1.86 to 1.88 s |
| Title or path sort under an outcome filter: first page; last page | 2 to 4 ms; 2 to 12 ms | 7 to 415 ms; 13 ms to 1.48 s |
| Title or path sort under a player filter: first page; last page | 1 to 3 ms; 2 to 6 ms | 26 to 275 ms; 76 to 757 ms |
| Time or player sort under an outcome filter and a player filter: first page; last page | 1 to 10 ms; 22 to 245 ms | 1 to 10 ms; 21 to 262 ms |
| Title or path sort under an outcome filter and a player filter: first page; last page | 1 to 13 ms; 25 to 397 ms | 23 to 287 ms; 23 to 722 ms |
| The outcome counts | 3 to 12 ms | 3 to 12 ms |

A search for "film" adds 20 to 70 ms to a page read and 25 to 45 ms to the counts, and without their indexes the title and path sorts' first pages take 703 to 741 ms under no filter and 34 to 464 ms under one.
A two-character search's counts take 36 to 467 ms either way, and its last pages up to 813 ms with every index and up to 919 ms without, since both read every shown playback's search text.
After a clear halfway through the playbacks every view shows half of them, and the times follow: without their indexes the title and path sorts' first pages take 532 to 559 ms under no filter and their last pages 1.09 to 1.17 s, and with every index the last page under an outcome filter and a player filter takes 113 to 173 ms.

- **The indexes stay.** Without `ix_playback_row_history_by_title`, `_by_path` and their six filtered composites, SQLite scans the table, or the filter's time index, and sorts every match in a temporary B-tree, so every title or path sort misses the budget on its first page, and 240 of the 480 views' plans change: `SCAN fi_doc_playbackrow USING INDEX ix_playback_row_history_by_title` becomes `SCAN fi_doc_playbackrow` with `USE TEMP B-TREE FOR ORDER BY`, and `SEARCH ... USING INDEX ix_playback_row_history_sent_by_title (<expr>=?)` becomes the same search on `ix_playback_row_history_sent_by_time` with the temporary B-tree.
  The review counted the title and path indexes as seven; the index list has eight.
- **The last page under an outcome filter and a player filter misses the budget with every index**: 158 to 397 ms for a view with no search, 180 to 435 ms with "film", against 1 to 13 ms for the same views' first pages.
  No index holds both the player and the outcome in a sort's order, so SQLite walks the player's index and reads each document for its outcome until it reaches the page.
  The first page stops after 50 matches.
  A composite per sort that starts with `PlayerName` and `Sent`, and a partial one on `DeliveryFailed`, would serve it at the cost of six more indexes on every playback write and rebuild.
  The operator accepted this view as an exception to the History query budget on 2026-10-04, and decision Q14 records it.
- **A two-character search reads every shown playback**, as task 60 found, so its counts and its last pages miss the budget with every index.
  Its first pages take 1 to 4 ms when the sort's index meets 50 matches early, and up to 527 ms when it meets them late.

## What task 94 found

`tools/time-library-wide-commands.cs` timed *Re-detect All*, and a standard ratios change that turns every detected result into one from the file, on a copy of the large library (45,000 video files, each with a result), once alone and once while four workers rewrote the stats of those files through `AddFilePath`, as a scan after a remount does.

| Command | Alone | Beside four writing workers |
|---|---|---|
| *Re-detect All* | succeeded in 26.2 s, 1 attempt | failed after 6 attempts in 24.1 s; the workers committed 2,963 times |
| Standard ratios change | succeeded in 25.7 s, 1 attempt | failed after 6 attempts in 28.3 s; the workers committed 3,532 times |

A command that appends at a version to every video file it reaches in one transaction fails whenever any of them changes while it runs, and the retries meet the same writes.
Both commands therefore run inside a scan pause, which cancels the running library or folder scan and holds scans until they end (decision Q17); the cancelled scan runs again once the pause ends.

## Open questions

None.

## Checking a new version

- Compare the front-matter's `packages` with `src/Directory.Packages.props` and, for the transitive ones, `src/Debarr/obj/project.assets.json`; a version that differs means the entries need checking again, and the front-matter's `checked` and `packages` change once they are.
- Each package's XML documentation is beside its assembly in the NuGet cache, such as `~/.nuget/packages/fisher/<version>/lib/net10.0/Fisher.xml`; search it for a member's `name="M:..."` or `name="P:..."` entry.
- Types and members without documentation show only by reflection.
  A console project in the scratchpad that references the packages at the new versions lists them: for each assembly (`typeof(Wolverine.IMessageBus).Assembly`, `typeof(Wolverine.Fisher.FisherIntegration).Assembly`, `typeof(Fisher.IDocumentSession).Assembly`, `typeof(JasperFx.Events.IEvent).Assembly`), enumerate `GetExportedTypes()` and print the members of the types this document names, and of every type that implements `IObservable<T>` or `IObserver<T>`.
- Search a binary for a member's name, such as `grep -c -a AppendObserver Wolverine.Fisher.dll`, to see whether an assembly uses it.
- Fisher 1.14.0's LINQ, load-by-id and event reads (`FisherQueryProvider.ToListAsync`, `FisherDocumentStorage.QueryManyAsync`, `EventOperations`) dispose their reader and leave the `SqliteCommand` to the finalizer, so a connection closed with such a command's statements still prepared keeps `debarr.db` open until the finalizer thread reaches them.
  `TestDataDirectory.DeleteAsync` runs the finalizers before it retries for that reason.
  Check whether a new version disposes those commands, and drop the forced finalization when it does.
- Run `AppendVersionTests` on the new version.
  A version whose inline projections fold under the write lock turns its first test red, and Q17's rule then guards a defect that is gone.

## Sources

- [Wolverine: Instrumentation and Metrics](https://wolverinefx.net/guide/logging), for the wire tap and its policies.
- [Early April Releases for the Critter Stack](https://jeremydmiller.com/2026/04/14/early-april-releases-for-the-critter-stack/), which introduced the wire tap.
- [SignalR + the Critter Stack](https://jeremydmiller.com/2026/03/01/signalr-the-critter-stack/), for projection side effects routed to a live UI.
- [Using SignalR](https://wolverinefx.io/guide/messaging/transports/signalr.html).
- [Publishing read model changes from Marten](https://event-driven.io/en/publishing_read_model_changes_from_marten/), for commit listeners.
- [Wolverine and Fisher](https://wolverinefx.io/guide/durability/fisher/).
- Fisher's [indexing](https://fisher.jasperfx.net/documents/indexing/), [duplicated fields](https://fisher.jasperfx.net/documents/indexing/duplicated-fields.html), [declared indexes](https://fisher.jasperfx.net/documents/indexing/indexes.html), [strings](https://fisher.jasperfx.net/documents/querying/linq/strings.html), [grouping](https://fisher.jasperfx.net/documents/querying/linq/grouping.html) and [full-text search](https://fisher.jasperfx.net/documents/querying/linq/full-text.html) pages, read 2026-10-01.
