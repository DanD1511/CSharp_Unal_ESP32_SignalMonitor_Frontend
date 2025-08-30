using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Xml.Linq;

namespace CSharp_WPF_Websockets.Domain.Entities
{
    /// <summary>
    /// Represents a microcontroller device with connection information and associated signals.
    /// </summary>
    public partial class MicroControllerDevice : ObservableObject
    {
        [ObservableProperty]
        private string _id;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _ipAddress;

        [ObservableProperty]
        private int _port;

        [ObservableProperty]
        private ConnectionStatus _status;

        [ObservableProperty]
        private DateTime _lastSeen;

        public List<DeviceSignal> Signals { get; set; } = new();

        public MicroControllerDevice()
        {
            Id = string.Empty;
            Name = string.Empty;
            IpAddress = string.Empty;
            Port = 80;
            Status = ConnectionStatus.Disconnected;
            LastSeen = DateTime.Now;
        }
    }

    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }
}