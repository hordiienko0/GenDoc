using System.Globalization;
using System.Windows.Data;

namespace GenDoc.Converters
{
    // MultiBinding: [0]=зайнято, [1]=місткість, [2]=ActualWidth контейнера -> ширина смуги заповненості.
    public class OccupancyToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Length < 3) return 0.0;
            if (values[0] is not int count) return 0.0;
            if (values[1] is not int capacity || capacity <= 0) return 0.0;
            if (values[2] is not double actualWidth || actualWidth <= 0) return 0.0;

            var ratio = Math.Min(1.0, Math.Max(0.0, (double)count / capacity));
            return actualWidth * ratio;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
