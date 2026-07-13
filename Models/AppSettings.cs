namespace TaskNinja.Models;

using System;

/// <summary>App-wide preferences serialized to settings.json.</summary>
public class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public bool LaunchOnStartup { get; set; } = false;

    /// <summary>Last-selected bucket Id, restored on startup. Empty string
    /// means "All" view.</summary>
    public string LastBucketId { get; set; } = Bucket.DefaultBucketId;

    /// <summary>Default sort within each due-date section.
    /// Options: "DueAsc" (default), "CreatedDesc", "Manual".</summary>
    public string SortMode { get; set; } = "DueAsc";

    /// <summary>Whether to show the Done section expanded on startup.</summary>
    public bool ShowDoneExpanded { get; set; } = false;

    /// <summary>One-time tray hint flag (same pattern as ClipNinja).</summary>
    public bool ShowTrayHint { get; set; } = true;

    // ── In-app updates (GitHub Releases) ─────────────────────────────

    /// <summary>GitHub repo ("owner/name") that in-app updates check
    /// against. Defaults to the official repo so fresh installs are
    /// pre-wired; editable in Settings. The repo must be public and its
    /// releases must carry a published .exe or .zip asset (the bundled
    /// GitHub Actions workflow does this on every v* tag push).</summary>
    public string UpdateRepo { get; set; } = "motter/TaskNinja";

    /// <summary>Check for updates in the background shortly after
    /// startup. Never interrupts with dialogs — a newer version just
    /// shows a status-bar hint. Manual checks live in Settings and the
    /// tray menu.</summary>
    public bool AutoCheckForUpdates { get; set; } = true;

    // ── Daily digest notifications ──────────────────────────────────
    // The daily summary popup shows overdue + due-today tasks once a day.
    // Settings here let the user pick a time, snooze the popup, or disable
    // it entirely.

    /// <summary>If true, show the daily summary popup. If false, no popups
    /// — the user relies on the tray badge / app for awareness.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>What time of day the daily digest pops. Default 8:00 AM.
    /// Stored as "HH:mm" string for round-tripping; the service parses
    /// it into a TimeSpan at runtime. We use string instead of TimeSpan
    /// directly because System.Text.Json doesn't serialize TimeSpan
    /// well in older configurations.</summary>
    public string DailyDigestTime { get; set; } = "08:00";

    /// <summary>Date the digest was last shown (YYYY-MM-DD). Used to
    /// suppress re-popping the same day. Null = never shown.</summary>
    public string? LastDigestShownDate { get; set; }

    /// <summary>If the user clicked "Remind me later" on the digest,
    /// this is when to re-pop it. Null = no pending re-pop.</summary>
    public DateTime? DigestRemindAt { get; set; }
}
