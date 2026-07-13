using System;

namespace TaskNinja.Models;

/// <summary>
/// A single entry in a task's state-change history. Recorded automatically
/// by <see cref="TaskItem"/>'s State setter whenever the state changes.
///
/// Stored as strings rather than typed enums so the JSON file stays
/// readable and resilient to enum renames — adding a new state in the
/// future doesn't invalidate history entries that reference older state
/// names.
/// </summary>
public class StateChangeEntry
{
    /// <summary>When the change happened.</summary>
    public DateTime At { get; set; }

    /// <summary>State the task was in BEFORE the change.</summary>
    public string From { get; set; } = "";

    /// <summary>State the task moved TO.</summary>
    public string To { get; set; } = "";
}
