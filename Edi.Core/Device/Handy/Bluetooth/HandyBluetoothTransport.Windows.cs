#if WINDOWS
using System.Globalization;
using DiscoveredBluetoothDevice = InTheHand.Bluetooth.BluetoothDevice;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using NativeGattCharacteristic =
    Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic;

namespace Edi.Core.Device.Handy;

internal sealed class HandyBluetoothTransport : IHandyBluetoothTransport
{
    internal static readonly Guid ServiceUuid =
        Guid.Parse("77834d26-40f7-11ee-be56-0242ac120002");
    internal static readonly Guid TxUuid =
        Guid.Parse("77835032-40f7-11ee-be56-0242ac120002");
    internal static readonly Guid RxUuid =
        Guid.Parse("77835410-40f7-11ee-be56-0242ac120002");

    private readonly BluetoothLEDevice _device;
    private readonly GattDeviceService _service;
    private readonly NativeGattCharacteristic _tx;
    private readonly NativeGattCharacteristic _rx;
    private readonly GattSession _session;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _disposed;

    private HandyBluetoothTransport(
        BluetoothLEDevice device,
        GattDeviceService service,
        NativeGattCharacteristic tx,
        NativeGattCharacteristic rx,
        GattSession session)
    {
        _device = device;
        _service = service;
        _tx = tx;
        _rx = rx;
        _session = session;
        _rx.ValueChanged += Rx_ValueChanged;
        _device.ConnectionStatusChanged +=
            Device_ConnectionStatusChanged;
    }

    public string Id => _device.BluetoothAddress.ToString("X12");
    public string Name => _device.Name;

    public int MaxWriteSize =>
        _session.MaxPduSize > 3
            ? _session.MaxPduSize - 3
            : 509;

    public event Action<byte[]> FrameReceived;
    public event Action Disconnected;

    internal static async Task<HandyBluetoothTransport> ConnectAsync(
        DiscoveredBluetoothDevice discoveredDevice,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(
            discoveredDevice.Id,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var bluetoothAddress))
        {
            throw new InvalidOperationException(
                "The Handy Bluetooth address is not valid.");
        }

        var device = await BluetoothLEDevice
            .FromBluetoothAddressAsync(bluetoothAddress)
            .AsTask(cancellationToken)
            ?? throw new InvalidOperationException(
                "Windows could not open the Handy Bluetooth device.");
        try
        {
            var access = await device.RequestAccessAsync()
                .AsTask(cancellationToken);
            if (access != DeviceAccessStatus.Allowed)
            {
                throw new UnauthorizedAccessException(
                    $"Windows denied Handy Bluetooth access ({access}).");
            }

            var session = await GattSession
                .FromDeviceIdAsync(device.BluetoothDeviceId)
                .AsTask(cancellationToken)
                ?? throw new InvalidOperationException(
                    "Windows could not create a Handy GATT session.");
            try
            {
                if (session.CanMaintainConnection)
                    session.MaintainConnection = true;

                var serviceResult = await device
                    .GetGattServicesAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);
                EnsureSuccess(
                    serviceResult.Status,
                    serviceResult.ProtocolError,
                    "enumerate Handy services");
                var service = serviceResult.Services.FirstOrDefault(
                    candidate => candidate.Uuid == ServiceUuid)
                    ?? throw new InvalidOperationException(
                        "The Handy Bluetooth service was not found.");
                try
                {
                    var characteristicResult = await service
                        .GetCharacteristicsAsync(
                            BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken);
                    EnsureSuccess(
                        characteristicResult.Status,
                        characteristicResult.ProtocolError,
                        "enumerate Handy characteristics");
                    var tx = characteristicResult.Characteristics
                        .FirstOrDefault(
                            candidate => candidate.Uuid == TxUuid)
                        ?? throw new InvalidOperationException(
                            "The Handy Bluetooth TX characteristic " +
                            "was not found.");
                    var rx = characteristicResult.Characteristics
                        .FirstOrDefault(
                            candidate => candidate.Uuid == RxUuid)
                        ?? throw new InvalidOperationException(
                            "The Handy Bluetooth RX characteristic " +
                            "was not found.");

                    var transport = new HandyBluetoothTransport(
                        device,
                        service,
                        tx,
                        rx,
                        session);
                    try
                    {
                        var notificationStatus = await rx
                            .WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue
                                    .Notify)
                            .AsTask(cancellationToken);
                        EnsureSuccess(
                            notificationStatus,
                            null,
                            "enable Handy notifications");
                        return transport;
                    }
                    catch
                    {
                        await transport.DisposeAsync();
                        throw;
                    }
                }
                catch
                {
                    service.Dispose();
                    throw;
                }
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public async Task WriteAsync(
        byte[] frame,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        if (frame.Length > MaxWriteSize)
        {
            throw new InvalidOperationException(
                $"The Handy BLE frame is {frame.Length} bytes, " +
                $"but the negotiated payload limit is {MaxWriteSize} bytes.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var writer = new DataWriter();
            writer.WriteBytes(frame);
            var result = await _tx.WriteValueWithResultAsync(
                    writer.DetachBuffer(),
                    GattWriteOption.WriteWithResponse)
                .AsTask(cancellationToken);
            EnsureSuccess(
                result.Status,
                result.ProtocolError,
                "write a Handy Bluetooth frame");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveDisconnect(
            BluetoothLEDevice sender,
            object args)
        {
            if (sender.ConnectionStatus ==
                BluetoothConnectionStatus.Disconnected)
            {
                disconnected.TrySetResult();
            }
        }

        _device.ConnectionStatusChanged += ObserveDisconnect;
        _rx.ValueChanged -= Rx_ValueChanged;
        try
        {
            await _rx
                .WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue
                        .None);
        }
        catch
        {
        }

        if (_session.CanMaintainConnection)
            _session.MaintainConnection = false;
        _service.Dispose();
        _session.Dispose();

        var wasDisconnected = _device.ConnectionStatus ==
            BluetoothConnectionStatus.Disconnected;
        _device.Dispose();
        if (!wasDisconnected)
        {
            try
            {
                await disconnected.Task.WaitAsync(
                    TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                // Windows can release the GATT session without publishing
                // the status transition. The timeout still gives the stack
                // time to release the radio link before a new scan starts.
            }
        }

        _device.ConnectionStatusChanged -= ObserveDisconnect;
        _device.ConnectionStatusChanged -=
            Device_ConnectionStatusChanged;
        _writeLock.Dispose();
    }

    private void Rx_ValueChanged(
        NativeGattCharacteristic sender,
        GattValueChangedEventArgs args)
    {
        using var reader =
            DataReader.FromBuffer(args.CharacteristicValue);
        var frame = new byte[args.CharacteristicValue.Length];
        reader.ReadBytes(frame);
        if (frame.Length > 0)
            FrameReceived?.Invoke(frame);
    }

    private void Device_ConnectionStatusChanged(
        BluetoothLEDevice sender,
        object args)
    {
        if (sender.ConnectionStatus ==
            BluetoothConnectionStatus.Disconnected)
        {
            Disconnected?.Invoke();
        }
    }

    private static void EnsureSuccess(
        GattCommunicationStatus status,
        byte? protocolError,
        string operation)
    {
        if (status == GattCommunicationStatus.Success)
            return;

        var protocolDetail = protocolError.HasValue
            ? $" (protocol error {protocolError.Value})"
            : string.Empty;
        throw new IOException(
            $"Could not {operation}: {status}{protocolDetail}.");
    }
}
#endif
