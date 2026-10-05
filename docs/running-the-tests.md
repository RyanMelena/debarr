# Running the tests

Every test lives in `src/Debarr.Tests`, in folders that mirror `src/Debarr`.
The tests are xUnit v3 and run on **Microsoft.Testing.Platform** (MTP), which `src/global.json` selects:

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

MTP is not VSTest.
It rejects the flags VSTest accepted, and most of the surprises below come from passing one.

## Run the whole suite from `src/`

```sh
cd src
dotnet test
```

The command builds `Debarr.sln` and runs every test.
On the operator's machine, a full run of 1,004 tests took 2 minutes 57 seconds and printed:

```
Test run summary: Passed!
  total: 1004
  failed: 0
  succeeded: 1004
  skipped: 0
  duration: 2m 56s 734ms
```

MTP prints nothing until the run ends, so a deadlock looks like a slow run.
If a run passes 6 minutes, treat it as a hang, and find the test by running the classes alone, as *Run one class* shows.

The repository root has no project or solution file, so `dotnet test` there fails before it builds anything:

```
MSBUILD : error MSB1003: Specify a project or solution file. The current working directory does not contain a project or solution file.
```

Run it from `src/`, or name the project with `--project src/Debarr.Tests`.

## Pass no VSTest flags

`dotnet test` hands the arguments it doesn't know to the test executable, and MTP refuses them.
`--nologo` is the common one:

```
Debarr.Tests.dll (net10.0) Zero tests ran
Exit code: 5

Test run summary: Zero tests ran
  error: 1
```

"Zero tests ran" with exit code 5 means the command line was wrong, not that discovery found nothing.
Drop the flag and run `dotnet test` bare.
`dotnet test --help` lists what MTP accepts, such as `--project`, `--no-build`, `--list-tests` and `--results-directory`.

## Read the exit code, not the last lines

A test failure ends with the summary block, but a build failure prints compiler errors and stops with no `Test run summary` line at all.
A check that greps the tail for `failed:` finds nothing in either case and can read a broken build as a pass.

In bash, a pipe also hides the exit code, because `$?` belongs to the last command:

```sh
dotnet test | tail -20; echo $?                  # 0 even when the build failed
dotnet test | tail -20; echo ${PIPESTATUS[0]}    # the exit code of dotnet test
```

Run `dotnet test` unpiped, or write its output to a file and read the file:

```sh
dotnet test > "$TMPDIR/test.log" 2>&1; status=$?
tail -30 "$TMPDIR/test.log"
```

## Run one class

xUnit's own MTP options filter the tests, after a `--` separator.
`--no-build` skips the build when the tests are already built:

```sh
dotnet test --project Debarr.Tests --no-build -- --filter-class Debarr.Tests.ArchitectureTests
```

`--filter-class` takes the class's full name, or a pattern such as `"*ProjectionRebuild*"`.
`--filter-method` filters by method the same way, and `--filter-namespace` by namespace, such as `Debarr.Tests.Scanning`.
`ArchitectureTests` alone runs in about 6 seconds.

## Know what each test boots

Most tests call pure code: a handler's `Validate` and `Handle`, a value type, a parser.
The rest boot some of the app on a temporary data directory under `TMPDIR`, named `debarr-<random>`, and delete it when they end.

- `TestHost` builds a generic host with the app's services, as `Program.cs` registers them, and starts none of the app's processes.
  A test adds the processes it exercises, replaces the services it doubles, such as the clock or the detector, and starts `Scheduler` when its Quartz triggers should fire.
- `DebarrWebApplicationFactory` boots the whole app through `Program`, with `App:DataDir` set to its temporary directory.
  It gives each Quartz scheduler a unique name, since Quartz keeps one scheduler per name in the process and the tests run in parallel.
- `AppTestContext` is the base class for a test that boots the whole app through `DebarrWebApplicationFactory`, appends events to its store, and reads what the projections, the aggregates and the health checks make of them.
  Its `RebuildAsync` replays the events into one read model.
- `PageTestContext` is the base class for the bUnit page tests in `Components/Pages/`.
  It boots the whole app, renders a page inside the layout as the router does, and resolves the services the page injects from a scope of the app's container.
  MudBlazor's services come from the test's own container, and JavaScript interop runs in loose mode.
  `SeedVideoFileAsync`, `SeedPlaybackAsync` and `SeedPlaybacksAsync` add a video file and playbacks to its store.
- The other bUnit tests in `Components/` derive from `BunitContext` directly and render one component with the services they add.

## Await every event a component test raises

bUnit's synchronous `Click()` and `Input()` return before the handler runs whenever the renderer is busy, since they discard the task of a dispatch the renderer queues behind the work it is doing, and a `WaitForElement` can return while the renderer is still finishing the batch that rendered the element.
A test that then reads the markup sees the page as it was before the click, now and then.
So a component test awaits each event it raises, and the test project's build refuses the synchronous forms through `BannedSymbols.txt`.
A page test calls the test project's `cut.RaiseClickAsync("#kodi-save", Timeout)` and `cut.RaiseInputAsync("#kodi-name", "Theater", Timeout)`.
Each waits for the element to render, then raises the event on the renderer, and returns once the handler has run up to its first wait and the page has rendered, so a handler that waits for what the test does next, such as a confirmation it cancels or a lock it holds, never holds the test.
What the handler finishes later, such as a command's reply, the reload its commit causes or a modal closing, shows later, so check it with `WaitForAssertion`.
A test that holds a plain element, such as one row of a `FindAll`, awaits bUnit's own `element.ClickAsync()` or `element.InputAsync(value)`.
The two pairs have different names because they return at different points.
`RaiseClickAsync` and `RaiseInputAsync` return once the handler reaches its first wait, and bUnit's `ClickAsync` and `InputAsync` return once the handler completes, so a handler raised through bUnit's calls must finish without waiting on the test.

`ProgramTests` boots `Program` and checks each page, the health endpoint, the WAL database, the order the processes start in and the log file.
`ProgramConfigurationTests` sets process environment variables, so its collection turns parallel runs off.
Leave that attribute in place, since a test running beside it would read its variables.

## Know what the rule tests check

Some tests check the rules in `AGENTS.md`, so a change that breaks a rule fails the suite, not only a review.

- `ArchitectureTests` scans the source: one type per file named for it except in a chapter's files, no namespace segment named for a type, no `@code` block in a Razor file, every text field and select outlined and dense, and the shape of each handler's `Validate` and `Handle`.
  It finds the source through the path the compiler recorded for `ArchitectureTests.cs`, so it needs the source where the build found it.
  It also boots the app through `DebarrWebApplicationFactory` and checks the store's projections: each is registered `Inline`, since no async daemon runs, and no `Create` or `Apply` takes a session, since a fold that reads documents gives a different row on a rebuild.
  Each of those two tests first checks that its rule names a probe projection that breaks it.
- `BannedSymbolsTests` resolves every entry in the app's and the test project's `BannedSymbols.txt` against the assemblies that project references, and names each entry that matches no symbol.
  The analyzer ignores such an entry, so a package upgrade that changes a banned overload's signature would otherwise lift the ban with no warning.
- `LogTemplateTests` checks every `[LoggerMessage]` template and log scope for the style AGENTS.md's *Logging* sets.
- `RetryRuleTests` and `CommandPipelineTests` check that a write conflict logs Debarr's retry line at Warning and no `Invocation of` line from Wolverine, that a command that runs out of retries logs its `failed` line at Error, and that any other exception still logs Wolverine's line at Error.
- `ProjectionRebuildTests` stores, for each read model, a document the replay of the events can't recreate, rebuilds the read model, and checks that the document is gone.
  `Every_read_model_has_a_stale_document` fails when a projection has no entry in its `StaleDocuments`, so a new read model needs one.
- `NotificationTests` compares the notification's JSON with `Playing/Snapshots/notification.json`.

## Expect the detector tests to skip on a fresh clone

The `AspectRatioDetectorTests` cases that run ffmpeg call `Assert.SkipUnless` and `Assert.SkipWhen` instead of failing when a prerequisite is missing, and print the reason.

| Prerequisite | How to meet it |
|---|---|
| `ffmpeg` and `ffprobe` on `PATH` | Install a static ffmpeg build and put it on `PATH`. |
| The fixture videos in `.dev/fixtures/` | Run `tools/make-detection-fixtures.ps1`. |

`.dev/` is ignored by git, so a fresh clone has no fixtures, and a green run with skips is normal there.
The run above had both prerequisites and skipped nothing.

## Know why `debarr.db` can stay locked

A test that boots the app deletes its data directory when it ends, through `TestDataDirectory.DeleteAsync`.
On Windows, a file that is still open can't be deleted, and `debarr.db` can stay open for seconds after the host stops.

The cause is in Fisher 1.14.0.
Its LINQ queries, its loads by id and its event reads dispose the data reader but not the `SqliteCommand`, so the command's prepared statements wait for the finalizer.
A connection closed in the meantime stays open, holding `debarr.db`, until the finalizer thread reaches those statements, and finalizing its last one closes it, checkpointing its WAL.
The process has one finalizer thread, which closes every test's connections in turn, so the wait grows with the suite's load and the disk's speed.
A dump during task 109 found 3,940 objects ready for finalization, 1,443 of them prepared statements, while six tests waited on the finalizer thread.

`TestDataDirectory.DeleteAsync` retries the delete every 20 ms.
After a second it runs `GC.Collect` and `GC.WaitForPendingFinalizers` once, which waits out that backlog, and after 2 minutes it fails the test with the exception that names the open file.
The wait also paces the suite, since a test that waits starts no new host while the finalizer thread catches up; a run that deleted in the background instead grew the test process to 8.5 GB and crashed it after 787 tests.
A failure that says `debarr.db` is "being used by another process" therefore means the file stayed open for 2 minutes, which the finalizer backlog alone doesn't explain, so find what holds the file.
Until task 109 the limit was 30 seconds, and a loaded run failed a test now and then while the backlog outlasted it.
Each run first deletes the `debarr-*` directories in the temp folder older than an hour, which an earlier run's exit left.
[critter-stack.md](critter-stack.md)'s *Checking a new version* says to drop the forced finalization once a Fisher version disposes those commands.

## Share the working tree with care

Another agent can be working in the same checkout.
A build error in a file you never touched usually means someone else is mid-edit, so run `git status --porcelain` before you investigate.

Two `dotnet test` runs at once contend for `bin/` and `obj/`, which shows up as file locks or `MSB3027` and `MSB3021`.
A running app holds `Debarr.dll` open in the same way, so stop it before you build.
Wait and run again rather than deleting the build output from under the other run.

## Give the run enough time

A full run takes about 3 minutes once the build is warm.
The first run after a fresh clone, a package change or a cleaned `bin/` and `obj/` also restores and builds both projects.
Give the command a timeout of 10 minutes rather than stopping it partway and leaving a half-written `obj/`.
