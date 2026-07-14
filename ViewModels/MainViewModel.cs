using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskNinja.Models;
using TaskNinja.Services;

namespace TaskNinja.ViewModels;

/// <summary>
/// Top-level view model. Owns all tasks, all buckets, the active bucket
/// selection, the filter, and the search term. Drives the visible list
/// via a computed VisibleTasks collection that the XAML binds to.
///
/// Persistence is debounced — every mutation calls ScheduleSave() and
/// the actual disk write happens 500ms after the last edit, on a
/// background task. App exit flushes synchronously.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PersistenceService _persistence;
    private readonly DispatcherTimer _saveDebouncer;

    public AppSettings Settings { get; private set; }

    /// <summary>All tasks across all buckets, including archived. The
    /// VisibleTasks projection filters this down to what's shown.</summary>
    public ObservableCollection<TaskItem> AllTasks { get; } = new();

    /// <summary>All buckets including the default. The first entry is
    /// always the default bucket.</summary>
    public ObservableCollection<Bucket> Buckets { get; } = new();

    /// <summary>Autocomplete list of person names (persisted to
    /// people.json).</summary>
    public ObservableCollection<string> People { get; } = new();

    /// <summary>The tasks currently shown in the main list, after
    /// applying the active bucket, filter, and search term. Re-computed
    /// whenever any of those change.</summary>
    public ObservableCollection<TaskItem> VisibleTasks { get; } = new();

    private string _activeBucketId = Bucket.DefaultBucketId;
    /// <summary>The currently selected bucket. Empty string means
    /// "All buckets". Driven by the dropdown in the UI.</summary>
    public string ActiveBucketId
    {
        get => _activeBucketId;
        set
        {
            if (_activeBucketId == value) return;
            _activeBucketId = value ?? "";
            Settings.LastBucketId = _activeBucketId;
            Notify();
            RebuildVisible();
            ScheduleSave();
        }
    }

    private string _activeFilter = "All";

    private string _activeTag = "";
    /// <summary>Active tag filter ("" = none). Deliberately spans ALL
    /// buckets: a tag like "audit" is a cross-cutting concern, and
    /// intersecting it with the bucket filter would usually return
    /// nothing and look broken. Selecting a tag therefore also clears
    /// the bucket filter (see the UI's tag-chip click handler).</summary>
    public string ActiveTag
    {
        get => _activeTag;
        set
        {
            var v = (value ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (_activeTag == v) return;
            _activeTag = v;
            Notify();
            Notify(nameof(HasActiveTag));
            Notify(nameof(ActiveTagLabel));
            RebuildVisible();
        }
    }
    public bool HasActiveTag => _activeTag.Length > 0;
    public string ActiveTagLabel => _activeTag.Length > 0 ? $"#{_activeTag}" : "";

    /// <summary>Every tag in use across all non-archived tasks, sorted
    /// by frequency then alphabetically — powers the tag picker.</summary>
    public List<string> AllTagsInUse =>
        AllTasks.Where(t => !t.IsArchived)
                .SelectMany(t => t.AllTags)
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key)
                .ToList();
    /// <summary>Named filter: "All" / "Today" / "ThisWeek" / "Overdue" /
    /// "NoDate" / "Done". Drives RebuildVisible's section logic.</summary>
    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (_activeFilter == value) return;
            _activeFilter = value ?? "All";
            Notify();
            RebuildVisible();
        }
    }

    private string _searchTerm = "";
    /// <summary>Case-insensitive substring match against title and body.
    /// Empty string = no search filter.</summary>
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm == value) return;
            _searchTerm = value ?? "";
            Notify();
            RebuildVisible();
        }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Notify(); }
    }

    public int OpenCount => AllTasks.Count(t => !t.IsArchived && t.State != TaskState.Done);

    /// <summary>How many tasks are overdue right now (due date in the
    /// past, not Done, not archived). Used for the tray tooltip badge
    /// and as a quick-glance indicator on the daily digest.</summary>
    public int OverdueCount => AllTasks.Count(t =>
        !t.IsArchived && t.State != TaskState.Done
        && t.DueDate is { } d && d.Date < DateTime.Today);
    public int DoneTodayCount => AllTasks.Count(t =>
        t.State == TaskState.Done && t.CompletedAt is { } c && c.Date == DateTime.Today);

    /// <summary>How many tasks are currently deferred (StartDate in the
    /// future). Drives the "Scheduled (N)" filter button label so the
    /// user knows there's stuff waiting in the wings.</summary>
    public int ScheduledCount => AllTasks.Count(t =>
        !t.IsArchived && t.State != TaskState.Done && t.IsDeferred);

    public MainViewModel()
    {
        _persistence = new PersistenceService();
        Settings = _persistence.LoadSettings();

        _saveDebouncer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebouncer.Tick += (_, _) => { _saveDebouncer.Stop(); SaveAll(); };

        foreach (var b in _persistence.LoadBuckets()) Buckets.Add(b);
        foreach (var t in _persistence.LoadTasks())
        {
            t.PropertyChanged += OnTaskChanged;
            AllTasks.Add(t);
        }
        foreach (var p in _persistence.LoadPeople()) People.Add(p);

        // Restore last-active bucket. Empty string is the legitimate
        // "All buckets" view; otherwise the bucket must still exist.
        if (Settings.LastBucketId == "" ||
            Buckets.Any(b => b.Id == Settings.LastBucketId))
        {
            _activeBucketId = Settings.LastBucketId;
        }

        RebuildVisible();
    }

    // ── Mutations ───────────────────────────────────────────────────

    /// <summary>Create a new task. Defaults to Open state. The bucket
    /// is the explicit <paramref name="bucketId"/> if supplied, otherwise
    /// the currently-active bucket, otherwise the default bucket.</summary>
    public TaskItem AddTask(string title, DateTime? dueDate = null,
        string? responsiblePerson = null, string? body = null,
        string? bucketId = null)
    {
        // Resolve target bucket. Validate that the explicit bucketId
        // actually refers to a bucket we know about; fall back if not.
        string resolvedBucketId;
        if (!string.IsNullOrEmpty(bucketId) && Buckets.Any(b => b.Id == bucketId))
            resolvedBucketId = bucketId;
        else if (!string.IsNullOrEmpty(_activeBucketId) && Buckets.Any(b => b.Id == _activeBucketId))
            resolvedBucketId = _activeBucketId;
        else
            resolvedBucketId = Bucket.DefaultBucketId;

        var t = new TaskItem
        {
            Title = title?.Trim() ?? "",
            BucketId = resolvedBucketId,
            DueDate = dueDate,
            ResponsiblePerson = responsiblePerson?.Trim() ?? "",
            Body = body ?? "",
        };
        t.PropertyChanged += OnTaskChanged;
        AllTasks.Add(t);
        if (!string.IsNullOrWhiteSpace(t.ResponsiblePerson))
            AddPerson(t.ResponsiblePerson);
        RebuildVisible();
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        StatusText = $"➕ Added: {(t.Title.Length > 24 ? t.Title[..24] + "…" : t.Title)}";
        ScheduleSave();
        return t;
    }

    /// <summary>Cycle a task's state: Open → InProgress → Done → Open.
    /// Also handles recurrence: when a recurring task hits Done, the
    /// next occurrence spawns automatically.</summary>
    public void CycleState(TaskItem task)
    {
        var next = task.State switch
        {
            TaskState.Open => TaskState.InProgress,
            TaskState.InProgress => TaskState.Done,
            TaskState.Done => TaskState.Open,
            _ => TaskState.Open,
        };
        task.State = next;

        // Recurrence: when a recurring task becomes Done, spawn the
        // next instance with the due date advanced by the interval.
        if (next == TaskState.Done && task.Recurrence != RecurrencePattern.None)
        {
            SpawnNextRecurrence(task);
        }

        RebuildVisible();
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        Notify(nameof(DoneTodayCount));
        ScheduleSave();
    }

    /// <summary>Public access to recurrence spawning. External callers
    /// (e.g. the preview popup's state picker, the row glyph picker, the
    /// daily digest's ✓ button) set State=Done themselves, then call this
    /// to spawn the next instance. With an explicit overrideDate, the
    /// caller can override the auto-computed next due (e.g. after the
    /// user adjusted it in the recurrence confirmation popup).</summary>
    public void SpawnRecurrenceFor(TaskItem completed, DateTime? overrideDate = null,
        DateTime? overrideStartDate = null, bool startDateExplicitlyCleared = false)
        => SpawnNextRecurrence(completed, overrideDate, overrideStartDate, startDateExplicitlyCleared);

    /// <summary>Compute the next due date for a recurring task WITHOUT
    /// spawning it. Used by the confirmation popup to show the proposed
    /// date before committing. Returns null for non-recurring tasks or
    /// tasks without a due date.
    ///
    /// Smart-clamp behavior: if the naive (due + interval) date is
    /// already in the past, fall back to (today + interval) so the
    /// spawned task isn't born already-overdue. This handles the "fell
    /// behind on a recurring task" case — a weekly task you forgot for
    /// 3 weeks doesn't suddenly drown you in 3 overdue instances; you
    /// get one fresh instance with a fresh interval.</summary>
    public DateTime? ComputeNextRecurrence(TaskItem completed)
    {
        if (completed.Recurrence == RecurrencePattern.None) return null;
        if (completed.DueDate is not { } due) return null;
        var interval = Math.Max(1, completed.RecurrenceInterval);

        // Naive next: previous-due + interval. Preserves cadence when
        // the user completes on time.
        var naive = completed.Recurrence switch
        {
            RecurrencePattern.Daily => due.AddDays(interval),
            RecurrencePattern.Weekly => due.AddDays(7 * interval),
            RecurrencePattern.Monthly => due.AddMonths(interval),
            RecurrencePattern.Yearly => due.AddYears(interval),
            _ => due,
        };
        // Smart clamp: never spawn already-overdue. If naive is in the
        // past (user fell behind), shift forward to today + interval —
        // a fresh interval, anchored at completion time. This trades
        // strict cadence for "you're caught up, here's a new clock."
        if (naive < DateTime.Today)
        {
            var fromToday = completed.Recurrence switch
            {
                RecurrencePattern.Daily => DateTime.Today.AddDays(interval),
                RecurrencePattern.Weekly => DateTime.Today.AddDays(7 * interval),
                RecurrencePattern.Monthly => DateTime.Today.AddMonths(interval),
                RecurrencePattern.Yearly => DateTime.Today.AddYears(interval),
                _ => DateTime.Today,
            };
            return fromToday;
        }
        return naive;
    }

    /// <summary>External hook for "I just mutated a task directly, please
    /// refresh counts and persist." Used by the preview popup's state
    /// picker. The OnTaskChanged property-change wiring already schedules
    /// a save; this just nudges the count properties.</summary>
    public void OnTaskMutated()
    {
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        Notify(nameof(DoneTodayCount));
        Notify(nameof(ScheduledCount));
        ScheduleSave();
    }

    /// <summary>Visibility-window defaults per recurrence pattern. The
    /// spawned next instance gets <c>StartDate = nextDue - window</c> so
    /// it stays hidden from normal views until N days before due.
    /// Discoverable in the "Scheduled" filter and editable per-task via
    /// the completion popup. Daily recurrence skips deferral entirely —
    /// a 1-day interval doesn't have room for a window.</summary>
    private static TimeSpan VisibilityWindowFor(RecurrencePattern p) => p switch
    {
        RecurrencePattern.Daily => TimeSpan.Zero,         // show immediately
        RecurrencePattern.Weekly => TimeSpan.FromDays(2),  // 2 days before due
        RecurrencePattern.Monthly => TimeSpan.FromDays(7), // 7 days before due
        RecurrencePattern.Yearly => TimeSpan.FromDays(14), // 14 days before due
        _ => TimeSpan.Zero,
    };

    /// <summary>Public compute helper for the completion popup so it
    /// can show a sensible default "show on" date.</summary>
    public DateTime? ComputeAutoStartDate(TaskItem completed, DateTime nextDue)
    {
        var window = VisibilityWindowFor(completed.Recurrence);
        if (window == TimeSpan.Zero) return null;
        var startDate = nextDue.Date - window;
        // Don't set StartDate in the past — that defeats the purpose.
        // If the window would already be open (e.g. user picked a
        // next-due that's only 3 days out for a Monthly pattern with
        // 7-day window), just don't defer.
        return startDate > DateTime.Today ? startDate : (DateTime?)null;
    }

    private void SpawnNextRecurrence(TaskItem completed, DateTime? overrideDate = null,
        DateTime? overrideStartDate = null, bool startDateExplicitlyCleared = false)
    {
        // Use the override if the caller supplied one (popup-confirmed
        // date). Otherwise compute the smart-clamped default.
        var nextDue = overrideDate ?? ComputeNextRecurrence(completed);
        if (nextDue is null) return;

        // Compute the auto-deferred StartDate. The caller can override
        // (popup lets the user edit the "show on" date), or explicitly
        // clear it (popup unchecks "hide until visibility window opens")
        // — both cases handled distinctly so null-vs-cleared is honored.
        DateTime? startDate;
        if (startDateExplicitlyCleared) startDate = null;
        else if (overrideStartDate.HasValue) startDate = overrideStartDate;
        else startDate = ComputeAutoStartDate(completed, nextDue.Value);

        var next = new TaskItem
        {
            Title = completed.Title,
            Body = completed.Body,
            BucketId = completed.BucketId,
            DueDate = nextDue,
            StartDate = startDate,
            ResponsiblePerson = completed.ResponsiblePerson,
            Recurrence = completed.Recurrence,
            RecurrenceInterval = completed.RecurrenceInterval,
        };
        next.PropertyChanged += OnTaskChanged;
        AllTasks.Add(next);
        Trace.Log("recur", $"Spawned next recurrence of '{completed.Title}' due {nextDue:yyyy-MM-dd}, visible from {(startDate is { } sd ? sd.ToString("yyyy-MM-dd") : "now")}");
    }

    /// <summary>Archive (don't delete) — sets IsArchived=true. Archived
    /// tasks survive across sessions but don't show in any view except
    /// "Archive" (not yet exposed in v1.0 UI; right-click → Archive
    /// puts them away).</summary>
    public void ArchiveTask(TaskItem task)
    {
        task.IsArchived = true;
        RebuildVisible();
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        StatusText = "📦 Archived";
        ScheduleSave();
    }

    public void UnarchiveTask(TaskItem task)
    {
        task.IsArchived = false;
        RebuildVisible();
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        ScheduleSave();
    }

    /// <summary>Bulk-archive every Done task across all buckets (or just
    /// the active bucket if one is selected). Returns the count archived
    /// so the UI can show a confirmation. Doesn't delete — tasks live on
    /// in the archive and can be unarchived if needed.</summary>
    public int ArchiveAllDone()
    {
        var targets = AllTasks.Where(t =>
            !t.IsArchived &&
            t.State == TaskState.Done &&
            (string.IsNullOrEmpty(_activeBucketId) || t.BucketId == _activeBucketId)
        ).ToList();
        foreach (var t in targets) t.IsArchived = true;
        RebuildVisible();
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        StatusText = $"📦 Archived {targets.Count} done task{(targets.Count == 1 ? "" : "s")}";
        ScheduleSave();
        return targets.Count;
    }

    /// <summary>Hard delete — removes from list, drops attachments. The
    /// trash-can action.</summary>
    public void DeleteTask(TaskItem task)
    {
        foreach (var a in task.Attachments)
            _persistence.DeleteAttachment(a.FileName);
        AllTasks.Remove(task);
        RebuildVisible();
        Notify(nameof(OpenCount));
        Notify(nameof(OverdueCount));
        StatusText = "🗑️ Deleted";
        ScheduleSave();
    }

    /// <summary>Push the due date out: tomorrow / weekend (next Saturday) /
    /// next week (+7 days). If the task had no due date, uses today as
    /// the baseline.</summary>
    /// <summary>Bump a task's due date forward by a preset offset.
    /// Tomorrow (+1 day), Weekend (next Saturday), 2Days (+2 days),
    /// 1Week (+7 days from today), 2Weeks (+14 days from today),
    /// NextWeek (+7 from existing due, preserves cadence).
    /// If the task had no due date, uses today as the baseline.</summary>
    public void Snooze(TaskItem task, string preset)
    {
        var baseDate = task.DueDate ?? DateTime.Today;
        var newDate = preset switch
        {
            "Tomorrow"  => DateTime.Today.AddDays(1),
            "2Days"     => DateTime.Today.AddDays(2),
            "Weekend"   => NextSaturday(),
            "1Week"     => DateTime.Today.AddDays(7),
            "2Weeks"    => DateTime.Today.AddDays(14),
            "NextWeek"  => baseDate.AddDays(7),  // legacy preset name
            _ => baseDate,
        };
        SnoozeTo(task, newDate);
    }

    /// <summary>Set a task's due date to an arbitrary date. Used by the
    /// "..." date-picker option in the snooze menu, which lets the user
    /// pick any future date without a preset.</summary>
    public void SnoozeTo(TaskItem task, DateTime newDate)
    {
        task.DueDate = newDate;
        RebuildVisible();
        StatusText = $"💤 Snoozed to {task.DueLabel}";
        ScheduleSave();
    }

    private static DateTime NextSaturday()
    {
        var d = DateTime.Today;
        int days = ((int)DayOfWeek.Saturday - (int)d.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        return d.AddDays(days);
    }

    /// <summary>Move a task to a different bucket. Bucket must exist.</summary>
    public void MoveToBucket(TaskItem task, string bucketId)
    {
        if (!Buckets.Any(b => b.Id == bucketId)) return;
        task.BucketId = bucketId;
        RebuildVisible();
        StatusText = $"📂 Moved to {Buckets.First(b => b.Id == bucketId).Name}";
        ScheduleSave();
    }

    public Bucket AddBucket(string name)
    {
        var b = new Bucket { Name = name?.Trim() ?? "Untitled", SortOrder = Buckets.Count };
        Buckets.Add(b);
        StatusText = $"📁 Added bucket: {b.Name}";
        ScheduleSave();
        return b;
    }

    public void RenameBucket(Bucket bucket, string newName)
    {
        bucket.Name = newName?.Trim() ?? bucket.Name;
        StatusText = "📁 Renamed";
        ScheduleSave();
    }

    /// <summary>Delete a bucket. Default bucket can't be deleted. Tasks
    /// belonging to the deleted bucket get moved to the default bucket.</summary>
    public bool DeleteBucket(Bucket bucket)
    {
        if (bucket.Id == Bucket.DefaultBucketId) return false;
        foreach (var t in AllTasks.Where(t => t.BucketId == bucket.Id).ToList())
            t.BucketId = Bucket.DefaultBucketId;
        Buckets.Remove(bucket);
        if (_activeBucketId == bucket.Id) ActiveBucketId = Bucket.DefaultBucketId;
        StatusText = $"📁 Deleted bucket: {bucket.Name}";
        ScheduleSave();
        return true;
    }

    public void AddPerson(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        if (People.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase))) return;
        // Insert in sorted position
        int i = 0;
        while (i < People.Count && string.Compare(People[i], trimmed, StringComparison.OrdinalIgnoreCase) < 0) i++;
        People.Insert(i, trimmed);
        ScheduleSave();
    }

    // ── Visible list projection ────────────────────────────────────

    /// <summary>Rebuild VisibleTasks based on ActiveBucketId, ActiveFilter,
    /// and SearchTerm. Called whenever any of these — or the task list —
    /// changes. Cheap on small lists; if this ever shows up in a profile
    /// we'd swap to a CollectionView with a Predicate.</summary>
    public void RebuildVisible()
    {
        VisibleTasks.Clear();
        var today = DateTime.Today;
        var weekEnd = today.AddDays(7);

        var pool = AllTasks.AsEnumerable();

        // Filter: archived hidden everywhere unless explicitly viewing Archive.
        pool = pool.Where(t => !t.IsArchived);

        // Filter: active bucket (empty = all buckets).
        if (!string.IsNullOrEmpty(_activeBucketId))
            pool = pool.Where(t => t.BucketId == _activeBucketId);

        // Filter: deferred tasks (future StartDate) handling.
        //
        // v1.0.25 change: deferred tasks are hidden EVERYWHERE except
        // the new "Scheduled" filter, where they're the only thing
        // shown. Previously "All" surfaced deferred tasks alongside
        // active ones — confusing when you said "don't show until X"
        // and the task still appeared in your bucket / All view.
        //
        // The Scheduled filter is the dedicated home for "stuff
        // that's coming but you don't need to see yet": auto-spawned
        // recurring tasks waiting for their visibility window to
        // open, and any manually-deferred task with a future
        // StartDate. Bucket filtering still applies inside Scheduled,
        // so picking "Home" + Scheduled shows only Home-bucket
        // deferred tasks.
        if (_activeFilter == "Scheduled")
            pool = pool.Where(t => t.IsDeferred && t.State != TaskState.Done);
        else
            // Done tasks bypass the deferred check: if you complete a
            // scheduled task EARLY (from the Scheduled view, before its
            // StartDate arrived), it's Done but still technically
            // deferred — without this exemption it vanished from every
            // view including Done, which read as data loss. A completed
            // task's visibility should be governed by the Done filter
            // alone; "don't show until" is about hiding future WORK,
            // and there's no work left.
            pool = pool.Where(t => !t.IsDeferred || t.State == TaskState.Done);

        // Filter: named view (Scheduled handled above; this switch
        // covers the date-based / state-based views).
        pool = _activeFilter switch
        {
            "Today" => pool.Where(t => t.State != TaskState.Done && t.DueDate is { } d && d.Date == today),
            "ThisWeek" => pool.Where(t => t.State != TaskState.Done && t.DueDate is { } d && d.Date >= today && d.Date <= weekEnd),
            "Overdue" => pool.Where(t => t.State != TaskState.Done && t.DueDate is { } d && d.Date < today),
            "NoDate" => pool.Where(t => t.State != TaskState.Done && t.DueDate is null),
            "Done" => pool.Where(t => t.State == TaskState.Done),
            "Scheduled" => pool,  // already filtered above
            // "All" means "all active tasks" — completed ones live in their
            // own Done filter view, where you can bulk-archive them.
            // Otherwise completed tasks just accumulate visual clutter in
            // the main view, defeating the purpose of marking them done.
            _ => pool.Where(t => t.State != TaskState.Done),
        };

        // Filter: search term across title + body + person + tags.
        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            var s = _searchTerm.ToLowerInvariant();
            pool = pool.Where(t =>
                (t.Title ?? "").ToLowerInvariant().Contains(s) ||
                (t.Body ?? "").ToLowerInvariant().Contains(s) ||
                (t.ResponsiblePerson ?? "").ToLowerInvariant().Contains(s) ||
                t.ParsedTags.Any(tag => tag.Contains(s)));
        }

        // Filter: active tag (cross-bucket by design — see ActiveTag).
        if (_activeTag.Length > 0)
            pool = pool.Where(t => t.AllTags.Contains(_activeTag));

        // Sort: Done items always go to the bottom. Within the active
        // items, behavior depends on Settings.SortMode:
        //   • "Manual"  → respect SortOrder (set via drag-drop reorder).
        //                 Ties (or zeros) fall through to date.
        //   • "DueAsc"  → overdue first, then dated, then no-date last,
        //                 all ascending by due date.
        IOrderedEnumerable<TaskItem> sorted;
        if (_activeFilter == "Done")
        {
            // Done view: most recently completed at the top, oldest at
            // the bottom — a reverse-chronological "what have I
            // finished" log. Overrides the Manual/DueAsc sort modes;
            // manual ordering is about planning open work, and due
            // dates are history once a task is complete. Tasks with no
            // CompletedAt (done before v1.0.16 introduced the stamp)
            // sink to the bottom in creation order.
            sorted = pool
                .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
                .ThenByDescending(t => t.CreatedAt);
        }
        else if (Settings.SortMode == "Manual")
        {
            // Manual mode leaves ordering entirely to the user's drag
            // order — floating important tasks would silently fight the
            // arrangement they just made by hand. The row's amber tint
            // and ❗ still mark them.
            sorted = pool
                .OrderBy(t => t.State == TaskState.Done ? 1 : 0)
                .ThenBy(t => t.SortOrder == 0 ? int.MaxValue : t.SortOrder)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.CreatedAt);
        }
        else
        {
            // Date sort: important tasks float to the top of the active
            // group (still below nothing, still above everything else),
            // then normal due-date ordering. Done stays at the bottom
            // regardless — an important task that's finished is history.
            sorted = pool
                .OrderBy(t => t.State == TaskState.Done ? 1 : 0)
                .ThenByDescending(t => t.State != TaskState.Done && t.IsImportant)
                .ThenBy(t => t.DueDate is null ? 1 : 0)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.CreatedAt);
        }

        foreach (var t in sorted) VisibleTasks.Add(t);
    }

    /// <summary>Reorder a task by drag-and-drop. Moves the task in
    /// VisibleTasks (the user's current view), then assigns sequential
    /// SortOrder values (1000, 2000, 3000...) to all visible tasks so
    /// the new order survives the next RebuildVisible. Switches the
    /// sort mode to "Manual" if it wasn't already, so the user's
    /// manual order is what they see going forward.</summary>
    public void ReorderTask(TaskItem task, int newIndex)
    {
        if (newIndex < 0 || newIndex >= VisibleTasks.Count) return;
        var oldIndex = VisibleTasks.IndexOf(task);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        Settings.SortMode = "Manual";
        Notify(nameof(SortMode));

        VisibleTasks.Move(oldIndex, newIndex);
        // Renumber with 1000-wide gaps so future single-position drags
        // don't need to renumber everyone.
        int sortVal = 1000;
        foreach (var t in VisibleTasks)
        {
            t.SortOrder = sortVal;
            sortVal += 1000;
        }
        ScheduleSave();
    }

    /// <summary>Sort mode exposed as a bindable property so the toolbar
    /// toggle can read/write it.</summary>
    public string SortMode
    {
        get => Settings.SortMode;
        set
        {
            if (Settings.SortMode == value) return;
            Settings.SortMode = value;
            Notify();
            RebuildVisible();
            ScheduleSave();
        }
    }

    // ── Persistence plumbing ────────────────────────────────────────

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Property changes that don't affect persistence:
        if (e.PropertyName is nameof(TaskItem.IsDone) or nameof(TaskItem.IsInProgress)
            or nameof(TaskItem.StateGlyph) or nameof(TaskItem.HasContent)
            or nameof(TaskItem.AttachmentCount) or nameof(TaskItem.IsDeferred)
            or nameof(TaskItem.BodyPreview) or nameof(TaskItem.DueLabel)
            or nameof(TaskItem.DueState) or nameof(TaskItem.ParsedTags))
            return;
        ScheduleSave();
    }

    public void ScheduleSave()
    {
        _saveDebouncer.Stop();
        _saveDebouncer.Start();
    }

    private readonly object _saveLock = new();
    private bool _saveInProgress;
    private bool _saveQueued;

    public void SaveAll()
    {
        var tasksSnap = AllTasks.ToList();
        var bucketsSnap = Buckets.ToList();
        var peopleSnap = People.ToList();
        var settingsSnap = Settings;

        lock (_saveLock)
        {
            if (_saveInProgress) { _saveQueued = true; return; }
            _saveInProgress = true;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using (Trace.Time("save", $"Save (tasks={tasksSnap.Count}, buckets={bucketsSnap.Count})"))
                {
                    _persistence.SaveSettings(settingsSnap);
                    _persistence.SaveBuckets(bucketsSnap);
                    _persistence.SaveTasks(tasksSnap);
                    _persistence.SavePeople(peopleSnap);
                }
            }
            catch (Exception ex) { Trace.Log("save", $"FAILED: {ex.Message}"); }
            finally
            {
                bool again;
                lock (_saveLock)
                {
                    _saveInProgress = false;
                    again = _saveQueued;
                    _saveQueued = false;
                }
                if (again)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(SaveAll));
                }
            }
        });
    }

    public void Flush()
    {
        _saveDebouncer.Stop();
        _persistence.SaveSettings(Settings);
        _persistence.SaveBuckets(Buckets);
        _persistence.SaveTasks(AllTasks);
        _persistence.SavePeople(People);
    }

    /// <summary>Expose the persistence service for attachment I/O from
    /// the detail editor (which needs to save pasted images and load
    /// existing ones).</summary>
    public PersistenceService Persistence => _persistence;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
