using System.Globalization;
using System.Windows.Data;
using CSharp_WPF_Websockets.Domain.Entities;
using CSharp_WPF_Websockets.Presentation.ViewModels;

namespace CSharp_WPF_Websockets.Presentation.Converters
{
    public class DeviceSignalToCardVMConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DeviceSignal signal)
            {
                return new SignalCardViewModel(signal);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
