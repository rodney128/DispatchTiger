using System;
using System.Globalization;
using System.Windows.Data;

namespace DispatchTiger.Converters
{
    /// <summary>
    /// Returns true when both bound values are equal. Used in DataTrigger
    /// MultiBindings where DataTrigger.Value cannot accept a Binding directly.
    /// </summary>
    public class EqualityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values.Length == 2 && Equals(values[0], values[1]);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
