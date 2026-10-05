# Debarr core principles

These principles define what Debarr does and the rules its design follows.
A design choice that conflicts with them needs the operator to change the principle first.
They were agreed on 2026-09-23 and updated on 2026-09-24, 2026-09-27 and 2026-09-29.

## Purpose

Debarr detects the original aspect ratio of video files, the ratio of the picture with any letterbox or pillarbox bars removed, before they are played.
When a player starts playback, it reports that ratio to automation.

The consumer is a home theater control system.
At the start of playback it recalls a projector lens memory and a screen masking memory.
The control system maps ranges of ratios to memories itself.
Debarr reports facts and never tells automation what to do.

Debarr is the operator's own tool first and is meant to be publishable later.
It configures and behaves like the *arr apps, and supports other people's layouts within reason.

## Library

1. **The library is the files under the configured root folders.**
   Debarr scans the root folders at startup, on an interval, and when the operator presses *Scan Now*.
   Watching folders triggers a scan of the folder that changed, once changes have been quiet for a short time.
   The operator can switch the interval and the watching off.
   Adding or enabling a root folder scans it.
   Removing or disabling one drops its paths at once and archives every video file left with no path.
   No root folder overlaps another.
2. **Detection covers every video file under the root folders.**
   A video file is one whose extension is in an editable list.
   There are no other exclusion rules.
3. **A file's identity is a partial file hash**: SHA-256 over its size, a chunk from the start and a chunk from the end.
   The scan keeps each path's modification time and hash, and recomputes the hash only when the size or modification time changes.
4. **Results and overrides belong to the hash.**
   A rename or move keeps them.
   An upgrade or re-encode has a new hash and gets a fresh detection, so a result is never served for different content.
5. **A video file with no path is archived**, and a file with its hash that appears again gets its result, override and history back.
   A file whose last path a scan removes is archived at the end of the next library scan, so a file moved between folders is never archived.
   Nothing archived is deleted, so the database only grows.

## Players and identification

6. **Playback events are player-neutral.**
   Kodi is the first player, and Jellyfin and others can follow.
   More than one instance of a player type is supported if it costs little.
7. **A playing file is identified by its path.**
   The player's path is canonicalised, translated into Debarr's local view by that player's path mappings (longest match wins), and looked up with its size, modification time and hash.
   Matching is exact: no suffix, fuzzy or id-based matching.
   A path that is new or changed is hashed inline at playback.
   Playback never waits on a scan or a detection.
8. **A path outside the root folders, or one Debarr cannot read, gets the player-reported ratio only.**
   It is not added to the library.
   A `plugin://` or `pvr://` stream publishes nothing.
9. **A video file is shown and searched by its file paths.**
   A video file holds no titles or other media metadata, from players or anywhere else.
   A playback keeps the title its player reported, as a record of that playback.
   The *arrs are not part of the library or of identification.

## Detection

10. **A container ratio that snaps to a standard ratio marked *Check Picture* goes to cropdetect; any other is accepted as it is.**
    The operator configures the standard ratios and a match tolerance, and marks some of the standard ratios *Check Picture*, 1.78 and 1.33 by default.
    The match tolerance has a floor.
    A file whose snapped container ratio checks the picture has its ratio detected with ffmpeg cropdetect, one run per sample point.
    With no ratio marked *Check Picture*, every container ratio is accepted, and the settings page warns about it.
    The most common ratio across the samples is the result, and the share of samples that agree is its confidence.
11. **Only the raw ratios are stored, and one snap function reads them.**
    Detection, the notification and the UI all call it, and the UI shows the raw value beside the snapped one.
12. **Every detection result follows the current standard ratios.**
    Changing the match tolerance or the standard ratios re-checks every file with a detection result, and changing which ratios check the picture re-checks exactly the files whose container ratio snaps to them.
    A result from the file whose ratio now checks the picture is cleared and detected again.
    A detected result whose ratio no longer checks the picture becomes a result from the file, without running ffmpeg.
    A detection running when the standard ratios or the match tolerance change is abandoned and queued again.
13. **Detection runs the newest video file first**, a configurable number at a time.
    The operator can run *Detect Now* on one file, whatever its status, which starts at once beside the queue.
    One *Detect Now* runs at a time.
    An override never takes a file out of the queue.
14. **A detection result is kept when the detector changes.**
    Each detection result records the detector version, and the operator runs *Re-detect All* for new results.
    It clears every file's current detection result and failure and keeps overrides.
    Every finished detection, success or failure, is kept with its video file as that file's detection history.
15. **A failed detection is recorded once and not retried.**
    It runs again when the operator asks or when the file changes.
    Only a detection result replaces a detection result, so a failed *Detect Now* keeps the file's result.
    Failures are easy to find in the UI.
16. **An override holds a fixed ratio, a "don't send" flag, or both, plus a note.**
    It belongs to the file's hash.
    When both are set, "don't send" wins, and the ratio is kept for when the flag is cleared.

## Notification

17. **A playback started event publishes one notification or none**, with `player` (the player's unique name), `occurred_at` (when Debarr received the event), `aspect_ratio` (snapped) and `source`.
    The source is `manual`, `detected`, `container` or `player`.
18. **With no stored result, the player-reported ratio is sent** with `source: player`.
    No second notification is sent when detection finishes.
19. **An override marked "don't send" publishes nothing.**
20. **Delivery is MQTT and webhook.**
    Each delivery is attempted once, with a short timeout, and one notifier never blocks another.
    Every playback and its deliveries are kept in the history until the operator clears it, and clearing it hides them from the history without deleting them.
    Writing the history never delays a notification.

## Application

21. **Settings follow Radarr.**
    Host settings use .NET configuration, and everything else is edited in the UI and applies without a restart.
    Integrations have a settings page with a card per configured integration, a modal for each type, and a Test button that uses the modal's unsaved values.
22. **Every page updates live** when the facts it shows change.
    Every write is an event in the store, and each read model announces its own changes once they commit.
    Work in progress publishes activity events.
    Pages and activity messages subscribe to both through one in-process feed.
    Edits come from one operator at a time.
    The same operator may have Debarr open in several tabs, and each tab shows the others' saves as they commit.
    Two people editing settings or video files at the same moment is outside Debarr's intended use, so the UI needs no merging of simultaneous edits beyond refusing a save made from a stale form.
    Debarr's own background work, such as scans, detections and root folder removals, still runs beside an operator's edits, and its writes stay correct when they meet one.
23. **There is no authentication.**
    Debarr runs on a trusted LAN or behind a reverse proxy.
24. **The Status page shows each player's connection** and a one-line summary of its last playback: what was sent and why, or why nothing was sent.
    It also shows each notifier's recent deliveries, the last library scan, the detections running now, and the pending and failed counts.
    Short-lived activity messages in the navigation show what Debarr is doing, such as scanning, hashing and detecting.
    Log files are kept under the data directory, and *System > Logs* shows them.

## Code and process

- The code speaks the ubiquitous language of the *Names* table: the operator's words, the same in the UI and the code.
- The domain is organised in vertical slices, following Wolverine's guidance: a folder per chapter, one capability of the app, and a file per command.
  A chapter's aggregates, its events and each of its read models have a file each, and every other type has a file of its own, named for the type.
  The folder path matches the namespace, and no folder shares a name with a type.
- A command's handler decides, with pure static functions that take the command and the aggregate's state and return events.
  The aggregate applies events.
  An extension method sits beside the type it extends, and `Extensions/` holds only extensions on framework types.
- Use descriptive names over short ones, and follow the standard .NET coding conventions.
- A Razor component's C# lives in a code-behind file.
  Types are public, and an interface is written only for a second implementation or a test double.
- Established NuGet packages are preferred over hand-written protocol clients, parsers and process wrappers.
  Package versions are managed centrally, and the integration packages stay: CliWrap, StreamJsonRpc, MQTTnet, and `IHttpClientFactory` with System.Text.Json.
  Every domain write is an event in Fisher, commands run on Wolverine, and each page reads a projection shaped for it.
  In-process events use System.Reactive, scheduled work uses Quartz.NET, and expected failures return FluentResults.
- Keep code comments short, factual and about the code as it is now.
- The solution is one app project, `Debarr`, and one test project, `Debarr.Tests`, which mirrors it.
- The image runs as a non-root user with media mounted read-only.
  Every writable path is under the data directory, and scratch files go to `TMPDIR`.
- Plans are short task lists checked against this document.
  Names are settled before code is written.
  Tests prove each task, and a live check proves each milestone.
