using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskNinja.Models;
using TaskNinja.ViewModels;

namespace TaskNinja.Views;

/// <summary>
/// Weekly activity report. Aggregates state-history data captured by
/// <see cref="TaskItem.StateHistory"/> (added in v1.0.16) into per-week
/// rollups. The user can navigate to any past week with prev/next.
///
/// What's shown:
///   • Headline counters: created, completed, in-progress transitions,
///     still-open snapshot at week end
///   • Daily activity chart: per-day created vs done bars
///   • By-bucket: how many created and completed in each bucket
///   • Completed list: which tasks finished, when, and where
///
/// Layout is intentionally text + simple shapes (no chart library).
/// Bars are just rectangles sized to a max-value normalizer.
/// </summary>
public class WeeklyReportDialog
{
    private static Window? _currentWindow;

    public static void Show(Window owner, MainViewModel vm)
    {
        if (_currentWindow is not null)
        {
            _currentWindow.Activate();
            return;
        }

        var dialog = new Window
        {
            Title = "TaskNinja — Weekly report",
            Owner = owner,
            Width = 640,
            MinHeight = 500,
            MaxHeight = 820,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
        };
        _currentWindow = dialog;
        dialog.Closed += (_, _) => _currentWindow = null;

        // Outer dark chrome
        var chrome = new Border
        {
            Background = (Brush)Application.Current.Resources["BgBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
        };
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // week nav
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // body
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer
        chrome.Child = rootGrid;

        // ── Header ────────────────────────────────────────────────────
        // Doubles as drag handle. The window has WindowStyle=None for
        // custom dark chrome — without a default titlebar, we need to
        // wire DragMove on a top-region element so the user can move it.
        var header = MakeHeader();
        header.Cursor = System.Windows.Input.Cursors.SizeAll;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try { dialog.DragMove(); } catch { /* swallow edge cases */ }
            }
        };
        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // ── Week navigator ────────────────────────────────────────────
        // State for which week we're viewing. Starts at the current week
        // (the most-recently-completed Monday). Prev/Next buttons mutate
        // this and rebuild the body.
        var weekStart = WeekStart(DateTime.Today);

        var navBar = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = System.Windows.Input.Cursors.SizeAll,
        };
        // Extending the drag region: the header alone is a thin band,
        // and users instinctively try to drag from the bigger area
        // below (where the week label and nav buttons live). Wiring
        // DragMove on navBar gives them a much larger surface area.
        // The prev/next buttons handle their own clicks (they live
        // inside the navBar and will receive the MouseLeftButtonDown
        // first), so clicking those still navigates instead of dragging.
        navBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try { dialog.DragMove(); } catch { /* swallow edge cases */ }
            }
        };
        var navGrid = new Grid();
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prevBtn = MakeNavButton("◀  Previous week");
        var nextBtn = MakeNavButton("Next week  ▶");
        var weekLabel = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(prevBtn, 0);
        Grid.SetColumn(weekLabel, 1);
        Grid.SetColumn(nextBtn, 2);
        navGrid.Children.Add(prevBtn);
        navGrid.Children.Add(weekLabel);
        navGrid.Children.Add(nextBtn);
        navBar.Child = navGrid;
        Grid.SetRow(navBar, 1);
        rootGrid.Children.Add(navBar);

        // ── Body (scrollable) ─────────────────────────────────────────
        var bodyScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(16, 12, 16, 12),
        };
        Grid.SetRow(bodyScroll, 2);
        rootGrid.Children.Add(bodyScroll);

        // The body is rebuilt every time the week changes OR the user
        // clicks a card to drill into a category. drillMode is one of:
        //   "None"        — overview (chart, bucket, completed list)
        //   "Created"     — list of tasks created this week
        //   "Completed"   — list of tasks completed this week
        //   "InProgress"  — list of tasks moved to In-progress this week
        //   "Open"        — list of tasks still open at week end
        // Switching weeks resets drillMode to None so the user lands
        // back at the overview for the new week.
        string drillMode = "None";

        void RebuildBody()
        {
            var weekEnd = weekStart.AddDays(7);
            weekLabel.Text = $"{weekStart:MMM d} – {weekEnd.AddDays(-1):MMM d, yyyy}";

            // Disable "next" if we're already on the current week.
            nextBtn.IsEnabled = weekStart < WeekStart(DateTime.Today);

            var stats = ComputeStats(vm.AllTasks, weekStart, weekEnd, vm.Buckets);
            bodyScroll.Content = BuildBody(stats, weekStart, weekEnd, drillMode, newMode =>
            {
                // Click same active card again → collapse back to overview.
                // Click a different card → switch to that drill.
                drillMode = (newMode == drillMode) ? "None" : newMode;
                RebuildBody();
            });
            // Always scroll to top after a rebuild so the user sees the
            // updated content from the start (especially helpful when
            // switching drill modes).
            bodyScroll.ScrollToTop();
        }

        prevBtn.Click += (_, _) =>
        {
            weekStart = weekStart.AddDays(-7);
            drillMode = "None";  // reset drill on week change
            RebuildBody();
        };
        nextBtn.Click += (_, _) =>
        {
            weekStart = weekStart.AddDays(7);
            drillMode = "None";  // reset drill on week change
            RebuildBody();
        };

        // ── Footer ────────────────────────────────────────────────────
        var footer = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(0, 0, 6, 6),
        };
        var footerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var closeBtn = new Button
        {
            Content = "Close",
            Padding = new Thickness(20, 5, 20, 5),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"],
            IsDefault = true,
            IsCancel = true,
        };
        closeBtn.Click += (_, _) => dialog.Close();
        footerRow.Children.Add(closeBtn);
        footer.Child = footerRow;
        Grid.SetRow(footer, 3);
        rootGrid.Children.Add(footer);

        // Initial build
        RebuildBody();

        dialog.Content = chrome;
        dialog.Show();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static Border MakeHeader()
    {
        var header = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = "📊",
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Weekly report",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Child = stack;
        return header;
    }

    private static Button MakeNavButton(string label)
    {
        return new Button
        {
            Content = label,
            Padding = new Thickness(10, 4, 10, 4),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
            FontSize = 12,
        };
    }

    private static FrameworkElement BuildBody(WeekStats stats, DateTime weekStart, DateTime weekEnd,
        string drillMode, Action<string> onCardClick)
    {
        var stack = new StackPanel();

        // Headline counters — clickable. The active card (matching drillMode)
        // gets an accent border to show the user which list they're viewing.
        stack.Children.Add(BuildCounterRow(stats, drillMode, onCardClick));

        // Drilled view — replace the overview (chart + bucket + completed)
        // with a single detailed list for the selected category. Checked
        // BEFORE the empty-state because a week with no created/completed/
        // in-progress activity can still have carryover tasks in
        // "Still open" — the user needs to be able to drill into those.
        if (drillMode != "None")
        {
            stack.Children.Add(BuildDrillList(stats, weekStart, weekEnd, drillMode));
            return stack;
        }

        // Empty-state — no activity in this week (we already passed the
        // drill check, so this only fires for the overview).
        if (stats.CreatedThisWeek.Count == 0 && stats.CompletedThisWeek.Count == 0
            && stats.MovedToInProgressThisWeek.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Nothing happened this week.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 30),
            });
            // Even with no activity, if there are open tasks the user
            // can still drill into them — hint at that explicitly.
            if (stats.OpenAtEndOfWeek > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"{stats.OpenAtEndOfWeek} task{(stats.OpenAtEndOfWeek == 1 ? "" : "s")} still open from earlier weeks — click \"Still open\" above to see them.",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 400,
                });
            }
            return stack;
        }

        // Overview — daily chart, by-bucket breakdown, completed list.

        // Daily activity
        stack.Children.Add(SectionHeader("Daily activity"));
        stack.Children.Add(BuildDailyChart(stats, weekStart));

        // By bucket
        if (stats.ByBucket.Count > 1)  // only show if there's variety
        {
            stack.Children.Add(SectionHeader("By bucket"));
            stack.Children.Add(BuildBucketBreakdown(stats));
        }

        // Completed this week
        if (stats.CompletedThisWeek.Count > 0)
        {
            stack.Children.Add(SectionHeader($"Completed this week ({stats.CompletedThisWeek.Count})"));
            stack.Children.Add(BuildCompletedList(stats));
        }

        // Hint that the cards are clickable. Subtle — just a small caption
        // under the counter row. Most users will discover via hover anyway.
        stack.Children.Add(new TextBlock
        {
            Text = "Tip: click any counter card above to see the full list.",
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return stack;
    }

    private static FrameworkElement BuildCounterRow(WeekStats stats, string drillMode, Action<string> onCardClick)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddCounterCard(grid, 0, "Created",     "Created",    stats.CreatedThisWeek.Count.ToString(),
            (Brush)Application.Current.Resources["TextBrush"],   drillMode, onCardClick);
        AddCounterCard(grid, 1, "Completed",   "Completed",  stats.CompletedThisWeek.Count.ToString(),
            (Brush)Application.Current.Resources["AccentBrush"], drillMode, onCardClick);
        AddCounterCard(grid, 2, "In-progress", "InProgress", stats.MovedToInProgressThisWeek.Count.ToString(),
            (Brush)Application.Current.Resources["SkyBrush"],    drillMode, onCardClick);
        AddCounterCard(grid, 3, "Still open",  "Open",       stats.OpenAtEndOfWeek.ToString(),
            (Brush)Application.Current.Resources["SubTextBrush"], drillMode, onCardClick);
        return grid;
    }

    /// <summary>Build one counter card. label = display, category = drill key.
    /// When the card's category matches the active drillMode, it's
    /// highlighted with the accent border + a different cursor to
    /// signal "this is what you're looking at right now". Click toggles
    /// the drill via onCardClick (which the caller wires up).</summary>
    private static void AddCounterCard(Grid host, int col, string label, string category,
        string value, Brush numberColor, string drillMode, Action<string> onCardClick)
    {
        var isActive = drillMode == category;
        var card = new Border
        {
            Background = isActive
                ? (Brush)Application.Current.Resources["PanelLightBrush"]
                : (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = isActive
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 3 ? 0 : 4, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = isActive ? "Click to collapse" : $"Click to see the {label.ToLower()} tasks",
        };
        // The card responds to clicks anywhere on its surface — the
        // entire Border becomes the hit-test region. MouseLeftButtonUp
        // (not Down) for the same reason the harvey-ball picker uses
        // it: avoids the spurious-MouseUp dismissing other popups.
        card.MouseLeftButtonUp += (_, _) => onCardClick(category);

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = numberColor,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        card.Child = content;
        Grid.SetColumn(card, col);
        host.Children.Add(card);
    }

    private static TextBlock SectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 12, 0, 6),
        };
    }

    /// <summary>7-row chart showing created and completed counts per
    /// day of the report week. Each row has the day label, a created
    /// bar (sky blue), a completed bar (amber), and the numeric counts.
    /// Bars are scaled relative to the day with the most activity so
    /// the visualization stays readable regardless of magnitude.</summary>
    private static FrameworkElement BuildDailyChart(WeekStats stats, DateTime weekStart)
    {
        var grid = new Grid();
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions[0].Width = new GridLength(48);   // day label
        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);  // bar area
        grid.ColumnDefinitions[2].Width = new GridLength(60);   // created count
        grid.ColumnDefinitions[3].Width = new GridLength(60);   // completed count
        for (int i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Normalizer — the largest single-day value across both metrics
        // sets the max bar width. Avoids divide-by-zero with Max(1, ...).
        int maxValue = Math.Max(1,
            Math.Max(stats.DailyCreated.Max(), stats.DailyCompleted.Max()));

        for (int day = 0; day < 7; day++)
        {
            var date = weekStart.AddDays(day);
            var dayName = date.ToString("ddd");

            var dayLabel = new TextBlock
            {
                Text = dayName,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3),
            };
            Grid.SetColumn(dayLabel, 0);
            Grid.SetRow(dayLabel, day);
            grid.Children.Add(dayLabel);

            // Bar area: two stacked horizontal bars, created (top) and
            // completed (bottom). Each is at most 80% of the column
            // width to leave breathing room visually.
            var barStack = new StackPanel { Margin = new Thickness(0, 4, 8, 4) };
            barStack.Children.Add(MakeBar(stats.DailyCreated[day], maxValue,
                (Brush)Application.Current.Resources["SkyBrush"]));
            barStack.Children.Add(MakeBar(stats.DailyCompleted[day], maxValue,
                (Brush)Application.Current.Resources["AccentBrush"]));
            Grid.SetColumn(barStack, 1);
            Grid.SetRow(barStack, day);
            grid.Children.Add(barStack);

            var createdText = new TextBlock
            {
                Text = $"{stats.DailyCreated[day]} new",
                FontSize = 10,
                Foreground = stats.DailyCreated[day] > 0
                    ? (Brush)Application.Current.Resources["SkyBrush"]
                    : (Brush)Application.Current.Resources["DimTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(createdText, 2);
            Grid.SetRow(createdText, day);
            grid.Children.Add(createdText);

            var completedText = new TextBlock
            {
                Text = $"{stats.DailyCompleted[day]} done",
                FontSize = 10,
                Foreground = stats.DailyCompleted[day] > 0
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["DimTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(completedText, 3);
            Grid.SetRow(completedText, day);
            grid.Children.Add(completedText);
        }
        return grid;
    }

    private static FrameworkElement MakeBar(int value, int maxValue, Brush color)
    {
        // Use a Grid with a fixed-width "track" and a Rectangle whose
        // width is bound to the proportional value. The grid itself is
        // stretch-width so it fills its parent column.
        var track = new Grid { Height = 8, Margin = new Thickness(0, 1, 0, 1) };
        track.Background = (Brush)Application.Current.Resources["PanelLightBrush"];
        if (value <= 0) return track;
        // Rectangle child sized to the fraction. Use ColumnDefinitions
        // to set up a proportional layout: filled portion + empty rest.
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(maxValue - value, GridUnitType.Star) });
        var fill = new System.Windows.Shapes.Rectangle
        {
            Fill = color,
            RadiusX = 2, RadiusY = 2,
        };
        Grid.SetColumn(fill, 0);
        track.Children.Add(fill);
        return track;
    }

    private static FrameworkElement BuildBucketBreakdown(WeekStats stats)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        // One row per bucket. Sorted by completed-this-week descending so
        // the most-active bucket appears first.
        var ordered = stats.ByBucket
            .OrderByDescending(kv => kv.Value.Completed)
            .ThenByDescending(kv => kv.Value.Created)
            .ToList();
        int maxBucketValue = Math.Max(1, ordered.Max(kv =>
            Math.Max(kv.Value.Created, kv.Value.Completed)));

        for (int i = 0; i < ordered.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bucketName = ordered[i].Key;
            var counts = ordered[i].Value;

            var nameBlock = new TextBlock
            {
                Text = "📂 " + bucketName,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(nameBlock, 0); Grid.SetRow(nameBlock, i);
            grid.Children.Add(nameBlock);

            var barStack = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            barStack.Children.Add(MakeBar(counts.Created, maxBucketValue,
                (Brush)Application.Current.Resources["SkyBrush"]));
            barStack.Children.Add(MakeBar(counts.Completed, maxBucketValue,
                (Brush)Application.Current.Resources["AccentBrush"]));
            Grid.SetColumn(barStack, 1); Grid.SetRow(barStack, i);
            grid.Children.Add(barStack);

            var createdText = new TextBlock
            {
                Text = $"{counts.Created} new",
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["SkyBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(createdText, 2); Grid.SetRow(createdText, i);
            grid.Children.Add(createdText);

            var completedText = new TextBlock
            {
                Text = $"{counts.Completed} done",
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(completedText, 3); Grid.SetRow(completedText, i);
            grid.Children.Add(completedText);
        }
        return grid;
    }

    private static FrameworkElement BuildCompletedList(WeekStats stats)
    {
        var stack = new StackPanel();
        foreach (var (task, completedAt) in stats.CompletedThisWeek.OrderBy(c => c.completedAt))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            titleStack.Children.Add(new TextBlock
            {
                Text = "●  ",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = task.Title,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 380,
            });
            Grid.SetColumn(titleStack, 0);
            row.Children.Add(titleStack);

            var timeBlock = new TextBlock
            {
                Text = completedAt.ToString("ddd h:mm tt"),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(timeBlock, 1);
            row.Children.Add(timeBlock);

            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>Build the drill-down list shown when a counter card is
    /// active. Replaces the chart/bucket/completed overview with a
    /// single focused list of the tasks behind that category's number.
    ///
    /// Each row shows the task's status glyph, title, bucket chip,
    /// and a context-appropriate timestamp (created date for Created,
    /// completion time for Completed, etc.).</summary>
    private static FrameworkElement BuildDrillList(WeekStats stats, DateTime weekStart,
        DateTime weekEnd, string drillMode)
    {
        var stack = new StackPanel();
        var (headerText, items) = GetDrillItems(stats, drillMode);

        stack.Children.Add(SectionHeader(headerText));

        if (items.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Nothing in this category for this week.",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 12, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return stack;
        }

        foreach (var (task, timestamp, timestampLabel) in items)
        {
            stack.Children.Add(BuildDrillRow(task, timestamp, timestampLabel));
        }

        // Footer hint — explains the toggle behavior so the user
        // knows how to get back to the overview.
        stack.Children.Add(new TextBlock
        {
            Text = "Click the active card again to collapse this list.",
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return stack;
    }

    /// <summary>Map drill mode to the matching task list + section header.
    /// Returns a list of (task, sort timestamp, display label) tuples so
    /// each category can use a context-relevant timestamp (created,
    /// completed, or in-progress).</summary>
    private static (string header, List<(TaskItem task, DateTime timestamp, string label)> items)
        GetDrillItems(WeekStats stats, string drillMode)
    {
        switch (drillMode)
        {
            case "Created":
                return ($"Created this week ({stats.CreatedThisWeek.Count})",
                    stats.CreatedThisWeek
                        .OrderBy(t => t.CreatedAt)
                        .Select(t => (t, t.CreatedAt, "created"))
                        .ToList());

            case "Completed":
                return ($"Completed this week ({stats.CompletedThisWeek.Count})",
                    stats.CompletedThisWeek
                        .OrderBy(c => c.completedAt)
                        .Select(c => (c.task, c.completedAt, "done"))
                        .ToList());

            case "InProgress":
                return ($"Moved to In-progress this week ({stats.MovedToInProgressThisWeek.Count})",
                    stats.MovedToInProgressThisWeek
                        .Select(t =>
                        {
                            // Use the in-week InProgress transition time.
                            // Multiple transitions could exist; take the
                            // last one within the window.
                            var inProgEntry = t.StateHistory
                                .Where(h => h.To == "InProgress")
                                .OrderByDescending(h => h.At)
                                .FirstOrDefault();
                            return (t, inProgEntry?.At ?? t.CreatedAt, "in-prog");
                        })
                        .OrderBy(x => x.Item2)
                        .ToList());

            case "Open":
                return ($"Still open at end of week ({stats.OpenAtEndOfWeek})",
                    stats.OpenAtEndOfWeekList
                        .OrderBy(t => t.CreatedAt)
                        .Select(t => (t, t.CreatedAt, "created"))
                        .ToList());

            default:
                return ("", new List<(TaskItem, DateTime, string)>());
        }
    }

    /// <summary>One row in the drill list: glyph + title + timestamp.
    /// Compact, hover-cursor on the title (in case we want to wire
    /// click-to-open-editor later).</summary>
    private static FrameworkElement BuildDrillRow(TaskItem task, DateTime timestamp, string timestampLabel)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };

        // State glyph — matches the row styling in the main list so the
        // status is immediately visible. Color hints at the state too.
        var glyph = task.State switch
        {
            TaskState.Done       => "●",
            TaskState.InProgress => "◐",
            _                    => "○",
        };
        var glyphColor = task.State switch
        {
            TaskState.Done       => (Brush)Application.Current.Resources["AccentBrush"],
            TaskState.InProgress => (Brush)Application.Current.Resources["SkyBrush"],
            _                    => (Brush)Application.Current.Resources["SubTextBrush"],
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = glyph + "  ",
            FontSize = 13,
            Foreground = glyphColor,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 420,
        });
        Grid.SetColumn(titleStack, 0);
        row.Children.Add(titleStack);

        var timeBlock = new TextBlock
        {
            Text = $"{timestamp:ddd h:mm tt}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"{timestampLabel} {timestamp:F}",
        };
        Grid.SetColumn(timeBlock, 1);
        row.Children.Add(timeBlock);

        return row;
    }

    // ── Stats computation ─────────────────────────────────────────────

    /// <summary>Monday-of-week for the given date. Sunday counts as part
    /// of the previous week (matches Outlook + most US calendars).</summary>
    private static DateTime WeekStart(DateTime d)
    {
        int diff = (7 + (int)d.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return d.Date.AddDays(-diff);
    }

    private class BucketCounts
    {
        public int Created;
        public int Completed;
    }

    private class WeekStats
    {
        public List<TaskItem> CreatedThisWeek = new();
        public List<(TaskItem task, DateTime completedAt)> CompletedThisWeek = new();
        public List<TaskItem> MovedToInProgressThisWeek = new();
        public int OpenAtEndOfWeek;
        /// <summary>The actual task list backing OpenAtEndOfWeek. Used
        /// by the drill-down view; the count is kept alongside for the
        /// counter card's bold number.</summary>
        public List<TaskItem> OpenAtEndOfWeekList = new();
        public int[] DailyCreated = new int[7];
        public int[] DailyCompleted = new int[7];
        public Dictionary<string, BucketCounts> ByBucket = new();
    }

    private static WeekStats ComputeStats(IEnumerable<TaskItem> allTasks,
        DateTime weekStart, DateTime weekEnd, IEnumerable<Bucket> buckets)
    {
        var stats = new WeekStats();
        var bucketName = buckets.ToDictionary(b => b.Id, b => b.Name);

        foreach (var t in allTasks)
        {
            if (t.IsArchived) continue;

            // Bucket lookup once per task (resilient if BucketId references
            // a deleted bucket — fall back to the raw id string).
            var bn = bucketName.TryGetValue(t.BucketId ?? "", out var name) ? name : (t.BucketId ?? "Tasks");

            // Created in this week?
            if (t.CreatedAt >= weekStart && t.CreatedAt < weekEnd)
            {
                stats.CreatedThisWeek.Add(t);
                stats.DailyCreated[(t.CreatedAt - weekStart).Days]++;
                if (!stats.ByBucket.ContainsKey(bn)) stats.ByBucket[bn] = new BucketCounts();
                stats.ByBucket[bn].Created++;
            }

            // Completed in this week? Use StateHistory if available;
            // otherwise fall back to CompletedAt (works for tasks
            // completed before v1.0.16, since CompletedAt has always
            // been tracked). We count the LATEST transition-to-Done
            // within the week if multiple exist.
            DateTime? completedAt = null;
            var transitionsToDone = t.StateHistory
                .Where(h => h.To == "Done" && h.At >= weekStart && h.At < weekEnd)
                .ToList();
            if (transitionsToDone.Count > 0)
            {
                completedAt = transitionsToDone.Max(h => h.At);
            }
            else if (t.CompletedAt is { } completed && completed >= weekStart && completed < weekEnd
                && t.StateHistory.Count == 0)
            {
                // Legacy fallback for tasks predating StateHistory.
                completedAt = completed;
            }
            if (completedAt is { } cAt)
            {
                stats.CompletedThisWeek.Add((t, cAt));
                stats.DailyCompleted[(cAt - weekStart).Days]++;
                if (!stats.ByBucket.ContainsKey(bn)) stats.ByBucket[bn] = new BucketCounts();
                stats.ByBucket[bn].Completed++;
            }

            // Moved to InProgress in this week?
            if (t.StateHistory.Any(h => h.To == "InProgress" && h.At >= weekStart && h.At < weekEnd))
            {
                stats.MovedToInProgressThisWeek.Add(t);
            }

            // Snapshot: still open at end of week? A task is "open at
            // end of week" if it was created before weekEnd and was
            // not Done at weekEnd. For the current week (in progress),
            // we just use today's state.
            if (t.CreatedAt < weekEnd)
            {
                var openAtEnd = !WasDoneAt(t, weekEnd);
                if (openAtEnd)
                {
                    stats.OpenAtEndOfWeek++;
                    stats.OpenAtEndOfWeekList.Add(t);
                }
            }
        }

        return stats;
    }

    /// <summary>Determine if the task was in the Done state at the
    /// given moment by walking StateHistory. Without history, fall
    /// back to comparing to current state and CompletedAt — imperfect
    /// for past-week reports of legacy tasks but reasonable.</summary>
    private static bool WasDoneAt(TaskItem t, DateTime when)
    {
        if (t.StateHistory.Count > 0)
        {
            // Find the latest transition AT-OR-BEFORE 'when'. The "To"
            // of that transition is the state at that moment.
            var lastTransition = t.StateHistory
                .Where(h => h.At < when)
                .OrderByDescending(h => h.At)
                .FirstOrDefault();
            if (lastTransition is null) return false;  // never transitioned, still in default Open
            return lastTransition.To == "Done";
        }
        // Legacy fallback: assume the task has been in its current
        // state since creation, unless CompletedAt says otherwise.
        if (t.State == TaskState.Done)
        {
            return t.CompletedAt is { } c && c <= when;
        }
        return false;
    }
}
