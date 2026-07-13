using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskNinja.Models;
using TaskNinja.ViewModels;

namespace TaskNinja.Views;

/// <summary>
/// Dialog for managing buckets — add, rename, delete. Lists every
/// existing bucket; default bucket can't be deleted but can be renamed.
/// Closes via X / Esc; changes are applied immediately to the VM and
/// persisted via the normal debounced save path.
/// </summary>
public class BucketManagerDialog
{
    public static void Show(Window owner, MainViewModel vm)
    {
        var dialog = new Window
        {
            Title = "Manage buckets",
            Width = 360,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            // Same "no minimize button" fix as TaskDetailEditor —
            // ShowInTaskbar=false + a minimize button = a modal that
            // can vanish into thin air, blocking the main app with
            // nothing to click on.
            WindowStyle = WindowStyle.ToolWindow,
            Background = (Brush)Application.Current.Resources["BgBrush"],
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "Buckets are top-level containers for your tasks. The default bucket (\"Tasks\") can't be deleted — but renaming is fine.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SubTextBrush"],
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var listBox = new ListBox
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };
        Grid.SetRow(listBox, 1);
        root.Children.Add(listBox);

        void RefreshList()
        {
            listBox.Items.Clear();
            foreach (var b in vm.Buckets)
            {
                var row = new Grid { Margin = new Thickness(4, 2, 4, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var taskCount = vm.AllTasks.Count(t => t.BucketId == b.Id && !t.IsArchived);
                var label = new TextBlock
                {
                    Text = $"{b.Name}  ({taskCount} task{(taskCount == 1 ? "" : "s")})",
                    Foreground = (Brush)Application.Current.Resources["TextBrush"],
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var renameBtn = new Button
                {
                    Content = "Rename",
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(4, 0, 4, 0),
                    FontSize = 10,
                    Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
                };
                renameBtn.Click += (_, _) =>
                {
                    var input = InputPrompt.Show(dialog,
                        $"Rename bucket '{b.Name}':", "Rename bucket",
                        b.Name, maxLength: 60);
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        vm.RenameBucket(b, input);
                        RefreshList();
                    }
                };
                Grid.SetColumn(renameBtn, 1);
                row.Children.Add(renameBtn);

                var deleteBtn = new Button
                {
                    Content = "Delete",
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 0, 0),
                    FontSize = 10,
                    Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
                    IsEnabled = b.Id != Bucket.DefaultBucketId,
                };
                deleteBtn.Click += (_, _) =>
                {
                    var msg = taskCount > 0
                        ? $"Delete bucket '{b.Name}'?\n\n{taskCount} task{(taskCount == 1 ? "" : "s")} will be moved to the default bucket."
                        : $"Delete bucket '{b.Name}'?";
                    if (MessageBox.Show(msg, "Delete bucket",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        vm.DeleteBucket(b);
                        RefreshList();
                    }
                };
                Grid.SetColumn(deleteBtn, 2);
                row.Children.Add(deleteBtn);

                listBox.Items.Add(row);
            }
        }
        RefreshList();

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var addBtn = new Button
        {
            Content = "+ Add bucket",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        addBtn.Click += (_, _) =>
        {
            var name = InputPrompt.Show(dialog,
                "Name for the new bucket:", "Add bucket",
                "", maxLength: 60);
            if (!string.IsNullOrWhiteSpace(name))
            {
                vm.AddBucket(name);
                RefreshList();
            }
        };
        var closeBtn = new Button
        {
            Content = "Close",
            Padding = new Thickness(16, 4, 16, 4),
            IsCancel = true,
            IsDefault = true,
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        closeBtn.Click += (_, _) => dialog.Close();
        btnRow.Children.Add(addBtn);
        btnRow.Children.Add(closeBtn);
        Grid.SetRow(btnRow, 2);
        root.Children.Add(btnRow);

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
