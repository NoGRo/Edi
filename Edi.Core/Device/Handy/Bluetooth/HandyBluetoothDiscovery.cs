using System.Collections.Concurrent;
using InTheHand.Bluetooth;
using Microsoft.Extensions.Logging;

namespace Edi.Core.Device.Handy;

public interface IHandyBluetoothDiscovery
{
    Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class HandyBluetoothDiscovery(
    ILogger<HandyBluetoothDiscovery> logger)
    : IHandyBluetoothDiscovery
{
    public async Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await Bluetooth.GetAvailabilityAsync())
            {
                logger.LogDebug(
                    "Bluetooth is unavailable; skipping local Handy discovery.");
                return [];
            }

            var discoveredDevices =
                new ConcurrentDictionary<string, BluetoothDevice>();
            void OnAdvertisementReceived(
                object sender,
                BluetoothAdvertisingEvent advertisement)
            {
                if (IsHandyAdvertisement(
                    advertisement.Name,
                    advertisement.Uuids.Select(uuid => uuid.Value)))
                {
                    discoveredDevices[advertisement.Device.Id] =
                        advertisement.Device;
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

            var clients = new List<IHandyClient>();
            foreach (var device in discoveredDevices.Values)
            {
                try
                {
                    var transport =
                        await HandyBluetoothTransport.ConnectAsync(
                            device,
                            cancellationToken);
                    var client = await HandyBluetoothClient.CreateAsync(
                        transport,
                        logger,
                        initialize: true,
                        cancellationToken);
                    clients.Add(client);
                    logger.LogInformation(
                        "Connected to Handy {DeviceName} over Bluetooth.",
                        device.Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Could not connect to Handy {DeviceName} over Bluetooth.",
                        device.Name);
                    device.Gatt.Disconnect();
                }
            }

            return clients;
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
                "Bluetooth Handy discovery is unavailable.");
            return [];
        }
    }

    internal static bool IsHandyAdvertisement(
        string name,
        IEnumerable<Guid> serviceUuids)
        => name?.StartsWith(
            "OHD",
            StringComparison.OrdinalIgnoreCase) == true ||
           serviceUuids.Contains(HandyBluetoothTransport.ServiceUuid);
}
