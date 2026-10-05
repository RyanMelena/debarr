# Complexity audits: overlap and ranking, 2026-10-05

This report compares `complexity-audit-2026-10-04-claude-opus.md` (8 findings) and `complexity-audit-2026-10-04-qwen-3.8.md` (5 findings), and ranks their findings by benefit to the code base.
Both audits were made at `a169f08`.
I checked every finding by reading the cited code at `900e15d`, eleven commits later.
I changed no code and ran no build or test, so every claim below comes from reading unless it says otherwise.

## 1. Overlap

The audits share one finding, disagree on one, and agree on two rejections.

| Topic | Opus | Qwen | Relation |
|---|---|---|---|
| A shared base for the two notifier modals | Finding 4 | Finding 1 | The same refactor. Both rate it low risk and high confidence. |
| `ScanPause` and `DetectionPause` as one type | Rejected | Finding 5 | A direct conflict. Opus rejects the merge because they are two rows in the *Names* table. |
| The pause protocol | Finding 5 pairs the two pauses for their callers | Rejected a shared `PauseGate` inside the two owners | Compatible. Opus changes the callers, and Qwen declines to merge the two state machines. |
| The archived-stream retry in `HashAsync` | Rejected | Rejected | Agreement. |
| Hotspots | `LibraryScanner`, `MediaRow`, `VideoFileDetailPage`, `PlaybackHandler`, `DetectionOrchestrator`, `PlaybackRow`, `StatusPage` | The same seven | Agreement on where the churn is. They differ on what to do there. |

That leaves 12 distinct items.
Opus's other seven findings are inside files, and Qwen's other three are about chapter boundaries and type identity.

## 2. What changed since the audits

Three later commits change the findings.

- `bee4ba7` (refuse a save from a stale integration modal) added 26 lines to each of the three integration modals.
  The notifier modals now hold an `EditedForm<TForm>`, a `Saved` parameter and a `SaveIntegrationAsync` that re-reads the notifier and rebuilds the typed form.
  Both audits' sketches of the modal base and their line numbers are out of date, and the duplication grew.
- `47a31e9` (task 111) rewrote the two `Finally` summaries.
  The comment mismatch that Opus's finding 5 lists under "what disappears" is already fixed.
- `5ec5e45` (parse each command once) touched `ChangeDetectionSettings.cs` but left the three recheck-scope computations of Opus's finding 3 in place.

## 3. Ranking

Ranked by benefit if implemented, highest first.
Benefit weighs what a later change no longer has to know or repeat, against the lines and concepts the refactor adds.

| Rank | Item | Source | Benefit | Effort | Risk | Recommendation |
|---|---|---|---|---|---|---|
| 1 | One base for the notifier modals | Opus 4, Qwen 1 | High | S to M | Low | Proceed, with a new design |
| 2 | Drop the detail page's mirrored fields | Opus 1 | Medium to high | S | Low | Proceed |
| 3 | One library write pause | Opus 5 | Medium | S to M | Medium | Proceed, after item 5 |
| 4 | `Result` and `Error` on `Detection` | Opus 2 | Medium | S | Low | Proceed with the accessors only |
| 5 | Work out the recheck scope once | Opus 3 | Low to medium | S | Low | Proceed |
| 6 | Event predicates beside each process, and `Summary` from parts | Opus 6 | Low to medium | S to M | Low | Proceed, low priority |
| 7 | Share the file path list rules | Opus 8 | Low | S | Low | Proceed when next in these files |
| 8 | Typed read-model identity | Qwen 4 | Low | M | Medium | Do not proceed. A check is enough. |
| 9 | Split `LibraryScanner` | Opus 7 | Low now | M | Medium | Defer. Take step 4 now. |
| 10 | A notifier-type registry | Qwen 3 | None with two types | M | Medium | Do not proceed |
| 11 | Move the delivery types to `Notifying/` | Qwen 2 | None | S | Low | Do not proceed |
| 12 | One shared pause token type | Qwen 5 | None | S | Low | Do not proceed |

## 4. Findings in rank order

### 1. One base for the notifier modals (Opus 4, Qwen 1)

**Checked.**
`MqttNotifierModal.razor.cs` and `WebhookNotifierModal.razor.cs` now share about 50 lines each: the `Saved` parameter, `Form`, `OnInitialized`, `RemoveTitle`, `RemoveMessage`, `ValidateIntegrationAsync`, `TestIntegrationAsync`, `SaveIntegrationAsync`, `ReadNotifiersAsync` and `RemoveIntegrationAsync`.
They differ in three places: the `EditedForm` constructor arguments, the settings type that `SaveIntegrationAsync` matches (`MqttSettings` or `WebhookSettings`), and the factory that rebuilds the form (`FromMqttNotifier` or `FromWebhookNotifier`).
`bee4ba7` had to make the same 26-line edit in both, which is the cost both audits predict.
No test names either modal type.
`NotifiersPageTests` drives them through the page.

**Why first.**
This is the only finding both audits reached, the duplication is the largest of the twelve, and it has grown since the audits.

**Design correction.**
Neither sketch fits the code any more.
Qwen's non-generic base cannot hold `SaveIntegrationAsync`, which needs the typed form.
Opus's `[Parameter] TForm Form` names a parameter that is now `Saved`.
The base needs to be `NotifierModalBase<TForm> where TForm : class, INotifierForm`, own the `EditedForm<TForm>`, and leave each modal two members: how to build its `EditedForm`, and how to rebuild its form from a stored `Notifier` (null when the settings are another type).
The base takes its services through `[Inject]` properties, as `IntegrationModalBase` does.
My estimate is about 100 lines removed and about 65 added.

**Recommendation.**
Proceed.
Leave `KodiPlayerModal` out, as Opus says, since one player type is no pattern.

### 2. Drop the detail page's mirrored fields (Opus 1)

**Checked.**
`VideoFileDetailPage.razor.cs:21-24` declares `_archived`, `_currentResult`, `_lastFailure` and `_status`, and `ReloadAsync` copies each from `_videoFile`.
The markup reads `_currentResult` at `.razor:51` and `:187` but `_videoFile.CurrentResult` at `:88`, `:90` and `:304`.
`ShowsPaths` runs in the header and again for every row (`.razor:277`, `:322`, `:344`, `:357`).
The `<dd>` at `.razor:56` and `:61` differs only in its label and id.

**Benefit.**
The page is the second hotspot in both audits, and the copies are the reason a reader has to trace `ReloadAsync` before trusting the markup.
The rows² cost is real but small, since a video file has few detections.

**Recommendation.**
Proceed.
Keep the ids `video-file-ratio` and `video-file-detected`, which the tests select.

### 3. One library write pause (Opus 5)

**Checked.**
`DetectionOrchestrator.PauseScansAndDetectionsAsync(LibraryScanner, …)` returns a tuple, and its three callers each end the two pauses in the same order: `ChangeDetectionSettings.cs:64-82`, `RedetectAll.cs:18-26` and `RootFolderRemover.cs:71-73`.
The orchestrator takes the scanner as a parameter only for this method.
The comment mismatch the audit lists is already fixed.

**Benefit.**
One type replaces a tuple, a two-argument `Finally` and two `using` lines at three sites, and the dispose order has one home.
A fourth library-wide command would get its pause in one line.

**A risk the audit missed.**
`architecture-review-2026-10-02.md:739` records that Wolverine's generated handler wrapped a disposable returned from `BeforeAsync` in `using var`, so the pause was disposed twice, once there and once in `Finally`.
The tuple return avoids that today.
A single `LibraryWritePause : IDisposable` return would bring the double disposal back.
I infer this from the review's note and have not regenerated the handlers to confirm it.
It is harmless only if the new type's `Dispose` ends the pause once, as `ScanPause` does, and the `Finally` summaries that task 111 wrote would need rewording.

**Recommendation.**
Proceed, after item 5, which edits the same `BeforeAsync`.
Read the regenerated handlers before committing them, and name the type in the *Names* table first.

### 4. `Result` and `Error` on `Detection` (Opus 2)

**Checked.**
The ten pattern sites exist as listed.
`RecordDetectionHandler` builds the outcome from `outcome.Result.IsSuccess` (`:46`) and tests the outcome it built on the next line.

**Benefit.**
Each reader of a current result drops one nested pattern.
The razor sites still need a pattern, since they bind both the raw ratio and the result, so the gain per site is small.

**A detail the audit missed.**
`Detection` is stored inside events.
A computed `Result` property would be written into the event JSON beside `Outcome`, including the samples, unless it carries `[JsonIgnore]`, as `MediaRow.Status` does.
I infer this from System.Text.Json's default of writing get-only properties.

**Recommendation.**
Proceed with the two accessors and the `RecordDetectionHandler` branch.
Skip the optional `CurrentResult` type.
It changes stored state to rule out a value that only two `Apply` methods can create.

### 5. Work out the recheck scope once (Opus 3)

**Checked.**
`RecheckScope.ForChange` runs in `BeforeAsync` (through `ChangesStandardRatios`), in `LoadAsync` and in `Handle`, each with `settings ?? DetectionSettings.Default`.
`Validate` already takes `settings` and does not use it.
The generated handler calls `BeforeAsync` before `Validate`.

**Benefit.**
Three computations and one helper become one field on `ParsedDetectionSettings`, and the unused parameter gets a purpose.
One correction: `Validate` still runs twice per command, since `BeforeAsync` runs first and must parse the command itself.

**Recommendation.**
Proceed.
It is one file and one test line, and it should land before item 3.

### 6. Event predicates beside each process, and `Summary` from parts (Opus 6)

**Checked.**
`RunningWork.Summary` is a seven-arm switch over three facts.
The duplication is wider than the audit counted.
`DetectionStartedEvent or DetectionFinishedEvent` is listed in four components (`RunningWork`, `TasksPage`, `MediaPage`, `DetectNowButton`), and the root folder removal pair in four (`RunningWork`, `TasksPage`, `StatusPage`, `LibraryPage`).
`e734646` added `ScanStoppingEvent` and had to touch both `TasksPage` and `RunningWork`.

**Benefit.**
A new event that changes a process's runtime state is listed once, beside the state.
The `Summary` rewrite removes the combinatorial switch.
No bug from a missed list is on record, so the benefit is preventive.

**Recommendation.**
Proceed at low priority.
The `Summary` step stands alone and is the better half.
The *Names* row for running work omits root folder removals, which the component shows, and that row needs fixing either way.

### 7. Share the file path list rules (Opus 8)

**Checked.**
`MediaRow.cs:163-167` and `VideoFile.cs:66-69` write the same add-or-replace rule, and `MediaRow` already calls `VideoFile.InDetectionOrder` for the ordering.
The remove rule is one expression on each side.

**Benefit.**
About four lines, and one place for path identity.

**Recommendation.**
Proceed when a change next touches either fold.
A static function beside `InDetectionOrder` fits the existing pattern, so a new extensions file is optional.

### 8. Typed read-model identity (Qwen 4)

**Checked.**
`ReadModelChanged` carries a string, and 16 components and 5 processes name read models by string.
The audit's main failure, a rename that silently breaks subscribers, does not happen.
Every name is `nameof(...)` or a `ReadModel` constant that is itself `nameof(...)`, and each projection sets `Name` from the same constant, so a rename carries through at compile time.
The remaining gap is narrower: a component can list an aggregate that no module registered with `AddFoldedAggregate`, and it would then never reload.

**Recommendation.**
Do not proceed.
A new identity type across 22 sites costs more than the gap.
If the gap matters, one check closes it: compare each name passed to `ChangesTo` or listed in `ReadModels` with the names `ReadModelChangeListener` publishes, and fail on an unknown one.
That is a suggestion I have not designed in detail.

### 9. Split `LibraryScanner` (Opus 7)

**Checked.**
The file has 718 lines and holds the running scans with the scan pause, the per-hash locks, the batched removals and the three kinds of scan.
`HashAsync` opens two query sessions back to back (`:452-465`).

**Benefit.**
The split deletes nothing and adds 25 to 45 lines, three *Names* rows and a move of test call sites.
Its value is that each lock domain could be read alone.
The file's churn came from M8 to M13, and nothing has touched it since `c1c5f68` on 2026-10-03.

**Recommendation.**
Defer until a change next has to alter the pause or the hash race.
Take step 4 now, the merge of the two sessions, since it is a few lines in one method.

### 10. A notifier-type registry (Qwen 3)

**Checked.**
The type switches exist in `NotificationPublisher.CreateClient`, `NotifiersPage.EditAsync`, `NotifiersPage.AddAsync` and the card markup.

**Why not.**
With two types, a registry that maps a settings type to a client factory, a form factory, a modal type and card markup adds more than the four two-arm switches it replaces.
The audit's own estimate is a net increase, and its own open question asks whether a third type is planned.
The modal still needs a generic `ShowAsync<T>` call per type.

**Recommendation.**
Do not proceed.
Revisit when a third notifier type is planned.

### 11. Move the delivery types to `Notifying/` (Qwen 2)

**Checked.**
Two of the audit's premises are wrong.
`Notification` lives in `Playing/Notification.cs`, so `INotifierClient`, `MqttNotifierClient` and `WebhookNotifierClient` import `Debarr.Playing` for it and not for `Delivery`.
The move would leave `Notifying` depending on `Playing` and would add imports of `Notifying` to `Playback.cs` and `PlaybackRow.cs`.
The *Names* table defines a delivery as an entity of a playback, and the `Playback` aggregate appends `DeliveryFinished`.
The chapter rule puts an aggregate's events in its own chapter's `Events.cs`.

**Recommendation.**
Do not proceed.
The move removes no dependency and breaks a rule the repository states.

### 12. One shared pause token type (Qwen 5)

**Checked.**
`ScanPause` and `DetectionPause` are the same nine lines.

**Why not.**
The proposal keeps both names as subclasses of a new base, so it ends with three types in place of two and saves about four lines.
Opus rejects the same merge, and item 3 removes the cost of pairing them.

**Recommendation.**
Do not proceed.

## 5. Suggested order

1. Items 2, 5 and 4, which are each one or two files with no generated code.
2. Item 1.
3. Item 3, with regenerated handlers.
4. Items 6 and 7, and the session merge from item 9, as the files come up.

## 6. Decisions that are yours

- The *Names* row for the library write pause (item 3).
- Whether running work includes root folder removals in the *Names* table (item 6).
- Whether a third notifier type is planned (item 10).
