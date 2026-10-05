# Debarr architecture review, 2026-10-02

This is the saved copy of the review published as the private artifact https://claude.ai/artifact/HEiSS7KSfEUVk5Kaj4ewug, taken after the spikes, the operator's decisions and the task list were added.
Its *Tasks* section is mirrored in [rewrite-plan.md](rewrite-plan.md) as milestone M9, tasks 65 to 84, which is where their status is kept; task n in the *Tasks* section is task 64 + n in the plan.
The spike worktrees it cites are at `C:/repos/gitea/ryan/debarr-spikes/spike1` to `spike6`, on branches `spike/1` to `spike/6`, uncommitted.

Branch `domain-model-event-store` at `ac7a447`, M8 done apart from task 64.
Reviewed 2026-10-02; six spikes run the same day in throwaway worktrees; decisions taken and tasks written the same day.
A review, not an implementation: no code on the branch was changed.

## Summary

The M8 design is sound where it was measured.
Fisher's documents and folds, Wolverine's handlers with pure `Validate` and `Handle`, the Quartz execution group, and the Rx pipelines in `PlayerConnectionService`, `PlaybackHandler`, `HealthCheckService` and `LiveComponentBase` all serve the goals and should stay.
No proposal here reverses a decision from Q1 to Q16, and none touches the projection storage that tasks 54, 58 and 60 timed.

The accidental complexity sits in four places.
First, the activity feed: thirteen types push into their own subjects to announce facts the event store already committed, and four services exist only to do that.
Second, placement: six scan slices sit in `Detecting/`, every integration and health check sits outside the chapter it serves, and several domain rules hide under `Extensions/`.
Third, the composition root: `Program.cs` holds 63 registrations that each chapter could own, and twelve test classes copy them by hand.
Fourth, the UI: two pages carry the same paged-table plumbing, five carry the same save flow, and three modals the same frame.

The fourteen proposals below would delete about 1,300 app lines and 1,300 test lines, add about 700, move about 1,200 into their chapters, and let the operator remove a dozen sentences from the docs and four bullets from `AGENTS.md`.
The adversarial pass broke one draft proposal and reshaped another; six spikes then settled every open framework question: proposals 5 and 14 are proven in their final shape, the constructor-injection sweep in proposal 8 and the Meziantou analyzer in proposal 11 are dropped, replacing the refusal strategy is disproven, and the removal-versus-scan race is a reproduced defect.
Six features were put to the operator as decisions, not refactors; the operator took them, and the Tasks section turns the result into twenty commit-sized tasks.
- 924 tests passed, 0 failed, 2 min 39 s
- 12,555 app lines outside generated code
- 15,867 test lines, 718 methods
- 2,783 doc lines across seven files
- 63 registrations in Program.cs

#### Goals, as numbered in the brief
- 1 Less complexity
- 2 Less boilerplate and wiring
- 3 Stronger vertical slices
- 4 More declarative code
- 5 Processes as Rx pipelines
- 6 A ubiquitous language
- 7 Findable code
- 8 Subtract first
- 9 Rules in structure
- 10 Docs count as complexity
- 11 Types encode the domain
- 12 Composition root
- 13 Blazor components
- 14 Features versus their cost

#### The six leads

- **Lead 1, activity from one source.** Verified, with corrected counts: thirteen `IActivitySource` implementations, ten `Subject.Synchronize` sites and one plain subject.
  Eight saved-settings events, `HistoryCleared`, `VideoFilesArchived`, `FileHashed` and `FilePathDeleted` restate stored events the commit listener already holds; three pushed events have no reader at all.
  Runtime state (scans, detections, removals, connection states, health, host settings) has no stored event and stays a push source.
  Proposal 1.
- **Lead 2, the nine services.** Four only forward a command and push an event (`UISettingsService`, `OverrideService`, `PlayerSettingsService`, `NotifierSettingsService`).
  `LibrarySettingsService` carries behaviour but decides each command a second time against a stale read (proposal 5).
  `DetectionSettingsService` carries the pause and a save lock; spike 2 showed the pause moves into the slice and the retry replaces the lock (proposal 14).
  `PlayerConnectionService`, `LibraryStartupService`, `HealthCheckService` and `NotificationPublisher` are processes and stay.
- **Lead 3, enum and extension pairs.** Rejected as a pattern: `AspectRatioSource`, `DetectionOrigin` and `VideoFileStatus` are stored enums whose `ToDisplayText` is UI wording, and the extension beside the enum follows the project's own rule.
  Two exceptions: `NotSentReason` should be cases of `PlaybackOutcome` (proposal 7), and `ToNotificationAspectRatioSource` belongs to Playing (proposal 6).
  On the UI side, a status's word, icon and colour live in three switches across two folders and want one table per enum.
- **Lead 4, single-implementation interfaces.** Keep `IAspectRatioDetector`: `DetectorDouble` (79 lines) drives 85 tests in six fixtures, and faking ffmpeg would cost more.
  Drop `IPlayerConnection`: no test double exists anywhere; every test drives the real connection over `FakeKodiServer`.
  Principle 6 argues for keeping the seam, and the factory's switch keeps it either way (proposal 9).
- **Lead 5, chapter boundaries.** Verified: six scan-only slices sit in `Detecting/`, against the domain model's own *Folders* text (proposal 2).
  `Players` and `Playback` belong in one chapter.
  The Detecting-to-Playing dependency is one extension method (proposal 6).
- **Lead 6, folders outside the chapters.** `Integrations/`, the five health checks and 30 of `Activity/`'s 34 files belong to the chapter that serves or raises them (proposal 3).
  `Hosting/` and `EventStore/` belong where they are.

## Ranked proposals

Ranked by value to the goals over cost and risk, deletions ahead of additions at equal value.
Line counts are approximate and were checked by the adversarial pass.

| # | Proposal | Goals | Value | Cost | Risk | Operator decision |
|---|---|---|---|---|---|---|
| 1 | Activity from committed events at one source; four forwarding services, twelve event types and three unread events go | 1 2 3 5 8 10 13 | One push source for committed facts; pages send their own commands; six Rx-doc sentences go | app −300 +50; tests −575 +120 | medium | Q13 wording; one subject; History count |
| 2 | Move the six scan-only slices from `Detecting/` to `Scanning/` | 3 7 6 | A change to the scan touches one folder | 0 (200 moved) | low | none |
| 3 | Each integration, health check, activity record and hidden domain rule lives in its chapter; `Integrations/` goes | 3 7 6 | A change to Kodi touches `Playing/` only | −12 (about 1,000 moved) | low | two doc sentences |
| 4 | Each chapter registers itself; hosted-service order stays in `Program.cs` | 3 12 2 7 | `Program.cs` 221 to about 120; tests stop copying the wiring | −100 +120 across six files | low | AGENTS.md extension rule |
| 5 | `LibrarySettingsService` re-decides each command; its follow-up work moves into the slice's `AfterCommitAsync` (spike: proven) | 1 4 8 13 3 | One decision per command; the page sends it | −148 +100; tests −110 | low | decided: types public; race fix chosen |
| 6 | A pure static decision for playback steps 2 to 6 | 4 1 3 | The core rule tested without a host; one cross-chapter dependency gone | −35 +45; tests −150 +40 | low | none |
| 7 | Closed hierarchies where a nullable pair or a flag stands for a concept | 11 8 6 | Illegal states unrepresentable; 51 lines of checks go | −60 +80; import tool | medium | scan outcome wording; sequence with task 64 |
| 8 | `Components/`: one paged table, one edited form, one modal frame, one host-setting field, one render loop (spike: proven) | 2 13 9 | Each UI convention written once | −550 +300 | low | one ui-conventions line |
| 9 | Delete one-caller layers and the interface without a double | 8 1 7 | Fewer layers between a question and its answer | −180 | low | `IPlayerConnection`; the `Playback` fold |
| 10 | Value types own their bounds; the form annotations go | 11 9 4 | Port 0 and QoS 7 refused everywhere, not only in the modal | −30 +45 | low | one *Aggregates* row; inline validation |
| 11 | Rules in structure: IDE0130, one architecture test class, a rebuild theory (spike: proven) | 9 10 | Three AGENTS.md bullets become build errors or tests | +100 −50 tests; +3 config | low | none |
| 12 | Ubiquitous language fixes in UI text, ids, query values and docs | 6 10 | One word per concept | ±0 | low | *Remove* versus *Delete* |
| 13 | Test hosting: one app context, one `TestHost` over the chapter modules | 2 1 | Twelve copied host setups go | tests −250 | low | none |
| 14 | Detection settings: the pause moves into the slice and `DetectionSettingsService` goes (spike: proven) | 1 2 3 4 8 13 | One decision per save; five parses become one | −110 +45 | low | commit order of concurrent saves |

Order of landing: see Tasks, which sequences the proposals after the decisions.

### 1. Activity from committed events at one source

#### Problem

Thirteen types implement `IActivitySource`, ten hold `Subject.Synchronize(new Subject<...>())`, and `Program.cs` forwards seven of them with their own `AddSingleton<IActivitySource>` line.
Four exist only to call `SendCommandAsync` and push one event on success: `Appearance/UISettingsService.cs:11-29` (30 lines), `Detecting/OverrideService.cs:11-29` (30), `Playing/PlayerSettingsService.cs:14-42` (43) and `Notifying/NotifierSettingsService.cs:14-33` (34).
`DetectionSettingsService.cs:21,66,86`, `LibrarySettingsService.cs:24,49,131,140` and `PlaybackHandler.cs:39,102,151,157` hold a subject beside real behaviour.

Every one of those events restates a stored event the same command appended: `UISettingsSavedEvent` is `UISettingsChanged`, `OverrideSavedEvent` is `OverrideSaved`, `PlayerSavedEvent` and `PlayerDeletedEvent` are `PlayerAdded`, `PlayerChanged`, `PlayerRenamed` and `PlayerRemoved` (each carries `PlayerId`, `Playing/Events.cs:6-12`), and so on for notifiers, detection settings, the library and `HistoryCleared`.
`EventStore/ReadModelChangeListener.cs:36-52` already receives every committed event in `commit.GetEvents()` and keeps only the stream keys.
Three pushed events have no reader in the app at all: `PlaybackHandledEvent`, `DeliveryFinishedEvent` and `DetectionResultsClearedEvent`; only tests poll them.

The UI's whole need from the saved events is one sentence per kind in `Components/Layout/ActivityMessageArea.razor.cs:58-75`.
Two processes depend on the push rather than the commit: `Playing/PlayerConnectionService.cs:44-49` merges `playerSettingsService.PlayerEvents`, and `Scanning/FolderWatcher.cs:39-41` filters `LibrarySettingsSavedEvent`.
A commit to `Players` that bypasses the wrapper reconnects nothing; tests already append to `Players` directly (`PlayersPageTests.cs:26-30`, `PlayerConnectionHealthCheckTests.cs:51-55`) and no test checks that path.
No component sends a command directly today, although `docs/domain-model.md` *Commands* says "Pages send them through `SendCommandAsync`", and `PageTestContext.cs:115` already does.

#### Change

`ReadModelChangeListener` publishes each committed event once, wrapped as `CommittedEvent(IEvent Event) : ActivityEvent`, from the same `AfterCommitAsync` loop, with a catch-and-log around `OnNext` so a throwing subscriber can never turn a committed command into a failed reply.
Then, in one wave with proposal 5:

- Delete the four forwarding services, the twelve `Activity/*Event.cs` records they and the two settings services push, the three unread events and their `OnNext` calls, seven `AddSingleton<IActivitySource>` lines and four service registrations.
- `UIPage`, `VideoFileDetailPage`, `KodiPlayerModal`, `MqttNotifierModal` and `WebhookNotifierModal` inject `IWolverineRuntime` and call `SendCommandAsync`.
- `ActivityMessageArea.Describe` matches stored events: `UISettingsChanged` gives "UI settings saved.", `PlayerRemoved` gives "Player removed.", and so on, one arm per stored event.
  Its one-second `Buffer` and `Distinct` still collapse a rename's two events into one message.
- `PlayerConnectionService` keys on the committed player events.
  `FolderWatcher` triggers on `ChangesTo(nameof(Library))`, safe because its pipeline already ends in `DistinctUntilChanged` on the root path list (`FolderWatcher.cs:46-47`).
- `DetectionSettingsService`, `LibrarySettingsService` and `PlaybackHandler` lose their subjects and `IActivitySource`.

*Before, UIPage.razor.cs:52*

```csharp
await UISettingsService.SaveAsync(
    model.ToSaveUISettings(), CancellationToken.None)
```

*After*

```csharp
await Runtime.SendCommandAsync(
    model.ToSaveUISettings(), CancellationToken.None)
// UISettingsService.cs, UISettingsSavedEvent.cs
// and Program.cs:124-125 are gone.
```

*Before, PlayerConnectionService.cs:44-49*

```csharp
_events = Observable
   .Defer(() => _startupPlayerIds
       .Select(PlayerEvent (id) => new PlayerSavedEvent(id))
       .ToObservable())
   .Merge(playerSettingsService.PlayerEvents)
   .GroupBy(playerEvent => playerEvent.PlayerId)
   .SelectMany(CurrentPlayerConnectionEvents)
   .Publish();
```

*After*

```csharp
_events = Observable
   .Defer(() => _startupPlayerIds
       .Select(id => new PlayerChange(id, Removed: false))
       .ToObservable())
   .Merge(listener.CommittedEvents
       .Select(PlayerChange.From).OfType<PlayerChange>())
   .GroupBy(change => change.PlayerId)
   .SelectMany(CurrentPlayerConnectionEvents)
   .Publish();
// PlayerChange.From: a four-arm switch over PlayerAdded,
// PlayerChanged, PlayerRenamed, PlayerRemoved; null otherwise.
```

#### Deletes and adds

App: about 300 lines deleted (137 of services, 55 of event records, 16 of registrations, three subjects, `ClearHistoryAsync`), about 50 added.
Tests: `UISettingsServiceTests` (54), `OverrideServiceTests` (124), `PlayerSettingsServiceTests` (191) and `NotifierSettingsServiceTests` (205) go; three cases move (the notifier settings round trip to `EventSerializationTests`, the kept path mapping to `SavePlayerTests`, an override saved through the page to `VideoFileDetailPageTests`); one new case commits `PlayerChanged` with no wrapper and sees the reconnect, which fails today and passes after.
`PlaybackHandlerTests` waits on the deleted events at about 20 sites and moves its sync point to `CommittedEvents.OfType<DeliveryFinished>()`, a rewrite of its waiting rather than a deletion.

Docs: `reactive-extensions.md` *Subjects* loses "a service reporting the writes it commits", and *Debarr's streams* loses the sentences on the per-service subjects, on `PlayerConnectionService` reading a player on each `PlayerSettingsService` event, on `FolderWatcher` reading root folders on each `LibrarySettingsService` save, and on `PlaybackHandler`'s stored-playback events.
`ui-conventions.md`'s "A service returns such a refusal as a `FieldError`" becomes "A handler returns".

#### Conflicts

Q13 says "the activity feed keeps its sources and `ReadModelChangeListener`" against a Wolverine wire-tap bridge measured in task 53.
This proposal uses the same listener and no Wolverine hook, so it reverses no measurement, but Q13 needs a sentence.
The Rx doc requires the operator's agreement for every subject: the listener's subject is reshaped (or a second one added) to carry the committed events.
*Behaviour* "Activity messages" holds.
`SetRootFolderEnabled` on an already-matching root appends nothing, so it shows no message where today's service pushes one; arguably more correct.

> **Decision.** `HistoryClearedEvent.Count` is read before the command (`PlaybackHandler.cs:92-95`) and can be off.
> Either `ClearHistory`'s handler counts in `LoadAsync` and `HistoryCleared` carries the count (an event edited in place, allowed before release), or the message becomes "History cleared.".

#### Framework check

Fisher 1.14.0 `Fisher.xml`, `IDocumentSessionListener` remarks: `AfterCommitAsync` "runs after the commit and outside [the SQLITE_BUSY retry] because a retried SQLITE_BUSY re-executes the whole delegate and a hook invoked inside it would fire twice"; it does not fire for an empty unit of work or a session enlisted in a transaction it does not own; "an exception from AfterCommitAsync reaches the caller of SaveChangesAsync but the transaction has already committed".
So the listener completes before `SendCommandAsync` returns to the page, and a subscriber exception would reach Wolverine's error handling, hence the catch around `OnNext`.
`IChangeSet.GetEvents` is "Every event appended across every stream in this unit of work".
`CommandPipelineTests` and `UISettingsProviderTests` already prove Wolverine's commits reach the listener.

#### Risk and verification

Medium: two processes change their trigger, and players seeded straight into `Players` by `PlayersPageTests`, `StatusPageTests` and `PlayerConnectionHealthCheckTests` will now open connections, which those tests must expect.
Run `PlayerConnectionServiceTests`, `FolderWatcherTests`, `ActivityMessageAreaTests`, the page tests that save, and the new reconnect-on-commit test.
Live check: save each settings page and read the navigation message on a second tab; save a player and watch its connection restart on Status; clear the history.

### 2. Move the six scan-only slices to Scanning

#### Problem

`Detecting/AddFilePath.cs`, `RemoveFilePath.cs`, `RemoveFilePaths.cs`, `ArchiveVideoFiles.cs`, `UnarchiveVideoFile.cs` and `StoredFilePath.cs` (200 lines) are sent or read only by `Scanning/LibraryScanner.cs` (lines 186, 351, 355, 372, 425, 430, 433, 445, 452) and `LibraryPage.razor.cs:52`.
`FileHash.ComputeAsync` has one caller, `LibraryScanner.cs:394`.
`docs/domain-model.md` *Folders* gives `Scanning/` "the library, folder and file scans" and already says "A slice may load an aggregate another chapter owns, such as a library scan appending to a video file".
The *Names* table files *Video file*, *File path*, *Stored file path*, *File stat*, *File hash* and *Archived* under *The library*.
Nine files in `Detecting/` import `Debarr.Scanning` already.

#### Change

Move the six files and their tests (`ArchiveVideoFilesTests`, `StoredFilePathTests`, the scan half of `VideoFileConcurrencyTests`) to `Scanning/`; namespaces only.
Move `IFilePathCommand.cs` (9 lines) to `EventStore/`, since `RecordDetection` implements it too and its one reader is `SendCommandAsync`'s log line.
Keep `VideoFile.cs`, `Events.cs` and `FileHash.cs` in `Detecting/`: the aggregate's rules are detection rules and `Events.cs` holds every event the aggregate appends.
Run `codegen write` after the move.

#### Deletes and adds, goals, conflicts

No lines change.
`Detecting/` goes from 41 files to 35 and `Scanning/` from 22 to 28.
No conflict: the *Folders* text already describes this placement.
Wolverine discovers handlers by assembly scan (`ApplicationAssembly`, `IServiceCollectionExtensions.cs:52`), so no registration moves.
Verification: build and the moved tests.

### 3. Each integration, health check, activity record and hidden rule lives in its chapter

#### Problem

Every file in `Integrations/` imports the chapter it serves and is used by it alone: the ffmpeg runners and parser import `Debarr.Detecting` (`CropDetectParser.cs:3`, `CropDetectRunner.cs:3`, `FfprobeRunner.cs:5`), the Kodi connection imports `Debarr.Playing` (`KodiPlayerConnection.cs:5-8`), the notifier clients and factory import `Debarr.Notifying`, and `Notifying/NotificationPublisher.cs:2` imports `Debarr.Integrations` back.
Each of the five health checks reads one chapter; only `HealthCheckService`, `IHealthCheck`, `HealthMessage` and `HealthSeverity` (174 lines) are shared.
`Activity/` holds 34 files; 30 are one-line records raised by exactly one chapter's process, and `PlaybackStartedEvent`, the *Names* table's *Input*, imports `Debarr.Playing` while *Folders* says an input "keeps its own file" in the chapter.

Domain rules hide in `Extensions/`: the cropdetect box-grouping rule (`IEnumerableExtensions.cs:11-64`, a *Constraints* rule with one caller, `AspectRatioDetector.cs:97`), Kodi's path canonicalisation (`StringExtensions.cs:28-65`, the *Names* table's "player endpoint canonicalises that type's player paths"), the Serilog line parser (`IEnumerableExtensions.cs:70-126`, bound to `LogFileDirectory`'s template), and `Notification`'s timestamp pair (`DateTimeOffsetExtensions.cs` and `StringExtensions.ParseUtcTimestamp`, both used only at `Playing/Notification.cs:29,32`).
`TimeSpanExtensions.ToDisplayText` formats with `CurrentCulture` where every other formatter uses `InvariantCulture`, so a comma-decimal host renders "3,2 s" beside "2.39".

#### Change

- Move the ffmpeg runners and parser to `Detecting/`, the Kodi connection, message handler and factory to `Playing/`, the notifier clients and factory to `Notifying/`; delete `Integrations/` (13 files, 826 lines moved).
- Move each health check into its chapter; `Health/` keeps the four shared types.
- Move the 30 activity records to the chapters that raise them; `Activity/` keeps `ActivityEvent`, `IActivitySource`, `ActivityFeed` and `ReadModelChanged`.
- Move the crop-sample grouping to `Detecting/` as members of a `CropSamples` value or an extension on `IEnumerable<CropSample>`, `ToKodiPlayerPath` into `KodiEndpoint`, the log parser and `ReadLastLinesAsync` beside `LogEntry` in `Hosting/`, the timestamp pair into `Notification.cs`.
- Move `AddEventStore`, `SendCommandAsync` and `DecideAgainOnConcurrentAppend` to `EventStore/`, and each aggregate's `Read*Async` fold beside its aggregate.
- Fix `ToDisplayText` to `InvariantCulture`.
  `Extensions/` is left with about 12 files of true framework helpers.

#### Deletes and adds, conflicts, risk

About 1,000 lines moved, 2 files and 12 lines deleted.
`docs/domain-model.md` *Folders* and `AGENTS.md` *Project structure* each lose `Integrations/` from their list, and `AGENTS.md`'s extension placement rule gains "unless the receiver or element is a domain type", which is what core-principles' "`Extensions/` holds only extensions on framework types" intends.
The `exercise-kodi` and `debarr-live-check` skills name `KodiPlayerConnection`'s path and update in the same session.
Risk is low and mechanical; tests move with their subjects.

### 4. Each chapter registers itself

#### Problem

`Program.cs` (221 lines) holds 63 `builder.Services` calls: 45 `AddSingleton`, seven `AddHostedService`, five `IHealthCheck` and twelve `IActivitySource` forwards.
Scanning's wiring is spread over `Program.cs:101-122`, `Extensions/IQuartzBuilderExtensions.cs` (the jobs, the execution limit and the idle wait), `Extensions/IServiceCollectionExtensions.cs:35,39` (two projections) and `:42` (the hand-kept list of six folded aggregates); the same for every chapter.
Twelve test classes build their own host and copy `Program.cs`'s registrations by hand (about 360 lines; `PlaybackHandlerTests.cs:61-85` lists 15 and none of the `IActivitySource` entries), so a test host can silently differ from the app.

#### Change

A static class per chapter with `AddScanning(this IServiceCollection)`, `AddDetecting`, `AddPlaying`, `AddNotifying` and `AddAppearance`, each registering its services, health checks and activity sources, and contributing its projections and its folded aggregates through Fisher's `ConfigureFisher`.
Scanning's Quartz jobs stay an `IQuartzBuilder` extension, now in `Scanning/`, called from the one `AddQuartz`.
`Program.cs` keeps the host, the store, Quartz, Razor, MudBlazor and, in order, the seven `AddHostedService` lines with their two comments (`:144`, `:150`), because that order is behaviour.
The folded-aggregate list becomes explicit per chapter; a reflection scan was rejected because `VideoFile`'s stream id is per file and the scan would silence the video file page's live reload.

*Before: Program.cs:101-122 plus IServiceCollectionExtensions.cs:35,39,42*

```csharp
builder.Services.AddSingleton<LibraryScanner>();
builder.Services.AddSingleton<IActivitySource>(s => s.GetRequiredService<LibraryScanner>());
//... nine more lines for Scanning...
// and in AddEventStore:
LibraryScanSummaryRowProjection.AddTo(options);
StoredFilePathProjection.AddTo(options);
options.Listeners.Add(new ReadModelChangeListener(options.Projections,
    [typeof(Library), typeof(VideoFile), /* four more */]));
```

*After: Scanning/ScanningModule.cs (name goes in the Names table first)*

```csharp
internal static class ScanningModule
{
    public static IServiceCollection AddScanning(this IServiceCollection services)
    {
        services.AddSingleton<LibraryScanner>()
           .AddSingleton<IActivitySource>(s => s.GetRequiredService<LibraryScanner>());
        services.AddSingleton<RootFolderRemover>();
        services.AddSingleton<FolderWatcher>();
        services.AddSingleton<IHealthCheck, RootFolderHealthCheck>();
        services.AddSingleton(new FoldedAggregate(typeof(Library)));
        services.ConfigureFisher(options =>
        {
            LibraryScanSummaryRowProjection.AddTo(options);
            StoredFilePathProjection.AddTo(options);
        });
        return services;
    }
}
// Program.cs:
builder.Services.AddScanning().AddDetecting().AddPlaying().AddNotifying().AddAppearance();
// then the seven AddHostedService lines, in order, with their comments.
```

#### Deletes and adds

`Program.cs` 221 to about 120 lines; `AddEventStore` to about 35 (store, listener, Wolverine); each chapter gains 15 to 30 lines.
`IQuartzBuilderExtensions.cs` and the folded-aggregate list go.
Health messages of equal severity are ordered by registration order today (`HealthCheckService.cs:31-41`), so the chapter modules either keep that order or the service sorts by name.

#### Conflicts and framework check

`AGENTS.md`'s "An extension method lives... in `Extensions/` when it extends a framework type" is covered by proposal 3's sharpening.
"Read models are Fisher's own projection types, registered inline" holds: `ConfigureFisher` contributes to the same `StoreOptions` before the store is built.

Fisher 1.14.0 `ConfigureFisher(IServiceCollection, Action<StoreOptions>)`: "Contribute configuration to the primary store's options from a lambda (fisher#70)... integration code that layers its own StoreOptions contributions onto a store somebody else registered...
Runs after the AddFisher(...) lambda, in registration order, and may be called either side of it; the contributions are resolved when the store is built".
Wolverine 6.43.0 finds handlers by convention from `ApplicationAssembly` (`HandlerDiscovery`, `WolverineOptions.ApplicationAssembly`), and offers `IWolverineExtension` ("Use to create loadable extensions to Wolverine applications") should a chapter ever need Wolverine options; none does today.
Quartz 4.2.0 `IQuartzBuilder` "Configures a scheduler and the services it is built from...
Members return the builder so configuration can be chained"; `AddScanJobs` already is such an extension; `ConfigureAllQuartzSchedulers` ("Configures every Quartz scheduler in the container, whenever it was registered...
The order of the calls does not matter") is the alternative if the jobs should register without touching `AddQuartz`.
Hosted services start in registration order, which the two comments rely on today.

#### Risk and verification

Low.
`ProgramTests`, `CommandPipelineTests`, `HealthCheckServiceTests`; the live check for the start order.

### 5. LibrarySettingsService re-decides; its follow-up work moves into the slice

#### Problem (verified first-hand)

`Scanning/LibrarySettingsService.cs:85-86` reads the library, then calls `SetRootFolderEnabledHandler.Validate` and `Handle` itself before sending the command, and switches on that copy at 93-101 to trigger a scan or queue a removal; `RemoveRootFolderAsync` does the same at 109, and `SaveSettingsAsync` (36-47) reads the library before the send to compare intervals.
So each decision runs once in Wolverine and once here, against a read taken before the command's transaction, and two circuits saving at once can disagree with what committed.
`AddRootFolderAsync` (59-69) also checks the full path and the folder's existence before the command.
The follow-up work (reschedule, *Scan Now*, unschedule folder scans, enqueue the removal) is real behaviour and the reason this service survives lead 2.

#### Change

The follow-up work moves into each slice as a Wolverine `AfterCommitAsync` method that takes the command and the `[WriteModel]` library the handler loaded.
It keys on the command, not on every commit, so test seeding through store sessions does not start scans; the loaded aggregate is the "before" state, so "saving a new scan interval restarts that clock" needs no held state; a failure surfaces as it does today, because the method runs inside the command before `SendCommandAsync` returns.
`LibraryPage` sends `AddRootFolder`, `SetRootFolderEnabled`, `RemoveRootFolder` and `ChangeLibrarySettings` itself.
The full-path and folder-exists checks stay at the page boundary or in a static on the slice outside `Validate` and `Handle`, which stay pure.
`TasksPage` calls `LibraryScanJob.TriggerNowAsync` directly and reloads its next run on a `ScheduleChanged` stream adapted from Quartz's `ISchedulerListener` the way `ObserveJobs` adapts `IJobListener`, which replaces the reload `LibrarySettingsSavedEvent` gave it.
`LibraryStartupService` stays as it is.

*Before, LibrarySettingsService.cs:83-104*

```csharp
var library = await ReadLibraryAsync(ct);
var decided = SetRootFolderEnabledHandler.Validate(command, library).IsSuccess
    ? SetRootFolderEnabledHandler.Handle(command, library) : [];
var result = await SendAsync(command, ct);
if (result.IsFailed) return result;
switch (decided.SingleOrDefault())
{
    case RootFolderEnabled: await ApplyRootFolderEnabledAsync(ct); break;
    case RootFolderDisabled d: await ApplyRootFolderDisabledAsync(d.Path, ct); break;
}
```

*After, in SetRootFolderEnabled.cs*

```csharp
public static async Task AfterCommitAsync(
    SetRootFolderEnabled command, [WriteModel] Library library,
    ISchedulerFactory schedulers, RootFolderRemover remover, CancellationToken ct)
{
    var was = library.FindRootFolder(command.Path)?.Enabled;
    if (was == command.Enabled) return;
    var scheduler = await schedulers.GetScheduler(ct);
    if (command.Enabled)
        await LibraryScanJob.TriggerNowAsync(scheduler, ct);
    else
    {
        await FolderScanJob.UnscheduleUnderAsync(scheduler, new DirectoryInfo(command.Path.Value), ct);
        remover.Enqueue(command.Path);
    }
}
// The page: () => runtime.SendCommandAsync(new SetRootFolderEnabled(root.Path, enabled), ct)
```

#### Deletes and adds

`LibrarySettingsService.cs` (142), `Activity/LibrarySettingsSavedEvent.cs` (4) and `Program.cs:116-117` deleted; about 60 lines of `AfterCommitAsync` across four slices, about 30 for the schedule adapter.
`LibrarySettingsServiceTests` (360 lines, 16 tests) keeps its assertions as slice and page tests, and its five host-level cases that repeat `LibraryTests` (196-280) go.

#### Conflicts

*Behaviour* "Adding or enabling a root folder runs a library scan" and "saving a new scan interval restarts that clock" hold.
The Rx doc sentences on `LibrarySettingsService` go with proposal 1.
The alternative design, a store-wide reaction on `ChangesTo(nameof(Library))` with a pure `Diff(before, after)`, was demoted: it fires on every test seed inside a full app host and on imports, and the page would stop seeing a scheduler failure.

#### Framework check

Wolverine 6.43.0 `Wolverine.xml`: a method "conventionally named AfterCommit / AfterCommitAsync" on a handler type "should be called after the transactional commit, rather than merely after the handler", its position "is structural and does not depend on the order policies happened to run in", and "these methods do NOT run when the commit throws".
Spike 1 (worktree `spike/1`) built the method on `SetRootFolderEnabledHandler` and read the generated handler: the call is emitted after `SaveChangesAsync` and `FlushOutgoingMessagesAsync`, inside the try and inside the `{Command} {MessageId}` scope; it binds the command, the `[WriteModel] Library` as the same pre-commit aggregate the handler decided from, `ISchedulerFactory`, `ILogger`, the events `Handle` returned, the `IEventStream<Library>` and the `CancellationToken`; it runs when `Handle` returns no events and does not run when `Validate` refuses.

Two qualifications the spike found.
A public handler method can name only public types, so `RootFolderRemover` (internal) does not compile as a parameter (CS0051), and making it public cascades through `LibraryScanner` into the activity types; decision 12 makes every type public, so the slice takes `RootFolderRemover` directly.
An exception in `AfterCommitAsync` reaches the page as a failed `Result` worded "Nothing was saved." although the events committed, so the method catches and logs, or `CommandReply` gains a post-commit wording.

#### Risk and verification

Low: the binding and placement are measured.
`A_saved_interval_schedules_the_next_scan...`, `A_save_that_keeps_the_interval_leaves_the_scheduled_scan_alone`, `An_enabled_root_gets_a_library_scan...` and `A_removed_root_loses_its_file_paths_in_the_background...` keep their assertions.
Live check on *Settings > Library*: add, disable, enable and remove a root folder, then the next run on *System > Tasks*.

> **Confirmed defect, a decision because every fix edits a *Behaviour* sentence.** Spike 5 reproduced the removal-versus-scan race deterministically in both orderings (worktree `spike/5`, `RootFolderRemovalRaceTests`, a Fisher session listener as the interleaving hook).
> Ordering A: the removal's `RemoveFilePaths` lands, the scan's `AddFilePath` retries and re-adds the path, the removal's `ArchiveVideoFiles` keeps the file; the scan's sweep removes the path again but archives only files whose last path left before the scan started, so the file is archived one library scan later (`VideoFilesArchived = 0` at the end of scan 2, archived after scan 3).
> Ordering B: the removal archives first; the scan's `AddFilePath` hits the archived stream, is not retried, and the scan fails with "Event stream... is archived and cannot be appended to", recorded on its summary and on the root folder's last scan.
> The structural fix alone (the removal waits for the running scan) archives nothing when a scan was running, because the sweep removed the paths at the scan's own start time.
> The two-line fix (archive the video files of the `outside` paths after the sweep) closes A, passes 38 of 39 `LibraryScannerTests` (the one failure pins the sentence it changes) and does not touch B.
> Closing both needs the two-line fix plus either the structural fix or a scanner-side recovery that treats the archived-stream refusal as the signal to send `UnarchiveVideoFile` and `AddFilePath` once more.
> Each option edits *Behaviour* "Archiving at the end of a library scan" or "Root folders"; see feature challenge 6.

### 6. A pure static decision for playback steps 2 to 6

#### Problem

The *Behaviour* playback rules live in a local function `ChooseOutcomeAsync` inside `PlaybackHandler.cs:162-197`, which assigns `localPath` (179) and `videoFile` (182) to captured locals declared at 116-117 and read at 136-137, so what the playback records depends on how far the local function got before it threw.
Six host tests (`PlaybackHandlerTests.cs:143-303`) boot Fisher, Wolverine, Quartz, two fake Kodi servers and a stub webhook to check one switch arm each.
`Detecting/AspectRatioSourceExtensions.cs:7-13` `ToNotificationAspectRatioSource` has one caller (`PlaybackHandler.cs:193`) and is the only reason `Detecting/` imports `Debarr.Playing`.

#### Change

A pure static beside `PlaybackHandler` (one caller, so not on the aggregate, which would conflict with Q11 and AGENTS.md's "rules more than one slice uses"):

```csharp
internal static PlaybackOutcome Decide(Override? @override, Detection? currentResult,
    AspectRatio? playerAspectRatio, StandardRatios standardRatios) =>
    (@override, currentResult, playerAspectRatio) switch
    {
        ({ DontSend: true }, _, _) => new PlaybackOutcome.DontSend(),
        ({ AspectRatio: { } manual }, _, _) => new PlaybackOutcome.Sent(manual.Value, NotificationAspectRatioSource.Manual, null),
        (_, { Outcome: DetectionOutcome.Succeeded { Result: var result } } current, _) => new PlaybackOutcome.Sent(
            standardRatios.Snap(result.RawAspectRatio).Value,
            result.AspectRatioSource is AspectRatioSource.Detected ? NotificationAspectRatioSource.Detected : NotificationAspectRatioSource.Container,
            current.Id),
        (_, _, { } player) => new PlaybackOutcome.Sent(standardRatios.Snap(player).Value, NotificationAspectRatioSource.Player, null),
        _ => new PlaybackOutcome.NoPlayerAspectRatio(),
    };
```

`HandleAsync` becomes: stream check, read, translate, identify (step 1, a file scan), `Decide`, with `localPath` and `videoFile` as plain step results declared before the `try` so the failure path still records them.
`ToNotificationAspectRatioSource` is deleted and the mapping lives in Playing.

#### Deletes and adds, goals, risk

`HandleAsync` 86 to about 60 lines; 8 lines gone from Detecting; a theory of about 40 lines pins every arm and the six host duplicates go (about 150 test lines).
Goals 4, 1, 3; the `Detecting` to `Playing` dependency ends.
It moves toward AGENTS.md's "pure static functions, tested without a database" and conflicts with nothing once the function sits beside the handler.
Risk is low: the new theory pins every arm and `PlaybackHandlerTests` keeps one host test per integration concern.

### 7. Closed hierarchies where a nullable pair or a flag stands for a concept

#### Four instances

- `Playing/PlaybackOutcome.cs:20-36`: `NotSent(NotSentReason reason, string? error = null)` throws unless `error` is set exactly when the reason is `Failed`; `PlaybackOutcomeText.razor:9` checks the pair again; `PlaybackOutcomeTests` (20 lines) tests only that check; the *Names* table lists five cases.
  Make them five sealed records under `[JsonPolymorphic]`, as `DeliveryOutcome` already is, and delete `NotSentReason.cs`, `NotSentReasonExtensions.cs` and `PlaybackOutcomeTests.cs` (51 lines together).
  `PlaybackRow`'s indexed `Sent` and `DetectionId` are computed in C#, so no index changes.
- `Detecting/VideoFile.cs:131-141`: `Detection` holds `DetectionResult?
  Result, DetectionFailure?
  Failure`, so both or neither is representable, and four sites branch around it (`RecordDetection.cs:51-62`, `MediaRow.cs:97-99,105` with `??
  ""`, `VideoFile.cs:29` with `?.Result?.`).
  A closed `DetectionOutcome { Succeeded(DetectionResult); Failed(DetectionFailure) }`; the runner's existing `DetectionOutcome` becomes `DetectorOutcome`.
- `Detecting/DetectionSettings.cs:32-55`: `RecheckScope(bool All, IReadOnlyList<double>)` encodes three states in four; make it `All`, `Some(list)`, `None`.
- `Scanning/LibraryScanner.cs:111-114` catches every exception, `OperationCanceledException` included, into `string?
  Error`, and `LibraryScanSummaryRow` carries `Error` plus `bool Interrupted`, so `TasksPage.razor:50` shows cancelled and interrupted scans as failed.
  A `LibraryScanOutcome` (`Finished`, `Failed(Error)`, `Cancelled`, `Interrupted`).

Smaller, same shape: `LastScanText` takes `DateTimeOffset?
At, bool Failed`; `Detection.Path` is null and `Duration` zero when `Origin` is `StandardRatiosChange`; `string Path` in `DetectionRequest`, `RunningDetection` and the two detection activity events where `LocalPath` exists (`DetectionRunner.cs:47,63,68` unwraps and rewraps).

```csharp
[JsonPolymorphic]
[JsonDerivedType(typeof(Sent), "sent")] [JsonDerivedType(typeof(Stream), "stream")]
[JsonDerivedType(typeof(DontSend), "dont_send")] [JsonDerivedType(typeof(NoPlayerAspectRatio), "no_player_aspect_ratio")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record PlaybackOutcome
{
    private PlaybackOutcome() { }
    public sealed record Sent(double AspectRatio, NotificationAspectRatioSource Source, Guid? DetectionId) : PlaybackOutcome;
    public sealed record Stream : PlaybackOutcome;
    public sealed record DontSend : PlaybackOutcome;
    public sealed record NoPlayerAspectRatio : PlaybackOutcome;
    public sealed record Failed(string Error) : PlaybackOutcome;
}
```

#### Deletes and adds, conflicts

About 60 deleted and 80 added across the four.
Seven stored events change shape (`PlaybackHandled`, `AspectRatioDetected`, `DetectionFailed`, `DetectionResultConverted`, `VideoFileRestored`, `StandardRatiosChanged`, `LibraryScanEnded`), which AGENTS.md allows in place before the first release.
The cost the draft missed: the large-library import on `task60-large-library-timings` writes the old shapes (task 64 already notes this for `DeliveryFinished`), and the `.dev` data directories the live-check skills use must be re-imported, so this lands in one wave with task 64.
C# 14 checks no exhaustiveness over a closed record hierarchy, so a default arm stays; the gain is that "failed with no error" and "stream with an error" cannot be built.

> **Decision.** An interrupted scan shows as failed on purpose today (`LibraryScanSummaryRow.InterruptedError`), and *Behaviour* "Library scan summary" says a scan stores "its error, whether it finished, failed or was cancelled".
> Showing cancelled and interrupted scans apart changes that sentence and needs a word in the *Names* table.

#### Framework check and verification

`[JsonPolymorphic]` records inside Fisher events are already stored (`PlaybackOutcome`, `DeliveryOutcome`, `Players.cs:43-44`, `Notifiers.cs:42-44`) through `ConfigureSerialization(EnumStorage.AsString, Casing.CamelCase)`.
The projection rebuild tests prove the new JSON round-trips; the re-import proves the import.

### 8. Components: five repetitions become five shared pieces

#### Problem

- `MediaPage.razor.cs` and `HistoryPage.razor.cs` share about 120 identical distinct lines of paged-table and URL plumbing: the same fields (42-66 and 33-58), `ReloadAsync` to `TakeViewFromUrl` (109-181 and 118-193), `ShowPageAsync` to `LoadRowsAsync` (195-236 and 204-246), the sort, page and URL handlers (264-332 and 248-317), two comments word for word, and the chip set and search box markup three times (LogsPage too).
- Five pages repeat the save-and-reload-keeps-edits pattern with the same comment "Unsaved edits survive a reload." (`UIPage.razor.cs:14-16,26-38,42-60`; `GeneralPage`, `LibraryPage`, `DetectionPage`, `VideoFileDetailPage`), about 150 lines for one `ui-conventions.md` sentence.
- The three integration modals repeat a 21-line frame (`KodiPlayerModal.razor:85-105`, `MqttNotifierModal.razor:53-73`, `WebhookNotifierModal.razor:54-73`), and the Kodi modal re-implements `PlayerConnectionStateText`.
- `GeneralPage.razor:32-82` repeats the environment-variable lock wiring on six fields.
- The render-on-observable loop appears four times and the dispose ritual seven; `LogsPage` and `ElapsedTime` re-implement `LiveComponentBase`'s loop because their trigger is a timer.
- 25 `HelperTextOnFocus="false"` set MudBlazor 9.10.0's documented default; 13 three-attribute field-error hookups; 65 `[Inject]` blocks (195 lines).

#### Change

`PagedTableView<TRow>` with a `PagedTable<TRow>` component and a `FilterBar`; an `EditedForm<TForm>` value of about 25 lines (`Saved`, `Model`, `HasUnsavedChanges`, `Take`, `SaveAsync`); an `IntegrationModal` frame; a `HostSettingField`; a virtual `LiveComponentBase.Reloads` stream so `LogsPage` and `ElapsedTime` inherit the base; the 25 default attributes deleted and `ui-conventions.md:38` changed with them; `PageAction.FieldErrorAttributes(name)` splatted onto each field (spike 3 proved it: the dictionary with `Error` and `ErrorText` beside `@bind-Value` rendered the refusal beneath the field and cleared it on the next edit, and a negative control failed as expected).
Primary constructors replace the `[Inject]` blocks on the leaf components: spike 3 showed bUnit 2.11.3 and the real renderer both construct a component with a primary constructor, and its one blocker, that a public constructor can name only public types while the injected services were `internal`, is removed by decision 12 (every type public).
Base classes such as `LiveComponentBase` keep `[Inject]`.
Spike 3 also found that `Min="1"` on a `MudNumericField` clamps input, so that field's server refusal cannot be reached from the UI; the sweep over the 13 hookups should check each for the same.
Also move `DetectionFailureExplanation.cs` (112 lines, which parses Detecting's error strings) to `Detecting/`, with its *Names* role changed from *Read model* to *Value type, worked out*.

*Before, UIPage.razor.cs (60 lines)*

```csharp
private UISettingsForm? _saved;
private UISettingsForm? _model;
private readonly PageAction _save = new();
[Inject] private IDocumentStore Store { get; set; } = default!;
[Inject] private UISettingsService UISettingsService { get; set; } = default!;

protected override async Task ReloadAsync(CancellationToken ct)
{
    await using var session = Store.QuerySession();
    var saved = UISettingsForm.FromSettings(await session.ReadUISettingsAsync(ct));
    // Unsaved edits survive a reload.
    if (_model is null || _model == _saved) { _model = saved with { }; }
    _saved = saved;
}

private async Task SaveAsync()
{
    var model = _model with { };
    await _save.RunAsync(() => UISettingsService.SaveAsync(model.ToSaveUISettings(), CancellationToken.None), Logger, "The settings were not saved.");
    if (_save.Error is null &&...) { _saved = model; }
}
```

*After (about 30 lines)*

```csharp
public partial class UIPage(IDocumentStore store, IWolverineRuntime runtime)
{
    private readonly EditedForm<UISettingsForm> _form = new(model => model with { });
    private readonly PageAction _save = new();

    private protected override IReadOnlySet<string> ReadModels { get; } =
        new HashSet<string> { nameof(UISettings) };

    protected override async Task ReloadAsync(CancellationToken ct)
    {
        await using var session = store.QuerySession();
        _form.Take(UISettingsForm.FromSettings(await UISettings.ReadAsync(session, ct)));
    }

    private Task SaveAsync() => _form.SaveAsync(_save, Logger, "The settings were not saved.",
        model => runtime.SendCommandAsync(model.ToSaveUISettings(), CancellationToken.None));
}
// Markup binds _form.Model.Theme and passes HasUnsavedChanges="_form.HasUnsavedChanges".
```

#### Deletes and adds, conflicts, framework check

About 550 deleted and 300 added across the folder.
The keep-unsaved-edits rule and the outlined-dense convention become one type and one architecture test.
MudBlazor 9.10.0 XML: `HelperTextOnFocus` "Defaults to false"; `MudGlobal` holds only `MenuDefaults`, `TooltipDefaults` and `UnhandledExceptionHandler`, so the outlined-dense pair cannot be set once and the test pins it; `MudForm.Model` and `For` confirm that a settable form type per edited value is the right shape, so the form records stay.
Risk is low: `MediaPageTests` (10), `HistoryPageTests` (10) and the settings page tests pin the URL round trip and the save flows.

### 9. Delete one-caller layers and the interface without a double

- `Integrations/IPlayerConnection.cs` and `IPlayerConnectionExtensions.cs` (30 lines): one implementation and no test double anywhere; every test drives `KodiPlayerConnection` over `FakeKodiServer` (`IPlayerConnectionExtensionsTests.cs:39-41` builds the real one).
  `PlayerConnectionFactory.Create` returns `IObservable<PlayerConnectionEvent>` and gains `TestAsync`. core-principles: "an interface is written only for a second implementation or a test double".
- `NotifierClientFactory.cs` (15 lines): one caller, no double; fold into `NotificationPublisher`.
  Keep `INotifierClient`, which has two implementations.
- `RootFolderRemover.cs` (34 lines) passes through to `LibraryScanner.RemoveRootFolderAsync` (193-219), so the process named in the *Names* table lives in a file named for something else; move the 27 lines and `RunningRootFolderRemoval` into `RootFolderRemover`, with the batch helpers shared.
- The running library scan is held twice: `LibraryScanner._runningLibraryScanStartedAt` and the open `LibraryScanSummaryRow`.
  `TasksPage.razor.cs:88` reads the field right after triggering, before the job can have started.
  Reading the open document (indexed on `(Closed, StartedAt)`) deletes about 12 lines; `RunningWork.razor.cs:57` reads the field too, and the document appears a few milliseconds after the field is set.
- `VideoFile.Recheck`, `ConvertToFromFile` and `CurrentResultRecheck` (about 40 lines) have one caller, `ChangeDetectionSettings`; the aggregate file is for "rules more than one slice uses".
- `FolderWatcher.WatchedRootFolderPaths` is read by tests only; four `FolderWatcherTests` wait on it, so it goes only with a replacement sync point.
- `IObservableExtensions.SelectUnit` (one caller; the health checks inline the same thing), `DateTimeOffsetExtensions.cs`, `TrimToNull`, `ToFts5Phrase`: one caller each.
- `PlaybackHandler.ClearHistoryAsync` (16 lines): a command wrapper inside a hosted process; the page sends `ClearHistory` (with proposal 1).

> **Two decisions.** Principle 6 ("Kodi is the first player, and Jellyfin and others can follow") argues for keeping `IPlayerConnection` as a seam; the factory's switch keeps that seam either way, and the interface costs 13 lines.
> The ten playback fields are written out four times (`RecordPlayback`, `PlaybackHandled`, `Playback`, `PlaybackRow`) and `RecordDeliveryHandler`'s body never reads its `[WriteModel] Playback`; but that non-nullable parameter is an existence and version guard (`EntityIsNotNullGuard` in the generated code) that refuses a delivery for a playback never recorded.
> A `RecordPlayback(PlaybackHandled)` and an `Append` without a fold would delete about 25 lines, touches Q11 ("the aggregate applies events"), and needs another existence check; offered, not recommended.

### 10. Value types own their bounds

#### Problem

`KodiPlayerForm.cs:16-31` carries `[Required]` and `[Range(1, 65535)]`, `MqttNotifierForm.cs:17-35` `[Range(0, 2)]` on `Qos`, while `Playing/Players.cs:52` `KodiEndpoint` and `Notifying/Notifiers.cs:51` `MqttSettings` have no `Create` and no check, and `SavePlayerHandler.Validate` checks the name and duplicate paths only.
So `SavePlayer` with port 0 or `SaveNotifier` with QoS 7 is accepted from anywhere but the modal (confirmed by all three reviewers); `MqttNotifierClient.cs:41` casts `(MqttQualityOfServiceLevel)settings.Qos` unchecked and `WebhookNotifierClient.cs:17` builds `new HttpMethod(settings.Method)` outside its `try`.
`docs/domain-model.md:21` says `MqttSettings` holds `MqttBrokerAddress`, but it holds `string Url`, parsed three times.
`Detecting/` already does it right: `PictureMeasurement.Create` and `StandardRatios.Create` own their bounds, and `DetectionSettingsForm` has no annotations.

#### Change

`KodiEndpoint.Create(...)` and parsed `MqttSettings(MqttBrokerAddress Broker,..., QualityOfService Qos)` and `WebhookSettings(Uri Url, WebhookMethod Method,...)`, each with a private constructor and a `[JsonConstructor]` so an invalid value cannot be built, a Debarr-owned QoS enum rather than MQTTnet's so the event schema does not depend on a package's member names, and an abstract `Validate()` on `PlayerEndpoint` and `NotifierSettings` that the handlers' `Validate` merges.
The modals drop the annotations and the `MudForm.ValidateAsync` step for *Save*; *Test* calls the same static `Validate(command, null)`; the handler's duplicate path-mapping refusal becomes a row `FieldError` so it still shows beneath the row.

#### Deletes and adds, conflicts, framework check

About 30 deleted and 45 added; the two client throw paths become unreachable; `NotifierAdded` and `NotifierChanged` change shape, so this lands in proposal 7's wave.
`domain-model.md:21`'s invariants ("A valid broker URL") gain the QoS, method and URL, and the *Names* table gets a *Notifier settings* row, the counterpart of *Player endpoint*.
The operator loses the red field before the first click, which `ui-conventions.md` *Saving* does not promise.
MudBlazor 9.10.0 XML: `MudForm.Validation` accepts a `Func<object, string, IEnumerable<string>>`, so nothing in MudForm needs the annotations; a scratch probe showed MQTTnet 5.2's `Build()` throws only on an empty topic.
Verification: the form tests, `NotifiersPageTests`, `PlayersPageTests`, and new handler tests for port 0 and QoS 3 refused beneath their fields.

### 11. Rules in structure

#### Today

`.editorconfig` has CA1848, CA1727 and CA2254 at error; `LogTemplateTests` checks every template by reflection; `ProgramTests` checks no EF Core package.
Against today's tree: zero namespace-folder mismatches, zero `@code` blocks, 25 static handlers all with a static `Handle`, six of six projections with a rebuild test, 47 multi-type files of which 46 are allowed (`SavePlayer.cs` also holds `PathMappingEntry`), one impure `Handle` (`UnarchiveVideoFileHandler.Handle(..., IDocumentSession)`), and 204 public against 149 internal top-level types, a split decision 12 ends by making every type public.

#### Change

- Folder equals namespace: `dotnet_style_namespace_match_folder = true`, `dotnet_diagnostic.IDE0130.severity = error` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` in the existing `src/Directory.Build.props`.
  Spike 6 measured it: 0 warnings and 0 errors on today's tree, no exemption needed for `Internal/Generated/` (the generated files carry `<auto-generated/>` and `#pragma warning disable`), no other IDE rule raised, and a probe file in `Detecting/` declaring `namespace Debarr.Scanning` fails the build with `error IDE0130`.
- `ArchitectureTests.cs` beside `LogTemplateTests.cs` (about 80 lines, no package): no `@code` or `@functions` block under `Components/`; no namespace segment equals a type name; every static class that declares a `Handle` has it static, taking its command first, with no `IQuerySession` parameter, and any `Validate` returns a `ResultBase`; every `Mud*Field` and `MudSelect` carries `Variant.Outlined` and `Margin.Dense`.
  The predicate selects static classes declaring `Handle`, not every `*Handler`; spike 6 ran it as a `TheoryData<Type>` theory in xunit.v3 4.0.1: 25 cases, exactly one failure, `UnarchiveVideoFileHandler`, and `PlaybackHandler` and `KodiMessageHandler` not enumerated.
- A `ProjectionRebuildTests` theory over a `StaleDocuments` table replaces the six hand-written "stale document" halves (about 50 lines) and fails when a seventh projection lands without an entry.
- File name equals type and one type per file: Meziantou.Analyzer MA0048 was tried in spike 6 and dropped.
  With `only_validate_first_type` it checks only the first type in a file (a probe file holding two types passed), so it does not enforce one type per file, and the package adds about 1,040 warnings from its other rules that would have to be turned off.
  The one-type-per-file rule stays a source-scan test in `ArchitectureTests` or stays prose.
- ArchUnitNET 0.13.4 (`TngTech.ArchUnitNET.xUnitV3`, Apache-2.0) only if the operator wants dependency-direction rules; AGENTS.md states none today.
  NetArchTest.Rules rejected (last release 2021).

#### Deletes

AGENTS.md *Types, files and names* bullets on folder equals namespace and code-behind, and the *Logging* enforcement sentences, each replaced by "the build enforces this"; the one-type-per-file bullet stays until the source-scan test exists.
The handler-shape theory's one failure, `UnarchiveVideoFileHandler`, either joins an allow-list or returns a store operation.

### 12. Ubiquitous language fixes

- *Remove* versus *Delete*: the commands are `RemovePlayer` and `RemoveNotifier` and the event `PlayerRemoved`; the UI says "Delete" (`KodiPlayerModal.razor:101`, `IntegrationModalBase.cs:87`) and the message "Player deleted."; `ui-conventions.md:97,130` prescribe "Delete" while the *Names* table uses *Remove*.
  The two documents disagree; the operator picks.
- Query values keep retired words: `?status=overridden` and `?status=container` (`MediaPage.razor.cs:18-19`) for *Manual* and *From File*, while Status links `?status=pending` and `?status=failed` with the current words.
- Detection page ids keep retired words: `detection-concurrency`, `detection-edge-skip`, `detection-limit-sdr`, `detection-snap-*`, `detection-no-ambiguous` (`DetectionPage.razor:24-123`); the tests select by them and change in the same sweep.
- The Media column "Added" labels `MediaRow.DiscoveredAt`, which the *Names* table calls when a scan first found the file; `FilePath.FirstSeenAt` is a different fact (the path) and stays.
- "Archiving {RootFolder}" (`RunningWork.razor.cs:37,73`) for the process the table calls *Root folder removal*.
- "Notifiers saved." after a removal; with proposal 1 the committed `NotifierRemoved` gives "Notifier removed.".
- Docs: `AGENTS.md:118`'s naming example "`MqttNotifierClient` beside `MqttNotifier`" names a type that does not exist; `domain-model.md:54` lists `NotificationPublisher` among processes that "send commands for every write" but it sends none; the *Names* table has no *Notifier settings* row and lists *Detection failure explanation* as a *Read model* though it is computed; `LibraryScanJob.ScheduleAsync(IScheduler, int?)` takes the primitive where every caller holds a `ScanInterval`.
- `TasksPage` injects `ISchedulerFactory` and `HistoryPage` injects `PlaybackHandler` for one read each; `LibraryScanner` exposes `NextScheduledRunAsync`.
- Three switches in two folders give one status its word, icon and colour (`VideoFileStatusExtensions`, `VideoFileStatusText.razor.cs:49-65`, and the same for `PlaybackOutcomeText`); one table per enum in its chapter would make `ui-conventions.md`'s *Colour and state* table a value the tests read.

### 13. Test hosting

`ReadModelTestContext` (45 lines) and `HealthCheckTestContext` (44) are the same core, and `PageTestContext` (158) is a third copy inside `BunitContext`.
Twelve classes build their own host with `AddEventStore` and copy `Program.cs`'s registrations (about 360 lines: 12 copies of the SQLite pooling block, 11 of the Quartz instance-name block, 13 stop-dispose sequences).
The 106 read-model tests boot the whole web app with its background services to read the store; that background work is why page tests pause detections to stop reloads.

Merge the two contexts into one `AppTestContext`; add one `TestHost.StartAsync(services => services.AddDetecting().Replace<IAspectRatioDetector>(_detector))` over proposal 4's chapter modules, plus the hosted services a test needs, so a test registers a chapter as `Program.cs` does and replaces one service.
About 250 test lines go (less than first estimated, because the hosted services stay listed in `Program.cs`); the speed gain from skipping hosted services is an inference to measure.
Also: delete the duplicated host-level cases that repeat pure handler tests (about 110 lines in `LibrarySettingsServiceTests` and `DetectionSettingsServiceTests`), the `DispatchProxy` store in `PlayerConnectionServiceTests` (drop `fi_events` as `NotificationPublisherTests` does), the three one-job HTTP handler doubles, and `LibraryPageTests.cs:79-87`, which asserts the absence of a feature Q8 retired.
Name slice tests for the slice (`SaveOverrideTests.cs` beside `SaveOverride.cs`); the `SaveOverride` rules sit in four files today.

### 14. Detection settings: the pause moves into the slice and the service goes

#### Problem

`Detecting/DetectionSettingsService.cs:33-70` reads the settings, calls `Validate` and `ChangesStandardRatios`, pauses detections, then sends, under `_saveLock`; the generated handler runs `Validate`, `LoadAsync` and `Handle`, which each parse again, so `StandardRatios.Create` runs five times per save and `PictureMeasurement.Create` three.
`DetectionResultsClearedEvent` (pushed at 86) has no reader.
The adversarial pass broke the draft's shape: a `Before` with no aggregate parameter runs before the fetch and before `Validate`, so a refused save would pause, and reading settings outside the write model reopens the race the lock closes.
Spike 2 (worktree `spike/2`) then found the shape that works and measured it.

#### Change

`ChangeDetectionSettingsHandler` gains `BeforeAsync(ChangeDetectionSettings command, DetectionSettings? settings, DetectionOrchestrator orchestrator, CancellationToken ct)` returning `Task<DetectionPause?>`, and `Finally(DetectionPause? pause) => pause?.Dispose()`.
The plain `DetectionSettings? settings` parameter, with no attribute, binds to the aggregate the handler already fetches, exactly as `Validate` does, and Wolverine hoists the fetch ahead of it.
`BeforeAsync` calls the pure `Validate` itself and pauses only when the command is valid and changes the standard ratios, so a refused save never pauses.
`Configure` adds `chain.DecideAgainOnConcurrentAppend()`, which replaces the lock: a save that loses the race re-runs the whole handler, `BeforeAsync` included, against the newly committed settings.
`RedetectAllHandler` gets the same pair with an unconditional pause.
Spike 2 needed a public `IDetectionPauser` over the internal orchestrator; with decision 12 (every type public) `BeforeAsync` takes `DetectionOrchestrator` directly.
`DetectionSettingsService` becomes a bare forwarder and is deleted with proposal 1; `DetectionPage` sends `ChangeDetectionSettings` and `RedetectAll` itself.
Parse once as a second step: `Validate` returns `Result<ParsedDetectionSettings>` and `LoadAsync` and `Handle` take it, keeping the `[WriteModel]` parameter, which needs `RefusalContinuationStrategy` to match `ResultBase`, `CommandReply.RefuseAsync` to take it, and `FailAsync` to receive a plain `Result` built from the errors.

*Before: DetectionSettingsService.cs:33-70, then the handler*

```csharp
await _saveLock.WaitAsync(ct);
var settings = await ReadDetectionSettingsAsync(ct);
var decided = ChangeDetectionSettingsHandler.Validate(command, settings);
if (decided.IsSuccess && ChangesStandardRatios(command, settings))
{
    using var pause = await orchestrator.PauseDetectionsAsync(ct);
    result = await runtime.SendCommandAsync(command, ct);
}
else { result = await runtime.SendCommandAsync(command, ct); }
// then Wolverine: FetchForWriting, Validate again,
// LoadAsync, Handle, SaveChangesAsync
```

*After: the frame order spike 2 measured*

```csharp
// ChangeDetectionSettings.cs
public static async Task<DetectionPause?> BeforeAsync(
    ChangeDetectionSettings command, DetectionSettings? settings,
    DetectionOrchestrator orchestrator, CancellationToken ct) =>
    Validate(command, settings).IsSuccess && ChangesStandardRatios(command, settings)
        ? await orchestrator.PauseDetectionsAsync(ct) : null;
public static void Finally(DetectionPause? pause) => pause?.Dispose();
public static void Configure(HandlerChain chain) => chain.DecideAgainOnConcurrentAppend();

// generated: FetchForWriting (37) → BeforeAsync (38)
//   → CommandMiddleware.Before (41) → Validate / RefuseAsync (44-45)
//   → LoadAsync (46) → Handle (53) → SaveChangesAsync (67)
//   → finally CommandMiddleware.Finally (77)
//   → finally Finally(detectionPause) (84)
```

#### Deletes and adds

About 110 deleted (the service 91, two registrations, three duplicate parses, the unread event) and about 35 added (the two `BeforeAsync` and `Finally` pairs, `DetectionPause`, the parsed record).
`DetectionSettingsServiceTests` (551 lines) retargets to the bus and the orchestrator; spike 2 ran all 15 against the probe with the service cut to a bare send, and all 15 passed, including `A_save_from_a_stale_form_re_checks_against_the_settings_the_save_before_it_stored`.

#### Conflicts and framework check

*Behaviour* "runs inside a detection pause" (both commands) and *Constraints* "One pause runs at a time" hold.
Spike 2 measured, from Wolverine 6.43.0's generated code: a `[WriteModel(Required = false)]` attribute on the `BeforeAsync` parameter is wrong (it emits a second fetch and an out-of-scope `Finally` argument, CS0136, CS0128, CS0103), the plain parameter is right; the return type must be a named type, not `IDisposable?`, because Wolverine names the variable after its type and clashes with `CommandMiddleware.Before`'s; a `HandlerContinuation` return would stop the chain only after `LoadAsync`, and a `Result` return stops nothing, which is why `BeforeAsync` calls `Validate` itself; `DecideAgainOnConcurrentAppend` re-runs the whole handler including `BeforeAsync` (traced: the second save's `BeforeAsync` printed twice, before and after the retry).
Two side effects to accept or fix: the fetch and the pause run outside the `{Command} {MessageId}` logging scope, because `CommandMiddleware.Before` is now generated after `BeforeAsync`; and the pause is disposed twice (`using var` and `Finally`), which `Disposable.Create` tolerates.

> **Decision.** The lock also made concurrent saves commit in send order.
> With the retry instead, a save waiting in its pause can be overtaken by one that needs no pause, then retries against the later save's settings and commits last, so the final settings may not be the last ones sent (reasoned by the spike, not observed; the measured run kept send order).
> Each save still decides against the latest committed settings, which is what *Behaviour* and the tests require.
> Accept the weaker ordering, or keep a serialization point.

## Feature challenges, each a decision for the operator

1. **Saved-settings activity messages.** Eight toasts cost four services, two subjects in two more, twelve event types, seven registrations and about 575 test lines.
   The operator who pressed *Save* sees the form's own state in the same second; the message reaches other open circuits only.
   *Behaviour* "Activity messages" requires "what was saved", so dropping them is the operator's call; proposal 1 keeps them at about a tenth of the cost.
   **Decided: keep, derived from commits.**
2. **The count in "Cleared N playbacks from the history".** A count query before the command, off by any racing playback, a subject event, and the reason History reaches into a hosted process; the dialog showed the same number a moment before.
   "History cleared.", or the count on the stored event.
   **Decided: "History cleared."**
3. **`PlaybackRow`'s 19 indexes.** Rebuild 263.5 s against 141.5 s with the first 9 (`critter-stack.md` *What task 60 found*); seven serve only the title and path sorts under a filter; `PlaybackRowQueryTests` spends about 150 lines on the per-view index map.
   Time History's sorts without them on the large library before deciding; Radarr's History sorts were not checked.
   **Decided: measure first against a 100 ms server-side bar.**
4. **Motion.** `MotionList` and its item types, `StateLabel`'s fade and the row-enter bookkeeping in Media and History: about 250 to 300 app lines, about 230 test lines and `ui-conventions.md`'s 14-line *Motion* section, for 150 to 250 ms of fade that one operator watching a live page sees.
   Principle 22 wants live updates, not motion.
   Proposal 8 removes the row-enter bookkeeping either way.
   Line counts are the readers', not re-measured.
   **Decided: keep.**
5. **Converting a detected result to *From File* without running ffmpeg** (principle 12, second half).
   `DetectionResultConverted`, `VideoFile.ConvertToFromFile`, `DetectionOrigin.StandardRatiosChange`, the nullable `Detection.Path` and zero `Duration` for that origin: about 60 lines, five of fifteen `DetectionSettingsServiceTests` cases and half of `ChangeDetectionSettingsTests`.
   The alternative, clear and let the queue re-detect, exists for the other direction and costs one ffprobe run per file.
   Principle 12 names it, so it is a decision, never a refactor.
   **Decided: keep.**
6. **The removal-versus-scan race** (under proposal 5).
   Reproduced by spike 5 in both orderings.
   The menu: the two-line fix alone (closes the lagging archive, leaves the failing scan, changes two *Behaviour* sentences under "Archiving at the end of a library scan", fails one existing test that pins them); the two-line fix plus the structural fix (the removal waits for a running scan; "archives at once" and "under running work" in "Root folders" change); or the two-line fix plus a scanner-side recovery in `HashAsync` that treats the archived-stream refusal as the signal to unarchive and add once more (no *Behaviour* change beyond the two sentences).
   Doing nothing leaves a one-scan archive lag and an occasional failed scan.
   **Decided: the two-line fix plus the scanner-side recovery.**

## Decisions taken

The operator took these on 2026-10-02, after the spikes.
Each feature challenge above carries its outcome here, and the decisions the proposals left open are closed.

#### Feature challenges

1. **Saved-settings activity messages.** Keep, derived from the committed domain events at the one listener (proposal 1 as written).
   No *Behaviour* change.
2. **Clear-history count.** Drop the count: the message becomes "History cleared.", derived from the committed `HistoryCleared`; the page sends `ClearHistory` itself and `PlaybackHandler.ClearHistoryAsync` goes.
3. **`PlaybackRow`'s 19 indexes.** Measure first, against a new bar set by user experience rather than Q14's 7 ms: a History sort or page read on the large library completes in 100 ms or less server-side, the "feels instant" threshold for a response to input, leaving the SignalR round trip and the render inside a 200 to 300 ms total.
   Drop the seven title and path indexes if every view stays under the bar without them; keep them otherwise.
   The bar is recorded beside Q14 as the History query budget.
4. **Motion.** Keep.
   Proposal 8 still removes the duplicated row-enter bookkeeping.
5. **Converting a detected result to *From File* without ffmpeg.** Keep; principle 12 stands.
   Proposal 7's closed `DetectionOutcome` still removes the Result/Failure pair; the Path-and-Duration special case for the *Standard Ratios Change* origin stays, or becomes a smaller closed type at the implementer's call.
6. **Removal-versus-scan race.** The two-line fix plus the scanner-side recovery: the scan's end archives the video files of the paths its sweep removed, and `HashAsync` treats the archived-stream refusal as the signal to send `UnarchiveVideoFile` then `AddFilePath` once more.
   The removal keeps running at once in the background.
   Two *Behaviour* sentences under "Archiving at the end of a library scan" change, the one `LibraryScannerTests` case that pins them changes with them, and spike 5's `RootFolderRemovalRaceTests` become the pinning tests.

#### Decisions inside the proposals

7. ***Remove* versus *Delete* (proposal 12).** *Remove* everywhere: modal buttons, the activity message and `ui-conventions.md`'s Modals and Destructive actions sections change; the commands and events keep their names.
8. **`IPlayerConnection` (proposal 9).** Drop it: `PlayerConnectionFactory.Create` returns `IObservable<PlayerConnectionEvent>` and gains `TestAsync`.
9. **The `Playback` fold (proposal 9).** Keep the fold and the `[WriteModel] Playback` guard; Q11 stands.
10. **Library scan outcome (proposal 7).** Show cancelled and interrupted scans apart with a closed `LibraryScanOutcome`; *Behaviour* "Library scan summary" and the *Names* table gain the words.
11. **Save order (proposal 14).** Accept the retry in place of the lock; each save decides against the latest committed settings; the orchestrator's pause lock keeps one pause at a time.
12. **Visibility.** Every type is public; nested implementation details stay private.
    Q1 and `AGENTS.md` say "Types are public".
    So proposal 5's `AfterCommitAsync` takes `RootFolderRemover` directly, proposal 14's `BeforeAsync` takes `DetectionOrchestrator` directly, proposal 8's primary-constructor sweep returns, proposal 11 drops its public-type test, and `[WolverineIgnore]` goes on `PlaybackHandler` and `KodiMessageHandler` as a guard against conventional discovery (neither exposes a public `Handle` or `Consume` method today).
    The sweep over 149 declarations is its own task and lands first.
13. **The Rx subject (proposal 1).** Agreed: one subject in `ReadModelChangeListener` carrying both `ReadModelChanged` and `CommittedEvent`, with catch-and-log around `OnNext`.
    The implementer explores whether an `Observable.Create` adapter over the session listener can replace the subject while keeping the stream hot and shared, and adopts it if it reads better within the Rx doc's rules.
14. **Inline validation (proposal 10).** Acceptable: the modals show the refusal beneath the field after *Test* or *Save*, as Detection settings does; the annotations go.

## Tasks

The proposals and decisions as commit-sized tasks, in landing order, each proven by its tests and the live check it names.
They follow the plan's convention of one task per session, and task n here is task 64 + n in the plan.
The two tasks that reshape stored events (10 and 11) land in one wave with task 64's re-import, so the importer on `task60-large-library-timings` and the live-check data directories are updated once.

1. **Make every type public.** Sweep the 149 `internal` declarations in `src/Debarr/` to `public`; nested implementation details stay private.
   Add `[WolverineIgnore]` to `PlaybackHandler` and `KodiMessageHandler`.
   Q1 and `AGENTS.md` (*Types, files and names*, *Project structure*) say "Types are public".
   Tests: build, full suite, `codegen write` clean.
   Depends on nothing.
2. **Move the scan slices to Scanning.** `AddFilePath`, `RemoveFilePath`, `RemoveFilePaths`, `ArchiveVideoFiles`, `UnarchiveVideoFile`, `StoredFilePath` and their tests to `Scanning/`; `IFilePathCommand` to `EventStore/`; `codegen write`.
   Tests: build and the moved tests.
   Proposal 2.
3. **Move the integrations and health checks into their chapters.** ffmpeg to `Detecting/`, Kodi to `Playing/`, the notifier clients and factory to `Notifying/`, each health check to its chapter; delete `Integrations/`; `Health/` keeps its four shared types.
   Update *Folders* in `domain-model.md`, *Project structure* in `AGENTS.md`, and the `KodiPlayerConnection` path in the `exercise-kodi` and `debarr-live-check` skills.
   Tests move with their subjects.
   Proposal 3, part one.
4. **Move the activity records and the hidden rules.** The 30 one-line records in `Activity/` to the chapters that raise them (`Activity/` keeps `ActivityEvent`, `IActivitySource`, `ActivityFeed`, `ReadModelChanged`); the crop-sample grouping to `Detecting/`, `ToKodiPlayerPath` into `KodiEndpoint`, the log parser and `ReadLastLinesAsync` beside `LogEntry`, the timestamp pair into `Notification.cs`, `DetectionFailureExplanation` to `Detecting/` (Names role: *Value type, worked out*); `AddEventStore`, `SendCommandAsync` and `DecideAgainOnConcurrentAppend` to `EventStore/`; each aggregate's `Read*Async` beside its aggregate; fix `TimeSpanExtensions.ToDisplayText` to `InvariantCulture` with a comma-culture test.
   `AGENTS.md`'s extension placement rule gains "unless the receiver or element is a domain type".
   Proposal 3, part two.
5. **Chapter modules.** `AddScanning`, `AddDetecting`, `AddPlaying`, `AddNotifying`, `AddAppearance`, each registering its services, health checks, activity sources, folded aggregates and projections (through `ConfigureFisher`); Scanning's Quartz extension moves into `Scanning/`; `Program.cs` keeps the seven `AddHostedService` lines in order with their comments; the hand-kept folded-aggregate list goes; health message order kept explicitly.
   The *Names* table names the module concept first.
   Tests: `ProgramTests`, `CommandPipelineTests`, `HealthCheckServiceTests`; live check for the start order.
   Proposal 4.
6. **Committed events on the feed.** `ReadModelChangeListener` publishes `CommittedEvent(IEvent)` beside `ReadModelChanged` from the same loop, with catch-and-log around `OnNext` (explore an `Observable.Create` adapter first, per decision 13); `ActivityMessageArea.Describe` matches stored events; delete `PlaybackHandledEvent`, `DeliveryFinishedEvent`, `DetectionResultsClearedEvent` and their pushes; `PlaybackHandlerTests` waits on `CommittedEvents`.
   `reactive-extensions.md` *Subjects* and *Debarr's streams* and Q13 gain their sentences.
   Tests: `ActivityMessageAreaTests`, `ReadModelChangeListenerTests`, `PlaybackHandlerTests`.
   Proposal 1, part one.
7. **Library follow-up work into the slices.** `AfterCommitAsync` on `SetRootFolderEnabledHandler`, `RemoveRootFolderHandler`, `AddRootFolderHandler` and `ChangeLibrarySettingsHandler`, taking the command, the `[WriteModel] Library`, `ISchedulerFactory` and `RootFolderRemover`, catching and logging their own failures; a `ScheduleChanged` stream adapted from Quartz's `ISchedulerListener` for `TasksPage`; `LibraryPage` sends its four commands and keeps the full-path and folder-exists checks at the boundary; `TasksPage` calls `LibraryScanJob.TriggerNowAsync`; delete `LibrarySettingsService` and `LibrarySettingsSavedEvent`; `FolderWatcher` triggers on `ChangesTo(nameof(Library))`.
   Tests: the four named `LibrarySettingsServiceTests` cases keep their assertions as slice and page tests, the five host-level duplicates go.
   Live check: add, disable, enable and remove a root folder, then the next run on *System > Tasks*.
   Proposal 5; depends on 1 and 6.
8. **The detection pause into the slice.** `BeforeAsync(ChangeDetectionSettings, DetectionSettings?, DetectionOrchestrator, CancellationToken)` returning `Task<DetectionPause?>` and `Finally(DetectionPause?)` on `ChangeDetectionSettingsHandler`, the same pair with an unconditional pause on `RedetectAllHandler`, `DecideAgainOnConcurrentAppend` on both; parse once (`Validate` returns `Result<ParsedDetectionSettings>`; `RefusalContinuationStrategy` matches `ResultBase`; `CommandReply` builds a plain `Result`); `DetectionPage` sends its commands; delete `DetectionSettingsService`.
   Record the two side effects (the fetch and the pause outside the command's log scope; the double dispose) in the code or fix them.
   Tests: all 15 `DetectionSettingsServiceTests` retargeted to the bus; `CommandPipelineTests`.
   Live check: save a tolerance change during a detection and read the log.
   Proposal 14; depends on 1 and 6.
9. **Delete the forwarding services.** `UISettingsService`, `OverrideService`, `PlayerSettingsService`, `NotifierSettingsService`, their eight activity records, their registrations; `UIPage`, `VideoFileDetailPage`, `KodiPlayerModal`, `MqttNotifierModal`, `WebhookNotifierModal` and `HistoryPage` send their commands; `PlayerConnectionService` keys on the committed player events; `PlaybackHandler` loses `ClearHistoryAsync` and its subject, and the message becomes "History cleared."; the three test cases move (notifier settings round trip, kept path mapping, override through the page); the reconnect-on-commit test is added; the page tests that seed players expect connections.
   `ui-conventions.md`'s "A service returns such a refusal" becomes "A handler returns".
   Live check: save each settings page and read the message on a second tab; save a player and watch Status.
   Proposal 1, part two; depends on 6, 7 and 8.
10. **Closed hierarchies.** `PlaybackOutcome` as five sealed cases (delete `NotSentReason`, its extension and `PlaybackOutcomeTests`); `DetectionOutcome` for `Detection` (the runner's record becomes `DetectorOutcome`); `RecheckScope` as `All`, `Some`, `None`; `LibraryScanOutcome` with *Cancelled* and *Interrupted* shown apart (*Behaviour* "Library scan summary" and the *Names* table gain the words); `LocalPath` in `DetectionRequest`, `RunningDetection` and the detection activity events; `LastScanText` takes the scan.
    Update the importer on `task60-large-library-timings` and re-import the live-check data directories with task 64.
    Tests: the projection rebuild tests, `PlaybackRowTests`, `MediaRowTests`, `LibraryScanSummaryRowTests`, `TasksPageTests`.
    Proposal 7 and decision 10.
11. **Value types own their bounds.** `KodiEndpoint.Create`, parsed `MqttSettings` (`MqttBrokerAddress`, a Debarr-owned QoS enum) and `WebhookSettings` (`Uri`, `WebhookMethod`), private constructors with `[JsonConstructor]`, an abstract `Validate()` on `PlayerEndpoint` and `NotifierSettings` merged by the handlers' `Validate`; the modals drop the annotations and the pre-save `MudForm.ValidateAsync`; the duplicate path-mapping refusal becomes a row `FieldError`; `domain-model.md:21`'s invariants and a *Notifier settings* row in the *Names* table.
    Same re-import wave as task 10.
    Tests: the form tests, `NotifiersPageTests`, `PlayersPageTests`, new handler tests for port 0 and QoS 3.
    Proposal 10 and decision 14.
12. **A pure playback decision.** `PlaybackDecision.Decide` (or a static beside `PlaybackHandler`) for steps 2 to 6 and the no-ratio rule; `HandleAsync` keeps the stream check, the file scan and the failure path with `localPath` and `videoFile` declared before the `try`; delete `ToNotificationAspectRatioSource` from Detecting.
    Tests: a theory over every arm; the six host duplicates in `PlaybackHandlerTests` go.
    Proposal 6; depends on 10.
13. **The removal-versus-scan race.** Archive the video files of the paths the scan's end sweep removes (`LibraryScanner.cs:100-109`); in `HashAsync`, treat `ArchivedStreamException` from `AddFilePath` as the signal to send `UnarchiveVideoFile` then `AddFilePath` once more; edit the two *Behaviour* sentences under "Archiving at the end of a library scan"; change `A_library_scan_removes_the_file_paths_under_no_enabled_root_folder_and_archives_their_video_files_at_the_next_one` to the new rule; bring spike 5's `RootFolderRemovalRaceTests` and its `CommitHooks` listener into the test project as the pinning tests.
    Live check: disable a root folder during a library scan and confirm the archive and a finished scan.
    Decision 6.
14. **Components, the paged table.** `PagedTableView<TRow>`, `PagedTable<TRow>` and `FilterBar`; `MediaPage`, `HistoryPage` and `LogsPage`'s filter markup use them; the row-enter bookkeeping lives once.
    Tests: `MediaPageTests` and `HistoryPageTests` unchanged in what they assert.
    Proposal 8, part one.
15. **Components, the edited form and the shared frames.** `EditedForm<TForm>` on the five pages; `IntegrationModal` frame for the three modals and `PlayerConnectionStateText` in the Kodi modal; `HostSettingField` on General; `LiveComponentBase.Reloads` so `LogsPage` and `ElapsedTime` inherit it; sealed components drop `SuppressFinalize`; delete the 25 `HelperTextOnFocus="false"` and change `ui-conventions.md:38`; `PageAction.FieldErrorAttributes` splatted on the 13 hookups, checking each field's `Min` for an unreachable refusal; primary constructors replace the `[Inject]` blocks on the leaf components (base classes keep `[Inject]`).
    Tests: the page tests, a bUnit test of `EditedForm`, the probe test from spike 3 for the splat.
    Proposal 8, part two; depends on 1 and 9.
16. **Delete the pass-throughs.** `IPlayerConnection` and its extension (the factory returns the observable and gains `TestAsync`); `NotifierClientFactory` folded into `NotificationPublisher`; the root folder removal moved from `LibraryScanner` into `RootFolderRemover`; the running library scan read from the open summary document; `VideoFile.Recheck` helpers into `ChangeDetectionSettings`; `WatchedRootFolderPaths` replaced by a test sync point; `SelectUnit`, `DateTimeOffsetExtensions`, `TrimToNull`, `ToFts5Phrase` inlined.
    Tests: the named connection, publisher, scanner and watcher tests.
    Proposal 9 and decisions 8 and 9.
17. **Rules in structure.** IDE0130 at error with `EnforceCodeStyleInBuild`; `ArchitectureTests` (no `@code` block, no folder named for a type, handler shape as spike 6's `HandlerShapeTests`, outlined-dense fields, one top-level type per file by source scan with the allowed files); `ProjectionRebuildTests` theory replacing the six hand-written halves; fix `UnarchiveVideoFileHandler` or allow-list it; `AGENTS.md` bullets on folder equals namespace, one type per file, code-behind and logging enforcement become "the build enforces this".
    Proposal 11.
18. **Ubiquitous language.** *Remove* on the modal buttons and messages and in `ui-conventions.md`; `?status=manual` and `?status=from-file`; the Detection page ids renamed with their tests; the Media column "First Seen"; "Removing {RootFolder}" in running work; "Notifier saved." and "Notifier removed."; `AGENTS.md:118`'s example, `domain-model.md:54`'s process list, the *Names* rows; `LibraryScanJob.ScheduleAsync(ScanInterval)`; `TasksPage` and `HistoryPage` injections; one table per display enum for word, icon and colour.
    Live check: capture the pages and read the words.
    Proposal 12 and decision 7.
19. **Test hosting.** `AppTestContext` merging the two contexts; `TestHost.StartAsync` over the chapter modules plus the hosted services a test names; the twelve copied host setups go; the duplicated host-level cases, the `DispatchProxy` store, the three HTTP handler doubles and the retired-feature assertion go; slice tests named for the slice.
    Measure `dotnet test` before and after.
    Proposal 13; depends on 5.
20. **History's indexes, measured.** On the large library, time every History view with only the time, player, outcome and trigram indexes against the 100 ms server-side bar; drop the seven title and path indexes if every view passes, keep them otherwise; record the bar beside Q14 and the result in `critter-stack.md`; adjust `PlaybackRowQueryTests`.
    Decision 3.

Not tasks: the retention of motion (decision 4), the conversion without ffmpeg (decision 5) and the `Playback` fold (decision 9) stay as they are.
Task 64 continues as planned; these twenty tasks are milestone M9, tasks 65 to 84, in `docs/rewrite-plan.md`, ahead of the UI audit (M10) and Ship (M11), since four of them change the pages the audit captures.

## What to keep

- The pure deciders and their database-free tests: `SavePlayerHandler`, `SaveNotifierHandler`, `AddFilePathHandler.Handle`'s tuple switch, `VideoFile.StatusOf`, `SaveUISettingsHandler`, `ChangeDetectionSettingsHandler.Validate`.
- Value types that parse at the boundary and return `Result<T>` with `FieldError`s: `Override.Create`, `PictureMeasurement.Create`, `StandardRatios.Create`, `MqttBrokerAddress.Parse`, `ScanInterval`, `VideoExtensions`; `AspectRatio`, `FileHash` and `FileStat` as validating record structs; `StandardRatios.Snap` as the one snap function.
- The closed hierarchies with private constructors, `DeliveryOutcome` and `PlayerConnectionState`; proposal 7 extends the pattern.
- The Rx pipelines that already are the design: `PlayerConnectionService` (`GroupBy`, `Switch`, `Scan`, `Replay(1)`), `PlaybackHandler`'s outer pipeline with `Task.WhenEach` inside, `HealthCheckService` (the best goal-5 example in the app), `FolderWatcher`, `LiveComponentBase`, `ActivityMessageArea`'s `Buffer` and `Concat`, `ActivityFeed`'s `Merge` with `Observable.Never`, `ObserveJobs`.
- `DetectionOrchestrator`'s `PeriodicTimer` shape and `PauseDetectionsAsync` returning an `IDisposable`; `NotificationPublisher`'s eager tasks, which the *Delivering before recording* constraint needs; `DetectionOrchestrator`'s `_pauseLock`, which keeps "one pause at a time" whatever proposal 14 does to the save lock.
- `ReadModelChangeListener` deriving read-model names from `ProjectionBase.IncludedEventTypes` and the aggregates' `Apply` signatures; `EventStore/`'s four types as the smallest correct Wolverine hooks (Wolverine 6.43.0's own `ResultTypeContinuationPolicy` only logs and stops, so `RefusalContinuationStrategy` earns its 48 lines; spike 4 disproved replacing it: Wolverine's `UseResultType` only logs and early-returns, the page then sees a successful null, and `ResultFailureException` carries error strings only and never fires for handlers that return events).
- `IAspectRatioDetector` with `DetectorDouble`; `INotifierClient`; `FakeKodiServer` over a real socket; `CommandPipelineTests` with `Probing/`; `Interleaver`; the query-plan tests; `LogTemplateTests`; the `Test*` seeders that append events rather than write read models.
- The Quartz design (one execution group, triggers keyed per folder, `Replacing` for *Scan Now*); `ArchiveVideoFiles` appending at the version its row was read at; `RecordDetection`'s `ArchivedStreamException` policy; `MediaRow`'s computed indexed members and `StoredFilePathQuery`'s range read; `HistoryClear` as one document on one stream.
- `PageAction`, the shared components and state texts, pages reading straight from `IDocumentStore.QuerySession()`, the form records per edited value, `HostSettingsFile`'s atomic write and its push source, `KodiMessageHandler`, `FieldError : Error`, and keeping `UISettingsChanged`, `SaveUISettings` and `UISettings` as separate records with the same fields.

## Considered and rejected

- **The draft's shape of the detection pause in the slice** (a `Before` with no aggregate parameter, or one marked `[WriteModel]`): the first pauses before validation, the second does not compile.
  Spike 2 found the shape that works, now proposal 14.
- **A store-wide reaction for the library's follow-up work**: fires on every test seed and import, and hides scheduler failures from the page.
  Demoted to the alternative behind proposal 5's `AfterCommitAsync`.
- **Discovering folded aggregates by reflection** on a static `StreamId`: misses `VideoFile`.
  Dropped.
- **The root folder removal as a Quartz job in its own group** as a race fix: does not fix it.
  Kept only as the structural fix inside the `scan` group, which is an operator decision.
- **`Decide` on the `Playback` aggregate**: one caller, so it conflicts with Q11.
  Moved beside `PlaybackHandler`.
- **`DetectionOrchestrator` as an Rx pipeline**: `Merge(maxConcurrent)` fixes the limit at subscribe time where *Behaviour* applies a saved number from the next check; the running set is needed for the queue query anyway; *Detect Now* needs a slot outside the merge; the pause needs a subject.
  Same line count, less visible state.
- **`NotificationPublisher` as an `IObservable`**: a fixed set of single results is `Task` by the Rx doc; a cold observable would need `Publish` and `Connect` before the record and a subscription outliving the cancellation.
- **A shared generic for `SaveNotifier` and `SavePlayer`** (about 75 duplicated lines): two instances, rules of two or three lines, a generic would cross chapters and need a concrete shim per chapter; Q12 chose the shape.
  A shared test theory is the cheap guard.
- **Moving `VideoFile` to `Scanning/`**; splitting `Players` and `Playback`; `Components/` by chapter (Status reads five chapters).
- **Diffing the folded `Players` on `ChangesTo(nameof(Players))`** to find the changed player: `PathMappings` is a list, so record equality fails on every read; the committed events carry the id.
- **Faking ffmpeg to drop `IAspectRatioDetector`**: 150 or more lines, IPC to hold and release, slower runs, against a 79-line double.
- **Binding immutable command records in `MudForm`**: same line count per field, no `For`, and mutable Wolverine messages.
- **One closed hierarchy per display enum** (lead 3 as stated): stored enums whose `ToDisplayText` is UI wording; a type per member adds nothing.
- **Replacing `RefusalContinuationStrategy` with Wolverine's `UseResultType`**: disproven by spike 4.
  The generated frame becomes `if (ResultTypeContinuationPolicy.ShouldStop(...)) return;` and never touches the reply, so a refused command returns a successful null to the page and two `CommandPipelineTests` fail; `ResultFailureException` has one property, `Errors` as strings, and fires only when the handler's return is the result type.
- **Meziantou.Analyzer MA0048** for one type per file: it checks only the first type in a file and brings about 1,040 unrelated warnings (spike 6).
  **NetArchTest.Rules** (unmaintained since 2021); ArchUnitNET for anything but dependency direction.
- **Reversing any of Q6, Q14, Q15 or Q16** (projection storage): every reader found the measured choices sound and no proposal touches them.

## Method

`dotnet build` from `src/`: succeeded with no warnings.
`dotnet test`: 924 passed, 0 failed, 2 min 39 s, so every claim here is about code that builds and passes.
Eight readers each read one part in full: `Scanning/`, `Detecting/`, `Playing/`, `Notifying/`, `Appearance/`, the cross-cutting folders with `Program.cs`, `Components/`, and `Debarr.Tests/`; `Internal/Generated/` was read only to check what Wolverine generates for this app's middleware.
Every framework claim was checked against the pinned packages' XML docs in the NuGet cache (Fisher 1.14.0, WolverineFx 6.43.0, Quartz 4.2.0, MudBlazor 9.10.0, System.Reactive 7.0.0) or, for Blazor, Microsoft's.NET 9 and 10 docs; the few claims that could not be verified are marked as spikes above.
Three adversarial reviewers on different models (Opus, Fable, Sonnet) then tried to break every proposal against the code, the *Behaviour* rules and the XML docs; they broke one, reshaped one, and corrected numbers, conflict lines and risk lines on nine others, all folded in above.
The central claims of proposal 1 and the re-deciding in proposal 5 were re-read first-hand by the author.

Six spikes then ran in throwaway worktrees (`C:\\repos\\gitea\\ryan\\debarr-spikes\\spike1` to `spike6`, branches `spike/1` to `spike/6`, nothing committed): `AfterCommitAsync` binding (proven, two qualifications), the pause in the slice (proven in a corrected shape, one sub-claim disproven), bUnit constructor injection (mechanism proven, blocked by internal services) and `@attributes` splatting (proven), `UseResultType` (disproven), the removal-versus-scan race (reproduced in both orderings), and IDE0130, MA0048 and the handler-shape theory (proven, disproven, proven).
Each spike's report, generated code and test output are in the session scratchpad under `spikes/`.

Readers, reviewers and spikes were told to change nothing on the branch; no repository file was modified.
