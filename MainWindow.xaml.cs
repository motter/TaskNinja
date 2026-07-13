using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskNinja.Models;
using TaskNinja.Services;
using TaskNinja.ViewModels;
using TaskNinja.Views;

namespace TaskNinja;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private HotkeyService? _hotkeys;
    private PreviewPopup? _previewPopup;
    private NotificationService? _notifications;

    // Tray icon (uses WinForms NotifyIcon via reflection — see TrayIconWrapper).
    private TrayIconWrapper? _tray;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // Set the window icon as early as possible — BEFORE the window is
        // shown for the first time. Setting it later (in OnWindow_Loaded)
        // can cause Windows to register the window with the .exe's
        // embedded ApplicationIcon first, then "see" the icon swap when
        // OnWindow_Loaded runs and treat it as a new app — contributing
        // to the two-taskbar-icons grouping bug. Setting it here means
        // the window is born with its final icon.
        TrySetWindowIcon();
    }

    private void OnWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Version label in title bar — pulled from App's single source of truth
        VersionRun.Text = $" v{App.DisplayVersion}";

        // Populate bucket dropdown
        RefreshBucketCombo();
        RefreshSortButton();

        // Hotkey: Ctrl+Shift+T to show/hide
        _hotkeys = new HotkeyService(this);
        _hotkeys.Register(HotkeyService.CtrlShift, Key.T, ToggleVisibility);

        // Preview popup
        _previewPopup = new PreviewPopup(
            owner: this,
            persistence: _vm.Persistence,
            onStateChangeRequested: (task, newState) =>
            {
                // Set the state directly (not via CycleState — the picker
                // tells us which state to land on). Then refresh visuals.
                if (task.State != newState)
                {
                    task.State = newState;
                    // Marking Done triggers the completion popup
                    // (comment + by + for recurring tasks, next-due).
                    // For non-recurring tasks the popup still shows
                    // so the user can capture a note + who completed.
                    if (newState == TaskState.Done)
                        PromptAndRecordCompletion(task);
                    _vm.RebuildVisible();
                    _vm.OnTaskMutated();
                }
            },
            // Bucket support — the popup shows a clickable bucket chip
            // and opens a menu of all buckets when clicked. getBuckets
            // is a delegate (not a snapshot list) so newly-added buckets
            // appear without restarting the app.
            getBuckets: () => _vm.Buckets,
            onBucketChangeRequested: (task, bucketId) =>
            {
                _vm.MoveToBucket(task, bucketId);
            });

        // Tray icon
        try
        {
            _tray = new TrayIconWrapper(
                tooltip: "TaskNinja — click to show",
                onLeftClick: () => Dispatcher.BeginInvoke(new Action(ShowWindowFromTray)),
                onShow: () => Dispatcher.BeginInvoke(new Action(ShowWindowFromTray)),
                onExit: () =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        OnAppExiting();
                        Application.Current.Shutdown();
                    }));
                },
                onShowDigest: () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    // User-initiated show — don't update LastDigestShown (so the
                    // automatic daily pop still happens at the configured time).
                    ShowDigestPopup(updateLastShownOnDismiss: false);
                })),
                onShowSettings: () => Dispatcher.BeginInvoke(new Action(OpenSettingsDialog)),
                onShowWeeklyReport: () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Show the main window first so the report has a
                    // sensible parent (and the user sees the app come
                    // forward — useful confirmation that the click was
                    // received).
                    ShowWindowFromTray();
                    WeeklyReportDialog.Show(this, _vm);
                })),
                onCheckUpdates: () => Dispatcher.BeginInvoke(new Action(OnTrayCheckUpdates)));
        }
        catch (Exception ex)
        {
            Trace.Log("tray", $"tray init failed: {ex.Message}");
        }

        // Fire-and-forget background update check (respects the
        // AutoCheckForUpdates setting; repo defaults to motter/TaskNinja).
        CheckForUpdatesSilently();

        // Daily summary notifications. The service ticks every minute
        // and decides whether to pop the digest based on settings +
        // today's "already shown" stamp.
        _notifications = new NotificationService(
            settings: _vm.Settings,
            showDigest: () => ShowDigestPopup(updateLastShownOnDismiss: true),
            persistSettings: () => _vm.ScheduleSave());
        _notifications.Start();

        // Keep the tray tooltip showing the overdue count so users have
        // at-a-glance awareness from the tray. Refreshes whenever the
        // view model's OverdueCount changes (after task mutations).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.OverdueCount) ||
                e.PropertyName == nameof(_vm.OpenCount))
            {
                UpdateTrayTooltip();
            }
        };
        UpdateTrayTooltip();  // initial set

        _vm.StatusText = "Ready";
    }

    /// <summary>Open the daily digest popup, wiring up the per-task
    /// actions, snooze callback, dismiss callback, AND the new ⚙
    /// settings shortcut on the digest's footer. When
    /// <paramref name="updateLastShownOnDismiss"/> is true (automatic
    /// daily trigger), clicking "Got it for today" persists the
    /// "shown today" stamp. When false (user-initiated via tray menu),
    /// dismissing doesn't update the stamp — so the automatic pop
    /// still happens at the configured time.</summary>
    private void ShowDigestPopup(bool updateLastShownOnDismiss)
    {
        DailyDigestPopup.Show(
            owner: this,
            vm: _vm,
            onRemindLater: when => _notifications?.RemindAt(when ?? DateTime.Now.AddHours(2)),
            onDismissed: updateLastShownOnDismiss
                ? () => _notifications?.MarkShownToday()
                : null,
            onShowSettings: OpenSettingsDialog);
    }

    /// <summary>Open the settings modal. Persists settings on Save.
    /// Used from the tray menu and from the daily digest footer's
    /// ⚙ shortcut.</summary>
    private void OpenSettingsDialog()
    {
        if (SettingsDialog.Show(this, _vm.Settings))
        {
            // Persist the changes. The notification service reads from
            // the same AppSettings instance and will pick up changes on
            // its next tick automatically.
            _vm.ScheduleSave();
        }
    }

    // ── In-app updates (GitHub Releases) ─────────────────────────────

    /// <summary>Tray "Check for updates..." — full interactive flow:
    /// check, offer with release notes, download, restart. Mirrors the
    /// Settings dialog's button (which also exists so the flow is
    /// discoverable in both places).</summary>
    private async void OnTrayCheckUpdates()
    {
        try
        {
            var (update, error) = await Services.UpdateService.CheckAsync(_vm.Settings.UpdateRepo);
            if (update is null)
            {
                MessageBox.Show(
                    error ?? $"✓ You're up to date (v{Services.UpdateService.CurrentVersion.ToString(3)}).",
                    "TaskNinja updates", MessageBoxButton.OK,
                    error is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
                return;
            }
            var notes = string.IsNullOrWhiteSpace(update.Notes)
                ? ""
                : "\n\nRelease notes:\n" + (update.Notes.Length > 600 ? update.Notes[..600] + "…" : update.Notes);
            var answer = MessageBox.Show(
                $"TaskNinja {update.TagName} is available (you have v{Services.UpdateService.CurrentVersion.ToString(3)}).{notes}\n\n" +
                "Update now? The app will restart.",
                "TaskNinja update", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;
            _vm.StatusText = "⬇ Downloading update…";
            var applyError = await Services.UpdateService.DownloadAndStageAsync(update);
            if (applyError is not null)
            {
                _vm.StatusText = applyError;
                return;
            }
            // The swap script in %TEMP% is waiting for our file lock to
            // release — exit promptly. Data is already persisted via the
            // normal save-on-change flow; OnAppExiting runs via Shutdown.
            OnAppExiting();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Trace.Log("update", $"tray update flow failed: {ex}");
            _vm.StatusText = $"Update failed: {ex.Message}";
        }
    }

    /// <summary>Silent background check ~5s after startup (if enabled).
    /// A newer release = a status-bar hint only, never a dialog.
    /// Errors go to the trace log; background checks have no business
    /// popping message boxes.</summary>
    private async void CheckForUpdatesSilently()
    {
        try
        {
            if (!_vm.Settings.AutoCheckForUpdates) return;
            if (string.IsNullOrWhiteSpace(_vm.Settings.UpdateRepo)) return;
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
            var (update, error) = await Services.UpdateService.CheckAsync(_vm.Settings.UpdateRepo);
            if (update is not null)
            {
                _vm.StatusText = $"⬆ TaskNinja {update.TagName} available — tray menu → Check for updates";
                Trace.Log("update", $"startup check: {update.TagName} available");
            }
            else if (error is not null)
            {
                Trace.Log("update", $"startup check: {error}");
            }
        }
        catch (Exception ex)
        {
            Trace.Log("update", $"silent check failed: {ex.Message}");
        }
    }

    /// <summary>Refresh the tray tooltip with current task counts so the
    /// user has at-a-glance awareness from the system tray. Format:
    ///   • "TaskNinja — 3 overdue, 5 open"      (overdue tasks exist)
    ///   • "TaskNinja — 5 open"                  (no overdue, work to do)
    ///   • "TaskNinja — all clear"               (nothing open)
    /// Wrapped in try/catch because the tray might not exist (failed
    /// to init) or might have been disposed during shutdown.</summary>
    private void UpdateTrayTooltip()
    {
        try
        {
            if (_tray is null) return;
            var overdue = _vm.OverdueCount;
            var open = _vm.OpenCount;
            string tooltip;
            if (open == 0)
                tooltip = "TaskNinja — all clear";
            else if (overdue == 0)
                tooltip = $"TaskNinja — {open} open";
            else
                tooltip = $"TaskNinja — {overdue} overdue, {open} open";
            _tray.SetTooltip(tooltip);
        }
        catch (Exception ex)
        {
            Trace.Log("tray", $"tooltip update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the window icon from the embedded resource. Wrapped in
    /// try/catch because a failure here should NEVER prevent the app
    /// from launching — a missing icon is cosmetic, not fatal. The
    /// embedded resource (TaskNinja.Resources.tasknin.ico) is reliably
    /// findable in both dev and published-single-file builds because it's
    /// bundled into the assembly's manifest rather than as a pack-URI
    /// content resource.
    /// </summary>
    private void TrySetWindowIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("TaskNinja.Resources.tasknin.ico");
            if (stream is null) return;

            var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0)
            {
                // Pick the largest frame for best Alt-Tab/taskbar rendering.
                System.Windows.Media.Imaging.BitmapFrame? best = null;
                int bestArea = 0;
                foreach (var f in decoder.Frames)
                {
                    int area = f.PixelWidth * f.PixelHeight;
                    if (area > bestArea) { best = f; bestArea = area; }
                }
                if (best is not null) this.Icon = best;
            }
        }
        catch (Exception ex)
        {
            Trace.Log("icon", $"failed to set window icon: {ex.Message}");
        }
    }

    /// <summary>Called from App.xaml.cs when another instance signals
    /// "show window".</summary>
    public void ShowWindowFromTray()
    {
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Focus();

        // If there's a modal dialog open (task editor, settings, bucket
        // manager, etc.) and the user minimized the main window while
        // it was up, the dialog is technically still visible but is
        // either off-screen, behind other windows, or has gone with the
        // owner-window minimize. The modal dialog ALSO blocks any
        // interaction with the main window — clicking the tray icon
        // shows the main window but the user STILL can't do anything
        // because the dialog has UI-thread priority.
        //
        // Surface every open child window so the user can see what's
        // blocking them. The modal dialog will then be in front and
        // focusable.
        SurfaceChildWindows();
    }

    /// <summary>Bring any open child windows (task editor, settings
    /// dialog, weekly report, etc.) to the front. Called whenever
    /// the main window becomes visible via any path — tray click,
    /// Ctrl+Shift+T toggle, or restore-from-minimize via the taskbar
    /// (see OnWindow_StateChanged). This is the recovery half of
    /// the "editor minimized → app frozen" fix; the prevention half
    /// is the editor using WindowStyle=ToolWindow so there's no
    /// minimize button in the first place.</summary>
    private void SurfaceChildWindows()
    {
        try
        {
            foreach (Window w in System.Windows.Application.Current.Windows)
            {
                if (w == this) continue;
                if (!w.IsLoaded) continue;
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Show();
                w.Activate();
            }
        }
        catch (Exception ex)
        {
            Trace.Log("show", $"surfacing child windows failed: {ex.Message}");
        }
    }

    /// <summary>Fires whenever the window state changes — including
    /// when the user clicks the taskbar entry to restore from
    /// minimize. Route restores through SurfaceChildWindows so any
    /// modal dialogs come with. Without this hook, restoring from
    /// the taskbar leaves child dialogs stranded (they don't have
    /// their own taskbar entry — see ShowInTaskbar=false), which
    /// looked like an "app frozen" bug from the user's side.</summary>
    private void OnWindow_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
        {
            // Defer one dispatcher cycle so the restore completes
            // before we start rearranging child windows — otherwise
            // Activate() on a child can race with the main window
            // becoming visible.
            Dispatcher.BeginInvoke(new Action(SurfaceChildWindows),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ToggleVisibility()
    {
        if (WindowState == WindowState.Minimized || !IsVisible)
        {
            // Mirror the ClipNinja v2.4.3 fix.
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            // Surface any modal dialogs — the OnWindow_StateChanged
            // hook usually handles this, but the explicit call here
            // is belt-and-suspenders for the toggle path (which
            // sometimes goes from Hidden → Normal without hitting
            // Minimized in between).
            SurfaceChildWindows();
        }
        else
        {
            // Tidy: when hiding to tray, clear ShowInTaskbar so Windows
            // doesn't keep a ghost taskbar slot. The button reappears on
            // the next ShowWindowFromTray() / ToggleVisibility() call.
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void OnWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // X button → hide to tray instead of close
        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
    }

    public void OnAppExiting()
    {
        try { _vm.Flush(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _previewPopup?.Close(); } catch { }
    }

    // ── Window chrome ───────────────────────────────────────────────

    private void OnTitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1) DragMove();
    }

    private void OnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Settings.ShowTrayHint)
        {
            _vm.Settings.ShowTrayHint = false;
            _vm.SaveAll();
            MessageBox.Show(
                "TaskNinja stays running in your system tray (look near the clock 🕐).\n\n" +
                "Right-click the tray icon → Exit to fully quit.",
                "TaskNinja keeps running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        Hide();
    }

    // ── Top-level keyboard shortcuts ────────────────────────────────

    private void OnWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ShowSearchBar();
            e.Handled = true;
        }
        else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ShowQuickAddBar();
            e.Handled = true;
        }
        else if (e.Key == Key.R && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Ctrl+R → weekly activity report. Also reachable from the
            // tray menu's "Weekly report" item.
            WeeklyReportDialog.Show(this, _vm);
            e.Handled = true;
        }
        else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Ctrl+D → show daily summary on demand. User-initiated, so
            // doesn't bump LastDigestShown — the morning pop still happens.
            ShowDigestPopup(updateLastShownOnDismiss: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SearchBar.Visibility == Visibility.Visible) HideSearchBar();
            if (QuickAddBar.Visibility == Visibility.Visible) HideQuickAddBar();
        }
    }

    // ── Drag-and-drop create task ────────────────────────────────────
    //
    // Supports three classes of dropped payload:
    //
    //   1. File paths (DataFormats.FileDrop) — from Windows Explorer, or
    //      from Outlook when the user drags an email's attached file.
    //      Images become the task's attachment; other files get a
    //      reference in the body.
    //
    //   2. Outlook email drag — Outlook offers several clipboard formats
    //      when an email is dragged:
    //        • FileGroupDescriptorW + FileContents (the .msg blob)
    //        • UnicodeText / Text — typically "Subject\nFrom\n\nBody"
    //        • HTML
    //      Parsing .msg requires a library, so we use the UnicodeText
    //      path: first line = subject (task title), rest = body.
    //
    //   3. Plain text — title becomes the first line, body the rest.
    //
    // A semi-transparent overlay appears during drag-over to signal that
    // a drop will create a task. The overlay is hidden in DragLeave and
    // after the Drop is processed.

    private void OnWindow_DragEnter(object sender, DragEventArgs e)
    {
        if (CanAcceptDrop(e))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAcceptDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindow_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnWindow_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        try
        {
            // ── 1. FileDrop: from Explorer, or some Outlook attachment drags.
            //      Handles both regular files and image files (which get
            //      saved as attachments to a new task).
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                CreateTasksFromFiles(paths);
                e.Handled = true;
                return;
            }

            // ── 2. Bitmap: raw image bytes on the clipboard. This is what
            //      Snipping Tool, ShareX, etc. produce when you drag a
            //      capture directly without saving to disk first. We save
            //      the bitmap as an attachment on a new task with a
            //      "Screenshot" placeholder title the user can rename.
            if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                if (e.Data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bmp)
                {
                    CreateTaskFromBitmap(bmp);
                    e.Handled = true;
                    return;
                }
            }

            // ── 3. Outlook email or HTML/text — fall back to text path
            string? text = null;
            if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                text = (string)e.Data.GetData(DataFormats.UnicodeText);
            else if (e.Data.GetDataPresent(DataFormats.Text))
                text = (string)e.Data.GetData(DataFormats.Text);

            if (!string.IsNullOrWhiteSpace(text))
            {
                CreateTaskFromText(text);
                e.Handled = true;
                return;
            }

            _vm.StatusText = "Couldn't read the dropped content (unsupported format).";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Drop failed: {ex.Message}";
            Trace.Log("dnd", $"OnWindow_Drop exception: {ex}");
        }
    }

    private static bool CanAcceptDrop(DragEventArgs e)
    {
        return e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetDataPresent(DataFormats.Bitmap)
            || e.Data.GetDataPresent(DataFormats.UnicodeText)
            || e.Data.GetDataPresent(DataFormats.Text);
    }

    /// <summary>Create a task per dropped file. Image files become the
    /// task's attachment; non-image files get a path reference in the
    /// task body.</summary>
    /// <summary>Create a task from a raw bitmap dropped onto the app.
    /// Used for screenshots dragged from Snipping Tool, ShareX, etc.
    /// — anywhere the image arrives as <see cref="BitmapSource"/> on the
    /// clipboard instead of as a file on disk. We save the bitmap as an
    /// attachment and use a "Screenshot N×M" placeholder title that the
    /// user can rename via the editor.</summary>
    private void CreateTaskFromBitmap(System.Windows.Media.Imaging.BitmapSource bmp)
    {
        var title = $"Screenshot ({bmp.PixelWidth}×{bmp.PixelHeight})";
        var task = _vm.AddTask(title);
        var savedFileName = _vm.Persistence.SaveAttachment(bmp);
        var att = new Models.BodyAttachment
        {
            FileName = savedFileName,
            Width = bmp.PixelWidth,
            Height = bmp.PixelHeight,
            Caption = "Dropped screenshot",
        };
        task.Attachments = new System.Collections.Generic.List<Models.BodyAttachment>(task.Attachments) { att };
        _vm.StatusText = $"📥 Created from screenshot ({bmp.PixelWidth}×{bmp.PixelHeight})";
    }

    private void CreateTasksFromFiles(string[] paths)
    {
        int created = 0;
        var imageExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

        foreach (var path in paths)
        {
            try
            {
                var fileName = System.IO.Path.GetFileName(path);
                var ext = System.IO.Path.GetExtension(path);
                var title = System.IO.Path.GetFileNameWithoutExtension(path);

                if (imageExt.Contains(ext))
                {
                    // Load into a BitmapImage so we can both attach it AND
                    // know the pixel dimensions for the BodyAttachment record.
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(path);
                    bi.EndInit();
                    bi.Freeze();

                    var task = _vm.AddTask(title);
                    var savedFileName = _vm.Persistence.SaveAttachment(bi);
                    var att = new Models.BodyAttachment
                    {
                        FileName = savedFileName,
                        Width = bi.PixelWidth,
                        Height = bi.PixelHeight,
                        Caption = fileName,
                    };
                    task.Attachments = new System.Collections.Generic.List<Models.BodyAttachment>(task.Attachments) { att };
                }
                else
                {
                    // Reference the file in the body.
                    _vm.AddTask(title, body: $"📎 Dropped file: {path}");
                }
                created++;
            }
            catch (Exception ex)
            {
                Trace.Log("dnd", $"Failed to create task from '{path}': {ex.Message}");
            }
        }
        if (created > 0)
            _vm.StatusText = $"📥 Created {created} task{(created == 1 ? "" : "s")} from drop";
    }

    /// <summary>Create a task from dropped text. First non-empty line
    /// becomes the title; the rest becomes the body. Outlook emails
    /// typically arrive as "Subject\nFrom\n\nBody" — we use the subject
    /// as title and the rest as body verbatim.</summary>
    private void CreateTaskFromText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string title = "Dropped task";
        string body;
        // Find first non-empty line for title
        int firstNonEmpty = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                firstNonEmpty = i;
                title = lines[i].Trim();
                break;
            }
        }
        if (firstNonEmpty >= 0 && firstNonEmpty < lines.Length - 1)
        {
            body = string.Join("\n", lines, firstNonEmpty + 1, lines.Length - firstNonEmpty - 1).Trim();
        }
        else
        {
            body = "";
        }
        // Cap title length so we don't end up with a paragraph as the title
        if (title.Length > 120) title = title[..117] + "...";
        _vm.AddTask(title, body: body);
        _vm.StatusText = $"📥 Created: {(title.Length > 32 ? title[..32] + "…" : title)}";
    }

    // ── Bucket dropdown ─────────────────────────────────────────────

    private bool _suppressBucketCombo;
    private void RefreshBucketCombo()
    {
        _suppressBucketCombo = true;
        BucketCombo.Items.Clear();
        // "All buckets" pseudo-entry first
        var allItem = new ComboBoxItem { Content = "All buckets", Tag = "" };
        BucketCombo.Items.Add(allItem);
        foreach (var b in _vm.Buckets)
        {
            var item = new ComboBoxItem { Content = b.Name, Tag = b.Id };
            BucketCombo.Items.Add(item);
            if (b.Id == _vm.ActiveBucketId) BucketCombo.SelectedItem = item;
        }
        if (BucketCombo.SelectedItem is null)
        {
            // Active bucket not found — fall back to "All"
            BucketCombo.SelectedIndex = 0;
        }
        _suppressBucketCombo = false;
    }

    private void OnBucketCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBucketCombo) return;
        if (BucketCombo.SelectedItem is ComboBoxItem item)
        {
            _vm.ActiveBucketId = (string)(item.Tag ?? "");
        }
    }

    private void OnManageBuckets_Click(object sender, RoutedEventArgs e)
    {
        BucketManagerDialog.Show(this, _vm);
        RefreshBucketCombo();
    }

    // ── Reports button + menu ──────────────────────────────────────
    //
    // The 📊 button on the toolbar opens its own ContextMenu (declared
    // inline in MainWindow.xaml). The menu offers Daily summary / Weekly
    // report / Notification settings — all reachable from the tray
    // menu too, but having them in the app proper makes them
    // discoverable without going hunting in the system tray.

    private void OnReports_Click(object sender, RoutedEventArgs e)
    {
        // Manually open the button's ContextMenu since clicking a Button
        // doesn't auto-open its menu (that's right-click behavior). Anchor
        // the menu to the button itself, placed below.
        if (ReportsMenu != null)
        {
            ReportsMenu.PlacementTarget = ReportsBtn;
            ReportsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ReportsMenu.IsOpen = true;
        }
    }

    private void OnShowDailyDigest_Click(object sender, RoutedEventArgs e)
    {
        // User-initiated — don't mark today as shown so the morning pop
        // still happens automatically.
        ShowDigestPopup(updateLastShownOnDismiss: false);
    }

    private void OnShowWeeklyReport_Click(object sender, RoutedEventArgs e)
    {
        WeeklyReportDialog.Show(this, _vm);
    }

    private void OnNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog();
    }

    // ── Filter radio buttons ────────────────────────────────────────

    private void OnFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            // Defensive: when IsChecked="True" is set on a RadioButton
            // declaratively in XAML, the Checked event fires during XAML
            // parsing — BEFORE other named elements (like DoneActionsBar
            // declared further down in the file) have been created.
            // _vm itself is fine (it's instantiated in the field
            // initializer before InitializeComponent runs), but any UI
            // element accessed here might still be null at parse-time.
            // Skip silently; the proper state gets applied on Loaded.
            if (_vm is null) return;
            _vm.ActiveFilter = tag;

            // Show the bulk-archive bar only when the Done filter is active.
            // This is where completed tasks accumulate and where the user
            // most plausibly wants a clean-up affordance.
            if (DoneActionsBar is not null)
            {
                DoneActionsBar.Visibility = tag == "Done"
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
    }

    private void OnArchiveAllDone_Click(object sender, RoutedEventArgs e)
    {
        var count = _vm.AllTasks.Count(t =>
            !t.IsArchived &&
            t.State == Models.TaskState.Done &&
            (string.IsNullOrEmpty(_vm.ActiveBucketId) || t.BucketId == _vm.ActiveBucketId));
        if (count == 0)
        {
            MessageBox.Show("Nothing to archive — no Done tasks in the current view.",
                "Archive all done", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var scope = string.IsNullOrEmpty(_vm.ActiveBucketId)
            ? "across all buckets"
            : $"in the '{_vm.Buckets.FirstOrDefault(b => b.Id == _vm.ActiveBucketId)?.Name ?? "?"}' bucket";
        var msg = $"Archive {count} done task{(count == 1 ? "" : "s")} {scope}?\n\n" +
                  "Archived tasks are hidden from views but not deleted — they can be recovered later.";
        if (MessageBox.Show(msg, "Archive all done",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _vm.ArchiveAllDone();
        }
    }

    // ── Sort toggle ─────────────────────────────────────────────────

    private void OnSortToggle_Click(object sender, RoutedEventArgs e)
    {
        // Cycle: Date → Manual → Date
        _vm.SortMode = _vm.SortMode == "Manual" ? "DueAsc" : "Manual";
        RefreshSortButton();
    }

    private void RefreshSortButton()
    {
        SortToggleBtn.Content = _vm.SortMode == "Manual" ? "↕ Manual" : "↕ Date";
    }

    // ── Drag-and-drop reorder ──────────────────────────────────────
    //
    // Standard WPF ListBox reorder pattern:
    //  1. PreviewMouseLeftButtonDown captures start point + the item under
    //     the cursor (if any).
    //  2. PreviewMouseMove checks if the cursor has moved past a drag
    //     threshold (small to avoid accidental drags); if so, calls
    //     DragDrop.DoDragDrop with the captured TaskItem.
    //  3. DragOver does insertion-line UX (we keep it simple — let WPF's
    //     default cursor change suffice).
    //  4. Drop calculates where to insert based on the cursor's Y position
    //     and calls _vm.ReorderTask.
    //
    // The state-glyph hit zone (the leftmost column of the row) is
    // exempted from drag init so clicks on it still cycle state.

    private Point _dragStartPoint;
    private TaskItem? _dragSource;

    private void OnTaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't initiate drag if the user clicked on the state glyph zone
        // (the leftmost 30px of the row); that's reserved for state cycling.
        var origin = e.OriginalSource as DependencyObject;
        if (origin is not null)
        {
            var hitBorder = FindAncestor<Border>(origin, b => b.Name == "RowBorder");
            if (hitBorder is not null)
            {
                var pos = e.GetPosition(hitBorder);
                if (pos.X < 30)
                {
                    _dragSource = null;
                    return;
                }
            }
        }
        _dragStartPoint = e.GetPosition(TaskList);
        _dragSource = FindTaskItemAt(e.OriginalSource as DependencyObject);
    }

    private void OnTaskList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(TaskList);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        try
        {
            DragDrop.DoDragDrop(TaskList, _dragSource, DragDropEffects.Move);
        }
        catch (Exception ex) { Trace.Log("dnd", $"DoDragDrop failed: {ex.Message}"); }
        finally
        {
            _dragSource = null;
        }
    }

    private void OnTaskList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTaskList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TaskItem)) is not TaskItem dragged) return;
        var dropTarget = FindTaskItemAt(e.OriginalSource as DependencyObject);

        int newIndex;
        if (dropTarget is null)
        {
            // Dropped in empty space below the last row → move to end.
            newIndex = _vm.VisibleTasks.Count - 1;
        }
        else if (dropTarget == dragged)
        {
            return;  // dropped on itself
        }
        else
        {
            newIndex = _vm.VisibleTasks.IndexOf(dropTarget);
            // If dragging downward, the index is "above the target"; if
            // dragging upward, it's "at the target's position".
            var oldIndex = _vm.VisibleTasks.IndexOf(dragged);
            if (newIndex > oldIndex) newIndex -= 0;  // dropping after target visually
        }
        _vm.ReorderTask(dragged, newIndex);
        RefreshSortButton();
        _vm.StatusText = "↕ Reordered (sort: Manual)";
    }

    /// <summary>Walk up the visual tree from a hit element to find the
    /// TaskItem it belongs to (via DataContext on the row Border).</summary>
    private static TaskItem? FindTaskItemAt(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is FrameworkElement fe && fe.DataContext is TaskItem t) return t;
            origin = System.Windows.Media.VisualTreeHelper.GetParent(origin);
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? origin, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        while (origin is not null)
        {
            if (origin is T match && (predicate is null || predicate(match))) return match;
            origin = System.Windows.Media.VisualTreeHelper.GetParent(origin);
        }
        return null;
    }

    // ── Search bar ──────────────────────────────────────────────────

    private void OnSearchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (SearchBar.Visibility == Visibility.Visible) HideSearchBar();
        else ShowSearchBar();
    }

    private void ShowSearchBar()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void HideSearchBar()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        _vm.SearchTerm = "";
    }

    private void OnSearchClose_Click(object sender, RoutedEventArgs e) => HideSearchBar();

    private void OnSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.SearchTerm = SearchBox.Text;
    }

    private void OnSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideSearchBar();
    }

    // ── Quick-add bar ───────────────────────────────────────────────

    private void OnAddTask_Click(object sender, RoutedEventArgs e) => ShowQuickAddBar();

    private void ShowQuickAddBar()
    {
        QuickAddBar.Visibility = Visibility.Visible;
        QuickAddText.Text = "";
        QuickAddDate.SelectedDate = null;
        QuickAddDate.Text = "";
        QuickAddPerson.Text = "";

        // Populate the bucket dropdown each time we show — buckets may
        // have been added/renamed since last time. Default selection is
        // the currently-active bucket (or the default bucket if "All
        // buckets" is selected, since you can't create a task in "All").
        QuickAddBucket.Items.Clear();
        var defaultId = string.IsNullOrEmpty(_vm.ActiveBucketId)
            ? Models.Bucket.DefaultBucketId
            : _vm.ActiveBucketId;
        foreach (var b in _vm.Buckets)
        {
            var item = new ComboBoxItem { Content = b.Name, Tag = b.Id };
            QuickAddBucket.Items.Add(item);
            if (b.Id == defaultId) QuickAddBucket.SelectedItem = item;
        }
        if (QuickAddBucket.SelectedItem is null && QuickAddBucket.Items.Count > 0)
            QuickAddBucket.SelectedIndex = 0;

        QuickAddText.Focus();
    }

    private void HideQuickAddBar()
    {
        QuickAddBar.Visibility = Visibility.Collapsed;
    }

    private void OnQuickAddSave_Click(object sender, RoutedEventArgs e) => CommitQuickAdd();
    private void OnQuickAddCancel_Click(object sender, RoutedEventArgs e) => HideQuickAddBar();

    private void OnQuickAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitQuickAdd();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideQuickAddBar();
            e.Handled = true;
        }
    }

    private void CommitQuickAdd()
    {
        var title = QuickAddText.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            HideQuickAddBar();
            return;
        }
        // Date input has two paths:
        //   • User picked from the calendar → SelectedDate is populated
        //   • User typed in the text portion → Text holds the raw string,
        //     and SelectedDate may or may not be set depending on whether
        //     WPF could parse it. Try ParseFriendlyDate on the raw text
        //     first (supports "today", "tomorrow", weekday names); if that
        //     returns null, fall back to SelectedDate.
        DateTime? due = null;
        if (!string.IsNullOrWhiteSpace(QuickAddDate.Text))
        {
            due = ParseFriendlyDate(QuickAddDate.Text);
        }
        if (due is null) due = QuickAddDate.SelectedDate;

        var person = QuickAddPerson.Text?.Trim();

        // Resolve the target bucket. The user picked it from the dropdown;
        // fall back to active bucket / default if no selection somehow.
        string? bucketId = null;
        if (QuickAddBucket.SelectedItem is ComboBoxItem item && item.Tag is string id)
            bucketId = id;

        _vm.AddTask(title, due,
            string.IsNullOrEmpty(person) ? null : person,
            bucketId: bucketId);
        HideQuickAddBar();
    }

    /// <summary>Parse a friendly date string. Supports:
    /// • Empty → null
    /// • "today", "tomorrow" → relative to DateTime.Today
    /// • Weekday names ("monday", "fri", "thu") → next occurrence
    /// • Any DateTime.TryParse-able string (e.g. "2026-07-04", "Jul 4")
    /// Returns null if input is empty or unparseable.</summary>
    private static DateTime? ParseFriendlyDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().ToLowerInvariant();
        if (s == "today") return DateTime.Today;
        if (s == "tomorrow" || s == "tmr" || s == "tmrw") return DateTime.Today.AddDays(1);
        // Weekday names: next occurrence after today
        string[] weekdays = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };
        string[] weekdayShort = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };
        for (int i = 0; i < 7; i++)
        {
            if (s == weekdays[i] || s == weekdayShort[i])
            {
                int delta = ((int)(DayOfWeek)i - (int)DateTime.Today.DayOfWeek + 7) % 7;
                if (delta == 0) delta = 7;  // "monday" when today IS monday = next monday
                return DateTime.Today.AddDays(delta);
            }
        }
        if (DateTime.TryParse(text, out var d)) return d.Date;
        return null;
    }

    // ── Task row interactions ──────────────────────────────────────

    private void OnTaskRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
        {
            var anchorTop = e.GetPosition(this).Y - 20;
            _previewPopup?.ScheduleShow(task, anchorTop);
        }
    }

    private void OnTaskRow_MouseLeave(object sender, MouseEventArgs e)
    {
        // Don't hide immediately — give the user a grace period to mouse
        // INTO the popup (e.g. to click an Open URL button). RequestHide
        // starts a short delayed-hide timer that the popup itself can
        // cancel via its MouseEnter handler.
        _previewPopup?.RequestHide();
    }

    private void OnTaskRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Single-click row body: select / show preview (already on hover).
        // Double-click: open editor.
        if (e.ClickCount == 2)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
                OpenEditor(task);
        }
    }

    private void OnTaskRow_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Right-click is handled by the row's ContextMenu in XAML; no-op here.
    }

    private void OnStateGlyph_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
        {
            ShowStatePickerForRow(fe, task);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Show a small popup with the three state options (○ Open /
    /// ◐ In progress / ● Done) anchored next to the row's state glyph.
    /// Replaces the previous "click-to-cycle" behavior, which could
    /// accidentally bury tasks if the user clicked twice too quickly
    /// or didn't realize the glyph cycled. Now state change is a
    /// deliberate two-click action: click the glyph, click a state.
    /// Click-outside dismisses with no change.
    /// </summary>
    private void ShowStatePickerForRow(FrameworkElement anchor, TaskItem task)
    {
        // Container: a small bordered panel showing the picker. We use
        // a WPF Popup (lightweight) anchored to the glyph element so
        // it dismisses automatically on click-outside (StaysOpen=false).
        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
        };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
            HorizontalOffset = 6,
            StaysOpen = false,        // click outside → close
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            Child = border,
        };

        // The shared TaskStatePicker builds the 3-button row. On click,
        // apply state and close the popup. Marking Done shows the
        // completion popup (comment + by-line + recurrence scheduling)
        // — same flow as the preview popup's state picker.
        border.Child = TaskStatePicker.Build(task, newState =>
        {
            popup.IsOpen = false;  // close picker FIRST so completion popup isn't behind it
            if (task.State != newState)
            {
                task.State = newState;
                if (newState == TaskState.Done)
                    PromptAndRecordCompletion(task);
                _vm.RebuildVisible();
                _vm.OnTaskMutated();
            }
        });

        popup.IsOpen = true;
    }

    private static TaskItem? GetTaskFrom(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskItem t) return t;
        if (sender is MenuItem mi && mi.DataContext is TaskItem t2) return t2;
        return null;
    }

    private void OnTaskMenu_Edit(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) OpenEditor(task);
    }

    // ── Set status from context menu ──────────────────────────────────
    // Direct-set the task's state. Marking Done spawns the next
    // occurrence if the task is recurring, mirroring CycleState's
    // recurrence path. Refresh visible list and persist after.

    private void OnTaskMenu_SetOpen(object sender, RoutedEventArgs e)
        => SetTaskStateFromMenu(sender, TaskState.Open);

    private void OnTaskMenu_SetInProgress(object sender, RoutedEventArgs e)
        => SetTaskStateFromMenu(sender, TaskState.InProgress);

    private void OnTaskMenu_SetDone(object sender, RoutedEventArgs e)
        => SetTaskStateFromMenu(sender, TaskState.Done);

    /// <summary>Centralized "a task was just marked Done" flow. Shows
    /// the combined completion popup (comment + completed-by + for
    /// recurring tasks, next-due / skip-recurrence), records the
    /// completion data on the task, and spawns the next instance if
    /// applicable. Called from all three completion paths — preview
    /// popup state picker, row glyph picker, right-click menu Set
    /// Done — so the experience is identical regardless of how the
    /// user marked it done.</summary>
    private void PromptAndRecordCompletion(TaskItem completed)
    {
        // For recurring tasks, compute the smart-clamped next-due AND
        // the default auto-defer "show on" date so the popup can show
        // both controls pre-populated. For non-recurring, both are
        // null and the popup hides its recurrence block.
        DateTime? proposedNextDue = null;
        DateTime? proposedShowOn = null;
        if (completed.Recurrence != RecurrencePattern.None)
        {
            proposedNextDue = _vm.ComputeNextRecurrence(completed);
            if (proposedNextDue is { } nd)
            {
                proposedShowOn = _vm.ComputeAutoStartDate(completed, nd);
            }
        }

        var result = CompletionDialog.Show(this, completed,
            proposedNextDue, proposedShowOn, _vm.People);

        // Record the completion data even if the user dismissed via
        // "Skip details" — that button still captures whatever's in
        // the form, just doesn't enforce it. (See CompletionDialog
        // Cancel button.) An empty record is still meaningful: it
        // marks WHEN the completion happened, even without a comment.
        completed.Completions.Add(new CompletionRecord
        {
            At = DateTime.Now,
            CompletedBy = result.CompletedBy?.Trim() ?? "",
            Comment = result.Comment?.Trim() ?? "",
        });

        // Spawn the next instance if recurring and not opted out. The
        // spawn method honors ShowOnExplicitlyCleared: if the user
        // unchecked "Hide until closer to due", we pass cleared=true
        // so the spawn doesn't re-apply the pattern default.
        if (result.SpawnNext && result.NextDue.HasValue)
        {
            _vm.SpawnRecurrenceFor(completed,
                result.NextDue,
                result.ShowOnDate,
                result.ShowOnExplicitlyCleared);
        }
    }

    private void SetTaskStateFromMenu(object sender, TaskState newState)
    {
        var task = GetTaskFrom(sender);
        if (task is null || task.State == newState) return;
        task.State = newState;
        if (newState == TaskState.Done)
            PromptAndRecordCompletion(task);
        _vm.RebuildVisible();
        _vm.OnTaskMutated();
    }

    private void OpenEditor(TaskItem task)
    {
        _previewPopup?.Hide();
        if (TaskDetailEditor.Show(this, task, _vm.People, _vm.Persistence, _vm.Buckets))
        {
            if (!string.IsNullOrWhiteSpace(task.ResponsiblePerson))
                _vm.AddPerson(task.ResponsiblePerson);
            _vm.RebuildVisible();
            _vm.ScheduleSave();
        }
    }

    private void OnTaskMenu_SetDueDate(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is null) return;
        var input = InputPrompt.Show(this,
            "Due date (YYYY-MM-DD format, or leave blank to clear):",
            "Set due date",
            task.DueDate?.ToString("yyyy-MM-dd") ?? "");
        if (input is null) return;
        if (string.IsNullOrWhiteSpace(input)) { task.DueDate = null; _vm.RebuildVisible(); _vm.ScheduleSave(); return; }
        if (DateTime.TryParse(input, out var d))
        {
            task.DueDate = d.Date;
            _vm.RebuildVisible();
            _vm.ScheduleSave();
        }
        else
        {
            MessageBox.Show("Could not parse that date. Try YYYY-MM-DD format.",
                "Set due date", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnTaskMenu_SetPerson(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is null) return;
        var input = InputPrompt.Show(this,
            "Responsible person (leave blank to clear):",
            "Set responsible person",
            task.ResponsiblePerson,
            maxLength: 60);
        if (input is null) return;
        task.ResponsiblePerson = input.Trim();
        if (!string.IsNullOrEmpty(task.ResponsiblePerson))
            _vm.AddPerson(task.ResponsiblePerson);
        _vm.RebuildVisible();
        _vm.ScheduleSave();
    }

    private void OnTaskMenu_SetRecurrence(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is null) return;
        // Cycle through recurrence patterns
        task.Recurrence = task.Recurrence switch
        {
            RecurrencePattern.None => RecurrencePattern.Daily,
            RecurrencePattern.Daily => RecurrencePattern.Weekly,
            RecurrencePattern.Weekly => RecurrencePattern.Monthly,
            RecurrencePattern.Monthly => RecurrencePattern.Yearly,
            RecurrencePattern.Yearly => RecurrencePattern.None,
            _ => RecurrencePattern.None,
        };
        _vm.StatusText = $"🔁 Recurrence: {task.Recurrence}";
        _vm.RebuildVisible();
        _vm.ScheduleSave();
    }

    private void OnTaskMenu_MoveBucket(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is null) return;
        // Build a small popup menu of buckets to move to
        var menu = new ContextMenu();
        foreach (var b in _vm.Buckets.Where(b => b.Id != task.BucketId))
        {
            var item = new MenuItem { Header = b.Name };
            var capturedId = b.Id;
            item.Click += (_, _) => _vm.MoveToBucket(task, capturedId);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnTaskMenu_SnoozeTomorrow(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.Snooze(task, "Tomorrow");
    }
    private void OnTaskMenu_Snooze2Days(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.Snooze(task, "2Days");
    }
    private void OnTaskMenu_SnoozeWeekend(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.Snooze(task, "Weekend");
    }
    private void OnTaskMenu_Snooze1Week(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.Snooze(task, "1Week");
    }
    private void OnTaskMenu_Snooze2Weeks(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.Snooze(task, "2Weeks");
    }
    private void OnTaskMenu_SnoozeNextWeek(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.Snooze(task, "NextWeek");
    }
    private void OnTaskMenu_SnoozePickDate(object sender, RoutedEventArgs e)
    {
        // Open a small modal with a DatePicker. Reuses the same helper
        // the daily digest snooze menu uses — single source of truth
        // for the "pick a date" UX.
        var task = GetTaskFrom(sender);
        if (task is null) return;
        var picked = DateSnoozePicker.Show(this, task.DueDate);
        if (picked is { } d) _vm.SnoozeTo(task, d);
    }

    private void OnTaskMenu_Archive(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is not null) _vm.ArchiveTask(task);
    }

    private void OnTaskMenu_Delete(object sender, RoutedEventArgs e)
    {
        var task = GetTaskFrom(sender);
        if (task is null) return;
        var titlePreview = task.Title.Length > 40 ? task.Title[..40] + "…" : task.Title;
        var msg = $"Delete this task?\n\n  \"{titlePreview}\"\n\nThis can't be undone (attachments will be deleted too).";
        if (MessageBox.Show(msg, "Delete task",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _vm.DeleteTask(task);
        }
    }
}
