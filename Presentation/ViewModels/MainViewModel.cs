using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CSharp_WPF_Websockets.Domain.Entities;
using CSharp_WPF_Websockets.Domain.Interfaces;
using CSharp_WPF_Websockets.Infrastructure.Data;
using CSharp_WPF_Websockets.Presentation.Views;

namespace CSharp_WPF_Websockets.Presentation.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IDeviceService _deviceService;
        private readonly SignalDataStore _signalDataStore;
        private readonly ILogger<MainViewModel> _logger;

        private readonly Dictionary<string, SignalCardViewModel> _signalViewModels = new();
        private DetailWindow? _detailWindow;

        //[ObservableProperty] private string _deviceIpAddress = "192.168.18.52";

        [ObservableProperty] private string _deviceIpAddress = "localhost";

        [ObservableProperty]
        private int _gridColumns = 3;

        // 2. Agrega este Nuevo Comando para los botones
        [RelayCommand]
        private void SetGridColumns(string columns)
        {
            if (int.TryParse(columns, out int result))
            {
                GridColumns = result;
            }
        }

        [ObservableProperty] private int _devicePort = 8080;

        [ObservableProperty] private bool _isConnected = false;

        [ObservableProperty] private bool _isConnecting = false;

        [ObservableProperty] private string _connectionStatus = "Disconnected";

        [ObservableProperty] private string _deviceName = "ESP32 Device";

        [ObservableProperty] private DateTime _lastUpdate = DateTime.Now;

        [ObservableProperty] private string _connectionStatusColor = "#FF6B6B";

        [ObservableProperty] private ObservableCollection<SignalCardViewModel> _signalCards = new();

        [ObservableProperty] private int _selectedSignalsCount = 0;

        public ObservableCollection<DeviceSignal> Signals => _signalDataStore.Signals;

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IRelayCommand OpenDetailWindowCommand { get; }

        public MainViewModel(
            IDeviceService deviceService,
            SignalDataStore signalDataStore,
            ILogger<MainViewModel> logger)
        {
            _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
            _signalDataStore = signalDataStore ?? throw new ArgumentNullException(nameof(signalDataStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            ConnectCommand = new AsyncRelayCommand(ExecuteConnectAsync, CanConnect);
            DisconnectCommand = new AsyncRelayCommand(ExecuteDisconnectAsync, CanDisconnect);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);
            OpenDetailWindowCommand = new RelayCommand(ExecuteOpenDetailWindow, CanOpenDetailWindow);

            _deviceService.SignalUpdated += OnSignalUpdated;
            _deviceService.DeviceStatusChanged += OnDeviceStatusChanged;

            SignalCards.CollectionChanged += (s, e) => UpdateSelectedCount();
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

        private void ExecuteOpenDetailWindow()
        {
            if (_detailWindow == null || !_detailWindow.IsVisible)
            {
                var selectedSignals = SignalCards.Where(s => s.IsSelected).ToList();

                var detailViewModel = new DetailWindowViewModel(selectedSignals);
                _detailWindow = new DetailWindow
                {
                    DataContext = detailViewModel,
                    Owner = App.Current.MainWindow
                };

                _detailWindow.Closed += (s, e) => _detailWindow = null;
                _detailWindow.Show();
            }
            else
            {
                _detailWindow.Activate();
            }
        }

        private bool CanConnect() => !IsConnecting && !IsConnected;
        private bool CanDisconnect() => IsConnected;
        private bool CanOpenDetailWindow() => SelectedSignalsCount > 0;

        private void OnSignalUpdated(object sender, DeviceSignal signal)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (signal == null || string.IsNullOrWhiteSpace(signal.Id))
                {
                    return;
                }

                var existingSignal = Signals.FirstOrDefault(s => s.Name == signal.Name);
                if (existingSignal != null)
                {
                    var index = Signals.IndexOf(existingSignal);
                    if (index >= 0)
                    {
                        var existing = Signals[index];
                        existing.Value = signal.Value;
                        existing.Timestamp = signal.Timestamp;
                        existing.Unit = signal.Unit;
                        existing.Color = signal.Color;
                        existing.MinValue = signal.MinValue;
                        existing.MaxValue = signal.MaxValue;
                    }
                }
                else
                {
                    Signals.Add(signal);
                }

                if (_signalViewModels.TryGetValue(signal.Name, out var existingViewModel))
                {
                    existingViewModel.UpdateSignal(signal);
                    _logger.LogDebug($"ViewModel actualizado para: {signal.Name}");
                }
                else
                {
                    var newViewModel = new SignalCardViewModel(signal);
                    newViewModel.PropertyChanged += OnSignalViewModelPropertyChanged;
                    _signalViewModels[signal.Name] = newViewModel;
                    SignalCards.Add(newViewModel);
                    _logger.LogDebug($"Nuevo ViewModel creado para: {signal.Name}");
                }

                LastUpdate = DateTime.Now;
            });
        }

        private void OnSignalViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SignalCardViewModel.IsSelected))
            {
                var selectedCount = SignalCards.Count(s => s.IsSelected);

                if (selectedCount > 3 && sender is SignalCardViewModel viewModel)
                {
                    viewModel.IsSelected = false;
                    _logger.LogInformation("Máximo 3 señales pueden ser seleccionadas");
                    return;
                }

                UpdateSelectedCount();
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedSignalsCount = SignalCards.Count(s => s.IsSelected);
            OpenDetailWindowCommand.NotifyCanExecuteChanged();
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

        public void DebugViewModels()
        {
            _logger.LogDebug($"=== DEBUG VIEWMODELS ===");
            _logger.LogDebug($"Total ViewModels: {_signalViewModels.Count}");
            _logger.LogDebug($"SignalCards.Count: {SignalCards.Count}");

            foreach (var kvp in _signalViewModels)
            {
                var vm = kvp.Value;
                _logger.LogDebug($"  {kvp.Key}: {vm.ChartValues.Count} puntos en gráfico");
            }
        }

        public void ClearSignals()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                foreach (var vm in _signalViewModels.Values)
                {
                    vm.PropertyChanged -= OnSignalViewModelPropertyChanged;
                }

                _signalViewModels.Clear();
                SignalCards.Clear();
                Signals.Clear();
                UpdateSelectedCount();
            });
        }
    }
}