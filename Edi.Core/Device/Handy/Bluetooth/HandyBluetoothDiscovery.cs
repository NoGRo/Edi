using System.Collections.Concurrent;
using System.Threading.Channels;
using InTheHand.Bluetooth;
using Microsoft.Extensions.Logging;

namespace Edi.Core.Device.Handy;

public interface IHandyBluetoothDiscovery
{
    Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
        TimeSpan timeout,
        int expectedDeviceCount,
        CancellationToken cancellationToken)
        => DiscoverAsync(timeout, cancellationToken);
}

internal sealed class HandyBluetoothDiscovery(
    ILogger<HandyBluetoothDiscovery> logger)
    : IHandyBluetoothDiscovery
{
    private static readonly TimeSpan DiscoveryQuietPeriod =
        TimeSpan.FromMilliseconds(750);

    public async Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => await DiscoverAsync(
            timeout,
            0,
            cancellationToken);

    public async Task<IReadOnlyList<IHandyClient>> DiscoverAsync(
        TimeSpan timeout,
        int expectedDeviceCount,
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
            var newDevices = Channel.CreateUnbounded<bool>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            void OnAdvertisementReceived(
                object sender,
                BluetoothAdvertisingEvent advertisement)
            {
                if (IsHandyAdvertisement(
                    advertisement.Name,
                    advertisement.Uuids.Select(uuid => uuid.Value))
                    && advertisement.Device != null)
                {
                    if (discoveredDevices.TryAdd(
                            advertisement.Device.Id,
                            advertisement.Device))
                    {
                        newDevices.Writer.TryWrite(true);
                    }
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
                    await WaitForDiscoveryWindowAsync(
                        newDevices.Reader,
                        timeout,
                        expectedDeviceCount,
                        cancellationToken);
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

            var clients = await Task.WhenAll(
                discoveredDevices.Values.Select(
                    device => ConnectDeviceAsync(
                        device,
                        cancellationToken)));
            return clients
                .Where(client => client is not null)
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

    private async Task<IHandyClient> ConnectDeviceAsync(
        BluetoothDevice device,
        CancellationToken cancellationToken)
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
            logger.LogInformation(
                "Connected to Handy {DeviceName} over Bluetooth.",
                device.Name);
            return client;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not connect to Handy {DeviceName} over Bluetooth.",
                device.Name);
            device.Gatt.Disconnect();
            return null;
        }
    }

    internal static Task WaitForDiscoveryWindowAsync(
        ChannelReader<bool> newDevices,
        TimeSpan timeout,
        int expectedDeviceCount,
        CancellationToken cancellationToken)
        => WaitForDiscoveryWindowAsync(
            newDevices,
            timeout,
            expectedDeviceCount,
            cancellationToken,
            Task.Delay);

    internal static async Task WaitForDiscoveryWindowAsync(
        ChannelReader<bool> newDevices,
        TimeSpan timeout,
        int expectedDeviceCount,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        using var scanCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var scanToken = scanCancellation.Token;
        var timeoutTask = delay(timeout, scanToken);
        try
        {
            if (expectedDeviceCount <= 0)
            {
                await timeoutTask;
                return;
            }

            Task<bool> nextDeviceTask;
            Task completed;
            for (var discoveredCount = 0;
                 discoveredCount < expectedDeviceCount;
                 discoveredCount++)
            {
                nextDeviceTask =
                    newDevices.ReadAsync(scanToken).AsTask();
                completed = await Task.WhenAny(
                    timeoutTask,
                    nextDeviceTask);
                await completed;
                if (ReferenceEquals(completed, timeoutTask))
                    return;

                await nextDeviceTask;
            }

            while (true)
            {
                var quietPeriodTask =
                    delay(DiscoveryQuietPeriod, scanToken);
                nextDeviceTask =
                    newDevices.ReadAsync(scanToken).AsTask();
                completed = await Task.WhenAny(
                    timeoutTask,
                    quietPeriodTask,
                    nextDeviceTask);
                await completed;
                if (!ReferenceEquals(completed, nextDeviceTask))
                    return;
            }
        }
        finally
        {
            scanCancellation.Cancel();
        }
    }
}
