using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace CSharp_WPF_Websockets.Presentation.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Instance = new BoolToColorConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Colors.LimeGreen : Colors.Gray;
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Convierte bool a Visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new BoolToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// Convierte DateTime a Color basado en qué tan reciente es
    /// </summary>
    public class TimestampToStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime timestamp)
            {
                var diff = DateTime.Now - timestamp;
                if (diff.TotalSeconds < 5)
                    return new SolidColorBrush(Colors.LimeGreen);
                if (diff.TotalMinutes < 1)
                    return new SolidColorBrush(Colors.Orange);
                return new SolidColorBrush(Colors.Red);
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Formatea valores numéricos según el tipo de señal
    /// </summary>
    public class SmartValueFormatter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is double value &&
                values[1] is string type &&
                values[2] is string unit)
            {
                // Formato especial para señales de alta frecuencia
                if (type?.ToLower().Contains("sine") == true)
                {
                    return $"{value:F3} {unit}";
                }

                // Formato para valores digitales
                if (unit?.ToLower() == "bool" || type?.ToLower().Contains("digital") == true)
                {
                    return value > 0 ? "ON" : "OFF";
                }

                // Formato estándar
                return $"{value:F1} {unit}";
            }

            return values[0]?.ToString() ?? "N/A";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
