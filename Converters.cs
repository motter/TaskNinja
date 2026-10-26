using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskNinja;

/// <summary>
/// Visibility converter: non-empty string → Visible, empty → Collapsed.
/// Used by the person chip on the task row.
/// </summary>
public class StringToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s)) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Visibility converter: true → Visible, false → Collapsed. Used by the
/// ❗ important marker and the tag-chip strip, both of which should take
/// up ZERO space (not just be invisible) when they don't apply.
/// </summary>
public class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
