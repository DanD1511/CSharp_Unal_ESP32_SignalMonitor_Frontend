using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CSharp_WPF_Websockets.Domain.Entities
{
    // Cada punto individual enviado por el servidor
    public class SamplePoint
    {
        public double T { get; set; }       // Tiempo relativo del sample
        public double Value { get; set; }   // Voltaje del sample
    }

    // Una señal completa con sus samples
    public partial class DeviceSignal : ObservableObject
    {
        [ObservableProperty]
        private string _id;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _unit;

        [ObservableProperty]
        private double _minValue;

        [ObservableProperty]
        private double _maxValue;

        [ObservableProperty]
        private string _color;

        // ⚡ AQUÍ ESTÁ LA MAGIA
        public List<SamplePoint> Samples { get; set; } = new();

        // DEJAMOS Value si lo necesitas en UI, pero ya NO es usado como valor real
        [ObservableProperty]
        private double _value;

        // Timestamp del paquete, no de cada señal
        [ObservableProperty]
        private DateTime _timestamp;

        // Estado de la señal
        [ObservableProperty]
        private SignalStatus _status;

        public DeviceSignal()
        {
            Id = string.Empty;
            Name = string.Empty;
            Unit = "V";
            Color = "#6C757D";
            Timestamp = DateTime.Now;
            Status = SignalStatus.Normal;
        }
    }

    // Lo que recibes del servidor
    public class PacketSignal
    {
        public double Timestamp { get; set; }          // timestamp del paquete
        public List<DeviceSignal>? Signals { get; set; }
    }

    public enum SignalStatus
    {
        Normal,
        Warning,
        Critical,
        Offline
    }
}
