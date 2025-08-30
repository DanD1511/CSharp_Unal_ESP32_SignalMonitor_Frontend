using System;
using System.ComponentModel;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CSharp_WPF_Websockets.Domain.Entities
{
    public partial class DeviceSignal : ObservableObject
    {
        [ObservableProperty]
        private string _id;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _type;

        [ObservableProperty]
        private double _value;

        [ObservableProperty]
        private string _unit;

        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private SignalStatus _status;

        [ObservableProperty]
        private double _minValue;

        [ObservableProperty]
        private double _maxValue;

        [ObservableProperty]
        private string _color;

        public DeviceSignal()
        {
            Id = string.Empty;
            Name = string.Empty;
            Type = string.Empty;
            Unit = string.Empty;
            Color = "#6C757D";
            Timestamp = DateTime.Now;
        }
    }

    public enum SignalStatus
    {
        Normal,
        Warning,
        Critical,
        Offline
    }
}