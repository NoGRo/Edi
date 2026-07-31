using Edi.Core.Services;
using PropertyChanged;
using System.ComponentModel;

namespace Edi.Core.Device.DgLab;

[AddINotifyPropertyChangedInterface]
[UserConfig]
public sealed class DgLabConfig
{
    public bool Enabled { get; set; } = true;
    public int DiscoverySeconds { get; set; } = 6;
    public int ReconnectSeconds { get; set; } = 30;
}

[GameConfig]
public sealed class DgLabDevicesConfig : INotifyPropertyChanged
{
    private Dictionary<string, DgLabChannelConfig> devices = new();

    public event PropertyChangedEventHandler PropertyChanged;

    public Dictionary<string, DgLabChannelConfig> Devices
    {
        get => devices;
        set
        {
            Unsubscribe(devices.Values);
            devices = value ?? new();
            Subscribe(devices.Values);
            RaiseDevicesChanged();
        }
    }

    public DgLabChannelConfig GetOrAdd(string deviceName)
    {
        if (devices.TryGetValue(deviceName, out var configuration))
            return configuration;

        configuration = new DgLabChannelConfig();
        devices.Add(deviceName, configuration);
        Subscribe(configuration);
        RaiseDevicesChanged();
        return configuration;
    }

    private void Subscribe(IEnumerable<DgLabChannelConfig> configurations)
    {
        foreach (var configuration in configurations)
            Subscribe(configuration);
    }

    private void Subscribe(DgLabChannelConfig configuration)
    {
        if (configuration is not INotifyPropertyChanged changed)
            return;

        changed.PropertyChanged -= ChildConfigurationChanged;
        changed.PropertyChanged += ChildConfigurationChanged;
    }

    private void Unsubscribe(IEnumerable<DgLabChannelConfig> configurations)
    {
        foreach (var configuration in configurations)
        {
            if (configuration is INotifyPropertyChanged changed)
                changed.PropertyChanged -= ChildConfigurationChanged;
        }
    }

    private void ChildConfigurationChanged(
        object sender,
        PropertyChangedEventArgs args)
        => RaiseDevicesChanged();

    private void RaiseDevicesChanged()
        => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(Devices)));
}
