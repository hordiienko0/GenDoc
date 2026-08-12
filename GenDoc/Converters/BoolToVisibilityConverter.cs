using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GenDoc.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var boolValue = value is bool b && b;

            // ConverterParameter="Invert" — щоб не заводити дзеркальні властивості у в'ю-моделях
            if (parameter is string s && string.Equals(s, "Invert", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
