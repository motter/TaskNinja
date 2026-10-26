# TaskNinja v1.0

A desktop task tracker styled to match ClipNinja. Sits on your screen, captures the things you need to do, lets you hide detail when you don't need it and expand when you do.

## Features

### Tasks
- **Title + body**: every task has a short title and an optional longer body for context, URLs, screenshots, pasted email content. The row shows just the title; hover or open the editor for the rest.
- **📎 chip** on the row signals "there's hidden content here" so you know when to look deeper.
- **Three states**: Open (○) → In progress (◐) → Done (●). Click the state glyph to cycle. Done tasks stay visible but grayed and struck through.
- **Due dates**, day-granular. Color-coded chip: red for overdue, amber for today, sage for "soon" (within 3 days), neutral for later.
- **Responsible person** field. Autocomplete remembers people you've used before.
- **Recurrence**: daily / weekly / monthly / yearly with custom interval. When a recurring task is marked Done, the next occurrence spawns automatically with the due date advanced.
- **Start date** ("don't show until"): hide a task from default views until a specific date arrives. Useful for "follow up Tuesday" items.
- **Tags**: type `#tag` inline in the title and it's parsed automatically. Tags also appear in the hover preview.
- **Hover preview popout**: just like ClipNinja's clip preview — hover a task, a popup slides in showing the full body, URLs (with "Open in browser" buttons), image attachments, and metadata.

### Buckets
- **Top-level containers** for your tasks. Switch between them via the dropdown. Default bucket "Tasks" can't be deleted, but you can rename it.
- **⚙ button** in the toolbar opens the bucket manager: add, rename, delete buckets. Deleting moves orphaned tasks into the default bucket.
- **"All buckets"** view in the dropdown shows everything mixed together.

### Views and filtering
- **Filter strip**: All / Today / Week / Overdue / Done. Switching is one click.
- **Ctrl+F** opens search — case-insensitive substring match across title, body, person, and tags.
- **Sort**: due date ascending, with overdue first, no-date last, Done items at the bottom. Stable across reorders.

### Quick add
- **+ Add task** button or **Ctrl+N**: pops the quick-add bar at the top.
- Type the title. **Enter saves**. That's the only required input. Date and person fields are right there if you want them; ignore them if you don't.
- **Esc** cancels.

### Detail editor
- **Double-click a task** or **right-click → Edit task…** to open the full editor.
- Edit title, body, due date, start date, responsible person, recurrence.
- **Paste images directly** with Ctrl+V — gets saved to `attachments\` and added to the task. Thumbnails appear at the bottom of the editor; right-click a thumbnail to remove.
- URLs in the body auto-link in the hover preview.

### Right-click on a task
- **Edit task…**
- **Set due date…**
- **Set responsible…**
- **Set recurrence…** (cycles through None → Daily → Weekly → Monthly → Yearly)
- **Move to bucket…**
- **Snooze**: Tomorrow / Weekend / Next week
- **📦 Archive** (preserves the task but hides it from views)
- **🗑️ Delete…** (asks for confirmation; deletes attachments too)

### System tray + hotkey
- **Ctrl+Shift+T** anywhere in Windows: show/hide TaskNinja
- Tray icon: left-click to show, right-click for menu (Show / Exit)
- **Launch on Windows startup** via the standard `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key — no admin rights required, no service installation. Toggle from the tray menu.
- Window minimize button goes to the Windows taskbar (NOT the hidden-icons tray flyout — we hit this bug in ClipNinja and applied the fix here from day one).

### Visual identity
- Same desert palette as ClipNinja
- Rounded, frameless window, draggable
- Topmost by default

## Where data lives

```
%AppData%\TaskNinja\
├── settings.json           preferences
├── tasks.json              all tasks across all buckets
├── buckets.json            bucket definitions
├── people.json             autocomplete list of person names
└── attachments\            PNGs for image attachments
    └── img_<guid>.png
```

Truly portable — delete that folder to fully wipe TaskNinja.

## Building from source

```powershell
cd TaskNinja
dotnet publish -c Release
```

Output: `bin\Release\net8.0-windows\win-x64\publish\TaskNinja.exe` — a single self-contained executable.

### Requirements
- .NET 8 SDK
- Windows 10 or 11

## Architecture

```
TaskNinja/
├── App.xaml(.cs)              single-instance mutex, --hidden flag
├── MainWindow.xaml(.cs)       title bar, toolbar, quick-add, list, status bar
├── TrayIconWrapper.cs         system tray icon
├── Converters.cs              StringToVisConverter
├── Models/
│   ├── TaskItem.cs            the task object
│   ├── Bucket.cs              named container
│   └── AppSettings.cs         preferences
├── Services/
│   ├── PersistenceService.cs  async debounced save to %AppData%
│   ├── HotkeyService.cs       Ctrl+Shift+T global hotkey
│   ├── StartupService.cs      HKCU Run-key
│   └── Trace.cs               diagnostic logger
├── ViewModels/
│   └── MainViewModel.cs       active bucket, filter, search, visible list
├── Views/
│   ├── InputPrompt.cs         text input dialog
│   ├── PreviewPopup.cs        hover preview popup
│   ├── TaskDetailEditor.cs    full editor with paste-image support
│   └── BucketManagerDialog.cs add/rename/delete buckets
└── Resources/
    ├── tasknin.ico            scroll-with-checkmark glyph (6 sizes)
    ├── Colors.xaml            desert palette (shared with ClipNinja)
    └── Styles.xaml
```

### Notable design decisions

- **No nested subtasks.** Tasks are flat with tags. Nested hierarchies become a museum of half-organized junk in personal todo apps; we chose against them deliberately.
- **Buckets are flat too.** No nested buckets. One level is enough.
- **Manual indices not used.** The list is sorted by due-date logic; manual order doesn't survive sort changes.
- **Recurrence spawns a new task on completion** rather than mutating dates in place. This way the completed instance retains its CompletedAt timestamp for any "what did I do this week" use case.
- **Body and attachments are HIDDEN from the row by default.** The 📎 chip signals their presence. This was the core spec input: "should just be the description and maybe the due date, then expand."
- **Notify-on-due is deliberately out of v1.0.** We'll add it if you find the tool sticks. Spec: "leave from scope for now, let's see how I like it before we invest too much."
- **No phone sync, no cloud, no team sharing.** Desktop-only, single-user, single-machine. Local-first design.
- **Migration story is "fresh install"** for v1.0. There's nothing to migrate FROM yet.

## Roadmap (not in v1.0)

- Notifications on overdue/due-soon (Windows toast)
- Drag-in from ClipNinja → creates task with clip content as body
- Tag colors
- Multi-person assignment for team boards
- Today bucket (drag tasks into a "doing today" zone at the top)
- Streak / momentum indicators
- Stale-task detection
- Task templates
- Import from CSV / markdown
- Export to CSV / markdown / JSON
- Encryption / password lock

## Hotkeys cheat sheet

| Hotkey            | What it does                              |
|-------------------|-------------------------------------------|
| Ctrl+Shift+T      | Show/hide TaskNinja window (global)       |
| Ctrl+N            | Open quick-add bar                        |
| Ctrl+F            | Open search                               |
| Esc               | Close search / quick-add bar              |
| Enter (in QA bar) | Save the new task                         |
| Click state glyph | Cycle Open → In progress → Done → Open    |
| Double-click row  | Open detail editor                        |
| Hover row         | Show preview popout                       |
| Ctrl+V (in body)  | Paste image as attachment                 |
