using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskNinja.Models;

namespace TaskNinja.Views;

/// <summary>
/// Settings dialog. Currently houses notification preferences (daily
/// digest enable/disable + time). Designed as a modal so settings
/// changes are confirmed (Save) or discarded (Cancel) — autosave on
/// every edit would be magical but harder to reason about.
/// </summary>
public class SettingsDialog
{
    /// <summary>Show the dialog modally. Returns true if the user
    /// clicked Save (settings mutated and should be persisted) or
    /// false on Cancel (no changes).</summary>
    public static bool Show(Window owner, AppSettings settings)
    {
        var dialog = new Window
        {
            Title = "Settings",
            Owner = owner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
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

        // Header — also serves as the drag handle. WindowStyle=None
        // means no default titlebar, so we wire DragMove on the header
        // TextBlock so the dialog can be moved out of the way.
        var headerBlock = new TextBlock
        {
            Text = "⚙  Settings  (drag to move)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 0, 0, 14),
            Cursor = System.Windows.Input.Cursors.SizeAll,
        };
        headerBlock.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try { dialog.DragMove(); } catch { }
            }
        };
        root.Children.Add(headerBlock);

        // ── Notifications section ─────────────────────────────────────
        root.Children.Add(SectionLabel("Notifications"));

        var enabledCheck = new CheckBox
        {
            Content = "Show daily summary popup",
            IsChecked = settings.NotificationsEnabled,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            Margin = new Thickness(0, 4, 0, 8),
        };
        root.Children.Add(enabledCheck);

        // Time row: label + textbox (HH:mm)
        var timeRow = new Grid { Margin = new Thickness(20, 0, 0, 12) };
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var timeLabel = new TextBlock
        {
            Text = "Time of day:  ",
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(timeLabel, 0);
        timeRow.Children.Add(timeLabel);

        var timeBox = new TextBox
        {
            Text = settings.DailyDigestTime,
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            ToolTip = "24-hour time, e.g. 08:00 or 17:30",
        };
        Grid.SetColumn(timeBox, 1);
        timeRow.Children.Add(timeBox);

        var timeHint = new TextBlock
        {
            Text = "  24-hour, e.g. 08:00",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(timeHint, 2);
        timeRow.Children.Add(timeHint);
        root.Children.Add(timeRow);

        // Bind enabled state to greying-out the time controls
        void SyncEnabledState()
        {
            var on = enabledCheck.IsChecked == true;
            timeRow.Opacity = on ? 1.0 : 0.5;
            timeBox.IsEnabled = on;
        }
        enabledCheck.Checked += (_, _) => SyncEnabledState();
        enabledCheck.Unchecked += (_, _) => SyncEnabledState();
        SyncEnabledState();

        // Description
        root.Children.Add(new TextBlock
        {
            Text = "Shows overdue and due-today tasks once per day with quick "
                + "actions to mark done or snooze.",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        // ── Updates section ───────────────────────────────────────────
        root.Children.Add(SectionLabel("Updates"));

        root.Children.Add(new TextBlock
        {
            Text = $"You're running v{Services.UpdateService.CurrentVersion.ToString(3)}. Updates come from GitHub Releases of the repo below.",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
        });

        var repoRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var repoLbl = new TextBlock
        {
            Text = "GitHub repo:  ",
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(repoLbl, 0);
        repoRow.Children.Add(repoLbl);
        var repoBox = new TextBox
        {
            Text = settings.UpdateRepo,
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            ToolTip = "owner/name — e.g. motter/TaskNinja",
        };
        Grid.SetColumn(repoBox, 1);
        repoRow.Children.Add(repoBox);
        root.Children.Add(repoRow);

        var autoUpdateCheck = new CheckBox
        {
            Content = "Check for updates at startup (status-bar hint only)",
            IsChecked = settings.AutoCheckForUpdates,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(autoUpdateCheck);

        var checkRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var checkBtn = new Button
        {
            Content = "Check for updates now",
            Padding = new Thickness(12, 4, 12, 4),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var checkResult = new TextBlock
        {
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 230,
        };
        checkBtn.Click += async (_, _) =>
        {
            checkBtn.IsEnabled = false;
            checkResult.Text = "Checking…";
            // Use the LIVE box text so the user can test a repo before
            // hitting Save.
            var (update, error) = await Services.UpdateService.CheckAsync(repoBox.Text.Trim());
            checkBtn.IsEnabled = true;
            if (update is null)
            {
                checkResult.Text = error ?? "✓ You're up to date.";
                return;
            }
            checkResult.Text = $"⬆ {update.TagName} available";
            var notes = string.IsNullOrWhiteSpace(update.Notes)
                ? ""
                : "\n\nRelease notes:\n" + (update.Notes.Length > 600 ? update.Notes[..600] + "…" : update.Notes);
            var answer = MessageBox.Show(dialog,
                $"TaskNinja {update.TagName} is available (you have v{Services.UpdateService.CurrentVersion.ToString(3)}).{notes}\n\n" +
                "Update now? The app will restart.",
                "TaskNinja update",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;
            checkBtn.IsEnabled = false;
            checkResult.Text = "Downloading update…";
            var applyError = await Services.UpdateService.DownloadAndStageAsync(update);
            if (applyError is not null)
            {
                checkBtn.IsEnabled = true;
                checkResult.Text = applyError;
                return;
            }
            // Swap script is waiting for our file lock to release —
            // graceful shutdown with a hard-exit fallback.
            Services.UpdateService.ShutdownForUpdate();
        };
        checkRow.Children.Add(checkBtn);
        checkRow.Children.Add(checkResult);
        root.Children.Add(checkRow);

        // ── Buttons ───────────────────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var saveBtn = new Button
        {
            Content = "Save",
            Padding = new Thickness(16, 5, 16, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"],
            IsDefault = true,
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 5, 14, 5),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
            IsCancel = true,
        };
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(cancelBtn);
        root.Children.Add(btnRow);

        bool saved = false;
        saveBtn.Click += (_, _) =>
        {
            // Validate time format before saving — bad input would silently
            // disable notifications which is confusing.
            var raw = timeBox.Text?.Trim() ?? "";
            if (!TimeSpan.TryParse(raw, out _) &&
                !DateTime.TryParse("2000-01-01 " + raw, out _))
            {
                MessageBox.Show(
                    "Couldn't parse the time. Use a 24-hour format like 08:00 or 17:30.",
                    "Invalid time", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            settings.NotificationsEnabled = enabledCheck.IsChecked == true;
            settings.DailyDigestTime = raw;
            settings.UpdateRepo = repoBox.Text.Trim();
            settings.AutoCheckForUpdates = autoUpdateCheck.IsChecked == true;
            saved = true;
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = chrome;
        dialog.ShowDialog();
        return saved;
    }

    private static TextBlock SectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 4, 0, 4),
        };
    }
}
