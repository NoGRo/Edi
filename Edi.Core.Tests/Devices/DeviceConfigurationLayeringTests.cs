using Edi.Core.Device;

namespace Edi.Core.Tests.Devices;

public class DeviceConfigurationLayeringTests
{
    [Fact]
    public void SharedDeviceConfigurationContainsNoFamilyTypes()
    {
        var familyProperties = typeof(DeviceConfig)
            .GetProperties()
            .Where(property =>
                property.PropertyType.Namespace?.StartsWith(
                    "Edi.Core.Device.DgLab",
                    StringComparison.Ordinal) == true
                || property.PropertyType.Namespace?.StartsWith(
                    "Edi.Core.Device.OSR",
                    StringComparison.Ordinal) == true)
            .Select(property => property.Name);

        Assert.Empty(familyProperties);
    }
}
