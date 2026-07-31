namespace Edi.Core.Device.Interfaces;

public interface IDeviceWithOffsetConfiguration
    : IDeviceWithConfiguration
{
    int OffsetMilliseconds { get; }
    Task OffsetUpdate { get; }
}
