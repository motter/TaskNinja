using System;

namespace TaskNinja.Models;

/// <summary>
/// Snapshot captured when a task is marked Done. Stored as a list on
/// <see cref="TaskItem"/> so multiple completions over time are
/// preserved (a task that's been done → reopened → done again has
/// two records, latest first).
///
/// All fields except <see cref="At"/> are optional — the completion
/// popup lets the user skip them. A bare record (just a timestamp,
/// no comment, no by-line) is fine and just means "marked done
/// without further notes".
/// </summary>
public class CompletionRecord
{
    /// <summary>When the completion happened.</summary>
    public DateTime At { get; set; }

    /// <summary>Who marked it done. Free text — usually one of the
    /// names in <c>vm.People</c>. Empty for "didn't say".</summary>
    public string CompletedBy { get; set; } = "";

    /// <summary>Optional note about the completion — anything from
    /// "took longer than expected" to a link to the deliverable.
    /// Visible in the Activity expander under the matching
    /// "→ Done" state-change entry.</summary>
    public string Comment { get; set; } = "";
}
