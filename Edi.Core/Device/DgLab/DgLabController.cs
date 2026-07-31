namespace Edi.Core.Device.DgLab;

internal sealed class DgLabController : IDgLabController
{
    private readonly IDgLabBluetoothTransport transport;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private int powerA;
    private int powerB;
    private int disposed;

    public DgLabController(IDgLabBluetoothTransport transport)
    {
        this.transport = transport;
        transport.Disconnected += Transport_Disconnected;
    }

    public string Id => transport.Id;
    public string Name => string.IsNullOrWhiteSpace(transport.Name)
        ? "DG-Lab PowerBox 2.0"
        : transport.Name;
    public bool IsConnected =>
        Volatile.Read(ref disposed) == 0 && transport.IsConnected;

    public event Action<IDgLabController> Disconnected = delegate { };

    public async Task SetPower(
        DgLabChannel channel,
        int power,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            power = Math.Clamp(
                power,
                0,
                DgLabChannelConfig.MaximumPower);
            if (channel == DgLabChannel.A)
                powerA = power;
            else
                powerB = power;

            await transport.WritePower(
                DgLabProtocol.EncodePower(powerA, powerB),
                cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task WriteWaveform(
        DgLabChannel channel,
        DgLabWaveform waveform,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await transport.WriteWaveform(
                channel,
                DgLabProtocol.EncodeWaveform(waveform),
                cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task Stop(
        DgLabChannel channel,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref disposed) != 0)
                return;

            if (channel == DgLabChannel.A)
                powerA = 0;
            else
                powerB = 0;

            await transport.WriteWaveform(
                channel,
                DgLabProtocol.EncodeWaveform(DgLabWaveform.Stopped),
                cancellationToken);
            await transport.WritePower(
                DgLabProtocol.EncodePower(powerA, powerB),
                cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        transport.Disconnected -= Transport_Disconnected;
        await transport.DisposeAsync();
        writeLock.Dispose();
    }

    private void Transport_Disconnected()
        => Disconnected(this);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
}
