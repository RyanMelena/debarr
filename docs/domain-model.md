# Debarr domain model

This is what Debarr is and does: its aggregates, events, commands and read models, the behaviour it keeps, and the constraints any implementation of it respects.
Milestone M8 of [rewrite-plan.md](rewrite-plan.md) moves the code to this design, following that plan's decisions Q6 to Q10.
Its evidence, including three spikes, is the [architecture review](https://claude.ai/artifact/7vmYXpgmbxYaK6EeQ69Xdp) of 2026-09-29.
The plan's *Names* table is the ubiquitous language, and the code uses its names.

## Aggregates

Every aggregate root, with its entities and value types, is in its chapter's `<Aggregate>.cs`, with a `Create` or `Apply` method per event and no decisions.
A rule that keeps an aggregate consistent is decided by the handler of the command it guards: a pure static `Handle` that takes the command, the aggregate's state and, when an event records a time, the current time, and returns events, and a pure static `Validate` that refuses a command the state can't take.
A rule more than one command needs is a static function in the aggregate's file.

| Aggregate root | Identity and stream | Entities | Value types | Invariants its commands keep |
|---|---|---|---|---|
| `VideoFile` | The file hash, as a string stream key | `FilePath` (keyed by its `LocalPath`, with its `FileStat`, when it was last hashed and when it was first seen), `Detection` (append-only) | `FileHash`, `FileStat`, `Override`, `ContainerMetadata`, `DetectionOutcome` (a `DetectionResult` or a failure's error), `CropSample`, `CropBox` | Principles 4, 15 and 16: only a result replaces a result, a new result clears the failure, and *Don't Send* wins. Its status. A path belongs to one video file at a time, which the library scan keeps, since it is the one process that moves a path to another video file. |
| `Library` | A singleton stream | `RootFolder` (path, enabled), and the paths of the root folders removed and not added again | `LibrarySettings` (`VideoExtensions`, scan interval, watch folders) | Principle 1: no root folder overlaps another. Normalised video extensions. Removing or disabling a root folder drops its paths. |
| `LibraryScan` | A Guid, which the library scan generates when it starts | none | `LibraryScanCounts`, `LibraryScanOutcome` | A scan starts once and ends once, and records nothing after its end. |
| `DetectionSettings` | A singleton stream | none | `StandardRatios` (each `StandardRatio` with *Check Picture*, and the match tolerance, with `Snap`), `PictureMeasurement` (sample count, skip start and end, and the black levels) | Principles 10 and 12: the bounds, a non-empty list with no ratio twice, and the tolerance floor. Each change says which results it re-checks. |
| `Players` | A singleton stream | `Player` (a Guid id, which a new player's form generates, its name, enabled, endpoint and path mappings), and the ids of the players removed | `PlayerEndpoint`, whose type is the player's type and which canonicalises that type's player paths: `KodiEndpoint` (host, port, ping interval, request timeout), `PathMapping` (`PlayerPath`, `LocalPath`) | Each player's name is unique among the players. An endpoint holds only values within its bounds: a Kodi endpoint a host, a port from 1 to 65535, and a ping interval and request timeout of 1 second or more. A player's path mappings have both paths, are unique by player path and canonicalised by the player type's rules. A player keeps its type, and a removed player stays removed. `Translate` from a `PlayerPath` to a `LocalPath` by the longest match. |
| `Notifiers` | A singleton stream | `Notifier` (a Guid id, which a new notifier's form generates, its name, enabled and settings), and the ids of the notifiers removed | `NotifierSettings`, whose type is the notifier's type: `MqttSettings` (`MqttBrokerAddress`, credentials, topic template, `QualityOfService`), `WebhookSettings` (URL, `WebhookMethod`, `WebhookHeader`s) | Each notifier's name is unique among the notifiers. Settings hold only values within their bounds: MQTT settings a broker host and a port from 1 to 65535, a topic template, and a QoS of 0, 1 or 2; webhook settings an absolute http or https URL, POST, PUT or PATCH, and headers that each have a name no other header has, ignoring case. A notifier keeps its type, and a removed notifier stays removed. |
| `Playback` | An id | `Delivery` | `PlaybackOutcome` (sent: ratio, source and detection; or not sent: reason and error), `Notification`, `DeliveryOutcome` (succeeded, failed with its error, or cancelled), `PlayerPath`, `LocalPath` | Principles 17 to 19: one notification or none, and exactly one of sent or not sent. |
| `UISettings` | A singleton stream | none | `UITheme`, the date and time formats | Formats from the offered set. |

- `Playback` is a root of its own rather than part of `Players`: it outlives its player, keeps the player's name as it was, and grows without bound.
  The rule that a newer playback cancels the handling of the older one is ordering in `PlaybackHandler`, not consistency.
- A missing singleton stream folds to the defaults, so nothing seeds settings.
- `AspectRatio` is a value type for a raw ratio, greater than 0, and `SnappedAspectRatio` for one matched to the standard ratios, so principle 11's raw and snapped ratios are different types.

## Events

| Aggregate | Events |
|---|---|
| `VideoFile` | `VideoFileDiscovered`, `FilePathAdded`, `FilePathRemoved`, `AspectRatioDetected`, `DetectionFailed`, `DetectionResultConverted`, `DetectionResultCleared`, `OverrideSaved`, `VideoFileArchived`, `VideoFileRestored` |
| `Library` | `RootFolderAdded`, `RootFolderEnabled`, `RootFolderDisabled`, `RootFolderRemoved`, `LibrarySettingsChanged` |
| `LibraryScan` | `LibraryScanStarted`, `RootFolderScanned`, `LibraryScanEnded`, `LibraryScanInterrupted` |
| `DetectionSettings` | `DetectionSettingsChanged`, `StandardRatiosChanged` |
| `Players` | `PlayerAdded`, `PlayerChanged`, `PlayerRenamed`, `PlayerRemoved`, each with the player's id |
| `Notifiers` | `NotifierAdded`, `NotifierChanged`, `NotifierRenamed`, `NotifierRemoved`, each with the notifier's id |
| `Playback` | `PlaybackHandled`, `DeliveryFinished` |
| none: one stream for the history | `HistoryCleared` |
| `UISettings` | `UISettingsChanged` |

Events store raw ratios only, which keeps principle 11.
An event stores a fact that still matters after a restart, such as a library scan starting, which leaves a scan open until it ends.
What is true only while the process runs stays runtime state and activity events: a running detection, a hash, a folder scan, a player connection's state, and health.

## Commands, processes and read models

- **Commands**, one per operator intent or process step, such as `RecordDetection`, `SaveOverride`, `RedetectAll`, `ChangeDetectionSettings`, `AddRootFolder`, `RemoveRootFolder`, `StartLibraryScan`, `ClearHistory`, `SavePlayer` and `RemovePlayer`.
  Each is a slice file with its static handler, such as `SaveOverride.cs` with `SaveOverrideHandler`.
  Pages send them through `SendCommandAsync`, and processes send them too.
  A command replies only with its refusal or failure, and carries no value back; the page reads what changed from its read models.
- **Process managers** keep their scheduling and send commands for every write: the library scan and `FolderWatcher` on Quartz.NET, `DetectionOrchestrator` with its queue checks and detection pause, and `PlaybackHandler` with its per-player `Switch`.
  `NotificationPublisher`, `PlayerConnectionService` and `HealthCheckService` run beside them and write nothing: they read the store and the activity feed and publish what they find.
- **Read models** are Fisher documents from projections registered inline, which Fisher's rebuild clears with their progress, read through Fisher's query session.
  A read model that folds a stream comes from a `SingleStreamProjection`: a Media row is one video file with its file paths, a History row is one playback with its deliveries, and a library scan summary is one library scan with the root folders it covered.
  A notifier delivery is one delivery, keyed by its id, from a `MultiStreamProjection` over the playback streams whose every event writes the whole document, so a notifier's newest deliveries read one index.
  A file path is a document keyed by its path, from a `MultiStreamProjection` over the video file streams whose every event writes the whole document.
  A page reads the library, the detection and UI settings, the players and the notifiers by folding their aggregate's stream, and a video file by folding the file's stream, which stores nothing and leaves nothing to rebuild.
  A root folder is the library's, with the newest library scan summary that lists it since it was added.
  Each read model's file holds its type, its projection and its query, such as `MediaRow.cs` with `MediaRow`, `MediaRowProjection` and `MediaRowQuery`.
  A read model's type ends in `Row` only when a page lists it as the rows of a table, such as `MediaRow`, and is otherwise named for what it holds, such as `StoredFilePath` or `HistoryClear`.
  The detection queue is a query on the Media documents.
  Read models are shared by the pages and processes that read the same facts, and each page shapes them with a query type of its own, following decision Q16; a reader that acts on one video file, such as the detection runner or playback, folds its stream.
- **Read model changed.** One store-wide session listener works out, from each commit's events, the event types each projection handles and the streams of the aggregates pages fold, which read models and which streams the commit changed.
  It publishes a `ReadModelChanged` to the activity feed after the commit.
  A page names the read models it reads, and reloads on their changes, so no page keeps its own list of events.
- **Page actions** run through `PageAction`, which holds an action's busy state, its last `Result`, its field errors and its form error, in place of each page's private fields.
  Each action a page shows apart has its own, such as *Save* beside the root folder actions on *Settings > Library*, and a modal's *Test*, *Save* and *Remove* share one.
- **Outside the event store:** host settings in `config.json`, log files, and runtime state (connection states, running detections, running work, health messages).

## Archiving

- Removing or disabling a root folder archives, at once, every video file it leaves with no path.
  A hardlink or copy under another root folder keeps its file live.
  A missing root folder, such as an unmounted share, is neither: its scan fails, and its paths stay.
- A removal runs in the background in batches under running work, since a root folder of 45,000 files takes about 18 s to archive.
  One removal runs at a time, and stopping the host cancels it.
  It runs inside a scan pause and a detection pause, so no library or folder scan and no detection writes to a file it archives, and only a file scan can meet its archive.
  A library scan removes the file paths under no enabled root folder, which only a removal a restart or a failure cut short leaves, and their files then follow the rule below.
  Until then a health message names each disabled or removed root folder that still holds file paths, and sends the operator to *Scan Now*.
- A file that a scan leaves with no path is archived at the end of the next library scan if it still has none, so a move between folders never archives it.
- An archive reads each file's Media row and appends `VideoFileArchived` at the row's version, so a path added since the read fails the commit and the retry keeps the file live.
  A file with no row is archived already.
  Every append states the version of what it decided from, so no commit is lost from a file's Media row and its version is its stream's.
- A projection removes a file's documents on `VideoFileArchived`, because a rebuild skips archived streams.
  The Media row goes with `ShouldDelete`, since after `DeleteEvent` a rebuild leaves a restored stream without its document.
  A file loses its paths before it is archived, so its file path documents are gone already, and a restore's `FilePathAdded` brings them back.
  A file's page folds its stream for its detections, live or archived, so every detection survives an archive, a rebuild and a restore.
- When a scan finds an archived hash, the handler unarchives the stream and commits, then appends `VideoFileRestored` carrying the file's state, and then `FilePathAdded`.
  Fisher refuses an append in the session that unarchives its stream, so a stream whose last event is `VideoFileArchived` is restored too, which completes a restore a crash cut short.
- A detection of an archived file is discarded: one that loads an archived file records nothing, and one whose append meets `ArchivedStreamException` loads the file again and finds it archived.
- A playback's link to an archived file opens its page, marked *Archived*.
- Nothing archived is deleted, and the database only grows.
  *Clear History* deletes nothing either: it records when the history was cleared, and the history's read models show only what came after.

## Folders

Decided 2026-09-30, following Wolverine's vertical slice guidance and the layout of JasperFx's CritterCrush sample.
The domain is split into chapters, one folder and namespace per capability, named so that no namespace hides a type of the same name:

| Chapter | Aggregates | Commands and processes |
|---|---|---|
| `Scanning/` | `Library` | Root folders, library settings, *Scan Now*, the library, folder and file scans, `FolderWatcher` |
| `Detecting/` | `VideoFile`, `DetectionSettings` | Recording detections, *Detect Now*, *Re-detect All*, overrides, a standard ratios change, `DetectionOrchestrator` |
| `Playing/` | `Players`, `Playback` | Saving and removing players, handling playback, *Clear History*, `PlayerConnectionService`, `PlaybackHandler` |
| `Notifying/` | `Notifiers` | Saving and removing notifiers, `NotificationPublisher` |
| `Appearance/` | `UISettings` | Saving the UI settings |

A chapter's files:

- `<Aggregate>.cs`: the aggregate root's state, a `Create` or `Apply` method per event, its entities, its value types, the static functions for rules more than one slice uses, and the static `ReadAsync` that folds its stream.
- `Events.cs`: every event the chapter's aggregates append, so the chapter's vocabulary reads on one screen.
  An input from outside the chapter keeps its own file.
- A slice file per command, named for it: the command record and a static class named for the command plus `Handler`, with pure static `Validate` and `Handle` methods.
  A slice may load an aggregate another chapter owns, such as a library scan appending to a video file.
- A read model file per read model: its type, its projection and its query.
- `<Chapter>Module.cs`: the chapter module, whose `Add<Chapter>` registers the chapter's services, processes, activity sources, health checks, folded aggregates and projections.
- A file per process, and one per integration and health check that serves the chapter, such as the ffmpeg runners and `DetectionHealthCheck` in `Detecting/`, the Kodi connection in `Playing/`, and the MQTT and webhook clients in `Notifying/`.

A value type more than one chapter uses lives in the chapter that owns its rules, such as `AspectRatio` and `StandardRatios` in `Detecting/`, and one no chapter owns, such as `FieldError`, in the folder of the function it serves.
The rest of the app has folders by function: `Activity/`, `Health/`, `Hosting/`, `Components/`, `Extensions/` and `EventStore/`.
`Activity/` holds the feed's own types, and each activity event lives in the folder of the code that raises it.
A document's type, such as `StoredFilePath`, lives in its read model's file with its projection and its query.

## Behaviour

These are the rules Debarr keeps, in the domain's words, whatever stores its data.
[core-principles.md](core-principles.md) says what Debarr does; this section says how exactly, rule by rule, and the tests pin each rule.
Every M8 task keeps every rule here.
Where M8 changes a rule on purpose, the rule says so and names the task.
A rule a task finds it can't keep goes to the operator as a decision before the task changes it.
The code holds the mechanism: tables, indexes, triggers, stream operators and type names.

### Library

- **Root folders.**
  A root folder is a full path to a folder that exists.
  No root folder is the same as, inside or contains another, enabled or not, compared by whole path segments on the canonical local path, and a refusal names the other root folder.
  Adding or enabling a root folder runs a library scan, as *Scan Now* does.
  Removing or disabling one returns once the library records it, and a root folder removal then drops its file paths in the background under running work and archives at once every video file it leaves with no path.
  A root folder removal runs inside a scan pause and a detection pause, so the library or folder scan it cancels runs again after it, the detections it cancels write nothing, and no scan or detection starts until the removal finishes or fails.
  A cancelled detection of a file that keeps a path under another enabled root folder runs again after it, and the files it archives leave the detection queue.
- **Video extensions** are saved normalised: lowercase, with no leading dot, split on spaces or commas, and with duplicates and empty entries dropped.
- **When a library scan runs.**
  At startup, every scan interval, on *Scan Now*, when a root folder is added or enabled, and when watching folders misses changes.
  The startup library scan catches up every scan a restart dropped, so no scan request is stored.
  It runs only when the library has an enabled root folder or a video file, since otherwise a scan has nothing to read, remove or archive.
  The scheduled scan's first run is one scan interval after startup, saving a new scan interval restarts that clock, and switching it off removes the schedule.
  *Scan Now* during a scan queues exactly one more library scan after it, and a scheduled scan can follow soon after a *Scan Now*.
- **Scans never overlap.**
  A library scan and a folder scan never run at once.
  A scan held back by another starts within about a second of that one ending, and a scan held back past its due time still runs.
- **What a scan does.**
  A library scan covers every enabled root folder, and a folder scan one folder.
  It hashes a file only when its path is new or its file stat changed, and a file it can't read counts as not found.
  It then removes the file paths it didn't find, except a path a file scan found while it ran.
  A root folder that is missing, such as an unmounted share, is skipped, keeps its file paths, and records the error.
  A library scan records on each root folder it covers when the scan started and its error, or no error, as that root folder finishes.
  A new path, a rename included, gets a new first-seen time.
- **Archiving at the end of a library scan.**
  A library scan ends by removing the file paths under no enabled root folder, then archives the video files those paths leave with none, and every video file whose last file path left before the scan started.
  A video file whose last path a scan removed from an enabled root folder is archived at the end of the next library scan, so a file moved between folders is found again first and never archived.
  A folder scan and a file scan never archive.
  A scan that hashes a file whose video file is archived restores it with its result, override and history, and the file's path gets a new first-seen time.
- **Library scan summary.**
  Every library scan stores when it started, and when it ends its counts and its library scan outcome: finished, failed with its error, or cancelled when Debarr shut down or a scan pause stopped it.
  A library scan the process stopped before its end, such as on a crash, stays open until startup ends it as interrupted, with no counts and no duration, since the scan held them in memory.
  Every summary is kept, and each outcome shows apart, so a cancelled or interrupted scan never shows as failed.
- **File scan.**
  A file scan takes one local path, runs at once beside any other scan and any detection, and never waits on them.
  It finds no video file for a path outside every enabled root folder, with an extension that isn't a video extension, or with no readable file, and a missing or unreadable file loses its file path.
  Playback runs one, and so does a detection that finds a path changed or unreadable.
- **Watch folders.**
  While watching folders is on, a created, changed, deleted or renamed file under an enabled root folder schedules a folder scan of its folder, and a rename schedules both folders.
  A folder is scanned once no change has arrived in it for 10 seconds, and activity in one folder never holds back another.
  A folder's own change of write time is ignored, since it follows the files in it.
  A missing root folder isn't watched.
  Watching follows each library settings save, and keeps its watchers while the root folders to watch are the same.
- **What a scan reports.**
  A library scan and a folder scan report when they start and finish, and a root folder removal when it starts and ends.
  The file paths a scan adds or removes and the video files it archives are reported from what committed, so each count is what was stored, whichever process stored it.
- **Stopping** the host cancels the running scan and waits for it to end.

### Video files and detection

- **Status.**
  A video file is *Manual* when its override has a ratio or *Don't Send*; otherwise *Detected* or *From File*, from its current result's source; otherwise *Failed* when it has a last failure; otherwise *Pending*.
  A video file with a current result and a later failure shows that failure beside its status.
- **The detection queue** is the pending video files that have at least one file path, newest first.
  A scan that adds a video file makes it pending, and that is the only link between scans and detection: a scan never waits on detection, and detection never starts a library or folder scan.
  An override, *Don't Send* included, never takes a file out of the queue, so its detection result is ready if the override is removed.
- **Starting detections.**
  A queue check runs at startup and then once a second, one at a time.
  It starts the newest pending video files that aren't running, one per free slot up to the simultaneous detections, so a file a scan or a playback just found is detected next, even behind a long backlog.
  A saved number of simultaneous detections applies from the next queue check.
  One detection runs per video file.
- ***Detect Now*** starts one detection at once, outside the simultaneous detections, whatever the file's status.
  It refuses a video file with no file path, during a detection pause, while another *Detect Now* runs, or while the file is being detected.
  While it runs, and on a file the queue is detecting, its controls are disabled.
  A restart abandons it and leaves the file as it was.
- **Running a detection.**
  A detection tries the video file's paths, most recently hashed first, and uses the first whose file stat matches and that opens; each path that fails gets a file scan.
  When no path is left, nothing is recorded, and the file leaves the queue.
  If the file changed while it was detected, the detection is discarded and its path gets a file scan, and new content found there joins the front of the queue.
- **Recording a detection.**
  Every finished detection records what started it, the path it read, when it started and how long it took, the detector and ffmpeg versions, the container metadata when ffprobe read it, and its detection result or detection failure.
  A detection result becomes the current result and clears the last failure.
  A detection failure, from an error or the timeout, becomes the last failure and leaves the current result in place, so only a detection result replaces the current result.
  A cancelled or discarded detection records nothing.
- **The detector.**
  A container ratio that doesn't snap to a standard ratio marked *Check Picture* is accepted as it is: a result from the file, with a confidence of 0.9.
  Otherwise cropdetect measures the picture at the sample count's sample points, evenly spaced across the runtime left after skipping the start and end, each one second long.
  The result's raw ratio is the mean ratio of the largest group of agreeing boxes, and its confidence is that group's share of the samples.
  A file with no duration, or with no sample that finds a picture, fails.
  The timeout covers ffprobe and every cropdetect run, and a detection that runs out fails with how many samples it read.
  A missing ffmpeg or ffprobe fails the detection with an error that says so.
- **Detector version.**
  Each detection records the detector version, which rises whenever the detector's output for a file can change.
  *Settings > Detection* shows how many current results an older version made, and *Re-detect All* replaces them.
- **Snap.**
  A raw ratio snaps to the nearest standard ratio within the match tolerance, and says which one it matched.
  A ratio exactly one tolerance away matches, a tie goes to the smaller standard ratio, and a ratio no standard ratio is near is rounded to two decimals.
  The detector's shortcut, the notification and every page snap the same way.
- **Standard ratios change.**
  Saving *Settings > Detection* with a change to the standard ratios or the match tolerance brings every detection result in line, in the same transaction as the save.
  A change to the match tolerance, or an added or removed standard ratio, re-checks every video file with a current result; a change to *Check Picture* alone re-checks the files whose current result's container ratio snaps to a standard ratio whose mark changed.
  A result from the file whose container ratio now checks the picture is cleared, so the file rejoins the queue, and its detection stays as history.
  A detected result whose container ratio no longer checks the picture is replaced by a new result from the file, started by *Standard Ratios Change*, from the stored container metadata and keeping the replaced detection's versions, without running ffmpeg.
  A cleared or replaced current result also clears the last failure, and overrides are kept.
  It runs inside a scan pause and a detection pause.
- ***Re-detect All*** runs inside a scan pause and a detection pause, clears every video file's current result and last failure, and keeps overrides and detection history, so the whole library rejoins the queue.
- **Scan pause.**
  A scan pause cancels the running library or folder scan, waits for it to end, and starts none until the pause ends; a scan that falls due meanwhile runs after it.
  The cancelled scan runs again once the pause ends, a library scan through its trigger and a folder scan rescheduled for its folder, so nothing is missed; each file it committed stays committed, and the rerun skips it by its stat.
  While a pause stops a library scan, the running work reads *Stopping the library scan*.
  A standard ratios change, *Re-detect All* and a root folder removal each run inside one, since they write to every video file they reach in one transaction, or archive them, and a scan's write to any of them would fail it on every attempt.
  A file scan runs during it and never waits.
- **Detection pause.**
  A pause cancels every running detection, waits for them to end, and starts none until it ends; the next queue check then starts queued ones.
  A standard ratios change, *Re-detect All* and a root folder removal each run inside one, and *Detect Now* is refused during it.
  One pause runs at a time.
- **Saving detection settings** refuses a value outside its bounds, an empty list of standard ratios, and a standard ratio listed twice, each beneath its field.
- **Saving an override** refuses a ratio of 0 or less beneath its field, stores a blank note as none, and records when it was saved.

### Players, playback and notifiers

- **Player and notifier names** are unique, and a clash is refused beneath the name field.
- **Player endpoints and notifier settings** are refused beneath their field when a value is outside its bounds, and a path mapping or a webhook header beneath its row, on *Test* and on *Save*.
  One *Test* or one *Save* shows every refusal at once, a name another player or notifier has included, whatever the endpoint or settings fields hold.
- **Player connections.**
  Each enabled player has one connection, opened at startup before the host accepts requests.
  Saving a player replaces only its connection, disabling or deleting one closes it, and a newer save cancels the one before.
  A player that can't be read stays closed until it is saved again.
  A connection pings the player every ping interval, bounds each request by the request timeout, and ends when the socket or a ping fails.
  It reconnects after 1 second, doubling to at most 60 seconds, and starts again at 1 second once it connects.
  Through its retries it stays Disconnected, with when it was lost, the latest error and when it retries; the first failure logs a warning, and each retry after it logs at Debug.
  It reports only playback that starts while it is connected.
  A player's *Test* opens a temporary connection with the unsaved values and reports the first state it settles in.
- **Playback.**
  A stream sends nothing and says so.
  For any other playback, Debarr canonicalises and translates the player path, then:
  1. Identify the video file with a file scan on the local path, so content replaced since the last scan never serves the old content's result, and a file moved or renamed since then keeps its result and override.
  2. When the file scan finds no video file, send the player-reported ratio.
  3. When the video file's override says *Don't Send*, send nothing.
  4. When the override has a ratio, send it as the operator set it, `source: manual`.
  5. When the video file has a current result, send its snapped ratio, `source: container` or `detected`, and record that detection on the playback.
  6. Otherwise send the snapped player-reported ratio, `source: player`.

  When the player reports no ratio, a step that sends it sends nothing.
  A player's newer playback cancels the handling of its older one, deliveries in flight included, so a stale notification never lands after a fresh one.
  A failure while handling a playback sends nothing, and its outcome says why.
- **Playback history.**
  Every playback is recorded with its outcome and the player's name as it was, and each delivery as its attempt ends, a cancelled one included.
  A player's newest playback is its last playback summary, and each notifier's recent deliveries come from the history, so both survive a restart.
  *Clear History* hides every playback and delivery so far at once, and keeps their events; task 49 changed it from *Purge History*, which deleted them.
- **Publishing a notification.**
  Each notification reads the enabled notifiers and starts one attempt per notifier at once, so one notifier never waits on another.
  Each attempt is tried once, within 5 seconds, and ends delivered, failed with the notifier's error or *Timed out after 5 s.*, or cancelled by a newer playback.
  A read of the notifiers that fails publishes to none.
  A notifier's *Test* sends a sample notification from player `debarr-test`, a detected 2.39 occurring now, with the unsaved values, and keeps it out of the history.

### The application

- **UI settings.**
  Every page follows the saved theme, date formats and time format, and a save re-renders every open page in every circuit.
  *Auto* follows the browser's light or dark preference, and dark stands in until the browser reports it.
  Dates show in the server's time zone with the invariant culture: the short date and the time, with the long date on hover, and seconds for a log entry.
  With relative dates on, a date today or yesterday reads *Today* or *Yesterday*, a date in the six days before reads its weekday, and any other date in the current year leaves out the year.
- **Live updates.**
  Every value a page shows updates in place while it is open.
  A page reloads at most once every 250 ms however many changes arrive, always sees the last change, and never renders a stale reload after a fresh one; a failure reaches the page's error boundary.
  Each page names the read models it reads and reloads when a commit changes one, and follows the runtime and host state it shows, such as running detections, through their activity events.
- **Activity messages.**
  Once a second, the activity of the second before becomes messages: one for each kind of event, so a burst of hashes or folder scans shows once, and only for what ended or was saved, since the running work shows what has started.
  Each message shows for 5 seconds.
- **Health checks.**
  Every check runs at startup and again when something it reads changes, at most once a second, and a newer change cancels a check in progress.
  A check that fails shows one error message that sends the operator to *System > Logs*.
  Messages list errors first, and messages of one severity in the order of their checks: root folders, ffmpeg, players, notifiers, then detection failures.
  A change to them updates Status and the navigation's badge.

## Constraints

These are the reasons behind choices any implementation keeps, which the code alone doesn't make obvious.

- **Hashing on a network share.**
  A scan hashes up to 4 files at a time, each in its own transaction: a cold read waits on the far disk, and 4 reads in flight gave about three times the throughput of one on the test NAS while leaving the share room to stream playback.
- **Scans that never overlap.**
  Quartz.NET's `[DisallowConcurrentExecution]` keeps apart two runs of one job only, so both scan jobs' triggers share an execution group whose limit is 1.
  A lock taken inside the jobs would hold a scheduler thread while it waits and fire the trigger before the scan it waits on, which puts misfire handling out of reach.
  Every trigger's misfire instruction fires it now, so a trigger a long scan held back still runs.
  Quartz frees the group's slot only once a finished job has woken the scheduler, so the scheduler's idle wait is 1 second, the lowest it accepts, where the default would hold a waiting scan for 24 to 30 seconds.
- **Debouncing and queueing scans.**
  A folder's scan is a trigger keyed by the folder's path, and each change replaces it, so a folder has at most one scan waiting.
  *Scan Now*'s scan is a one-shot trigger under one key, added only when none is waiting; one added while its scan runs replaces the running trigger and fires after that scan ends.
- **The detection queue.**
  Queue checks come from one loop on a timer, so they never overlap, and a check that finds nothing has to cost one indexed read, since it runs every second; task 38 found a query plan that took 26 ms a check.
  One lock guards the running list and the pause, a detection starts only once its entry is in the list, and it reports its start and end from its own task, in that order.
- **cropdetect.**
  - One ffmpeg run per sample point, since one run over every sample point reports the union of their pictures.
  - `-map 0:v:0`, since an MKV's default video stream can be attached cover art.
  - `scale=iw*sar:ih,setsar=1` before cropdetect, so an anamorphic source is measured in square pixels.
  - `round=2`, since the default of 16 can move a 1.85 crop to 1.78.
  - The limit sent as the black level divided by 255, a fraction below 1, so it follows each file's bit depth; the HDR black level replaces it for PQ and HLG files.
  - Boxes group when their width and height are within 4 pixels of the group's first box, and a tie goes to the group that formed first.
- **Snapping.**
  The distance to each standard ratio is rounded before it is compared, so floating-point error never stops a ratio exactly one tolerance away from matching, and equal distances tie.
- **SQLite.**
  The database is one file with one writer at a time, in WAL mode, and transactions begin immediately, so concurrent scans of one file write one at a time.
  A second connection that writes to the file while the process's own transaction holds the lock waits on that lock, which Fisher's documentation warns presents as a hang rather than an error, so every write inside a Fisher transaction goes through Fisher's connection.
- **Appending at a known version.**
  Fisher folds a commit's inline projections before it takes the write lock, and checks under the lock only a version the append states, so an append that states none commits over a commit that landed in between, and the Media row it folded loses that commit.
  Every append therefore states the version of the facts its decision read: an aggregate's through its load, and a command over many video files through the version of each Media row it decided from, which a row always matches once every append states one.
  Such a command commits or fails whole, and decides again on a fresh read when any of its files changed since it read them.
- **Delivering before recording.**
  A playback's notification attempts start before its playback is stored, and each delivery is stored as it ends, so the history adds nothing to the time from the player's event to delivery.
- **Kodi.**
  Kodi's JSON-RPC messages have no delimiter, so a message handler of Debarr's own reads them.
- **The activity feed.**
  Every stream that feeds it is hot, never ends, and reports a failure as an event, because a merged stream ends on the first error and would silence every page at once.
  The feed applies no scheduler, and each subscriber's callback returns quickly, since the source waits for it; [reactive-extensions.md](reactive-extensions.md) holds the rules.
- **Timestamps.**
  Every stored time is UTC as fixed-width ISO-8601 text with milliseconds, so text order is time order.
