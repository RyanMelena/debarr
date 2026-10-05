# Debarr

Debarr detects the original aspect ratio of your video files, the ratio of the picture with the letterbox or pillarbox bars removed.
When a player starts playback, Debarr sends that ratio to automation over MQTT or a webhook, so a projector's lens memory and a screen's masking can move to match.
It reports facts and never tells automation what to do.

Debarr scans the root folders you give it, detects each video file's ratio in the background, and keeps the result.
Playback reads the stored result, so a notification never waits on ffmpeg.
Its web UI follows Radarr, with *Settings* and *System* pages and a card for each player and notifier.

What Debarr does and the rules it follows are in [docs/core-principles.md](docs/core-principles.md).

## How Debarr reports a ratio

When a player starts playback, Debarr finds the playing file by its path and publishes one notification or none.
The notification is a JSON object with four fields:

```json
{"player":"Theater","occurred_at":"2026-09-14T19:04:11.482Z","aspect_ratio":2.39,"source":"detected"}
```

| Field | Value |
|---|---|
| `player` | The player's name, unique among the players. |
| `occurred_at` | When Debarr received the playback started event, in UTC with three fractional digits. |
| `aspect_ratio` | The ratio snapped to the nearest standard ratio, such as `1.78`, `1.85` or `2.39`. |
| `source` | Where the ratio came from, one of `manual`, `detected`, `container` or `player`. |

The source tells your automation how far to trust the ratio:

- `manual` is an override you set on the video file.
- `detected` is a detection result that ffmpeg's cropdetect measured from the picture.
- `container` is the ratio the file's container reports, which Debarr accepts as it is when it doesn't snap to a ratio marked *Check Picture* (1.78 and 1.33 by default).
- `player` is the ratio the player reports, sent when Debarr has no result for the file, or when the file is outside every root folder or Debarr can't read it.

Debarr publishes nothing for an override marked *Don't Send*, or for a `plugin://` or `pvr://` stream.
It sends no second notification when a detection finishes after playback started.

An MQTT notifier publishes to the topic template `debarr/player/{player}/playback` by default, with QoS 1 and no retain flag.
A webhook notifier sends the JSON with `POST`, `PUT` or `PATCH`, and any headers you add.
Debarr attempts each delivery once, with a short timeout, and *History* keeps every playback with its deliveries.

## Run Debarr in Docker

Build the image from a checkout, with `src/` as the build context:

```sh
docker build -t debarr:latest src
```

The image publishes Debarr onto `mcr.microsoft.com/dotnet/aspnet:10.0-noble` and copies a static ffmpeg and ffprobe, pinned by digest, into `/usr/local/bin`.
It runs as user `65532:65532`, listens on port 8080, and sets the data directory to `/data`.
It sets `TMPDIR` to `/data/tmp`, so every file Debarr writes is under the data directory and the container runs with `--read-only`.

Create the data directory on the host, owned by the user you run the container as, and mount your media read-only:

```sh
mkdir -p data
docker run -d --name debarr \
  --user 1000:1000 \
  -p 8080:8080 \
  -v "$PWD/data:/data" \
  -v /mnt/media/movies:/movies:ro \
  -v /mnt/media/tv:/tv:ro \
  debarr:latest
```

The image never runs `chown`, at build or run time, so the host directory's owner decides who can write it.
Without a bind mount, `/data` in the image belongs to user 65532 and group 0 with write permission, so the image's user or a user with group 0 can write it, and a new named volume mounted there starts with that owner.
Add each media mount's container path, such as `/movies`, as a root folder in *Settings > Library*, and give each player a path mapping from the paths it reports to those container paths.

Open `http://<host>:8080`.
Debarr has no sign-in, so run it on a trusted network or behind a reverse proxy that authenticates.
`GET /healthz` answers when the app is up, for a container health check or a load balancer.

## Run Debarr from source

Debarr needs the .NET 10 SDK, and ffmpeg and ffprobe on `PATH` or set in *Settings > General*.
Set the data directory to an absolute path, since the default `/data` suits only a container:

```sh
cd src
dotnet build
DEBARR__APP__DATADIR="$HOME/debarr-data" dotnet run --project Debarr
```

## Set up the library, players and notifiers

Every setting below is saved in Debarr's database and applies at once, without a restart.

1. In *Settings > Library*, add each root folder as Debarr sees it, such as `/movies`.
   Debarr scans a root folder when you add it, at startup, every scan interval (12 hours by default), and when you press *Scan Now*.
   With *Watch Folders* on, it also scans a folder once changes in it have been quiet for 10 seconds.
   *Video Extensions* lists the extensions that make a file a video file.
2. In *Settings > Detection*, check the standard ratios, the match tolerance and the ratios marked *Check Picture*.
   Debarr detects the newest video file first, *Simultaneous Detections* at a time.
3. In *Settings > Players*, add a Kodi player with the host and port of its JSON-RPC, 9090 by default.
   In Kodi, turn on *Settings > Services > Control > Allow remote control from applications on other systems*.
4. Add a path mapping to the player for each prefix where Kodi's path to a file differs from Debarr's, such as *Player Path* `smb://nas/movies` and *Local Path* `/movies`.
   The longest matching *Player Path* wins, and a path no mapping matches is looked up as Kodi reports it.
5. In *Settings > Notifiers*, add an MQTT or webhook notifier, and press *Test* to send a test notification with the values in the form.

*Media* lists every video file with its status and ratio, and a video file's page shows its detections and its override.
*System > Status* shows each player's connection and last playback, each notifier's recent deliveries, the last library scan and the running detections.

## Configure the host settings

Host settings are the data directory, the bind address, the port, the URL base, the log level, and the ffmpeg and ffprobe paths.
They use .NET configuration, and each layer overrides the one above it:

| Layer | Holds |
|---|---|
| The defaults | Bind address `*`, port `8080`, no URL base, log level `Information`, and `ffmpeg` and `ffprobe` from `PATH`. |
| `<data directory>/config.json` | The values you save in *Settings > General*. |
| `DEBARR__SECTION__KEY` environment variables | An override for any key, such as `DEBARR__SERVER__PORT=9090`. |

| Setting | Environment variable |
|---|---|
| Data directory | `DEBARR__APP__DATADIR` |
| Bind address | `DEBARR__SERVER__BINDADDRESS` |
| Port | `DEBARR__SERVER__PORT` |
| URL base | `DEBARR__SERVER__URLBASE` |
| Log level | `DEBARR__LOGGING__LOGLEVEL__DEFAULT` |
| FFmpeg path | `DEBARR__FFMPEG__FFMPEGPATH` |
| FFprobe path | `DEBARR__FFMPEG__FFPROBEPATH` |

Host settings take effect when Debarr restarts.
*Settings > General* marks each value an environment variable sets, so you can see why a saved value isn't in use.
Debarr reads only environment variables that start with `DEBARR__`.
`config.json` can't hold the data directory, because Debarr reads the data directory to find `config.json`.

The URL base serves Debarr under a path, such as `/debarr` behind a reverse proxy.

## Back up the data directory

The data directory, `/data` by default, is the one directory Debarr writes to.

| Path | Holds |
|---|---|
| `debarr.db` | The event store: every setting, root folder, player, notifier, video file, detection, override and playback, and the read models the pages show. |
| `config.json` | The host settings, written on the first save in *Settings > General*. |
| `keys/` | The Data Protection keys, which Blazor uses for antiforgery tokens. |
| `logs/` | The log files, which *System > Logs* shows and downloads. |

SQLite runs `debarr.db` in WAL mode, so `debarr.db-wal` and `debarr.db-shm` sit beside it.
A copy of `debarr.db` alone, taken while Debarr runs, can miss committed events.
Stop Debarr and copy the whole directory, or take an online backup with SQLite:

```sh
sqlite3 /path/to/data/debarr.db ".backup '/path/to/backup/debarr.db'"
```

Every write is an event, and Debarr never deletes one.
A video file whose last path goes away is archived, and its result, override and history come back when a file with its hash appears again.

The pages read read models that Debarr builds from the events.
To rebuild them, stop Debarr and run the `rebuild` command on the same data directory.
In Docker, run the image with `rebuild` as its argument:

```sh
docker run --rm --user 1000:1000 -v "$PWD/data:/data" debarr:latest rebuild
```

From source, run it through `dotnet run`:

```sh
DEBARR__APP__DATADIR=/path/to/data dotnet run --project src/Debarr -- rebuild
```

`rebuild <read model>` rebuilds one read model, and an unknown name lists the read models.

## Work on Debarr

[AGENTS.md](AGENTS.md) holds the conventions for the code, and [docs/running-the-tests.md](docs/running-the-tests.md) says how to run the tests.
The design is in [docs/domain-model.md](docs/domain-model.md), and the build plan with each task's status is in [docs/rewrite-plan.md](docs/rewrite-plan.md).
The previous version, which read its library from Radarr and Sonarr, is kept in a separate repository.
