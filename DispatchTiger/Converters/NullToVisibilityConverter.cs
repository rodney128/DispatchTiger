using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DispatchTiger.Converters;

/// <summary>
/// Converts null values to Visible and non-null to Collapsed.
/// Used for showing placeholder text when nothing is selected.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}