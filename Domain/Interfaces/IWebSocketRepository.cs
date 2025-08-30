using CSharp_WPF_Websockets.Domain.Entities;

namespace CSharp_WPF_Websockets.Domain.Interfaces
{
    /// <summary>
    /// Defines the contract for a WebSocket repository that manages device communication.
    /// </summary>
    public interface IWebSocketRepository
    {
        /// <summary>
        /// Occurs when a device signal is received.
        /// </summary>
        event EventHandler<DeviceSignal> SignalReceived;

        /// <summary>
        /// Occurs when the connection status changes.
        /// </summary>
        event EventHandler<ConnectionStatus> ConnectionStatusChanged;

        /// <summary>
        /// Asynchronously connects to the specified WebSocket URI.
        /// </summary>
        /// <param name="uri">The URI to connect to.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>True if the connection was successful; otherwise, false.</returns>
        Task<bool> ConnectAsync(string uri, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously disconnects from the WebSocket.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Asynchronously sends a command to the device via WebSocket.
        /// </summary>
        /// <param name="command">The command to send.</param>
        Task SendCommandAsync(string command);

        /// <summary>
        /// Gets a value indicating whether the WebSocket is currently connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the current connection status of the WebSocket.
        /// </summary>
        ConnectionStatus Status { get; }
    }
}
