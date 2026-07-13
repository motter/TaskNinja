using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TaskNinja.Views;

/// <summary>
/// Simple modal text-input dialog. Mirrors the ClipNinja InputPrompt
/// pattern: open with Show(...), returns the entered string, or null
/// if the user cancelled (Esc / close).
/// </summary>
public static class InputPrompt
{
    public static string? Show(Window owner, string prompt, string title,
        string initialValue = "", int maxLength = 0, bool multiline = false)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = multiline ? 260 : 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            // Same "no minimize button" fix as the editor + bucket
            // manager. Modal + ShowInTaskbar=false + minimize button
            // = a dialog that can disappear with no way to recover.
            WindowStyle = WindowStyle.ToolWindow,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush"],
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var promptBlock = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"],
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(promptBlock, 0);
        root.Children.Add(promptBlock);

        var textBox = new TextBox
        {
            Text = initialValue,
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
            Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        if (maxLength > 0) textBox.MaxLength = maxLength;
        Grid.SetRow(textBox, 1);
        root.Children.Add(textBox);

        string? result = null;
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var okBtn = new Button
        {
            Content = "OK",
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 4, 12, 4),
            IsCancel = true,
            Style = (Style)Application.Current.Resources["ToolbarButtonStyle"],
        };
        okBtn.Click += (_, _) => { result = textBox.Text; dialog.DialogResult = true; };
        cancelBtn.Click += (_, _) => { dialog.DialogResult = false; };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        Grid.SetRow(btnRow, 2);
        root.Children.Add(btnRow);

        // For single-line, Enter submits via IsDefault. For multiline, Ctrl+Enter submits.
        textBox.KeyDown += (_, e) =>
        {
            if (multiline && e.Key == Key.Enter &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                result = textBox.Text;
                dialog.DialogResult = true;
            }
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? result : null;
    }
}
