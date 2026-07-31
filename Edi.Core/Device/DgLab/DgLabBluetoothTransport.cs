using InTheHand.Bluetooth;

namespace Edi.Core.Device.DgLab;

internal interface IDgLabBluetoothTransport : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    bool IsConnected { get; }
    event Action Disconnected;

    Task WritePower(
        byte[] value,
        CancellationToken cancellationToken);

    Task WriteWaveform(
        DgLabChannel channel,
        byte[] value,
        CancellationToken cancellationToken);
}

internal sealed class DgLabBluetoothTransport
    : IDgLabBluetoothTransport
{
    internal const string AdvertisedName = "D-LAB ESTIM01";
    internal static readonly Guid ServiceUuid =
        Guid.Parse("955a180b-0fe2-f5aa-a094-84b8d4f3e8ad");
    internal static readonly Guid PowerUuid =
        Guid.Parse("955a1504-0fe2-f5aa-a094-84b8d4f3e8ad");
    internal static readonly Guid ChannelAUuid =
        Guid.Parse("955a1505-0fe2-f5aa-a094-84b8d4f3e8ad");
    internal static readonly Guid ChannelBUuid =
        Guid.Parse("955a1506-0fe2-f5aa-a094-84b8d4f3e8ad");

    private readonly BluetoothDevice device;
    private readonly GattCharacteristic power;
    private readonly GattCharacteristic channelA;
    private readonly GattCharacteristic channelB;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private int disposed;

    private DgLabBluetoothTransport(
        BluetoothDevice device,
        GattCharacteristic power,
        GattCharacteristic channelA,
        GattCharacteristic channelB)
    {
        this.device = device;
        this.power = power;
        this.channelA = channelA;
        this.channelB = channelB;
        device.GattServerDisconnected +=
            Device_GattServerDisconnected;
    }

    public string Id => device.Id;
    public string Name => device.Name;
    public bool IsConnected =>
        Volatile.Read(ref disposed) == 0
        && device.Gatt.IsConnected;

    public event Action Disconnected = delegate { };

    internal static async Task<DgLabBluetoothTransport> ConnectAsync(
        BluetoothDevice device,
        CancellationToken cancellationToken)
    {
        await device.Gatt.ConnectAsync().WaitAsync(cancellationToken);
        try
        {
            var services = await device.Gatt
                .GetPrimaryServicesAsync()
                .WaitAsync(cancellationToken);
            var service = services.FirstOrDefault(
                candidate => candidate.Uuid.Value == ServiceUuid)
                ?? throw new InvalidOperationException(
                    "The DG-Lab PowerBox 2.0 service was not found. " +
                    "This unit may use an undocumented hardware revision.");
            var characteristics = await service
                .GetCharacteristicsAsync()
                .WaitAsync(cancellationToken);

            GattCharacteristic Required(Guid uuid, string name)
                => characteristics.FirstOrDefault(
                       candidate => candidate.Uuid.Value == uuid)
                   ?? throw new InvalidOperationException(
                       $"The DG-Lab {name} characteristic was not found.");

            return new DgLabBluetoothTransport(
                device,
                Required(PowerUuid, "power"),
                Required(ChannelAUuid, "channel A waveform"),
                Required(ChannelBUuid, "channel B waveform"));
        }
        catch
        {
            device.Gatt.Disconnect();
            throw;
        }
    }

    public Task WritePower(
        byte[] value,
        CancellationToken cancellationToken)
        => Write(power, value, cancellationToken);

    public Task WriteWaveform(
        DgLabChannel channel,
        byte[] value,
        CancellationToken cancellationToken)
        => Write(
            channel == DgLabChannel.A ? channelA : channelB,
            value,
            cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return ValueTask.CompletedTask;

        device.GattServerDisconnected -=
            Device_GattServerDisconnected;
        device.Gatt.Disconnect();
        writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task Write(
        GattCharacteristic characteristic,
        byte[] value,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        if (value is not { Length: 3 })
        {
            throw new ArgumentException(
                "DG-Lab V2 writes must contain exactly three bytes.",
                nameof(value));
        }

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await characteristic.WriteValueWithResponseAsync(value)
                .WaitAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void Device_GattServerDisconnected(
        object sender,
        EventArgs e)
        => Disconnected();
}
