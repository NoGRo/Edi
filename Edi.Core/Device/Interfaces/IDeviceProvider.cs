namespace Edi.Core.Device.Interfaces
{
    public interface IDeviceProvider
    {
        Task Init();
        Task Disconnect() => Task.CompletedTask;
    }
}
