namespace Edi.Core.Device.Interfaces;

public interface IDeviceWithConfiguration
{
    void ApplyConfiguration(DeviceConfig configuration);
    void RemoveConfiguration() { }
}
