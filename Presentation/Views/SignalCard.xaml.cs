using System.Windows.Controls;
using CSharp_WPF_Websockets.Domain.Entities;
using CSharp_WPF_Websockets.Presentation.ViewModels;

namespace CSharp_WPF_Websockets.Presentation.Views
{
    public partial class SignalCard : UserControl
    {
        public SignalCard()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is DeviceSignal signal)
            {
                DataContext = new SignalCardViewModel(signal);
            }
        }
    }
}