using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskNinja.Models;
using TaskNinja.Services;

namespace TaskNinja.Views;

/// <summary>
/// Modal dialog for editing all properties of a TaskItem — title, body,
/// due date, person, recurrence, attachments. Opened from the task row
/// context menu's "Edit task…" entry, or from the quick-add bar's "more
/// details" toggle.
///
/// The body editor supports paste-image (Ctrl+V with an image on the
/// clipboard saves a PNG to attachments\ and adds an entry to the task's
/// Attachments list).
/// </summary>
public class TaskDetailEditor
{
    public static bool Show(Window owner, TaskItem task,
        IEnumerable<string> peopleAutocomplete,
        PersistenceService persistence,
        IEnumerable<Models.Bucket>? buckets = null)
    {
        var dialog = new Window
        {
            Title = "Edit task",
            Width = 500,
            Height = 680,   // roomier default; outer ScrollViewer covers overflow
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.CanResize,
            // ToolWindow gives a compact titlebar with only a close
            // button — no minimize, no maximize. Prevents the "frozen
            // app" bug where minimizing the editor sends it to nowhere
            // (ShowInTaskbar=false leaves no taskbar entry to restore
            // from), leaving the main app blocked by an invisible
            // modal. Close is still available.
            WindowStyle = WindowStyle.ToolWindow,
            Background = (Brush)Application.Current.Resources["BgBrush"],
            MinWidth = 400,
            MinHeight = 400,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0: Title
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1: Bucket
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2: State picker
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 3: Due + Person
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 4: Recurrence + Start
        // 5: Body — Auto, NOT Star. It used to be the only Star row, which
        // meant every Auto row above it (activity log, checklist,
        // attachments) took its space first and the notes box got the
        // remainder — which for a task with a few activity entries and
        // attachments was ZERO. The box vanished entirely and the only
        // recourse was manually dragging the window bigger. Now the form
        // sizes naturally and the whole dialog scrolls (see the
        // ScrollViewer below); the notes box holds its MinHeight.
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 5: Body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 6: Subtasks (NEW v1.0.22)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 7: Attachments
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 8: Buttons

        // Title
        AddLabel(root, 0, "Title", 0);
        var titleBox = new TextBox
        {
            Text = task.Title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBox, 0);
        root.Children.Add(titleBox);

        // ── Bucket picker ─────────────────────────────────────────────
        // Row 1: Combo to change the task's bucket. Falls back to a
        // read-only label if buckets weren't passed in (e.g. an older
        // caller of TaskDetailEditor.Show that doesn't supply them yet).
        var bucketRow = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        bucketRow.Children.Add(SmallLabel("Bucket"));
        ComboBox? bucketCombo = null;
        if (buckets is not null)
        {
            bucketCombo = new ComboBox
            {
                Style = (Style)Application.Current.Resources["DarkComboBoxStyle"],
            };
            foreach (var b in buckets)
            {
                var item = new ComboBoxItem { Content = b.Name, Tag = b.Id };
                bucketCombo.Items.Add(item);
                if (b.Id == task.BucketId) bucketCombo.SelectedItem = item;
            }
            if (bucketCombo.SelectedItem is null && bucketCombo.Items.Count > 0)
                bucketCombo.SelectedIndex = 0;
            bucketRow.Children.Add(bucketCombo);
        }
        else
        {
            // Read-only display if no bucket list was provided.
            bucketRow.Children.Add(new TextBlock
            {
                Text = task.BucketId,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            });
        }
        Grid.SetRow(bucketRow, 1);
        root.Children.Add(bucketRow);

        // ── State picker ──────────────────────────────────────────────
        // Row 2: 3-button row to set state directly. Same control as the
        // hover preview uses, for muscle-memory consistency. We use a
        // mutable holder for the picker so we can rebuild it after each
        // click (the active-button highlight needs to refresh).
        var stateRow = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        stateRow.Children.Add(SmallLabel("Status"));
        var statePickerHost = new ContentControl();
        void RefreshStatePicker()
        {
            statePickerHost.Content = TaskStatePicker.Build(task, target =>
            {
                task.State = target;
                RefreshStatePicker();
            });
        }
        RefreshStatePicker();
        stateRow.Children.Add(statePickerHost);

        // Important flag — sits with Status because that's what it is: a
        // second axis of "how does this task rank".
        var importantCheck = new CheckBox
        {
            Content = "❗ Important",
            IsChecked = task.IsImportant,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Tints the row, shows ❗, and floats the task to the top (except in Manual sort)",
        };
        stateRow.Children.Add(importantCheck);

        // Tags — comma-separated. These merge with any inline #hashtags
        // typed into the title, so both styles work and neither is a
        // second source of truth (TaskItem.AllTags does the union).
        stateRow.Children.Add(new TextBlock
        {
            Text = "Tags (comma-separated — #hashtags in the title count too)",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            Margin = new Thickness(0, 10, 0, 3),
        });
        var tagsBox = new TextBox
        {
            Text = string.Join(", ", task.Tags),
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            ToolTip = "e.g. audit, safety, vendor — click a tag chip on any row to filter by it",
        };
        stateRow.Children.Add(tagsBox);

        // ── Activity log ──────────────────────────────────────────────
        // Compact expander showing the task's state-change history.
        // Collapsed by default — most users don't need to see it. Click
        // to expand and view "Jun 24, 3:15 PM — Open → In progress" rows.
        // Powers itself off the StateHistory list on the TaskItem, which
        // is populated automatically every time State changes anywhere
        // in the app. Tasks created before this feature shipped have an
        // empty history — that's fine, we just show "No activity yet".
        if (task.StateHistory.Count > 0 || task.CreatedAt != default)
        {
            // Header: include Created in the entry count so the number
            // reads naturally — a brand-new task with 0 state changes
            // shows "Activity (1 entry)" (the Created entry), not
            // "Activity (0 changes)" which was misleading + ugly.
            var totalEntries = 1 + task.StateHistory.Count;  // +1 for Created
            var historyExpander = new Expander
            {
                Header = $"▸ Activity ({totalEntries} entr{(totalEntries == 1 ? "y" : "ies")})",
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                // Auto-expand when there's real history beyond the
                // implicit "Created" entry. A brand-new task has just
                // Created — no point wasting screen space to show it.
                // A task that's moved through states (or completed
                // with a comment) is where the log becomes useful,
                // and expanding by default surfaces that value without
                // requiring a click users may not know is there.
                IsExpanded = task.StateHistory.Count > 0,
            };
            var historyList = new StackPanel { Margin = new Thickness(8, 4, 0, 0) };

            // Created entry — always show this as the first activity
            // since it pre-dates the explicit StateHistory feature.
            historyList.Children.Add(MakeHistoryRow(
                task.CreatedAt,
                "Created",
                (Brush)Application.Current.Resources["SubTextBrush"]));

            // Chronological state changes. For "→ Done" entries, also
            // look up the matching CompletionRecord (closest-in-time
            // entry with At within ~5 minutes of the state change) and
            // append the comment + completed-by directly underneath.
            // This puts completion context right next to the event.
            foreach (var entry in task.StateHistory)
            {
                var arrow = $"{StateGlyph(entry.From)} {entry.From} → {StateGlyph(entry.To)} {entry.To}";
                historyList.Children.Add(MakeHistoryRow(entry.At, arrow,
                    (Brush)Application.Current.Resources["TextBrush"]));

                if (entry.To == "Done")
                {
                    var matching = FindMatchingCompletion(task.Completions, entry.At);
                    if (matching is not null && (!string.IsNullOrWhiteSpace(matching.Comment)
                        || !string.IsNullOrWhiteSpace(matching.CompletedBy)))
                    {
                        historyList.Children.Add(MakeCompletionDetailRow(matching));
                    }
                }
            }
            historyExpander.Content = historyList;
            stateRow.Children.Add(historyExpander);
        }

        Grid.SetRow(stateRow, 2);
        root.Children.Add(stateRow);

        // Row: Due date + Person side by side
        var metaRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dueCol = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        dueCol.Children.Add(SmallLabel("Due date"));
        // DatePicker themed via DarkDatePickerStyle — the calendar popup
        // now uses our dark palette (Calendar / CalendarDayButton /
        // CalendarButton default styles handle the popup contents).
        // The text portion accepts free-form input too, so users can
        // type "next friday" or "2026-07-04" if they prefer the keyboard.
        var duePicker = new DatePicker
        {
            SelectedDate = task.DueDate,
            Style = (Style)Application.Current.Resources["DarkDatePickerStyle"],
            ToolTip = "Pick a date from the calendar, or type a date. Leave blank for none.",
        };
        dueCol.Children.Add(duePicker);
        Grid.SetColumn(dueCol, 0);
        metaRow.Children.Add(dueCol);

        var personCol = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        personCol.Children.Add(SmallLabel("Responsible person"));
        var personBox = new ComboBox
        {
            IsEditable = true,
            Style = (Style)Application.Current.Resources["DarkComboBoxStyle"],
        };
        foreach (var p in peopleAutocomplete) personBox.Items.Add(p);
        personBox.Text = task.ResponsiblePerson;
        // The DarkComboBoxStyle template puts the editable TextBox over
        // column 0 and the arrow ToggleButton over column 1. In practice,
        // users click the textbox area expecting a dropdown — but only the
        // arrow column toggles it via the template. Wire a focus/click
        // hook so clicking anywhere on the combo opens the dropdown when
        // there's something to show. Doesn't interfere with typing —
        // once open, the user can type to filter or click an item.
        WireEditableComboOpenOnFocus(personBox);
        personCol.Children.Add(personBox);
        Grid.SetColumn(personCol, 1);
        metaRow.Children.Add(personCol);
        Grid.SetRow(metaRow, 3);
        root.Children.Add(metaRow);

        // Row: Recurrence + Start date
        var row2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var recurCol = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        recurCol.Children.Add(SmallLabel("Recurrence — every N units"));

        // Two-control row: a small integer text box for the interval ("every 1",
        // "every 6", etc.) next to the pattern dropdown ("Weekly", "Monthly").
        // Combined they read as: "every 6 Weeks" or "every 2 Months".
        // The interval input is disabled when pattern is None — no point typing
        // a number for "no recurrence".
        var recurRow = new Grid();
        recurRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        recurRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var intervalBox = new TextBox
        {
            Text = task.RecurrenceInterval.ToString(),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "How many of the unit between repeats. e.g. 6 + Weekly = every 6 weeks.",
            IsEnabled = task.Recurrence != RecurrencePattern.None,
        };
        Grid.SetColumn(intervalBox, 0);
        recurRow.Children.Add(intervalBox);

        var recurCombo = new ComboBox
        {
            Style = (Style)Application.Current.Resources["DarkComboBoxStyle"],
        };
        foreach (var p in Enum.GetNames(typeof(RecurrencePattern))) recurCombo.Items.Add(p);
        recurCombo.SelectedItem = task.Recurrence.ToString();
        recurCombo.SelectionChanged += (_, _) =>
        {
            // Toggle the interval input's enabled state. Selecting "None"
            // disables it (since the number is meaningless without a
            // unit); any real pattern re-enables it. Also resets to 1 if
            // turning recurrence on from off — better default than the
            // stale value left over from before.
            if (recurCombo.SelectedItem is string s && Enum.TryParse<RecurrencePattern>(s, out var pat))
            {
                var wasNone = !intervalBox.IsEnabled;
                intervalBox.IsEnabled = pat != RecurrencePattern.None;
                if (wasNone && pat != RecurrencePattern.None
                    && (!int.TryParse(intervalBox.Text, out var n) || n < 1))
                {
                    intervalBox.Text = "1";
                }
            }
        };
        Grid.SetColumn(recurCombo, 1);
        recurRow.Children.Add(recurCombo);

        recurCol.Children.Add(recurRow);
        Grid.SetColumn(recurCol, 0);
        row2.Children.Add(recurCol);

        var startCol = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        startCol.Children.Add(SmallLabel("Don't show until (optional)"));
        var startPicker = new DatePicker
        {
            SelectedDate = task.StartDate,
            Style = (Style)Application.Current.Resources["DarkDatePickerStyle"],
            ToolTip = "Hide this task until this date. Leave blank to show always.",
        };
        startCol.Children.Add(startPicker);
        Grid.SetColumn(startCol, 1);
        row2.Children.Add(startCol);
        Grid.SetRow(row2, 4);
        root.Children.Add(row2);

        // Body editor
        // Body area. Hosted in a Grid (not a StackPanel) so the TextBox
        // can stretch to fill the remaining vertical space allocated to
        // Row 5 (a Star-sized row in the parent Grid). A StackPanel would
        // collapse the TextBox to its MinHeight, defeating the * sizing
        // and preventing the scrollbar from kicking in when content
        // overflows.
        var bodyCol = new Grid();
        bodyCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // label
        bodyCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // textbox
        var bodyLabel = SmallLabel("Notes / body — URLs auto-link in preview. Paste images with Ctrl+V.");
        Grid.SetRow(bodyLabel, 0);
        bodyCol.Children.Add(bodyLabel);
        var bodyBox = new TextBox
        {
            Text = task.Body,
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Guaranteed floor: the notes box is always at least this
            // tall and scrolls internally past it. Combined with the
            // outer ScrollViewer, nothing in this dialog can ever be
            // compressed to invisibility.
            MinHeight = 130,
            MaxHeight = 320,
        };
        Grid.SetRow(bodyBox, 1);

        // List of attachments — modifiable working copy
        var workingAttachments = new List<BodyAttachment>(task.Attachments);

        // Attachments panel — DECLARED EARLY so the bodyBox paste-handler
        // lambda below can capture it. The lambda calls RefreshAttachmentList
        // which references attachPanel; without this hoist, C# definite-assignment
        // analysis fails (CS0165) because the closure captures the local by ref
        // before the variable's declaration point is reached.
        var attachPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(attachPanel, 7);
        root.Children.Add(attachPanel);

        // The refresh function is also declared before the paste-handler.
        // Local functions can be forward-referenced WITHIN the same scope
        // but the variables they close over must be assigned first.
        void RefreshAttachmentList()
        {
            attachPanel.Children.Clear();
            if (workingAttachments.Count == 0) return;
            var hdr = new TextBlock
            {
                Text = $"📎 Attachments ({workingAttachments.Count})",
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
                Margin = new Thickness(0, 0, 0, 4),
            };
            attachPanel.Children.Add(hdr);
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var a in workingAttachments.ToList())
            {
                var bmp = persistence.LoadAttachment(a.FileName);
                if (bmp is null) continue;
                var thumb = new Image
                {
                    Source = bmp,
                    Height = 60,
                    Width = 60,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(0, 0, 4, 4),
                };
                // Shared removal path — used by the ✕ badge and right-click.
                void RemoveThisAttachment()
                {
                    if (MessageBox.Show("Remove this attachment?", "Remove attachment",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    workingAttachments.Remove(a);
                    persistence.DeleteAttachment(a.FileName);
                    RefreshAttachmentList();
                }

                var border = new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    Child = thumb,
                };
                border.MouseRightButtonDown += (_, _) => RemoveThisAttachment();

                // ✕ badge, top-right of the thumbnail. Removal was
                // right-click-only, i.e. invisible — a destructive action
                // nobody could find. The Grid stacks the badge over the
                // thumbnail without disturbing the WrapPanel layout.
                var cell = new Grid { Margin = new Thickness(0, 0, 6, 6) };
                cell.Children.Add(border);
                var delBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xC0, 0x39, 0x2B)),
                    CornerRadius = new CornerRadius(9),
                    Width = 18,
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -6, -6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Remove this attachment",
                    Child = new TextBlock
                    {
                        Text = "✕",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                delBadge.MouseLeftButtonUp += (_, me) =>
                {
                    me.Handled = true;   // don't also open the image
                    RemoveThisAttachment();
                };
                cell.Children.Add(delBadge);
                // Left-click opens the attachment full-size in the default
                // image viewer — same behavior the preview popup has had.
                // Previously the editor thumbnails were view-only (right-
                // click-to-remove only), which was a gap: the one place
                // you're focused ON the task couldn't open its files.
                var capturedFileName = a.FileName;
                border.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        var path = System.IO.Path.Combine(persistence.AttachmentsDir, capturedFileName);
                        if (System.IO.File.Exists(path))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                            { UseShellExecute = true });
                        }
                    }
                    catch { /* best-effort — viewer missing or file locked */ }
                };
                border.Cursor = System.Windows.Input.Cursors.Hand;
                border.ToolTip = "Click to open • ✕ or right-click to remove";
                wrap.Children.Add(cell);   // cell = thumbnail + ✕ badge
            }
            attachPanel.Children.Add(wrap);
        }

        // Handle Ctrl+V paste in body — capture image clipboard payload
        bodyBox.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        var bmp = Clipboard.GetImage();
                        if (bmp is not null)
                        {
                            var fileName = persistence.SaveAttachment(bmp);
                            workingAttachments.Add(new BodyAttachment
                            {
                                FileName = fileName,
                                Width = bmp.PixelWidth,
                                Height = bmp.PixelHeight,
                            });
                            // Suppress the default paste to avoid pasting raw bytes into body
                            e.Handled = true;
                            // Refresh the attachments panel after this returns
                            // (handled via Dispatcher below).
                            bodyBox.Dispatcher.BeginInvoke(new Action(() => RefreshAttachmentList()));
                        }
                    }
                }
                catch (Exception ex) { Trace.Log("editor", $"paste image failed: {ex.Message}"); }
            }
        };

        bodyCol.Children.Add(bodyBox);
        Grid.SetRow(bodyCol, 5);
        root.Children.Add(bodyCol);

        // ── Subtasks / checklist ────────────────────────────────────
        // Compact section with: header line, list of existing subtasks
        // (each row = checkbox + text + delete-x), and a "+ add subtask"
        // input. Designed for the "Do this thing for 5 projects" use
        // case where a single parent task has a handful of related
        // checkboxes. NOT meant for deeply nested or scheduled work —
        // those should stay top-level tasks.
        var subtasksWorking = task.Subtasks.ToList();  // working copy until Save
        var subtasksPanel = BuildSubtasksSection(subtasksWorking);
        Grid.SetRow(subtasksPanel, 6);
        root.Children.Add(subtasksPanel);

        // Initial population of attachments
        RefreshAttachmentList();

        // Buttons
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var okBtn = new Button
        {
            Content = "Save",
            Padding = new Thickness(20, 4, 20, 4),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = false,
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 4, 12, 4),
            IsCancel = true,
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        bool saved = false;
        okBtn.Click += (_, _) =>
        {
            task.Title = titleBox.Text;
            task.Body = bodyBox.Text;
            // DatePickers expose SelectedDate; null = no date.
            task.DueDate = duePicker.SelectedDate;
            task.StartDate = startPicker.SelectedDate;
            task.ResponsiblePerson = personBox.Text ?? "";
            if (Enum.TryParse<RecurrencePattern>(recurCombo.SelectedItem as string, out var pat))
                task.Recurrence = pat;
            // Parse the "every N" interval. Anything non-numeric or < 1
            // falls back to 1 — better than rejecting the save.
            if (int.TryParse(intervalBox.Text?.Trim(), out var ival) && ival >= 1 && ival <= 999)
                task.RecurrenceInterval = ival;
            else
                task.RecurrenceInterval = 1;
            // Persist the bucket selection. If the user changed the bucket
            // in the combo, this moves the task to the new bucket.
            if (bucketCombo?.SelectedItem is ComboBoxItem bi && bi.Tag is string newBucketId)
            {
                task.BucketId = newBucketId;
            }
            task.Attachments = workingAttachments;
            task.Subtasks = subtasksWorking;
            task.IsImportant = importantCheck.IsChecked == true;
            // Split on commas; the Tags setter lowercases, strips any
            // leading #, dedupes, and caps at 8.
            task.Tags = (tagsBox.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
            saved = true;
            dialog.DialogResult = true;
        };
        cancelBtn.Click += (_, _) => dialog.DialogResult = false;
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);

        // Footer row: lifecycle summary on the left, Save/Cancel on the
        // right. The summary is the task's status milestones at a
        // glance — created / started (first move to In progress) /
        // completed — without expanding the Activity log. Only shows
        // milestones that happened; a fresh task just shows Created.
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lifecycleParts = new List<string> { $"Created {task.CreatedAt:MMM d}" };
        var startedAt = task.StateHistory.FirstOrDefault(e => e.To == "InProgress");
        if (startedAt is not null) lifecycleParts.Add($"Started {startedAt.At:MMM d}");
        if (task.State == TaskState.Done && task.CompletedAt is { } doneAt)
            lifecycleParts.Add($"Done {doneAt:MMM d}");
        var lifecycleBlock = new TextBlock
        {
            Text = string.Join("  •  ", lifecycleParts),
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)Application.Current.Resources["DimTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 12, 8, 0),
            ToolTip = "Full history in the Activity section above",
        };
        Grid.SetColumn(lifecycleBlock, 0);
        footer.Children.Add(lifecycleBlock);
        Grid.SetColumn(btnRow, 1);
        footer.Children.Add(btnRow);

        // Footer (lifecycle + Save/Cancel) is PINNED outside the scroll
        // region — Save must always be reachable without scrolling to
        // the bottom of a long task.
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroller = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 4, 0),   // room for the scrollbar
        };
        Grid.SetRow(scroller, 0);
        outer.Children.Add(scroller);

        Grid.SetRow(footer, 1);
        footer.Margin = new Thickness(12, 4, 12, 12);   // matches root grid margin
        outer.Children.Add(footer);

        dialog.Content = outer;
        dialog.Loaded += (_, _) => titleBox.Focus();
        dialog.ShowDialog();
        return saved;
    }

    private static void AddLabel(Grid g, int row, string text, int col = 0)
    {
        var lbl = SmallLabel(text);
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, col);
        g.Children.Add(lbl);
    }

    private static TextBlock SmallLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            Margin = new Thickness(0, 0, 0, 3),
        };
    }

    /// <summary>One row in the activity-log expander: timestamp on the
    /// left, description on the right. Used for both the "Created"
    /// pseudo-entry and the real state transitions.</summary>
    private static FrameworkElement MakeHistoryRow(DateTime at, string description, Brush textColor)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Format: "Jun 24, 3:15 PM" — concise, with year omitted unless
        // the change happened in a different year (most don't, so the
        // tighter format wins). If you ever need the full timestamp,
        // hover for a tooltip with the precise datetime.
        var fmt = at.Year == DateTime.Now.Year ? "MMM d, h:mm tt" : "MMM d yyyy, h:mm tt";
        var timeBlock = new TextBlock
        {
            Text = at.ToString(fmt),
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = at.ToString("F"),
        };
        Grid.SetColumn(timeBlock, 0);
        row.Children.Add(timeBlock);

        var descBlock = new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = textColor,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(descBlock, 1);
        row.Children.Add(descBlock);

        return row;
    }

    /// <summary>Returns the visual glyph for a state name. Used in the
    /// activity log so each entry has a quick visual cue alongside the
    /// text. Mirrors TaskItem.StateGlyph but works off the string name
    /// (which is what StateChangeEntry stores).</summary>
    private static string StateGlyph(string stateName) => stateName switch
    {
        "Open" => "○",
        "InProgress" => "◐",
        "Done" => "●",
        _ => "?",
    };

    /// <summary>Find the CompletionRecord most likely tied to a given
    /// state-change-to-Done entry. The state-change and completion
    /// record are written at nearly the same moment (within a few ms),
    /// but for safety we accept the closest record within a 5-minute
    /// window. Returns null if no record is plausibly the match —
    /// which is fine, the activity log just shows the state change
    /// without extra detail in that case.</summary>
    private static Models.CompletionRecord? FindMatchingCompletion(
        List<Models.CompletionRecord> completions, DateTime stateChangeAt)
    {
        if (completions.Count == 0) return null;
        var tolerance = TimeSpan.FromMinutes(5);
        Models.CompletionRecord? best = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var c in completions)
        {
            var delta = (c.At - stateChangeAt).Duration();
            if (delta < bestDelta && delta < tolerance)
            {
                best = c;
                bestDelta = delta;
            }
        }
        return best;
    }

    /// <summary>One indented row under a "→ Done" entry showing the
    /// optional completion comment and completed-by name. Skipped
    /// entirely (caller-side) if neither field is populated.</summary>
    private static FrameworkElement MakeCompletionDetailRow(Models.CompletionRecord rec)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(140, 0, 0, 6),  // align under description column
            Orientation = Orientation.Vertical,
        };
        if (!string.IsNullOrWhiteSpace(rec.CompletedBy))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"by {rec.CompletedBy}",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            });
        }
        if (!string.IsNullOrWhiteSpace(rec.Comment))
        {
            stack.Children.Add(new TextBlock
            {
                Text = "  " + rec.Comment,
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360,
            });
        }
        return stack;
    }

    /// <summary>Wire an editable ComboBox so clicking anywhere on it opens
    /// the dropdown (not just the arrow). The default template path
    /// requires clicking specifically on the arrow column, which is too
    /// narrow and easy to miss. We hook PreviewMouseLeftButtonDown on the
    /// combo to open the dropdown ourselves before the textbox swallows
    /// the click. Typing still works normally once open — the user can
    /// either click an item or just keep typing to filter.</summary>
    private static void WireEditableComboOpenOnFocus(ComboBox combo)
    {
        // Use Preview-down so we run before the textbox-focus handler
        // marks the event handled. Only open if there's something to
        // show (don't dangle an empty popup in the user's face).
        combo.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (combo.Items.Count == 0) return;
            if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true;
        };
        // Also open when the textbox gains keyboard focus via Tab, so
        // power users can keyboard-navigate without needing the mouse.
        combo.GotKeyboardFocus += (s, e) =>
        {
            if (combo.Items.Count == 0) return;
            if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true;
        };
    }

    /// <summary>Build the editable subtasks/checklist section. The
    /// caller passes the live working list; this method appends rows
    /// to that list as the user adds items, removes them on delete,
    /// and mutates the IsDone flag on checkbox toggle. The caller's
    /// list is the source of truth — no internal copy.</summary>
    private static FrameworkElement BuildSubtasksSection(List<Models.Subtask> working)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        // Header with running count, e.g. "Checklist (2 / 5)"
        var header = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            Margin = new Thickness(0, 0, 0, 4),
        };
        stack.Children.Add(header);

        // List host — one row per subtask. Rebuilt on every change for
        // simplicity. The list is short enough (typically <10 items)
        // that re-rendering on each tick is fine.
        var listHost = new StackPanel();
        stack.Children.Add(listHost);

        // Add-row input + button
        var addRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var addBox = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Type a checklist item, then press Enter or click +",
        };
        // Placeholder hint via a watermark would be cleaner but WPF
        // doesn't have built-in placeholder. ToolTip serves as docs.
        var addBtn = new Button
        {
            Content = "+",
            Padding = new Thickness(10, 3, 10, 3),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"],
            ToolTip = "Add this subtask",
        };
        Grid.SetColumn(addBox, 0);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(addBox);
        addRow.Children.Add(addBtn);
        stack.Children.Add(addRow);

        // Re-render the list + header. Pulled out so all the mutation
        // handlers can call it. WPF doesn't auto-rebind a plain
        // StackPanel from a List<T>, so this manual rebuild is the
        // simplest way to stay in sync.
        void Refresh()
        {
            listHost.Children.Clear();
            var doneCount = working.Count(s => s.IsDone);
            header.Text = working.Count == 0
                ? "Checklist (none — add subtasks for tracking small steps)"
                : $"Checklist ({doneCount} / {working.Count})";

            for (int i = 0; i < working.Count; i++)
            {
                var idx = i;  // capture for closures
                var sub = working[idx];

                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var check = new CheckBox
                {
                    IsChecked = sub.IsDone,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    Foreground = (Brush)Application.Current.Resources["TextBrush"],
                };
                check.Checked += (_, _) =>
                {
                    sub.IsDone = true;
                    sub.CompletedAt = DateTime.Now;
                    Refresh();
                };
                check.Unchecked += (_, _) =>
                {
                    sub.IsDone = false;
                    sub.CompletedAt = null;
                    Refresh();
                };
                Grid.SetColumn(check, 0);
                row.Children.Add(check);

                // Title is an editable TextBox so the user can correct
                // typos without re-entering. Strikethrough when done.
                var titleBox = new TextBox
                {
                    Text = sub.Title,
                    FontSize = 12,
                    Padding = new Thickness(4, 2, 4, 2),
                    Background = (Brush)Application.Current.Resources["PanelBrush"],
                    Foreground = sub.IsDone
                        ? (Brush)Application.Current.Resources["SubTextBrush"]
                        : (Brush)Application.Current.Resources["TextBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    TextDecorations = sub.IsDone ? TextDecorations.Strikethrough : null,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                titleBox.LostFocus += (_, _) => sub.Title = titleBox.Text;
                Grid.SetColumn(titleBox, 1);
                row.Children.Add(titleBox);

                var delBtn = new Button
                {
                    Content = "×",
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 6, 0),
                    Margin = new Thickness(4, 0, 0, 0),
                    Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
                    ToolTip = "Remove this subtask",
                };
                delBtn.Click += (_, _) =>
                {
                    working.RemoveAt(idx);
                    Refresh();
                };
                Grid.SetColumn(delBtn, 2);
                row.Children.Add(delBtn);

                listHost.Children.Add(row);
            }
        }

        void CommitAddBoxContent()
        {
            var text = (addBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;
            working.Add(new Models.Subtask { Title = text });
            addBox.Text = "";
            Refresh();
            addBox.Focus();
        }

        addBtn.Click += (_, _) => CommitAddBoxContent();
        addBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                CommitAddBoxContent();
                e.Handled = true;
            }
        };

        Refresh();
        return stack;
    }
}
