using Edi.Core.Device.OSR;
using Edi.Core.Funscript.Command;

namespace Edi.Core.Tests.Devices;

public class OsrDeviceConfigTests
{
    [Fact]
    public void NormalizeKeepsEveryAxisOrderedAndWithinPhysicalPercent()
    {
        var configuration = new OsrDeviceConfig
        {
            UpdateRate = 2000,
            RangeLimits = new RangeConfiguration
            {
                Linear = ExtendedRange(80, 200),
                Surge = ExtendedRange(20, 90)
            }
        };

        configuration.Normalize();

        Assert.Equal(1000, configuration.UpdateRate);
        Assert.Equal(80, configuration.RangeLimits.Linear.LowerLimit);
        Assert.Equal(100, configuration.RangeLimits.Linear.UpperLimit);
        Assert.Equal(20, configuration.RangeLimits.Surge.LowerLimit);
        Assert.Equal(90, configuration.RangeLimits.Surge.UpperLimit);
    }

    [Fact]
    public void CloneDoesNotShareAxisRanges()
    {
        var original = new OsrDeviceConfig();
        var clone = original.Clone();

        clone.RangeLimits.Twist.LowerLimit = 25;

        Assert.Equal(0, original.RangeLimits.Twist.LowerLimit);
        Assert.Equal(25, clone.RangeLimits.Twist.LowerLimit);
    }

    [Fact]
    public void FamilyContainerForwardsDeviceConfigurationChanges()
    {
        var configurations = new OsrDevicesConfig();
        var configuration = configurations.GetOrAdd(
            "test-osr",
            () => new OsrDeviceConfig());
        string? changedProperty = null;
        configurations.PropertyChanged +=
            (_, args) => changedProperty = args.PropertyName;

        configuration.UpdateRate = 300;

        Assert.Equal(
            nameof(OsrDevicesConfig.Devices),
            changedProperty);
    }

    private static CmdRange ExtendedRange(int lower, int upper)
    {
        var range = new CmdRange
        {
            UpperLimit = upper,
            LowerLimit = lower
        };
        return range;
    }
}
