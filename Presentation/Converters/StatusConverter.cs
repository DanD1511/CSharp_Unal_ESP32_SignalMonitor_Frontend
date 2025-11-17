using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CSharp_WPF_Websockets.Presentation.Converters
{
    public class StatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isText = parameter?.ToString() == "Text";

            if (value is DateTime timestamp)
            {
                var timeDiff = DateTime.Now - timestamp;

                if (timeDiff.TotalSeconds < 5)
                {
                    return isText ? "Live" : Brushes.Green;
                }
                else if (timeDiff.TotalMinutes < 1)
                {
                    return isText ? "Recent" : Brushes.Orange;
                }
                else
                {
                    return isText ? "Stale" : Brushes.Red;
                }
            }

            return isText ? "Unknown" : Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}