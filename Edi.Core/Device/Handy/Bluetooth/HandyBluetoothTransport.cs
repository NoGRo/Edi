using InTheHand.Bluetooth;

namespace Edi.Core.Device.Handy;

internal interface IHandyBluetoothTransport : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    int MaxWriteSize { get; }
    event Action<byte[]> FrameReceived;
    event Action Disconnected;

    Task WriteAsync(byte[] frame, CancellationToken cancellationToken);
}

#if !WINDOWS
internal sealed class HandyBluetoothTransport : IHandyBluetoothTransport
{
    internal static readonly Guid ServiceUuid =
        Guid.Parse("77834d26-40f7-11ee-be56-0242ac120002");
    internal static readonly Guid TxUuid =
        Guid.Parse("77835032-40f7-11ee-be56-0242ac120002");
    internal static readonly Guid RxUuid =
        Guid.Parse("77835410-40f7-11ee-be56-0242ac120002");

    private readonly BluetoothDevice _device;
    private readonly RemoteGattServer _server;
    private readonly GattCharacteristic _tx;
    private readonly GattCharacteristic _rx;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _disposed;

    private HandyBluetoothTransport(
        BluetoothDevice device,
        GattCharacteristic tx,
        GattCharacteristic rx)
    {
        _device = device;
        _server = device.Gatt;
        _tx = tx;
        _rx = rx;
        _rx.CharacteristicValueChanged += Rx_CharacteristicValueChanged;
        _device.GattServerDisconnected += Device_GattServerDisconnected;
    }

    public string Id => _device.Id;
    public string Name => _device.Name;

    public int MaxWriteSize =>
        _server.Mtu > 3 ? _server.Mtu - 3 : 509;

    public event Action<byte[]> FrameReceived;
    public event Action Disconnected;

    internal static async Task<HandyBluetoothTransport> ConnectAsync(
        BluetoothDevice device,
        CancellationToken cancellationToken)
    {
        await device.Gatt.ConnectAsync().WaitAsync(cancellationToken);
        try
        {
            try
            {
                await device.Gatt.RequestMtuAsync(512)
                    .WaitAsync(cancellationToken);
            }
            catch
            {
                // MTU negotiation is optional and is not exposed by every OS.
            }

            // Windows can throw ERROR_BAD_COMMAND when querying the
            // Handy's custom UUID directly even though enumerating all GATT
            // attributes returns it successfully.
            var services = await device.Gatt
                .GetPrimaryServicesAsync()
                .WaitAsync(cancellationToken);
            var service = services.FirstOrDefault(
                candidate => candidate.Uuid.Value == ServiceUuid)
                ?? throw new InvalidOperationException(
                    "The Handy Bluetooth service was not found.");
            var characteristics = await service
                .GetCharacteristicsAsync()
                .WaitAsync(cancellationToken);
            var tx = characteristics.FirstOrDefault(
                candidate => candidate.Uuid.Value == TxUuid)
                ?? throw new InvalidOperationException(
                    "The Handy Bluetooth TX characteristic was not found.");
            var rx = characteristics.FirstOrDefault(
                candidate => candidate.Uuid.Value == RxUuid)
                ?? throw new InvalidOperationException(
                    "The Handy Bluetooth RX characteristic was not found.");

            var transport = new HandyBluetoothTransport(device, tx, rx);
            await rx.StartNotificationsAsync().WaitAsync(cancellationToken);
            return transport;
        }
        catch
        {
            device.Gatt.Disconnect();
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
            await _tx.WriteValueWithResponseAsync(frame)
                .WaitAsync(cancellationToken);
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

        _device.GattServerDisconnected -= Device_GattServerDisconnected;
        _rx.CharacteristicValueChanged -= Rx_CharacteristicValueChanged;
        try
        {
            await _rx.StopNotificationsAsync();
        }
        catch
        {
        }

        _server.Disconnect();
        _writeLock.Dispose();
    }

    private void Rx_CharacteristicValueChanged(
        object sender,
        GattCharacteristicValueChangedEventArgs e)
    {
        if (e.Error is null && e.Value is { Length: > 0 })
            FrameReceived?.Invoke(e.Value.ToArray());
    }

    private void Device_GattServerDisconnected(object sender, EventArgs e)
        => Disconnected?.Invoke();
}
#endif
