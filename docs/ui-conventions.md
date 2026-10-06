# UI conventions

These are the conventions every page, modal and component in `src/Debarr/Components/` follows.
Where this document is silent, follow what Radarr does.
A new page uses the shared components below rather than repeating their markup.

## Page structure

- Every page sets a `PageTitle` that names it, such as *Library Settings - Debarr*, and starts with `PageHeader`: the page's title at the left, and the page's actions at the right.
  From tablet width the header stays under the app bar while the page scrolls, so *Save* is always in reach.
  On a phone it scrolls with the page, so it never covers a quarter of the screen.
- A page's content is a set of `PageSection` cards.
  A section has a title, optional actions beside the title, an optional description, and its content stacked with one gap.
- A section's title never repeats the page's: *Server* on General, *Root Folders* on Library, *Queue* on Detection.
- Settings pages lay their sections out in a `MudGrid`, two columns on a desktop and one on a phone.
  System pages stack their sections in one column with the `gap-6` spacing.
- A page shows `MudProgressLinear` beneath its header while it first loads.
  A paged table whose rows arrive after the page shows a `TableSkeleton`, one placeholder row for each row the page on screen is expected to hold, so the rows land where the placeholders were; `PagedTable` does this.
- A list with nothing in it says so in a sentence, and says what to do next when there is something to do.

## Lists

- A list the operator searches, such as Media, History or a log file, puts its filters above the table in a `FilterBar`: status or outcome chips at the left, each with its count, then any select and the search box at the right.
- A search box is the one field with a placeholder and no label, and the placeholder says what it matches, such as *Search file names and folders*.
  It ignores case, updates as the operator types, and clears with its clear button.
- A count on a chip counts what the other filters and the search match.
- A table that pages from the database is a `PagedTable`, whose `PagedTableView` keeps the filters, search, sort, direction, page and page size in the URL, so a link or the back button returns to the same view.
  The URL leaves out each value that is its default.
  A link whose sort the table doesn't know, such as one from before a sort was renamed, shows the default sort in its default direction, and the URL is replaced with the view shown.
- Every sortable column sorts both ways and never back to unsorted (`AllowUnsorted="false"`).
  Up to 960 px, where the table shows each row as a card, `PagedTable`'s `SortSelect` replaces the table's own sort select: the column, and a button that names the direction in words, such as *Newest First*, and reverses it.
- A link on a count or a root folder opens the list it names already filtered, such as *3 failed* on Status opening Media's failed filter.
- A table lays each row out as a card where its columns don't fit, with each value beside its column's name, so nothing scrolls sideways.
  A paged or log table, a `MudTable` with `Breakpoint.Sm`, does this up to 960 px through each cell's `DataLabel`, and a `MudSimpleTable`, which fits a tablet, below 600 px through the `stacked-table` class and a `data-label` on each cell, which `StackedTableAssert` checks.
  The cell that names the row, such as a path, leaves its label out and heads the card, a long value takes `stacked-table-block` to sit beneath its label, a row's actions take `stacked-table-actions` to sit in the card's top right corner, and a row that adds to the one above, such as a failed scan's error, takes `stacked-table-continuation`.
  A card leaves out a value the row doesn't have, such as a failed file's ratio on Media or the deliveries of a playback that sent nothing.
  A small table that fits a phone's width, such as the samples or the standard ratios, stays a table.

## Forms

- Every field is outlined and dense: `Variant="Variant.Outlined"` and `Margin="Margin.Dense"`.
  `ArchitectureTests` enforces this on every `Mud*Field` and `MudSelect`.
- A field has a label and no placeholder, so every label sits the same way.
  An example value goes in the help text, starting "Such as".
- Help goes beneath the field as its `HelperText`, which shows at all times.
  Help of one sentence is a fragment with no period, such as *Seconds between pings*, and help of two or more sentences ends each with a period.
  `ArchitectureTests` checks every help text in `Components/`.
- A text or number field sets `Immediate="true"`, so *Save* enables as the operator types.
- A switch is a `SwitchField`, on its own row, with its help beneath it where an outlined field puts its own.
  A modal's *Enable* switch is the first row.
- Settings an operator rarely changes show only while *Show Advanced* is on.
  `ShowAdvancedButton` sits in the page header, or in a modal's footer at the left.
- A ratio field hides its spin buttons and asks a phone for its decimal keypad, so the number has the field's width.
- A field and the button that adds it, such as *Add Root Folder*, share a row: the button keeps its one line and the field takes the rest.
  A button that adds a row to a list in a modal, such as *Add Path Mapping*, takes a row of its own at the modal's full width, which `IntegrationModal`'s fields column gives it.
- A form that takes no edits for now, such as an archived video file's override, shows its fields disabled, without *Save*, under a section description that says why and when its values apply.
- A card that opens a modal, such as a player's or the one that adds a notifier, is a `CardButton`, so Tab reaches it and Enter or Space opens it.

## Saving

- A form's *Save* is a `SaveButton`.
  It is enabled only while the form has unsaved changes, and reads *Saving* while the save runs.
- While a form has unsaved changes, leaving for another page asks first, and closing or reloading the tab asks through the browser.
  A link within the same page, such as a detection on the video file page, leaves the edits in place.
- *Save* sits in the page header when the form fills the page, at the end of the section when the form fills one section, and in the footer when the form fills a modal.
- Everything on a settings page waits for *Save*, except a section whose description says its changes apply at once, such as *Root Folders* on Library.
- A live reload keeps unsaved edits.
  A page or an integration modal holds its form in an `EditedForm`, which keeps the saved and the edited values, takes each reload's saved values, and saves a copy of the edits.
  A modal takes the stored values when *Save* runs, and a page takes them on each reload.
- A save from a stale form is refused.
  When the saved values differ from the ones the unsaved edits started from, such as after a save in another tab, *Save* sends nothing, keeps the edits on screen, and the page header, or the modal's alert above its buttons, says *Saved in another tab. Reload to see the change.*

## Live updates

- Every value a page shows updates in place while the page is open, with no reload, no `forceLoad` navigation and no button to refresh.
  A page inherits `LiveComponentBase`, names in `ReadModels` every read model its reload reads, and reloads when a commit changes one of them.
  Its `ShowsActivity` names only the activity events about state outside the event store: running work, player connection states, health messages and host settings.
  For running work it takes the predicates that `LibraryScanner`, `RootFolderRemover` and `DetectionOrchestrator` hold beside the state they guard, such as `DetectionOrchestrator.ChangesRunningDetections`.
  A page about one video file skips the changes to other video files, and its *Detect Now* follows every file's running detections, since *Detect Now* on another file makes it unavailable.
  A page whose values change with no commit and no activity event, such as a log file on *System > Logs*, checks for changes on a timer, through its `Reloads`.
- A live reload keeps the view: the sort, filters, search, page and page size, the scroll position, focus, an open *Details*, and unsaved edits.
- A row keeps its element while its values change.
  A list whose items come and go while the page is open is a `MotionList`, which keys each item; any other `@foreach` row has `@key` on what the row shows.
  A `MudTable`, which keys each row by its item, lists a row type that is equal by what it shows: the path on Media, the playback on History, the entry and its place among equal entries on Logs.
- A paged table's loading bar sits over the header's lower edge and shows only once a read has taken 400 ms, so a live reload never moves the rows or flashes a bar.
- Work that runs longer than a click shows beside the control that started it: the control reads what is happening with a spinner, such as *Scanning*, and stays disabled until the work ends.
  A row whose work runs says so in its status, such as *Detecting* on Media, with how long it has run on the same line, so the row keeps its height.
  A root folder whose removal runs keeps its row on *Settings > Library* and *System > Status*, reading *Removing* where its last scan shows, until the removal ends, and *System > Tasks* lists the removal.
- A disabled action says why where the operator sees it: in its tooltip and its accessible name, and on a card, since a touch screen has no tooltip, in its label, such as *Detect Now Busy*.
- The navigation lists the running work, a library scan, a root folder removal and the running detections, above the activity messages.
  While the navigation is closed, as it is on a phone, the app bar sums the running work up in one line that opens it.
  A library scan a scan pause is stopping reads *Stopping the library scan* there, and *Stopping* in its row on *System > Tasks*, until it ends.
- An activity message says what ended or was saved, such as *Finished detecting Heat (1995).mkv.*, since the running work shows what has started.
  One about one file names it, and one about several counts them.
  A failure, such as a failed scan or a root folder removal that did not finish, is an error alert, and a path in a message wraps after its separators.

## Motion

A change a live update makes moves, so the operator sees what changed.
Each animation runs for 150 to 250 ms, its CSS sits in the `.razor.css` of the component that renders the element, and under `prefers-reduced-motion` it shows the end state at once.
Nothing moves on a page's first render.

- An item that joins a `MotionList` enters and an item that leaves stays for 200 ms to leave, in its place.
  A block, such as an activity message, running work or a health message, grows in as it fades in and shrinks away as it fades out, so the blocks around it slide rather than jump.
  A table row, with `TableBody`, fades in under a tint and fades out.
  The item's root element takes the entry's `Class`, and a block's root holds one child that the animation clips.
- A `StateLabel` whose state changes, a new icon or colour, fades its new state in.
  A value that switches between states, such as a Media row's *Detecting* and its status, goes through one label so the change shows.
- A row that a live reload brings onto a paged table fades in under the same tint as a `MotionList` row, through `PagedTable`; a change of view, such as a new sort or page, brings none.
- A table whose values change width while the page is open, such as those on Status, fixes its column widths with `table-layout: fixed` from tablet width, so a column keeps its place; on a phone each row is a card.

## Destructive actions

- An action that deletes, clears or discards something applies only after `IDialogService.ConfirmAsync` asks.
  The dialog names what will go, and its red confirm button repeats the action's name, such as *Clear History*.
- A removal staged until *Save*, such as a standard ratio or a path mapping, applies without asking, since *Save* is the confirmation.
- A destructive page or section action is an outlined red button with an icon.
  A destructive row action is a red icon button with an `aria-label` that names the row.
  A modal's *Remove* is a red text button left of *Cancel*.

## Success and failure

- Success shows through the page itself: *Save* disables, the saved values stay, and an activity message says what was saved.
- A field's error shows beneath the field, including a refusal the server returns for that field, such as an overlapping root folder, a name another player has, or a setting outside its bounds.
  A handler returns such a refusal as a `FieldError` that names the property, and the form clears it once that field changes.
  The field takes the refusal through `PageAction.FieldErrorAttributes`, and has no `Min` or `Max` for a bound its handler refuses, so the operator reads the refusal rather than seeing the value change.
  A refused setting behind *Show Advanced* turns *Show Advanced* on, so its error shows.
- A page action's failure shows as an error alert beneath the page header, through `PageHeader`'s `Error`, and clears on that action's next attempt.
  Each action runs through a `PageAction`, which holds whether it runs, its field errors and its failure, and a header over several actions shows each one's failure through `PageAction.ErrorOf`.
- A modal's failure, and its *Test* result, show as an alert above the modal's buttons.
- A section form's failure shows as an alert above that section's *Save*.
- A failed save, *Test* or page action keeps the form as the operator left it.
  An exception it throws reaches the operator as a failure, through `ToResultAsync`, which logs it, leads with what did not happen, such as *The settings were not saved.*, and ends with *System > Logs has the details.*
- A page that fails while it renders shows *Something Went Wrong*, what failed, *Try Again* and a link to *System > Logs*, and opening another page clears it.
- A lost connection shows the reconnect modal, which says in plain words what is happening and offers *Retry*, *Resume* or *Reload* as the state needs.
  An error that stops the page shows a bar at the bottom with *Reload*.
- A failure says what went wrong and what to do.
  A failed detection shows through `DetectionFailureExplanation`: what went wrong, what to do with a *Fix in* link when another page fixes it, and the tool's own words, without memory addresses or the file's path, as secondary text.
  A list shows only what went wrong, with the rest in its tooltip.
- An address that names nothing says so, with a title that names what is missing, such as *Page Not Found* or *Video File Not Found*, and a link to Media.
- Pages use no snackbars.

## Explaining and copying

- A state, source or confidence says what it means where the operator sees it: a tooltip on a list, such as a status on Media, and text beside it on a detail page, since a phone can't show a tooltip.
  An explanation says what a playback sends, such as *Measured from the picture. A playback sends this ratio.*
- A full path or file hash has a `CopyButton` at its end, which shows a check and *Copied* for two seconds.
- A path, a URL or a log message is a `PathText`, which wraps after a path separator or a dot between names rather than inside a word.
  It copies over plain http as well as https.

## Modals

- Every modal has a close button in its header, and Escape closes it.
- A modal takes focus on its first control as it opens, such as *Cancel* in a confirmation, keeps Tab inside it, and gives focus back to the control that opened it when it closes.
- A modal's footer holds *Show Advanced* and *Test* at the left, then *Remove*, *Cancel* and *Save* at the right, which wrap onto a line of their own on a phone.
- An integration modal puts its fields in an `IntegrationModal`, which holds its failure, its *Test* result and its footer.

## Formatting

Each kind of value has one formatter, and every page uses it.

| Value | Formatter | Example |
|---|---|---|
| Date and time | `DateTimeText`, through `DateTimeFormatter` | *Today 14:05*, *Wednesday 09:12*, *Mar 5 17:30*, *Dec 31 2025 08:00*, with the long form on hover, and *Today 14:05:07* with `WithSeconds` for a log entry |
| Duration | `TimeSpan.ToDisplayText` | *850 ms*, *3.2 s*, *4 min 5 s*, *2 h 3 min* |
| Count | `int.ToCountText` | *1,204*, or *1 video file* and *1,204 video files* with a noun |
| Ratio sent, snapped or set by an override | `double.ToAspectRatioText` | *2.40*, *2.355* |
| Raw ratio | `double.ToRawAspectRatioText` | *2.387*, followed by *raw* where it sits beneath the snapped ratio |
| Confidence | `double.ToPercentText` | *92%* |
| File size | `long.ToFileSizeText` | *2.3 GB* |

- With relative dates on, a date shows as *Today*, *Yesterday* or its weekday within the past week, and leaves out the year within the current year.
- A playback's outcome is a `PlaybackOutcomeText`: *2.40 detected*, or *Not sent:* and the reason.
- A delivery's outcome is a `DeliveryOutcomeText`: *Delivered in 32 ms*, *Failed:* and the error, or *Cancelled by a newer playback*.
  It follows its notifier's name or its time after a middle dot, such as *Sink · Delivered in 32 ms*.
- A player connection's state is a `PlayerConnectionStateText`, with when it entered the state and, while disconnected, when it retries.
  The player's modal shows it in an alert without an icon of its own, whose severity is the state's through `PlayerConnectionStateText.SeverityOf`.
- When a scan last ran is a `LastScanText`, which takes the scan: the time, *Failed*, *Cancelled* or *Interrupted* and the time, or *Never*.
- A table that pages uses `MudTablePager`, with the same page sizes everywhere, `TableView.PageSizes`, and keeps its first and last page buttons where the table shows cards.

## Look

- `DebarrTheme` holds both palettes and the type scale, and *Settings > UI* switches between light and dark.
  A new colour goes in the palette, never in a page's CSS.
- The type is Inter, served from `wwwroot/fonts`, with tabular figures so numbers line up in columns.
  The page title is `Typo.h4`, a section or card title `Typo.h6`, and buttons keep the case they are written in.
- The app bar is charcoal in both themes, with the logo and *Debarr* at the left.
  The logo, crop corners around a picture band, is also the favicon.
  *Detect Now*'s icon is focus corners, so the action echoes the logo.
- Amber, the primary colour, marks actions, the current page, focus and selection, and nothing else.
  Focus from the keyboard is an amber ring on every control, and an outlined field's amber border.
- A link is text with an amber underline, a path beneath a name is secondary text, and neither takes a state's colour.
- Ratios, confidences and times use tabular figures, through the `numeric` class or `DateTimeText`, so they line up in columns.
- A value an environment variable sets shows a lock and a dashed, shaded box, through `HostSettingField`.

## Colour and state

A state shows through a `StateLabel`: its icon, then its text, both in the state's colour, so colour is never the only signal.
Every text, placeholder and state icon meets WCAG AA contrast against what it sits on, in both themes: 4.5:1 for text, and 3:1 for large text and icons.
The `debarr-live-check` skill's `audit.py` measures it.
Each state has one colour and one icon on every page.
A kind of state with all three takes its word, icon and colour from one table, such as `VideoFileStatusText.DisplayOf`, which the table below lists.

| State | Colour | Icon | Component |
|---|---|---|---|
| Detected, a ratio sent from a detection, delivered, connected, enabled, *Current* | Success | check circle, or link for a connection | `VideoFileStatusText`, `PlaybackOutcomeText`, `DeliveryOutcomeText`, `PlayerConnectionStateText` |
| Manual, a ratio sent from an override | Tertiary | edit | `VideoFileStatusText`, `PlaybackOutcomeText` |
| From File, a ratio sent from the file or the player | Info | inventory, or cast for the player | `VideoFileStatusText`, `PlaybackOutcomeText` |
| Connecting, detecting, a running library scan, *Removing* while a root folder removal runs | Info | sync | `PlayerConnectionStateText`, Media's status, *System > Tasks*, `RootFolderRemovalText` |
| Failed: a detection, a delivery, a scan, a root folder, a failed count above zero, disconnected | Error | error outline, or link off for a connection | `VideoFileStatusText`, `DeliveryOutcomeText`, `LastScanText`, `PlayerConnectionStateText` |
| Pending, not sent, cancelled, an interrupted library scan, disabled, not connected | Secondary text | hourglass, block, cancel, power off, pause, link off | `VideoFileStatusText`, `PlaybackOutcomeText`, `DeliveryOutcomeText`, `LastScanText` |

- Warning is for alerts, such as a current result that a later detection failed to replace, and for a log entry at the Warning level.
- A health message is an alert, Error or Warning by its severity, with what is wrong, then a secondary line with when it began and a link that names the page that fixes it, such as *Fix in Settings > Library*.
  The badge on the System navigation item counts the health messages, red when any is an error and in the warning colour otherwise, and says the count by severity to a screen reader.
- A count shows a state's colour only when it is above zero: *3 failed* is red, *0 failed* is plain.
- An error's detail beneath a state that already says *Failed* is secondary text.

## Words

- Titles, section titles, buttons, labels, table headers, tabs and tooltips on icon buttons use Title Case: *Detect Now*, *Re-detect All*, *Show Advanced*.
- Help, descriptions and messages are sentences in a plain voice, and say what happens rather than how the code does it.
- A cell value that names an action keeps the action's casing, such as *Detect Now* in a detection's origin.

- The UI and the code use the same words, the *Names* table in [rewrite-plan.md](rewrite-plan.md).
  A term an operator wouldn't understand changes on screen and in the code together.
- The terms on screen for Debarr's concepts:

| Concept | Term on screen |
|---|---|
| A folder the library is read from | *Root Folder*, never a bare *root* |
| Picking a folder from the folders on Debarr's filesystem | *Choose Folder*, and *Choose* in its modal |
| Reading every root folder | *Library Scan*, and *Scan Now* to start one |
| Reading one folder that changed | *Folder scan*, in activity messages |
| How often a library scan runs | *Scan Interval* |
| Scanning folders as their files change | *Watch Folders* |
| Sending nothing for a file | *Don't Send*, and *Not sent* in a playback's outcome |
| A status or source set by an override | *Manual* |
| A ratio the file states | *From File* |
| A ratio measured from the picture | *Detected* |
| The ratios a measured ratio is matched to | *Standard Ratios* |
| How far a ratio may be from a standard ratio | *Match Tolerance* |
| A standard ratio whose files have their picture measured | *Check Picture* |
| The detection whose result playback uses | *Current* |
| What started a detection | *Started By*: *Queue*, *Detect Now* or *Standard Ratios Change* |
| How many detections run at once | *Simultaneous Detections* |
| Runtime left out at each end of a file | *Skip Start and End* |
| cropdetect's black threshold | *Black Level (SDR)*, *Black Level (HDR)* |
| The settings for measuring the picture | *Picture Measurement* |
| A player's and Debarr's side of a path mapping | *Player Path*, *Local Path* |
| A video file with no path, kept for when it returns | *Archived* |
| When a scan first found a video file or a file path | *First Seen* |
| Taking a player, a notifier or a root folder out of Debarr | *Remove*, and *Removing* while a root folder removal runs |

- The notification payload keeps its own words, since automation reads them: `source` is `manual`, `detected`, `container` or `player`.
