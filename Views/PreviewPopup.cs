using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskNinja.Models;
using TaskNinja.Services;

namespace TaskNinja.Views;

/// <summary>
/// A small popup window that appears next to the task list when the user
/// hovers a task row. Shows the task's title, body, URLs (each with a
/// clickable "Open" button), attached images, due date, and tags.
///
/// Hover model:
///   - On row enter   → ScheduleShow() starts a debounce timer; after
///                      280ms (if still on the same row) the popup
///                      materializes.
///   - On row leave   → RequestHide() starts a HIDE-DELAY timer (~150ms).
///                      If the cursor enters the popup window during that
///                      delay, the timer cancels and the popup stays.
///   - On popup leave → RequestHide() restarts the hide timer.
///
/// Positioning:
///   The popup tries to sit just to the LEFT of the main window, aligned
///   to the row's vertical anchor. If there's no room on the left it falls
///   back to the right side. Critically, we use the work area of the
///   MONITOR the main window is on — not SystemParameters.WorkArea (which
///   only returns the primary monitor's bounds). This fixes the bug where
///   the preview popped up on the center monitor while TaskNinja was on a
///   side monitor.
/// </summary>
public class PreviewPopup
{
    private readonly Window _owner;
    private readonly PersistenceService _persistence;
    private readonly Action<TaskItem, TaskState>? _onStateChangeRequested;
    private readonly Func<System.Collections.Generic.IEnumerable<Models.Bucket>>? _getBuckets;
    private readonly Action<TaskItem, string>? _onBucketChangeRequested;
    private Window? _window;
    private TaskItem? _currentTask;
    private DispatcherTimer? _showTimer;
    private DispatcherTimer? _hideTimer;
    private bool _cursorInPopup;
    /// <summary>True while a child popup (e.g. the bucket-picker
    /// ContextMenu) is open. Suppresses auto-hide because the menu's
    /// visual tree lives outside the popup window, so the cursor
    /// moving into it would otherwise trigger MouseLeave → hide.</summary>
    private bool _menuOpen;

    private const int ShowDelayMs = 280;
    private const int HideDelayMs = 180;

    /// <summary>
    /// Construct the preview popup.
    ///   <paramref name="onStateChangeRequested"/> is called when the user
    /// clicks one of the state buttons in the popup.
    ///   <paramref name="getBuckets"/> returns the current bucket list (called
    /// each time the popup builds, so newly-added buckets show up immediately).
    ///   <paramref name="onBucketChangeRequested"/> is called when the user
    /// picks a different bucket from the bucket chip's menu.
    /// </summary>
    public PreviewPopup(Window owner, PersistenceService persistence,
        Action<TaskItem, TaskState>? onStateChangeRequested = null,
        Func<System.Collections.Generic.IEnumerable<Models.Bucket>>? getBuckets = null,
        Action<TaskItem, string>? onBucketChangeRequested = null)
    {
        _owner = owner;
        _persistence = persistence;
        _onStateChangeRequested = onStateChangeRequested;
        _getBuckets = getBuckets;
        _onBucketChangeRequested = onBucketChangeRequested;
    }

    public void ScheduleShow(TaskItem task, double anchorTop)
    {
        _hideTimer?.Stop();
        if (_currentTask == task && _window is not null && _window.IsVisible) return;

        _currentTask = task;
        _showTimer?.Stop();
        _showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowDelayMs) };
        _showTimer.Tick += (_, _) =>
        {
            _showTimer?.Stop();
            if (_currentTask is null) return;
            ActuallyShow(_currentTask, anchorTop);
        };
        _showTimer.Start();
    }

    public void RequestHide()
    {
        _showTimer?.Stop();
        _hideTimer?.Stop();
        // Don't start a hide timer if a child menu (bucket picker, etc.)
        // is currently open — the menu lives in its own visual tree and
        // would otherwise be torn out from under the user mid-click.
        if (_menuOpen) return;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HideDelayMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            if (_cursorInPopup) return;
            if (_menuOpen) return;  // belt-and-suspenders
            _currentTask = null;
            _window?.Hide();
        };
        _hideTimer.Start();
    }

    public void Hide()
    {
        _showTimer?.Stop();
        _hideTimer?.Stop();
        _currentTask = null;
        _window?.Hide();
    }

    private void ActuallyShow(TaskItem task, double anchorTop)
    {
        try
        {
            EnsureWindow();
            if (_window is null) return;

            _window.Content = BuildContent(task);

            double width = 560;
            double height = ComputePreferredHeight(task);
            _window.Width = width;
            _window.Height = height;

            var workArea = GetWorkAreaOfMainWindow();

            double mainLeft = _owner.Left;
            double mainTop = _owner.Top;
            double mainRight = mainLeft + _owner.ActualWidth;

            // Prefer LEFT side of the main window
            double left = mainLeft - width - 8;
            if (left < workArea.Left)
                left = mainRight + 8;
            if (left + width > workArea.Right)
                left = workArea.Right - width - 8;
            if (left < workArea.Left + 4)
                left = workArea.Left + 4;

            double top = mainTop + anchorTop;
            if (top + height > workArea.Bottom)
                top = workArea.Bottom - height - 8;
            if (top < workArea.Top + 8)
                top = workArea.Top + 8;

            _window.Left = left;
            _window.Top = top;
            _window.Show();
        }
        catch (Exception ex)
        {
            Trace.Log("preview", $"show failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve the work area of the monitor that contains the main window.
    /// WPF's SystemParameters.WorkArea returns only the PRIMARY monitor's
    /// work area — useless on multi-monitor setups. We use WinForms Screen
    /// (Win32 GetMonitorInfo under the hood) to find the right monitor.
    /// </summary>
    private Rect GetWorkAreaOfMainWindow()
    {
        try
        {
            var hwnd = new WindowInteropHelper(_owner).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                var wa = screen.WorkingArea;
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
        }
        catch (Exception ex)
        {
            Trace.Log("preview", $"GetWorkAreaOfMainWindow fallback: {ex.Message}");
        }
        // Fallback: virtual screen rect (spans all monitors)
        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new Window
        {
            Owner = _owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Focusable = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
        };
        // Track cursor presence so the user can mouse from row → popup
        // and click links without the popup disappearing.
        _window.MouseEnter += (_, _) =>
        {
            _cursorInPopup = true;
            _hideTimer?.Stop();
        };
        _window.MouseLeave += (_, _) =>
        {
            _cursorInPopup = false;
            RequestHide();
        };
    }

    private double ComputePreferredHeight(TaskItem task)
    {
        // Generous spacing — better too tall than too cramped. The popup
        // is meant to show enough of the task that the user doesn't have
        // to open the editor for routine glance-and-go.
        double h = 180;  // title + state picker + meta chips + footer
        if (!string.IsNullOrEmpty(task.Body)) h += Math.Min(500, task.Body.Length / 1.3 + 140);
        h += task.Attachments.Count * 140;
        var urls = ExtractUrls(task.Body);
        h += urls.Count * 42;
        if (h > 920) h = 920;
        if (h < 420) h = 420;
        return h;
    }

    /// <summary>Extract URLs from body text. Recognizes both
    /// http(s):// URLs and bare www.* domains (which we prefix with
    /// http:// when opening). Mirrors what ClipNinja recognizes.</summary>
    private static System.Collections.Generic.List<string> ExtractUrls(string body)
    {
        var list = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(body)) return list;
        // http(s):// URLs
        foreach (var m in Regex.Matches(body, @"https?://[^\s<>""']+"))
            list.Add(((Match)m).Value);
        // www.* URLs not already prefixed (rare for both to appear back-to-back
        // in real text; this is a heuristic match that handles bare links).
        foreach (var m in Regex.Matches(body, @"(?<!http://|https://)\bwww\.[A-Za-z0-9][^\s<>""']+"))
        {
            var url = ((Match)m).Value;
            // Only add if we haven't seen it as part of an http(s):// match
            if (!list.Any(existing => existing.Contains(url)))
                list.Add(url);
        }
        return list;
    }

    /// <summary>Normalize a URL for Process.Start — prepends http:// to
    /// bare www.* URLs so the OS treats them as web links.</summary>
    private static string NormalizeUrl(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return "http://" + url;
        return url;
    }

    private FrameworkElement BuildContent(TaskItem task)
    {
        var outer = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
        };

        var stack = new StackPanel();

        // Title row: title text + a 📋 copy button on the right that
        // copies the whole task as plain text (title, body, lifecycle
        // dates). The preview's body is a TextBlock (not selectable —
        // that's what keeps hyperlinks clickable), so the button is
        // the copy affordance.
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new TextBlock
        {
            Text = task.Title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(titleBlock, 0);
        titleRow.Children.Add(titleBlock);
        var copyBtn = new Button
        {
            Content = "📋",
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            ToolTip = "Copy task to clipboard (title, notes, dates)",
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        copyBtn.Click += (_, _) =>
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(task.Title);
                if (!string.IsNullOrWhiteSpace(task.Body))
                {
                    sb.AppendLine();
                    sb.AppendLine(task.Body);
                }
                sb.AppendLine();
                sb.Append($"Created {task.CreatedAt:MMM d, yyyy}");
                var st = task.StateHistory.FirstOrDefault(e => e.To == "InProgress");
                if (st is not null) sb.Append($" • Started {st.At:MMM d, yyyy}");
                if (task.State == TaskState.Done && task.CompletedAt is { } ca)
                    sb.Append($" • Done {ca:MMM d, yyyy}");
                Clipboard.SetText(sb.ToString());
                copyBtn.Content = "✓";
                // Flip the glyph back after a moment so the feedback is
                // visible but transient.
                var revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                revert.Tick += (_, _) => { copyBtn.Content = "📋"; revert.Stop(); };
                revert.Start();
            }
            catch { /* clipboard can be locked by another app — non-fatal */ }
        };
        Grid.SetColumn(copyBtn, 1);
        titleRow.Children.Add(copyBtn);
        stack.Children.Add(titleRow);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        // Bucket chip — clickable to change. Resolve the task's bucket
        // from the live bucket list (so renamed buckets show their new
        // name even if the popup was opened before the rename).
        //
        // Skip the chip entirely if the user only has ONE bucket (i.e.
        // they haven't engaged with the bucket feature). Showing "📂 Tasks"
        // alone is visual noise — the chip's value is "I can see and
        // change which bucket this is in", which only matters when there
        // are multiple buckets to move between.
        if (_getBuckets is not null)
        {
            var buckets = _getBuckets().ToList();
            if (buckets.Count > 1)
            {
                var current = buckets.FirstOrDefault(b => b.Id == task.BucketId);
                var bucketName = current?.Name ?? "Tasks";
                meta.Children.Add(MakeBucketChip(task, bucketName, buckets));
            }
        }

        if (!string.IsNullOrEmpty(task.DueLabel))
            meta.Children.Add(MakeChip("📅 " + task.DueLabel, ChipBrushForDue(task.DueState)));
        if (!string.IsNullOrEmpty(task.ResponsiblePerson))
            meta.Children.Add(MakeChip("👤 " + task.ResponsiblePerson, (Brush)Application.Current.Resources["SubTextBrush"]));
        // Subtask progress chip — e.g. "✓ 3 / 5". Only shown when the
        // task has subtasks. Helps the user see at a glance whether a
        // checklist task is in progress without opening the editor.
        // Color shifts to accent (green) when all subtasks are done,
        // since that's a meaningful milestone even if the parent isn't
        // marked Done yet.
        if (task.Subtasks.Count > 0)
        {
            var allDone = task.SubtaskDoneCount == task.Subtasks.Count;
            meta.Children.Add(MakeChip(
                $"✓ {task.SubtaskDoneCount} / {task.Subtasks.Count}",
                allDone
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SkyBrush"]));
        }
        foreach (var tag in task.ParsedTags)
            meta.Children.Add(MakeChip("#" + tag, (Brush)Application.Current.Resources["SkyBrush"]));
        if (meta.Children.Count > 0) stack.Children.Add(meta);

        // ── State picker ──────────────────────────────────────────────
        // Three-button row to set state directly: Open / In progress / Done.
        // Clicking dismisses the popup (state-change feedback is the row
        // updating in the list) and reports back via callback. If no
        // callback was wired, hide the picker entirely.
        if (_onStateChangeRequested is not null)
        {
            var picker = TaskStatePicker.Build(task, target =>
            {
                _onStateChangeRequested.Invoke(task, target);
                Hide();
            });
            picker.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(picker);
        }

        if (!string.IsNullOrEmpty(task.Body) || task.Attachments.Count > 0)
        {
            stack.Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)Application.Current.Resources["BorderBrush"],
                Margin = new Thickness(0, 10, 0, 10),
            });
        }

        if (!string.IsNullOrEmpty(task.Body))
        {
            // TextBlock is unconstrained vertically — let it grow to whatever
            // size the wrapped text needs. The ScrollViewer below caps the
            // VISIBLE area at 500px, and because the TextBlock can exceed
            // that, the scrollbar actually has something to scroll. If we
            // capped the TextBlock here too, it would clip silently and
            // the scrollbar would never activate.
            var bodyBlock = new TextBlock
            {
                Text = task.Body,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                TextWrapping = TextWrapping.Wrap,
            };
            var bodyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = bodyBlock,
                MaxHeight = 500,
            };
            stack.Children.Add(bodyScroll);
        }

        // ── URLs ──────────────────────────────────────────────────────
        var urls = ExtractUrls(task.Body);
        if (urls.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"🔗 LINKS ({urls.Count})",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                Margin = new Thickness(0, 12, 0, 6),
            });
            foreach (var url in urls.Take(8))
            {
                // Whole row is clickable (matches ClipNinja's link UX).
                // The "🌐 Open" button at the right gives an explicit affordance
                // for users who don't realize the row itself is clickable.
                var capturedUrl = url;
                var navigate = new Action(() =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(NormalizeUrl(capturedUrl))
                        { UseShellExecute = true });
                    }
                    catch (Exception ex) { Trace.Log("preview", $"open url failed: {ex.Message}"); }
                });

                var rowBorder = new Border
                {
                    Background = (Brush)Application.Current.Resources["PanelLightBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(8, 5, 5, 5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = $"Open {url} in your default browser",
                };
                rowBorder.MouseLeftButtonUp += (_, _) => navigate();

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = url,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SkyBrush"],
                    TextDecorations = System.Windows.TextDecorations.Underline,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(label, 0);
                rowGrid.Children.Add(label);

                var btn = new Button
                {
                    Content = "🌐 Open",
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0),
                    Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                btn.Click += (_, _) => navigate();
                Grid.SetColumn(btn, 1);
                rowGrid.Children.Add(btn);

                rowBorder.Child = rowGrid;
                stack.Children.Add(rowBorder);
            }
            if (urls.Count > 8)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"… and {urls.Count - 8} more (open the editor to see all)",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
                });
            }
        }

        // ── Attachments ──────────────────────────────────────────────
        if (task.Attachments.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"📎 ATTACHMENTS ({task.Attachments.Count})",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                Margin = new Thickness(0, 12, 0, 6),
            });
            foreach (var a in task.Attachments.Take(3))
            {
                var bmp = _persistence.LoadAttachment(a.FileName);
                if (bmp is null) continue;

                // Each attachment is a clickable card: thumbnail on the left,
                // explicit "🔍 View" button on the right. Either action opens
                // the image fullsize in the user's default image viewer.
                var capturedFileName = a.FileName;
                var openFullsize = new Action(() =>
                {
                    try
                    {
                        var path = System.IO.Path.Combine(_persistence.AttachmentsDir, capturedFileName);
                        if (System.IO.File.Exists(path))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                            { UseShellExecute = true });
                        }
                    }
                    catch (Exception ex) { Trace.Log("preview", $"open attachment failed: {ex.Message}"); }
                });

                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["PanelLightBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Click to open at full size in your default image viewer",
                };
                card.MouseLeftButtonUp += (_, _) => openFullsize();

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var img = new Image
                {
                    Source = bmp,
                    MaxHeight = 80,
                    MaxWidth = 100,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                Grid.SetColumn(img, 0);
                cardGrid.Children.Add(img);

                var dims = new TextBlock
                {
                    Text = $"\n{a.Width}×{a.Height}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(dims, 1);
                cardGrid.Children.Add(dims);

                var viewBtn = new Button
                {
                    Content = "🔍 View",
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                viewBtn.Click += (_, _) => openFullsize();
                Grid.SetColumn(viewBtn, 2);
                cardGrid.Children.Add(viewBtn);

                card.Child = cardGrid;
                stack.Children.Add(card);
            }
            if (task.Attachments.Count > 3)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"… and {task.Attachments.Count - 3} more (open the editor to see all)",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
                });
            }
        }

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["BorderBrush"],
            Margin = new Thickness(0, 10, 0, 6),
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"📅 Created: {task.CreatedAt:MMM d, h:mm tt}",
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
        });
        // Status milestones from the activity log: when work started
        // (first transition INTO InProgress) and when it was finished.
        // Same footer style as Created — the trio reads as a compact
        // lifecycle: created → started → done.
        var startedEntry = task.StateHistory.FirstOrDefault(e => e.To == "InProgress");
        if (startedEntry is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"▶  Started: {startedEntry.At:MMM d, h:mm tt}",
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
            });
        }
        if (task.State == TaskState.Done && task.CompletedAt is { } completedAt)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"✅ Done:    {completedAt:MMM d, h:mm tt}",
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
            });
        }

        outer.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        };
        return outer;
    }

    private static Brush ChipBrushForDue(string state)
    {
        return state switch
        {
            "Overdue" => (Brush)Application.Current.Resources["OverdueBrush"],
            "Today" => (Brush)Application.Current.Resources["TodayBrush"],
            "Soon" => (Brush)Application.Current.Resources["SoonBrush"],
            _ => (Brush)Application.Current.Resources["SubTextBrush"],
        };
    }

    private static Border MakeChip(string text, Brush color)
    {
        return new Border
        {
            Background = (Brush)Application.Current.Resources["PanelLightBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 0),
            Child = new TextBlock { Text = text, Foreground = color, FontSize = 11 },
        };
    }

    /// <summary>
    /// Build the bucket chip — visually similar to other chips but clickable
    /// (the cursor changes to Hand, hover lightens the background) and
    /// dispatches a ContextMenu of all buckets on click. Picking a bucket
    /// fires the _onBucketChangeRequested callback to mutate the task.
    /// </summary>
    private Border MakeBucketChip(TaskItem task, string bucketName,
        System.Collections.Generic.IReadOnlyList<Models.Bucket> buckets)
    {
        var chip = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelLightBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Click to move this task to a different bucket",
        };
        var label = new TextBlock
        {
            Text = "📂 " + bucketName,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            FontSize = 11,
            FontWeight = FontWeights.Bold,
        };
        chip.Child = label;

        // Build the menu lazily on click so the bucket list is always current.
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (_onBucketChangeRequested is null) return;
            var menu = new ContextMenu
            {
                PlacementTarget = chip,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            foreach (var b in buckets)
            {
                var capturedId = b.Id;
                var item = new MenuItem
                {
                    Header = b.Id == task.BucketId ? $"✓ {b.Name}" : b.Name,
                    IsEnabled = b.Id != task.BucketId,
                };
                item.Click += (_, _) =>
                {
                    _onBucketChangeRequested?.Invoke(task, capturedId);
                    Hide();  // dismiss popup; row will refresh in the host
                };
                menu.Items.Add(item);
            }
            // Keep the parent popup alive while the menu is open. The
            // menu's visual tree lives outside the popup window, so the
            // cursor moving into it would otherwise trigger MouseLeave →
            // hide → popup vanishes before the user can click an item.
            menu.Opened += (_, _) =>
            {
                _menuOpen = true;
                _hideTimer?.Stop();  // cancel any pending hide
            };
            menu.Closed += (_, _) =>
            {
                _menuOpen = false;
                // Re-evaluate: if the cursor is no longer over the popup
                // (e.g. user clicked elsewhere to dismiss the menu), kick
                // the normal hide flow. If they clicked an item, Hide()
                // was already called from the item handler.
                if (!_cursorInPopup) RequestHide();
            };
            menu.IsOpen = true;
        };

        // Subtle hover effect — gets a slightly darker fill on hover.
        chip.MouseEnter += (_, _) =>
        {
            chip.Background = (Brush)Application.Current.Resources["AccentBrush"];
            label.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x1A, 0x14));
        };
        chip.MouseLeave += (_, _) =>
        {
            chip.Background = (Brush)Application.Current.Resources["PanelLightBrush"];
            label.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
        };

        return chip;
    }

    public void Close()
    {
        _showTimer?.Stop();
        _hideTimer?.Stop();
        _window?.Close();
        _window = null;
    }
}
