using System;

namespace TaskNinja.Models;

/// <summary>
/// A checklist item under a TaskItem. Intentionally minimal — not a
/// nested TaskItem. Subtasks don't have due dates, recurrence, buckets,
/// attachments, or any of the rest. They're meant for tracking minor
/// progress within a single parent task, e.g. "do this thing for five
/// projects" → 5 subtasks, check them off as you go.
///
/// If a checklist item ever needs more weight (a real due date, its
/// own state log, etc), promote it to a full TaskItem instead of
/// growing this model.
/// </summary>
public class Subtask
{
    /// <summary>Short label — what the user typed when adding the
    /// subtask. Free text. No length limit enforced, but the UI
    /// trims long ones for display.</summary>
    public string Title { get; set; } = "";

    /// <summary>True when the user has checked it off.</summary>
    public bool IsDone { get; set; }

    /// <summary>When it was checked off. Null while open.
    /// Set by the UI on the IsDone toggle.</summary>
    public DateTime? CompletedAt { get; set; }
}
