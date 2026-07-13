using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TaskNinja.Models;

/// <summary>
/// Tri-state task lifecycle. Open is the default; In progress signals
/// active work; Done means complete (still visible in the list, just
/// grayed out, until archived).
/// </summary>
public enum TaskState { Open, InProgress, Done }

/// <summary>
/// Recurrence pattern for a task. When a recurring task is marked Done,
/// the persistence layer spawns the next occurrence with an updated
/// due date and resets the state to Open.
/// </summary>
public enum RecurrencePattern { None, Daily, Weekly, Monthly, Yearly }

/// <summary>
/// Body attachment — a reference to an image stored under
/// %AppData%\TaskNinja\attachments\. We don't embed the PNG bytes in
/// tasks.json (would balloon the file); instead we store a filename
/// and load on demand.
/// </summary>
public class BodyAttachment
{
    /// <summary>Filename under attachments\ (e.g. "img_abc123.png").</summary>
    public string FileName { get; set; } = "";
    /// <summary>Original pixel dimensions, used for layout sizing.</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Free-form caption the user can edit.</summary>
    public string Caption { get; set; } = "";
}

/// <summary>
/// A single task. Title is required; everything else is optional. The
/// "body" holds free-form markdown-style notes, URLs (auto-detected by
/// the UI), and image attachments — all of which are HIDDEN by default
/// in the row view and only revealed in the hover-preview or detail
/// editor. The row indicates "has hidden content" via a 📎 chip.
/// </summary>
public class TaskItem : INotifyPropertyChanged
{
    /// <summary>Stable identifier, generated once at creation.
    /// Persists across sessions; never reused.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Which bucket this task lives in. Tasks belong to
    /// exactly one bucket; "All" view is a filter, not a bucket.</summary>
    public string BucketId { get; set; } = "";

    private string _title = "";
    /// <summary>Required. The single-line summary shown on every row.
    /// May contain inline #hashtags which the UI parses into Tags.</summary>
    public string Title
    {
        get => _title;
        set { _title = value; Notify(); Notify(nameof(HasContent)); Notify(nameof(ParsedTags)); }
    }

    private string _body = "";
    /// <summary>Free-form longer text. May include URLs (auto-linkified
    /// in preview) and references to attachments by filename. Empty by
    /// default; the 📎 chip appears when this is non-empty OR Attachments
    /// has anything.</summary>
    public string Body
    {
        get => _body;
        set { _body = value; Notify(); Notify(nameof(HasContent)); Notify(nameof(BodyPreview)); Notify(nameof(HasUrls)); Notify(nameof(UrlCount)); }
    }

    private List<BodyAttachment> _attachments = new();
    /// <summary>Image attachments stored under attachments\. JSON
    /// serializer round-trips this list; the UI loads each PNG lazily
    /// when the detail editor or preview is opened.</summary>
    public List<BodyAttachment> Attachments
    {
        get => _attachments;
        set { _attachments = value ?? new(); Notify(); Notify(nameof(HasContent)); Notify(nameof(AttachmentCount)); }
    }

    private DateTime? _dueDate;
    /// <summary>Optional. Day-granular (time component is ignored).
    /// Null means "no due date" — task shows in a "No date" section.</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set { _dueDate = value?.Date; Notify(); Notify(nameof(DueLabel)); Notify(nameof(DueState)); }
    }

    private DateTime? _startDate;
    /// <summary>Optional "don't show me until" date. Tasks with a future
    /// StartDate are hidden from default views (still visible in "All").
    /// Useful for "follow up Tuesday" items you don't want clutter today.</summary>
    public DateTime? StartDate
    {
        get => _startDate;
        set { _startDate = value?.Date; Notify(); Notify(nameof(IsDeferred)); }
    }

    private string _responsiblePerson = "";
    /// <summary>Optional name. UI auto-completes from PeopleService's
    /// maintained list (added on first use, autocomplete thereafter).</summary>
    public string ResponsiblePerson
    {
        get => _responsiblePerson;
        set { _responsiblePerson = value ?? ""; Notify(); }
    }

    private TaskState _state = TaskState.Open;
    public TaskState State
    {
        get => _state;
        set
        {
            if (_state == value) return;  // no-op (no log spam from re-assigning same state)
            var oldState = _state;
            _state = value;
            // Stamp CompletedAt on the Open/InProgress → Done transition,
            // and clear it on Done → anything-else.
            if (value == TaskState.Done) CompletedAt = DateTime.Now;
            else if (oldState == TaskState.Done) CompletedAt = null;
            // Record the state transition for the activity log. Every
            // state change everywhere in the app flows through this setter
            // (state picker popups, daily digest ✓ button, right-click
            // menu, etc.), so this is the one place to capture history.
            // Use string names rather than enum values so the JSON
            // representation is stable even if the enum is renamed later.
            StateHistory.Add(new StateChangeEntry
            {
                At = DateTime.Now,
                From = oldState.ToString(),
                To = value.ToString(),
            });
            Notify();
            Notify(nameof(IsDone));
            Notify(nameof(IsInProgress));
            Notify(nameof(StateGlyph));
            // The row chip shows completion date for Done tasks, so
            // DueLabel/DueState now depend on State + CompletedAt too.
            Notify(nameof(DueLabel));
            Notify(nameof(DueState));
        }
    }

    /// <summary>Log of every state transition this task has gone through,
    /// in chronological order. Powers the "Activity" section of the
    /// detail editor and provides the data foundation for future
    /// reporting features (weekly summaries, time-in-progress
    /// analytics, etc.). Old tasks created before this field existed
    /// will deserialize with an empty list — that's correct behavior;
    /// we just don't have data from before this version.</summary>
    public List<StateChangeEntry> StateHistory { get; set; } = new();

    /// <summary>Records captured when this task was marked Done. One
    /// entry per Done event — a task that was done, reopened, and done
    /// again has two records. Latest is `Completions.LastOrDefault()`.
    /// Powers the per-completion comment + completed-by display in
    /// the Activity expander.</summary>
    public List<CompletionRecord> Completions { get; set; } = new();

    /// <summary>Subtasks / checklist items beneath this task. Use for
    /// tracking minor progress where a few related checkboxes within
    /// one task is more useful than five separate top-level tasks.
    /// Subtasks have no due dates of their own (see Subtask docs).
    /// JSON serialization handles empty/null gracefully — legacy
    /// tasks without subtasks deserialize as an empty list.</summary>
    public List<Subtask> Subtasks { get; set; } = new();

    /// <summary>Derived: count of completed subtasks. Useful for "3/5"
    /// progress chips in the preview popup and row.</summary>
    public int SubtaskDoneCount => Subtasks.Count(s => s.IsDone);

    public RecurrencePattern Recurrence { get; set; } = RecurrencePattern.None;
    /// <summary>For Recurrence != None: how many of the unit between
    /// repeats. e.g. Weekly + 2 = every 2 weeks. Daily + 1 = every day.</summary>
    public int RecurrenceInterval { get; set; } = 1;

    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Manual sort position. Larger gaps (1000, 2000, 3000…)
    /// leave room for insertions without renumbering. Default 0 means
    /// "no manual position" — the natural date-sort applies. When the
    /// user drags-and-drops to reorder, MainViewModel assigns explicit
    /// SortOrder values and the VM switches to manual sort mode.</summary>
    public int SortOrder { get; set; } = 0;

    // ── Derived / UI-only properties ──────────────────────────────────

    /// <summary>True if the body or any attachment is present — drives
    /// the 📎 chip on the row.</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(_body) || _attachments.Count > 0;

    public int AttachmentCount => _attachments.Count;

    /// <summary>True if the body contains 1+ URLs — drives the 🔗 chip
    /// on the row, matching ClipNinja's URL indicator.</summary>
    public bool HasUrls => UrlCount > 0;

    /// <summary>Number of distinct URLs detected in the body. Scans for
    /// http://, https://, and www.-prefixed links. The actual list is
    /// re-extracted on demand in the preview popup; this property is
    /// just for the chip.</summary>
    public int UrlCount
    {
        get
        {
            if (string.IsNullOrEmpty(_body)) return 0;
            int n = 0;
            // http(s):// links
            foreach (System.Text.RegularExpressions.Match _ in
                System.Text.RegularExpressions.Regex.Matches(_body, @"https?://[^\s<>""']+"))
                n++;
            // www. links not already counted (rough heuristic — same line
            // having "http" near "www" is uncommon enough to not dedupe).
            foreach (System.Text.RegularExpressions.Match _ in
                System.Text.RegularExpressions.Regex.Matches(_body, @"(?<!http://|https://)\bwww\.[A-Za-z0-9][^\s<>""']+"))
                n++;
            return n;
        }
    }

    public bool IsDone => _state == TaskState.Done;
    public bool IsInProgress => _state == TaskState.InProgress;

    public string StateGlyph => _state switch
    {
        TaskState.Open => "○",
        TaskState.InProgress => "◐",
        TaskState.Done => "●",
        _ => "?",
    };

    /// <summary>True if a future StartDate is set — task is hidden from
    /// default views until that date arrives.</summary>
    public bool IsDeferred => _startDate is { } sd && sd.Date > DateTime.Today;

    /// <summary>Brief preview of the body for hover popouts. Trimmed to
    /// 200 chars with ellipsis.</summary>
    public string BodyPreview
    {
        get
        {
            if (string.IsNullOrEmpty(_body)) return "";
            var s = _body.Replace("\r", "").Trim();
            return s.Length > 200 ? s[..200] + "…" : s;
        }
    }

    /// <summary>Human-readable date label for the row chip. For OPEN
    /// tasks: the due date ("Today", "Tomorrow", "Fri Jun 19"…). For
    /// DONE tasks: when it was completed ("✓ Today", "✓ Jun 19") —
    /// once a task is finished, the due date is history and the
    /// completion date is the interesting fact (especially in the
    /// Done filter view).</summary>
    public string DueLabel
    {
        get
        {
            if (State == TaskState.Done)
            {
                if (CompletedAt is not { } c) return "";
                return "✓ " + RelativeDayLabel(c.Date);
            }
            if (_dueDate is not { } d) return "";
            return RelativeDayLabel(d.Date);
        }
    }

    private static string RelativeDayLabel(DateTime d)
    {
        var today = DateTime.Today;
        var delta = (d.Date - today).Days;
        if (delta == 0) return "Today";
        if (delta == 1) return "Tomorrow";
        if (delta == -1) return "Yesterday";
        if (delta > 0 && delta < 7) return d.ToString("ddd");
        if (delta > -7 && delta < 0) return $"{-delta}d ago";
        return d.Year == today.Year ? d.ToString("MMM d") : d.ToString("MMM d, yyyy");
    }

    /// <summary>Used by the UI to color-code the due date chip:
    /// Overdue = red, Today = amber, Soon (within 3 days) = sage,
    /// Done = neutral (chip shows the completion date),
    /// otherwise neutral.</summary>
    public string DueState
    {
        get
        {
            if (State == TaskState.Done)
                return CompletedAt is null ? "None" : "Done";
            if (_dueDate is not { } d) return "None";
            var delta = (d.Date - DateTime.Today).Days;
            if (delta < 0) return "Overdue";
            if (delta == 0) return "Today";
            if (delta <= 3) return "Soon";
            return "Later";
        }
    }

    /// <summary>Parse inline #hashtags from the title. e.g.
    /// "Call vendor #urgent #work" → ["urgent", "work"]. Lowercased,
    /// deduped, capped at 8 tags per task.</summary>
    public List<string> ParsedTags
    {
        get
        {
            var tags = new List<string>();
            if (string.IsNullOrEmpty(_title)) return tags;
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(_title, @"#([A-Za-z0-9_-]+)"))
            {
                var tag = ((System.Text.RegularExpressions.Match)m).Groups[1].Value.ToLowerInvariant();
                if (!tags.Contains(tag)) tags.Add(tag);
                if (tags.Count >= 8) break;
            }
            return tags;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
