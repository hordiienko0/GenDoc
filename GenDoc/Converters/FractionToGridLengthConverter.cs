using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GenDoc.Converters
{
    public class FractionToGridLengthConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var fraction = value is double d ? Math.Max(0, d) : 0;
            return new GridLength(fraction, GridUnitType.Star);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
