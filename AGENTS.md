# Debarr

Debarr detects the original aspect ratio of the video files under its root folders, and sends it to automation when a player starts playback.
It is built with .NET 10 and C#.

- [docs/core-principles.md](docs/core-principles.md) says what Debarr does and the rules its design follows.
  A change that conflicts with it needs the operator to change the principle first.
- [docs/rewrite-plan.md](docs/rewrite-plan.md) holds the decisions, the names, and the task list with each task's status.
  Implement one task per session, and mark it done in the plan's *Status* when you commit it.
- [docs/domain-model.md](docs/domain-model.md) says what Debarr is and does: the aggregates, their events, commands and read models, archiving, the folders, the *Behaviour* every change keeps, and the *Constraints* behind choices any implementation keeps.
  Read *Behaviour* before changing what the app does; a rule a task can't keep goes to the operator as a decision first.
  Milestone M8 moved the code to this design, and [docs/rewrite-history.md](docs/rewrite-history.md) keeps its tasks.
- [docs/rewrite-history.md](docs/rewrite-history.md) keeps the finished milestones, with what each task found, how the rewrite ported v2, the schema as it was after M7, and how the pieces worked after M7.
  Read it when a task needs to know why the code is the way it is.
- [docs/reactive-extensions.md](docs/reactive-extensions.md) holds the rules for how Debarr creates, shares, schedules and subscribes to observables with System.Reactive.
  Read it before writing code that touches an `IObservable<T>`.
- [docs/critter-stack.md](docs/critter-stack.md) records what Fisher, Wolverine and their integration offer beyond commands: projection types, document storage, the hooks after a commit, the channels that carry messages out of the store, and Wolverine's runtime hooks and local queues, as checked against the package versions it lists.
  Read it before choosing how a projection is stored or how an event reaches code outside the command that appended it.
- [docs/ui-conventions.md](docs/ui-conventions.md) holds the conventions every page, modal and component follows: the page header, sections, forms, the save flow, confirmations, errors, formatters and the words the UI uses.
  Read it before changing anything in `Components/`.
- `docs/critter-stack.md`, `docs/reactive-extensions.md` and `docs/rewrite-history.md` open with front-matter that says when to read them, when to skip them, what each section holds and how to find it.
  Read the first 35 lines of one before the rest, and then only the sections the task needs.
- The previous design is a separate repository checked out at `../debarr-v2`, tagged `v2-final`.
  Read a file from it with `git -C ../debarr-v2 show v2-final:<path>` when a task names one, such as `docs/running-the-tests.md`.

Debarr reports aspect ratio facts when playback starts and never tells automation what to do.

```sh
cd src
dotnet build
dotnet test
```

The tests run on Microsoft.Testing.Platform, which rejects VSTest flags.
`dotnet test --nologo` reports "Zero tests ran", so run `dotnet test` bare.
[docs/running-the-tests.md](docs/running-the-tests.md) says how to run one test class, what each test base class boots, which tests check these rules, and why `debarr.db` can stay locked after a test.

Live checks are covered by two skills in `.claude/skills/`, which `.gitignore` excludes, so they are local to a working copy rather than part of the repository:

- `exercise-kodi` drives the test Kodi over JSON-RPC and carries the driver script.
  Use it before opening a socket to Kodi.
- `debarr-live-check` boots the app against a copied data directory, triggers playback, and reads the log and notification payload.

A task that changes what a skill drives updates the skill in the same session, such as the scripts' type names and paths after M8's renames and moves, and the payload example if the payload changes.

## Application model

Debarr works like the *arr apps, specifically Radarr.
When a choice about configuration, settings pages or navigation is open, follow what Radarr does.
Host settings are the exception.
They follow standard .NET configuration.

- Host settings (data directory, bind address, port, URL base, log level, ffmpeg paths) use .NET configuration and the options pattern.
  The options classes in `Hosting/` hold the defaults, and the log level is the standard `Logging:LogLevel:Default` key.
  `appsettings.json` logs Wolverine and Quartz at Warning, so their startup lines leave Debarr's own lines readable, and `DEBARR__LOGGING__LOGLEVEL__WOLVERINE` lowers it.
  `<datadir>/config.json` holds the values saved in *Settings > General*.
  `DEBARR__SECTION__KEY` environment variables override both, and *Settings > General* marks each value an environment variable sets.
  Only `DEBARR__` variables are read.
  Changes take effect on restart.
- `DEBARR__APP__DATADIR` selects the data directory, `/data` by default.
  It holds `config.json`, the database `debarr.db`, the Data Protection keys in `keys/` and the log files in `logs/`.
- All other settings are aggregates in the event store, and are edited in the UI.
  Changes take effect without a restart.
  Environment variables don't set them.
- Players and notifiers are configured the way Radarr configures download clients: a settings page for each, a card for every configured integration, and a hand-written modal and a typed settings value for each integration type.

## Code comments

- Keep comments short and factual.
  Say what the code does, or name the constraint it meets when that isn't obvious from the code.
- Write each comment about the code as it is now.
  Put rationale, rejected alternatives, and history in commit messages.
- Leave out references to other documents: no section markers, document names, task ids, or URLs.
- Leave out negatives: don't say what the code doesn't do, or why another approach wasn't taken.
  You can still describe when a value applies, but phrase it positively ("null when the path is outside every root folder").
- Skip comments that just repeat the code.

## Logging

- Log through source-generated `[LoggerMessage]` methods with no event id, never a `LogX` call.
  In a class with a logger they are `private partial` instance methods at the bottom of the class, and in a static class they are `private static partial` methods that take the `ILogger`.
  A scope is a `static readonly` delegate from `LoggerMessage.DefineScope`.
  The build enforces this, refusing a direct call or a varying template, and `LogTemplateTests` checks every template's style.
- A message is one or more sentences, each starting with a capital or a placeholder and ending with a period.
  An expected failure's message, `{Error}`, or a sentence passed in, `{Failure}`, stands as its own sentence after the message's period: `Could not record the playback on {Player}. {Error}`.
  Placeholders are PascalCase and named for the domain's terms, such as `{Player}`, `{Notifier}`, `{Path}`, `{VideoFile}` and `{RootFolder}`, and a duration is `{DurationMs}` in whole milliseconds.
  An unexpected exception goes in the exception parameter, never the message.
- A scope is a noun phrase with no period.
  Every command runs in `{Command} {MessageId}`, every detection in `Detection of {VideoFile}`, and the handling of every playback in `Playback {PlaybackId} on {Player}`, so the file scans, deliveries and commands they cause carry them.
  Work a process starts in reply to a commit, such as a player's connection or a folder watch, logs outside the scope of the command that committed.
  The log file writes the open scopes after the message, outermost first, as ` (first, second)`.
- `SendCommandAsync` logs one line per command, under the command's type: `{Command} succeeded in {DurationMs} ms.`, or `{Command} failed in {DurationMs} ms. {Error}`, at Information.
  A command a scan or a detection sends once per file, `AddFilePath`, `RemoveFilePath` and `RecordDetection`, implements `IFilePathCommand`, so its line names the path, `{Command} of {Path} succeeded in {DurationMs} ms.`, and a success logs at Debug.
  A caller that does more with a failure logs its own line as well.
  A command whose sender cancels it while it runs commits nothing, logs `{Command} was cancelled after {DurationMs} ms.` at Debug, and throws `OperationCanceledException` to its sender, so Wolverine logs no failure for it.
- A command that meets a write conflict commits nothing, and `CommandMiddleware` replies with a `WriteConflictError`, so Wolverine logs no failure for it.
  `SendCommandAsync` sends it again on a fresh read after a short wait, up to five times, and logs `{Command} met a write conflict on attempt {Attempt} and decides again on a fresh read. {Error}` at Warning before each.
  A command that meets one on every attempt logs its `failed` line at Error, with *Another change was saved at the same time, so nothing was saved. Try again.*
  The line's duration covers every attempt and wait.
  Any other exception reaches Wolverine, which logs `Invocation of <Command> ... failed!` with it at Error under the command's type.

## Documentation

- Write Markdown prose one sentence per line, with no wrapping at a fixed width, so a diff shows only the sentences that changed.
  A list item's later sentences go on their own lines, indented to the item's text.
- Tables, code blocks and headings keep their own layout.

## Types, files and names

- Give every top-level type (class, record, struct, enum, interface, delegate) its own file, except in a chapter's aggregate, events, slice and read model files.
  The file name matches the type name.
  A generic type `Foo<T>` goes in `Foo.cs`.
  `ArchitectureTests` enforces this by scanning the source.
- A nested type stays in its containing type's file.
  Only nest a type when it is an implementation detail of that type.
- The folder path matches the namespace, under the project's name: `src/Debarr/Health/` is `Debarr.Health`.
  The build enforces this.
- The domain is organised in vertical slices, one folder and namespace per chapter, named for a capability: `Scanning/` (`Library`), `Detecting/` (`VideoFile`, `DetectionSettings`), `Playing/` (`Players`, `Playback`), `Notifying/` (`Notifiers`) and `Appearance/` (`UISettings`).
  No folder shares a name with a type, since a namespace hides a type of the same name from the code under it, and `ArchitectureTests` enforces this.
- A chapter holds these files:
  - `<Aggregate>.cs` for each aggregate root: its state, a `Create` or `Apply` method per event, its entities and its value types, the static functions for rules more than one slice uses, and the static `ReadAsync` that folds its stream (`VideoFile.cs`).
  - `Events.cs`: every event the chapter's aggregates append, and nothing else.
    An input from outside the chapter, such as a player's playback started event, keeps its own file.
  - A slice file per command, named for the command (`SaveOverride.cs`): the command record, and a static class named for the command plus `Handler` whose pure static methods guard and decide, `Validate` and `Handle`.
  - A read model file per read model, named for it (`MediaRow.cs`): its type, its projection (`MediaRowProjection`) and its query (`MediaRowQuery`).
    A read model's type ends in `Row` only when a page lists it as the rows of a table, such as `MediaRow`, and is otherwise named for what it holds, such as `StoredFilePath` or `HistoryClear`.
  - The processes that serve the chapter, such as `LibraryScanner` in `Scanning/`, each in its own file.
- A value type more than one chapter uses lives in the chapter that owns its rules, such as `AspectRatio` in `Detecting/`, and one no chapter owns, such as `FieldError`, lives in the folder of the function it serves.
- A domain type takes its name from the *Names* table, and other code takes a suffix when its name would clash (`VideoFileStatusText` beside `VideoFileStatus`).
  `Components/` stays organised by page, and each page's component is named for the page plus `Page` (`MediaPage.razor`).
  The rest of the app has folders by function.
- A command's handler decides: its `Handle` takes the command, the aggregate's current state and, when an event records a time, the current time, and returns the events to append, and its `Validate` refuses a command the state can't take.
  Both are pure static functions, tested without a database.
  `ArchitectureTests` enforces the shape: `Handle` takes the command first, takes no session and returns what to write, and `Validate` returns a `Result`.
  The aggregate applies events and holds no decisions.
  Values that travel together are one value type.
- Put a Razor component's C# in a code-behind file, never in an `@code` block.
  The code-behind is a `partial class` named for the component, in a file named for the markup file plus `.cs` (`KodiPlayerModal.razor.cs` beside `KodiPlayerModal.razor`).
  The markup file holds only directives and markup, and `ArchitectureTests` enforces this.
- Behaviour that belongs to a type is a member of it.
  An extension method lives in a static class named for the type it extends, in that type's folder, or in `Extensions/` when it extends a framework type (`Extensions/IQueryableExtensions.cs`), unless the receiver or element is a domain type.
  Then it lives in that domain type's folder, in a class named for the domain type (`Detecting/CropSampleExtensions.cs` extends `IEnumerable<CropSample>`).
  A chapter module is the exception: `<Chapter>Module` in the chapter's folder holds the extensions that add the chapter to the host, `Add<Chapter>` on `IServiceCollection` and, for Scanning, `AddScanJobs` on `IQuartzBuilder`.
  A method on a generic type parameter takes the name of its constraint.
- Follow the standard .NET coding conventions.
  The build refuses an unused `using`.
  Prefer descriptive names over short ones: spell out `AspectRatio`, never `Aspect` or `Ar`.
  A private method with one caller can be as long as its meaning needs.
- Use the names in the rewrite plan's *Names* table.
  It is Debarr's ubiquitous language: the UI and the code use the same words, and a name an operator wouldn't understand is changed on screen and in the code together.
  Name a new concept there before it gets a type.
  Before naming a method or a local, check that each noun in it is a row in the table or qualified by one.
- Name a method for the effect it has, in the domain's words, never for the step it runs.
  `FillFreeDetectionSlotsFromDetectionQueueAsync` starts detections, so its name says it fills the slots, not that it checks the queue.
- Qualify every noun with the domain term it stands for: `DetectionQueue` and `DetectionSlot`, never a bare `Queue`, `Slot`, `Item` or `Entry`.
  A noun that could mean two things in its chapter names the one it means.
- A method that takes a bounded amount names what bounds it and where the work comes from.
- `Try` marks a method whose result says whether it acted, such as `TryStart` returning a `Result`.
  A method for which doing nothing is a normal outcome takes no `Try`.
- A direction word matches the domain's flow: a method that takes files out of the detection queue never says `Enqueue`.
- A name uses the domain's words over the runtime's, such as `Task`, `Thread` or `Buffer`, unless the code is about the runtime.
- A rename carries to the locals, summaries and log messages that use the old word, in the same change.
- Names outside the code use `debarr`: environment variables (`DEBARR__SERVER__PORT`), the Docker image (`debarr:latest`) and the database file (`debarr.db`).
- These rules apply to the test project too.
  Generated code, such as Wolverine's pre-generated handlers, is exempt.

## Dependencies

- Use established, actively maintained NuGet packages instead of writing your own protocol clients, message framing, file-format parsers, or process wrappers.
  Only write your own when no suitable package exists or the logic belongs to this domain, such as turning cropdetect results into an aspect ratio.
- Before adopting a package, check that it is still maintained, widely used, under a permissive license, supports `net10.0`, and supports async with cancellation.
- Integrations use these packages:
  - **Kodi**: StreamJsonRpc carries JSON-RPC over TCP.
    Kodi's messages have no delimiter, so a custom message handler reads them.
  - **MQTT**: MQTTnet.
  - **Webhooks**: `IHttpClientFactory` with System.Text.Json.
  - **ffmpeg and ffprobe**: CliWrap runs the tools, and Debarr parses their output by hand.
- Every domain write is an event in Fisher, JasperFx's SQLite event store, in `debarr.db`.
  Pin its version and read each release's notes, since minor versions have shipped breaking changes.
  Every write runs one pipeline: a command, its handler, the aggregate it decides from, the events it appends, the read model projections those events update, and a `ReadModelChanged` after the commit.
  Read models are Fisher's own projection types, registered inline, so Fisher's rebuild clears them and resets their progress in one transaction.
  `dotnet run --project src/Debarr -- rebuild [read model]`, run with the app stopped, applies the schema and rebuilds every read model, or the one it names.
  JasperFx's `projections rebuild` fails on this app, since Fisher reports its usage under the database file's URI rather than the store's subject.
  A read model is a document read through Fisher's query session and indexed as `docs/critter-stack.md` lays out.
  One that folds a stream, such as Media, History and the library scan summaries, comes from a `SingleStreamProjection`, and the file paths, keyed by path, and the notifier deliveries, one per delivery, from a `MultiStreamProjection` whose every event writes the whole document.
  An inline multi-stream document whose fields come from more than one stream loses an update when two commits meet, so no read model is one.
  The settings, the players, the notifiers and a single video file are read by folding their aggregate's stream with the aggregate's `ReadAsync`, which stores nothing.
  Pages and processes that read the same facts share a read model, and each page shapes it with a query type of its own.
  Each projection's rebuild is tested with a document the replay can't recreate, in `ProjectionRebuildTests`, which fails when a read model has none.
  A rebuild skips archived streams, so a projection removes a stream's documents when it is archived, and a restore event carries the state its documents need.
  Until the first release a changed event is edited in place; after it, a changed event gets an upcaster.
- Commands run on Wolverine, pinned to one version, with its Fisher integration in Solo mode.
  A handler loads its aggregate with `[WriteModel]`, and reads one without writing with `[ReadModel]`, the attributes Wolverine's Fisher integration names.
  Its `Validate` returns a `Result`, or a `Result<T>` whose value its later methods take so the command is parsed once, and a failed one stops the command and becomes its reply, so a `FieldError` reaches its form.
  Every append to an existing stream states the version of the facts its decision was read from (decision Q17), since Fisher folds inline projections before it takes the write lock and checks only a stated version under it.
  `AppendVersionGuard` refuses a commit with an append that states no version, in every session, and `src/Debarr/BannedSymbols.txt` fails the build on the overloads that state none or read the stream's current version.
  A test arranges its events through `AppendAtCurrentVersionAsync`, or starts a stream with `StartStream`.
  A handler that decides across many streams reads the read model it decides from in a static `LoadAsync`, and returns a `FisherOps.Append(id, version, ...)` per stream at the version of the document it decided from, such as `MediaRow.Version`, which commit with its own events in one transaction.
  Work that follows a command once it commits, such as scheduling a scan, is the handler's static `AfterCommitAsync`, which can take the command, the aggregate as the handler decided from it, the value its `Validate` returned, and the services it needs.
  It catches and logs its own failure, since an exception there would reply with a failure for events that committed.
  Work that holds for the whole command, such as the detection pause, starts in the handler's static `BeforeAsync`, which Wolverine runs inside the command's log scope once the aggregate it takes has loaded, and ends in its `Finally`, which takes what `BeforeAsync` returned.
  A page sends a command with `SendCommandAsync`, whose `Result` is that refusal, a form error when the command throws or meets a version conflict, or a success once its events commit.
  A command carries no value back: a new stream's id comes with the command, and the page reads what changed from its read models.
  Wolverine discovers public classes named `*Handler`, so a class with that name that handles no message carries `[WolverineIgnore]`.
  Sagas, `TimeoutMessage` and Wolverine's scheduling are not used.
- In-process events use System.Reactive.
  The activity feed is an `IObservable<ActivityEvent>`, and subscribers filter, sample and batch it with Rx operators.
- Scans, scheduled and triggered, use Quartz.NET 4.1.1 or later with its in-memory job store.
  `[DisallowConcurrentExecution]` keeps apart two runs of one job key only.
  Different jobs that must not overlap, such as `LibraryScanJob` and `FolderScanJob`, put their triggers in one execution group (`WithExecutionGroup`) whose execution limit is 1 (`UseExecutionLimits`).
  A trigger the limit holds back stays waiting, so it keeps its misfire handling and runs when the group frees up.
  Quartz releases the group's slot only after the job's completion has woken the scheduler, so a held trigger can sleep for the whole idle wait, 30 seconds by default.
  The scheduler's `IdleWaitTime` is 1 second, the lowest Quartz accepts, which keeps that delay under a second.
- Detections run outside Quartz.
  `DetectionOrchestrator` polls the detection queue each second on a `PeriodicTimer` over the injected `TimeProvider`, runs each detection as a task, and holds the running list under one lock.
- An operation whose failure is an expected outcome returns FluentResults' `Result` or `Result<T>`, such as `Result<ContainerMetadata>` from `FfprobeRunner`.
  Tests assert on results with xUnit's `Assert`.
- Package versions are managed centrally.
  Add a `PackageVersion` entry to `src/Directory.Packages.props`, and use a `PackageReference` with no version in the project file.
- When you report a change, list any packages you added.

## Project structure

```
src/
  Debarr.sln
  Dockerfile        The image, built with src/ as the context
  Debarr/           The app: domain, services, UI, host and infrastructure
  Debarr.Tests/     Tests for Debarr, in folders that mirror it
```

The app's domain is in the chapters `Scanning/`, `Detecting/`, `Playing/`, `Notifying/` and `Appearance/`, each with its aggregates, events, slices, and the services, jobs, integrations and health checks that serve it: the ffmpeg runners in `Detecting/`, the Kodi connection in `Playing/`, and the MQTT and webhook clients in `Notifying/`.
Its other folders are `Activity/`, `Health/`, `Hosting/`, `EventStore/`, `Internal/`, `Components/` and `Extensions/`.
`Activity/` holds the feed: `ActivityEvent`, `IActivitySource`, `ActivityFeed`, `CommittedEvent` and `ReadModelChanged`.
Each activity event lives in the folder of the code that raises it, such as `ScanFinishedEvent` in `Scanning/` and `HealthChangedEvent` in `Health/`.
Each chapter's module, such as `Scanning/ScanningModule.cs`, registers the chapter's services, processes, activity sources and health checks, the aggregates its pages fold, and its read models' projections through `ConfigureFisher`.
`Program.cs` adds the chapters through their modules and keeps the host's own registrations and the order the hosted services start in.
`Health/` holds what every health check shares: `HealthCheckService`, `IHealthCheck`, `HealthCheckKind`, `HealthMessage` and `HealthSeverity`.
Each document's type lives with its read model in its chapter.
`EventStore/` holds what Fisher and Wolverine need from Debarr: the command middleware, the policy that opens each command's log scope first, the guard that refuses an append with no version, the strategy that replies with a refusal, the failure reply, the error a write conflict replies with, the listener that publishes each committed event as a `CommittedEvent` and then `ReadModelChanged`, the `rebuild` command, `IFilePathCommand`, which names the path in a command's log line, `FoldedAggregate`, which names an aggregate the pages fold, and the extensions that add the event store (`AddEventStore`), add a folded aggregate (`AddFoldedAggregate`), and send a command (`SendCommandAsync`).
`SendCommandAsync` holds the one retry every command shares: a command that meets a write conflict decides again on a fresh read, and replies with the failure when its retries run out.
It is the one call that runs a command, and `src/Debarr/BannedSymbols.txt` fails the build on Wolverine's `InvokeAsync` anywhere else.
`Internal/Generated/` holds Wolverine's generated handlers, which `codegen write` rewrites.
Types are public, and a nested type that is an implementation detail is private.
Write an interface when more than one type implements it, when a test needs a double for it, or when it makes the code simpler or easier to follow.
A factory interface, for example, can move the logic that chooses and builds an object out of the class that uses it.

## Packaging

The image publishes `Debarr` framework-dependent onto `mcr.microsoft.com/dotnet/aspnet` and copies a static ffmpeg and ffprobe, pinned by digest, into `/usr/local/bin`.
The build command is `docker build -t debarr:latest src`.

- The container runs as a non-root user and never runs `chown`, at build or run time.
  The Dockerfile gives the user its paths with `COPY --chown`.
  Every writable path stays under the data directory.
  Code that needs to write somewhere new writes it there.
- Media is mounted read-only.
- Read `TMPDIR` for scratch files rather than naming `/tmp`, so the image runs with `--read-only`.
  The image sets it to `/data/tmp`, which startup creates.
- The image runs Wolverine's pre-generated handlers (`TypeLoadMode.Static`).
  The Dockerfile runs `codegen write` before `publish`, the generated code is committed, and Debug builds generate it at runtime instead.
  In that mode Wolverine fails the host at startup when a handler that `GeneratedHandlerRegistry` lists has no pre-generated type.
- Publish restores, because the static web asset manifest has to cover the Razor components.
  `--no-restore` after a csproj-only restore layer drops `blazor.web.js` from `wwwroot/_framework` and the UI never opens its circuit.
