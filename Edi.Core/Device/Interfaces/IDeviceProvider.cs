namespace Edi.Core.Device.Interfaces
{
    public interface IDeviceProvider
    {
        Task Init(ILoadDevice DeviceLoad);
    }
}