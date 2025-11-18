using System.IO;
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
            var buffer = new byte[8192]; 
            var messageBuffer = new ArraySegment<byte>(buffer);

            using var ms = new MemoryStream();

            try
            {
                while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(messageBuffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        UpdateStatus(ConnectionStatus.Disconnected);
                        break;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage)
                        continue;

                    var completeMessage = Encoding.UTF8.GetString(ms.ToArray());

                    ProcessReceivedMessage(completeMessage);

                    ms.SetLength(0);
                }
            }
            catch (OperationCanceledException)
            {
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
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                double packetTime = 0;
                if (root.TryGetProperty("timestamp", out var ts))
                {
                    packetTime = ts.GetDouble();
                }

                if (!root.TryGetProperty("signals", out var signalsElement) ||
                    signalsElement.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("Mensaje sin array 'signals'");
                    return;
                }

                foreach (var sig in signalsElement.EnumerateArray())
                {
                    var deviceSignal = new DeviceSignal();

                    if (sig.TryGetProperty("id", out var idEl)) deviceSignal.Id = idEl.GetString() ?? "";
                    if (sig.TryGetProperty("name", out var nameEl)) deviceSignal.Name = nameEl.GetString() ?? "";
                    if (sig.TryGetProperty("unit", out var uEl)) deviceSignal.Unit = uEl.GetString() ?? "V";
                    if (sig.TryGetProperty("min", out var minEl)) deviceSignal.MinValue = minEl.GetDouble();
                    if (sig.TryGetProperty("max", out var maxEl)) deviceSignal.MaxValue = maxEl.GetDouble();
                    if (sig.TryGetProperty("color", out var colEl)) deviceSignal.Color = colEl.GetString() ?? "#6C757D";

                    deviceSignal.Samples.Clear();

                    if (sig.TryGetProperty("samples", out var samplesEl) &&
                        samplesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sampleEl in samplesEl.EnumerateArray())
                        {
                            try
                            {
                                var sp = new SamplePoint
                                {
                                    T = sampleEl.GetProperty("t").GetDouble(),
                                    Value = sampleEl.GetProperty("value").GetDouble()
                                };

                                deviceSignal.Samples.Add(sp);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error parsing sample");
                            }
                        }
                    }

                    long ms = (long)(packetTime * 1000.0);
                    deviceSignal.Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

                    SignalReceived?.Invoke(this, deviceSignal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to parse message: {message}");
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
