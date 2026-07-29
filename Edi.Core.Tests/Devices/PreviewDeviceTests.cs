using Edi.Core.Device.Interfaces;
using Edi.Core.Device.Simulator;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;

namespace Edi.Core.Tests.Devices;

public class PreviewDeviceTests
{
    [Fact]
    public async Task RefreshRepositoryNotifiesTheVariantsBinding()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var device = new PreviewDevice(
            rig.Funscripts,
            rig.Definitions,
            NullLogger<PreviewDevice>.Instance);
        var notifications = new List<string?>();
        ((INotifyPropertyChanged)device).PropertyChanged +=
            (_, args) => notifications.Add(args.PropertyName);

        device.RefreshRepository();

        Assert.Contains(nameof(IDevice.Variants), notifications);
    }

    [Theory]
    [InlineData(0, 0, 50, 0)]
    [InlineData(25, 0, 50, 12)]
    [InlineData(50, 0, 50, 25)]
    [InlineData(100, 0, 50, 50)]
    [InlineData(0, 20, 80, 20)]
    [InlineData(50, 20, 80, 50)]
    [InlineData(100, 20, 80, 80)]
    [InlineData(-10, 20, 80, 20)]
    [InlineData(110, 20, 80, 80)]
    public void ScalePositionMapsTheWholeMovementIntoTheConfiguredRange(
        double position,
        int min,
        int max,
        int expected)
    {
        var result = SimulatorDevice.ScalePosition(position, min, max);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(1, "00:00:00.001")]
    [InlineData(61_234, "00:01:01.234")]
    [InlineData(3_661_007, "01:01:01.007")]
    [InlineData(97_200_005, "27:00:00.005")]
    [InlineData(-1, "00:00:00.000")]
    public void FormatTimeUsesHoursMinutesSecondsAndMilliseconds(
        long milliseconds,
        string expected)
    {
        var result = SimulatorDevice.FormatTime(milliseconds);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RecorderDevicesAreHiddenFromTheMainDeviceGrid()
    {
        Assert.True(
            typeof(IHiddenDevice).IsAssignableFrom(typeof(RecorderDevice)));
        Assert.True(
            typeof(SimulatorDevice).IsAssignableFrom(typeof(RecorderDevice)));
    }

}
