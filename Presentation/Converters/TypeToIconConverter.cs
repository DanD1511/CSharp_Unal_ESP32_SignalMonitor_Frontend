using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace CSharp_WPF_Websockets.Presentation.Converters
{
    public class TypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() switch
            {
                "Temperature" => PackIconKind.Thermometer,
                "Humidity" => PackIconKind.WaterPercent,
                "Pressure" => PackIconKind.GaugeFull,
                "Analog" => PackIconKind.ChartLine,
                "Digital" => PackIconKind.ToggleSwitch,
                _ => PackIconKind.Chip
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}