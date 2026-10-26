using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskNinja.Models;

namespace TaskNinja.Views;

/// <summary>
/// Reusable 3-button row for setting a task's state. Used in both the
/// hover preview popup and the detail editor so the UX is identical
/// wherever you change state.
///
/// Layout:  [ ○ Open ] [ ◐ In progress ] [ ● Done ]
/// The currently-active state is shown with the amber accent background
/// and bold text; the other two are outlined buttons. Clicking any
/// button sets the task to that state (no toggling/cycling — direct set,
/// since the visible buttons make the destination obvious).
///
/// Caller supplies a callback invoked after state changes so the
/// hosting view can refresh its presentation (e.g., recolor the chip,
/// dismiss the popup, etc.).
/// </summary>
public static class TaskStatePicker
{
    public static FrameworkElement Build(TaskItem task, Action<TaskState> onStateChanged)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };

        panel.Children.Add(MakeStateButton(task, TaskState.Open, "○  Open", onStateChanged));
        panel.Children.Add(MakeStateButton(task, TaskState.InProgress, "◐  In progress", onStateChanged));
        panel.Children.Add(MakeStateButton(task, TaskState.Done, "●  Done", onStateChanged));

        return panel;
    }

    private static Button MakeStateButton(TaskItem task, TaskState target, string label,
        Action<TaskState> onStateChanged)
    {
        var isActive = task.State == target;
        var btn = new Button
        {
            Content = label,
            FontSize = 12,
            FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            // Active button takes amber bg + dark text; inactive is outlined.
            Background = isActive
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = isActive
                ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1A, 0x14))
                : (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = isActive
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            ToolTip = $"Set to {target}",
        };
        // Override default template so background actually paints (default
        // WPF button template ignores Background for the look-and-feel reasons).
        btn.Template = MakeButtonTemplate();
        btn.Click += (_, _) => onStateChanged(target);
        return btn;
    }

    private static ControlTemplate MakeButtonTemplate()
    {
        // Minimal control template: a Border that respects Background +
        // BorderBrush + BorderThickness from the Button, with rounded
        // corners and a hover state.
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "RootBorder");
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty,
            new System.Windows.Data.Binding("Padding")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);

        template.VisualTree = border;

        // Hover trigger: subtle darken on non-active buttons.
        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hoverTrigger.Setters.Add(new Setter
        {
            TargetName = "RootBorder",
            Property = Border.OpacityProperty,
            Value = 0.85,
        });
        template.Triggers.Add(hoverTrigger);

        return template;
    }
}
