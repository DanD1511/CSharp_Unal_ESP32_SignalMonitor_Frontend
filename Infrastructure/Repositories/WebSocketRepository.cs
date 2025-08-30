using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CSharp_WPF_Websockets.Domain.Entities;
using CSharp_WPF_Websockets.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CSharp_WPF_Websockets.Infrastructure.Repositories
{
    public class WebSocketRepository : IWebSocketRepository, IDisposable
    {
        private ClientWebSocket _webSocket;
        private readonly ILogger<WebSocketRepository> _logger;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;

        public event EventHandler<DeviceSignal> SignalReceived;
        public event EventHandler<ConnectionStatus> ConnectionStatusChanged;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        public WebSocketRepository(ILogger<WebSocketRepository> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConnectAsync(string uri, CancellationToken cancellationToken = default)
        {
            try
            {
                UpdateStatus(ConnectionStatus.Connecting);

                _webSocket?.Dispose();
                _cancellationTokenSource?.Cancel();

                _webSocket = new ClientWebSocket();
                _cancellationTokenSource = new CancellationTokenSource();

                var fullUri = new Uri($"ws://{uri}/ws");
                await _webSocket.ConnectAsync(fullUri, cancellationToken);

                UpdateStatus(ConnectionStatus.Connected);

                _receiveTask = ReceiveLoop(_cancellationTokenSource.Token);

                _logger.LogInformation($"Connected to WebSocket: {fullUri}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to connect to WebSocket: {uri}");
                UpdateStatus(ConnectionStatus.Error);
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                UpdateStatus(ConnectionStatus.Disconnected);

                _cancellationTokenSource?.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }

                _receiveTask?.Wait(5000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnect");
            }
        }

        public async Task SendCommandAsync(string command)
        {
            if (!IsConnected) return;

            try
            {
                var buffer = Encoding.UTF8.GetBytes(command);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);

                _logger.LogDebug($"Sent command: {command}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send command: {command}");
            }
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessReceivedMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        UpdateStatus(ConnectionStatus.Disconnected);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, "WebSocket error in receive loop");
                UpdateStatus(ConnectionStatus.Error);
            }
        }

        private void ProcessReceivedMessage(string message)
        {
            try
            {
                var signal = JsonSerializer.Deserialize<DeviceSignal>(message, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (signal != null)
                {
                    signal.Timestamp = DateTime.Now;
                    SignalReceived?.Invoke(this, signal);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, $"Failed to deserialize message: {message}");
            }
        }

        private void UpdateStatus(ConnectionStatus newStatus)
        {
            if (Status != newStatus)
            {
                Status = newStatus;
                ConnectionStatusChanged?.Invoke(this, newStatus);
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _webSocket?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

    }
}
