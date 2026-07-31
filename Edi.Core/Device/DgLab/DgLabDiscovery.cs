using System.Collections.Concurrent;
using InTheHand.Bluetooth;
using Microsoft.Extensions.Logging;

namespace Edi.Core.Device.DgLab;

public interface IDgLabDiscovery
{
    Task<IReadOnlyList<IDgLabController>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class DgLabDiscovery(
    ILogger<DgLabDiscovery> logger)
    : IDgLabDiscovery
{
    public async Task<IReadOnlyList<IDgLabController>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await Bluetooth.GetAvailabilityAsync())
                return [];

            var devices =
                new ConcurrentDictionary<string, BluetoothDevice>();
            void OnAdvertisementReceived(
                object sender,
                BluetoothAdvertisingEvent advertisement)
            {
                if (IsPowerBoxAdvertisement(
                    advertisement.Name,
                    advertisement.Uuids.Select(uuid => uuid.Value)))
                {
                    devices.TryAdd(
                        advertisement.Device.Id,
                        advertisement.Device);
                }
            }

            Bluetooth.AdvertisementReceived += OnAdvertisementReceived;
            BluetoothLEScan scan;
            try
            {
                scan = await Bluetooth.RequestLEScanAsync(
                    new BluetoothLEScanOptions
                    {
                        AcceptAllAdvertisements = true,
                        KeepRepeatedDevices = true
                    });
                try
                {
                    await Task.Delay(timeout, cancellationToken);
                }
                finally
                {
                    scan.Stop();
                }
            }
            finally
            {
                Bluetooth.AdvertisementReceived -=
                    OnAdvertisementReceived;
            }

            var controllers = await Task.WhenAll(
                devices.Values.Select(device =>
                    Connect(device, cancellationToken)));
            return controllers
                .Where(controller => controller is not null)
                .ToArray();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "DG-Lab Bluetooth discovery is unavailable.");
            return [];
        }
    }

    internal static bool IsPowerBoxAdvertisement(
        string name,
        IEnumerable<Guid> serviceUuids)
        => string.Equals(
               name,
               DgLabBluetoothTransport.AdvertisedName,
               StringComparison.OrdinalIgnoreCase)
           || serviceUuids.Contains(
               DgLabBluetoothTransport.ServiceUuid);

    private async Task<IDgLabController> Connect(
        BluetoothDevice device,
        CancellationToken cancellationToken)
    {
        try
        {
            var transport =
                await DgLabBluetoothTransport.ConnectAsync(
                    device,
                    cancellationToken);
            logger.LogInformation(
                "Connected to DG-Lab PowerBox 2.0 over Bluetooth.");
            return new DgLabController(transport);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not connect to DG-Lab device {DeviceName}.",
                device.Name);
            device.Gatt.Disconnect();
            return null;
        }
    }
}
