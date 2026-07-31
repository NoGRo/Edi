using Edi.Core.Services;
using PropertyChanged;

namespace Edi.Core.Device.DgLab;

[AddINotifyPropertyChangedInterface]
[UserConfig]
public sealed class DgLabConfig
{
    public bool Enabled { get; set; } = true;
    public int DiscoverySeconds { get; set; } = 6;
    public int ReconnectSeconds { get; set; } = 30;
}
