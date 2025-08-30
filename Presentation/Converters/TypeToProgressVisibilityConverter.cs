using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CSharp_WPF_Websockets.Presentation.Converters
{
    public class TypeToProgressVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = value?.ToString();
            return type != "Digital" ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}