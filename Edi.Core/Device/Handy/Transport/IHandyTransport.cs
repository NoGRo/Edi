using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy.Transport
{
    /// <summary>
    /// Interface for abstracting different transport protocols for Handy communication.
    /// This allows switching between HTTP, Bluetooth, WebSocket, etc. without changing device logic.
    /// </summary>
    public interface IHandyTransport
    {
        /// <summary>
        /// Gets whether the transport is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Initializes the transport connection.
        /// </summary>
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnects the transport.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PUT request to the specified endpoint with the given payload.
        /// </summary>
        Task<HandyResponse> PutAsync(string endpoint, string payload, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a GET request to the specified endpoint.
        /// </summary>
        Task<HandyResponse> GetAsync(string endpoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the detected firmware version or null if not available.
        /// </summary>
        string? FirmwareVersion { get; }

        /// <summary>
        /// Sets the device mode.
        /// </summary>
        Task<bool> SetModeAsync(int mode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the HSP offset.
        /// </summary>
        Task<bool> SetHspOffsetAsync(int offset, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a response from a Handy transport operation.
    /// </summary>
    public class HandyResponse
    {
        public HandyResponse(bool success, string? content = null, string? errorMessage = null)
        {
            Success = success;
            Content = content;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public string? Content { get; }
        public string? ErrorMessage { get; }
    }
}
