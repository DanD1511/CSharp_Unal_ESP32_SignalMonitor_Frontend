using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CSharp_WPF_Websockets.Domain.Entities;

namespace CSharp_WPF_Websockets.Presentation.ViewModels
{
    public partial class SignalCardViewModel : ObservableObject
    {
        [ObservableProperty]
        private DeviceSignal _signal;

        [ObservableProperty]
        private double _animatedValue;

        [ObservableProperty]
        private string _displayValue;

        [ObservableProperty]
        private string _statusColor;

        [ObservableProperty]
        private string _statusText;

        [ObservableProperty]
        private bool _isOnline;

        public SignalCardViewModel(DeviceSignal signal)
        {
            Signal = signal;
            UpdateDisplayProperties();

            if (Signal is INotifyPropertyChanged notifySignal)
            {
                notifySignal.PropertyChanged += OnSignalPropertyChanged;
            }
        }

        private void OnSignalPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceSignal.Value) ||
                e.PropertyName == nameof(DeviceSignal.Status))
            {
                UpdateDisplayProperties();
            }
        }

        private void UpdateDisplayProperties()
        {
            if (Signal == null) return;

            AnimatedValue = Signal.Value;
            DisplayValue = Signal.Type == "Digital"
                ? (Signal.Value > 0 ? "ON" : "OFF")
                : $"{Signal.Value:F1} {Signal.Unit}";

            StatusColor = Signal.Status switch
            {
                SignalStatus.Normal => "#4ECDC4",
                SignalStatus.Warning => "#FFA500",
                SignalStatus.Critical => "#FF6B6B",
                SignalStatus.Offline => "#6C757D",
                _ => "#6C757D"
            };

            StatusText = Signal.Status.ToString();
            IsOnline = Signal.Status != SignalStatus.Offline;
        }
    }
}