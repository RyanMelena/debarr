# Debarr: complexity-collapsing refactor audit, 2026-10-04

A report-only audit for accidental complexity (Ousterhout's definition: anything that makes the system hard to understand or modify; symptoms of change amplification, cognitive load, and unknown unknowns; causes of dependencies and obscurity).
No code was changed.
Findings were gathered from git churn, change-coupling, and reading every cited file, then adversarially verified by fresh reviewers.

## 1. System sketch

- One ASP.NET Core / Blazor Server app (`Debarr`), no separate services.
  Entry points: Blazor pages (commands via `SendCommandAsync`), Quartz jobs (`LibraryScanJob`, `FolderScanJob`), a `PeriodicTimer` loop (`DetectionOrchestrator`), Rx-driven hosted services (`PlaybackHandler`, `PlayerConnectionService`, `HealthCheckService`), a `FolderWatcher`, and Kodi TCP connections.
- One store: Fisher (SQLite event store).
  Every write is a command -> Wolverine handler -> aggregate fold -> appended events -> inline read-model projections -> `ReadModelChanged` published by `ReadModelChangeListener`.
- State lives in event streams; read models (`MediaRow`, `PlaybackRow`, `NotifierDelivery`, `LibraryScanSummaryRow`, `StoredFilePath`) are inline projections.
  In-process facts flow through one `ActivityFeed` (`CommittedEvent`, `ReadModelChanged`, runtime events).
- Guarantees assumed: single operator, at-most-one-scan, one detection per video file, version-stated appends (`AppendVersionGuard`), one global concurrency retry, delivery attempted once with a 5 s timeout.

## 2. Evidence

### Top hotspots (churn x size, existing files)

| File | Commits | Lines | Note |
|---|---|---|---|
| `Scanning/LibraryScanner.cs` | 29 | 617 | scan/hash/archive orchestration |
| `Detecting/MediaRow.cs` | 23 | 293 | read model + projection + query |
| `Components/Pages/VideoFileDetailPage.razor.cs` | 26 | 203 | largest page |
| `Playing/PlaybackHandler.cs` | 18 | 210 | playback -> notify pipeline |
| `Playing/PlaybackRow.cs` | 18 | 242 | history read model |
| `Detecting/DetectionOrchestrator.cs` | 15 | 247 | detection slots + pause |
| `Components/Pages/StatusPage.razor.cs` | 20 | 275 | aggregates 8 read models |
| `Components/Layout/ActivityMessageArea.razor.cs` | 25 | 104 | event -> message switch |
| `Playing/PlaybackHandlerTests.cs` / `DetectionOrchestratorTests.cs` | 28 / 21 | - | test churn mirrors code |

### Top unexpected change-coupled pairs (non-test, ratio >= 0.5)

| Pair | Shared | Ratio | Interpretation |
|---|---|---|---|
| `MqttNotifierModal.razor.cs` <=> `WebhookNotifierModal.razor.cs` | 13 | 1.00 | every notifier change edits both identically - missing abstraction |
| `MqttNotifierForm.cs` <=> `WebhookNotifierForm.cs` | 11 | 1.00 | same, at the form layer |
| `KodiPlayerModal.razor` <=> `MqttNotifierModal.razor` / `WebhookNotifierModal.razor` | 10 | 1.00 | modal frame changes hit all three |
| `KodiPlayerForm.cs` <=> `MqttNotifierForm.cs` | 11 | 0.79 | shared form shape |
| `DetectionRunner.cs` <=> `LibraryScanner.cs` | 12 | 0.86 | scan/hash rules shared across chapters |
| `MediaRow.cs` <=> `LibraryScanner.cs` | 12 | 0.52 | file-path/archive semantics leak |
| `NotifiersPage.razor.cs` <=> `StatusPage.razor.cs` | 11 | 0.85 | notifier-card rendering duplicated |

The ratio-1.00 integration pairs are the dominant real signal.
The cross-chapter pairs (`DetectionRunner` <=> `LibraryScanner`, `MediaRow` <=> `LibraryScanner`) are mostly app-wide convention sweeps (logging, naming, the M8 event-store rewrite) - expected in a young codebase, not leakage.

### Context

A prior `docs/architecture-review-2026-10-02.md` proposed 14 refactors; most are now implemented (chapter modules, `IntegrationModalBase`, `EditedForm`, `PagedTable`, activity-from-committed-events, closed outcome hierarchies).
The findings below are what remains after that work.

## 3. Summary table

| # | Refactor | Lens | S/B | Hotspot? | Lines - | Lines + | Net | Concepts - | Effort | Risk | Confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `NotifierModalBase` for the two notifier modals | 3 (shallow/dup) | S | forms/modals coupled 1.00 | 55-60 | 12-18 | -40 | 5 methods x2 + 2 props | S | low | high |
| 2 | Move the delivery vocabulary to `Notifying/` | 3 (leakage) | S | `MediaRow`/`PlaybackRow` | 0 (moves) | 0 | 0 | 5 misplaced types, 5 cross-chapter imports | S | low | high |
| 3 | One notifier-type registry instead of 4 type-switches | 7 / 3 | S | `NotifiersPage` 0.85 | 15-20 | 20-28 | +5 | 3 switch sites -> 1 table | M | med | med |
| 4 | Typed read-model identity (replace string names) | 1 / 3 (obscurity) | S | 17 sites | 5-10 | 25-40 | +25 | stringly-typed coupling | M | med | med |
| 5 | One shared pause-token type behind `ScanPause`/`DetectionPause` | 8 (dup) | S | both hotspots | 8-10 | 4-6 | -4 | 1 duplicate type | S | low | high |

## 4. Findings

### Finding 1 - Extract `NotifierModalBase` (STRONG)

**Where:** `Components/Pages/Settings/MqttNotifierModal.razor.cs` (47 lines) and `WebhookNotifierModal.razor.cs` (60 lines).
A fresh diff confirms `ValidateIntegrationAsync`, `TestIntegrationAsync`, `SaveIntegrationAsync`, `ReadNotifiersAsync`, `RemoveIntegrationAsync`, `RemoveTitle`, `RemoveMessage` are byte-for-byte identical between the two.
Base: `Components/IntegrationModalBase.cs`.
Forms: `MqttNotifierForm.cs`, `WebhookNotifierForm.cs`.

**Current problem:** change amplification from duplication.
Any change to the notifier save/test flow (e.g. commit `7233431 "Show every refusal of a player or notifier at once"`, which touched all three modals with identical +19/-3 diffs) must be re-typed in both.
Git shows 13 shared commits at ratio 1.00.

**Proposed change:** both forms already expose the identical signature `Result<SaveNotifier> ToSaveNotifier(Notifiers)` and carry `Id`/`Name`/`IsNew`.
Introduce a small `INotifierForm { Guid Id; Result<SaveNotifier> ToSaveNotifier(Notifiers); }` (fits the repo's "interface when more than one type implements it" rule) and a `NotifierModalBase : IntegrationModalBase` holding the seven shared members.
Each concrete modal keeps only its type-specific bits (MQTT password toggle + `AdvancedFields`; Webhook header rows).

```csharp
public abstract class NotifierModalBase : IntegrationModalBase
{
    protected abstract INotifierForm NotifierForm { get; }
    protected abstract Task<ResultBase> RemoveNotifierAsync(Guid id, CancellationToken ct);
    // Validate/Test/Save/ReadNotifiers move here, unchanged.
}
```

**What disappears:** 5 methods + 2 properties duplicated across two files; the mandatory lockstep edit on every notifier-semantics change.

**Behavior change:** none (S).
`[Parameter] Form` inheritance and `DialogParameters<MqttNotifierModal>` binding in `NotifiersPage.razor.cs:70-78` keep working.

**Makes easy:** adding a third notifier type (domain model anticipates more integrations) - the shared flow is written once.

**Estimate:** -55 to -60 lines, +12 to +18 (base + interface).
Concepts: 7 duplicated members.
Sites: 2 modals, 2 forms, 1 new base.
Not in the code-line hotspot top 20 but in the top change-coupling pairs.

**Incremental path:**
  (1) add `INotifierForm`, implement on both forms (structure only);
  (2) add `NotifierModalBase`, move `ReadNotifiersAsync`/`RemoveIntegrationAsync`;
  (3) move `Validate`/`Test`/`Save`;
  (4) delete the duplicated members.

**Risk:** low - no test references the modal types directly (tests cover the forms); `ArchitectureTests` has no rule against intermediate abstract classes.

**Dependencies:** none.

### Finding 2 - Move the delivery vocabulary into `Notifying/` (STRONG)

**Where:** `Playing/DeliveryOutcome.cs`, `Playing/NotifierDelivery.cs` (+ `NotifierDeliveryProjection`, `NotifierDeliveryQuery`), `Delivery` (in `Playing/Playback.cs`), `DeliveryFinished` (in `Playing/Events.cs`).
Five files in `Notifying/` (`INotifierClient`, `MqttNotifierClient`, `WebhookNotifierClient`, `NotificationPublisher`, `NotifierHealthCheck`) all `using Debarr.Playing;` only to reach `Delivery`/`DeliveryOutcome`.

**Current problem:** information leakage / misplaced ownership causing unknown unknowns.
The delivery concept - how a notification attempt ended - lives in the `Playing` chapter, so the `Notifying` chapter depends on `Playing` for its own core vocabulary.
A developer changing delivery semantics must know to edit `Playing/`, not `Notifying/`.

**Proposed change:** move `DeliveryOutcome`, `Delivery`, `NotifierDelivery` + its projection/query, and `DeliveryFinished` into `Notifying/`.
`Playback`/`PlaybackRow` then reference `Notifying.Delivery` (Playing already depends on Notifying for `Notification`, so the direction is consistent).

**What disappears:** the `Notifying -> Playing` dependency for delivery types; the surprise that delivery state is stored under the players chapter.

**Behavior change:** none (S) - namespace move plus `codegen write`.

**Makes easy:** any delivery-history change (retry, new outcome) lands in one chapter.

**Estimate:** ~0 net lines (moves).
Concepts: 5 misplaced types, 5 cross-chapter imports.
Sites: 5 Notifying files, 2 Playing files, `PlayingModule`/`NotifyingModule` registration, `ProjectionRebuildTests`.

**Incremental path:**
  (1) move `DeliveryOutcome` + `Delivery`;
  (2) move `NotifierDelivery` + projection + query and re-register in `NotifyingModule`;
  (3) move `DeliveryFinished`;
  (4) `codegen write`.

**Risk:** low; Fisher projection name is `nameof(NotifierDelivery)` so the read-model name is unchanged.

**Dependencies:** none.

### Finding 3 - One notifier-type registry instead of scattered type-switches (MEDIUM)

**Where:** the notifier type is dispatched by `switch` on the settings type in four places: `Notifying/NotificationPublisher.cs:110-115` (`CreateClient`), `Components/Pages/Settings/NotifiersPage.razor.cs:63-66` (`EditAsync`), `NotifiersPage.razor:35-40` (card markup), and the already-table-driven `NotifierTypeModal.razor.cs:10-24`.

**Current problem:** change amplification.
Adding a notifier type touches the client factory, the page's edit switch, the page's markup switch, and the type-modal table - four independent sites that must stay in sync, with no compile-time link between them.

**Proposed change:** extend the existing `NotifierType` descriptor (already in `NotifierTypeModal`) into one per-type catalog holding: settings type -> form factory, -> client factory, -> display.
`CreateClient` and `EditAsync` key off the catalog instead of switching.
(Blazor modal opening stays type-specific via `ShowAsync<T>`; the catalog returns the modal type.)

**What disappears:** 2-3 hand-maintained `switch` arms that must be edited in lockstep; the `NotSupportedException` default arm in `CreateClient`.

**Behavior change:** none (S).

**Makes easy:** the "more notifiers can follow" path in the domain model.

**Estimate:** -15 to -20, +20 to +28 (net slightly up, but removes a class of "forgot a switch arm" bug).
Concepts: 3 switch sites -> 1 registry.
Sites: 4.

**Incremental path:**
  (1) introduce the descriptor with client-factory + form-factory;
  (2) rewrite `CreateClient` to consult it;
  (3) rewrite `EditAsync`;
  (4) drive the card markup.

**Risk:** medium - Blazor generic modal dispatch is the awkward part; keep the modal-type mapping explicit if full generalization is ugly.

**Dependencies:** benefits from Finding 1 (a shared modal base makes the modal-type mapping uniform).

### Finding 4 - Typed read-model identity (MEDIUM)

**Where:** `Activity/ReadModelChanged.cs` carries `string ReadModel`.
16 components declare `ReadModels` as `nameof(Players)`, `MediaRowProjection.ReadModel`, `nameof(Library)`, etc. (`LiveComponentBase.cs:30`, `StatusPage.razor.cs:52`, `VideoFileDetailPage.razor.cs:90`, ...).
Six `ChangesTo(...)` callers (`DetectionHealthCheck`, `NotifierHealthCheck`, `PlayerConnectionHealthCheck`, `FolderWatcher`, `RootFolderHealthCheck`, one test) pass the same strings.
The names are produced in `ReadModelChangeListener.cs:26-30` from `projection.Name` and `aggregate.Name`.

**Current problem:** unknown unknowns from obscurity.
A projection is matched to a subscriber by a string that is derived from a class name at runtime.
Renaming a projection class or a folded aggregate silently breaks every subscription to it - no compile error, no test failure unless a live-reload test happens to cover that page.
The `MediaRowProjection.ReadModel` const pattern mitigates projections, but folded aggregates (`nameof(Players)`, `nameof(Library)`, `nameof(DetectionSettings)`) have no such anchor.

**Proposed change:** give each read model a typed identity (a small `ReadModelIdentity` value, or a marker/attribute on the projection and folded-aggregate types) and make `ReadModelChanged`, `ReadModels`, and `ChangesTo` use it instead of a bare string.
The listener already enumerates projections and folded aggregates, so it can mint the identities.

**What disappears:** the string-matching contract between the listener and every subscriber; the rename-silently-breaks failure mode.

**Behavior change:** none (S).

**Makes easy:** renaming a read model or adding one without auditing string usages.

**Estimate:** -5 to -10, +25 to +40 (adds a type; net up, but removes a whole class of silent bug).
Concepts: 1 stringly-typed coupling across 22 call sites.
Sites: 16 components + 6 `ChangesTo` + listener.

**Incremental path:**
  (1) introduce the identity type and have the listener emit it alongside the string;
  (2) migrate `ChangesTo` callers;
  (3) migrate `ReadModels`;
  (4) drop the string field.

**Risk:** medium - touches many components; a missed migration shows as a page not auto-refreshing.

**Dependencies:** none.

### Finding 5 - One shared pause-token type (SMALL, clean)

**Where:** `Scanning/ScanPause.cs` and `Detecting/DetectionPause.cs` are byte-identical 8-line types (`sealed class X(Action end) : IDisposable` with `Interlocked.Exchange`).

**Current problem:** duplication.
Two identical disposable-token types.

**Proposed change:** one shared `PauseToken` (or `WorkPause`) implementation; keep the two domain names as thin aliases/subclasses so the ubiquitous-language terms survive (`AGENTS.md` mandates them).

**What disappears:** one duplicated type.

**Behavior change:** none (S).

**Estimate:** -8 to -10, +4 to +6.
Sites: 2 files.

**Note:** the larger pause-protocol duplication (lock + `_paused` flag + `SemaphoreSlim` + cancel-and-wait in `LibraryScanner` and `DetectionOrchestrator`) was tested and rejected - see section 5.

## 5. Rejected candidates

- **Unify the scan/detection pause protocol into one `PauseGate` owner.** Failed the elimination and caller tests.
  A reviewer confirmed the two state machines differ in load-bearing, documented, test-pinned ways: scans wait while paused (`StartScanAsync` loops on `_pauseEnded`) vs detections fail (`TryStart` returns `Result.Fail`); scans have no poller, detections restart via the 1 s queue check; `IsStoppingScan` feeds the UI with no detection analogue; shutdown calls `PauseAndCancelAllAsync` bypassing the semaphore.
  A shared gate needs a wait/fail policy seam larger than the ~20 duplicated lines.
  Git (`e734646`) shows the mirror was a deliberate, measured fix, not drift.
  Only the 8-line token (Finding 5) survives.
- **Make `AddFilePath` unarchive-then-append atomically to delete the scanner's `ArchivedStreamException` retry.** REFUTED.
  Fisher refuses an append in the session that unarchives (documented in `docs/critter-stack.md:262`, `docs/domain-model.md:90-91`, spike-tested).
  The pre-read also feeds `videoFileJoinsLibrary` (commands carry no value back) and avoids a futile 5-retry cooldown ladder.
  The race-retry is irreducible and pinned by `ArchiveRaceTests` and `RootFolderRemovalRaceTests`.
- **Table-drive `ActivityMessageArea`'s event->message switch.** Failed the name test - it is already the single correct owner of message wording; a table would not remove a concept, only relocate the switch.
- **Merge `ScanOutcome` and `LibraryScanOutcome`.** Failed the wrong-abstraction test - `ScanOutcome` (returned value) and `LibraryScanOutcome` (stored event, with `Interrupted`) encode different lifetimes and evolve independently.
- **Drop the `IsNew` flag in the three forms.** Failed the elimination test - `IsNew` drives `Removable` and the connection-state read; deriving it from the aggregate adds a read, not removes a concept.

## 6. Suggested order

1. **Finding 5** (pause token) - trivial, zero risk, immediate.
2. **Finding 2** (delivery vocabulary move) - pure moves, removes a cross-chapter dependency, unblocks clearer chapter boundaries.
3. **Finding 1** (`NotifierModalBase`) - safe, high churn payoff, no test surface.
4. **Finding 3** (notifier registry) - after Finding 1, since a shared modal base makes the type mapping uniform.
5. **Finding 4** (typed read-model identity) - broadest blast radius, do last with live-reload tests as the safety net.

## 7. Open questions

- **Notifier cardinality (unlocks Finding 3's value):** is a third notifier type actually planned?
  If MQTT + webhook is the end state, Finding 3 is speculative generality and only Findings 1/2 pay off.
  The domain model says "MQTT and webhook" as the shipped set - confirm before building the registry.
- **Read-model rename policy (Finding 4):** are read-model names part of the stable UI/health-check contract, or internal?
  If internal, the typed identity is clearly worth it; if names are surfaced (logs, `critter-stack.md` index docs), the identity type must preserve the current string values.
- **Chapter boundary for delivery (Finding 2):** confirm the operator accepts `Playback`/`PlaybackRow` depending on `Notifying.Delivery` (Playing -> Notifying), since it makes the delivery concept owned by Notifying rather than shared under Playing.
