namespace Edi.Core.Device.Handy.Transport.BLE
{
    /// <summary>
    /// Known Handy BLE service and characteristic UUIDs.
    /// Based on Handy public protocol documentation.
    /// </summary>
    public static class HandyBleUuids
    {
        /// <summary>
        /// Handy BLE service UUID (primary service).
        /// This UUID should be confirmed from official Handy documentation or device discovery.
        /// </summary>
        public static readonly Guid ServiceUuid = new Guid("0000180A-0000-1000-8000-00805F9B34FB"); // Device Information Service (standard)
        // TODO: Confirm actual Handy service UUID from official documentation

        /// <summary>
        /// RPC write characteristic UUID.
        /// Data is written to this characteristic to send RPC commands.
        /// </summary>
        public static readonly Guid RpcWriteCharacteristicUuid = new Guid("00002A37-0000-1000-8000-00805F9B34FB"); // Heart Rate Measurement (placeholder)
        // TODO: Confirm actual RPC write characteristic UUID

        /// <summary>
        /// RPC notify characteristic UUID.
        /// Notifications from this characteristic contain RPC responses.
        /// </summary>
        public static readonly Guid RpcNotifyCharacteristicUuid = new Guid("00002A38-0000-1000-8000-00805F9B34FB"); // Body Sensor Location (placeholder)
        // TODO: Confirm actual RPC notify characteristic UUID

        /// <summary>
        /// Attempts to get service UUID from options, falling back to default.
        /// </summary>
        public static Guid GetServiceUuid(BleHandyOptions? options) => options?.ServiceUuid ?? ServiceUuid;

        /// <summary>
        /// Attempts to get write characteristic UUID from options, falling back to default.
        /// </summary>
        public static Guid GetWriteCharacteristicUuid(BleHandyOptions? options) => options?.WriteCharacteristicUuid ?? RpcWriteCharacteristicUuid;

        /// <summary>
        /// Attempts to get notify characteristic UUID from options, falling back to default.
        /// </summary>
        public static Guid GetNotifyCharacteristicUuid(BleHandyOptions? options) => options?.NotifyCharacteristicUuid ?? RpcNotifyCharacteristicUuid;
    }
}
