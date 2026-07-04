using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy.Transport.BLE
{
    /// <summary>
    /// Extension methods for BLE transport integration.
    /// </summary>
    public static class BleHandyExtensions
    {
        /// <summary>
        /// Checks if an RPC response indicates success.
        /// </summary>
        public static bool IsSuccess(this RpcResponse response)
        {
            return response?.Status == 0;
        }

        /// <summary>
        /// Checks if an RPC response indicates an error.
        /// </summary>
        public static bool IsError(this RpcResponse response)
        {
            return response?.Status != 0;
        }

        /// <summary>
        /// Gets the error message from an RPC response.
        /// </summary>
        public static string? GetErrorMessage(this RpcResponse response)
        {
            return response?.Error ?? (response?.Status != 0 ? $"Error code: {response?.Status}" : null);
        }

        /// <summary>
        /// Converts HandyResponse to a more informative string.
        /// </summary>
        public static string ToDetailedString(this HandyResponse response)
        {
            if (response == null) return "null";
            
            if (response.Success)
                return $"Success: {response.Content}";
            
            return $"Failed: {response.ErrorMessage}";
        }

        /// <summary>
        /// Creates a HandyResponse from an RPC response.
        /// </summary>
        public static HandyResponse ToHandyResponse(this RpcResponse rpcResponse)
        {
            if (rpcResponse == null)
                return new HandyResponse(false, null, "Null RPC response");

            return rpcResponse.Status == 0
                ? new HandyResponse(true, rpcResponse.Result)
                : new HandyResponse(false, null, rpcResponse.Error ?? $"Error code: {rpcResponse.Status}");
        }
    }

    /// <summary>
    /// Helper for managing BLE device reconnection and health checks.
    /// </summary>
    public sealed class BleHandyHealthMonitor
    {
        private readonly IHandyTransport _transport;
        private readonly BleHandyOptions _options;
        private bool _isMonitoring = false;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;

        public event EventHandler<BleHealthEventArgs>? HealthStatusChanged;

        public BleHandyHealthMonitor(IHandyTransport transport, BleHandyOptions? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new BleHandyOptions();
        }

        /// <summary>
        /// Starts health monitoring (periodic connectivity checks).
        /// </summary>
        public void StartMonitoring(TimeSpan? interval = null)
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;
            _monitoringCts = new CancellationTokenSource();
            var checkInterval = interval ?? TimeSpan.FromSeconds(30);

            _monitoringTask = MonitoringLoop(checkInterval, _monitoringCts.Token);
        }

        /// <summary>
        /// Stops health monitoring.
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;
            _monitoringCts?.Cancel();

            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
        }

        private async Task MonitoringLoop(TimeSpan interval, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

                    var isConnected = _transport.IsConnected;
                    var status = isConnected ? BleHealthStatus.Connected : BleHealthStatus.Disconnected;

                    HealthStatusChanged?.Invoke(this, new BleHealthEventArgs { Status = status, Timestamp = DateTime.UtcNow });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    HealthStatusChanged?.Invoke(this, new BleHealthEventArgs 
                    { 
                        Status = BleHealthStatus.Error, 
                        Timestamp = DateTime.UtcNow 
                    });
                }
            }
        }
    }

    /// <summary>
    /// Health check status.
    /// </summary>
    public enum BleHealthStatus
    {
        Unknown = 0,
        Connected = 1,
        Disconnected = 2,
        Error = 3,
        Reconnecting = 4
    }

    /// <summary>
    /// Event arguments for health status changes.
    /// </summary>
    public sealed class BleHealthEventArgs : EventArgs
    {
        public BleHealthStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Configuration for BLE device discovery logging.
    /// </summary>
    public sealed class BleDiscoveryLogger
    {
        private readonly List<string> _discoveredDevices = new();
        private readonly List<Guid> _discoveredServices = new();
        private readonly List<Guid> _discoveredCharacteristics = new();

        public IReadOnlyList<string> DiscoveredDevices => _discoveredDevices.AsReadOnly();
        public IReadOnlyList<Guid> DiscoveredServices => _discoveredServices.AsReadOnly();
        public IReadOnlyList<Guid> DiscoveredCharacteristics => _discoveredCharacteristics.AsReadOnly();

        public void LogDevice(string deviceName, string? address = null)
        {
            var entry = address != null ? $"{deviceName} ({address})" : deviceName;
            if (!_discoveredDevices.Contains(entry))
            {
                _discoveredDevices.Add(entry);
            }
        }

        public void LogService(Guid serviceUuid)
        {
            if (!_discoveredServices.Contains(serviceUuid))
            {
                _discoveredServices.Add(serviceUuid);
            }
        }

        public void LogCharacteristic(Guid characteristicUuid)
        {
            if (!_discoveredCharacteristics.Contains(characteristicUuid))
            {
                _discoveredCharacteristics.Add(characteristicUuid);
            }
        }

        public void Clear()
        {
            _discoveredDevices.Clear();
            _discoveredServices.Clear();
            _discoveredCharacteristics.Clear();
        }

        public string GetSummary()
        {
            return $"Discovered: {_discoveredDevices.Count} devices, " +
                   $"{_discoveredServices.Count} services, " +
                   $"{_discoveredCharacteristics.Count} characteristics";
        }
    }
}
