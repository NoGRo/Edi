using System;
using System.Threading;

namespace Edi.Core.Device.Handy.Transport.BLE
{
    /// <summary>
    /// Configuration options for BLE Handy transport.
    /// </summary>
    public sealed class BleHandyOptions
    {
        /// <summary>
        /// Device name or identifier to search for. Default is "The Handy".
        /// </summary>
        public string? DeviceName { get; set; } = "The Handy";

        /// <summary>
        /// BLE service UUID. If null, will be discovered automatically.
        /// </summary>
        public Guid? ServiceUuid { get; set; }

        /// <summary>
        /// BLE write characteristic UUID. If null, will be discovered automatically.
        /// </summary>
        public Guid? WriteCharacteristicUuid { get; set; }

        /// <summary>
        /// BLE notify characteristic UUID. If null, will be discovered automatically.
        /// </summary>
        public Guid? NotifyCharacteristicUuid { get; set; }

        /// <summary>
        /// Timeout for RPC requests. Default is 5 seconds.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Scan timeout for BLE device discovery. Default is 10 seconds.
        /// </summary>
        public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Connection timeout. Default is 10 seconds.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Enable detailed BLE discovery logging for debugging.
        /// </summary>
        public bool DebugLogging { get; set; } = false;
    }
}
