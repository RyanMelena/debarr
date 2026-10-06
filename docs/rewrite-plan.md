# Debarr rewrite plan

This plan rebuilds Debarr against [core-principles.md](core-principles.md).
Each task is one commit on `main`, except M8's, which were commits on the branch `domain-model-event-store`, merged once the milestone's live check passed.
Each task is proven by its tests, and the last task of each milestone adds a live check against the test Kodi.
[domain-model.md](domain-model.md) says what Debarr is and does, with the behaviour every task keeps, and M8 moved the code to it.
[rewrite-history.md](rewrite-history.md) holds the finished milestones with their findings, how the rewrite ported v2, the schema as it was after M7, and how the pieces worked after M7.

## Status

A task's commit marks it done here.

| Milestone | Tasks | Status |
|---|---|---|
| M0 to M8 | 1 to 64 | Done, in [rewrite-history.md](rewrite-history.md) |
| M9. Architecture review follow-up | 65 to 84 | Done |
| M10. UI audit | 85 to 87 | Done; task 86 was the former task 42 |
| M11. Ship | 88 and 89 | Done |
| M12. Appends at a known version | 90 to 94 | Done |
| M13. Known issues after M12 | 95 to 109 | Done |
| M14. Critter Stack audit and M13 follow-ups | 110 to 119 | Done; task 114 changed no code, since Wolverine already checks at startup |
| M15. Complexity audit | 120 to 127 | Done |
| M16. Known issues after M15 | 128 | Not started |
| M17. Folder browser | 129 | Done |

## Decisions

| | Question | Answer |
|---|---|---|
| Q1 | Project layout | One app project `Debarr` and one test project `Debarr.Tests`, which mirrors it. Through M7, folders by general function: `Components/`, `Data/`, `Models/`, `Services/`, `Jobs/`, `Events/`, `Integrations/` (Kodi, MQTT, webhook, ffmpeg), `Hosting/`, `Extensions/`. From task 45, `Domain/` holds every domain type in one folder and one namespace, so no namespace hides a type of the same name. From task 47, decision Q11's chapters replace it, and the rest of the app keeps folders by function, following [domain-model.md](domain-model.md)'s *Folders*. Through task 64, types were `internal` unless Blazor, the tests or Wolverine needed them public; from task 65, every type is public, and nested implementation details stay private. |
| Q2 | Names for the content and its paths | The hash-keyed content is a **video file** (`video_file`, `VideoFile`). Each path to it is a **file path** (`file_path`, `FilePath`). A hardlink is one video file with two file paths. |
| Q3 | When the detector changes | Each detection records its detector version, and current results stay current. The operator runs *Re-detect All* when they want new results. |
| Q4 | Video extensions | An editable list on *Settings > Library*, with a default of `mkv mp4 m4v avi mov ts m2ts wmv webm mpg mpeg`. |
| Q5 | A changed or unscanned path at playback | Playback hashes it inline, a 2 MiB read. Playback never pauses detection or a scan. |
| Q6 | Persistence | Decided 2026-09-29. Every domain write is an event in Fisher, JasperFx's SQLite event store, in `debarr.db`. Each page reads an inline Fisher projection written as plain tables, which a read-only EF Core context queries. Host settings stay in `config.json`, logs in files, and runtime state in memory. The evidence, including three spikes, is the [architecture review](https://claude.ai/artifact/7vmYXpgmbxYaK6EeQ69Xdp). |
| Q7 | Commands | Decided 2026-09-29. One command per operator intent or process step, handled by Wolverine with its Fisher integration in Solo mode. A handler loads its aggregate with `[WriteModel]`, decides with pure static functions, and returns a `Result`, following decision Q11. Handlers, commands, events and aggregates are public, and the image runs pre-generated code. Quartz.NET, the detection orchestrator and Rx keep scheduling and live coordination; sagas, `TimeoutMessage` and Wolverine's scheduling are not used. |
| Q8 | Leaving the library | Decided 2026-09-29. Removing or disabling a root folder archives its video files left with no path at once. A scan that leaves a file with no path archives it at the end of the next library scan, so a move between folders never archives. A file whose hash appears again is restored with its result, override and history. Nothing archived is deleted, and the database only grows. The retention setting and *Purge Now* retire. |
| Q9 | Names | Decided 2026-09-29. The code takes the UI's names, after the ones an operator wouldn't understand are improved, following [domain-model.md](domain-model.md)'s *Renames*. The notification payload's `source` values stay `manual`, `detected`, `container` and `player`. |
| Q10 | How to get there | Decided 2026-09-29. M8 refactors on a branch, one aggregate at a time, with `Playback` first as a gate. A rewrite was rejected: the product doesn't change, and the tests, which check behaviour through the UI, survive a refactor. |
| Q11 | Code layout | Decided 2026-09-30. Vertical slices, as Wolverine recommends and JasperFx's CritterCrush sample lays out on WolverineFx 6.38: a folder per chapter, one capability of the app; `<Aggregate>.cs` with the root's state, `Apply` methods, entities and value types; one `Events.cs` per chapter; a file per command holding the command and a static handler whose pure `Validate` and `Handle` decide; and a file per read model with its row, projection and query. The aggregate holds no decisions. Handlers use the store-agnostic `[WriteModel]` and `[ReadModel]`. Sources: Wolverine's [persistence](https://wolverinefx.io/guide/handlers/persistence.html), [vertical slice](https://wolverinefx.io/tutorials/vertical-slice-architecture.html) and [Fisher](https://wolverinefx.io/guide/durability/fisher/) pages, [CritterCrush](https://github.com/JasperFx/CritterStackSamples/pull/20) and [bobcat #358](https://github.com/JasperFx/bobcat/pull/358). |
| Q12 | Players and notifiers | Decided 2026-09-30. Players and notifiers are each one aggregate on a singleton stream, `Players` and `Notifiers`, whose entities are the players and the notifiers, so a unique name is an invariant their commands keep. It follows `Library` and its root folders. It costs no contention, since players and notifiers are few and one operator edits them. |
| Q13 | Process work on Wolverine | Decided 2026-09-30. Detections stay on `DetectionOrchestrator`, and the activity feed keeps its sources and `ReadModelChangeListener`. Task 53's spike, on the branch `task53-wolverine-process-spike`, met its stop rule: on a buffered local queue, `IListenerCircuit.PauseAsync`, `PauseWithDrainAsync` and an error policy's pause hold nothing, and the queue has no listening agent, so a detection pause would need a hold and a cancellation of Debarr's own. A local queue is also first in, first out, so a file a scan just found waits behind the backlog; it runs two messages for one file at once; and it keeps the parallelism it started with. The wire tap and `UseFastEventForwarding` work, but a bridge through them adds about 0.2 ms a message, still ends in a subject, and gives the feed no single source while detections, jobs and player connections stay outside Wolverine. [critter-stack.md](critter-stack.md) records the measurements. Task 70 put the committed events on the feed through the same listener, which publishes each event a commit appended as a `CommittedEvent` beside its `ReadModelChanged`, with no Wolverine hook, so no measurement here changes. |
| Q14 | Projection storage | Decided 2026-10-01, amending Q6. Media and History are Fisher documents, each folded by a `SingleStreamProjection` and read through Fisher's query session, and a Media row is one video file with its file paths, no longer one file path. Read models whose rows map one event to one row stay flat tables that EF Core reads: the file paths, the detections, the settings, the players, the notifiers, the root folders, the library scan summaries and the history clears. Task 54's spike, on the branch `task54-projection-storage-spike`, timed each storage on the large library's 388,907 events, with the indexes Fisher's indexing guide offers. As documents, every Media query and History's open, filters, search and last playback summaries take 7 ms or less; the flat tables of `task54-video-files-flat-table-wip` take 229 ms for Media's status counts and 492 ms for its failed filter, since every count per file path joins it to its video file. Media rebuilds in 24 s and History in 50 s. Fisher's LINQ refuses `SelectMany`, so a document per video file can't list file paths; one row per file path through `ProjectToEfCore` was as fast to query but took 88 s to rebuild and needed an EF Core write context and Weasel tables kept in step with it by hand. The status is worked out in C# by the fold, so its rule is written once. [critter-stack.md](critter-stack.md) records the measurements and what each storage needs. The History query budget, decided 2026-10-02 in the [architecture review](architecture-review-2026-10-02.md): a History sort or page read on the large library completes in 100 ms or less server-side, the threshold at which a response to input feels instant, which leaves the SignalR round trip and the render inside a 200 to 300 ms total. Task 84 timed every History view against it with and without `PlaybackRow`'s eight title and path indexes and kept them, since without them the title and path sorts take 628 ms to 1.9 s. [critter-stack.md](critter-stack.md)'s *What task 84 found* has the numbers and the views that miss the budget with every index. One of them is an accepted exception, decided 2026-10-04: History's last page under an outcome filter and a player filter took 158 to 397 ms on the large library with every index, against 1 to 13 ms for its first page, and no index is added for it, since one per sort would cost six more indexes on every playback write and rebuild. |
| Q15 | Flat tables | Decided 2026-10-01, amending Q6 and Q14. Every read model is read through Fisher, and EF Core goes. The file paths are `FilePathRow` documents keyed by path, from a `MultiStreamProjection` over the video file streams whose every event writes the whole document, and the scans read a folder's paths through a range of ids in `AdvancedSql`, since Fisher's LINQ refuses string comparisons and its `StartsWith` reads the whole table. The video file page folds the file's stream for its detections, as it already does for an archived file, so every detection survives an archive, a rebuild and a restore, and the detection rows go. The library scan summaries are `LibraryScanSummaryRow` documents from a `SingleStreamProjection`, each listing the root folders its scan covered, and a root folder is the library's, with the newest scan that covered it since it was added. The library, detection and UI settings, the players and the notifiers are read by folding their aggregate's stream, and the history clear is a document on its stream. Composite projections, `EventProjection` and vector projections aren't used, and decision Q14 stands. Task 58's spike, on the branch `task58-flat-table-documents-spike`, timed each on the large library's events against today's flat tables. A scan's read of the 31,914 paths under one root folder takes 100 to 115 ms against 102 to 108 ms, the paths under no root folder 2 to 17 ms against 2.5 to 9 ms, and every other read of a document 3 ms or less against 9 ms or less. Folding a settings aggregate, the players or the notifiers after 50 saves takes 0.45 to 1.2 ms, and a video file's detections 0.4 to 0.7 ms. Every read model rebuilds in 102 s against 136 s, and a scan's writes take at most 9% longer. A root folder can't be a document folded from the library and the library scans: an inline multi-stream document whose fields come from two streams lost an update in 13 to 14 of 20 rounds of concurrent commits, and optimistic concurrency failed every commit. [critter-stack.md](critter-stack.md) records the measurements. |
| Q16 | Sharing read models | Decided 2026-10-01, after task 59. A read model is shared by the pages and processes that read the same facts, and each page shapes it with a query type of its own, since a read model of its own per page costs more than it saves here. Every projection runs inline in its command's transaction on SQLite's one write lock, so a document per file adds work to every video file event: task 58's burst of 1,000 video files took 2.9 to 3.5 s with decision Q15's documents and 3.4 to 4.5 s with every shape it built, and `MediaRow`'s indexes take its rebuild from 15 s to 90 s. A page model that combines streams, such as Status or a root folder with its scans, or counts across them, such as the status counts, would be an inline multi-stream document, which lost an update in 13 to 14 of 20 rounds of concurrent commits. The single-file readers of `MediaRow` read the file's stream instead: the video file page folds it for everything it shows, the detection runner and playback fold the file they act on, and the scanner checks whether a hash is known from its stream's state. `MediaRow` serves Media, the status and detector version counts, the detection queue, archiving and the library-wide commands. `PlaybackRow`, `LibraryScanSummaryRow`, `StoredFilePath` and `HistoryClear` stay shared for the same reasons. |
| Q17 | Appending at a known version | Decided 2026-10-03. Every append to an existing stream states the version of the facts its decision was read from: one aggregate through `[WriteModel]`, a new stream through `StartStream`, and a handler that appends to many streams through `FisherOps.Append(id, version, ...)` at the version of the `MediaRow` it decided from, which Fisher sets to its stream's version. An archive is a versioned append of `VideoFileArchived` beside `ArchiveStream`. Fisher 1.14.0 folds inline projections before it takes SQLite's write lock and checks a stated version under it, so an append with no version that meets another commit stores a document that lost that commit, as task 87's live check found. Once every append states its version, a commit writes its folded document only when no commit landed since the version it read, so an inline single-stream document always equals the fold of its stream and its `Version` is a safe expected version. A store listener refuses an append with no version before anything is folded, and a banned-API analyzer refuses the unversioned and read-the-current-version overloads at build time. One Wolverine rule retries a version conflict, a stream id collision, an archived stream and a held write lock, with short cooldowns, for every handler, and replies with the failure when retries run out. Documents built before the change are rebuilt once. The JasperFx skills behind it, the spike and the designs considered are in `.dev/append-version/`, which `.gitignore` keeps local. |

## Names

This table is Debarr's ubiquitous language: the words the operator sees, the words the code uses, and what each means.
These names are settled before any code is written.
A new concept gets a name here before it gets a type, and a type that implements a named concept takes its name from that concept (`LibraryScanner`) and needs no row of its own.
The role says what the concept is in the domain model, which [domain-model.md](domain-model.md) lays out.
Nothing from v2 carried over by default: every ported type was settled under *Ported types*, now in [rewrite-history.md](rewrite-history.md), before its task started.

### Roles and terms

Every row below has one of these roles, sometimes with the aggregate it belongs to.
Every rule has an owner: an aggregate's commands keep its invariants, a value type owns the calculations over its contents, and a rule that spans aggregates belongs to the event store or to the process that coordinates them, such as a path belonging to one video file at a time, kept by the scan.

| Name | Meaning |
|---|---|
| **Aggregate** | A cluster of facts that must stay consistent together, changed only by the events its commands' handlers decide from its state: a video file, the library, the detection settings, the players, the notifiers, a playback, or the UI settings. Its commands' handlers keep its invariants, the rules its facts must never break, such as "only a detection result replaces the current result" or "no root folder overlaps another". Each is in a chapter. |
| **Entity** | A thing inside an aggregate with an identity of its own that lasts while its details change, such as a root folder or a file path. Only its aggregate changes it. |
| **Value type** | A value defined only by what it holds, with no identity of its own, so two that hold the same are the same, such as a file hash or an override. It owns the rules about its own contents and the calculations over them, such as snapping a ratio to the standard ratios or telling a stream from its player path. |
| **Command** | A request to change one aggregate, named for what the operator or a process wants, such as *Save Override* or *Record Detection*. Its handler loads the aggregate and decides the events from its state, or refuses with a field error or a failure. |
| **Event** | A fact an aggregate decided, named in the past tense, such as *Override Saved*. Events are stored and never change; an aggregate's current state is its events applied in order. |
| **Input** | A report from outside Debarr that starts work, such as a player's playback started event. Debarr decides what to store from it. |
| **Read model** | A document shaped for one page, or for the process that reads it, and kept up to date from the events as they are stored, such as the Media rows or the stored file paths, or an aggregate folded from its stream when it is read, such as the detection settings. Changing a document's shape rebuilds it from the events, so nothing is lost. |
| **Process** | Work that runs over time or on a schedule and sends a command for every write, such as a library scan or the detection orchestrator. |
| **Runtime state** | What a process holds in memory while it runs, lost on a restart, such as the running detections. |
| **Notification** | Something published in process as it happens and never stored, such as an activity event. |
| **UI** | Something only the pages have, such as an activity message. |
| **Host** | Something the host owns, outside the domain: the data directory, the host settings and the log files. |
| **Event store** | Where every event is kept, in `debarr.db`. |
| **Chapter** | One capability of the app, and the folder and namespace that hold its aggregates, events, slices, read models and processes, such as *Detecting*. |
| **Slice** | One command's file: the command and the handler that guards and decides it. |
| **Chapter module** | The static class, named for its chapter plus `Module`, whose `Add<Chapter>` adds the chapter to the host in one call: its services and processes, its activity sources, its health checks, the aggregates the pages fold and its read models' projections. `Program.cs` adds each chapter through its module and keeps the order the processes start in. |

### The library

| Name | Role | Meaning |
|---|---|---|
| **Data directory** | Host | The one directory Debarr writes to, set by `DEBARR__APP__DATADIR` and `/data` by default. It holds `config.json`, the database `debarr.db`, the Data Protection keys in `keys/` and the log files in `logs/`. |
| **Library** | Aggregate | The root folders, and how Debarr scans them: the video extensions, the scan interval and whether it watches folders, and the root folders removed, whose file paths a cut-short removal can leave. |
| **Root folder** | Entity of the library | A folder the operator adds, as Debarr sees it; library scans read everything under it. No root folder is the same as, inside or contains another, enabled or not, compared by whole path segments on the canonical local path. Each holds when a scan last covered it and that scan's error, such as a missing folder. Removing or disabling one archives every video file it leaves with no path. |
| **Folder browser** | UI: a modal | Lists the folders on Debarr's filesystem, from the drives on Windows or `/` elsewhere, so the operator picks a root folder rather than typing its path. |
| **Video extensions** | Value type in the library | The extensions that make a file a video file: an editable list, `mkv mp4 m4v avi mov ts m2ts wmv webm mpg mpeg` by default, stored lowercase with no leading dot. |
| **Library scan** | Process | One pass over every enabled root folder that brings the library's file paths in line with the disk, hashing new and changed files, and records a library scan summary. It runs at startup, every scan interval, on *Scan Now*, and when a root folder is added or enabled. Library scans and folder scans never overlap. |
| **Folder scan** | Process | A scan of one folder under a root folder, after its files change, when watching folders. It records nothing on the root folder. |
| **File scan** | Process | A scan of one path, when a playback or a detection finds it new, changed or unreadable. It never waits on another scan or a detection. |
| **Root folder removal** | Process | The work that follows removing or disabling a root folder, in the background under running work: it removes the root folder's file paths and archives at once every video file they leave with no path. One runs at a time, and stopping the host cancels it; the next library scan removes the file paths a cut-short removal left. |
| **Scan interval** | Value type in the library | How many hours pass between scheduled library scans, 12 by default. Off switches the schedule off. |
| **Scan Now** | UI: a page action | Runs a library scan now, or once the running one ends. Pressed during a scan, it queues exactly one more. |
| **Library scan** | Aggregate | One run of the library scan, on a stream of its own: when it started, each root folder it covered, and its end. A scan a crash stopped stays open until startup ends it as interrupted. |
| **Library scan outcome** | Value type in *Library Scan Ended* and the library scan summary | How a library scan ended: *Finished*; *Failed*, with its error; *Cancelled*, when Debarr shut down or a scan pause stopped the scan, which still ends with the counts of the part that ran; or *Interrupted*, when the process stopped before the scan ended and startup ended it with no counts. |
| **Scan outcome** | Runtime state | How a library or folder scan that returned ended: *Finished*, or *Cancelled* by a scan pause, after which its job runs it again. A scan that fails throws its error. |
| **Library scan counts** | Value type in *Library Scan Ended* | How many file paths a library scan found, files it hashed, video files it added, file paths it removed and video files it archived. |
| **Library scan summary** | Read model | What one library scan did: when it started, how long it took, how many file paths it found, files it hashed, video files it added, file paths it removed and video files it archived, and its library scan outcome. Every one is kept, and *System > Tasks* lists the newest, the running one first. |
| **Root folder scanned** | Event of a library scan | The library scan covered a root folder: its error, such as a missing folder, or none when the scan read it, recorded as that root folder finishes. |
| **Watch folders** | Value type in the library | Scanning a folder once changes in it have been quiet for 10 seconds, under every enabled root folder. The scheduled library scan covers changes watching misses, such as writes to a network share from another machine. |
| **Video file** | Aggregate | The content one file hash identifies, wherever it is stored, and its size. It holds its file paths, its detections, its current result, its last failure and its override. Copies and hardlinks of one file are one video file with several file paths. |
| **File path** | Entity of a video file | One path under a root folder to a video file, with the file stat it had and when a scan last hashed it, and when a scan first found it. |
| **Stored file path** | Read model | One file path as the scans compare it with the disk: its video file and the stat it was hashed at, keyed by the path. |
| **File stat** | Value type | A file's size and modification time as a scan read them when it hashed the file, which tell whether the file changed since. The modification time is UTC to the millisecond. |
| **File hash** | Value type | SHA-256 of the file size plus 1 MiB from the start and 1 MiB from the end, or of the whole file when it is under 2 MiB, as lowercase hex. It is a video file's identity. |
| **Archived** | Value type: a video file's state | A video file with no path left, kept with its result, override and history. It leaves every page and the detection queue, and comes back when a file with its hash appears again. Nothing archived is deleted, so the database only grows. |
| **Video file status** | Value type, worked out | Where a video file stands: *Manual*, *Detected*, *From File*, *Failed* or *Pending*, worked out from its override, its current result and its last failure following [domain-model.md](domain-model.md)'s *Behaviour*. |
| **Media row** | Read model | One video file as the Media page lists it: its file paths with when a scan first found each, when a scan first found the video file, shown as *First Seen*, its status and override, the current result's raw ratio, source and confidence, and the last failure. |
| **Video file detail** | Read model | The page for one video file: its file paths, its status, its current result with the container metadata and samples, its last failure, its override, its detections and its playbacks, and *Detect Now*. |

### Detection

| Name | Role | Meaning |
|---|---|---|
| **Detection** | Entity of a video file | One finished attempt to find a video file's ratio: ffprobe for the container ratio, and cropdetect when that ratio's standard ratio is marked *Check Picture*. It records what started it, the path it read, when it started, how long it took, the detector and ffmpeg versions, the container metadata, and its detection outcome. A cancelled or discarded detection records nothing. A video file's detections are its detection history. |
| **Container metadata** | Value type | What ffprobe reports about the video stream: the container ratio, width, height, codec and colour transfer. |
| **Detection result** | Value type | What a successful detection found: the raw ratio, its source (*Detected* or *From File*), the confidence and the crop samples. |
| **Current result** | Value type in a video file | The detection result a playback uses, marked *Current*. Only a new detection result replaces it: a failed detection is recorded beside it. |
| **Detection outcome** | Value type in a detection | Either its detection result or its detection failure, never both. |
| **Detection failure** | Value type: a case of the detection outcome | Why a detection failed, in the detector's words. It is recorded once and not retried until the operator asks or the file changes. |
| **Detection failure explanation** | Value type, worked out | What a detection failure means to the operator: what went wrong in plain words, what to do next and the page where it is done, and the tool's own words without memory addresses or the file's path. |
| **Crop sample** | Value type | One cropdetect run at one sample point, and the picture box it found. The confidence is the share of samples that agree. |
| **Detected** | Value type: a source | A ratio measured from the picture with cropdetect. |
| **From File** | Value type: a source | A ratio the file states, accepted without measuring the picture. The notification calls it `container`. |
| **Manual** | Value type: a source and a status | A ratio or a status set by the override. The notification calls it `manual`. |
| **Detection settings** | Aggregate | How detection runs: simultaneous detections, the picture measurement settings, the timeout, the standard ratios and the match tolerance. |
| **Standard ratios** | Value type in the detection settings | The ratios a raw ratio snaps to: 1.33, 1.66, 1.78, 1.85, 2.00, 2.20, 2.35 and 2.39 by default. Detection, the notification and the UI all snap through one function. |
| **Check Picture** | Value type on a standard ratio | A file whose container ratio snaps to a standard ratio marked *Check Picture* has its picture measured; 1.33 and 1.78 by default. With none marked, every container ratio is accepted. |
| **Match tolerance** | Value type in the detection settings | How far a raw ratio may be from a standard ratio and still snap to it, 0.04 by default and never under 0.01. |
| **Snap** | Value type: an operation of the standard ratios | Matching a raw ratio to the nearest standard ratio within the match tolerance, or rounding it to two decimals when none is near. A ratio exactly one tolerance away matches, and a tie goes to the smaller standard ratio. |
| **Standard ratios change** | Process | Bringing every detection result in line after the standard ratios or the match tolerance change, following principle 12. It shows as *Standard Ratios Change* in a detection's *Started By*. |
| **Recheck scope** | Value type in *Standard Ratios Changed* | Which current results a standard ratios change re-checks: *All* when the match tolerance changed or a standard ratio was added or removed, otherwise *Some*, those whose container ratio snaps to a standard ratio whose *Check Picture* changed, or *None* when nothing that re-checks changed. |
| **Simultaneous detections** | Value type in the detection settings | How many queued detections run at once, sized to the disks the library sits on. |
| **Picture measurement** | Value type in the detection settings | Sample count, skip start and end, and the black levels (SDR and HDR) that cropdetect uses. |
| **Detector version** | Value type | A number raised whenever the detector's output for a file can change. Each detection records it, and the operator runs *Re-detect All* to replace older results. |
| **Detection queue** | Read model | The pending video files that have a file path, newest first. |
| **Detection orchestrator** | Process | Starts every detection: the detection queue's, up to the simultaneous detections, and *Detect Now*'s at once in a detection slot of its own. One detection runs per video file, and one *Detect Now* at a time. |
| **Detection slot** | Runtime state | Room for one running detection. The detection queue's detections share as many as the simultaneous detections allow, and *Detect Now* has one of its own. |
| **Queue check** | Process | One pass of the detection orchestrator that starts a detection in each free detection slot, taking the newest pending video files in the detection queue: at startup and then once a second, one at a time. |
| **Detect Now** | UI: a page action | Detects one video file at once, whatever its status, beside the detection queue. |
| **Re-detect All** | Command | Clears every video file's current result and last failure, keeping overrides and detection history, so the whole library rejoins the detection queue. |
| **Running detection** | Runtime state | A detection in progress: the video file, its most recently hashed file path, what started it, and when. Held in memory, so the list is empty after a restart. |
| **Scan pause** | Runtime state | A span in which no library or folder scan runs. It cancels the running one and waits for it to end, the cancelled scan runs again once the pause ends, and a scan that falls due meanwhile runs after it. A library write pause starts with one, so a standard ratios change, *Re-detect All* and a root folder removal run inside one, and a file scan runs during it. |
| **Detection pause** | Runtime state | A span in which no detection runs. It cancels every running detection and waits for them to end, and a cancelled detection records nothing. A library write pause takes one after its scan pause, so a standard ratios change, *Re-detect All* and a root folder removal run inside one. |
| **Library write pause** | Runtime state | A scan pause and then a detection pause, taken together by a command that writes to every video file it reaches in one transaction, or archives them: a standard ratios change, *Re-detect All* and a root folder removal. Ending it ends the detection pause and then the scan pause, each once however often it is ended, and a start that fails after its scan pause began ends that scan pause. |
| **Override** | Value type in a video file | An optional fixed ratio, *Don't Send* flag and note on a video file, with when it was last saved. When both are set, *Don't Send* wins, and the ratio is kept for when the flag is cleared. An override never takes a file out of the detection queue. |
| **Don't Send** | Value type in an override | Sends nothing when the file plays; a playback's outcome says *Not sent*. |

### Players, notifiers and playback

| Name | Role | Meaning |
|---|---|---|
| **Players** | Aggregate | Every player, and the players removed, so each player's name is unique among them. |
| **Player** | Entity of the players | A media player Debarr connects to, which reports playback started events: its unique name, its player endpoint, and its path mappings. Debarr never starts or stops a player. |
| **Player endpoint** | Value type in a player | Where a player listens and how its requests are timed, such as a Kodi endpoint's host, port, ping interval and request timeout. Its type is the player's type (Kodi for now), it canonicalises the player paths of that type, and it holds only values within their bounds. |
| **Player connection** | Runtime state | Debarr's live link to one enabled player: it connects, reconnects with backoff, pings, and reports its state and the player's playback started events. Its state is Connecting, Connected or Disconnected, each with when it began; through its retries it stays Disconnected with when it was lost and the latest error. |
| **Player path** | Value type | A path as a player reports it, canonicalised by that player type's rules. |
| **Local path** | Value type | A path as Debarr's filesystem sees it. Root folders and file paths are local paths, and so is a translated player path. |
| **Path mapping** | Value type in a player | A player path and a local path. A player path translates by the longest path mapping whose player path it starts with, and one no path mapping matches is used unchanged. |
| **Path mapping entry** | Value type in *Save Player* | A path mapping as the operator entered it, before its player path is canonicalised by the player type's rules. |
| **Playback started event** | Input | A player's report that it started playing a file: the player, the player path, the player-reported ratio, the title and the time. |
| **Stream** | Value type: a kind of player path | A playback whose player path starts with `plugin://` or `pvr://`, such as an add-on or live TV. It has no file under a root folder, so it sends nothing and says so. |
| **Playback** | Aggregate | The record of one playback started event, whether or not it sent anything: when Debarr received it, the player's name as it was, the title, the player path and the local path, the video file and the detection whose result it sent when there are any, the player-reported ratio, its playback outcome and its deliveries. It outlives its player and its video file. |
| **Playback outcome** | Value type in a playback | Either the ratio a playback sent with its source and detection, or why it sent nothing: a stream, *Don't Send*, no player ratio, or a failure with its error. |
| **Notification** | Value type | The payload Debarr publishes for a playback started event: `player` (the player's unique name), `occurred_at` (when Debarr received the event, UTC ISO-8601 with milliseconds), `aspect_ratio` (snapped) and `source` (`manual`, `detected`, `container` or `player`). Each playback started event publishes one notification or none. |
| **Notifiers** | Aggregate | Every notifier, and the notifiers removed, so each notifier's name is unique among them. |
| **Notifier** | Entity of the notifiers | A destination Debarr publishes notifications to, over MQTT or a webhook, with its unique name and its type's settings. |
| **Notifier settings** | Value type in a notifier | Where and how a notifier sends, such as an MQTT broker address, topic template and QoS, or a webhook's URL, method and headers. Its type is the notifier's type, and it holds only values within their bounds. |
| **Notifier client** | Runtime state | Debarr's link to the destination one notifier names, over MQTT or a webhook: it sends one notification and reports every failure as a failed result. |
| **Delivery** | Entity of a playback | One attempt to send one notification through one notifier, and how it ended: delivered, failed, or cancelled by a newer playback, with its error and duration. It keeps the notifier's name as it was. |
| **History** | Read model | Every playback, newest first, with its deliveries, on the *History* page. |
| **Clear History** | Command | Hides every playback and delivery so far from History, Status and a video file's playbacks, and keeps their events. It records when the history was cleared as *History Cleared*, on one stream for the history. |
| **History clear** | Read model | When History was last cleared, on the history's stream; History, Status and a video file's playbacks show only what came after it. |
| **Last playback summary** | Read model | One line per player saying what its last playback sent and why, or why nothing was sent. It is the player's newest playback, so it survives a restart. |
| **Notifier delivery** | Read model | One delivery as Status, *Settings > Notifiers* and the notifier health check read a notifier's newest deliveries, keyed by the delivery, so a notifier with none reads nothing. |

### The application

| Name | Role | Meaning |
|---|---|---|
| **UI settings** | Aggregate | How the UI shows things, on *Settings > UI*, as Radarr has it: the theme (auto, light or dark), the short and long date formats, the time format and whether recent times show as relative. |
| **Stream version** | Value type | A stream and the version a read model folded it at, which a command that decided from that read model states when it appends to the stream. |
| **Write conflict** | Exception | A command's write that met another command's append, stream start, archive or write lock after it read the facts it decided from. The command replies with a `WriteConflictError`, its sender sends it again to decide on a fresh read, and it fails with *Another change was saved at the same time, so nothing was saved. Try again.* when its retries run out. |
| **Activity event** | Notification | Work in progress or runtime state published as it happens, such as a scan or a detection starting, a player connecting, or the health changing. Never stored. |
| **Read model changed** | Notification | The announcement, after a commit, that a read model changed and for which aggregates. Pages reload on it. |
| **Schedule changed** | Notification | The announcement that the library scan's schedule changed, such as after a save of a new scan interval, so its next run moved. *System > Tasks* reads the next run again on it. |
| **Activity feed** | Notification | The one in-process stream of activity events and read model changes, which pages and activity messages subscribe to. |
| **Activity message** | UI | A short-lived message at the bottom of the navigation that says what ended or was saved, since the running work shows what has started. |
| **Running work** | Runtime state | The library scan, the root folder removal and the detections running now, as the navigation shows them above the activity messages. Folder scans stay out of it. While the navigation is closed, the app bar sums them up in one line that opens it. |
| **Page action** | UI | An action the operator starts on a page or modal, with its busy state, its result and where its failure shows. |
| **Field error** | Value type | A refusal of the value in one field of a form, such as a player name another player has, or a detection setting outside its bounds. The form shows it beneath that field until the field changes. |
| **Log file** | Host | A daily file of Debarr's log under `<datadir>/logs`, at the level `Logging:LogLevel:Default` sets. The newest 7 are kept, and *System > Logs* lists them, shows one and downloads it. |
| **Log entry** | Host | One entry of a log file as *System > Logs* shows it: the time, the level, the source, the message, and the lines that follow it, such as an exception's stack trace. |
| **Health check** | Process | A check for one kind of problem the operator has to fix: no enabled root folder, one whose last scan failed, file paths left under a disabled or removed root folder, an ffmpeg or ffprobe that can't run, an enabled player that is disconnected, an enabled notifier whose last delivery failed, or files that failed detection. Each runs at startup and again when what it reads changes. Each names its kind of problem, and Status lists the messages of one severity in the order the kinds are listed here. |
| **Health message** | Runtime state | One problem a health check found: its severity, what is wrong, the page that fixes it, and when the problem began when that is known. Status lists them, errors first, and a badge on the System navigation item counts them. |

Retired by decision Q8: the retention setting and *Purge Now*, which archiving replaces.

## Schema

`debarr.db` holds the event store: Fisher's `fi_streams` and `fi_events`, which hold every event, and a `fi_doc_` table for each document read model, with the indexes its projection declares.
Fisher creates and updates the tables at startup, and Debarr has no migration of its own.
The documents are `MediaRow`, `PlaybackRow` with its deliveries, `NotifierDelivery` keyed by its delivery, `StoredFilePath` keyed by its path, `LibraryScanSummaryRow` and `HistoryClear`.
The library, detection and UI settings, the players, the notifiers and a video file's detections are folded from their streams and store nothing.
The scans read a folder's file paths through SQL written by hand over `fi_doc_storedfilepath`, and History's search probes its trigram index through a fragment written by hand, each pinned by a test to the SQL Fisher writes.
[rewrite-history.md](rewrite-history.md) keeps the schema as it was after M7, with the reasons for its indexes.

## Milestones and tasks

M0 to M8, tasks 1 to 64, are done, and [rewrite-history.md](rewrite-history.md) keeps them with what each found.

### M9. Architecture review follow-up

M9 carries out the proposals and decisions of the second architecture review, [architecture-review-2026-10-02.md](architecture-review-2026-10-02.md), whose *Decisions taken* section records the fifteen operator decisions behind these tasks and whose *Tasks* section is the source of this list.
Each task is one commit, proven by its tests and the live check it names.
Tasks 74 and 75 reshape stored events and land in one wave, so the importer on `task60-large-library-timings` and the live-check data directories, which task 64 imported again, are updated once more.
Probe code from the review's spikes sits uncommitted in the worktrees `C:/repos/gitea/ryan/debarr-spikes/spike1` to `spike6`, on branches `spike/1` to `spike/6`; tasks 72, 77, 79 and 81 reuse it.
M9 runs after task 64 and before M10's survey, since tasks 73, 78, 79 and 82 change the pages M10 captures.

65. **Make every type public.**
    Sweep the 149 `internal` declarations in `src/Debarr/` to `public`; nested implementation details stay private.
    Add `[WolverineIgnore]` to `PlaybackHandler` and `KodiMessageHandler`, since neither is a message handler and Wolverine scans public classes named `*Handler`.
    Q1 and `AGENTS.md` (*Types, files and names*, *Project structure*) change to "Types are public".
    Tests: the build, the full suite, and a clean `codegen write`.
66. **Move the scan slices to Scanning.**
    Move `AddFilePath`, `RemoveFilePath`, `RemoveFilePaths`, `ArchiveVideoFiles`, `UnarchiveVideoFile` and `StoredFilePath` with their tests to `Scanning/`, and `IFilePathCommand` to `EventStore/`; run `codegen write`.
    `VideoFile.cs`, `Events.cs` and `FileHash.cs` stay in `Detecting/`.
    Tests: the build and the moved tests.
67. **Move the integrations and health checks into their chapters.**
    ffmpeg to `Detecting/`, Kodi to `Playing/`, the notifier clients and factory to `Notifying/`, each health check to its chapter; delete `Integrations/`; `Health/` keeps `HealthCheckService`, `IHealthCheck`, `HealthMessage` and `HealthSeverity`.
    Update *Folders* in [domain-model.md](domain-model.md), *Project structure* in `AGENTS.md`, and the `KodiPlayerConnection` path in the `exercise-kodi` and `debarr-live-check` skills.
    Tests move with their subjects.
68. **Move the activity records and the hidden rules.**
    Move the 30 one-line records in `Activity/` to the chapters that raise them; `Activity/` keeps `ActivityEvent`, `IActivitySource`, `ActivityFeed` and `ReadModelChanged`.
    Move the crop-sample grouping to `Detecting/`, `ToKodiPlayerPath` into `KodiEndpoint`, the log parser and `ReadLastLinesAsync` beside `LogEntry`, the timestamp pair into `Notification.cs`, and `DetectionFailureExplanation` to `Detecting/` with its *Names* role changed to a value type that is worked out.
    Move `AddEventStore`, `SendCommandAsync` and `DecideAgainOnConcurrentAppend` to `EventStore/`, and each aggregate's `Read*Async` beside its aggregate.
    Fix `TimeSpanExtensions.ToDisplayText` to `InvariantCulture`, with a comma-culture test.
    `AGENTS.md`'s extension placement rule gains "unless the receiver or element is a domain type".
69. **Chapter modules.**
    `AddScanning`, `AddDetecting`, `AddPlaying`, `AddNotifying` and `AddAppearance`, each registering its services, health checks, activity sources, folded aggregates and projections through `ConfigureFisher`; Scanning's Quartz extension moves into `Scanning/`.
    `Program.cs` keeps the seven `AddHostedService` lines in order with their comments; the hand-kept folded-aggregate list goes; the order of health messages of equal severity is kept explicitly.
    Name the module concept in the *Names* table first.
    Tests: `ProgramTests`, `CommandPipelineTests`, `HealthCheckServiceTests`.
    **Live check:** boot the app and confirm the start order through the first scan and the player connections.
70. **Committed events on the feed.**
    `ReadModelChangeListener` publishes `CommittedEvent(IEvent)` beside `ReadModelChanged` from the same `AfterCommitAsync` loop, with catch-and-log around `OnNext`; explore an `Observable.Create` adapter over the session listener first and adopt it if it reads better within [reactive-extensions.md](reactive-extensions.md)'s rules.
    `ActivityMessageArea.Describe` matches stored events.
    Delete `PlaybackHandledEvent`, `DeliveryFinishedEvent` and `DetectionResultsClearedEvent` with their pushes; `PlaybackHandlerTests` waits on the committed events instead.
    [reactive-extensions.md](reactive-extensions.md) *Subjects* and *Debarr's streams* and decision Q13 gain their sentences.
    Tests: `ActivityMessageAreaTests`, `ReadModelChangeListenerTests`, `PlaybackHandlerTests`.
71. **Library follow-up work into the slices.**
    `AfterCommitAsync` on `SetRootFolderEnabledHandler`, `RemoveRootFolderHandler`, `AddRootFolderHandler` and `ChangeLibrarySettingsHandler`, taking the command, the `[WriteModel] Library`, `ISchedulerFactory` and `RootFolderRemover`, each catching and logging its own failure.
    A `ScheduleChanged` stream adapted from Quartz's `ISchedulerListener`, the way `ObserveJobs` adapts `IJobListener`, for *System > Tasks*.
    `LibraryPage` sends its four commands and keeps the full-path and folder-exists checks at the boundary; `TasksPage` calls `LibraryScanJob.TriggerNowAsync`.
    Delete `LibrarySettingsService` and `LibrarySettingsSavedEvent`; `FolderWatcher` triggers on `ChangesTo(nameof(Library))`.
    Tests: the four `LibrarySettingsServiceTests` cases that pin the reschedule, the kept interval, the scan on enable and the background removal keep their assertions as slice and page tests; the five host-level duplicates of `LibraryTests` go.
    **Live check:** add, disable, enable and remove a root folder, then read the next run on *System > Tasks*.
72. **The detection pause into the slice.**
    `BeforeAsync(ChangeDetectionSettings, DetectionSettings?, DetectionOrchestrator, CancellationToken)` returning `Task<DetectionPause?>` and `Finally(DetectionPause?)` on `ChangeDetectionSettingsHandler`, the same pair with an unconditional pause on `RedetectAllHandler`, and `DecideAgainOnConcurrentAppend` on both, in the shape the review's spike 2 measured (worktree `spike2`).
    Parse once: `Validate` returns `Result<ParsedDetectionSettings>`, `RefusalContinuationStrategy` matches `ResultBase`, and `CommandReply` builds a plain `Result` from the errors.
    `DetectionPage` sends its commands; delete `DetectionSettingsService` and its registrations.
    Record or fix the two side effects: the fetch and the pause run outside the command's log scope, and the pause is disposed twice.
    Tests: all 15 `DetectionSettingsServiceTests` retargeted to the bus; `CommandPipelineTests`.
    **Live check:** save a tolerance change during a detection and read the log for the cancelled detection and the re-check.
73. **Delete the forwarding services.**
    Delete `UISettingsService`, `OverrideService`, `PlayerSettingsService` and `NotifierSettingsService`, their eight activity records and their registrations.
    `UIPage`, `VideoFileDetailPage`, `KodiPlayerModal`, `MqttNotifierModal`, `WebhookNotifierModal` and `HistoryPage` send their commands.
    `PlayerConnectionService` keys on the committed player events; `PlaybackHandler` loses `ClearHistoryAsync` and its subject, and the message becomes "History cleared.".
    Move the three test cases the services pinned alone (the notifier settings round trip, the kept path mapping, an override saved through the page); add the reconnect-on-commit test; the page tests that seed players expect connections to open.
    [ui-conventions.md](ui-conventions.md)'s "A service returns such a refusal" becomes "A handler returns".
    **Live check:** save each settings page and read the message on a second tab; save a player and watch its connection on Status.
74. **Closed hierarchies.**
    `PlaybackOutcome` as five sealed cases, deleting `NotSentReason`, its extension and `PlaybackOutcomeTests`; `DetectionOutcome` for `Detection`, renaming the runner's record `DetectorOutcome`; `RecheckScope` as `All`, `Some` and `None`; `LibraryScanOutcome` with *Cancelled* and *Interrupted* shown apart, with the words added to *Behaviour* "Library scan summary" and the *Names* table; `LocalPath` in `DetectionRequest`, `RunningDetection` and the detection activity events; `LastScanText` takes the scan.
    Update the importer on `task60-large-library-timings` and import the live-check data directories again, as task 64 did.
    Tests: the projection rebuild tests, `PlaybackRowTests`, `MediaRowTests`, `LibraryScanSummaryRowTests`, `TasksPageTests`.
75. **Value types own their bounds.**
    `KodiEndpoint.Create`, a parsed `MqttSettings` (`MqttBrokerAddress`, a Debarr-owned QoS enum) and `WebhookSettings` (`Uri`, `WebhookMethod`), each with a private constructor and a `[JsonConstructor]`; an abstract `Validate()` on `PlayerEndpoint` and `NotifierSettings` that the handlers' `Validate` merges.
    The modals drop the annotations and the pre-save `MudForm.ValidateAsync`; the duplicate path-mapping refusal becomes a row `FieldError`.
    [domain-model.md](domain-model.md)'s *Aggregates* invariants for `Notifiers` and a *Notifier settings* row in the *Names* table.
    Same re-import wave as task 74.
    Tests: the form tests, `NotifiersPageTests`, `PlayersPageTests`, and new handler tests for port 0 and QoS 3 refused beneath their fields.
76. **A pure playback decision.**
    A static decision beside `PlaybackHandler` for *Behaviour* "Playback" steps 2 to 6 and the no-ratio rule; `HandleAsync` keeps the stream check, the file scan and the failure path, with `localPath` and `videoFile` declared before the `try`.
    Delete `ToNotificationAspectRatioSource` from `Detecting/`.
    Tests: a theory over every arm; the six host duplicates in `PlaybackHandlerTests` go.
77. **The removal-versus-scan race.**
    At the scan's end, archive the video files of the paths the sweep removed; in `HashAsync`, treat `ArchivedStreamException` from `AddFilePath` as the signal to send `UnarchiveVideoFile` then `AddFilePath` once more.
    Edit the two *Behaviour* sentences under "Archiving at the end of a library scan"; change `A_library_scan_removes_the_file_paths_under_no_enabled_root_folder_and_archives_their_video_files_at_the_next_one` to the new rule.
    Bring spike 5's `RootFolderRemovalRaceTests` and its `CommitHooks` listener (worktree `spike5`) into the test project as the pinning tests for both orderings.
    **Live check:** disable a root folder during a library scan and confirm the archive and a finished scan.
78. **Components: the paged table.**
    `PagedTableView<TRow>`, `PagedTable<TRow>` and `FilterBar`; `MediaPage`, `HistoryPage` and `LogsPage`'s filter markup use them; the row-enter bookkeeping lives once.
    Tests: `MediaPageTests` and `HistoryPageTests` unchanged in what they assert.
79. **Components: the edited form and the shared frames.**
    `EditedForm<TForm>` on the five pages; an `IntegrationModal` frame for the three modals, with `PlayerConnectionStateText` in the Kodi modal; `HostSettingField` on General; `LiveComponentBase.Reloads` so `LogsPage` and `ElapsedTime` inherit the base; sealed components drop `SuppressFinalize`.
    Delete the 25 `HelperTextOnFocus="false"` and change the [ui-conventions.md](ui-conventions.md) line that prescribes them; splat `PageAction.FieldErrorAttributes` onto the 13 field-error hookups, checking each field's `Min` for an unreachable refusal (spike 3, worktree `spike3`, has the probe test).
    Primary constructors replace the `[Inject]` blocks on the leaf components; base classes keep `[Inject]`.
    Tests: the page tests, a bUnit test of `EditedForm`, the splat probe test.
80. **Delete the pass-throughs.**
    `IPlayerConnection` and its extension (the factory returns the observable and gains `TestAsync`); `NotifierClientFactory` folded into `NotificationPublisher`; the root folder removal moved from `LibraryScanner` into `RootFolderRemover`; the running library scan read from the open summary document; `VideoFile.Recheck` and its helpers into `ChangeDetectionSettings`; `WatchedRootFolderPaths` replaced by a test sync point; `SelectUnit`, `TrimToNull` and `ToFts5Phrase` inlined.
    Tests: the connection, publisher, scanner and watcher tests.
81. **Rules in structure.**
    IDE0130 at error with `EnforceCodeStyleInBuild`; `ArchitectureTests` for no `@code` block, no folder named for a type, the handler shape (spike 6's `HandlerShapeTests`, worktree `spike6`), outlined-dense fields, and one top-level type per file by source scan with the allowed files; a `ProjectionRebuildTests` theory replacing the six hand-written stale-document halves; fix `UnarchiveVideoFileHandler` or allow-list it.
    `AGENTS.md`'s bullets on folder equals namespace, one type per file, code-behind and logging enforcement become "the build enforces this".
82. **Ubiquitous language.**
    *Remove* on the modal buttons and messages and in [ui-conventions.md](ui-conventions.md); `?status=manual` and `?status=from-file`; the Detection page ids renamed with their tests; the Media column *First Seen*; "Removing {RootFolder}" in running work; "Notifier saved." and "Notifier removed."; `AGENTS.md`'s naming example, [domain-model.md](domain-model.md)'s process list, and the *Names* rows; `LibraryScanJob.ScheduleAsync(ScanInterval)`; `TasksPage` and `HistoryPage` drop `ISchedulerFactory` and `PlaybackHandler`; one table per display enum for word, icon and colour.
    **Live check:** capture the pages and read the words.
83. **Test hosting.**
    `AppTestContext` merging `ReadModelTestContext` and `HealthCheckTestContext`; `TestHost.StartAsync` over the chapter modules plus the hosted services a test names; the twelve copied host setups go; the duplicated host-level cases, the `DispatchProxy` store, the three HTTP handler doubles and the retired-feature assertion go; slice tests named for the slice.
    Measure `dotnet test` before and after.
    Find what holds a test's `debarr.db` past `TestDataDirectory.DeleteAsync`'s 5 s, which fails one test with *being used by another process* in 2 of 8 full runs since task 64 added `RebuildCommandTests`, and in none of 8 before it.
    The failing test varies, most often `PlaybackRowQueryTests`' trigram search test.
84. **History's indexes, measured.**
    On the large library, time every History view with only the time, player, outcome and trigram indexes against the bar of 100 ms server-side; drop the seven title and path indexes if every view passes, keep them otherwise.
    Record the bar beside Q14 as the History query budget and the result in [critter-stack.md](critter-stack.md); adjust `PlaybackRowQueryTests`.

### M10. UI audit

M10 checks the UI after the refactor, and carries the accessibility and layout work that was M7's task 42.
It keeps the conventions tasks 33 to 41 settled in [ui-conventions.md](ui-conventions.md), and follows M7's rules: each task captures every page with Playwright at desktop and phone width before and after, following the `debarr-live-check` skill, and bUnit tests pin each behaviour a task adds.

85. **Survey.**
    Open every page and modal in each state task 32 listed, and in the states M8 added: an archived video file, a root folder being archived, and the settings pages under their new terms.
    Compare each capture with its capture from task 41, and add each difference the refactor caused as a bullet under task 87.
    Add each accessibility or layout finding as a bullet under task 86.
    Change no code.
86. **Accessibility and layout.**
    Check every page at phone, tablet and desktop width: nothing scrolls sideways, tables collapse to a readable layout, and modals fit the screen.
    Check that every control is reachable and usable from the keyboard, that focus moves into a modal and back out, that every icon button has a label, and that text and status colours meet WCAG AA contrast on both themes.
    Give every page a `PageTitle` that names it.
    These findings came from task 32's captures, before M8:
    - `capture.py`'s checks found no page that scrolls sideways and no control without an accessible name, at either width.
    - At phone width, tables clip inside their cards instead of collapsing.
      Root Folders hides the *Enabled* switch and the remove button, Library Scans shows 4 of 7 columns, History hides *Sent* and *Deliveries*, and the detail page's Detections and Playbacks hide *Result* and *Sent*.
    - On desktop, a long UNC path makes Media's table wider than its card, so the *Detect Now* column needs the table scrolled sideways.
    - At phone width, Status breaks paths mid-word and wraps Bedroom's error across 10 lines, and the log viewer wraps mid-word.
    - On a phone, the MQTT modal's topic template help sits under the footer, with no sign that the body scrolls.
    - A long webhook URL runs past its notifier card's right edge.
    - Media's phone cards take about 300 px each and list empty Ratio, Source and Confidence rows for a failed file.
    - The override ratio is a numeric spinner whose arrows take much of the field on a phone.
    - The not-found page and the confirmation dialogs leave the document title empty.
    Task 85's captures, in `.dev/task85-captures/` (`head/` after M9, `task41-rebuilt/` from task 41's commit), found these after M8:
    - Of the bullets above, the MQTT modal's topic template help, Media's table on desktop at 1440 px, and the empty titles of the not-found page and History's *Clear History* confirmation no longer show, and the rest still show (`head/library/settings-library-phone-light.png`, `system-tasks-phone-light.png`, `system-status-phone-light.png`, `video-file-detected-playbacks-phone-light.png`, `head/empty/system-logs-phone-light.png`).
      The long webhook URL went unchecked, since the survey's only webhook URL is short.
    - At phone width History's card labels break mid-word, *P / at / h* beside a long path (`head/library/history-phone-light.png`, `history-player-failed-phone-light.png`).
    - At phone width the detail page's File Paths card breaks each path and its root folder link mid-word, such as `\\nas.exam / ple.lan` and *Bro / ken Rip* (`head/library/video-file-detected-phone-light.png`, `video-file-failed-phone-dark.png`).
    - At phone width *System > Tasks*' Scheduled table hides *Next Run* (`head/library/system-tasks-phone-light.png`).
    - At phone width the app bar's running work summary runs under the *Debarr* title and is cut off at the right, *Debarrmoving a root folder, detecting 2* (`head/m8/removal-running-library-1-phone-light.png`).
    - On desktop a running work item in the drawer cuts its subject short, `Removing C:\repos\gite…` and *Detecting The Fabelman…*, so it doesn't say which root folder or file it is about (`head/m8/removal-running-library-1-desktop-light.png`, `head/running/running-tasks-desktop-light.png`).
    - At phone width the footer of every Kodi and MQTT modal wraps *Show Advanced* onto two lines and clips it, and in the edit modals, which add *Remove*, pushes it past the modal's left edge (`head/library/settings-players-edit-phone-light.png`, `settings-notifiers-edit-phone-light.png`, `settings-notifiers-add-mqtt-phone-light.png`).
    - At phone width the error bar's dismiss button sits on the corner of *Reload* (`head/library/error-bar-phone-light.png`, `error-bar-phone-dark.png`).
    - At phone width *Add Root Folder* on Library and *Add Ratio* on Detection wrap onto two lines and squeeze their field to half the card (`head/library/settings-library-phone-light.png`, `settings-detection-advanced-phone-dark.png`).
    - At phone width the detail page's samples table marks a sample that differs with an icon alone, where desktop says *Differs* (`head/library/video-file-detected-phone-light.png`).
    - A *Not Sent* playback lists an empty Deliveries row on its phone card, and on desktop its reason wraps over 3 to 4 lines in *Sent* beside an empty, wide Deliveries column (`head/library/history-not-sent-phone-light.png`, `history-not-sent-desktop-light.png`).
    Task 86 fixed each bullet that still showed, measured contrast and keyboard use with the `debarr-live-check` skill's `audit.py`, and left its captures in `.dev/task86-captures/`: `after/` at 1440, 820 and 390 px, and `before-tablet/` at 820 px from the code before it, since task 85 captured no tablet width.
87. **Fixes.**
    Fix each difference task 85 found that the refactor caused and task 86 doesn't cover.
    The operator's first run on an empty data directory found these, fixed before the survey:
    - A new install listed a library scan of the empty library, with a duration and no counts, beside the scan its first root folder started.
      The startup library scan now runs only when the library has an enabled root folder or a video file.
    - Status and *System > Tasks* both showed the last library scan, and *Scheduled* showed a scan that a root folder started as running.
      Status now shows the state of the library, the players and the notifiers, and *System > Tasks* the background work: *Scheduled* shows the library scan's schedule and its next run, *Library Scans* lists the running scan first, and *Detections* lists the running detections, which moved from Status.
    Task 85 compared its captures with task 41's states, captured again from a worktree at task 41's commit since task 41's own captures were gone, and found these differences the refactor caused (files under `.dev/task85-captures/`):
    - The Kodi edit modal shows the connection state as plain red text above *Enable*, where task 41 showed a red alert (`head/library/settings-players-edit-desktop-light.png` against `task41-rebuilt/library/settings-players-edit-desktop-light.png`).
    - A Kodi modal whose endpoint is refused shows only the endpoint's error: a blank host hides the errors of an empty path mapping row, which task 41 showed together (`head/library/settings-players-refused-endpoint-desktop-light.png` against `task41-rebuilt/library/settings-players-refused-endpoint-desktop-light.png`).
      The handlers check the name and the path mappings only once the endpoint parses.
    - The webhook modal's *Add Header* runs on from the last word of the Headers help, and the Kodi modal's *Add Path Mapping* is a short left-aligned button, where task 41 gave each its own full-width row (`head/library/settings-notifiers-add-webhook-desktop-light.png` and `settings-players-add-advanced-desktop-light.png`, against the same names in `task41-rebuilt/library/`).
    - *System > Logs* ends a line from long-running work with the scope of the command that started it: Bedroom's reconnect warnings end *(SavePlayer …)* and the folder watcher's lines *(AddRootFolder …)* minutes after those commands ended (`head/library/system-logs-desktop-light.png`, `system-logs-warnings-phone-dark.png`).
    - A first run's log holds 17 more Information lines than task 41's, Wolverine's table migrations, node agents, handler discovery and code generation mode, which bury Debarr's own lines (`head/empty/system-logs-desktop-light.png` against `task41-rebuilt/empty/system-logs-desktop-light.png`).
    - History's Deliveries cell lists a failed delivery before a delivered one that was recorded first, where task 41 listed them in the order they were recorded (`head/running/running-history-after-play-desktop-light.png` against `task41-rebuilt/running/running-history-after-play-desktop-light.png`).
    - While a root folder is removed, only the drawer, or the phone's app bar, shows it: Library drops the row at once and shows its empty state, Status says there is no root folder, and Tasks lists nothing (`head/m8/removal-running-library-1-desktop-light.png`, `removal-running-status-1-desktop-light.png`, `removal-running-tasks-1-desktop-light.png`).
      The phone capture right after the confirmation shows neither the row nor any running work (`head/m8/removal-running-library-0-phone-light.png`).
    - Removing a root folder posts *Library settings saved.*, and nothing names the removal until *Archived 4,000 video files.* (`head/m8/removal-running-library-3-desktop-light.png`).
    - Detections keep running on files under a root folder being removed (`head/m8/removal-running-tasks-2-desktop-light.png`).
    - During a removal, Status's health message and its Detection section give different failed counts in the same capture, such as 270 and 258 (`head/m8/removal-running-status-1-desktop-light.png`, `removal-running-status-1-phone-light.png`).
    - A detection of many new files stacks five or more identical *Finished detecting 2 files.* messages in the drawer, past the bottom of the screen (`head/m8/removal-before-library-desktop-light.png`).
    - An archived video file's page is titled *Video File*, and its document title *Video File - Debarr*, naming no file, and its Override card shows Ratio and Note as editable fields beside a disabled *Don't Send* with no *Save*, with nothing saying the card is read-only (`head/library/video-file-archived-desktop-light.png`, `video-file-archived-phone-dark.png`).
    **Live check:** repeat the M6 live check, capture every page again, and compare each with its capture from task 85.

### M11. Ship

88. **Docker.**
    Rewrite the Dockerfile for the new project layout: non-root, read-only media, `TMPDIR`, the pinned static ffmpeg, Wolverine's `codegen write` before `publish`, and a publish that includes the restore.
    **Live check:** run the image with `--read-only` and the real root folders mounted.
89. **Documents.**
    Rewrite `AGENTS.md` and `README.md` for the new design, and write `docs/running-the-tests.md` from its `v2-final` copy for the new test project.

### M12. Appends at a known version

Decision Q17's tasks, in order.

90. **Fisher's append behaviour, pinned.**
    `AppendVersionTests` pins what Q17 rests on: an append with no version that meets a commit between its fold and its write loses that commit from the document, a versioned one fails and writes nothing, an append at 0 starts a missing stream and conflicts with an existing one, two appends at one version to one stream merge, and `AppendOptimistic` states the stream's current version.
    `docs/critter-stack.md` names the tests under *Projection types* and *Checking a new version*.
91. **One retry rule.**
    One global rule in `AddEventStore` retries a version conflict, a stream id collision, an archived stream and a held write lock with cooldowns, and replaces every handler's `Configure` and `HandlerChainExtensions`.
    A command that runs out of retries replies *Another change was saved at the same time, so nothing was saved. Try again.*
92. **Versioned multi-stream appends.**
    `MediaRow` carries its stream's version, and `RemoveFilePaths`, `RedetectAll`, the standard ratios recheck and `ClearHistory` append at a stated version; `RemoveFilePaths` removes only paths its row still holds.
    Each meets a commit between its fold and its write in a test, red first, and every data directory under `.dev` is rebuilt and checked for rows whose version differs from their stream's.
93. **Enforcement.**
    A store listener refuses an append with no version, and a banned-API analyzer refuses the unversioned and read-the-current-version overloads in `Debarr`; test seeds append with `AppendOptimistic` or `StartStream`.
94. **Measured, then live.**
    Re-detect All and a standard ratios change are timed on the large library beside a library scan that rewrites most stats, and their retries counted; if they run out, library-wide commands wait for the running scan.
    **Live check:** a root folder removal and *Scan Now* while detections run, then Re-detect All during a scan; a snapshot of every `MediaRow` and `HistoryClear`, rebuilt and compared, shows no difference.

### M13. Known issues after M12

The operator decided each of these on 2026-10-04, after M12's follow-ups.
Each task is one commit on the branch `m13-known-issues`, red first where a test can show the defect, with the full suite green and a live check for anything the operator sees.

95. **A scan pause cancels the running scan.**
    A scan pause cancels the running library or folder scan, waits for it to end, and starts none until it ends; the cancelled scan runs again once the pause ends, a library scan through its trigger and a folder scan rescheduled for its folder.
    A cancelled library scan shows as *Cancelled* on *System > Tasks*, and the running work reads *Stopping the library scan* while it stops.
    Re-detect All, a standard ratios change and a root folder removal each run inside a scan pause and a detection pause.
    The removal-versus-scan races go with their tests, and the scanner's unarchive recovery stays only for a file scan that meets an archive.
96. **A cancelled command logs as a cancellation.**
    A command whose sender cancels it while it commits logs `{Command} was cancelled after {DurationMs} ms.` at Debug, and Wolverine logs no failure for it.
    A detection logs when it starts and when a pause or shutdown cancels it, at Debug.
97. **A save from a stale form is refused.**
    `EditedForm` marks itself out of date when a reload's saved values differ from the ones its edits started from, and *Save* refuses with *Saved in another tab. Reload to see the change.*; this was task 95 in M12's follow-ups.
98. **A command's log scope opens first.**
    A handler policy opens `{Command} {MessageId}` before the handler's `BeforeAsync` and its aggregate's fetch.
99. **A stored event missing a member fails to read.**
    The event store's serializer respects required constructor parameters, and a nullable one stays optional, since the serializer leaves out a null.
100. **`DiscoveredAt` becomes `FirstSeenAt`.**
     The member on `VideoFileDiscovered`, `VideoFileRestored`, `VideoFile` and `MediaRow`, its indexes and the detection queue's order take the UI's word; a script reshapes the stored events in every `.dev` store, and each is rebuilt.
101. **The scanner publishes no copies of stored facts.**
     `FileHashedEvent`, `FilePathDeletedEvent` and `VideoFilesArchivedEvent` go, and the activity messages count the committed events.
102. **A root folder removal that doesn't finish shows a health message.**
103. **Every refusal at once.**
     One *Test* or *Save* of a player or notifier shows every refusal, a taken name included, whatever the endpoint or settings fields hold.
104. **An unknown sort key resets to the default.**
105. **Help text punctuation.**
106. **Layout without `MudSpacer`.**
     Every `MudSpacer` goes, and three layout faults are fixed, each captured before and after at desktop, tablet and phone.
107. **Notes.**
     Decision Q14 records History's accepted exception to its query budget, and `docs/critter-stack.md` that a rebuild of a store with no events clears nothing.
108. **Tidy-ups.**
     The unpooled connection string in the test hosts, a detection log test that waits on its result, `LibraryStartupService` on the open scan's query, and a comment pass over the files task 68 moved.
109. **The data directory lock flake.**
     A test deleted its data directory waiting on the process's one finalizer thread, which closes every test's connections that Fisher's undisposed commands keep open, and failed when that shared backlog outlasted the delete's 30 s limit.
     The limit is 2 minutes, which still fails a file that never closes, and each run deletes the directories an earlier run left.

### M14. Critter Stack audit and M13 follow-ups

Tasks 110 to 114 are the findings of the Critter Stack audit of 2026-10-03 that still held on `main` at a169f08.
Tasks 115 to 117 are M13's open follow-ups, which the operator decided on 2026-10-04.
Tasks 118 and 119 fix what the review of tasks 115 and 117 found, which the operator decided on 2026-10-04.
Each task is one commit, red first where a test can show the defect, with the full suite green.

110. **Projection rules.**
     `ArchitectureTests` gains two rules: every projection in the store's options is registered `Inline`, since no daemon runs and an asynchronous one would never be written, and no projection's `Create` or `Apply` takes a session, since a fold that reads present-day documents gives a different row on a rebuild.
     Each rule is red first against a probe projection.
111. **The pause comments.**
     The summaries of `ChangeDetectionSettingsHandler.Finally` and `RedetectAllHandler.Finally` say what the generated handler does: it disposes each pause once, through `Finally`.
     `ScanPause` and `DetectionPause` keep their idempotent `Dispose`.
112. **Archiving decides from Media rows.**
     `ArchiveVideoFilesHandler.LoadAsync` reads its batch's Media rows in one query, as `RemoveFilePaths` does, and appends `VideoFileArchived` at each row's `Version`, which decision Q17 makes equal to its stream's.
     A video file with no row is archived already and is skipped.
     The fold of each stream was there because a row could lag its stream before Q17.
     `ArchiveRaceTests` stays green, and AGENTS.md's rule that `ArchiveVideoFiles` decides from folded aggregates goes.
113. **One parse per command.**
     `ChangeLibrarySettingsHandler.Validate` returns a `Result<T>` of the parsed video extensions and scan interval, and `SaveOverrideHandler.Validate` one of the parsed override, which `Handle` and `AfterCommitAsync` take.
     `SetRootFolderEnabledHandler.Validate` returns the root folder it found, so `Handle` and `AfterCommitAsync` stop using `library!.FindRootFolder(...)!`.
     `RecordDelivery` loads its playback with `[WriteModel(Required = false)]`, and its `Validate` refuses a playback that doesn't exist, where the generated guard now returns with no reply and its sender reads a success.
114. **Pre-generated handlers, asserted.**
     The Release branch of `AddEventStore` sets `AssertAllPreGeneratedTypesExist`, so a missing pre-generated handler fails the host at startup rather than at its first command.
     WolverineFx 6.43 already fails a `TypeLoadMode.Static` start on a missing handler, and the flag runs only under `check-env`, so the task changed only AGENTS.md.
115. **A retried attempt logs a warning.**
     Wolverine's `Invocation of <Command> ... failed!` logs at Warning, not Error, when its exception is one the retry rule retries: a version conflict, a stream id collision, an archived stream or a held write lock.
     It logs under the command's type, as Debarr's own line does, so a log filter matches the exception, not the category, and a test pins it, since the filter matches Wolverine's message.
     A command that runs out of retries still logs its failure.
116. **A save from a stale modal is refused.**
     The Kodi, MQTT and webhook modals keep the player or notifier as it was when they opened, and *Save* refuses with *Saved in another tab. Reload to see the change.* when the stored one differs, as task 97 does for the pages.
117. **Component tests wait around a click.**
     A component test that clicks waits for the component to finish loading before the click, and checks what the click shows with `WaitForAssertion`, starting with the *Show Advanced* tests on *Settings > Players* and *Settings > Detection*.
     The namespace runs repeatedly with no failure.
118. **Write conflicts stay out of Wolverine.**
     Task 115's logger factory goes, along with its match on Wolverine's message.
     `CommandMiddleware` catches a write conflict, as it catches a cancellation, and replies with a typed error, so Wolverine logs nothing for it.
     `SendCommandAsync` sends the command again on a write conflict, after the same waits the retry rule used, logs each retry at Warning and a command that runs out at Error, and replies *Another change was saved at the same time, so nothing was saved. Try again.*
     Wolverine's retry rule for write conflicts goes.
119. **The banned triggers stay banned.**
     A test fails when an entry in either `BannedSymbols.txt`, the app's or the test project's, names no symbol in the assemblies the project references, since the analyzer ignores an entry that matches nothing.
     The test helpers that return once the handler reaches its first wait take a name of their own, apart from bUnit's `ClickAsync` and `InputAsync`, which return once the handler completes.

### M15. Complexity audit

Tasks 120 to 127 are the findings of the two complexity audits of 2026-10-04 that [complexity-audit-2026-10-05-ranking.md](complexity-audit-2026-10-05-ranking.md) recommends, as checked on `main` at 900e15d.
The ranking says why each of the others is left out.
No task changes what the app does, so each keeps its tests green as they are, and a test changes only where it names a member that moved, or where a task adds one.
The operator decided the open questions on 2026-10-05: the name *Library write pause*, that the *Running work* row gains the root folder removal and folder scans stay out of running work, that a pause the generated handler disposes twice is accepted, and how the run commits.
The plan and the three reports are one commit on the branch `m15-complexity-audit`, and each task is one commit after it with the full suite green.
The run asks the operator nothing, and it ends with the branch ready for the operator to review and merge.
A task that can't be finished as written is reverted, its reason goes in the *Status* row, and the run goes on with the tasks that don't depend on it; task 125 depends on task 121.
The order puts the one-file tasks first, task 121 before task 125, which edits the same `BeforeAsync`, and the tasks that regenerate handlers or wait on a name after them.

120. **The detail page reads its video file.**
     `VideoFileDetailPage` drops `_archived`, `_currentResult`, `_lastFailure` and `_status`, and its markup reads them from `_videoFile`, which it reads only where the video file exists.
     `ReloadAsync` works out once whether the detections table and the playbacks table show their Path column, where `ShowsPaths` runs again for every row.
     The snapped ratio's `<dd>` is written once, with its label and id chosen by `IsManual`; the ids `video-file-ratio` and `video-file-detected` stay.
121. **The recheck scope, worked out once.**
     `ParsedDetectionSettings` carries the `RecheckScope` of the change, which `ChangeDetectionSettingsHandler.Validate` works out from the settings it already takes.
     `BeforeAsync`, `LoadAsync` and `Handle` read it, and `ChangesStandardRatios` goes with its test's call.
     `BeforeAsync` still parses the command itself, since the generated handler runs it before `Validate`.
122. **A detection's result and error.**
     `Detection` gains `Result`, null for a failure, and `Error`, null for a success, each with `[JsonIgnore]`, since a detection is stored in its events.
     The ten places that match `DetectionOutcome.Succeeded` or `Failed` to reach them read the members: `VideoFileDetailPage` and its markup, `MediaRowResult.From`, `MediaRowFailure.From`, `VideoFile.Status` and `PlaybackHandler`.
     `RecordDetectionHandler` chooses its event from the detector's result, where it tests the outcome it just built.
     A test reads a stored `AspectRatioDetected` and finds no `Result` or `Error` member beside its `Outcome`.
     `CurrentResult` stays a `Detection`.
123. **One rule for a video file's file paths.**
     `VideoFile` holds the rule that adds a file path or replaces the one with its path, beside `InDetectionOrder`, and the rule that removes one, and `VideoFile.Apply` and `MediaRowProjection.Apply` both call them.
124. **One base for the notifier modals.**
     `INotifierForm` names what the MQTT and webhook forms share: `Id`, `Name` and `ToSaveNotifier`.
     `NotifierModalBase<TForm>`, between `IntegrationModalBase` and the two modals, holds the `Saved` parameter, the edited form, the remove title and message, and Validate, Test, Save and Remove, with its services injected as `IntegrationModalBase` injects its own.
     Each modal keeps how its edited form copies and compares, how a stored notifier of its type becomes its form, and its own fields: the MQTT password toggle and advanced fields, and the webhook header rows.
     `KodiPlayerModal` stays as it is, since there is one player type.
     `NotifiersPageTests` stays green unchanged, the stale save of task 116 included.
125. **One library write pause.**
     A *Names* row comes first, *Library write pause*, for the scan pause and the detection pause a library-wide write takes together, and the rows for *Scan pause* and *Detection pause* name it where they list what runs inside one.
     Its type, `LibraryWritePause`, starts the scan pause and then the detection pause, ends them in the reverse order, and ends each once however often it is disposed.
     `ChangeDetectionSettingsHandler` and `RedetectAllHandler` return it from `BeforeAsync` and take it in `Finally`, `RootFolderRemover` holds it in one `using`, and `DetectionOrchestrator.PauseScansAndDetectionsAsync` goes, with the orchestrator's use of `LibraryScanner`.
     `codegen write` regenerates the two handlers.
     Read them before the commit: Wolverine wrapped a disposable that `BeforeAsync` returned in `using var` before the tuple, which disposes it a second time after `Finally`.
     A second disposal is accepted, as task 72 accepted it: a test pins that a pause disposed twice ends once, and the two `Finally` summaries of task 111 say what the regenerated handlers do.
     The rows for *Scan pause* and *Detection pause* stay, since tests take each alone.
126. **Running work from parts, and each process names its events.**
     `RunningWork.Summary` uses the long text when one kind of work runs, and otherwise joins each kind's short text, where a switch lists every combination.
     `DetectionOrchestrator`, `RootFolderRemover` and `LibraryScanner` each hold a predicate, beside the runtime state it guards, for the activity events that change that state.
     The components that read the state use the predicate in `ShowsActivity`: `RunningWork`, `TasksPage`, `MediaPage`, `DetectNowButton`, `StatusPage` and `LibraryPage`.
     The *Running work* row in the *Names* table names the root folder removal, which the component has shown since task 82, and the component shows no folder scans, as now.
127. **One session for a hashed file's stored facts.**
     `LibraryScanner.HashAsync` reads the stored file path and the stream's state in one query session, where it opens two in a row.

### M16. Known issues after M15

128. **A root folder that comes back is watched again.**
     A watcher whose root folder goes missing, such as an unmounted share, stops for good, and today only a library settings save or a restart watches the root folder again.
     A library scan that finds an enabled root folder present and unwatched, while watching folders is on, starts its watcher, and *Watch folders* in [domain-model.md](domain-model.md) says so in the same commit.
     A test removes a root folder, creates it again, runs a library scan, and finds a change in it schedules a folder scan.

### M17. Folder browser

129. **A folder browser for *Add Root Folder*.**
     *Add Root Folder*'s field on *Settings > Library* gains a folder button that opens the folder browser, as Radarr's does, from the typed folder when it exists and from the top otherwise.
     `FolderListing` in `Scanning/` reads one folder's visible direct subfolders, sorted by name ignoring case, or the top: the ready drives on Windows, and `/` elsewhere, so a Docker mount such as `/media` shows at once.
     A folder Debarr cannot find or read is a failure that names it.
     `FolderBrowserDialog` lists the folder with a *..* row while a listing sits above it, lists the folder typed in its field, shows a typed path it cannot list beneath the field, and closes with the folder on *Choose*.
     `IDialogService.ChooseFolderAsync` opens it, and the chosen folder fills the field without adding it, so *Add Root Folder* still refuses an overlapping folder beneath the field.
     `FolderListingTests` checks the listing on a temporary folder, and two `LibraryPageTests` choose a folder and type one that is missing.
