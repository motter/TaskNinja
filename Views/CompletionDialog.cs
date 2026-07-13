using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskNinja.Models;

namespace TaskNinja.Views;

/// <summary>
/// Shown after a task is marked Done. Captures an optional comment and
/// "completed by" name, and (for recurring tasks) the next-due date
/// with a skip-recurrence option. One popup covers both concerns so
/// the user doesn't get two-in-a-row for recurring completions.
///
/// Supersedes the v1.0.21 recurrence-only popup. Same behavior for
/// recurrence (smart-clamp message, skip option) plus the new comment
/// and by-line fields. For non-recurring tasks the recurrence section
/// is hidden — popup degrades to a small "anything to note?" dialog.</summary>
public static class CompletionDialog
{
    public class Result
    {
        public string CompletedBy { get; set; } = "";
        public string Comment { get; set; } = "";
        /// <summary>For recurring tasks: did the user want to spawn a
        /// next instance? Always false for non-recurring tasks.</summary>
        public bool SpawnNext { get; set; }
        /// <summary>For recurring tasks: the user-chosen (or
        /// proposed) next-due date. Null if SpawnNext is false.</summary>
        public DateTime? NextDue { get; set; }
        /// <summary>For recurring tasks: when to make the spawned
        /// instance VISIBLE. Defaults to a pattern-dependent offset
        /// before due (e.g. weekly = due-2d, monthly = due-7d). User
        /// can edit or clear to "show immediately".</summary>
        public DateTime? ShowOnDate { get; set; }
        /// <summary>True if the user explicitly unchecked "hide until
        /// closer to due" — distinguishes "no defer" from "we just
        /// didn't compute one". Lets the spawn logic skip the
        /// auto-default and use null instead.</summary>
        public bool ShowOnExplicitlyCleared { get; set; }
    }

    /// <summary>Show the popup synchronously and return the user's
    /// input. For recurring tasks, pass the proposed next-due from
    /// <c>MainViewModel.ComputeNextRecurrence</c>; for non-recurring,
    /// pass null and the recurrence block won't render.
    ///
    /// Defaults: CompletedBy pre-fills with the task's responsible
    /// person if set (so for solo use you can usually just hit Save).
    /// </summary>
    public static Result Show(Window owner, TaskItem completed,
        DateTime? proposedNextDue, DateTime? proposedShowOn, IEnumerable<string> knownPeople)
    {
        var result = new Result
        {
            CompletedBy = completed.ResponsiblePerson ?? "",
            Comment = "",
            SpawnNext = proposedNextDue.HasValue,  // default ON for recurring
            NextDue = proposedNextDue,
        };

        var dlg = new Window
        {
            Title = "Mark done",
            Owner = owner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
        };

        var chrome = new Border
        {
            Background = (Brush)Application.Current.Resources["BgBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        chrome.Child = root;

        // ── Header (also drag handle) ─────────────────────────────────
        var header = new TextBlock
        {
            Text = proposedNextDue.HasValue
                ? "✅  Completion + next occurrence"
                : "✅  Mark done",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.SizeAll,
        };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { dlg.DragMove(); } catch { }
            }
        };
        root.Children.Add(header);

        // Task title for context — truncated so long titles don't
        // blow out the layout. Hover for the full title.
        root.Children.Add(new TextBlock
        {
            Text = $"'{Truncate(completed.Title, 70)}'",
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            ToolTip = completed.Title,
            Margin = new Thickness(0, 0, 0, 14),
        });

        // ── Completed by ──────────────────────────────────────────────
        root.Children.Add(SmallLabel("Completed by (optional)"));
        var byBox = new ComboBox
        {
            IsEditable = true,
            Style = (Style)Application.Current.Resources["DarkComboBoxStyle"],
            Margin = new Thickness(0, 0, 0, 10),
            Text = result.CompletedBy,
        };
        // Populate with known people (from the responsible-person
        // dropdown). User can also type a fresh name freely.
        foreach (var person in knownPeople.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            byBox.Items.Add(person);
        }
        root.Children.Add(byBox);

        // ── Comment ───────────────────────────────────────────────────
        root.Children.Add(SmallLabel("Comment (optional)"));
        var commentBox = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 60,
            MaxHeight = 120,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Anything to note about this completion?",
        };
        root.Children.Add(commentBox);

        // ── Recurrence section (only for recurring tasks) ─────────────
        // Widgets declared at outer scope so the Save handler can read
        // them without struggling with closure semantics. Null when
        // the task isn't recurring.
        DatePicker? nextDuePicker = null;
        CheckBox? spawnCheck = null;
        CheckBox? hideUntilCheck = null;
        DatePicker? showOnPicker = null;
        if (proposedNextDue is { } proposed)
        {
            // Soft separator between "completion" and "recurrence" so
            // the user reads them as related-but-distinct blocks.
            root.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 12),
            });

            spawnCheck = new CheckBox
            {
                Content = "Schedule next occurrence",
                IsChecked = true,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
            };
            root.Children.Add(spawnCheck);

            // Smart-clamp explanation when proposed != naive previous-
            // due + interval. Same logic as RecurrenceConfirmDialog
            // had in v1.0.21.
            if (ComputeNaiveNext(completed) is { } naiveDate && naiveDate != proposed)
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"(Original schedule would have been {naiveDate:MMM d}, but that's already past — bumping forward.)",
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 0, 0, 6),
                });
            }

            var intervalLabel = completed.RecurrenceInterval == 1
                ? completed.Recurrence.ToString().ToLowerInvariant()
                : $"every {completed.RecurrenceInterval} {PluralizeUnit(completed.Recurrence)}";
            root.Children.Add(new TextBlock
            {
                Text = $"Repeats: {intervalLabel}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                Margin = new Thickness(20, 0, 0, 4),
            });

            nextDuePicker = new DatePicker
            {
                SelectedDate = proposed,
                Style = (Style)Application.Current.Resources["DarkDatePickerStyle"],
                Margin = new Thickness(20, 0, 0, 10),
                IsEnabled = true,
            };
            root.Children.Add(nextDuePicker);

            // ── "Hide until" controls (auto-defer) ────────────────────
            // Recurring tasks spawn IMMEDIATELY on completion but stay
            // hidden from normal views until N days before due. This
            // prevents the "I just finished it, why is the next one
            // already in my list" clutter. Defaults pulled from
            // VisibilityWindowFor on the vm. User can edit the date or
            // uncheck entirely to show immediately.
            //
            // hideUntilCheck.IsChecked == true → use the picker's date
            // hideUntilCheck.IsChecked == false → no defer, show now
            hideUntilCheck = new CheckBox
            {
                Content = "Hide until closer to due",
                IsChecked = proposedShowOn.HasValue,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                FontSize = 12,
                Margin = new Thickness(20, 0, 0, 4),
                ToolTip = "Recurring tasks stay hidden from buckets and 'All' until this date. " +
                          "Find them under the 'Scheduled' filter while hidden.",
            };
            root.Children.Add(hideUntilCheck);

            showOnPicker = new DatePicker
            {
                // Default: a sensible date even when unchecked, so flipping
                // the checkbox on doesn't require picking a date from
                // scratch.
                SelectedDate = proposedShowOn ?? DateTime.Today,
                Style = (Style)Application.Current.Resources["DarkDatePickerStyle"],
                Margin = new Thickness(40, 0, 0, 14),
                IsEnabled = proposedShowOn.HasValue,
            };
            root.Children.Add(showOnPicker);
            // Bind checkbox ↔ picker enabled state.
            hideUntilCheck.Checked += (_, _) => showOnPicker.IsEnabled = true;
            hideUntilCheck.Unchecked += (_, _) => showOnPicker.IsEnabled = false;

            // The "Schedule next" master checkbox disables BOTH child
            // controls when off — they're meaningless without a spawn.
            spawnCheck.Checked += (_, _) =>
            {
                nextDuePicker.IsEnabled = true;
                hideUntilCheck.IsEnabled = true;
                showOnPicker.IsEnabled = hideUntilCheck.IsChecked == true;
            };
            spawnCheck.Unchecked += (_, _) =>
            {
                nextDuePicker.IsEnabled = false;
                hideUntilCheck.IsEnabled = false;
                showOnPicker.IsEnabled = false;
            };
        }

        // ── Buttons ──────────────────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelBtn = new Button
        {
            Content = "Skip details",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
            ToolTip = "Close without recording a comment. Task stays Done.",
            IsCancel = true,
        };
        cancelBtn.Click += (_, _) =>
        {
            // Treat "Skip details" like Save-with-empty-fields: still
            // commits whatever's in the by-box / spawn-checkbox so the
            // user doesn't lose typed input. Just doesn't force them
            // to acknowledge.
            CaptureResultFromWidgets();
            dlg.DialogResult = false;
        };
        var saveBtn = new Button
        {
            Content = "Save",
            Padding = new Thickness(18, 6, 18, 6),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"],
            IsDefault = true,
        };
        saveBtn.Click += (_, _) =>
        {
            CaptureResultFromWidgets();
            dlg.DialogResult = true;
        };

        // Local helper — both Save and Skip-details paths capture the
        // same widget state into Result. Pulled out so the two
        // closures don't drift.
        void CaptureResultFromWidgets()
        {
            result.CompletedBy = byBox.Text ?? "";
            result.Comment = commentBox.Text ?? "";
            result.SpawnNext = spawnCheck?.IsChecked == true;
            result.NextDue = nextDuePicker?.SelectedDate;
            // Hide-until handling:
            //   • checkbox checked + picker has date → defer to that date
            //   • checkbox UNchecked (user said "show immediately") →
            //     explicitly clear so the spawn doesn't re-apply the default
            //   • no recurring task → leave both null
            if (hideUntilCheck is not null)
            {
                if (hideUntilCheck.IsChecked == true)
                {
                    result.ShowOnDate = showOnPicker?.SelectedDate;
                    result.ShowOnExplicitlyCleared = false;
                }
                else
                {
                    result.ShowOnDate = null;
                    result.ShowOnExplicitlyCleared = true;
                }
            }
        }
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(saveBtn);
        root.Children.Add(btnRow);

        dlg.Content = chrome;
        dlg.Loaded += (_, _) => commentBox.Focus();  // jump straight to typing
        dlg.ShowDialog();
        return result;
    }

    // ── Helpers (mirrored from the old RecurrenceConfirmDialog) ──────

    private static TextBlock SmallLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
        Margin = new Thickness(0, 0, 0, 3),
    };

    private static string PluralizeUnit(RecurrencePattern p) => p switch
    {
        RecurrencePattern.Daily => "days",
        RecurrencePattern.Weekly => "weeks",
        RecurrencePattern.Monthly => "months",
        RecurrencePattern.Yearly => "years",
        _ => "occurrences",
    };

    private static DateTime? ComputeNaiveNext(TaskItem t)
    {
        if (t.DueDate is not { } due) return null;
        var n = Math.Max(1, t.RecurrenceInterval);
        return t.Recurrence switch
        {
            RecurrencePattern.Daily => due.AddDays(n),
            RecurrencePattern.Weekly => due.AddDays(7 * n),
            RecurrencePattern.Monthly => due.AddMonths(n),
            RecurrencePattern.Yearly => due.AddYears(n),
            _ => (DateTime?)null,
        };
    }

    private static string Truncate(string s, int max)
        => s.Length > max ? s.Substring(0, max - 1) + "…" : s;
}
