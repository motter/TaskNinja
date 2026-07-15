using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;   // Cursors (clickable rows)
using System.Windows.Media;
using TaskNinja.Models;
using TaskNinja.ViewModels;

namespace TaskNinja.Views;

/// <summary>
/// Daily summary popup — shows the user's overdue and due-today tasks
/// with quick-action buttons per task. Designed to be the ONE daily
/// interruption from TaskNinja instead of N per-task popups (which
/// would cause notification fatigue).
///
/// Trigger model:
///   • <see cref="NotificationService"/> ticks once a minute and decides
///     whether to call <see cref="Show"/>.
///   • Show is also called explicitly when the user picks "Show daily
///     digest now" from the tray menu.
///
/// Layout:
///   [App icon]  TaskNinja — Daily summary               [Settings ⚙]
///   ─────────────────────────────────────────────────────────────
///   ⚠️  Overdue (3)
///     • Renew domain         [✓] [💤 Tomorrow] [💤 Weekend]
///     • Email Jacob          [✓] [💤 Tomorrow] [💤 Weekend]
///     • Submit timesheet     [✓] [💤 Tomorrow] [💤 Weekend]
///
///   📅 Due today (2)
///     • Standup notes        [✓] [💤 Tomorrow] [💤 Weekend]
///     • Buy birthday card    [✓] [💤 Tomorrow] [💤 Weekend]
///   ─────────────────────────────────────────────────────────────
///                          [Remind me in 2h]  [Got it for today]
///
/// Or the empty state:
///   🎉 You're all caught up. Nothing overdue, nothing due today.
///                                              [Got it for today]
/// </summary>
public class DailyDigestPopup
{
    /// <summary>Close the digest if it's showing. Used when opening a
    /// task from a row, so the editor isn't stacked behind the popup.</summary>
    public static void CloseIfOpen()
    {
        _currentWindow?.Close();
        _currentWindow = null;
    }

    private static Window? _currentWindow;

    /// <summary>Set by Show() for the life of the popup: opens a task in
    /// the detail editor. BuildTaskRow is a static helper several layers
    /// down, so a field beats threading the callback through every
    /// signature. Cleared on close.</summary>
    private static Action<TaskItem>? _openTask;

    /// <summary>
    /// Show the daily digest. If one is already on screen, brings it
    /// to front instead of opening a duplicate.
    /// </summary>
    public static void Show(Window owner, MainViewModel vm,
        Action<DateTime?>? onRemindLater = null,
        Action? onDismissed = null,
        Action? onShowSettings = null,
        Action<TaskItem>? onOpenTask = null)
    {
        // Stash the open-task callback for BuildTaskRow (a static helper
        // several layers down). Cleared when the window closes.
        _openTask = onOpenTask;
        if (_currentWindow is not null)
        {
            _currentWindow.Activate();
            return;
        }

        var today = DateTime.Today;
        var overdue = vm.AllTasks
            .Where(t => !t.IsArchived && t.State != TaskState.Done
                     && t.DueDate is { } d && d.Date < today)
            .OrderBy(t => t.DueDate)
            .ToList();
        var dueToday = vm.AllTasks
            .Where(t => !t.IsArchived && t.State != TaskState.Done
                     && t.DueDate is { } d && d.Date == today)
            .OrderBy(t => t.BucketId)
            .ToList();

        var win = new Window
        {
            Title = "TaskNinja — Daily summary",
            Owner = owner,
            Width = 540,
            MinHeight = 240,
            MaxHeight = 720,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
        };
        _currentWindow = win;
        win.Closed += (_, _) => { _currentWindow = null; _openTask = null; };

        // Outer chrome — rounded border to match the main window
        var chrome = new Border
        {
            Background = (Brush)Application.Current.Resources["BgBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
        };

        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer
        chrome.Child = root;

        // ── Header ────────────────────────────────────────────────────
        // Doubles as the drag handle for moving the window. WindowStyle
        // is None (so we can paint our own dark chrome), which means
        // Windows doesn't provide a default titlebar to drag. Wiring
        // MouseLeftButtonDown → win.DragMove() on the header gives us
        // the same drag behavior without giving up the custom chrome.
        var header = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.SizeAll,  // hint: draggable
        };
        header.MouseLeftButtonDown += (_, e) =>
        {
            // Only initiate drag on a single button-down — DragMove
            // throws if called when the mouse isn't pressed, which can
            // happen during rapid clicks. ChangedButton check is a
            // simple guard.
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try { win.DragMove(); } catch { /* swallow drag-edge cases */ }
            }
        };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new TextBlock
        {
            Text = "☑️",
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Daily summary",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        // Right-side date
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerStack, 0);
        headerGrid.Children.Add(headerStack);
        var dateLabel = new TextBlock
        {
            Text = today.ToString("dddd, MMM d"),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(dateLabel, 1);
        headerGrid.Children.Add(dateLabel);
        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // ── Body ──────────────────────────────────────────────────────
        var bodyScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 10, 14, 10),
        };
        var body = new StackPanel();
        bodyScroll.Content = body;
        Grid.SetRow(bodyScroll, 1);
        root.Children.Add(bodyScroll);

        if (overdue.Count == 0 && dueToday.Count == 0)
        {
            // Empty-state celebration
            body.Children.Add(new TextBlock
            {
                Text = "🎉  You're all caught up.",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 6),
            });
            body.Children.Add(new TextBlock
            {
                Text = "Nothing overdue, nothing due today.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
            });
        }
        else
        {
            if (overdue.Count > 0)
            {
                body.Children.Add(SectionHeader($"⚠️  Overdue ({overdue.Count})",
                    (Brush)Application.Current.Resources["AccentBrush"]));
                foreach (var t in overdue)
                    body.Children.Add(BuildTaskRow(t, vm, win));
            }
            if (dueToday.Count > 0)
            {
                body.Children.Add(SectionHeader($"📅  Due today ({dueToday.Count})",
                    (Brush)Application.Current.Resources["TextBrush"]));
                foreach (var t in dueToday)
                    body.Children.Add(BuildTaskRow(t, vm, win));
            }
        }

        // ── Footer ────────────────────────────────────────────────────
        var footer = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(0, 0, 6, 6),
        };
        var footerRow = new Grid();
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = overdue.Count + dueToday.Count > 0
                ? $"{overdue.Count + dueToday.Count} task{(overdue.Count + dueToday.Count == 1 ? "" : "s")} need your attention"
                : "All done!",
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(hint, 0);
        footerRow.Children.Add(hint);

        // Settings shortcut — visible in the digest itself so users can
        // find the "change daily summary time" control without hunting
        // through the tray menu. Only rendered if a callback is wired.
        if (onShowSettings is not null)
        {
            var settingsBtn = new Button
            {
                Content = "⚙",
                FontSize = 14,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
                ToolTip = "Notification settings — change the time, turn off, etc.",
            };
            settingsBtn.Click += (_, _) => onShowSettings?.Invoke();
            Grid.SetColumn(settingsBtn, 1);
            footerRow.Children.Add(settingsBtn);
        }

        var remindBtn = new Button
        {
            Content = "Remind me in 2h",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        remindBtn.Click += (_, _) =>
        {
            onRemindLater?.Invoke(DateTime.Now.AddHours(2));
            win.Close();
        };
        Grid.SetColumn(remindBtn, 2);
        footerRow.Children.Add(remindBtn);

        var dismissBtn = new Button
        {
            Content = "Got it for today",
            Padding = new Thickness(14, 5, 14, 5),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"],
        };
        dismissBtn.Click += (_, _) =>
        {
            onDismissed?.Invoke();
            win.Close();
        };
        Grid.SetColumn(dismissBtn, 3);
        footerRow.Children.Add(dismissBtn);

        footer.Child = footerRow;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        win.Content = chrome;
        win.Show();
    }

    private static TextBlock SectionHeader(string text, Brush color)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = color,
            Margin = new Thickness(0, 10, 0, 6),
        };
    }

    /// <summary>One task row inside a section. Shows the title + a small
    /// "bucket · person" meta line, plus three quick-action buttons:
    /// ✓ (mark Done), 💤 Tomorrow, 💤 Weekend. Buttons mutate the task
    /// via the view model and refresh the digest popup in place by
    /// closing and re-opening — simpler and correct than trying to
    /// surgically update the existing UI.</summary>
    private static Border BuildTaskRow(TaskItem task, MainViewModel vm, Window digestWin)
    {
        var border = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left side: title + meta line
        var leftStack = new StackPanel();
        leftStack.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        var bucketName = vm.Buckets.FirstOrDefault(b => b.Id == task.BucketId)?.Name ?? "Tasks";
        var metaParts = new List<string> { $"📂 {bucketName}" };
        if (!string.IsNullOrEmpty(task.ResponsiblePerson))
            metaParts.Add($"👤 {task.ResponsiblePerson}");
        if (task.DueDate is { } d)
            metaParts.Add($"📅 {d:MMM d}");
        leftStack.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", metaParts),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(leftStack, 0);
        // Click the title/meta area to open the task in the editor. The
        // action buttons on the right keep their own clicks (they're in
        // a different column and set e.Handled), so quick-triage still
        // works without opening anything.
        if (_openTask is not null)
        {
            leftStack.Background = Brushes.Transparent;  // make whitespace hit-testable
            leftStack.Cursor = Cursors.Hand;
            leftStack.ToolTip = "Click to open this task";
            leftStack.MouseLeftButtonUp += (_, _) => _openTask?.Invoke(task);
        }
        grid.Children.Add(leftStack);

        // Right side: action buttons
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var doneBtn = MakeActionButton("✓", "Mark done");
        doneBtn.Click += (_, _) =>
        {
            task.State = TaskState.Done;
            // The digest is the "quick triage" context — no popup
            // here, since interrupting the flow with a comment dialog
            // defeats the point. Still record a bare CompletionRecord
            // (timestamp only) so the data structure is consistent;
            // the user can add a comment by reopening the task editor.
            task.Completions.Add(new CompletionRecord { At = DateTime.Now });
            if (task.Recurrence != RecurrencePattern.None) vm.SpawnRecurrenceFor(task);
            vm.RebuildVisible();
            vm.OnTaskMutated();
            RefreshDigest(digestWin, vm);
        };
        actions.Children.Add(doneBtn);

        // Snooze button with a dropdown of presets. v1.0.21 had two
        // separate buttons (Tmrw, Wknd) — fine but limited. The user
        // wanted more granular options (1 day, 2 days, 1 week, 2 weeks,
        // arbitrary date). A single button with a menu is much cleaner
        // than 5+ buttons in the row.
        var snoozeBtn = MakeActionButton("💤 ▾", "Push the due date out");
        snoozeBtn.ContextMenu = BuildSnoozeMenu(task, vm, digestWin);
        snoozeBtn.Click += (_, _) =>
        {
            // Open the ContextMenu on left-click (the default is right-click).
            snoozeBtn.ContextMenu.PlacementTarget = snoozeBtn;
            snoozeBtn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            snoozeBtn.ContextMenu.IsOpen = true;
        };
        actions.Children.Add(snoozeBtn);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        border.Child = grid;
        return border;
    }

    /// <summary>Build the snooze dropdown menu for a digest row.
    /// Same options surface in the right-click menu in the main task
    /// list (see MainWindow.xaml.cs), so users learn the menu once
    /// and it works in both places.</summary>
    private static ContextMenu BuildSnoozeMenu(TaskItem task, MainViewModel vm, Window digestWin)
    {
        var menu = new ContextMenu();
        void AddPreset(string label, string preset)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                vm.Snooze(task, preset);
                RefreshDigest(digestWin, vm);
            };
            menu.Items.Add(item);
        }
        AddPreset("Tomorrow",                 "Tomorrow");
        AddPreset("In 2 days",                "2Days");
        AddPreset("This weekend (Saturday)",  "Weekend");
        AddPreset("In 1 week",                "1Week");
        AddPreset("In 2 weeks",               "2Weeks");
        menu.Items.Add(new Separator());

        // Arbitrary date — opens a small popup with a DatePicker.
        var pickItem = new MenuItem { Header = "Pick a date..." };
        pickItem.Click += (_, _) =>
        {
            var picked = DateSnoozePicker.Show(digestWin, task.DueDate);
            if (picked is { } d)
            {
                vm.SnoozeTo(task, d);
                RefreshDigest(digestWin, vm);
            }
        };
        menu.Items.Add(pickItem);
        return menu;
    }

    private static Button MakeActionButton(string content, string tooltip)
    {
        return new Button
        {
            Content = content,
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(3, 0, 0, 0),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
            ToolTip = tooltip,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
    }

    /// <summary>Refresh the digest after a quick action by closing and
    /// re-opening with the latest task list. Simpler than mutating the
    /// existing visual tree in place. Preserves the owner + callbacks
    /// from the existing window. If the latest re-build is empty (user
    /// just cleared their last overdue/today task), we still want the
    /// celebration screen to appear so the user gets feedback.</summary>
    private static void RefreshDigest(Window digestWin, MainViewModel vm)
    {
        var owner = digestWin.Owner;
        digestWin.Close();
        if (owner is not null)
        {
            Show(owner, vm);
        }
    }
}
