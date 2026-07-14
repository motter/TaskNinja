# Changelog

## [1.3.1] — 2026-07-14

### Fixed — v1.3.0 failed to build (no release was produced)
Two mistakes in the v1.3.0 tag, both mine:
- `AllTagsInUse` referenced a `Tasks` collection that doesn't exist —
  the view model's collection is `AllTasks`.
- The tag chip used `StringFormat=#{0}`; inside a XAML markup
  extension the inner `{0}` is parsed as a NESTED extension and fails
  to compile. Replaced with two plain TextBlocks.

v1.3.0's features (important flag, tags + tag filtering, visible
attachment removal) ship here.

## [1.3.0] — 2026-07-14

### Added — mark tasks important
- Right-click a task → **❗ Toggle important**, or tick the checkbox
  in the editor (next to Status).
- Important tasks get a warm amber tint, an amber left edge, and an
  ❗ before the title — visible at a glance in any list.
- They also float to the top of the active group in date sort. In
  **Manual** sort they stay put: you arranged that order by hand and
  the app shouldn't fight you. The tint and ❗ still mark them.

### Added — tags, and filtering by them
- Tasks have a **Tags** field in the editor (comma-separated). Inline
  **#hashtags** typed into the title still work and merge with them —
  both styles feed one list, no second source of truth.
- Tags show as chips under the task title. **Click a chip to filter
  the whole list to that tag** — across every bucket, because a tag
  like "audit" is a cross-cutting concern and intersecting it with
  the bucket filter would usually return nothing and look broken.
  (Selecting a tag switches the bucket dropdown to "All buckets" so
  what you see matches what's filtered.)
- The active tag appears as an amber chip in the filter row; click it
  to clear. Tags are also matched by the normal search box.

### Added — remove attachments without guessing
- Attachment thumbnails now carry a visible **✕ badge**. Removal used
  to be right-click-only, i.e. a destructive action nobody could
  find. Right-click still works; the tooltip now mentions both.

## [1.2.0] — 2026-07-14

### Fixed — task editor crushed the Notes box to nothing
Notes was the only Star-sized row in the editor's layout, so every
Auto row above it — activity log, checklist, attachments — claimed
its space first. On a task with a few activity entries and some
attachments, Notes got ZERO height and vanished; the only way to see
it was to manually drag the window bigger.

Now: the whole form sizes naturally and lives in a **ScrollViewer**,
so nothing can be squeezed out of existence — if the content doesn't
fit, you scroll. The Notes box holds a guaranteed minimum height
(and scrolls internally once you type past it). Save/Cancel are
pinned below the scroll region so they're always reachable without
scrolling to the bottom. Default window is a bit taller too.

### Added — click a task in the weekly report to open it
Task rows in the summary dashboard (both the completed-this-week
list and the drill-down lists) are now clickable: hover highlights
the row, click opens that task in the detail editor. No more
hunting for it in the main list.

## [1.1.1] — 2026-07-13

### Fixed / hardened — self-update reliability (parity with ClipNinja v2.8.1)
- Update dialogs are owned by the main window so they can't be
  buried under topmost windows.
- Swap script delay uses ping instead of `timeout` (which can die
  instantly without an interactive console, exhausting the retry
  loop in milliseconds).
- The script writes a swap log next to the exe; a failed swap is
  surfaced in the status bar on next launch.
- Exiting for the swap: graceful shutdown with a 3-second hard-exit
  fallback so the file lock always releases.

## [1.1.0] — 2026-07-12

### Added — in-app updates via GitHub Releases
- Same self-update pipeline as ClipNinja v2.7.x: **Settings →
  Updates** (repo field, startup-check toggle, "Check for updates
  now" with release notes and one-click install) and **tray → Check
  for updates...**. Silent startup check ~5s after launch shows a
  status-bar hint when a newer release exists — never a popup.
- The swap keeps the outgoing exe as `TaskNinja.exe.bak` for instant
  local rollback.
- Update repo defaults to **motter/TaskNinja** so fresh installs are
  pre-wired.

### Added — GitHub repo scaffolding
- `.github/workflows/release.yml`: push a v* tag and GitHub Actions
  publishes the exe, zips it as TaskNinja-win-x64.zip, and attaches
  it to an auto-created Release.
- `RELEASING.md`: one-time repo setup + the three-command release
  loop, plus the rules that keep the updater happy.

## [1.0.29] — 2026-07-08

### Added — Done view shows and sorts by completion date
- In the Done filter, each row's date chip now shows WHEN the task
  was completed ("✓ Today", "✓ Jul 3") in the sage tone, instead of
  the stale due date. Applies anywhere a done task renders.
- Done view sorts most-recently-completed first — a reverse-
  chronological "what have I finished" log. (Tasks completed before
  v1.0.16, which lack a completion timestamp, sink to the bottom.)

### Added — attachments open from the editor
- Clicking an attachment thumbnail in the task editor opens it
  full-size in your default viewer — same as the preview popup.
  Right-click still removes. Tooltip updated to say both.

### Added — status milestones visible in preview + editor
- The preview footer now shows the full lifecycle: 📅 Created,
  ▶ Started (first move to In progress), ✅ Done — each with date
  and time. Milestones that haven't happened are omitted.
- The editor footer (left of Save/Cancel) shows the same trio
  compactly: "Created Jun 12 • Started Jul 1 • Done Jul 8". Full
  per-transition history stays in the Activity section.

### Added — copy task from the preview
- New 📋 button next to the preview title copies the task as plain
  text: title, notes body, and lifecycle dates. Button flashes ✓ to
  confirm. (The body stays a non-selectable TextBlock so URLs remain
  clickable — the button is the copy path.)

## [1.0.28] — 2026-07-08

### Changed
- Filter pills get 3px vertical margin so the wrapped second row
  (narrow windows) has breathing room instead of touching the first.

## [1.0.27] — 2026-07-08

### Fixed — Done filter pill was clipped out of view
The v1.0.25 "Scheduled" pill made the filter row six pills wide,
which no longer fits the default 460px window — and the row was a
non-wrapping StackPanel, so the **Done pill was silently pushed off
the right edge**. It looked like the Done filter was removed; it was
just invisible. The pill row is now a WrapPanel, so at narrow widths
the pills flow onto a second line and every filter stays reachable.

Bucket + Done still combine as before: pick the bucket in the
dropdown, then click Done — you get that bucket's completed tasks.

### Fixed — tasks completed early (while deferred) vanished entirely
If you completed a task from the Scheduled view BEFORE its "don't
show until" date arrived, it was Done but still technically deferred
— and the deferred-hiding rule hid it from every view including
Done. Completed tasks now bypass the deferred check: a Done task's
visibility is governed by the Done filter alone.

## [1.0.26] — 2026-07-01

### Fixed — Modal dialog frozen-app bug (real fix this time)

Previous versions had a partial fix (surfacing dialogs on tray click),
but the underlying trap was still there: **the editor and other modal
dialogs had a default Windows titlebar with a minimize button.
Clicking it minimized the dialog to nowhere** (ShowInTaskbar=false
means no taskbar entry to click back), leaving the main app blocked
by an invisible modal. Clicking the taskbar's main-window entry
didn't route through the "surface child windows" helper either, so
recovery was manual (right-click tray → Exit).

Two-layer fix:

1. **Prevention** — TaskDetailEditor, BucketManagerDialog, and
   InputPrompt now use `WindowStyle=ToolWindow`, which shows a
   compact titlebar with only a close button. No minimize, so the
   trap is gone entirely.
2. **Recovery** — Main window's `StateChanged` event now surfaces
   any open child windows whenever the main window returns to
   Normal state. So even for dialogs that stay minimize-able
   (should any slip through, or if a modal is behind a taskbar
   restore), clicking the taskbar entry brings them all forward.

### Changed — Activity expander auto-expands when there's real history

Fresh tasks (Created only, no state changes) still show the expander
collapsed — nothing interesting to see. Tasks with any state
transitions (moved through Open → In progress, marked Done, etc.)
now show the log expanded by default. Surfaces the completion
comment + timestamps without requiring the user to know to click
the ▸ chevron.

## [1.0.25] — 2026-06-30

### Changed — Deferred-task visibility model

Tasks with a future "Don't show until" (StartDate) are now hidden from
ALL normal views — buckets, "All", Today, Week, Overdue, NoDate. They
live exclusively under a new **Scheduled** filter, where you can review
what's queued up and edit dates if needed. Previously "All" showed
deferred tasks, which defeated the point of saying "don't show until X".

### Added — Auto-deferral for spawned recurring tasks

When a recurring task is marked Done, the next instance still spawns
immediately but is now born with an auto-set `StartDate` so it stays
hidden until N days before due. Defaults per pattern:

- **Daily**     — 0 days (show immediately; daily intervals don't have
                  room for a hide window)
- **Weekly**    — 2 days before due
- **Monthly**   — 7 days before due
- **Yearly**    — 14 days before due

So if you finish the water-softener task on Jun 30 with a 6-week
interval, the next instance spawns due Aug 11 but is hidden until
Aug 4. You won't see it cluttering your bucket for 5 weeks; it
shows up the week of.

### Added — "Hide until closer to due" control in the completion popup

The completion popup (shown when marking a recurring task Done) now
includes:

- "Hide until closer to due" checkbox (pre-checked when the auto-defer
  applies)
- A date picker that defaults to the auto-computed visibility date

You can edit the date, uncheck the box to show immediately, or leave
the defaults and just click Save. The smart-clamp message still
appears when the proposed next-due was bumped forward.

### Added — Scheduled filter pill

New "Scheduled" pill in the filter row, between Overdue and Done.
Shows only deferred tasks (future StartDate, not Done, not archived).
Respects the active bucket — combining Scheduled + Home bucket shows
only Home-bucket deferred tasks. Hover tooltip explains the model.

### Migration

Existing recurring tasks that were already spawned (visible right now)
stay visible. The auto-defer only applies to NEW spawns from this
version onward. If you want to defer something that's currently
visible, edit it and set "Don't show until" manually.

## [1.0.24] — 2026-06-30

### Added — Completion popup

- **Comment + "completed by" prompt when marking a task Done.** A
  small popup opens with two optional fields: who completed it
  (combo box pre-filled with the task's responsible person if
  set, dropdown of known names) and a comment text area. Both
  can be left blank — hit Save (or "Skip details") to record
  just the timestamp. Stored as a `CompletionRecord` on the task.
- **Combined with recurrence scheduling.** Recurring tasks used
  to show a separate "Schedule next" popup; now both concerns
  fit in one popup. Recurring section appears below the comment
  fields with the smart-clamped next-due date, an editable
  DatePicker, and a "Schedule next occurrence" checkbox you can
  uncheck to end the recurrence. (Previous v1.0.21
  `RecurrenceConfirmDialog` is superseded by this combined
  `CompletionDialog`.)
- **All four completion paths consistent**: preview popup state
  picker, row glyph picker, and right-click → Set Done all show
  the new popup. The daily summary's per-row ✓ button stays
  popup-free (it's the rapid-triage context) but still records
  a bare `CompletionRecord` so the data is consistent.
- **Completion details appear in the Activity expander.** Under
  each "→ Done" state-change entry, the comment and completed-by
  show as an indented sub-row so you can review past completions
  in context. Old completions without comments (digest-✓ or
  pre-v1.0.24 tasks) just show the bare state-change line.

### Fixed
- **Activity expander said "(0 changes)" for new tasks** — ugly
  and misleading since the Created entry always shows. Header
  now counts the Created entry too: "Activity (1 entry)" for a
  fresh task, "Activity (4 entries)" once you've moved it
  around.

### Compatibility note
- Tasks completed before v1.0.24 don't have `CompletionRecord`
  entries — the Activity expander shows their state-change
  history as it always did, just without the new comment
  sub-rows. No migration needed; old data is fine.

## [1.0.23] — 2026-06-30

### Fixed
- **CS0411 build error from v1.0.22**: `SubtaskDoneCount` in
  `TaskItem.cs` used `Subtasks.Count(s => s.IsDone)` but the file
  didn't import `System.Linq`. The compiler tried to bind to
  `MemoryExtensions.Count<T>(Span<T>, T)` instead of LINQ's
  `Enumerable.Count<T>(IEnumerable<T>, Func<T,bool>)` and failed.
  Same class of bug as v1.0.13's HashSet issue — I added a new
  LINQ usage to a file that previously didn't need the import.
  Adding `dotnet build` to my pre-package routine would catch
  these, but I can't run that in my sandbox.
- **CS8629 nullable warning** in `RecurrenceConfirmDialog.cs`:
  reworked the smart-clamp explanation block to use a pattern
  match (`if (ComputeNaiveNext(completed) is { } naiveDate ...)`)
  instead of `naive.HasValue && naive.Value != ...`. The analyzer
  loses null-tracking across that boundary; pattern matching
  preserves it. Same behavior, no warning.

## [1.0.22] — 2026-06-30

### Added — Snooze presets + arbitrary date picker

- **Expanded snooze presets**: Tomorrow, In 2 days, This weekend
  (Saturday), In 1 week, In 2 weeks. Available in two places:
  - **Daily summary**: the per-row 💤 button now opens a dropdown
    menu with all presets (consolidated from the previous two
    separate Tmrw/Wknd buttons — one button with a menu is cleaner
    than 5+ buttons crammed in the row).
  - **Main list right-click → 💤 Snooze**: expanded submenu with
    the same options.
- **"Pick a date..." in both menus** opens a small modal with a
  DatePicker so you can defer to any arbitrary date. Defaults to
  one day after the current due (or tomorrow if no due). Single
  shared dialog (`DateSnoozePicker`) used by both entry points so
  the UX is identical.

### Added — Subtasks / checklist

- **Checklist section in the detail editor.** Add short items
  (just text + checkbox) for tracking minor progress within a
  parent task — e.g. "do this thing for 5 projects" with one
  subtask per project. Each row has a checkbox, editable title,
  and a delete-× button. Press Enter in the add input (or click
  the + button) to commit a new subtask. Header shows running
  progress: "Checklist (2 / 5)".
- **Progress chip in the hover preview** — tasks with subtasks
  show a "✓ 3 / 5" chip in the meta row. Color shifts to accent
  (green) when all subtasks are done, even if the parent task
  isn't marked Done yet.
- **Intentionally minimal subtask model** — just title + IsDone +
  CompletedAt. No due dates, recurrence, or any of the other
  TaskItem complexity. If a "subtask" needs more weight, it
  should be a real top-level task instead.
- **Legacy compatibility**: tasks created before v1.0.22 deserialize
  with an empty subtask list. No migration needed.

## [1.0.21] — 2026-06-28

### Added — Recurrence improvements

- **"Every N units" intervals in the recurrence editor.** A new numeric
  input next to the recurrence dropdown lets you say "every 6 weeks",
  "every 2 months", "every 3 days", etc. The field is disabled when
  recurrence is None and auto-resets to 1 when turning recurrence on.
  Valid range 1–999; bad input falls back to 1.
- **Smart-clamp on spawn.** When you mark a recurring task Done, the
  next instance is no longer born already-overdue. If the naive
  next-due (previous-due + interval) is in the past — i.e. you fell
  behind on the task — the spawn date shifts forward to today +
  interval. A weekly task you forgot about for 3 weeks now gives you
  one fresh week to do the next one, instead of three overdue
  instances stacking up.
- **Confirmation popup on recurring-task completion.** Marking a
  recurring task Done now opens a small dialog showing the proposed
  next-due date with three options:
    1. **Schedule next** — accept the date as shown (default action)
    2. **Edit the date** — pick any date with the date picker
    3. **Skip — don't repeat** — end the recurrence here (this is
       the last occurrence)
  When the smart-clamp kicked in, the popup explains why: "Original
  schedule would have been Jan 15, but that's already past — bumping
  forward."
- **All three completion paths route through the same flow** —
  marking Done from the preview popup, the row-glyph picker, or the
  right-click → Set Done menu all show the confirmation dialog.

## [1.0.20] — 2026-06-28

### Pre-test cleanup pass — no new features, just polish

- **Drill order bug**: in the weekly report, if a week had zero new
  activity (no created, completed, or in-progress) but DID have
  carryover open tasks, the "Nothing happened this week" empty-state
  would short-circuit before the drill view could render. Result:
  clicking "Still open" did nothing. Reordered the checks so drill
  mode is honored even on otherwise-empty weeks. Empty-state now
  also includes a hint pointing the user at the "Still open" card
  when carryover exists.
- **Removed two dead helper methods** in `TaskDetailEditor.cs`
  (`MakeDarkTextBox`, `ParseDateField`) — leftovers from an earlier
  iteration when date fields were text boxes. The editor uses
  `DatePicker` now, so these were never called. 31 lines lighter.

## [1.0.19] — 2026-06-26

### Added
- **📊 Reports button on the main toolbar.** Opens a small menu with
  "Daily summary (Ctrl+D)", "Weekly report (Ctrl+R)", and
  "Notification settings…". Same shortcuts still work, but now you
  can find these features without going hunting in the system tray
  menu. Tray menu access stays as a secondary path.
- **Clickable summary cards in the weekly report.** Each of the four
  counter cards (Created, Completed, In-progress, Still open) is now
  clickable. Clicking one drills into the list of tasks behind that
  number — shows the actual task titles with status glyphs, sorted
  chronologically, with timestamps appropriate to the category
  (creation time for Created/Open, completion time for Completed,
  in-progress transition time for In-progress). The active card is
  highlighted with an accent border. Click the same card again to
  collapse back to the chart overview.
- **Bigger drag region on the weekly report** — the nav bar (where
  the week label and prev/next buttons live) is now also a drag
  handle, in addition to the header. The prev/next buttons still
  handle their own clicks; everywhere else on the bar moves the
  window.

## [1.0.18] — 2026-06-26

### Fixed
- **Daily summary popup wouldn't move.** WindowStyle=None hides the
  default titlebar (so we can paint our own dark chrome) but also
  removes the drag-from-title gesture. Wired the header band as a
  drag handle: cursor turns to the four-way arrow on hover, and
  click-drag on it moves the window. Same treatment applied to the
  Weekly report and Settings dialogs (same root cause in each).
- **"Remind me in 2h" was re-popping within 60 seconds.** The
  notification service had two trigger paths — a remind-at timestamp
  AND a time-of-day check — and "Remind me in 2h" only set the
  former. So 60s later, the time-of-day check fired ("it's after
  8 AM and we haven't shown the digest today"). Fix: RemindAt now
  ALSO marks today as shown, so only the explicit remind-at
  timestamp can re-pop. And the remind-at trigger now clears
  itself after firing, in case the user keeps the popup open
  across timer ticks.

### Added
- **⚙ settings button on the daily summary footer** so the user can
  jump straight to notification settings (change time, disable, etc.)
  without hunting through the tray menu. Tooltip explains.
- **Ctrl+D hotkey** — show the daily summary on demand. Pairs with
  Ctrl+R for the weekly report.
- **Three ways to access the daily summary**:
    1. Automatically at the configured time (default 08:00)
    2. Right-click tray icon → "Show daily summary"
    3. Press Ctrl+D from the main window
- **Three ways to change the time / disable notifications**:
    1. Right-click tray icon → "Settings…"
    2. ⚙ button on the daily summary footer
    3. (Same dialog reached either way)

## [1.0.17] — 2026-06-25

### Added — Weekly activity report

- **New tray menu item: "Weekly report"** opens a window with a
  per-week summary of TaskNinja activity. Also accessible via
  **Ctrl+R** while the main window is focused.
- **Headline counter cards** show four key metrics at the top:
  Created, Completed, In-progress transitions, and Still open at
  week's end.
- **Daily activity chart**: seven rows (Mon–Sun) with two bars per
  day showing tasks created (sky-blue) vs tasks completed (amber),
  scaled to the highest single-day value so the chart reads well
  whether you closed 2 tasks or 20.
- **By-bucket breakdown**: same created/completed bar pattern but
  grouped by bucket, sorted with the most-active bucket on top.
  Only shown when you have 2+ buckets with activity.
- **Completed-this-week list**: each task that finished in this
  week, with the precise completion time ("Tue 9:15 AM"). Tasks
  listed chronologically through the week.
- **Week navigator**: ◀ Previous / Next ▶ buttons to browse past
  weeks. "Next" is disabled when you're on the current week (no
  future-week data exists).
- **Powered by the v1.0.16 StateHistory data** for accurate
  completion-time tracking, with a legacy fallback to CompletedAt
  for tasks that finished before StateHistory existed.

## [1.0.16] — 2026-06-25

### Fixed
- **Harvey-ball state picker required holding the mouse button** — the
  picker popup was opening on `MouseLeftButtonDown`, but the popup's
  `StaysOpen=false` interpreted the matching `MouseLeftButtonUp` as a
  click outside itself and instantly closed. Switched to
  `MouseLeftButtonUp` so the popup opens AFTER the click is complete
  and sticks around for proper interaction.

- **Modal task editor frozen when window minimized.** Opening a task
  editor (or settings / bucket manager) creates a modal child window.
  Minimizing the main window left the modal alive but invisible /
  hard to find — blocking interaction with the main window. The only
  recovery was right-clicking → close. Now `ShowWindowFromTray` and
  the Ctrl+Shift+T hotkey surface ALL open windows (the main one and
  any child dialogs), bringing the modal forward so the user can see
  and dismiss it.

- **Two taskbar icons (still)** — moved `TrySetWindowIcon()` from
  `OnWindow_Loaded` to the constructor so the icon is set BEFORE the
  window is first shown. Setting it later was causing a brief
  icon-swap during startup that may have contributed to Windows
  registering the window with the wrong AppID grouping.

  **Also new**: `Fix-TaskbarPin.cmd` script in the source root.
  Double-click it to clean up any stale pre-v1.0.14 pin and get
  on-screen instructions for re-pinning the running app correctly.
  Should resolve the persistent dual-icon issue.

### Added — Status change history

- **Activity log on every task.** Every state change (Open → In
  progress → Done, regardless of which UI path triggered it — state
  picker, daily digest ✓, right-click menu, etc.) is now recorded
  with a timestamp. The data lives on `TaskItem.StateHistory` and
  persists to disk.
- **"Activity" expander in the detail editor** — collapsed by
  default, expands to show "Jun 24, 3:15 PM — Open → In progress"
  rows. The "Created" timestamp appears as the first entry of every
  task's log, even tasks created before this feature existed.
- **Foundation for future reporting** — the data is now there so
  future features like "weekly activity reports" (how many tasks
  created/completed this week) have something real to display.

## [1.0.15] — 2026-06-24

### Fixed
- **Bucket-change from preview popup no longer crashes the popup.**
  When the user clicked the bucket chip, the popup would briefly show
  the menu of buckets and then vanish, leaving them unable to actually
  pick one. Root cause: the ContextMenu's visual tree lives outside
  the popup window, so the cursor moving into the menu triggered the
  popup's `MouseLeave` → auto-hide timer. Added an `_menuOpen` flag
  that suppresses the hide while a child menu is open, and rewires
  the menu's `Opened`/`Closed` events to set/clear it correctly.
- **State-glyph click on a row now opens a picker instead of cycling
  silently.** Clicking the ○/◐/● glyph used to advance state by one
  on every click — easy to over-click and accidentally mark a task
  Done. Now a small popup anchored to the glyph shows the three
  states as labeled buttons. Click a state to apply, or click
  outside to cancel. Uses the existing `TaskStatePicker` widget so
  the UX is identical to the preview popup's state row.

### Polish
- **Bucket chip in the preview hides when there's only one bucket.**
  For users who haven't set up multiple buckets, a "📂 Tasks" chip
  alone was visual noise — its value is "see and change which bucket
  this is in", which only matters when there's somewhere else to
  move it. Now the chip only appears when 2+ buckets exist.

## [1.0.14] — 2026-06-24

### Fixed
- **Two taskbar icons** when launching from a pinned shortcut. Windows
  groups taskbar entries by `AppUserModelID`; without one set, the
  pinned shortcut and the running process were treated as separate
  apps, producing the duplicate icon. Added a
  `SetCurrentProcessExplicitAppUserModelID("Anthropic.TaskNinja")`
  call at process startup so they unify. (Existing pins may need to
  be unpinned and re-pinned once after this update for Windows to
  pick up the new ID.)
- **Notes / body scrollbar missing in the detail editor.** The body
  TextBox was hosted in a `StackPanel`, which sizes to content — so
  even though the parent Grid row was Star-sized (and the TextBox had
  `VerticalScrollBarVisibility=Auto`), the StackPanel collapsed the
  TextBox to its 120px MinHeight. Long bodies just overflowed
  silently. Switched the host to a `Grid` so the TextBox stretches
  to fill the row, and the scrollbar activates when content overflows.
- **Notes / body scrollbar missing in the hover preview.** The body
  TextBlock and its outer ScrollViewer both had `MaxHeight=500`. Because
  the TextBlock clipped at 500, it never overflowed the ScrollViewer,
  so the ScrollViewer's scrollbar never had anything to scroll.
  Removed the MaxHeight from the TextBlock — it can now grow as tall
  as the wrapped text needs, and the ScrollViewer caps the visible
  area at 500 with a working scrollbar.
- **Tidied `Hide()` paths** (X button + Ctrl+Shift+T toggle) to clear
  `ShowInTaskbar=false` while hidden, preventing ghost taskbar slots.

## [1.0.13] — 2026-06-24

### Fixed
- **CS0246 build error from v1.0.12**: `The type or namespace name
  'HashSet<>' could not be found`. The v1.0.11 drop-handler code used
  `HashSet<string>` for the image-extension set, but `MainWindow.xaml.cs`
  was importing `System.Collections.Generic.List<>` only inline (fully
  qualified) and never had the namespace open at the file level. Added
  `using System.Collections.Generic;` at the top of the file.

  This was masked in earlier versions because the only generic
  collections used in that file were fully qualified (`List<>`) — the
  unqualified `HashSet<>` slipped in with the v1.0.11 drop handler and
  v1.0.12 didn't catch it because I didn't actually build before
  packaging.

## [1.0.12] — 2026-06-24

### Added — Daily summary notifications

- **One daily popup, not N per-task popups.** Each morning (default
  08:00, configurable) TaskNinja shows a single window with two
  sections: **⚠️ Overdue** and **📅 Due today**. Each task row has
  three quick-action buttons: **✓** (mark done), **💤 Tmrw** (snooze
  to tomorrow), **💤 Wknd** (snooze to next Saturday).
- **Two footer buttons**: *Remind me in 2h* re-pops the digest two
  hours later; *Got it for today* dismisses it until tomorrow morning.
  The "last shown" date persists across restarts so the digest doesn't
  re-pop the same day you've already dismissed.
- **Empty-state celebration**: "🎉 You're all caught up. Nothing
  overdue, nothing due today." Showing a celebration is more
  rewarding than just silently not popping.
- **Tray menu access**: right-click the tray icon → "Show daily
  summary" pops it on demand. User-initiated shows DON'T update the
  "last shown" date — the automatic morning pop still happens.
- **Tray tooltip badge**: hover the tray icon to see counts at a
  glance — "TaskNinja — 3 overdue, 7 open" or "TaskNinja — all clear".
- **Settings dialog**: right-click the tray → "Settings…" opens a
  modal with the notification enable toggle and time-of-day (24-hour
  HH:mm). Validation rejects bad input rather than silently disabling
  notifications.

### Internal changes
- New `NotificationService` (1-minute tick, evaluates whether to pop).
- New `DailyDigestPopup` view with empty-state, sectioned task list,
  and per-task quick actions.
- New `SettingsDialog` view (notifications section only for now;
  designed to grow).
- New `AppSettings` fields: `NotificationsEnabled`, `DailyDigestTime`,
  `LastDigestShownDate`, `DigestRemindAt`.
- New `MainViewModel.OverdueCount` property + property-changed
  notifications, used by the tray tooltip.

### Deferred to a later version
- **Per-app notification sounds** — would need a small wav asset.
- **Outlook .msg parsing** for deeper task extraction (sender, attachments).
- **Per-task notification scheduling** — currently only the daily
  digest fires; we could add "remind me at X" per task later, but the
  digest covers 90% of the use cases without notification fatigue.

## [1.0.11] — 2026-06-23

### Added
- **Bucket picker in quick-add.** When creating a new task, you can now
  choose which bucket it goes into. Defaults to the active bucket
  (or default bucket if "All buckets" is selected).
- **Bucket displayed and editable in the preview popup.** A clickable
  amber "📂 BucketName" chip in the meta row. Click it to open a menu
  of all buckets and move the task; current bucket is checkmarked and
  disabled to avoid confusion.
- **Bucket combo in the detail editor.** Row 1, right under the title.
  Editing this field on save persists the bucket change.
- **Right-click → Set status submenu** at the top of the task context
  menu. Three items: ○ Open / ◐ In progress / ● Done. Marking Done
  spawns the next recurrence if the task is recurring.
- **Drag-and-drop to create tasks.** Drop ANY of these onto the window
  to create a task automatically:
    • Outlook email (drag from your inbox) — subject becomes the title,
      body becomes the task body
    • Files from Explorer — file name becomes the title, path goes in
      the body for reference
    • Image files (.png, .jpg, .jpeg, .gif, .bmp, .webp) — attached to
      the task with full pixel dimensions preserved
    • **Raw clipboard bitmaps** — dragging a screenshot directly from
      Snipping Tool, ShareX, or similar (without saving to disk first)
      now creates a task with the bitmap attached and a "Screenshot
      W×H" placeholder title.
    • Plain text from anywhere — first line is the title, rest is body
  A semi-transparent overlay appears during drag-over so you know the
  drop will be accepted.

### Fixed
- **Names dropdown didn't open when clicked.** The autocomplete-style
  "Responsible person" combo in the detail editor only dropped down
  if you clicked the tiny arrow on the right — clicking the textbox
  area (where users actually look) did nothing. Wired
  `PreviewMouseLeftButtonDown` and `GotKeyboardFocus` so clicking
  ANYWHERE on the combo opens the dropdown (when there are items
  to show). Typing still works normally — once the dropdown is open,
  you can keep typing to filter, or click an item to pick it.

## [1.0.10] — 2026-06-23

### Fixed
- **Build error MC4109 in v1.0.9**: `Cannot find the Template Property
  'IsSelected' on the type CalendarButton`. I added an IsSelected
  trigger to the new `CalendarButton` style, but `IsSelected` only
  exists on `CalendarDayButton` (the individual day cells). The
  month/year navigation `CalendarButton` doesn't have a persistent
  selected state — it just navigates the calendar view on click.
  Replaced the bad trigger with `IsPressed` for click feedback.

## [1.0.9] — 2026-06-23

### Added
- **DatePicker is back, themed for dark mode.** Previously yanked
  because WPF's default calendar popup uses light system colors. New
  `DarkDatePickerStyle` provides a full custom template, AND default
  Calendar / CalendarDayButton / CalendarButton styles ensure the
  popup contents (the actual calendar grid) inherit the dark
  palette automatically. Today's date is outlined in amber; the
  selected date is filled in amber. Click the 📅 icon to drop down.
- **Free-text date entry still works too.** Type "today", "tomorrow",
  weekday names like "fri" or "monday", or YYYY-MM-DD into the
  picker's text portion — `CommitQuickAdd` parses the text first
  before falling back to the calendar selection.
- **Field labels above each input in the quick-add bar**: "Task
  title" / "Due date" / "Responsible person". The previous unlabeled
  layout was ambiguous about what each box was for.
- DatePickers restored in the detail editor too (Due date + Don't
  show until), with the same dark theming.

## [1.0.8] — 2026-06-23

### Fixed
- **Zombie-mutex lockout from prior crashed launches.** The earlier
  v1.0.4/v1.0.5 launches crashed inside `OnFilter_Changed` with a
  `NullReferenceException`. But by the time they crashed, the OS had
  already created the single-instance mutex AND the .NET runtime kept
  it from being released cleanly. Subsequent launches (even after the
  NullReferenceException itself was fixed in v1.0.7) saw a "held"
  mutex, tried to signal the "other instance" via a named event,
  found the event didn't exist (because that other process died
  before creating it), and silently shut down.

  Final fix: if signaling the existing instance fails with
  `WaitHandleCannotBeOpenedException`, treat the lock as stale —
  the "owner" is a zombie, not a live primary instance — and proceed
  to launch as a fresh primary. Same for any other signal-time
  exception. This way a crashed prior instance can never lock the
  user out indefinitely.

## [1.0.7] — 2026-06-23

### Fixed
- **App still failed to launch on v1.0.4 / v1.0.5 / v1.0.6.** Real
  root cause this time (the icon & mutex fixes were both red herrings
  masked by the orphan-mutex symptom): `OnFilter_Changed` fires
  during XAML parsing because `FilterAll` has `IsChecked="True"`
  declaratively, and WPF raises Checked events during property
  initialization. At that moment, OTHER named elements declared
  further down in the file (notably `DoneActionsBar`, added in v1.0.3)
  are still null. The handler dereferenced them and threw
  `NullReferenceException`.

  Two defensive guards: skip the handler entirely if `_vm` is null
  (shouldn't happen, but cheap), and check `DoneActionsBar is not
  null` before touching it. The correct visibility state gets
  applied later anyway, on Window.Loaded or on the next user click.

## [1.0.6] — 2026-06-23

### Fixed
- **App refused to launch when an orphan TaskNinja process was holding
  the single-instance mutex** (most likely from v1.0.4's crash before
  it could release the mutex). The original logic called `new Mutex(true, ...)`
  which asks for immediate ownership; if the previous TaskNinja died
  without releasing, the .NET runtime threw `AbandonedMutexException`,
  the `OpenExisting` call to find the other instance threw
  `WaitHandleCannotBeOpenedException`, and the app silently exited
  with code 0.

  Restructured the single-instance dance to be robust:
    1. Create the mutex without immediate ownership, then `WaitOne(50ms)`
       to acquire it. This makes ownership explicit and bounded in time.
    2. Catch `AbandonedMutexException` — if a previous instance died
       holding the mutex, we now treat the mutex as ours and continue.
    3. Wrap the entire single-instance check in a defensive try/catch.
       If anything goes wrong (ACL issues, weird security contexts),
       we log it and continue launching anyway. Better to allow a
       possible second instance than to silently refuse to start.
    4. Same defensive treatment for the show-window-event registration.

## [1.0.5] — 2026-06-23

### Fixed
- **Published .exe failed to launch on v1.0.4.** Root cause was the
  `Icon="Resources/tasknin.ico"` attribute I added on the Window
  element. WPF resolves that as a pack URI, and pack URI resolution
  is brittle in PublishSingleFile + SelfContained builds — at runtime
  the resource can't be found and the WPF parser throws during XAML
  load, before any window even renders. Removed the XAML attribute
  and instead load the icon programmatically from the embedded
  resource stream (the same mechanism TrayIconWrapper already uses
  reliably). Wrapped in try/catch so a missing icon never blocks
  launch again.

## [1.0.4] — 2026-06-23

### Fixed
- **Windows taskbar icon now shows the TaskNinja scroll glyph**
  instead of the generic blank-document icon. The csproj's
  ApplicationIcon embeds the .ico in the .exe for File Explorer,
  but the running Window also needs an `Icon="..."` attribute for
  WPF to set the runtime taskbar/Alt-Tab icon. Added that.

### Added
- **Drag-and-drop reorder of tasks.** Click and drag any task to
  move it up or down in the list. First drag auto-switches sort
  mode to "Manual"; the toolbar's new ↕ Date / ↕ Manual button lets
  you swap between manual order and due-date order at any time.
  The leftmost 30px of each row (the state glyph hit zone) is
  exempted from drag so single-clicks there still cycle state.
- **Row hover highlight** — tasks now subtly light up on hover so
  it's clear what you'll interact with. Tiny detail, big difference
  for legibility on dense lists.
- **Improved empty state** with concrete hints: shortcut, #tag tip,
  and drag-to-reorder mention.

### Changed
- **Window wider**: 380 → 460px (and minimum width 320 → 380). Row
  chips and titles weren't fighting for space anymore but with the
  bigger fonts from v1.0.2 this gives proper breathing room.
- **Preview popup even bigger**: width 460 → 560px, min height 350
  → 420, max 850 → 920. Body section grows to 500px before scroll
  kicks in. The popup should now feel spacious, not cramped.

### Internal
- Added `TaskItem.SortOrder` (int, default 0) for manual ordering.
  Default 0 means "no manual position" so date-sort applies as
  before; values are assigned in 1000-wide gaps when the user
  drags so future single-item drags don't need to renumber
  everything.
- New VM methods: `ReorderTask(task, newIndex)` and a bindable
  `SortMode` property.

## [1.0.3] — 2026-06-22

### Changed
- **"All" filter now means "all ACTIVE"**, excluding Done tasks.
  Previously Done tasks lingered on the All view forever and felt
  like clutter. Today/Week/Overdue already excluded Done; this brings
  All into line. Done tasks remain accessible via the dedicated Done
  filter pill.
- **Hover preview is bigger**: width bumped 360 → 460px, max height
  600 → 850, body section grows to 400px before scrolling kicks in,
  and the minimum height is now 350 so small tasks still feel
  substantial. Body text bumped 12 → 13pt.

### Added
- **"📦 Archive all done" action bar** appears at the top of the list
  whenever the Done filter is active. One click moves every Done task
  in the current view (current bucket, or all buckets if "All buckets"
  is selected) to the archive. Archive is not delete — tasks survive
  and can be recovered. Confirmation dialog before action.

## [1.0.2] — 2026-06-22

### Added
- **State picker in the hover preview**: a row of three buttons —
  ○ Open / ◐ In progress / ● Done — sits below the meta chips. Click
  any to set the task to that state directly. The active state is
  shown with the amber accent background; the others are outlined.
  After clicking, the popup dismisses and the row updates on the home
  list. You no longer have to remember that the cryptic ○/◐/● glyph
  cycles on click.
- **State picker in the detail editor**: same three-button row, placed
  prominently right below the title — state is a top-level property,
  not a buried setting. The picker updates live as you click; click
  Save to commit (or any change persists immediately since TaskItem
  notifies). Same widget as the popup uses, so the muscle memory is
  identical wherever you change state.
- **Full patch version in the header**: "TaskNinja v1.0.2" instead of
  the previous "v1.0". Version pulled from a single source of truth
  (App.DisplayVersion) so future bumps update everywhere.
- **🔗 chip on rows** when the body contains URLs, paralleling the 📎
  chip for body/attachment content. The chip shows the URL count.
- **Recurrence spawning** now works from the popup's state picker too
  (not just the cycle-on-glyph-click path).

### Fixed
- **Preview attachments now look clickable.** Each attachment is in a
  card with the thumbnail, pixel dimensions, and an explicit "🔍 View"
  button that opens the image at full size in your default image
  viewer. The whole card is clickable too.
- **URL section in the preview is more prominent.** "🔗 LINKS (n)"
  header in amber bold. Each URL is a clickable card with an explicit
  "🌐 Open" button. URLs are styled as underlined dusty-blue links
  for clear "this is clickable" affordance.
- **Bare `www.*` URLs are now recognized**, not just `http(s)://`
  ones. Matches ClipNinja's URL recognition.
- **Dropdown contrast** improved everywhere — recurrence and person
  combos in the detail editor now use the dark theme with amber
  popup (was raw light system theme).
- **DatePicker calendar popup readability**: the WPF DatePicker's
  calendar popup uses system light colors that don't theme well to
  the dark palette, so we replaced both DatePickers (in the detail
  editor and the quick-add bar) with typed YYYY-MM-DD TextBox fields.
  Quick-add accepts friendly inputs too: "today", "tomorrow", "tmr",
  weekday names like "fri" or "monday".

### Changed
- **Font sizes** bumped 1pt across the app to match ClipNinja's
  readability. Toolbar/filter/status type now 12-13pt baseline;
  primary buttons 14pt; row titles 14pt.
- Small label sizes in the detail editor bumped from 10pt to 12pt
  with a touch more vertical breathing room.

## [1.0.1] — 2026-06-19

### Fixed
- **Bucket dropdown was unreadable.** The closed-state of the ComboBox
  was styled but WPF's default theme overrode the popup chrome. Wrote
  a full ControlTemplate for the dropdown — popup background is now
  the amber AccentBrush, items are dark text with a darker-amber hover
  state. Reliably readable now.
- **Hover preview disappeared when you tried to mouse into it.** Hide
  was being called immediately on row MouseLeave, so the cursor never
  had a chance to enter the popup. Added a 180ms grace period: row
  leave starts a delayed-hide timer that the popup's MouseEnter
  cancels. You can now mouse from a row into the popup to click an
  "Open URL" button without it vanishing.
- **Preview appeared on the wrong monitor.** WPF's
  SystemParameters.WorkArea only returns the PRIMARY monitor's bounds,
  so multi-monitor positioning was broken — the popup tried to clamp
  to the primary monitor regardless of where TaskNinja actually was.
  Now uses System.Windows.Forms.Screen.FromHandle to resolve the
  work area of the monitor containing the main window. Popup appears
  next to TaskNinja on whichever screen it lives on.

### UI design pass
- **Bigger fonts overall.** Toolbar/filter/status type bumped from
  10-11pt to 12-13pt; quick-add text field 14pt. Title bar 13pt.
- **+ Add task is now a PRIMARY button** (amber background, dark
  text, bold). It's the most important action on screen so it gets
  the visual weight. Moved to the right of the toolbar (heaviest
  element on the right where the eye lands).
- **Filter strip uses pills** instead of tiny radio buttons. Active
  pill takes the amber background; inactive pills are outlined with
  the border color. The whole strip is significantly more legible
  and tap-friendly.
- **Task rows have breathing room.** Row padding bumped from 8,6
  to 12,10; state glyph grew from 16 to 20pt; chip text from 9-10
  to 11-12.
- **Consistent spacing rhythm**: 8/12/16 px scale across the window.
- **Bucket dropdown** fills the toolbar's free space instead of
  being a fixed 120px column.

## [1.0.0] — 2026-06-17

Initial release of TaskNinja — a desktop task tracker, styled to match
ClipNinja, single-user, local-only.

### Features
- Tasks with title, body (with paste-image support), due date, responsible
  person, recurrence (daily/weekly/monthly/yearly), start date, inline
  #hashtag parsing
- Three states: Open / In progress / Done; click the state glyph to cycle
- Color-coded due-date chips (overdue/today/soon/later)
- Hover preview popout showing full body, URLs (with Open buttons), image
  attachments, and metadata
- Full detail editor (double-click a row) with paste-image support via
  Ctrl+V
- Buckets — top-level containers for tasks; add/rename/delete via the ⚙
  manager dialog. Default bucket can be renamed but not deleted. "All
  buckets" view shows everything mixed together.
- Filter strip: All / Today / Week / Overdue / Done
- Ctrl+F search across title, body, person, and tags
- Right-click menu: Edit / Set due date / Set responsible / Set
  recurrence / Move to bucket / Snooze (tomorrow/weekend/next week) /
  Archive / Delete
- System tray icon with Show / Exit menu
- Global hotkey Ctrl+Shift+T to show/hide
- Launch on Windows startup via HKCU Run-key (no admin needed)
- Topmost frameless draggable window in the desert palette
- Single-file self-contained .exe; portable data folder under
  `%AppData%\TaskNinja\`

### Deliberately NOT in v1.0
- Notifications (Windows toast) — out of scope until we know the tool sticks
- Drag-in integration with ClipNinja — pending
- Tag colors, team boards, multi-person assignment
- Phone / cloud sync
- Import / export
- Streak indicators, daily review prompts, stale-task detection
