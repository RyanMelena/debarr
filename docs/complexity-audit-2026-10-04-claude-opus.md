# Refactor report, 2026-10-04

Written for the owner of Debarr, choosing which refactors to schedule.
It was made from `main` at `a169f08`, and no code was changed.

## 1. System sketch

- Debarr is one ASP.NET Core host.
  `Program.cs` adds five chapters through their modules: `Scanning`, `Detecting`, `Playing`, `Notifying` and `Appearance`.
- Every domain write is a Wolverine command whose handler decides from a Fisher aggregate (`VideoFile`, `Library`, `Players`, `Notifiers`, …) and appends events.
  Inline projections fold those events into read models (`MediaRow`, `PlaybackRow`, `StoredFilePath`, …).
- Long-running processes hold runtime state outside the store.
  `LibraryScanner` runs scans through Quartz.
  `DetectionOrchestrator` polls the detection queue.
  `RootFolderRemover` runs root folder removals.
  `PlaybackHandler` turns player events into recorded playbacks and notifications.
- Any command that writes to many video files first pauses scans and detections, so their commits can't conflict with it.
- After each commit, `ReadModelChanged` and the processes' own activity events go into the Rx `ActivityFeed`.
  Blazor pages (`LiveComponentBase`) reload when an event names a read model they read or matches their `ShowsActivity` list.

## 2. Evidence

**Method.**
Hotspot score is the number of commits touching the current path multiplied by a count of branch tokens (`if`, `case`, `&&`, `??`, …).
The history covers 230 commits from 2026-09-24 to 2026-10-04.
Renames are not followed, so commits made before a file's last move are not counted.
For change coupling, I counted pairs that changed together in at least 6 commits of 30 files or fewer.
Degree is the shared commit count divided by the smaller file's commit count.
I excluded test-and-subject pairs and razor-and-code-behind pairs.

### Top 10 hotspots

| # | File | Commits | Lines | Branches | Interpretation |
|---|---|---|---|---|---|
| 1 | `Scanning/LibraryScanner.cs` | 29 | 718 | 49 | Four concerns and two lock domains in one class (finding 7). |
| 2 | `Components/Pages/VideoFileDetailPage.razor` | 18 | 378 | 41 | Repeated outcome unwrapping, and some checks run again for every row (findings 1 and 2). |
| 3 | `Components/Pages/VideoFileDetailPage.razor.cs` | 26 | 245 | 25 | Fields that copy `_videoFile` (finding 1). |
| 4 | `Detecting/MediaRow.cs` | 23 | 357 | 17 | Applies the same events as `VideoFile` (finding 8). |
| 5 | `Detecting/DetectionOrchestrator.cs` | 15 | 289 | 24 | Owns the paired-pause method, which takes `LibraryScanner` as a parameter (finding 5). |
| 6 | `Playing/PlaybackHandler.cs` | 18 | 242 | 17 | `Decide` unwraps `Detection.Outcome` by hand (finding 2). |
| 7 | `Components/Pages/StatusPage.razor` | 14 | 257 | 18 | Mostly layout. Its code-behind keeps its own list of activity events (finding 6). |
| 8 | `Playing/PlaybackRow.cs` | 18 | 301 | 11 | 19 indexes chosen for the query plans. Its churn came from rewriting History, not from structure. |
| 9 | `Program.cs` | 35 | 179 | 5 | High churn but few branches, and only 5 of the 56 commits since M8. The chapter modules already took most of it. |
| 10 | `Components/Pages/TasksPage.razor` | 12 | 199 | 14 | Reads the runtime state of three processes directly (finding 6). |

### Top 10 unexpected change-coupled pairs

| # | Pair | Shared | Degree | Interpretation |
|---|---|---|---|---|
| 1 | `ChangeDetectionSettings.cs` ↔ `RedetectAll.cs` | 7 | 0.88 | Both repeat the same paired-pause `BeforeAsync`/`Finally` code (finding 5). |
| 2 | `MediaRow.cs` ↔ `VideoFile.cs` | 8 | 0.53 | The same file path rules are written in both folds (finding 8). |
| 3 | `MqttNotifierModal.razor.cs` ↔ `WebhookNotifierModal.razor.cs` | 7 | 0.54 | The command code is copied line for line (finding 4). |
| 4 | `MainLayout.razor` ↔ `Program.cs` | 7 | 0.44 | All 7 commits are feature additions from before the chapter modules existed (`ff9ce5f` to `2ddd3a4`). Already fixed, so no finding. |
| 5 | `TasksPage.razor` ↔ `LibraryScanner.cs` | 6 | 0.50 | The page reads `IsStoppingScan`, so the scan-pause change `e734646` had to touch it (findings 6 and 7). |
| 6 | `TasksPage.razor.cs` ↔ `LibraryScanner.cs` | 6 | 0.33 | Same cause as pair 5. |
| 7 | `DetectionRunner.cs` ↔ `LibraryScanner.cs` | 6 | 0.43 | All 12 shared commits are M8 sweeps (storage, logging style, public types). Expected during a rewrite, so no finding. |
| 8 | `VideoFileDetailPage.razor.cs` ↔ `PlaybackHandler.cs` | 6 | 0.33 | Both unwrap `Detection.Outcome`, and `d8d3e4d` "Close the outcome hierarchies" had to touch both (finding 2). |
| 9 | `FolderWatcherTests.cs` ↔ `LibraryPage.razor.cs` | 6 | 0.43 | I guess these are watch-setting sweeps, but I didn't check the commits. No finding. |
| 10 | `MediaRow.cs` ↔ `LibraryScanner.cs` | 6 | 0.26 | The scanner queries `MediaRow` for archive candidates and archive counts (`LibraryScanner.cs:164`, `:424`) (finding 7). |

## 3. Summary table

Sorted by how much easier the code gets to understand per unit of effort.
For effort, S is under half a day and M is about a day.

| # | Refactor | Lens | S/B | Hotspot? | Lines − | Lines + | Net | Concepts − | Effort | Risk | Confidence |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Drop the detail page's mirrored fields | State | S | Yes (#2, #3) | 15–25 | 3–6 | −12 to −20 | 4 fields | S | Low | High |
| 2 | Give `Detection` its `Result` and `Error` | Types | S | Yes (#2–#4, #6) | 10–15 | 4–6 | −6 to −10 | 1 repeated pattern | S | Low | High |
| 3 | Work out the recheck scope once | Duplication | S | Yes (11th) | 8–12 | 2–4 | −5 to −9 | 1 helper | S | Low | High |
| 4 | One base for the notifier modals | Duplication | S | Coupled (pair 3) | 35–45 | 20–30 | −10 to −20 | 1 copied protocol (+1 base) | S | Low | High |
| 5 | One library write pause | Boundary | S | Yes (#5, 11th) | 25–35 | 20–30 | −5 to −10 | 1 paired protocol | S–M | Med | Med–High |
| 6 | Keep each process's event predicates next to its runtime state | Cohesion | S | Yes (#7, #10) | 15–25 | 15–25 | ~0 | 1 class of bug | M | Low | Med |
| 7 | Split `LibraryScanner` | Cohesion | S | Yes (#1) | 0–10 | 30–50 | +25 to +45 | 0 (6 fields leave the class) | M | Med | Med |
| 8 | Share the file path list rules | Duplication | S | Yes (#4) | 6–8 | 3–5 | −3 to −5 | 1 rule written twice | S | Low | Med |

## 4. Findings

### Finding 1. Drop the detail page's mirrored fields

**Where:**

- `VideoFileDetailPage.razor.cs:21-24` declares `_archived`, `_currentResult`, `_lastFailure` and `_status`.
  Lines `:123-125` and `:136` copy them from `_videoFile`.
- The markup reads both copies.
  It uses `_currentResult` at `.razor:51` and `:187`, but `_videoFile.CurrentResult` at `.razor:88`, `:90` and `:304`.
- `ShowsPaths(...)` runs once for every table row at `.razor:322` and `:357`, on top of the header calls at `:277` and `:344`.
- `.razor:53-62` renders the same snapped-ratio `<dd>` twice.
  Only the label and the id differ.

**Current problem:**
This causes cognitive load through obscurity.
A reader has to check that each copy is refreshed on every reload path.
`_status` is assigned after the null check (`:131-136`), but the other three are assigned before it, so in the not-found case the fields come from different points in the reload.
Each per-row `ShowsPaths` call scans every row again, so building a tab costs rows² path comparisons.

**Proposed change:**
The markup only reads these values in the branch where `_videoFile` is not null.
Read `_videoFile.Archived`, `.CurrentResult` and `.Status` directly there, or use expression-bodied properties such as `private bool Archived => _videoFile is { Archived: true };`.
Compute `_detectionsShowPaths` and `_playbacksShowPaths` once in `ReloadAsync`.
Render one `<dd>` whose label and id are chosen by `IsManual`.

**What disappears:**
4 fields and 6 assignments, the per-row recomputation, and one duplicated markup block.

**Behavior change:**
None.

**Makes easy:**
Showing a new `VideoFile` fact on the page takes one line.

**Estimate:**
I counted the declarations, the assignments and the duplicated markup lines.
The page is the only site.

**Incremental path:**

1. Replace the copies with reads of `_videoFile`.
2. Compute `ShowsPaths` once per reload.
3. Merge the duplicated `<dd>`.

**Risk:**
`VideoFileDetailPageTests` covers the page and selects elements by id, so `video-file-ratio` and `video-file-detected` must stay.

**Dependencies:**
None.
Pairs well with finding 2, since `_samples` (`:137`) also unwraps the outcome.

### Finding 2. Give `Detection` its `Result` and `Error`

**Where:**
10 sites test `DetectionOutcome.Succeeded` or `Failed` by pattern:

- `VideoFileDetailPage.razor:51`, `:85`, `:293`, `:297`
- `VideoFileDetailPage.razor.cs:137`
- `MediaRow.cs:97`, `:105`
- `RecordDetection.cs:47`
- `VideoFile.cs:37`
- `PlaybackHandler.cs:158`

**Current problem:**
`VideoFile.CurrentResult` and `LastFailure` are both typed `Detection?` (`VideoFile.cs:23-24`).
The type therefore allows a current result whose outcome is `Failed`, so every reader checks for `Succeeded` again.
A reader that gets the check wrong fails silently, as `VideoFile.Status` would by treating such a value as no result.
The symptoms are change amplification and unknown unknowns, caused by obscurity.
The rule that a current result always succeeded only lives where the events are created.
`RecordDetectionHandler` also builds the outcome from `outcome.Result.IsSuccess` (`RecordDetection.cs:46`) and then tests the outcome it just built (`:47`).

**Proposed change:**

```csharp
public DetectionResult? Result => (Outcome as DetectionOutcome.Succeeded)?.Result;
public string? Error => (Outcome as DetectionOutcome.Failed)?.Error;
```

The razor test then becomes `_videoFile.CurrentResult?.Result is { } detectionResult`.
As an optional second step, the rule becomes a type: `CurrentResult(Detection Detection, DetectionResult Result)` in `VideoFile` and `VideoFileRestored`.

**What disappears:**
10 nested property patterns and the double branch in `RecordDetectionHandler`.
With the optional step, the invalid state itself.

**Behavior change:**
None for the accessors.
The optional step changes the stored event JSON (see open question 2).

**Makes easy:**
Any new reader of the current result, such as a payload field or a column, is one member access.

**Estimate:**
I counted the grep hits for `DetectionOutcome.(Succeeded|Failed)`, leaving out constructors and declarations.

**Incremental path:**

1. Add the two members and migrate the 10 sites.
2. Collapse the double branch in `RecordDetectionHandler`.
3. Optionally, introduce the `CurrentResult` type.

**Risk:**
Low.
`MediaRowTests`, `PlaybackHandlerTests` and `VideoFileDetailPageTests` read these paths.

**Dependencies:**
Helps findings 1 and 8.

### Finding 3. Work out the recheck scope once

**Where:**

- `ChangeDetectionSettings.cs:55-57` defines `ChangesStandardRatios`.
- `BeforeAsync` runs `Validate` again and then calls the helper (`:71-73`).
- `LoadAsync` calls the helper again (`:91`).
- `Handle` runs `RecheckScope.ForChange` once more (`:105`).
- The committed generated handler calls `BeforeAsync` (line 42) before `Validate` (line 45), so `Validate` runs twice for each command.
- The helper also has one test caller, `ChangeDetectionSettingsTests.cs:197`.

**Current problem:**
The decision about whether a change triggers a recheck is computed three times from the same inputs.
Each site has to pass `settings ?? DetectionSettings.Default` the same way.
The cost is small, but it is change amplification.

**Proposed change:**
Add the scope to the parsed value:

```csharp
public sealed record ParsedDetectionSettings(PictureMeasurement PictureMeasurement, StandardRatios StandardRatios, RecheckScope Recheck);
```

`Validate` computes the scope once.
`BeforeAsync` tests `validated.Value.Recheck is not RecheckScope.None`, and `LoadAsync` and `Handle` read `parsed.Recheck`.

**What disappears:**
`ChangesStandardRatios` and two recomputations.

**Behavior change:**
None.

**Makes easy:**
A future setting that also has to pause and recheck changes one place.

**Estimate:**
One file plus one test line.

**Incremental path:**

1. Add `Recheck` to `ParsedDetectionSettings`.
2. Migrate the three methods and the test, then delete the helper.

**Risk:**
Low.
`ChangeDetectionSettingsTests` covers it, and the generated handler's signature doesn't change.

**Dependencies:**
Touches the same `BeforeAsync` as finding 5, so do this one first.

### Finding 4. One base for the notifier modals

**Where:**

- `MqttNotifierModal.razor.cs:20-22, 26-58` and `WebhookNotifierModal.razor.cs:18-20, 41-73` are the same 38 lines (Validate, Test, Save, Remove, `ReadNotifiersAsync` and the two Remove strings).
- Both forms expose `Id`, `Name` and `ToSaveNotifier(Notifiers)` (`MqttNotifierForm.cs:54-67`, `WebhookNotifierForm.cs:39-46`).

**Current problem:**
Change amplification caused by a protocol that is written once per notifier type.
The two files changed together in 7 commits.
Any fix to the save flow or to the "Delivered in N ms" message has to be made twice.

**Proposed change:**

```csharp
public interface INotifierForm { Guid Id { get; } string Name { get; } Result<SaveNotifier> ToSaveNotifier(Notifiers notifiers); }

public abstract class NotifierModalBase<TForm> : IntegrationModalBase where TForm : INotifierForm
{
    [Parameter] public TForm Form { get; set; } = default!;
    // [Inject] store, runtime and notificationPublisher, as IntegrationModalBase injects its services (IntegrationModalBase.cs:26-30)
    // RemoveTitle, RemoveMessage, ValidateIntegrationAsync, TestIntegrationAsync, SaveIntegrationAsync, RemoveIntegrationAsync
}
```

Each markup file then declares `@inherits NotifierModalBase<MqttNotifierForm>` or the webhook equivalent.

**What disappears:**
One copy of five methods and two properties.

**Behavior change:**
None.

**Makes easy:**
A third notifier type needs only a settings record, a form and its markup.

**Estimate:**
−38 lines in one modal and −35 in the other, against about +25 for the interface and the base.

**Incremental path:**

1. Implement `INotifierForm` on both forms.
2. Add the base and move the MQTT modal onto it.
3. Move the webhook modal.

**Risk:**
Low.
`NotifiersPageTests` drives both modals.

**Dependencies:**
None.

### Finding 5. One library write pause

**Where:**

- `DetectionOrchestrator.cs:93-109` defines `PauseScansAndDetectionsAsync(LibraryScanner scanner, …)`, which returns a tuple.
- Its callers are `ChangeDetectionSettings.cs:64-82`, `RedetectAll.cs:18-26` and `RootFolderRemover.cs:71-73`.
- `ScanPause.cs` and `DetectionPause.cs` contain the same 6-line class.
- The Names rows for the two pauses (`rewrite-plan.md:139-140`) list the same three users.

**Current problem:**
Every production caller takes both pauses together, in the same order, and ends them in reverse order.
Each site still writes that out as two values: a two-element tuple, a two-argument `Finally` and two `using` lines.
This is dependency plus obscurity.
`DetectionOrchestrator` owns a method that is about neither of its own concerns, and takes the scanner as a parameter to do it.
The `Finally` comments (`ChangeDetectionSettings.cs:77`, `RedetectAll.cs:21`) say the generated handler disposes the pauses a second time.
The committed generated handlers I read (`RedetectAllHandler685385856.cs:40-69` and its ChangeDetectionSettings counterpart, lines 42-84) only call `Finally`.
I did not check the handlers that Debug builds generate at runtime.

**Proposed change:**

```csharp
/// <summary>Scans and detections paused together, for a command that writes to many video files in one transaction.</summary>
public sealed class LibraryWritePause : IDisposable
{
    public static async Task<LibraryWritePause> StartAsync(LibraryScanner scanner, DetectionOrchestrator orchestrator, CancellationToken cancellationToken) { … }
    public void Dispose() { _detectionPause.Dispose(); _scanPause.Dispose(); }
}
```

`BeforeAsync` returns a `LibraryWritePause?` and `Finally` takes one.

**What disappears:**
The tuple, three copies of the dispose order, `DetectionOrchestrator`'s dependency on `LibraryScanner`, and the comment that doesn't match the code.

**Behavior change:**
None.

**Makes easy:**
Another library-wide command, such as clearing all overrides, gets its pause from one `BeforeAsync` line.
Changing the pause order happens in one place.

**Estimate:**
I counted the lines at the four call sites and in the two pause classes.

**Incremental path:**

1. Add the type and use it in `RootFolderRemover`.
   This is plain C# with no codegen.
2. Migrate the two handlers and run `codegen write` again.
3. Delete `PauseScansAndDetectionsAsync`.

**Risk:**
Medium, because this is concurrency code and the regenerated handlers must be committed.
`ScanPauseTests`, `RedetectAllTests`, `ChangeDetectionSettingsTests` and `RootFolderRemovalRaceTests` cover the pause behavior.

**Dependencies:**
Do it after finding 3 and before finding 7, so that finding 7 only has one pause call site to migrate.
Needs a Names row (open question 1).

### Finding 6. Keep each process's event predicates next to its runtime state

**Where:**

- `RunningWork.razor.cs:32-43` is a seven-arm switch over three facts, `:47-52` is its `ShowsActivity`, and `:55-100` is its reload.
- `TasksPage.razor.cs:47-55` and `StatusPage.razor.cs:64-68` keep their own `ShowsActivity` lists.
- The state they read comes from `LibraryScanner.IsStoppingScan` (`LibraryScanner.cs:88-97`), `RootFolderRemover.RunningRootFolderRemoval` (`:40`) and `DetectionOrchestrator.RunningDetections` (`:47-56`).

**Current problem:**
Unknown unknowns.
A component that reads a process's runtime state must also list, in its own `ShowsActivity`, every activity event that changes that state, and nothing checks that the two match.
The coupling between `TasksPage` and `LibraryScanner` (pairs 5 and 6) comes from this.
The `Summary` switch has to list every combination of running work.
Three kinds need 7 arms, and a fourth kind would need 15.

**Proposed change:**
Each process owns a predicate next to its state:

```csharp
public static bool ChangesRunningDetections(ActivityEvent activityEvent) => activityEvent is DetectionStartedEvent or DetectionFinishedEvent;
public static bool ChangesRunningRootFolderRemoval(ActivityEvent activityEvent) => activityEvent is RootFolderRemovalStartedEvent or RootFolderRemovalFinishedEvent;
```

Components OR together the predicates they need.
`Summary` uses the long text when one kind of work runs, and otherwise joins the short parts with ", " and capitalizes the first.

**What disappears:**
Three hand-maintained event lists and the combinatorial switch.

**Behavior change:**
None.
The same events trigger reloads.

**Makes easy:**
A new kind of running work, such as folder scans, needs one predicate and one summary part.

**Estimate:**
−12 lines from the switch and about −15 from the lists, against about +20 for the predicates.

**Incremental path:**

1. Build `Summary` from parts.
2. Add the predicates on the owners and migrate the three components.
3. Fix the Names row (open question 4).

**Risk:**
Low.
`RunningWork` has no test class of its own, but `TasksPageTests`, `MediaPageTests`, `LibraryPageTests` and `NotifiersPageTests` assert on `running-work` ids.

**Dependencies:**
Easier after finding 7 if `IsStoppingScan` moves.

### Finding 7. Split `LibraryScanner`

**Where:**
`LibraryScanner.cs` has 718 lines and 9 private fields (`:47-68`).
It holds four concerns:

- (a) The running scans and the scan pause, about 125 lines guarded by `_lock` and `_pauseLock` (`:56-68`, `:87-131`, `:510-552`, `:600-623`).
- (b) Recording a hashed file's path under its video file, including the per-hash locks and the race with archiving, about 110 lines (`:430-494`, `:625-671`).
- (c) The batched removals and archives, which `RootFolderRemover` also calls (`:387-428`).
- (d) The three kinds of scan and the scheduler's activity events.

**Current problem:**
Cognitive load caused by dependency.
The lock invariants for (a) and (b) are spread through a file whose main job is (d).
Its hotspot score is twice the next file's.
`RootFolderRemover`, `DetectionOrchestrator`, `RunningWork` and `TasksPage` all depend on the scanner for pieces that aren't scanning.

**Proposed change:**

```csharp
// Scanning/RunningScans.cs: the library and folder scans running now, and the scan pause. An IActivitySource for ScanStoppingEvent.
public sealed class RunningScans { Task<RunningScan> StartAsync(string? folder, CancellationToken ct); Task<ScanPause> PauseAsync(CancellationToken ct); bool IsStopping { get; } }
// Scanning/VideoFileLocks.cs: moved unchanged.
// Scanning/FilePathRecorder.cs: HashAsync becomes RecordAsync(FileInfo, CancellationToken), which returns HashedFile?.
```

As part of this, merge the two back-to-back query sessions at `:452-465` into one.

**What disappears:**
No code.
`LibraryScanner` drops from 9 fields to about 3, and each lock sits in a file of about 100 lines with its invariant.

**Behavior change:**
None.

**Makes easy:**
Changing how the pause works without reading the scan code, and testing the hash race without running a scan.

**Estimate:**
Adds 25 to 45 lines for class shells, constructors and registration.
It still qualifies because it separates two independent lock domains, so each one can be checked on its own.

**Incremental path:**

1. Move `VideoFileLocks` to its own file.
2. Extract `RunningScans` and migrate its callers.
   Besides the production callers, that includes `TasksPageTests.cs:205`, `RootFolderHealthCheckTests.cs:122` and `ScanPauseTests`.
3. Extract `FilePathRecorder`.
4. Merge the two sessions.

**Risk:**
Medium.
`LibraryScannerTests`, `ScanPauseTests`, `ArchiveRaceTests`, `RootFolderRemovalRaceTests` and `FolderWatcherTests` cover it.
I haven't checked whether the `debarr-live-check` skill names these types.

**Dependencies:**
After finding 5.
Needs Names rows (open question 1).

### Finding 8. Share the file path list rules

**Where:**
The rule for adding or replacing a path in detection order appears at `MediaRow.cs:163-167` and `VideoFile.cs:66-69`.
The rule for removing a path appears at `MediaRow.cs:172` and `VideoFile.cs:71`.

**Current problem:**
Change amplification.
Each rule is written twice (pair 2).
If the definition of path identity changes, both copies must change, or Media and the detail page will disagree.

**Proposed change:**
Add `Detecting/FilePathExtensions.cs` with `WithFilePath(this IEnumerable<FilePath>, FilePathAdded)` and `WithoutFilePath(this IEnumerable<FilePath>, LocalPath)`, and call them from both folds.

**What disappears:**
One rule written twice.

**Behavior change:**
None.

**Makes easy:**
Changing path identity happens in one place.

**Estimate:**
Two folds and one new file.

**Incremental path:**
One step.

**Risk:**
Low.
`MediaRowTests` and `ProjectionRebuildTests` cover it.

**Dependencies:**
None.

## 5. Rejected candidates

- **Drop the archived-stream pre-check in `HashAsync` (`LibraryScanner.cs:458-476`) and keep only the catch (`:480-486`).**
  This fails on cost.
  The command retry rule retries `ArchivedStreamException` with cooldowns from 50 ms to 1 s, about 1.9 s in total, before it gives up (`EventStore/IServiceCollectionExtensions.cs:73-83`).
  Every restore would stall that long.
- **One generic aggregate for `Players` and `Notifiers`.**
  This fails the concepts test.
  The event types differ in each chapter, and players have path mappings with their own validation.
  A generic version would add a type parameter and event interfaces to save about 40 lines.
- **One search helper for Media and History (`MediaRow.cs:300-316`, `PlaybackRow.cs:262-280`).**
  This fails the concepts test.
  History writes its own `+rowid in (…)` SQL (`PlaybackRow.cs:188-192`) so SQLite keeps reading in index order, while Media uses `NgramSearch`.
  A shared helper would need a strategy parameter and would save about 8 lines.
  Quote handling is tested on both sides.
- **Merge `ScanPause` and `DetectionPause` into one class.**
  This fails the names test.
  They are two separate Names rows, and tests take each one on its own (`RootFolderHealthCheckTests.cs:122`, `DetectionOrchestratorTests.cs:163`).
  Finding 5 removes the cost of pairing them instead.
- **Build `MediaRow` from the folded `VideoFile`.**
  This fails the fit test.
  An inline single-stream projection applies events one at a time, so every document would have to store the whole aggregate, including the full detection history.
- **Move the configuration-source ordering in `Program.cs` (`:29-73`) into `Hosting/`.**
  This fails the hotspot test.
  The churn was feature registrations, which the chapter modules have since taken over, and the file changed in only 5 of the 56 commits since M8.
- **Put `KodiPlayerModal` on finding 4's base.**
  This fails the rule of two, since there is only one player type.

## 6. Suggested order

1. Findings 1, 2 and 3.
   Each touches one or two files, needs no codegen, and has high confidence.
2. Findings 4 and 8.
3. Finding 5, which needs codegen.
4. Finding 7, steps 1 and 2.
5. Finding 6.
6. Finding 7, steps 3 and 4.

## 7. Open questions

1. **Names.**
   Findings 5 and 7 need Names rows before their types exist.
   For example, "Library write pause", "Running scans" and "File path recorder".
2. **Typed current result.**
   Should the current result become a type of its own in `VideoFile` and `VideoFileRestored` (finding 2's optional step)?
   That changes the stored event JSON, so existing dev databases would need a reset.
   The pre-release rule allows the change.
3. **Detect Now during a pause.**
   Should Detect Now during a detection pause wait and run afterwards, instead of failing with "Detection is paused…" (`DetectionOrchestrator.cs:189-192`)?
   Waiting would remove a failure path and its UI error.
   This is a product call.
4. **What counts as running work.**
   The "Running work" row (`rewrite-plan.md:183`) names only the library scan and detections, but the component also shows root folder removals.
   Which one is right?
   And should folder scans show there too?
5. **Restores and retries.**
   Could restoring a video file skip the retry rule, for example with an `AddFilePath` that refuses an archived stream at once?
   That would make the first rejected candidate viable and remove one of `HashAsync`'s two archive branches.
