using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskNinja.Views;

/// <summary>
/// Small modal for picking an arbitrary date — used by the snooze flow
/// in two places: the daily digest's per-row 💤 menu ("Pick a date...")
/// and the main list's right-click → Snooze → Pick a date... item.
/// Centralized here so both entry points share the same UX.
/// </summary>
public static class DateSnoozePicker
{
    /// <summary>Show the picker. Returns the selected date or null if
    /// the user cancelled.</summary>
    public static DateTime? Show(Window owner, DateTime? current)
    {
        DateTime? result = null;
        var dlg = new Window
        {
            Title = "Snooze to date",
            Owner = owner,
            Width = 320,
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
        var stack = new StackPanel { Margin = new Thickness(16) };
        chrome.Child = stack;

        // Header is also the drag handle since WindowStyle=None
        var header = new TextBlock
        {
            Text = "💤  Pick a new due date",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.SizeAll,
        };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { dlg.DragMove(); } catch { }
            }
        };
        stack.Children.Add(header);

        var picker = new DatePicker
        {
            // Default to one day after the current due (or tomorrow if
            // no due). Most snooze actions push forward; this saves a
            // click in the common case.
            SelectedDate = current?.AddDays(1) ?? DateTime.Today.AddDays(1),
            Style = (Style)Application.Current.Resources["DarkDatePickerStyle"],
            Margin = new Thickness(0, 0, 0, 12),
        };
        stack.Children.Add(picker);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
            IsCancel = true,
        };
        cancelBtn.Click += (_, _) => dlg.Close();
        var okBtn = new Button
        {
            Content = "Snooze",
            Padding = new Thickness(14, 5, 14, 5),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"],
            IsDefault = true,
        };
        okBtn.Click += (_, _) =>
        {
            result = picker.SelectedDate;
            dlg.Close();
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        stack.Children.Add(btnRow);

        dlg.Content = chrome;
        dlg.ShowDialog();
        return result;
    }
}
