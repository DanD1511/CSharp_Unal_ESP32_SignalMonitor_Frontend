using CSharp_WPF_Websockets.Domain.Entities;
using CSharp_WPF_Websockets.Domain.Interfaces;
using CSharp_WPF_Websockets.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace CSharp_WPF_Websockets.Application.Services
{
    public class DeviceService : IDeviceService, IDisposable
    {
        private readonly IWebSocketRepository _webSocketRepository;
        private readonly SignalDataStore _signalDataStore;
        private readonly ILogger<DeviceService> _logger;
        private MicroControllerDevice _currentDevice;

        public event EventHandler<DeviceSignal> SignalUpdated;
        public event EventHandler<MicroControllerDevice> DeviceStatusChanged;

        public bool IsConnected => _webSocketRepository.IsConnected;

        public DeviceService(
            IWebSocketRepository webSocketRepository,
            SignalDataStore signalDataStore,
            ILogger<DeviceService> logger)
        {
            _webSocketRepository = webSocketRepository;
            _signalDataStore = signalDataStore;
            _logger = logger;

            _webSocketRepository.SignalReceived += OnSignalReceived;
            _webSocketRepository.ConnectionStatusChanged += OnConnectionStatusChanged;
        }

        public async Task<bool> ConnectToDeviceAsync(string ipAddress, int port = 80)
        {
            try
            {
                _currentDevice = new MicroControllerDevice
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"ESP32-{ipAddress}",
                    IpAddress = ipAddress,
                    Port = port,
                    Status = ConnectionStatus.Connecting
                };

                DeviceStatusChanged?.Invoke(this, _currentDevice);

                var connectionString = $"{ipAddress}:{port}";
                var connected = await _webSocketRepository.ConnectAsync(connectionString);

                if (connected)
                {
                    _currentDevice.Status = ConnectionStatus.Connected;
                    _currentDevice.LastSeen = DateTime.Now;

                    // Initialize default signals
                    InitializeDefaultSignals();

                    _logger.LogInformation($"Successfully connected to device: {connectionString}");
                }
                else
                {
                    _currentDevice.Status = ConnectionStatus.Error;
                    _logger.LogWarning($"Failed to connect to device: {connectionString}");
                }

                DeviceStatusChanged?.Invoke(this, _currentDevice);
                return connected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error connecting to device: {ipAddress}:{port}");
                if (_currentDevice != null)
                {
                    _currentDevice.Status = ConnectionStatus.Error;
                    DeviceStatusChanged?.Invoke(this, _currentDevice);
                }
                return false;
            }
        }

        public async Task DisconnectFromDeviceAsync()
        {
            try
            {
                await _webSocketRepository.DisconnectAsync();

                if (_currentDevice != null)
                {
                    _currentDevice.Status = ConnectionStatus.Disconnected;
                    DeviceStatusChanged?.Invoke(this, _currentDevice);
                }

                _signalDataStore.Clear();
                _logger.LogInformation("Disconnected from device");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from device");
            }
        }

        public async Task SendCommandAsync(string command)
        {
            try
            {
                await _webSocketRepository.SendCommandAsync(command);
                _logger.LogDebug($"Command sent: {command}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send command: {command}");
            }
        }

        public IEnumerable<DeviceSignal> GetCurrentSignals()
        {
            return _signalDataStore.GetAllSignals();
        }

        public MicroControllerDevice GetCurrentDevice()
        {
            return _currentDevice;
        }

        private void OnSignalReceived(object sender, DeviceSignal signal)
        {
            try
            {
                // Update signal status based on value ranges
                UpdateSignalStatus(signal);

                _signalDataStore.UpdateSignal(signal);
                SignalUpdated?.Invoke(this, signal);

                if (_currentDevice != null)
                {
                    _currentDevice.LastSeen = DateTime.Now;
                }

                _logger.LogDebug($"Signal updated: {signal.Name} = {signal.Value} {signal.Unit}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing received signal: {signal?.Name}");
            }
        }

        private void OnConnectionStatusChanged(object sender, ConnectionStatus status)
        {
            if (_currentDevice != null)
            {
                _currentDevice.Status = status;
                DeviceStatusChanged?.Invoke(this, _currentDevice);
            }
        }

        private void UpdateSignalStatus(DeviceSignal signal)
        {
            if (signal.MaxValue > signal.MinValue)
            {
                var range = signal.MaxValue - signal.MinValue;
                var normalizedValue = (signal.Value - signal.MinValue) / range;

                signal.Status = normalizedValue switch
                {
                    > 0.9 or < 0.1 => SignalStatus.Critical,
                    > 0.8 or < 0.2 => SignalStatus.Warning,
                    _ => SignalStatus.Normal
                };
            }
            else
            {
                signal.Status = SignalStatus.Normal;
            }
        }

        private void InitializeDefaultSignals()
        {
            var defaultSignals = new[]
            {
                new DeviceSignal
                {
                    Id = "temp", Name = "Temperature", Type = "Temperature",
                    Unit = "°C", MinValue = -10, MaxValue = 50, Color = "#FF6B35"
                },
                new DeviceSignal
                {
                    Id = "hum", Name = "Humidity", Type = "Humidity",
                    Unit = "%", MinValue = 0, MaxValue = 100, Color = "#4ECDC4"
                },
                new DeviceSignal
                {
                    Id = "press", Name = "Pressure", Type = "Pressure",
                    Unit = "hPa", MinValue = 900, MaxValue = 1100, Color = "#45B7D1"
                },
                new DeviceSignal
                {
                    Id = "light", Name = "Light Level", Type = "Analog",
                    Unit = "lux", MinValue = 0, MaxValue = 1000, Color = "#FFA07A"
                },
                new DeviceSignal
                {
                    Id = "motion", Name = "Motion Sensor", Type = "Digital",
                    Unit = "", MinValue = 0, MaxValue = 1, Color = "#98D8C8"
                }
            };

            foreach (var signal in defaultSignals)
            {
                signal.Value = 0;
                signal.Status = SignalStatus.Offline;
                signal.Timestamp = DateTime.Now;
                _signalDataStore.UpdateSignal(signal);
            }
        }

        public void Dispose()
        {
            _webSocketRepository?.DisconnectAsync();
        }
    }
}
