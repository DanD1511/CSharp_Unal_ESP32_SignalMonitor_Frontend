using CSharp_WPF_Websockets.Domain.Entities;

namespace CSharp_WPF_Websockets.Domain.Interfaces
{
    public interface IDeviceService
    {
        event EventHandler<DeviceSignal> SignalUpdated;
        event EventHandler<MicroControllerDevice> DeviceStatusChanged;

        Task<bool> ConnectToDeviceAsync(string ipAddress, int port = 80);
        Task DisconnectFromDeviceAsync();
        Task SendCommandAsync(string command);

        IEnumerable<DeviceSignal> GetCurrentSignals();
        MicroControllerDevice GetCurrentDevice();
        bool IsConnected { get; }
    }
}
