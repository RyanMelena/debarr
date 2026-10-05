---
title: Debarr rewrite history
description: "The finished milestones M0 to M8, tasks 1 to 64, with what each task found; how the rewrite ported v2; the database schema as it was after M7; and how the code worked after M7, before M8 changed it. It describes the past, not the rules now."
size: "about 100 KB; read only the section a task needs"
read_when:
  - "A task needs to know why the code is the way it is, or what an earlier task found or measured, such as task 38's timings."
  - "A task ports from v2 or compares with it."
  - "A task or document names a section here, such as Activity feed or Schema through M7."
skip_when:
  - "The current rules are wanted: they are in core-principles.md, domain-model.md (Behaviour and Constraints), AGENTS.md, ui-conventions.md and reactive-extensions.md."
  - "Open tasks, decisions or names are wanted: they are in rewrite-plan.md."
sections:
  - "Porting v2: where the v2 repository is, how to read a file from it, and what was ported as code."
  - "Ported types: each type ported from v2, with its v3 name."
  - "Schema through M7: the EF Core schema's SQL before the event store, with the reason for each index."
  - "How the pieces worked after M7: the behaviour and mechanism before M8, one bold-titled paragraph group per piece: Video file status, Library scan, File scan, Scan triggers, Watch folders, Scans and detection, Detection queue, Detect Now, Running a detection, Detector, Detector version, Player connections, Playback, Publishing notifications, Snap, Standard ratios change, UI settings, Activity feed, Health checks."
  - "Milestones M0 to M8: one heading per milestone and a numbered item per task: M0 A new solution that boots (tasks 1 to 4), M1 Library (5 to 8), M2 Detection (9 to 12), M3 Playback and notification (13 to 17), M4 Review (18 to 22), M5 History and diagnostics (23 to 28), M6 Media and status (29 to 31), M7 UI and UX (32 to 41), M8 Domain model and event store (43 to 64)."
find:
  - 'Sections and milestones: grep -n "^##" docs/rewrite-history.md'
  - 'A piece, such as the activity feed: grep -n "^\*\*Activity feed\.\*\*" docs/rewrite-history.md'
  - 'A task, such as task 38: grep -n "^38\. " docs/rewrite-history.md'
---

# Debarr rewrite history

This is the finished part of [rewrite-plan.md](rewrite-plan.md): how the rewrite ported v2 and which types it ported, the schema as it was after M7, and milestones M0 to M8, tasks 1 to 64, with what each task found.
Task 42 moved to the plan's UI audit, now M10.
The code has moved on since, so a task here describes what was built then, not the rules now.
*Schema* is below as *Schema through M7*, and a section a task names, such as *Walk* or *Activity feed*, is below under *How the pieces worked after M7*.

## Porting v2

The v2 code is a separate repository checked out beside this one at `../debarr-v2`, tagged `v2-final`.
Read it with `git -C ../debarr-v2 show v2-final:<path>`, and its spec with `git -C ../debarr-v2 show v2-final:debarr-spec.md`.
The rewrite ports these as code, not as structure: the ffprobe and cropdetect runner, its parser and the sample aggregation; the Kodi JSON-RPC transport and message handler; the MQTT and webhook notifiers; path canonicalisation and path mappings; and the Radarr-style settings shell.

## Ported types

Each type a task ports from v2 is vetted against this plan's names before its task starts.
A row is settled when the operator confirms it; until then the v3 name is a proposal.

| v2 type | v3 type | Status | Reasoning |
|---|---|---|---|
| `AppOptions` | `AppOptions` | Settled | Binds `DEBARR__APP__`, which holds the data directory. |
| `ServerOptions` | `ServerOptions` | Settled | Binds `DEBARR__SERVER__`: bind address, port, URL base. |
| `ToolsOptions` | `FfmpegOptions` | Settled | It holds only the ffmpeg and ffprobe paths; "tools" names nothing. |
| `ConfigJsonFile` | `HostSettingsFile` | Settled | Named for what it holds, the host settings, not its file name. |
| `EnvironmentOverrides` | same | Settled | Finds the `DEBARR__` variable that sets a host setting key, so *Settings > General* marks that value. |
| `MediaDbContext` | `DebarrDbContext` | Settled | "Media" is not a concept in this plan. |
| `ChangeFeed`, `ChangeSubscription`, `ChangeTopic` | none | Proposed | Replaced by the activity feed. Pages filter events themselves, and System.Reactive's `Sample` replaces the hand-written merging. |
| — | `ActivityFeed`, `ActivityEvent`, `IActivitySource` | Proposed | New. Named from *Activity feed*, *Activity event* and *Activity source*. |
| `LiveComponentBase` | same | Proposed | The live component base: it owns a page's activity feed subscription. |
| — | `ActivityMessageArea` | Proposed | New. The area at the bottom of the navigation that shows activity messages. |
| — | `HostSettingsSavedEvent` | Proposed | New. The activity event `HostSettingsFile` publishes when *Settings > General* saves. |
| — | `VideoFileEvent`, `FileHashedEvent`, `FilePathDeletedEvent` | Proposed | New. The base record of the activity events about one video file, and the two that `LibraryWalker` publishes: a file hashed, and a file path a file walk deleted. |
| — | `WalkStartedEvent`, `WalkFinishedEvent` | Proposed | New. The activity events `LibraryWalker` publishes from the walk jobs' job events when a full or folder walk starts and ends. |
| — | `JobEvent`, `JobStartedEvent`, `JobFinishedEvent` | Proposed | New. A Quartz.NET job's start and end, with its execution context and, for an end, the exception it threw. `ObserveJobs` emits them from a job listener for the jobs a matcher selects, and the walk events are mapped from them. |
| — | `LibrarySettingsService`, `LibrarySettingsSavedEvent` | Proposed | New. Saves *Settings > Library*, the library roots and the library settings, applies each change to the walk triggers, and publishes one event per save, which the watch follows. |
| — | `LibraryStartupService` | Proposed | New. The hosted service that adds the startup triggers, the scheduled walk and a full walk now. |
| — | `VideoFilesPurgedEvent` | Proposed | New. The activity event `LibraryWalker` publishes when a purge deletes video files, from a full walk or *Purge now*. |
| — | `LibrarySettingsForm` | Proposed | New. The library settings as *Settings > Library* edits them, with an interval of 0 for a schedule switched off, as Radarr writes a switched-off interval. |
| `GeneralSettingsForm` | same | Proposed | The host settings as *Settings > General* edits them. |
| — | `SnappedAspectRatio` | Proposed | New. What `Snap` returns: the raw ratio, the snap-list ratio it matched or none, and the snapped ratio derived from those two. |
| `FfprobeProber` | `FfprobeRunner` | Proposed | Pairs with `CropDetectRunner`; "prober" repeats "ffprobe". |
| `CropDetectRunner`, `CropDetectParser` | same | Proposed | Each names the one ffmpeg filter it runs or parses. |
| `FfmpegVersion` | same | Proposed | Supplies `ffmpeg_version`. |
| `ProbeResult` | `ContainerMetadata` | Proposed | What ffprobe reports is the plan's *container metadata*. The parser turns the sample aspect ratio into a number, so no ffprobe text travels past it. |
| `CropSample`, `CropBox` | same | Proposed | One cropdetect run at one sample point, and the picture area it found. The aggregation becomes an extension method on the samples. |
| `DetectionPolicy` | none | Proposed | The detector takes `DetectionSettings` and the snap list as they are stored. |
| `DetectionException` | none | Proposed | The runners return a `Result`, and its errors become the detection's failure. |
| `AspectRatioDetector` | same | Proposed | Performs one detection. |
| — | `IAspectRatioDetector` | Proposed | New. The detector's interface, so the orchestrator's tests run a double in its place. |
| — | `DetectionRequest` | Proposed | New. Named from *Detection request*. |
| — | `DetectionRunner` | Proposed | New. Named from *Detection runner*. It replaces the `AspectRatioDetectionJob` and `DetectionSlotAllocator` that task 11 added, when the orchestrator stopped running detections as Quartz.NET jobs. |
| — | `DetectionOrigin` | Proposed | New. Which of the detection queue and *Detect now* started a running detection, and for a stored detection also the snap re-check. |
| — | `DetectionStartedEvent`, `DetectionFinishedEvent` | Proposed | New. The activity events `DetectionOrchestrator` publishes from a private subject as each detection's task starts and ends. |
| — | `DetectionSettingsService`, `DetectionSettingsSavedEvent`, `DetectionResultsClearedEvent` | Proposed | New. Saves *Settings > Detection*, the detection settings and the snap list, with the snap re-check a change needs, and runs *Re-detect all*. It publishes one event per save and one per *Re-detect all*, with the count it cleared. |
| — | `DetectionSettingsForm`, `SnapAspectRatioForm` | Proposed | New. The detection settings and one snap-list ratio as *Settings > Detection* edits them. |
| `DetectionResult` | `Result<AspectRatioDetectionResult>` | Settled | FluentResults' `Result<T>` replaces the union. A failure is its error message, and the value is an *aspect ratio detection result*. The detector returns it inside a `DetectionOutcome`. |
| — | `DetectionOutcome` | Proposed | New. Named from *Detection outcome*. |
| `KodiPathCanonicalizer` | none | Proposed | `ToKodiPlayerPath`, an extension method on the reported path's string, applies Kodi's canonicalisation rules. |
| `PlayerPath`, `LocalPath` | same | Proposed | Named from *Player path* and *Local path*. |
| `IPath`, `PrefixRule`, `PathSeparators` | none | Proposed | They let arr paths and player paths share one matcher. Player paths are the only paths that translate, so `ToLocalPath`, an extension method on `PlayerPath`, takes the path mappings as stored. |
| `IPlayer`, `KodiPlayer` | `IPlayerConnection`, `KodiPlayerConnection` | Proposed | Named from *Player connection*: the type is Debarr's link to a player, not the player. |
| `KodiMessageHandler` | same | Proposed | Frames Kodi's undelimited JSON-RPC. |
| `PlayerHealth` | `PlayerConnectionState` | Proposed | It describes the connection, so it takes the connection's name. |
| `KodiPlayingItem`, `PlayerEvent`, `PlaybackState` | none | Proposed | The connection reads the playing item itself and emits a `PlaybackStartedEvent`, so no Kodi type leaves it. Nothing in this plan follows a stop. |
| `KodiRequestException` | same, nested in `KodiPlayerConnection` | Proposed | It describes a failed Kodi request for the connection's disconnected state, and nothing outside the connection sees it. |
| — | `PlayerConnectionEvent`, `PlayerConnectionStateChangedEvent` | Proposed | New. The base record of the events a player connection emits, and the one it emits each time it enters a state. `PlaybackStartedEvent` derives from the base too, and `PlayerConnectionService` passes only the state changes to the activity feed. |
| — | `PlayerConnectionFactory` | Proposed | New. Creates the player connection for a player definition's type, so `PlayerConnectionService` holds no player type's dependencies. |
| `PlayerSettingsService` | same | Proposed | Saves and deletes player definitions for *Settings > Players*, and publishes a player definition event for each committed save and delete. |
| — | `PlayerDefinitionEvent`, `PlayerDefinitionSavedEvent`, `PlayerDefinitionDeletedEvent` | Proposed | New. The base record of the activity events about one player definition, which carries its id, and the two that `PlayerSettingsService` publishes when a save or a delete commits. Like every activity event they carry the id and no stored type, and a subscriber reads the definition from the database. |
| `NotifierSettingsService` | same | Proposed | Saves and deletes notifier definitions for *Settings > Notifiers*. The publisher reads the enabled notifiers for each notification, so nothing follows its changes but the page. |
| — | `NotifierSettingsSavedEvent` | Proposed | New. The activity event `NotifierSettingsService` publishes for each save and delete. |
| — | `ConnectOnceAsync` | Proposed | New. An extension method on `IPlayerConnection` that opens one connection, returns the first state it settles in, connected or disconnected, and closes it. A player's Test calls it. |
| `KodiPlayerForm`, `KodiPathRuleRow` | `KodiPlayerForm`, `PlayerPathMappingForm` | Proposed | The Kodi player and one path mapping as the Kodi modal edits them. "Rule" becomes *Path mapping*. |
| `MqttNotifierForm`, `WebhookNotifierForm`, `WebhookHeaderRow` | `MqttNotifierForm`, `WebhookNotifierForm`, `WebhookHeaderForm` | Proposed | Each notifier, and one webhook header, as its modal edits them. |
| `KodiPlayerModal`, `MqttNotifierModal`, `WebhookNotifierModal` | same | Proposed | The hand-written modal for each integration type. |
| `PlayerSupervisor` | `PlayerConnectionService` | Proposed | A hosted service that opens, reconnects and closes player connections. "Supervisor" is a term nothing else in this plan uses, and it read as starting players. |
| `MqttNotifier`, `WebhookNotifier` | same | Proposed | Named from *Notifier*. |
| `NotificationDispatcher` | `NotificationPublisher` | Proposed | This plan says Debarr *publishes* a notification. Its `SendAsync` starts sending one notification to every enabled notifier at once and returns each attempt as a task that ends with its delivery, and `PlaybackHandler` stores them. |
| `INotifier` | same | Proposed | Named from *Notifier*. `SendAsync` returns a `Result`, and the publisher owns the timeout. |
| — | `NotifierFactory` | Proposed | New. Creates the notifier for a notifier definition's type, so `NotificationPublisher` holds no notifier type's dependencies. |
| `PlaybackNotification` | `Notification` | Proposed | Named from *Notification*, with the four-field payload. |
| — | `NotificationAspectRatioSource` | Proposed | New. The notification's `source`: `manual`, `detected`, `container` or `player`. `AspectRatioSource` holds only the two a detection result stores. |
| `DeliveryResult` | `Delivery` | Proposed | Named from *Delivery*, and it carries the notifier's id and name. It is stored with its playback. |
| — | `DeliveryOutcome` | Proposed | New. How a delivery attempt ended: succeeded, failed or cancelled. |
| `MqttBrokerAddress` | same | Proposed | The host, port and TLS flag an MQTT notifier's URL resolves to: `mqtt://` or `mqtts://`, a host and an optional port, with 1883 or 8883 by default. `Parse` reads it with `Uri` and returns a `Result`, since the URL is the operator's input. |
| `MqttSettings`, `WebhookSettings`, `NotifierKind` | none | Proposed | The notifiers take their stored definitions, whose type says which kind each is. |
| `PlaybackHandler` | same | Proposed | Rebuilt rather than ported: v2's handler identified items through the *arrs, and v3 identifies a file by its path alone. A hosted service that handles each playback started event in one async method: it chooses the ratio to send, stores the playback while its notification is published, and stores each delivery. It also runs *Purge history*. |
| — | `NotSentReason` | Proposed | New. Why a playback sent nothing: a stream, "don't send", no player ratio, or a failure, whose error the playback stores. A playback that sent a ratio stores the ratio and its source instead. A player ratio sent for a path outside the library has no video file id. |
| — | `PlaybackHandledEvent`, `DeliveryFinishedEvent`, `HistoryPurgedEvent` | Proposed | New. The activity events `PlaybackHandler` publishes from its private subject as each write commits: one for each stored playback, with the player and the playback ids, one for each stored delivery, a cancelled one included, with the notifier id, and one for each *Purge history*, with the count. |
| — | `Detection` | Proposed | New. Named from *Detection*: one stored row per finished detection. |
| — | `FullWalk` | Proposed | New. The stored full walk summary, named for its table. It replaces `FullWalkSummary` in `Models/`. |
| — | `Playback` | Proposed | New. Named from *Playback*. |
| — | `UISettings` | Proposed | New. Named from *UI settings*, with the two-letter acronym capitalised as .NET names it. |
| — | `UITheme` | Proposed | New. The theme *UI settings* holds: `Auto`, `Light` or `Dark`. |
| — | `UISettingsService`, `UISettingsSavedEvent` | Proposed | New. Saves *Settings > UI* and publishes one event per save. |
| — | `UISettingsForm` | Proposed | New. The UI settings as *Settings > UI* edits them. |
| — | `UISettingsProvider` | Proposed | New. Reads the UI settings, and again after each save, and cascades the theme to the layout and a `DateTimeFormatter` to every page. |
| — | `DateTimeFormatter` | Proposed | New. The one formatter for dates and times, built from the UI settings. |
| — | `OverrideService`, `OverrideSavedEvent` | Proposed | New. Saves a video file's override for the video file detail page, sets `override_updated_at`, and publishes one event per save, which carries the video file id. |
| — | `OverrideForm` | Proposed | New. The override as the video file detail page edits it. |
| — | `NotifierTypeModal` | Proposed | New. The modal *Add Notifier* opens, with a tile for each notifier type that says what it does. |
| — | `ShowAdvanced`, `ShowAdvancedButton` | Proposed | New. Whether settings pages and modals show the settings an operator rarely changes, which the layout holds for the circuit and cascades to every page and modal, and the button that toggles it. |

## Schema through M7

The database as M0 to M7 built it, which M8 replaces with the event store.
The comments record why each index exists; most came from task 38's timings on the large library.

Every `*_at` column holds UTC as fixed-width ISO-8601 text (`2026-09-24T19:04:11.482Z`), so text order is time order.
`modified_at` is the exception: unix milliseconds, as the filesystem reports it.

```sql
CREATE TABLE library_root (
  id             INTEGER PRIMARY KEY,
  path           TEXT NOT NULL UNIQUE,       -- canonical local path, no trailing separator
  enabled        INTEGER NOT NULL DEFAULT 1,
  last_walked_at TEXT,                       -- the last full walk, or folder walk of the whole root, that covered it
  walk_error     TEXT                        -- that walk's error, such as a missing folder; NULL when it read the root
);

CREATE TABLE video_file (
  id                      INTEGER PRIMARY KEY,
  file_hash               TEXT NOT NULL UNIQUE,
  size                    INTEGER NOT NULL,  -- bytes; also hashed into file_hash
  first_seen_at           TEXT NOT NULL,
  last_seen_at            TEXT NOT NULL,     -- updated by every walk that finds one of its file paths
  detection_result_id     INTEGER REFERENCES detection(id) ON DELETE SET NULL,  -- the detection whose result is served
  failed_detection_id     INTEGER REFERENCES detection(id) ON DELETE SET NULL,  -- the last failure; kept beside a result when Detect now fails
  -- override:
  override_aspect_ratio   REAL,
  suppress_notification   INTEGER NOT NULL DEFAULT 0,
  override_note           TEXT,
  override_updated_at     TEXT               -- when the override was last saved
);
-- Orders the detection queue. The queue query also requires a file path, through ix_file_path_video_file.
CREATE INDEX ix_video_file_queue ON video_file(id DESC) WHERE detection_result_id IS NULL AND failed_detection_id IS NULL;
-- ON DELETE SET NULL looks up the video files that point at each detection a purge deletes.
-- Partial, so a query for the video files with no result or no failure uses ix_video_file_queue.
CREATE INDEX ix_video_file_detection_result_id ON video_file(detection_result_id) WHERE detection_result_id IS NOT NULL;
CREATE INDEX ix_video_file_failed_detection_id ON video_file(failed_detection_id) WHERE failed_detection_id IS NOT NULL;

-- One row per finished detection, success or failure. A row never changes once written.
CREATE TABLE detection (
  id                      INTEGER PRIMARY KEY,
  video_file_id           INTEGER NOT NULL REFERENCES video_file(id) ON DELETE CASCADE,
  origin                  TEXT NOT NULL CHECK (origin IN ('queue', 'detect_now', 'snap_recheck')),
  path                    TEXT,              -- the file path it read; NULL for snap_recheck
  started_at              TEXT NOT NULL,     -- for snap_recheck, when the re-check ran
  duration_ms             INTEGER NOT NULL,  -- 0 for snap_recheck
  detector_version        INTEGER NOT NULL,
  ffmpeg_version          TEXT,
  -- container metadata, when ffprobe read it:
  container_aspect_ratio  REAL,
  width                   INTEGER,
  height                  INTEGER,
  codec_name              TEXT,
  color_transfer          TEXT,
  -- detection result, when it succeeded:
  raw_aspect_ratio        REAL,
  aspect_ratio_source     TEXT,              -- container | detected
  confidence              REAL,
  samples_json            TEXT,
  -- failure:
  error                   TEXT,
  CHECK ((raw_aspect_ratio IS NULL) <> (error IS NULL))
);
CREATE INDEX ix_detection_video_file ON detection(video_file_id, id DESC);
-- Tell Media's statuses apart and count the results an older detector made, reading the index instead of the rows and their samples.
CREATE INDEX ix_detection_aspect_ratio_source ON detection(aspect_ratio_source);
CREATE INDEX ix_detection_detector_version ON detection(detector_version);

CREATE TABLE file_path (
  path          TEXT PRIMARY KEY,          -- local path
  video_file_id INTEGER NOT NULL REFERENCES video_file(id) ON DELETE CASCADE,
  modified_at   INTEGER NOT NULL,          -- unix ms, from the stat when last hashed
  first_seen_at TEXT NOT NULL,
  last_seen_at  TEXT NOT NULL
);
CREATE INDEX ix_file_path_video_file ON file_path(video_file_id);

-- One row per full walk, all kept.
CREATE TABLE full_walk (
  id                  INTEGER PRIMARY KEY,
  started_at          TEXT NOT NULL,
  duration_ms         INTEGER NOT NULL,
  file_paths_found    INTEGER NOT NULL,
  files_hashed        INTEGER NOT NULL,
  video_files_added   INTEGER NOT NULL,
  file_paths_deleted  INTEGER NOT NULL,
  video_files_purged  INTEGER NOT NULL,
  error               TEXT
);

-- The history: one row per playback started event, kept until Purge history.
CREATE TABLE playback (
  id                    INTEGER PRIMARY KEY,
  occurred_at           TEXT NOT NULL,     -- when Debarr received the event; the notification's occurred_at
  player_definition_id  INTEGER REFERENCES player_definition(id) ON DELETE SET NULL,
  player_name           TEXT NOT NULL,     -- as it was at playback
  title                 TEXT,              -- as the player built it
  player_path           TEXT NOT NULL,
  local_path            TEXT,              -- NULL for a stream
  video_file_id         INTEGER REFERENCES video_file(id) ON DELETE SET NULL,
  detection_id          INTEGER REFERENCES detection(id) ON DELETE SET NULL,  -- the detection whose result was sent
  player_aspect_ratio   REAL,
  sent_aspect_ratio     REAL,              -- NULL when nothing was sent
  source                TEXT,              -- manual | detected | container | player; NULL when nothing was sent
  not_sent_reason       TEXT,              -- stream | suppressed | no_player_aspect_ratio | failed
  error                 TEXT               -- the failure, when not_sent_reason is failed
);
CREATE INDEX ix_playback_player ON playback(player_definition_id, id DESC);
CREATE INDEX ix_playback_video_file ON playback(video_file_id, id DESC);
CREATE INDEX ix_playback_detection_id ON playback(detection_id);
-- History's player list, player filter and Sent and Not Sent counts.
CREATE INDEX ix_playback_player_name ON playback(player_name);
CREATE INDEX ix_playback_sent_aspect_ratio ON playback(sent_aspect_ratio);

CREATE TABLE delivery (
  id                      INTEGER PRIMARY KEY,
  playback_id             INTEGER NOT NULL REFERENCES playback(id) ON DELETE CASCADE,
  notifier_definition_id  INTEGER REFERENCES notifier_definition(id) ON DELETE SET NULL,
  notifier_name           TEXT NOT NULL,   -- as it was at delivery
  started_at              TEXT NOT NULL,
  duration_ms             INTEGER NOT NULL,
  outcome                 TEXT NOT NULL CHECK (outcome IN ('succeeded', 'failed', 'cancelled')),
  error                   TEXT
);
CREATE INDEX ix_delivery_playback ON delivery(playback_id);
CREATE INDEX ix_delivery_notifier ON delivery(notifier_definition_id, id DESC);
-- History's Delivery Failed filter.
CREATE INDEX ix_delivery_failed ON delivery(playback_id) WHERE outcome = 'failed';

-- Settings: one singleton per settings page, plus the snap list's rows. InitialCreate seeds every singleton
-- and the snap list through HasData. Every numeric setting has a CHECK, and the UI validates the same bounds.
-- Integrations: a base table per kind holds the shared columns, and a typed table per integration type shares
-- its id, mapped as EF Core table-per-type.
CREATE TABLE library_settings (
  id                  INTEGER PRIMARY KEY CHECK (id = 1),
  video_extensions    TEXT NOT NULL DEFAULT 'mkv mp4 m4v avi mov ts m2ts wmv webm mpg mpeg',
  walk_interval_hours INTEGER DEFAULT 12 CHECK (walk_interval_hours >= 1),  -- NULL = off
  watch_enabled       INTEGER NOT NULL DEFAULT 1,
  retention_days      INTEGER NOT NULL DEFAULT 30 CHECK (retention_days >= 0)
);

CREATE TABLE detection_settings (
  id                      INTEGER PRIMARY KEY CHECK (id = 1),
  concurrency             INTEGER NOT NULL DEFAULT 2 CHECK (concurrency >= 1),               -- spindles, not cores
  sample_count            INTEGER NOT NULL DEFAULT 12 CHECK (sample_count >= 1),
  edge_skip_percent       INTEGER NOT NULL DEFAULT 5 CHECK (edge_skip_percent BETWEEN 0 AND 45),  -- runtime skipped at each end
  timeout_seconds         INTEGER NOT NULL DEFAULT 900 CHECK (timeout_seconds >= 1),         -- per detection
  limit_sdr               INTEGER NOT NULL DEFAULT 24 CHECK (limit_sdr BETWEEN 0 AND 254),   -- cropdetect black threshold, 8-bit 0-255 scale, sent as limit/255
  limit_hdr               INTEGER CHECK (limit_hdr BETWEEN 0 AND 254),                       -- PQ and HLG files; NULL uses limit_sdr
  snap_tolerance          REAL NOT NULL DEFAULT 0.04 CHECK (snap_tolerance >= 0.01)
);

-- Each format is one of the formats Settings > UI offers.
CREATE TABLE ui_settings (
  id                  INTEGER PRIMARY KEY CHECK (id = 1),
  theme               TEXT NOT NULL DEFAULT 'auto' CHECK (theme IN ('auto', 'light', 'dark')),
  short_date_format   TEXT NOT NULL DEFAULT 'MMM d yyyy',
  long_date_format    TEXT NOT NULL DEFAULT 'dddd, MMMM d yyyy',
  time_format         TEXT NOT NULL DEFAULT 'HH:mm',
  show_relative_dates INTEGER NOT NULL DEFAULT 1
);

-- Seeded with 1.33, 1.66, 1.78, 1.85, 2.00, 2.20, 2.35 and 2.39; 1.33 and 1.78 are ambiguous.
-- The UI lists the ratios in ascending order.
CREATE TABLE snap_aspect_ratio (
  id           INTEGER PRIMARY KEY,
  aspect_ratio REAL NOT NULL UNIQUE CHECK (aspect_ratio > 0),
  is_ambiguous INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE player_definition (
  id      INTEGER PRIMARY KEY,
  name    TEXT NOT NULL UNIQUE,
  enabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE kodi_player_definition (
  id                      INTEGER PRIMARY KEY REFERENCES player_definition(id) ON DELETE CASCADE,
  host                    TEXT NOT NULL,
  port                    INTEGER NOT NULL DEFAULT 9090 CHECK (port BETWEEN 1 AND 65535),
  ping_interval_seconds   INTEGER NOT NULL DEFAULT 60 CHECK (ping_interval_seconds >= 1),
  request_timeout_seconds INTEGER NOT NULL DEFAULT 10 CHECK (request_timeout_seconds >= 1)
);

CREATE TABLE player_path_mapping (
  id                   INTEGER PRIMARY KEY,
  player_definition_id INTEGER NOT NULL REFERENCES player_definition(id) ON DELETE CASCADE,
  player_prefix        TEXT NOT NULL,
  local_prefix         TEXT NOT NULL,
  UNIQUE (player_definition_id, player_prefix)
);

CREATE TABLE notifier_definition (
  id      INTEGER PRIMARY KEY,
  name    TEXT NOT NULL UNIQUE,
  enabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE mqtt_notifier_definition (
  id             INTEGER PRIMARY KEY REFERENCES notifier_definition(id) ON DELETE CASCADE,
  url            TEXT NOT NULL,                     -- mqtt:// or mqtts://, a host and an optional port; mqtts turns TLS on
  client_id      TEXT,                              -- NULL lets MQTTnet generate one
  username       TEXT,
  password       TEXT,                              -- sensitive; masked in the modal
  topic_template TEXT NOT NULL DEFAULT 'debarr/player/{player}/playback',  -- {player} is the only variable
  qos            INTEGER NOT NULL DEFAULT 1 CHECK (qos IN (0, 1, 2))
);

CREATE TABLE webhook_notifier_definition (
  id      INTEGER PRIMARY KEY REFERENCES notifier_definition(id) ON DELETE CASCADE,
  url     TEXT NOT NULL,
  method  TEXT NOT NULL DEFAULT 'POST' CHECK (method IN ('POST', 'PUT', 'PATCH')),
  headers TEXT                                      -- JSON object; sensitive, may include auth; values masked in the modal
);
```

## How the pieces worked after M7

The behaviour and mechanism of the code as M0 to M7 left it, before M8.
[domain-model.md](domain-model.md)'s *Behaviour* and *Constraints* hold the rules that stay, in the domain's words; the code holds the mechanism.

**Video file status.**
A video file's status comes from its override, its current result and its last failure, so nothing stores it:

| Status | Rule |
|---|---|
| Manual | `override_aspect_ratio` is set or `suppress_notification` is 1 |
| Detected or From File | `detection_result_id`, the current result, is set; which of the two comes from that detection's `aspect_ratio_source` |
| Failed | `failed_detection_id`, the last failure, is set |
| Pending | none of the above |

A video file with a current result and a later failure shows that failure's error beside its status.

**Library scan.**
For each file under an enabled root folder whose extension is one of the video extensions (`video_extensions`), stat it.
If the path is new, or its size differs from its video file's or its modification time changed, hash it, find or insert the video file for the hash, and upsert the file path.
A scan hashes up to 4 files at a time, each in its own transaction.
A cold read on a network share waits on the far disk, and 4 reads in flight gave about three times the throughput of one on the test NAS, while leaving the share room to stream playback.
Inserting a video file or a file path sets its `first_seen_at`.
Set `last_seen_at` on every file path and video file it finds.
Then delete the file paths whose `last_seen_at` is older than the scan's start, which are the ones it did not find, so a path a file scan added during the scan survives.
Then delete video files that have no file paths and whose `last_seen_at` is older than `retention_days`.
*Purge Now* deletes every video file with no file paths, and each purge that deletes video files publishes one activity event with the count.
Task 57 replaces this retention and purge with archiving, following decision Q8.
A library scan ends by storing its library scan summary in `full_walk`, whether it finished, failed or was cancelled.
A file the scan cannot read counts as not found.
A root folder that is missing, such as an unmounted share, is skipped and keeps its file paths.
A library scan, or a folder scan of a whole root folder, sets `last_walked_at` on each root folder it covers, and sets `walk_error` to its error, or to NULL when it read the root folder.
Library scans and folder scans never overlap, following *Scan triggers*.
*Settings > Library* saves the video extensions normalised: lowercase, with no leading dot, and with duplicates and empty entries dropped.
It refuses a root folder whose path is relative, names no folder, or overlaps another root folder, and names the other root folder.
A library scan and a folder scan publish an activity event when they start and finish, from Quartz's job events, and every scan publishes one for each file it hashes, which says whether the file path and the video file are new, and one for each file path a file scan removes.

**File scan.**
A file scan takes one local path under an enabled root folder with one of the video extensions.
It stats the path: a missing or unreadable file deletes its file path and publishes an activity event, and a new or changed one is hashed and upserted as in *Library scan*, then its `last_seen_at` is set.
It returns the path's video file, or none when the path is outside every enabled root folder, has another extension, or has no readable file.
A file scan runs at once, alongside any library or folder scan and any detection, and never waits on them.
Playback runs one, and so does a detection that finds a path changed or unreadable.

**Scan triggers.**
Quartz.NET runs library scans and folder scans as two jobs, `FullWalkJob` and `FolderWalkJob`, in its in-memory job store.
Library scans and folder scans never overlap.
Quartz's `[DisallowConcurrentExecution]` keeps apart two runs of one job only, so every trigger of both jobs is also in the `walk` execution group, and the scheduler's execution limit for that group is 1.
Quartz skips a trigger whose group is full and leaves it waiting in the store, so a trigger that comes due during a scan runs after the scan ends and no firing is lost.
A lock taken inside the jobs would hold a scheduler thread while it waits and fire the trigger before the scan it waits on, which puts misfire handling out of reach.
Every trigger's misfire instruction fires it now, so a trigger held back past the misfire threshold by a long scan still runs.
Quartz frees the group's slot only after a finished job has woken the scheduler, so without help a held trigger waits out the whole idle wait, 24 to 30 seconds by default.
The scheduler's `IdleWaitTime` is 1 second, the lowest Quartz accepts, so a held scan starts within a second of the one before it ending.
With the in-memory job store, waking once a second costs next to nothing.

`FullWalkJob` has two triggers.
The scheduled trigger repeats every scan interval (`walk_interval_hours`), starting one interval after startup.
The default is 12 hours, and switching the scan interval off (NULL) removes the schedule.
Saving a new scan interval replaces the trigger, so the next scheduled library scan runs one new interval after the save.
The immediate trigger is a one-shot that fires now, always under one trigger key.
Startup, *Scan Now* and a folder watcher error add it unless that key is already waiting, so *Scan Now* pressed during a scan queues one library scan after it.
A one-shot trigger stays in the store while its scan runs, so adding the key then replaces the running trigger, and the replacement fires after the scan ends.
The schedule runs on its own clock, so a scheduled library scan can follow soon after a *Scan Now*.
Stopping the host cancels the running scan and waits for it to end (`ShutdownJobInterruption.WhenWaitingForJobs` and `WaitForJobsToComplete`).

`FolderWalkJob` is one durable job.
Each of its triggers carries one folder and is keyed by that folder's path, so a folder has at most one trigger waiting.
Watching folders adds them, following *Watch folders*.
Adding or enabling a root folder adds a one-shot that fires now for the root folder.
Removing or disabling one removes the waiting folder triggers under it, deletes the file paths under it at once and publishes one activity event.
Their video files keep their data under *Library scan*'s retention rule, so a root folder enabled again within `retention_days` reuses its results, until task 57 archives them instead.

**Watch folders.**
When watching folders is on (`watch_enabled`), `LibraryWatcher` holds one `FileSystemWatcher` over each enabled root folder, its subfolders included.
It reads the enabled root folders at startup and after each library settings save, and keeps its watchers while the root folders it would watch are unchanged.
A root folder that is missing is not watched, and logs a warning; a read that fails keeps the watchers as they were.
A created, changed, deleted or renamed entry schedules a folder scan of the folder it is in, and a rename schedules a folder scan of both the old and the new folder.
A folder's own changed event is ignored, because its write time changes with the files in it and would schedule a scan of its parent.
Scheduling a folder scan replaces the folder's `FolderWalkJob` trigger with a one-shot that fires 10 seconds later, so a folder is scanned once no event has arrived in it for 10 seconds, and activity in one folder never holds back another.
A folder scan follows *Library scan* within that folder: it deletes only file paths under the folder and skips the retention purge.
A watcher error, such as a buffer overflow, adds `FullWalkJob`'s immediate trigger.
The scheduled library scan covers changes watching folders never sees, such as writes to a network share from another machine.

**Scans and detection.**
Scans decide which video files exist, and detection finds their aspect ratio.
A scan that inserts a video file leaves it pending, which puts it in the detection queue.
That is the only link: a scan never waits on detection, and detection never starts a library or folder scan.
An override, *Don't Send* included, never takes a file out of the queue: a pending file with an override is detected like any other, so its detection result is ready if the override is removed.

**Detection queue.**
`DetectionOrchestrator` is a hosted service that runs each detection as a task on the thread pool, through `DetectionRunner`.
It finds work by polling: a queue check runs at startup and then once a second on a `PeriodicTimer` over the injected `TimeProvider`, and nothing tells it that a video file became pending.
The detection queue is a query on the partial index `ix_video_file_queue`, so a check that finds nothing costs one indexed read.
A video file a scan or a playback adds, and a slot a detection frees, are taken up by the next check, within a second.
The timer's loop is the only caller of a queue check, so checks run one at a time, and a tick that comes due during a check runs once the check ends.
A queue check reads the simultaneous detections (`concurrency`), counts the queue detections running, and starts the newest files in the detection queue that are not in the running list, one for each free slot.
The newest id goes first, so a file that a scan or a playback just found is detected next, even behind a long backlog.
A saved number of simultaneous detections applies from the next queue check.
A standard ratios change and *Re-detect All* write inside a detection pause from the orchestrator, which cancels the running detections and starts none until the pause ends; the next queue check then starts queued ones.
*Re-detect All* clears every video file's current result (`detection_result_id`) and last failure (`failed_detection_id`) and keeps overrides and detections, so the whole library rejoins the queue and each file's detection history stays.
It cancels the running detections first, as a standard ratios change does.

The orchestrator holds the in-memory list of running detections, for the queue and for *Detect Now*, with each one's task and cancellation.
One lock guards the list and the paused flag, and every start checks the rules under it: nothing starts during a pause, one *Detect Now* runs at a time, and a video file in the list is never started again.
The queue query skips the video files in the list.
A detection's task starts only once its entry is in the list, and removes the entry when the detection ends, however it ends.
Each start and end publishes an activity event from the orchestrator's private subject, in that order, from the detection's own task.
Stopping the host ends the queue checks, cancels every running detection and waits for them to end.

**Detect Now.**
*Detect Now* asks `DetectionOrchestrator` to detect one video file at once, whatever its status.
The orchestrator starts it at once, outside the simultaneous detections, and refuses when the video file has no file path, during a detection pause, while another *Detect Now* runs, or while the video file is in the running list.
While it runs, and on a video file the queue is detecting, the *Detect Now* controls are disabled, following the activity feed.
A restart abandons it and leaves the video file as it was.

**Running a detection.**
A detection tries the video file's paths, most recently seen first.
It stats each one and uses the first whose size and modification time match the stored values and that it can open; each path that fails gets a file scan.
When no path is left, nothing is recorded, and a pending video file has left the queue.
On the chosen path it runs the detector and stats again.
If the size and modification time held, it records the detection.
Otherwise it discards the detection, recording nothing, and runs a file scan on that path, and a new video file found there joins the front of the queue as the newest id.

The detector returns a `DetectionOutcome`: the ffmpeg version and container metadata it read, and a `Result<AspectRatioDetectionResult>`, whose value is the detection result and whose errors are the detection failure.
Either way the detection writes one `detection` row, with what started it, the path it read, its start and duration, and the detector and ffmpeg versions, in one transaction with the pointer it moves.
A success stores its detection result on the row, makes it the current result (`detection_result_id`), and clears the last failure (`failed_detection_id`).
A failure, from a detector error or a timeout, stores its error messages on the row with any container metadata ffprobe read, makes it the last failure, and leaves the current result in place, so only a detection result replaces the current result.

**Detector.**
`AspectRatioDetector` reads the ffmpeg version, then runs ffprobe for the container metadata.
A container ratio that doesn't snap to a standard ratio marked *Check Picture* is accepted as it is: a result from the file, with a confidence of 0.9.
Otherwise it measures the picture with cropdetect at `sample_count` sample points, evenly spaced across the runtime left after skipping `edge_skip_percent` at each end, each centred in its share of that span.
Each sample point is one ffmpeg run over one second, since one run over every sample point would report the union of their pictures.
The run reads the first video stream (`-map 0:v:0`), since an MKV's default video stream can be attached cover art, and makes its pixels square with `scale=iw*sar:ih,setsar=1`, so an anamorphic source is measured in square pixels.
cropdetect runs with `round=2`, since its default of 16 can move a 1.85 crop to 1.78, and with its limit sent as the black level (SDR) divided by 255, a fraction below 1, so it follows each file's bit depth; a PQ or HLG file uses the black level (HDR) when one is set.
The samples' boxes fall into buckets whose width and height are within 4 pixels of the bucket's first box, and the largest bucket wins, the one that formed first on a tie.
The detection result's raw ratio is the mean ratio of that bucket's boxes, rounded to 4 decimals, and its confidence is the bucket's share of the samples.
A file with no duration, or with no sample that finds a picture, fails.
The timeout (`timeout_seconds`) covers ffprobe and every cropdetect run, and a detection that runs out fails with how many samples it read.
A missing ffmpeg or ffprobe fails the detection with an error that says so.

**Detector version.**
`AspectRatioDetector` holds its version as an integer constant, which a change raises whenever the detector's output for a file can change.
A detection result records it in `detector_version`.
*Settings > Detection* shows the current version and how many current results have an older one, and the operator runs *Re-detect All* when they want those results replaced.

**Player connections.**
`PlayerConnectionService` reads the enabled players at startup, before the host accepts requests, and opens a connection for each.
It follows each player's saved and deleted events: a save reads that player again and replaces its connection, a disabled or deleted player's connection closes, and a newer event cancels the read or the connection before it.
A read that fails logs an error and leaves that player's connection closed until it is saved again.
A Kodi connection opens a socket to the player, pings it every ping interval, and ends the session when the socket or a ping fails; each request is bounded by the request timeout.
It reconnects with a backoff that starts at 1 second and doubles to at most 60 seconds, and starts again at 1 second once it connects.
Through its retries it stays Disconnected, with when it was lost, the latest error and when it retries; the first failure logs a warning, and each retry that fails after it logs at Debug.
A playback started event comes from Kodi's `Player.OnPlay` for a video, once `Player.GetItem` has named the file.
A player's *Test* opens a temporary connection with the modal's unsaved values and reports the first state it settles in, connected or disconnected.

**Playback.**
The player's connection emits a player-neutral `PlaybackStartedEvent`: the player's name, the player path, the player-reported ratio and the title the player builds.
Kodi builds a movie's title as `title (year)` and an episode's as `showtitle S01E02 title`.
A stream sends nothing and says so.
For any other playback, Debarr canonicalises and translates the player path, then:

1. Identify the video file with a file scan on the local path.
   It hashes only a path that is unknown, or whose size differs from its video file's or whose modification time changed.
   So content replaced since the last scan never serves the old content's result, and a file moved or renamed since then keeps its result and override.
2. The file scan finds no video file, because the local path is outside every root folder, its extension is not one of the video extensions, or its file is missing or unreadable: send the player-reported ratio.
3. The video file's override says *Don't Send* (`suppress_notification`): send nothing.
4. The video file's override has a ratio: send it, `source: manual`.
5. The video file has a current result: send its snapped ratio, `source: container | detected`, and record that detection on the playback.
6. Otherwise send the player-reported ratio, `source: player`.

The override's ratio is sent as the operator set it, and the player-reported ratio and a current result's raw ratio are sent snapped.
When the player reports no ratio, a step that sends the player-reported ratio sends nothing.
A player connection reports only playback that starts while it is connected, so a playback already running when it connects sends nothing.
A player's playback started event cancels the handling of that player's event before it, deliveries in flight included, so a stale notification never lands after a fresh one.
Each step's result goes into the playback's outcome, and the player's newest playback is its last playback summary; a playback handled activity event tells the Status page it changed.
A failure while handling the event sends nothing, and the summary says why.

Every playback started event stores one `playback` row with its title, paths, outcome and the player's name, and each delivery stores one `delivery` row as its attempt ends.
A delivery that a newer playback cancels is stored as cancelled.
The rows are written beside the deliveries, never before them, so storing the history adds nothing to the time from the player's event to delivery.
Status reads each player's last playback summary from its newest playback and each notifier's recent deliveries from `delivery`, so both survive a restart.
*Purge History* deletes every playback and delivery in one transaction and publishes one activity event.

**Publishing notifications.**
Each notification reads the enabled notifiers and starts one attempt per notifier at once, so one notifier never waits on another.
Each attempt is tried once, within 5 seconds, and ends delivered, failed with the notifier's error or *Timed out after 5 s.*, or cancelled when a newer playback of the same player cancels it.
A notifier reports its failures as results, and an exception from one is logged as a defect and stored as a failure.
A read of the notifiers that fails logs an error and publishes to none.
A notifier's *Test* sends a sample notification from player `debarr-test`, a detected 2.39 occurring now, with the modal's unsaved values and the same timeout, and keeps it out of the history.

**Snap.**
`Snap` is an extension method on the standard ratios (today's `SnapAspectRatio` list) that takes a raw ratio and the match tolerance (`SnapTolerance`).
It returns the nearest standard ratio within the match tolerance, or the raw ratio rounded to two decimals when none is, and says which standard ratio matched.
A ratio exactly one match tolerance away matches, and a tie goes to the smaller standard ratio, whatever the list's order.
The detector's shortcut for a ratio from the file, the payload and every UI display call it.
That shortcut accepts the container ratio unless it snaps to a standard ratio marked *Check Picture* (`IsAmbiguous`).

**Standard ratios change.**
Saving *Settings > Detection* brings every detection result in line in the same transaction when the standard ratios or the match tolerance changed, and publishes one activity event.
A change to the match tolerance, or an added or removed standard ratio, re-checks every video file with a current result.
A change to *Check Picture* alone re-checks the video files whose current result has a `container_aspect_ratio` that snaps to a standard ratio whose mark changed.
For each video file it snaps its current result's `container_aspect_ratio` against the saved settings:

- A *From File* result (`container`) whose container ratio now checks the picture is cleared: `detection_result_id` becomes NULL, so the video file rejoins the detection queue, and its detection stays as history.
- A *Detected* result (`detected`) whose container ratio no longer checks the picture is replaced by a new *From File* detection started by *Standard Ratios Change* (`snap_recheck`), as the detector's shortcut writes it from the stored container metadata, and that detection becomes the current result.
  ffmpeg does not run, and the new detection keeps the detector and ffmpeg versions of the one it replaces.

Overrides are kept.
A cleared or replaced current result also clears the last failure, as a new detection result does, and a video file the change leaves alone keeps its last failure.
A standard ratios change cancels every running detection before it runs, through its cancellation token, and a cancelled detection records nothing.
The next queue check after the change is saved starts queued detections.
A pending video file rejoins the detection queue, and *Detect Now* leaves its video file as it was, as a restart does.
Either way the change then applies to it like any other video file.

**UI settings.**
`UISettingsProvider` wraps the router, so every layout and page sits inside it.
It reads `ui_settings` when each circuit starts and again on each save's activity event, and cascades the theme and a new `DateTimeFormatter`, so a save re-renders every open page's dates and the theme in every circuit.
`Auto` follows the browser's light or dark preference, and dark stands in until the browser reports it.
The formatter shows each moment in the server's time zone, which `TZ` sets in a container, with the invariant culture.
A date shows in the short date format and the time format, with the long date format in its tooltip, and a log entry's time shows seconds.
With relative dates on, a date today or yesterday reads Today or Yesterday, a date in the six days before reads its weekday, and any other date in the current year leaves out the year.

**Activity feed.**
Each type that originates activity events is an activity source, and builds its stream from the source of its events, following [docs/reactive-extensions.md](reactive-extensions.md).
`ActivityFeed` takes every registered `IActivitySource` and merges their streams with `Observable.Merge`, which delivers one event at a time, so the feed needs no `Synchronize` and nothing pushes into it.
The feed applies no scheduler: each subscriber filters, rate-limits and moves to its own context itself, and its callback returns quickly, since the activity source waits for it.

The live component base owns a page's subscription.
The page supplies a filter and a reload method, and the base subscribes with `Where(filter)`, `Sample(250 ms)`, and the reload as `Observable.FromAsync` through `InvokeAsync` behind `Switch`.
A burst of events causes at most one re-read per window, the last event is always seen, and a newer event cancels a reload still running, so a stale reload never renders after a fresh one.
The reload catches its exceptions and passes them to `DispatchExceptionAsync`, and so does the subscription's error handler, so a failure reaches the page's error boundary.
The sample runs on the app's `IScheduler`, `DefaultScheduler` wrapped with `Scheduler.Catch`, which logs any other exception.
Disposing the component disposes the subscription.
The activity message area gathers the feed into one batch a second with `Buffer(1 s)`, and turns each batch into messages: one message for each kind of event, so a burst of hashes or folder scans shows once, and only for what ended or was saved, since the running work shows what has started.
Each message shows for 5 seconds, and the area applies each change in order with `Concat`.

**Health checks.**
`HealthCheckService` runs every health check at startup, and each again when one of its triggers fires, at most once a second, with a newer trigger cancelling the run in progress.
Each check names the activity streams whose events can change its result, and runs apart from the others.
A check that throws logs the error and shows one error message that sends the operator to *System > Logs*.
The service holds every message, errors first, replays them to each new subscriber, and publishes a health changed activity event when they change.

## Milestones M0 to M8

### M0. A new solution that boots

1. **Create the solution.**
   `src/` holds `.dockerignore`, `Directory.Build.props`, `Directory.Packages.props`, `Dockerfile` and `global.json`, and no projects.
   Create `Debarr.sln`, `Debarr` and `Debarr.Tests` beside them, and keep the shared files; the Dockerfile is rewritten in task 43.
   Tests use xUnit v3 on Microsoft.Testing.Platform.
   `dotnet build` and bare `dotnet test` pass with one smoke test.
2. **Host configuration.**
   Port `AppOptions`, `ServerOptions`, `FfmpegOptions`, `HostSettingsFile`, `EnvironmentOverrides` and the source order, plus the data directory, Data Protection keys and `/healthz`.
   The source order is the options defaults, then `<datadir>/config.json`, then `DEBARR__` environment variables, and no other environment variables are read.
   The log level is the standard `Logging:LogLevel:Default` key, with no options class.
   Test that `config.json` overrides a default, that a `DEBARR__` variable overrides `config.json`, that an unprefixed variable is ignored, and that `EnvironmentOverrides` names the variable that sets a key.
3. **Database and activity feed.**
   Add `DebarrDbContext` with the schema above as `InitialCreate`, the WAL setup on startup, and `ActivityFeed` on System.Reactive, following *Activity feed* above.
   Test that concurrent publishers deliver one event at a time, that a publisher never waits on a slow subscriber, and that a sampled subscription fires once for a burst and sees its last event.
4. **UI shell.**
   Add the MudBlazor layout, Radarr-style navigation (Media, Settings, System), the live component base and the activity message area following *Activity feed* above, and a *Settings > General* page that reads and saves host settings, including the log level, and marks each value an environment variable sets.
   Test that a failing filter or reload reaches `DispatchExceptionAsync`, and that disposing a component ends its subscription.
   Update the `debarr-live-check` skill for the new project paths and data directory.
   **Live check:** the app boots on an empty data directory, and the General page saves through a live circuit.

### M1. Library

5. **File hash.**
   Add `ComputeFileHashAsync`, an extension method on `FileInfo`, with the fixed chunk layout.
   Test that the hash is stable, that a changed byte in either sampled chunk changes it, and that files under 2 MiB hash whole.
6. **Walk.**
   Add `LibraryWalker`, following *Walk* above.
   Test against a temp directory: new, changed, renamed, hardlinked and deleted files, extension filtering, and retention purge with a fake clock.
   Add the file walk: test a new, changed, unchanged and missing file, and that a full walk keeps a path a file walk added while it ran.
   Test the activity events a walk publishes.
7. **Walk triggers.**
   Add `FullWalkJob`, `FolderWalkJob` and `LibraryWatcher`, following *Walk triggers* and *Watch* above.
   Pin Quartz.NET 4.1.1 or later, and add the `walk` limit and the 1-second idle wait where the scheduler is registered.
   A fake `TimeProvider` changes the fire times Quartz computes but does not wake its scheduler, so the tests come in two layers.
   With `FakeTimeProvider`, test the triggers' fire times: the first scheduled walk one interval after startup, a saved interval, a schedule switched off, and a folder trigger pushed back when its folder walk is scheduled again.
   With a running scheduler, a shortened quiet period and a shortened misfire threshold, test that full and folder walks never overlap, that a walk held back by another starts within about a second of that walk ending, that *Scan now* pressed twice during a walk queues one full walk, and that a trigger held back past the misfire threshold still runs, and that a folder trigger replaced while that folder's walk runs survives the walk's end.
   Test the quiet period, folder-scoped deletes, both folders of a rename, the full walk after a watcher error, the folder walk of a root that is added or enabled, and that removing or disabling a root deletes its file paths at once and keeps its video files until retention purges them.
8. **Settings > Library.**
   The roots list, the video extensions, the interval, the watch toggle, retention, *Scan now*, *Purge now*, and the last walk's time, duration and counts.
   Saving a root that overlaps another is refused with a message naming the other root.
   Test a root inside another, one containing another, a sibling sharing a name prefix (`/media/movies` beside `/media/movies2`), a trailing separator, and an overlap with a disabled root.
   **Live check:** add the real media roots and walk them.
   Record the path count and walk time, check that a rename in a scratch root keeps its video file, and watch the walk's activity messages come and go.

### M2. Detection

9. **Snap.**
   Add the snap function, following *Snap* above.
   Test the tolerance edges, a raw ratio that no snap-list ratio matches, and that the result names the matched ratio and its `IsAmbiguous`.
10. **Port the detector.**
    Port `FfprobeRunner`, `CropDetectRunner`, `CropDetectParser`, `FfmpegVersion` and `AspectRatioDetector` with its aggregation and the container shortcut, together with their tests.
    The container shortcut follows *Snap* above: test a container ratio within the tolerance of an ambiguous ratio, one just outside it, and one that snaps to a ratio that is not ambiguous.
    A tie between sample buckets goes to the bucket that formed first.
    `AspectRatioDetector` returns a `Result<AspectRatioDetectionResult>`.
    Its value holds the raw ratio and no snapped one, and a failure carries the error message.
    A sample point is a 1-second read at one of `sample_count` evenly spaced times across the runtime left after skipping `edge_skip_percent` at each end, each centred in its share of that span.
    These cropdetect requirements carry over from v2:
    - One ffmpeg run per sample point, since one concatenated run reports the union of every sample's content.
    - `-map 0:v:0`, since ffmpeg's default video stream can be an MKV's attached cover art.
    - `scale=iw*sar:ih,setsar=1` before cropdetect, so anamorphic sources are measured in square pixels.
    - `round=2`, since the default of 16 can move a 1.85 crop to 1.78.
    - `limit` sent as `limit_sdr / 255`, a fraction below 1, so it follows each file's bit depth.
    - `limit_hdr`, when set, replaces `limit_sdr` for PQ and HLG files.

    The detector stores the raw ratio only, and every reader snaps it following *Snap* above.
    Restore `tools/make-detection-fixtures.ps1` from `v2-final` for the fixtures the tests read.
11. **Detection orchestrator.**
    Add `DetectionOrchestrator`, `AspectRatioDetectionJob` and *Detect now*, following *Walks and detection*, *Detection queue*, *Detect now* and *Running a detection* above.
    Using a detector double and a running scheduler, test that the newest id goes first, that a run's end starts the next file, that a video file becoming pending starts a detection when a slot is free, that no more than `concurrency` queue detections run and a saved `concurrency` applies from the next queue check, that a job that throws leaves the running list, that the queue and *Detect now* skip a video file in the running list, fail once, that a failure keeps an existing detection result, falling back to the next path when one cannot be opened, a file walk on a path whose stored modification time is stale, a discarded result when the file changes during detection, and a video file with no file paths staying out of the queue.
    Test that *Detect now* runs while every queue slot is busy, that a second request while one runs is refused, and that it re-detects a video file that already has a detection result.
    Test that each start and end of a running detection publishes an activity event.
12. **Settings > Detection.**
    The detection settings, the snap list with an *Ambiguous* checkbox on each ratio, the tolerance, the detector version with the count of older detection results, and *Re-detect all*, which keeps overrides.
    A value outside its column's CHECK bounds is refused, such as a tolerance under 0.01.
    With no ratio marked ambiguous the page shows a warning that every container ratio will be accepted without cropdetect.
    Add the snap re-check, following *Snap re-check* above.
    Test that a tolerance change re-checks every video file with a detection result, that an `IsAmbiguous` change touches only the video files that snap to that ratio, both directions of a re-check, that overrides are kept and a cleared or converted result's failure is cleared, that clearing every `IsAmbiguous` turns every `detected` detection result into a `container` one, and that a detection running during the save is cancelled, writes nothing, and leaves a pending video file in the queue.
    **Live check:** detect a letterboxed remux and a cropped web rip from the real library, and compare against v2's results on the same files.

### M3. Playback and notification

13. **Paths.**
    Port `KodiPathCanonicalizer` as `ToKodiPlayerPath`, `PlayerPath`, `LocalPath` and longest-match translation by path mapping as `ToLocalPath`, with their tests.
14. **Kodi player connection.**
    Port `KodiPlayer` as `KodiPlayerConnection`, and `KodiMessageHandler`.
    `KodiPlayerConnection` implements `IPlayerConnection`, which emits `PlaybackStartedEvent` in player-neutral terms and reports a `PlayerConnectionState`.
    Port `PlayerSupervisor` as `PlayerConnectionService`, which reconnects only the saved player's connection on save, closes the connection of a disabled or deleted player definition, and opens one for an enabled one.
    Update the `exercise-kodi` skill for the new type names and paths.
15. **Notifiers.**
    Port `MqttNotifier`, `WebhookNotifier` and `NotificationPublisher`: one attempt, a 5 s timeout, and the last 20 deliveries kept in memory.
    The payload becomes `player`, `occurred_at`, `aspect_ratio` and `source`, pinned by a snapshot test, with three fractional digits in `occurred_at`.
    Restore `tools/webhook-sink.ps1` from `v2-final`; the `debarr-live-check` skill starts it.
    Replace the skill's example payload with the four-field one.
16. **Playback handler.**
    Add `PlaybackHandler`, following *Playback* above, which chooses the ratio each playback sends and publishes the playback handled activity event.
    Test every step of the decision list with an `IPlayerConnection` double and a notifier stub, that each step publishes the event with its summary, and that a playback during a walk and a running detection completes without waiting on either.
17. **Settings > Players and Settings > Notifiers.**
    Cards, one modal per type, and Test buttons, with the Kodi modal's path mappings.
    The Kodi modal saves each player prefix through `ToKodiPlayerPath`, so it matches the player paths it translates.
    The MQTT modal masks the password and the webhook modal masks header values, each with a toggle that reveals them.
    Test uses the modal's unsaved values through a temporary instance: a player connects and pings, and a notifier delivers a sample notification from player `debarr-test`.
    A player's Test creates a connection for the modal's unsaved definition through `PlayerConnectionFactory`, subscribes to its events until it reports connected or disconnected, and disposes the subscription.
    `PlayerSettingsService` saves and deletes player definitions, and publishes a `PlayerDefinitionSavedEvent` or a `PlayerDefinitionDeletedEvent` for each, carrying the player definition id.
    The same stream is its activity source, so *Settings > Players* reloads on it.
    It publishes from a private subject once the write commits, as the other settings services do, so a failed save publishes nothing.
    `PlayerConnectionService` follows that stream, and its `Refresh` method and subject are removed.
    A saved event reads that player definition, and a newer event for the same player cancels the read.
    A failed read logs and leaves that player's connection closed until its next save.
    It reads the ids of the enabled player definitions once at startup, before the host accepts requests, so no save can arrive during that read, and handles each as a saved event.
    Test that each save and each delete publishes one event, that a failed save publishes none, that a failed read leaves the player closed and a later save reconnects it, and that saving, disabling and deleting a player reconnects, closes and closes its connection through the stream.
    **Live check:** with the `exercise-kodi` and `debarr-live-check` skills, play a detected file, an undetected file, a file with an override, a suppressed file and a file outside the roots.
    Read each payload at the sink, and measure the time from `OnPlay` to delivery.

### M4. Review

Each task reviews every file in `Debarr` and `Debarr.Tests` for one concern and changes no behaviour.
`dotnet build` and bare `dotnet test` pass before and after, and a test changes only to follow a rename or a move.
A finding that needs a behaviour change, or a change to a rule in `AGENTS.md` or [docs/reactive-extensions.md](reactive-extensions.md), goes to the operator instead of into the commit.

18. **Rx and observables.**
    Check every `IObservable<T>` against [docs/reactive-extensions.md](reactive-extensions.md): how each stream is created, shared, scheduled and subscribed, who owns and disposes each subscription, and where each error ends up.
    Expose each source of events, callbacks or pushed values as an `IObservable<T>` as close to where it is created as possible, such as the type that wraps a library's event or callback, so every consumer composes that stream instead of re-wrapping the source or passing values along by hand.
    Replace hand-rolled subjects, callbacks and locks that an Rx operator covers, and justify each operator kept by its documented purpose.
    Add a test for each stream whose ordering, sharing or disposal no test pins.
19. **Names.**
    Check every type, member, parameter and local against the *Names* table and the naming rules in `AGENTS.md`.
    Spell out abbreviations, make a method's name say what it returns or changes, and make each argument's name match the parameter it fills where the two mean the same thing.
    Add any concept the code names that the *Names* table lacks, and rename test classes and methods with the code they test.
20. **Comments.**
    Check every comment and XML doc comment against the code beside it and the comment rules in `AGENTS.md`.
    Correct each comment the code has outgrown, cut history, rationale, negatives and references to other documents, shorten wordy ones, and delete those that repeat the code.
    Add a comment where a constraint the code meets is not obvious from the code.
21. **Interfaces.**
    Check every interface, and every class that builds or chooses its own collaborators, against the interface rule in `AGENTS.md`.
    Add an interface where it makes the code simpler or easier to follow, such as a factory that moves the logic that chooses and builds an object out of the class that uses it, and list for the operator any interface that meets none of the rule's reasons.
    A test may switch to a double of a new interface.
22. **Organisation.**
    Check every file against the type, file and folder rules in `AGENTS.md`: one top-level type per file, folders matching namespaces, code-behind for every component, extension methods in `Extensions/`, stored types in `Data/` and the rest in `Models/`, and `internal` unless Blazor or the tests need `public`.
    Merge duplicated logic, split types that do more than one job, and make the test folders mirror the app's.
    **Live check:** repeat the M3 live check and compare each payload and the time from `OnPlay` to delivery with M3's.

### M5. History and diagnostics

Each task that changes the schema edits `InitialCreate` in place and deletes the development databases.
These tasks store the data the M6 pages show, so each page is built once on its final schema.

23. **Remove titles.**
    Drop `video_file.title` from `InitialCreate` and `VideoFile`.
    `PlaybackStartedEvent` keeps the title Kodi builds, and the playback log lines name it.
    A detection request and a running detection carry the video file's most recently seen file path.
    Delete the development databases.
24. **Detections.**
    Add `detection`, move the detection result, container metadata and failure columns from `video_file` to it, and add `detection_result_id` and `failed_detection_id`, following *Schema*, *Running a detection* and *Snap re-check* above.
    The detection queue, `DetectionOrchestrator`, `DetectionRunner`, the snap re-check, *Re-detect all*, the detector version count and `PlaybackHandler` read and write through the two pointers.
    The detector returns a `DetectionOutcome`, so a failed row keeps the container metadata ffprobe read.
    Rebuild `DetectionOrchestrator` as a hosted service that polls, following *Detection queue* and *Detect now* above: a queue check each second on a `PeriodicTimer`, each detection a task through `DetectionRunner`, and the running list, the rules and the pause under one lock.
    It replaces `AspectRatioDetectionJob`, `DetectionSlotAllocator`, the detection job listener and `LibraryWalker.VideoFilesAdded`, and detection events come from the orchestrator's private subject.
    Test that a success writes one row, points `detection_result_id` at it and clears `failed_detection_id`, that a failure writes one row and keeps `detection_result_id`, and that a cancelled or discarded detection writes nothing.
    Test that a re-check conversion writes a `snap_recheck` row that copies the container metadata and versions, that a re-check clear and *Re-detect all* clear only the pointers and keep every row, and that purging a video file deletes its detections.
    Drive the orchestrator's tests with `FakeTimeProvider`, one queue check per advance, and test that nothing starts during a pause, that the first queue check after a pause starts the queued detections, that a pause ends once however often it is disposed, and that stopping cancels the running detections and ends the queue checks.
25. **Playback history.**
    Add `playback` and `delivery`, following *Schema* and *Playback* above, and store every playback started event and delivery there in place of the notifiers' in-memory deliveries.
    Add a query for each player's newest playback and each notifier's recent deliveries, which Status reads.
    Add an *Activity* group to the navigation, as Radarr has, with a *History* page: every playback, newest first, with its time, title, player, path, the ratio and source sent or why nothing was sent, and its deliveries.
    *History* pages in the database and has *Purge history*, which asks first.
    Test that every step of the decision list stores one playback, streams and paths outside the roots included, that each delivery stores one row, a cancelled one included, and that the rows are written without delaying the notification.
    Test that deleting a player or notifier definition and purging a video file keep their playbacks, that *Purge history* deletes every playback and delivery and publishes one event, and that the newest-playback query returns each player's last playback summary after a restart.
26. **Walk history and library root state.**
    Add `full_walk`, `library_root.last_walked_at`, `library_root.walk_error` and `file_path.first_seen_at`, following *Walk* above.
    `FullWalk` in `Data/` replaces `FullWalkSummary` in `Models/`, and `AGENTS.md` takes another example of a type in `Models/`.
    *Settings > Library* shows each root's last walk, its error and its file path count, and the newest full walks with their times, durations and counts.
    Test that a full walk stores its summary whether it finishes or fails, that a missing root sets `walk_error` and the next walk that reads it clears it, and that a new file path sets `first_seen_at` and a rename sets a new one.
27. **Settings > UI.**
    Add `ui_settings` and a *Settings > UI* page for the theme, the date and time formats and relative dates, following *UI settings* above.
    A save publishes an activity event and applies at once in every open circuit, the theme included.
    Add one formatter for dates and times that reads the UI settings, and show every date and time through it, on the pages that exist now and on every page after.
    The theme's light palette is a working one here, and the visual design task designs both.
    Test that a save publishes one event, that the formatter follows each format and the relative setting, and that an open page re-renders its dates after a save.
28. **Log files and System > Logs.**
    Add Serilog through `Serilog.AspNetCore`, writing the console as now and a daily file under `<datadir>/logs` that keeps the newest 7, at the level `Logging:LogLevel:Default` sets, so *Settings > General* still sets it.
    Add *System > Logs*, which lists the log files with their sizes and times, shows one, newest lines last, and downloads it.
    `AGENTS.md` names `logs/` in the data directory.
    Test that a log line reaches the file under the data directory, that the configured level filters it, and that the page lists the files.
    **Live check:** repeat the M3 live check and read each playback and its deliveries on *Activity > History*, and compare the time from `OnPlay` to delivery with M4's.
    Restart the app, and check that *History* and *Settings > Library*'s walks survive it, and that the log file holds the run.

### M6. Media and status

29. **Media page.**
    One row per file path, with path search and status filters.
    Columns are path, ratio (snapped with raw), source, confidence, status and added, and every column sorts.
    The path column shows the file name, with its folder beneath it.
    Ratio, source and confidence come from the detection `detection_result_id` points at.
    The row action is *Detect now*.
30. **Video file detail.**
    Every file path with when it was first seen, the served detection result's container metadata and samples, the last failure's error, the override form (ratio, don't send, note) with when it was last saved, and *Detect now*.
    Add `video_file.override_updated_at`, which saving the override sets.
    The page lists the file's detections, newest first: origin, path, start, duration, versions, and the result or the error.
    It lists the file's playbacks, and each playback that sent a detection result links to that detection.
    Each row on *History* links to its video file's page.
    Test that saving an override sets `override_updated_at`.
31. **System > Status.**
    Each player's connection and last playback summary from its newest playback, each notifier's recent deliveries, the newest full walk and each root's walk state, the running detections with their elapsed time, and detection's pending and failed counts with a link to the failed filter.
    **Live check:** open every page with Playwright and act on one control each, then play a file and watch Status and *History* update without a reload.
    Run *Detect now* and watch its activity messages come and go, the file show on Status until it ends, and its detection join the video file detail page.
    Restart the app, and check that Status still shows each player's last playback and the last walk.

### M7. UI and UX

Each task reviews every page, layout, modal and component in `Components/` for one concern, and improves what it finds.
A task changes what the UI shows and how it behaves, and leaves the services, jobs and schema as they are.
A finding that needs a change outside `Components/`, such as a new activity event or a new query, goes to the operator instead of into the commit, unless the task says otherwise.
Where a choice is open, follow what Radarr does.
Each task captures every page with Playwright at desktop and phone width before and after, following the `debarr-live-check` skill, and compares the two sets.
`dotnet build` and bare `dotnet test` pass after each task, and bUnit tests pin each behaviour a task adds.

32. **Survey.**
    Open every page and modal in each state it can show: an empty data directory, a walked library, a walk and a detection running, a failed detection, an override, a missing root, a disconnected player, an empty and a long history, and a failed delivery.
    Capture each at desktop and phone width, and compare each page with the Radarr page it corresponds to.
    Add the capture script to the `debarr-live-check` skill, so every later task captures the same pages in the same states.
    Add each finding as a bullet under the task below that covers it, and change no code.
    The operator trims the list before the next task starts.
33. **Information organisation.**
    Check what each page is for and what it holds: the navigation and its order, which settings live on which page, how each page groups its settings into sections, and what the Media, video file detail and Status pages put first.
    Check what each card shows at a glance, following Radarr's download client cards: the name, the type, whether it is enabled, and its state.
    Give every list and page an empty state that says what to do next, such as a link to *Settings > Library* when no root is configured.
    Give each setting whose meaning is not obvious a short help text beneath it, as Radarr does.
    Put the settings an operator rarely changes, such as the sample count, the edge skip and the SDR and HDR limits, behind a *Show Advanced* toggle, as Radarr does.
    - Player cards show only the name and *Enabled*, so the disconnected Bedroom looks the same as the connected Theater.
      The edit modal shows no connection state or last error either.
    - Notifier cards show nothing of the last delivery, so the failing Home Assistant looks the same as the working Sink.
      Outside `Components/`: the settings page needs each notifier's last delivery.
    - The empty Players and Notifiers pages show a lone add card, with nothing saying what a player or notifier is.
    - Adding a notifier opens a two-item menu (MQTT, Webhook).
      Radarr's *Add Connection* opens a modal of type tiles, each with a short description.
    - No settings page has *Show Advanced*.
      Sample count, edge skip, the SDR and HDR limits, tolerance, timeout, the Kodi ping interval and request timeout, and the MQTT QoS and client id always show.
    - General has no help text for the bind address, URL base and ffmpeg and ffprobe paths, and doesn't say host settings take effect on restart.
    - Detection's snap list *Ambiguous* column and the detector version section have no help text.
      The MQTT modal's QoS has none, and its TLS switch sits beside a broker URL whose scheme could also say it.
    - *Settings > Library* holds *Scan Now* and the full walk table, which Radarr keeps on *System > Tasks* rather than a settings page.
    - A missing root shows *Last Walked Today 22:58* beside *The folder is missing.* on Library and Status, so the failed walk reads as a success.
    - Status leads with Players and Notifiers.
      On an empty data directory, *No library root yet*, the first thing to fix, is fourth on the page.
    - Activity has one child, History, which costs a navigation level and a click.
    - The video file page puts the full sample table, about 100 rows for The Fabelmans, above Detections and Playbacks, which land 3,500 px down.
      Radarr splits a movie's page into tabs.
    - An overridden file shows its detected ratio in Media's Ratio column and the detail page's Ratio row, and only the status says what is sent.
    - Media's Status column repeats Source on every row that is neither overridden nor failed.
    - Each row of the detail page's Detections and Playbacks tables repeats the file's full path in the widest column.
    - About leads with the full commit hash, and lacks the start time and the ffmpeg and ffprobe versions that Radarr's About shows.
      Outside `Components/`: the versions need a query.
34. **Consistency.**
    Make pages that do the same thing look and act the same.
    One page header with the title and the page's actions, one form layout, one save flow, one way to confirm a destructive action, and one way to report success and failure.
    Follow Radarr's save flow: *Save* is enabled only while the page has unsaved changes, and leaving the page with unsaved changes asks first.
    Format dates, durations, counts, aspect ratios and paths the same way everywhere, through one formatter per kind.
    Use the same casing, icons, spacing and typography for the same things, and move markup that pages repeat into shared components.
    Write every label, button, message and help text in one plain voice, with the same word for the same thing on every page.
    Where a name in the *Names* table is too internal for the screen, choose the term the UI shows instead and use it everywhere.
    Write the conventions this task settles in `docs/ui-conventions.md`, and link it from `AGENTS.md`, so later UI work follows them.
    - *Save* sits at the bottom of General, Library, Detection and UI, off screen on Library and Detection at desktop height.
      Radarr puts *Save* and *Show Advanced* in a toolbar at the top of each settings page.
    - Library and Detection mix row actions that apply at once, adding or removing a root or a snap ratio, with fields that wait for *Save*, and nothing says which is which.
    - One action has three names: *Scan Now* starts a full walk, listed under *Full Walks*, in a section named *Walk*.
    - Destructive actions look different on each page: red trash icons, a red outlined *Re-Detect All*, a grey *Purge Now*, and an amber *Purge* in History's confirmation.
    - Section titles repeat their page's: *Detection* on Detection, *Library Roots* on Library, *Host* on General.
    - Modal fields mix two looks: a field with a placeholder floats its label, and one without keeps it inside the box.
    - Help under a switch sits flush left while help under a field is indented, and a modal's *Enable* switch drops to its own indented row at phone width.
    - Modals have no close button in their header, which Radarr's have.
    - Dates show as *Today 23:13*, *Yesterday 08:18* and *Sep 23 2026 00:59* in one column, with the year shown for the current year.
      The override field shows 2.4 where Status shows 2.40.
    - The log file table sets its whole row in bold, and at phone width centres the file name while the other cells align left.
    - The phone pager keeps only previous and next, where the desktop pager also has first, last and rows per page.
35. **Visual design.**
    Give the app a look of its own: the theme palette, the type scale, the density of tables and forms, the app bar, a logo and a favicon.
    Give each state one colour and use it everywhere, such as a sent ratio, a pending file, a failure and a disconnected player.
    Pair each colour with an icon or text, so colour is never the only signal.
    Design both themes *Settings > UI* offers, light and dark, to the same standard.
    - The app bar is the menu button and *Debarr* in body text, with no logo, and in dark it barely stands apart from the page.
    - One amber marks links, paths, the *0 pending* count, timestamps and the current navigation item, so a link, a label and a state look alike.
      History's *Sent* column shows a detected ratio in amber and an override's in grey.
    - *0 failed* is red when the count is zero.
    - State is colour alone: *Connected* and *Disconnected* have no icon, and a failed delivery is red text among plain lines.
      Media colours only *Failed*, so overridden, suppressed and container rows look like detected ones.
    - A port set by an environment variable looks almost like an editable field, with only the helper line to tell it apart.
    - In dark, disabled buttons, the *Enabled* chips and small secondary text are low in contrast, and a modal's backdrop barely dims the page.
    - *Detect now* is a bare icon on each Media row, the same icon as the Detection navigation item, and a phone can't show its tooltip.
36. **Usability.**
    Check how quickly the operator can find a file and answer a question about it, such as why it was sent a ratio, why it is pending or why it failed.
    Media's search matches the file name and folder, ignores case, updates as the operator types, and has a control that clears it.
    Each filter shows its count, and the search, filters, sort and page live in the URL, so a link or the back button returns to the same view.
    Put the answer to a common question where the operator already looks: a failed row shows its error, a truncated path shows in full on hover, and a status, source or confidence explains itself in a tooltip.
    Link related things to each other: a count on Status to the filtered Media list, a playback to its video file and the detection it sent, and a video file to the root that holds it.
    *History* searches titles and paths and filters by player and outcome, as Media does.
    Keep each common action within one click of where the operator sees the thing it acts on, and let the operator copy a path.
    A finding that needs a new query, such as a search the database has to run, may add it in this task, with a test.
    - History has no search, no filter, no sortable column and no total; the purge confirmation is the only place the count shows.
    - Media's phone sort select doesn't show or change the direction.
    - A failed Media row cuts its error at *ffprobe exited 1: [matroska,webm @ 00�*, and a phone can't show the rest.
    - Media's Ratio cell stacks the snapped and raw ratios with no label, where the detail page says *raw*.
    - The detail page links a file path to nothing, and has no copy control for a path or the file hash.
    - The detail page doesn't explain the *Served* chip, the origins *Queue* and *Detect now*, or *Detector 1*.
    - The sample table doesn't mark the samples that disagree with the result.
    - History links a path only when it is under a root, and a `smb://`, `nfs://` or `plugin://` path can't be copied.
    - The log viewer is plain text with no level column, colour, filter or search, where Radarr shows a log table.
    - Media has no way to act on several files, such as re-detecting every failed one.
      Outside `Components/`: *Detect now* refuses while another *Detect now* runs, so this needs queueing.
37. **Health checks.**
    Add a *Health* section to *System > Status*, and a badge on the System navigation item that counts its messages, as Radarr does.
    Each message says what is wrong and links to the page that fixes it.
    Check for no enabled root, a root whose `walk_error` is set, ffmpeg or ffprobe missing or failing to report a version, an enabled player that is disconnected, a notifier whose last delivery failed, and video files that failed detection.
    The section and the badge update in place as a check's result changes.
    Each player connection keeps in memory when it entered its state and its last error, and Status and the check show them.
    Name the new concepts in the *Names* table first.
    This task may add the service that runs the checks and the activity events it needs.
    Test each check passing and failing, and that a change in a check's result updates the section.
    - The disconnected player, the failing notifier, the missing root and the failed detection each show only as red text in their own table on Status.
    - Between retries a disconnected player shows a plain *Connecting* and loses its error and retry time, so the failure disappears from Status.
38. **Large libraries.**
    Check each page against a generated database of 50,000 file paths, 100,000 detections and 100,000 playbacks, and record the time to open the page and the time for a live reload.
    Media and *History* page, sort, filter and search in the database, and a live reload re-reads only the page on screen.
    This task may change queries and add indexes to `InitialCreate`, with a test for each query it changes.
    - The `debarr-live-check` skill's `seed-large.py` writes the database: 45,000 video files, 50,000 file paths, 100,000 detections and 100,000 playbacks with 92,856 deliveries, 192 MB.
      `time-pages.py` times each page on it.
    - Before, History opened in 798 ms, its *Delivery Failed* filter took 1,280 ms, and its player filter 643 ms.
      The *Delivery Failed* count probed `delivery` once per playback, 616 ms, and ran on every open and every live reload.
    - Before, Media's status counts read a 2 KB detection row per file path, 125 ms, on each Media, Status and health check reload, and the detection queue's once-a-second poll took 26 ms with nothing pending, because the planner chose `ix_video_file_failed_detection_id` over `ix_video_file_queue`.
    - After, every page opens in 250 ms or less: Media 241 ms, History 231 ms, Status 170 ms.
      History's sorts, filters and pages change in 56 to 167 ms, and its search in 355 ms, a full scan of three text columns.
      Media's sorts, filters, searches and pages change in 94 to 204 ms.
    - A Media live reload takes 58 ms of server time at the median and 82 ms at the 90th percentile, measured with Media, Status and *Settings > Detection* open while the queue worked through 400 files.
      The queue's poll takes 0 ms.
    - Picking a page size on Media or History read the table 11 to 14 times, because each pager callback wrote the URL and each URL came back as a new view, and the page it landed on depended on which URL arrived last.
      It now reads the table once and shows the first page.
39. **Live updates.**
    Every value a page shows updates in place while the page is open, with no reload, no `forceLoad` navigation and no button to refresh.
    Check that each page's `Shows` filter covers every event that changes what it shows.
    A reload keeps the scroll position, focus, sort, filter, paging, expanded rows and unsaved form edits, and `@key` on each row keeps the rows that did not change from re-rendering.
    A long operation shows its progress beside the control that started it, and the control shows it is busy and why it is disabled.
    Test that an event updates a row while the page keeps its sort, filter and an unsaved edit.
    A finding that needs a new activity event or a finer-grained one may add it in this task, with a test that it is published.
    - A link or the back button that changes both the page size and the page reads the table three times, since MudTable's `SetRowsPerPage` reads the first page before the page is set; it takes about 700 ms on the large library, and lands on the right page.
    - Media's rows jump about 4 px on each live reload.
      MudTable shows its server-data loading row, `.mud-table-loading`, in the header while `ReloadServerData` runs, and every activity event on Media's filter runs one.
      `capture-running.py` caught it on each of three runs: the first row moved from 210 to 214 px exactly while the loading row showed.
    - A running detection shows nowhere on Media: its row keeps its old status, *Pending* stays 0, and only the action's tooltip says *Detecting*.
    - A second *Detect now* shows the warning toast *Detect now is already running on another video file* over the page header, and the row actions don't say they are unavailable.
    - *Scan Now* shows no progress and stays enabled while its walk runs.
    - After *Detect now* scrolls Media's table sideways, the table stays scrolled with the file names cut off on the left.
    - Activity messages don't name the file (*Started detecting 1 file.*), and at phone width, with the drawer closed, nothing shows that work is running.
    - After, a link or the back button that changes both the page size and the page reads the table once, and Media's opens in 279 ms on the large library.
      The first of the three reads came from Media re-rendering inside `SetRowsPerPage`'s `RowsPerPageChanged`, since a navigation reaches `OnParametersSetAsync` outside a render batch.
      The other page opens and view changes stay within task 38's times: Media opens in 282 ms and changes view in 51 to 211 ms, and History opens in 200 ms and changes view in 60 to 318 ms.
    - MudTable keys each row by its item, and Media's rows were new objects on every read, so a live reload replaced every row's element and dropped focus and hover.
      Media's row is now equal by its path and History's by its playback, so a reload keeps each row's element and updates its values in place.
    - The table's loading bar lies over the header's lower edge and shows after 400 ms, so no read moves the rows.
    - A running row's status reads *Detecting* with how long it has run, and its *Detect Now* stays one element, marked busy, so it keeps focus.
      At phone width the other rows' *Detect Now* reads *Detect Now Busy*, and its accessible name says it is available when the running one ends.
    - Task 39's after captures show Media's first row steady at 210 px through a running walk, detections and a playback, and no console errors beyond the not-found page's 404.
    - Outside `Components/`: a missing root's health message changes its *Since* on every scan, rather than keeping when the problem began.
    - *Scan Now* reads *Scanning* with a spinner, disabled, and the library scan's last run reads *Running for* while its walk runs.
    - Task 39's before captures found no sideways scroll on Media at either width: the table fits its card, and the survey's scroll came from Playwright scrolling a *Detect Now* into view on a table that a long UNC path widened, which task 42 covers.
    - The drawer lists the running work above the activity messages, and with the drawer closed the app bar sums it up in one line that opens the drawer.
      An activity message about one detection names the file.
    - *System > Logs* checks its files every 2 seconds and shows new entries in place, keeping the filter, search, page and an open *Details*.
    - *Settings > General* kept no unsaved edit through a reload, *Settings > Detection* missed a purge, and the video file page reloaded on every other file's events; each now follows the rule.
    - Outside `Components/`: the detection started and finished events carry the file path, and `LibraryWalker.RunningFullWalkStartedAt` says when the running full walk began.
40. **Motion and feedback.**
    Animate the changes a live update makes, so the operator sees what changed: a row that appears, leaves or changes, a status that changes, and an activity message that comes or goes.
    Show a skeleton or a progress indicator while a page or section first loads, in place of a blank area.
    Keep each animation short, 150 to 250 ms, put its CSS in the component's `.razor.css`, and honour `prefers-reduced-motion`.
    - Activity messages stay after the work ends: *Started detecting 1 file.* sits above *Finished detecting 1 file.*, and *Full walk started.* above *Full walk finished*.
    - Status's columns move as their content changes: the Last Playback column started at about 1060, 935 and 736 px across three running captures.
    - After, an activity message reports only what ended or was saved, since the running work shows what has started, and a running detection's line shrinks away as its *Finished detecting* message grows in.
      `MotionList` animates the activity messages, the running work, Status's health messages, running detections and recent deliveries, Tasks' library scans, the video file page's detections and playbacks, and Library's roots.
    - A status that changes fades its new state in, and Media's *Detecting* and the status it becomes go through one label, so the change shows on the row.
    - A row that a live reload brings onto Media or History fades in; a new sort, filter or page brings none.
    - Media's first read showed *Loading…* for 120 to 190 ms and History's an empty table for 130 to 230 ms; both now show one placeholder row for each row the page will hold.
      Every other page already had its data in the circuit's first render, so none showed a blank area.
    - Status's columns held one position each through `capture-running.py`'s walk, detections and playback at desktop width; at phone width the columns size to their content, since fixed widths broke words inside themselves.
    - Under `prefers-reduced-motion` every animation is off and a leaving item hides at once.
41. **Errors and recovery.**
    Check what the operator sees when something goes wrong, and that each failure says what happened and what to do.
    Show each validation message beside its field, including a refusal the server returns, such as an overlapping root or a value outside its CHECK bounds.
    A failed save or a failed *Test* keeps the form's edits and shows the reason.
    Restyle the reconnect modal and the unhandled error bar to match the app, with wording the operator understands.
    The layout's error boundary recovers when the operator navigates to another page.
    Test that a refused save keeps its edits and shows the message beside its field, and that the error boundary recovers on navigation.
    - A failed detection shows ffprobe's raw stderr, memory address included, with nothing saying what went wrong or what to do, and the page repeats it in the alert and in Detections.
    - A video file id that never existed says *This video file no longer exists. A purge deletes a video file once its last file path has been gone for the retention period.*, a cause that may be false, in internal terms, titled *Video File 99999*.
    - The not-found page is the template's text and heading style, with no link back to Media.
    - Outside `Components/`: the log writes *Bedroom disconnected � Retrying at �* at Information on every retry, with the retry time in UTC while the line's timestamp is local.
    - After, a failed detection says what went wrong and what to do, with a *Fix in* link when another page fixes it, and gives ffprobe's or ffmpeg's own words without the memory address or the path, once.
      Media and the Detections table show only what went wrong, with the rest in a tooltip.
    - A video file id that never existed is titled *Video File Not Found*, says the file may have left the library, and links to Media; the not-found page and `/Error` use the app's header and link onward.
    - The log writes a player's first failed connection at Warning, with *Retrying in 1 s.*, and each failed retry after it at Debug.
    - A refused player or notifier name, snap list, override ratio or detection setting shows beneath its field and clears once the field changes.
      The numeric fields clamp a typed value to their bounds, so a bounds refusal comes only from a save that bypasses the page.
    - Before, a save or action that threw replaced the page with the error boundary and lost the edits, and one in a modal stopped the circuit.
      Now the failure shows where the form's failures show, such as *The settings were not saved. Access to the path is denied. System > Logs has the details.*, and the edits stay; the live check made `config.json` a folder, and General kept */debarr* with *Save* enabled.
    - The error boundary shows *Something Went Wrong*, what failed, *Try Again* and a link to *System > Logs*, and clears when the operator opens another page.
    - The reconnect modal and the error bar take the app's surface, type and buttons in both themes.
      MudBlazor styles the reconnect modal as well, with `!important` on its background and its buttons' margin, which the modal's CSS overrides.
      Blazor adds the retrying class beside the first attempt's, so the modal hides the first attempt's text while it retries.

### M8. Domain model and event store

M8 moves the code to [domain-model.md](domain-model.md), following decisions Q6 to Q10, on the branch `domain-model-event-store`.
It goes one aggregate at a time, least coupled first, and each aggregate's task removes its EF Core writes as it lands.
Each task ends with `dotnet build` and bare `dotnet test` green, and keeps every rule in [domain-model.md](domain-model.md)'s *Behaviour* except those the rule itself says the task changes.
A rule a task finds it can't keep goes to the operator as a decision before the task changes it, and the decision updates *Behaviour*.
Page and integration tests stay as the check on behaviour, and service tests that read the database give way to decision and projection tests.

43. **Names and rules.**
    Add [domain-model.md](domain-model.md), decisions Q6 to Q10, *Status* and this milestone, and apply the rule changes to `AGENTS.md`, [core-principles.md](core-principles.md) and [ui-conventions.md](ui-conventions.md).
    Rewrite *Names* in the ubiquitous language, and move *How the pieces work* out of the plan: its rules to [domain-model.md](domain-model.md)'s *Behaviour* and *Constraints*, and its text to [rewrite-history.md](rewrite-history.md).
    Move the finished milestones, *Ported types*, the v2 porting notes and the schema's SQL to [rewrite-history.md](rewrite-history.md), and task 42 to the UI audit milestone.
    Documentation only.
44. **Words on screen.**
    Change every label, help text, message and title to the terms in [domain-model.md](domain-model.md)'s *Renames* and [ui-conventions.md](ui-conventions.md), such as *Root Folder*, *Manual*, *From File*, *Standard Ratios* and *Check Picture*.
    Behaviour and the notification payload stay as they are.
    Update the page tests' expected text, and capture every page as task 32 did.
45. **Domain and names in the code.**
    Move the domain types into `Domain/`, the activity feed and its events into `Activity/`, and the health checks into `Health/`, following [domain-model.md](domain-model.md)'s *Folders*, with namespaces to match, and rename types, members, tables and columns to the code column of its *Renames*.
    Name each page's component with the suffix `Page`.
    Edit `InitialCreate` in place.
    Test folders mirror the app, and tests change only their names and `using` lines.
46. **Value types.**
    Add `AspectRatio`, `SnappedAspectRatio`, `StandardRatios`, `FileHash`, `Override`, `PathMapping`, `PlaybackOutcome`, `ContainerMetadata` as a value, and the others [domain-model.md](domain-model.md) lists, and use them in today's services.
    Test each one's rules.
47. **Chapters and slices.**
    Move `Domain/`, `Services/` and `Jobs/` into the chapters, following decision Q11 and [domain-model.md](domain-model.md)'s *Folders*, with the tests mirroring them.
    Add the seven aggregate roots with a `Create` or `Apply` per event, each chapter's `Events.cs`, and a slice file per command whose static `Validate` and `Handle` decide, tested without a database.
    Add the value types that only group an aggregate's settings, which task 46 left to their aggregates: `PlayerEndpoint` with `KodiEndpoint`, `MqttSettings`, `WebhookSettings` with `WebhookHeader`, the scan interval, the date and time formats, and `DetectionFailure`.
    Today's services call the handlers and still store through EF Core.
48. **Event store and Wolverine.**
    Add Fisher on `debarr.db`, and Wolverine with its Fisher integration in Solo mode.
    Add middleware that opens a logging scope per command and turns an unexpected exception into a failed `Result`, and a retry with cooldown on `StreamLockedException`.
    Run code generation Static in the Dockerfile and Dynamic in Debug builds.
    Add the session listener that publishes `ReadModelChanged`.
    Read models use Fisher's projection types, whose rebuild clears their tables and resets their progress in one transaction.
    Test one command from a component end to end: a `FieldError` in its `Result` reaches its field, a version conflict reaches the form, and a write-lock conflict is retried.
    Test the listener, a rebuild that removes a row the replay can't recreate, and that the image boots with `--read-only`.
    Stop for review if the `Result` path doesn't hold, since it is untested upstream.
49. **Playback history.**
    `PlaybackHandler` sends commands that append `PlaybackHandled` and `DeliveryFinished` to a stream per playback.
    Inline projections write History's table and Status's last playback summary.
    *Purge History* becomes *Clear History*, which appends `HistoryCleared` and deletes nothing, and the history's read models show only what came after the newest clear.
    **Live check (the gate):** the time from Kodi's `OnPlay` to delivery is unchanged, and History's timings on the large library match or beat task 38's.
    Stop for review if either fails.
50. **Players and notifiers.**
    Commands for saving and removing players and notifiers, on the `Players` and `Notifiers` aggregates, each a singleton stream whose entities keep their names unique, and projections for their settings pages and Status, keyed by each player's and notifier's Guid.
    `PlayerConnectionService` follows the player events.
    Test a duplicate name, a rename that frees the old name, and that saving, disabling and removing a player reconnects and closes its connection.
51. **UI settings and detection settings.**
    Singleton streams whose missing stream means the defaults, commands for their pages, and projections the detection runner and the pages read.
    Test the bounds as field errors and that a change of the standard ratios says which results it re-checks.
52. **Library.**
    Root folders and library settings as commands, and each library scan as a `LibraryScan` stream of `LibraryScanStarted`, a `RootFolderScanned` per root folder, and `LibraryScanEnded`, or `LibraryScanInterrupted` when startup finds it open.
    Quartz.NET's rescheduling and the folder watchers follow the library events.
    Test an overlapping root folder, a changed scan interval, and a root folder's scan state.
    Adding or enabling a root folder runs a library scan, only a library scan records a root folder's scan, and startup ends an open library scan as interrupted.
53. **Wolverine for process work (spike).**
    A spike on a branch of its own that is never merged, whose outcome is a decision in *Decisions* and, when it holds, the changes to tasks 55, 56 and 59 that follow from it.
    [critter-stack.md](critter-stack.md) holds the hooks, side-effect channels and local queue controls it starts from, and its open questions; the spike records there what it proves or disproves.
    Run detections as messages on a Wolverine local queue in place of `DetectionOrchestrator`'s timer, lock and running list: `MaximumParallelMessages` for the detection limit, a retry policy for a transient ffmpeg failure, and a circuit breaker that pauses the queue while ffmpeg can't run.
    Bridge the runtime events to the activity feed through an `IWireTap` on the local queues: Wolverine calls the singleton tap with each envelope it handled, and the tap holds the subject an `Observable.Create` subscribes to.
    Read the queue's listening state, such as paused by its circuit breaker, from `IWolverineRuntime.Tracker`, the runtime's `IObservable<IWolverineEvent>`.
    Answer each question with a test or a measurement:
    - **Restart.** The queue fills from the Media projection at startup with no durable inbox, and no file is detected twice.
    - **Pause.** *Re-detect All* and a standard ratios change pause the queue on demand and resume it, with the running detections drained first, through a public API such as `IListenerCircuit`.
    - **Running work.** The running detections come from the queue and the wire tap, or say what state they still need of their own.
    - **Order.** A detection's start and end reach the feed in that order, and one handler for a marker interface receives every runtime event, or each event type needs a handler of its own.
    - **Cost.** The time from a detection ending to its activity message, against today's, and the cost of a scan's burst of hashes through the bus.
    - **Commits through the same bridge.** Only once the wire tap holds: committed events forwarded with `UseFastEventForwarding` to a local queue with the tap give the feed one source in place of `ReadModelChangeListener`, and a library scan's forwarding writes nothing to the outbox, or says what it writes.
    Stop and keep `DetectionOrchestrator` if restart or pause needs code of Debarr's own around the queue.
54. **Projection storage (spike).**
    A spike on a branch of its own that is never merged, whose outcome is a decision in *Decisions* on where each read model is stored and, when it holds, the changes to tasks 55, 57, 59 and 60 that follow from it.
    [critter-stack.md](critter-stack.md) holds the projection types, document storage and EF Core projection storage it starts from, and its open questions; the spike records there what it proves or disproves.
    Build the Media read model twice from the `VideoFile` events, each time as a `SingleStreamProjection` or `MultiStreamProjection` whose `Apply` methods fold the status, the current detection, the override, the failure and the paths:
    - as Fisher documents, read through Fisher's LINQ with duplicated fields for the columns Media filters and sorts on;
    - as EF Core entities through `ProjectToEfCore`, read through the EF Core context as today.
    Compare both with a `FlatTableProjection` and a generated status column, on a data directory imported from the large library.
    The branch `task54-video-files-flat-table-wip` holds that flat table: `video_file_row` with the generated status, `file_path_row` and `detection_row`, joined into `MediaRow`.
    It is an unfinished task 55, built before these spikes were planned, which builds but whose tests were never run green, and its last commit's message says what it holds.
    Answer each question with a test or a measurement:
    - **Queries.** Opening Media, the failed filter, search and sorting by ratio take no longer than the architecture review's 10 ms, 3 ms, 28 ms and 8 ms.
    - **Query features.** A case-insensitive sort, a search that escapes `%` and `_`, and the status counts each translate, or say what replaces them, such as Fisher's full-text index for search.
    - **History.** A `Playback` document holding its deliveries replaces `PlaybackRow`, `DeliveryRow` and `LastPlaybackSummary` and keeps History's timings from task 38.
    - **Rebuild.** A rebuild clears the read model and replays it in the time the review measured, 32 s for Media, and with `ProjectToEfCore`, a changed shape rebuilds with no migration.
    - **Archiving.** `DeleteEvent<VideoFileArchived>()` removes an archived file's rows, and a restore brings them back.
    Keep flat tables for the read models whose rows map one event to one row, such as the settings, unless one storage for every read model costs nothing more.
55. **Video files.**
    The library scan appends only what changed on disk, working it out from the file path rows.
    The detection runner sends `RecordDetection`, and the video file page sends `SaveOverride`.
    Following decision Q14, an inline `SingleStreamProjection` folds each video file's stream into a `MediaRow` document, whose status the fold works out with `VideoFile`'s rule, and the file path and detection rows stay flat tables.
    Media lists one row per video file with its file paths in the row, and the video file page reads the document for its status, result, failure and override.
    Index the document as [critter-stack.md](critter-stack.md)'s *What task 54 found* lays out: the status as an integer member, a lowercased path key for the path sort, a composite index for each filter with its sort, and a trigram full-text index over the lowercased paths for search, which falls back to `Contains` for a term under three characters.
    The detection queue is a query on the documents with a partial index.
    Test each decision's events, each projection, Media's filters, sorts and search, and that a playback during a scan and a detection completes without waiting on either.
    Start from `task54-video-files-flat-table-wip`: keep its commands, events, scan and detection changes and its file path and detection rows, and replace `video_file_row` and `MediaRow`'s join with the document.
    Its first commit, `5452123`, is already on this branch as `312c821`.
56. **Library-wide decisions.**
    *Re-detect All* and a standard ratios change, as commands inside the detection pause, appending to every affected stream in one transaction.
    Test both directions of a standard ratios change and that the queue picks up the cleared files.
57. **Archiving.**
    Follow [domain-model.md](domain-model.md)'s *Archiving*: archive at once when a root folder is removed or disabled, archive a file a scan left with no path at the end of the next library scan, and restore with the state-carrying event.
    Retire the retention setting and *Purge Now*, and mark an archived file's page *Archived*.
    The Media projection deletes a document with `ShouldDelete(VideoFileArchived)`, since a rebuild after `DeleteEvent<VideoFileArchived>()` leaves a stream archived and then restored without its document.
    Fisher refuses an append in the session that unarchives its stream, so a restore commits the unarchive first, and the scan restores any stream whose last event is `VideoFileArchived`, which completes a restore a crash cut short.
    Test removal, re-adding, a hardlink kept live, a move between folders never archived, a detection discarded on an archived file, a rebuild, and a rebuild after a restore.
58. **Flat table storage (spike).**
    A spike on a branch of its own, `task58-flat-table-documents-spike`, that is never merged.
    Its outcome is decision Q15 in *Decisions*: whether the read models decision Q14 kept as flat tables become Fisher documents, so every read model is read through Fisher's query session and the EF Core context goes, whether composite projections change decision Q14, and the changes to tasks 59, 60 and 63 that follow.
    Decision Q14 kept them as flat tables by default, since task 54 timed only Media and History.
    EF Core is the only reader of a flat table, so while one remains, Debarr has two ways to store, index, query and rebuild a read model.
    The read models are the file paths, the detections, the root folders, the library scan summaries, the library, detection and UI settings, the players, the notifiers and the history clears.
    Start from task 57's archiving and from [critter-stack.md](critter-stack.md)'s *Projection types* and *Document storage*, and record there what the spike proves or disproves, as task 54 did.
    Weigh each projection type in Fisher's [projections guide](https://fisher.jasperfx.net/events/projections/) that writes documents, and say for each read model which one fits, or why none does:
    - **`SingleStreamProjection`**: a document per stream.
      It fits a library scan summary and each singleton settings stream.
      On the `Library`, `Players` and `Notifiers` streams it gives one document holding every root folder, player or notifier.
      On a `VideoFile` stream it gives one document holding the file's detections, which the video file page reads together, or its file paths, which `MediaRow` already lists.
    - **`MultiStreamProjection`**: a document per key, grouped with `Identity`, `Identities`, `FanOut` or a custom grouping.
      It gives a document per root folder, player or notifier keyed by its id, a file path keyed by its path across the `VideoFile` streams, and a detection keyed by its id.
      Fisher's guide warns that an inline multi-stream projection contends on its documents; say whether Debarr's one writer meets that.
    - **`EventProjection`**: arbitrary writes per event, through `Create` or the operations `Project` is handed.
      Fisher's guide says its teardown is the most likely to be incomplete, so it declares each document type it writes with `DeleteViewTypeOnTeardown<T>()`, and its rebuild test proves it.
    - **Composite projections** run their projections as ordered stages that rebuild together in one pass, and a later stage reads what an earlier one just built from the aggregate cache.
      They could rebuild `MediaRow` and any document folded from the `VideoFile` events together, give a file path document the status `MediaRow`'s fold works out, and show a later stage which paths an archived `MediaRow` held.
      They run only on the async daemon, and Debarr's read models are inline:
      - `ReadModelChangeListener` hooks the session's commit, which never fires for the daemon's batches, so pages would stop reloading.
      - The library scanner decides from the file paths, the detection queue from `MediaRow`, and *Re-detect All*'s `LoadAsync` from `MediaRow`, each right after a commit.
      - [domain-model.md](domain-model.md) registers every read model inline, so the operator changes that rule first.
      List every reader that needs a read model current when its command commits, and say which an asynchronous read model breaks.
      Time a composite's rebuild of `MediaRow` and the documents folded from the same events against their separate inline rebuilds, and say whether the aggregate cache answers the archiving question below.
      Check whether Fisher can rebuild a composite while its projections run inline the rest of the time.
      Say whether the result changes decision Q14 for Media and History.
    - **Live aggregation** stores nothing and folds the stream on each read.
      Say whether the settings, the players or the notifiers can be read this way in place of a stored read model, and what each read costs.
    - **Vector projections** serve similarity search; confirm that no read model needs it.
    - `ProjectToEfCore` and `EfCoreEventProjection` keep EF Core, which this spike tests removing, and task 54 measured the first.
    Answer each question with a test or a measurement, on the large library's events through `LargeLibraryImport` from the branch `task54-projection-storage-spike`, against today's flat tables:
    - **Scans.** A library scan works out what changed on disk from the file path documents, and a folder scan from those under its folder, in no more time than from `file_path_row`.
      A file path held in a document per video file can't be indexed by Fisher, so say how a scan finds a path in that shape.
    - **Detections.** The detection runner reads a video file's paths in `HashedAt` order, and the video file page reads its paths and its detections newest first, each in no more time than from the tables.
      Say whether a detection's crop samples stay in its document or the page reads them separately.
    - **Every other reader.** Each settings page, Status, *System > Tasks*, the health checks, `PlayerConnectionService`, `PlaybackHandler` and `NotificationPublisher` read what they read today.
      What matters here is that every query translates, following *Document storage*'s list of what Fisher's LINQ refuses, more than its time.
    - **Writes.** A scan's burst, 1,000 video files appended 50 to a session as task 54 timed it, with the file path and detection documents written inline in place of their rows.
    - **Rebuild.** Each document projection's rebuild removes a document the replay can't recreate, and the whole store rebuilds in no more time than with the tables; task 54 found the detection rows alone took 34 s.
    - **Archiving.** Following task 57, an archived video file's documents leave their read models, a rebuild leaves them out, and a restore brings them back, through a rebuild too.
      A document keyed by something other than its stream's id can't be removed with `ShouldDelete` on its stream, so say how its projection finds the documents an archive removes.
    - **What goes.** List what leaving EF Core removes, such as `DebarrDbContext`, its value converters, the EF Core packages and the clash between Fisher's and EF Core's async operators, and what the documents add, such as an index set for each document type.
    The point of the change is one storage.
    If any read model has to stay a flat table, EF Core stays, and decision Q15 says whether moving the rest is still worth doing on its own merits.
59. **Documents for the flat tables.**
    Following decision Q15, replace every flat table but History's with a document or a folded aggregate, read through Fisher, as [critter-stack.md](critter-stack.md)'s *What task 58 found* lays out:
    - `FilePathRow` becomes a document keyed by its path, from a `MultiStreamProjection` over the video file streams whose every event writes the whole document.
      The scans read a folder's paths, and *Settings > Library* counts them, through a range of ids in `AdvancedSql` that reads the path, the video file and the stat, and a test pins that SQL to Fisher's table and columns.
    - The video file page folds the file's stream for its detections, live or archived, and `DetectionRow` and its projection go.
      Today a restored file's page lists no detection after a rebuild while the file was archived; test that it lists every one.
    - `LibraryScanSummaryRow` becomes a document from a `SingleStreamProjection`, which lists each root folder the scan covered with its error, indexed on `(Closed, StartedAt)` and on `StartedAt`.
      A root folder is the library's, with the newest scan that covered it since it was added, so a root folder added again starts afresh, and `RootFolderRow` goes.
    - The library, detection and UI settings, the players and the notifiers are read with `FetchLatest` on their aggregate's stream, and their rows and projections go.
      A player's or notifier's name stays unique through its aggregate's rule, without the unique index.
    - `HistoryClearRow` becomes a document on the history stream.
    - `ReadModelChangeListener` also publishes a `ReadModelChanged` named for each aggregate a page folds, when a commit appends to its stream.
    Delete `InitialCreate` and every EF Core write, and leave the context mapping only History's tables.
    Rewrite *Schema* for the event store and its documents.
    Test each document projection's rebuild with a document the replay can't recreate.
60. **Pages read read models.**
    Add a query type per page, and replace each page's and health check's `Shows` with the read models it reads.
    Following decision Q14, History and Status's last playback summaries read a `PlaybackRow` document that holds its deliveries, folded by a `SingleStreamProjection` and indexed as [critter-stack.md](critter-stack.md)'s *What task 54 found* lays out, in place of the playback, delivery and last playback summary tables.
    The document holds its deliveries as `Delivery`, the playback's entity, and `DeliveryRow` goes, so the pages, `DeliveryOutcomeText` and the notifier health check take `Delivery`.
    A notifier's newest deliveries, which Status, *Settings > Notifiers* and the notifier health check read, come from `NotifierDelivery`, a document per delivery that its `DeliveryFinished` writes whole, since read from the playback documents through `json_each` they took 422 to 457 ms on the large library for a notifier with none.
    Keep History's timings from task 38, and test each filter, sort and search, and a rebuild.
    Following decision Q16, the video file page reads the video file by folding its stream, the detection runner and `PlaybackHandler` fold the file they act on in place of loading its `MediaRow`, and the scanner checks whether a hash is known from its stream's state; remove each `MediaRow` member no remaining reader uses.
    Time playback's fold on the large library, for the file with the most detections, against today's `MediaRow` read, and stop for review if it adds more than 5 ms to a playback.
    Following decision Q15, delete `DebarrDbContext`, its value converters, `DebarrDbContextFactory`, the EF Core queries in `IQueryableExtensions`, `Data/`, the EF Core packages, `Fisher.EntityFrameworkCore`, which no code uses, and the `dotnet-ef` tool.
    Test that no project references EF Core.
    `ReadModelChangeListener` stays the commit notification, following decision Q13.
    Update [ui-conventions.md](ui-conventions.md)'s *Live updates* and [reactive-extensions.md](reactive-extensions.md)'s *Debarr's streams*.
    Test that every page reloads on its read models' changes, including Status on a change of the standard ratios.
61. **Page actions.**
    Add `PageAction`, and replace the busy, error and field-error fields on every page and modal.
    Test with bUnit.
62. **Logging.**
    Source-generated `[LoggerMessage]` methods with one template style, scopes around each command, detection and playback, and an Information line per command.
    Test with `FakeLogger`.
63. **Cleanup.**
    Remove what the move to the event store left behind, so the branch merges holding only the design in [domain-model.md](domain-model.md).
    Find each candidate with a sweep rather than from memory: the unused-member analyzers (IDE0051, IDE0052), a search for every old name, and a reference check for every file, package and setting.
    - **Code.** Types, members, services, registrations, options and settings keys nothing reaches any more, such as an EF Core write path, a service a command replaced, a row type a document replaced, or an activity event no source publishes, with the tests that only cover them.
    - **Folders.** No file is left in a folder from before the chapters, such as `Domain/`, `Services/`, `Jobs/` or `Data/`.
    - **Names.** No name in the code column of [domain-model.md](domain-model.md)'s *Renames* is left in the code, the tests, the UI's words, a log message or a skill.
      Then delete *Renames*, and the sentences in `AGENTS.md` and [domain-model.md](domain-model.md) that describe code a task hasn't reached yet.
    - **Members task 60 left with one reader or none.**
      `MediaRowResult.DetectionId` and `MediaRowFailure.DetectionId` have no reader in the app, since the video file page, *Detect Now* and playback fold the video file's stream; the tests in `DetectionOrchestratorTests`, `DetectionSettingsServiceTests`, `MediaRowTests` and `LibraryScannerTests` that tell the current detection by them read it from the folded `VideoFile` instead.
      `ToDetectionRequest` is on both `MediaRow`, for the detection queue, and `VideoFile`, for *Detect Now*, so the rule for which path a detection reads is written twice; keep it once.
      `PlaybackRowQuery.CountByOutcomeAsync` has one caller left, `CountPlaybackRowsByOutcomeAsync`.
    - **Refactors from task 60's comment review.**
      Each makes a name or a type say what a comment the review deleted said.
      A member an index names stays a get-only property that Fisher writes into the JSON, and a renamed member renames its index, which the store creates on startup, so check each plan test after a rename.
      The candidates:
      - `StringExtensions.ToSortKey`: a name that says the key orders ignoring case and breaks ties by the value.
      - `LibraryScanner`'s `videoFileAdded`: a name that says a restored archived video file counts as added, such as `videoFileJoinsLibrary`.
      - `VideoFileDetailPage.Shows(ReadModelChanged)`: the test that a `VideoFile` change is to the page's own stream, as a named predicate.
      - `VideoFileDetailPage.ShowsActivity`: the page reloads on every video file's detection start and end only to keep *Detect Now*'s availability current.
        Give that availability its own subscription or component, so the page reloads on its own video file alone.
      - `PlaybackRow.OutcomeGroup` with `SentBit` and `DeliveryFailedBit`: a `[Flags]` enum, still stored as an `int`, since the grouped count reads `Max` of it from `(OutcomeGroup, OccurredAt)` and `(PlayerName, OutcomeGroup, OccurredAt)`.
      - `PlaybackRowProjection.AddTo`: a name for each of its 19 indexes, for the read it serves, as the `DeliveryFailed` partial indexes have, so a plan test names the index it expects.
      - `CountPlaybackRowsByOutcomeAsync` and `OutcomeGroups` take a `PlaybackRowView` and ignore its outcome filter; a type that holds only the player and the search.
      - `PlaybackRowQuery.LastPlayback` returns every playback of the player, newest first; a name that says so.
      - `NotifierDelivery.Error`, null unless the outcome is *Failed*: the outcome and its error as one type, as `PlaybackOutcome` holds a not-sent reason with its error.
        `Delivery`, the playback's entity that `DeliveryFinished` carries, has the same pair, so the event changes with it, edited in place before the first release.
      - `NotifierDeliveryQuery.ReadNewestDeliveriesAsync` and `NewestBy` switch between two queries on `bool ranToEnd`; two methods, or an enum that names each read.
      - `PageTestContext.SaveDetectionSettingsAsync` sends `ChangeDetectionSettings` to the bus without the detection pause `DetectionSettingsService.SaveAsync` takes; a name that says so, such as `SendChangeDetectionSettingsAsync`.
    - **Generated code.** Run `codegen write`, so `Internal/Generated/` holds a handler for each command that exists and nothing else.
    - **Tooling.** Scripts in `tools/` for a step that no longer exists, such as `regenerate-initial-create.sh`, local tools in `.config/dotnet-tools.json` and packages in `src/Directory.Packages.props` no project uses, and `.gitignore` and `.dockerignore` entries for paths that no longer exist.
    - **Tests.** Doubles, helpers and fixtures no test uses, such as the `EventStore/Probing` types once no test needs them.
    - **Comments and warnings.** Comments that describe a former design, found with `tools/list-comments.sh`, and warning suppressions that only served removed code.
    - **Skills.** The `exercise-kodi` and `debarr-live-check` skills name no removed type or path.
      `debarr-live-check`'s `seed-large.py` still writes the flat tables task 60 removed.
    - **Data.** The development data directories under `.dev` that hold the old schema.
    Ask the operator whether to delete the spike branches `task53-wolverine-process-spike`, `task54-projection-storage-spike`, `task54-video-files-flat-table-wip`, `task58-flat-table-documents-spike` and `task60-large-library-timings`.
    Decisions Q13, Q14 and Q15 and [critter-stack.md](critter-stack.md)'s *What task 60 found* cite them as evidence, so a deleted branch keeps a tag.
    Behaviour doesn't change: page and integration tests change only where they cover removed code, and `dotnet build` reports no new warnings.
64. **Merge.**
    **Live check:** repeat the M6 live check on a data directory imported from the large library.
    Import it again first: task 63 edited `DeliveryFinished` in place, so the deliveries in `.dev/task60/large-library.db` no longer read, and the import on `task60-large-library-timings` writes the old shape, and it named `PlaybackRow`'s indexes, so check whether a database from before keeps the old ones beside them.
    Change one projection's shape and rebuild it, and check that no data was lost and no migration ran.
    The JasperFx command `projections rebuild` fails on this app, for two reasons task 55 found:
    - The command builds the host and never starts it, so Fisher's `ApplyAllDatabaseChangesOnStartup` never runs.
      A fresh database has no `fi_events`, and a projection whose shape changed has no new table or index; `db-apply` applies them.
    - Fisher 1.14.0's `IEventStore.Subject` is `fisher://main`, but its `TryCreateUsage` gives the database file's URI as the usage's `SubjectUri`.
      JasperFx's `ProjectionHost` looks the store up by that URI, throws `ArgumentOutOfRangeException` before any projection runs, and prints only *Errors detected*, even with every table in place.
    *Cannot override services when the IHost is already constructed* is a warning, since `Program` builds the host before `RunJasperFxCommands`, and isn't the cause.
    Decide the way in at the start of this task:
    - A command of Debarr's own, such as `rebuild [read model]`, run with the app stopped, that applies the schema and rebuilds each read model through `BuildProjectionDaemonAsync()` and `RebuildProjectionAsync`.
      It works on Fisher 1.14.0: task 55 rebuilt `MediaRow` this way in 90 s on the large library.
    - A fix in Fisher, so its usage's `SubjectUri` is the store's `Subject`, then `db-apply` and `projections rebuild` with the app stopped, after a pinned upgrade whose release notes are read.
    - A rebuild action on *System > Tasks*, inside a detection pause with scans held off, which is more than this task's check needs.
    Merge the branch into `main`.
    What task 64 found:
    - The way in is Debarr's own command, `rebuild [read model]`, run with the app stopped, since JasperFx's `projections rebuild` still printed only *Errors detected* on Fisher 1.14.0, and the release notes of 1.15.0 and 1.16.0 name no fix for the usage's `SubjectUri`.
      It builds the app's host without starting it, applies the schema with `ApplyAllConfiguredChangesToDatabaseAsync`, and rebuilds every projection Fisher registered, or the one named, through one projection daemon, logging `Rebuilt {ReadModel} in {DurationMs} ms.` for each.
      An unknown name fails before anything is written, naming the read models.
    - The importer on `task60-large-library-timings`, rebased onto the branch, writes each delivery's outcome as the sealed case task 63 made it, and the import took about 7 minutes: 45,000 video files, 50,000 file paths, 100,000 detections, 100,000 playbacks and 93,005 deliveries, each count the seed's.
    - A database from before task 63 keeps none of `PlaybackRow`'s old indexes: applying today's schema to the old import dropped its 19 indexes, 15 named by Fisher and 4 by hand, and created the 19 named ones, with no table changed.
    - The M6 live check passed on `.dev/data-task64`, the import with root folders `.dev/fixtures` and the NAS folders of *The Fabelmans* and *Braveheart*, Theater on the test Kodi and Sink enabled.
      The first boot's library scan removed the seed's 50,000 file paths in 1 min 57 s, and the scan the root folders started archived its 45,000 video files in 2 min 10 s, in 90 commands of 500.
      History and Status showed a playback of *The Fabelmans* 1.3 s after `Player.Open`, without a reload, and the delivery to Sink took 184 ms.
      *Detect Now* on *Braveheart* listed the detection on *System > Tasks*, showed *Finished detecting Braveheart (1995).mkv.* after 22 s, cleared it, and added the detection to the file's page.
      After a restart, Status showed Theater's last playback and *System > Tasks* every library scan.
    - Adding a member and an index to `PlaybackRow` and running `rebuild PlaybackRow` took 279 s on the 100,001 playback rows: every row gained the member, the index was created, the events and streams were byte for byte the same, the 93,006 deliveries stayed, and Fisher ran only the `CREATE INDEX`.
      Reverting the change and rebuilding again took 278 s and dropped the index, leaving the store as it was apart from the rebuild's progress row.
    - `RebuildCommandTests` call the command's `RebuildAsync` in the test process.
      Starting the built app as a process from the tests made another test's data directory fail to delete in 5 of 8 full runs, against none of 8 without it; in the test process it still happens in 2 of 8, which task 83 follows up.
