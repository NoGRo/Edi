namespace Edi.Core.Device.DgLab;

public interface IDgLabController : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    bool IsConnected { get; }
    event Action<IDgLabController> Disconnected;

    Task SetPower(
        DgLabChannel channel,
        int power,
        CancellationToken cancellationToken);

    Task WriteWaveform(
        DgLabChannel channel,
        DgLabWaveform waveform,
        CancellationToken cancellationToken);

    Task Stop(
        DgLabChannel channel,
        CancellationToken cancellationToken);
}
