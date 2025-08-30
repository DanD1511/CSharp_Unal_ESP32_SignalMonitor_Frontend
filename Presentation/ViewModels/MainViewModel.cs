using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CSharp_WPF_Websockets.Domain.Entities;
using CSharp_WPF_Websockets.Domain.Interfaces;
using CSharp_WPF_Websockets.Infrastructure.Data;

namespace CSharp_WPF_Websockets.Presentation.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IDeviceService _deviceService;
        private readonly SignalDataStore _signalDataStore;
        private readonly ILogger<MainViewModel> _logger;

        [ObservableProperty]
        private string _deviceIpAddress = "192.168.1.100";

        [ObservableProperty]
        private int _devicePort = 80;

        [ObservableProperty]
        private bool _isConnected = false;

        [ObservableProperty]
        private bool _isConnecting = false;

        [ObservableProperty]
        private string _connectionStatus = "Disconnected";

        [ObservableProperty]
        private string _deviceName = "ESP32 Device";

        [ObservableProperty]
        private DateTime _lastUpdate = DateTime.Now;

        [ObservableProperty]
        private string _connectionStatusColor = "#FF6B6B";

        public ObservableCollection<DeviceSignal> Signals { get; }

        // Comandos como propiedades públicas
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }

        public MainViewModel(
            IDeviceService deviceService,
            SignalDataStore signalDataStore,
            ILogger<MainViewModel> logger)
        {
            _deviceService = deviceService;
            _signalDataStore = signalDataStore;
            _logger = logger;

            Signals = _signalDataStore.Signals;

            // Inicializar comandos
            ConnectCommand = new AsyncRelayCommand(ExecuteConnectAsync, CanConnect);
            DisconnectCommand = new AsyncRelayCommand(ExecuteDisconnectAsync, CanDisconnect);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);


            // Suscribirse a eventos
            _deviceService.SignalUpdated += OnSignalUpdated;
            _deviceService.DeviceStatusChanged += OnDeviceStatusChanged;
        }

        private async Task ExecuteConnectAsync()
        {
            try
            {
                IsConnecting = true;
                ConnectionStatus = "Connecting...";
                ConnectionStatusColor = "#FFA500";

                var connected = await _deviceService.ConnectToDeviceAsync(DeviceIpAddress, DevicePort);

                if (!connected)
                {
                    ConnectionStatus = "Connection Failed";
                    ConnectionStatusColor = "#FF6B6B";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during connection attempt");
                ConnectionStatus = "Connection Error";
                ConnectionStatusColor = "#FF6B6B";
            }
            finally
            {
                IsConnecting = false;
                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
            }
        }

        private async Task ExecuteDisconnectAsync()
        {
            try
            {
                await _deviceService.DisconnectFromDeviceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnection");
            }
        }

        private async Task ExecuteRefreshAsync()
        {
            if (IsConnected)
            {
                try
                {
                    await _deviceService.SendCommandAsync("refresh");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending refresh command");
                }
            }
        }

        private bool CanConnect() => !IsConnecting && !IsConnected;
        private bool CanDisconnect() => IsConnected;

        private void OnSignalUpdated(object sender, DeviceSignal signal)
        {
            LastUpdate = DateTime.Now;
        }

        private void OnDeviceStatusChanged(object sender, MicroControllerDevice device)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                DeviceName = device.Name;
                IsConnected = device.Status == Domain.Entities.ConnectionStatus.Connected;

                ConnectionStatus = device.Status switch
                {
                    Domain.Entities.ConnectionStatus.Connected => "Connected",
                    Domain.Entities.ConnectionStatus.Connecting => "Connecting...",
                    Domain.Entities.ConnectionStatus.Disconnected => "Disconnected",
                    Domain.Entities.ConnectionStatus.Error => "Connection Error",
                    _ => "Unknown"
                };

                ConnectionStatusColor = device.Status switch
                {
                    Domain.Entities.ConnectionStatus.Connected => "#4ECDC4",
                    Domain.Entities.ConnectionStatus.Connecting => "#FFA500",
                    Domain.Entities.ConnectionStatus.Disconnected => "#6C757D",
                    Domain.Entities.ConnectionStatus.Error => "#FF6B6B",
                    _ => "#6C757D"
                };

                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
            });
        }
    }
}