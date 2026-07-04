using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DispatchTiger.Converters;

/// <summary>
/// Converts null values to Collapsed and non-null to Visible.
/// Used for showing content cards only when an object is selected.
/// </summary>
public class NullToCollapsedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}