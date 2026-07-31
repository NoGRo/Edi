using PropertyChanged;
using System.ComponentModel;

namespace Edi.Core.Device.DgLab;

[AddINotifyPropertyChangedInterface]
public sealed class DgLabChannelConfig : INotifyPropertyChanged
{
#pragma warning disable CS0067
    public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

    public const int MaximumPower = 2047;
    public const int MaximumSafePulseWidthMicroseconds = 100;

    // Zero by default is intentional: e-stim output must be calibrated
    // explicitly for the electrode and placement used by this game/session.
    public int PowerMin { get; set; }
    public int PowerMax { get; set; }
    public int FrequencyMinHz { get; set; } = 1;
    public int FrequencyMaxHz { get; set; } = 100;
    public int PulseWidthMicroseconds { get; set; } = 100;
    public int SpeedForMaximumFrequency { get; set; } = 400;
    public int DefaultVolumePercent { get; set; } = 100;

    public DgLabChannelConfig Clone()
        => new()
        {
            PowerMin = PowerMin,
            PowerMax = PowerMax,
            FrequencyMinHz = FrequencyMinHz,
            FrequencyMaxHz = FrequencyMaxHz,
            PulseWidthMicroseconds = PulseWidthMicroseconds,
            SpeedForMaximumFrequency = SpeedForMaximumFrequency,
            DefaultVolumePercent = DefaultVolumePercent
        };

    public void CopyFrom(DgLabChannelConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PowerMin = source.PowerMin;
        PowerMax = source.PowerMax;
        FrequencyMinHz = source.FrequencyMinHz;
        FrequencyMaxHz = source.FrequencyMaxHz;
        PulseWidthMicroseconds = source.PulseWidthMicroseconds;
        SpeedForMaximumFrequency = source.SpeedForMaximumFrequency;
        DefaultVolumePercent = source.DefaultVolumePercent;
    }

    public void Normalize()
    {
        PowerMin = Math.Clamp(PowerMin, 0, MaximumPower);
        PowerMax = Math.Clamp(PowerMax, PowerMin, MaximumPower);
        FrequencyMinHz = Math.Clamp(FrequencyMinHz, 1, 100);
        FrequencyMaxHz = Math.Clamp(FrequencyMaxHz, FrequencyMinHz, 100);
        PulseWidthMicroseconds = Math.Clamp(
            PulseWidthMicroseconds,
            0,
            MaximumSafePulseWidthMicroseconds);
        SpeedForMaximumFrequency = Math.Max(1, SpeedForMaximumFrequency);
        DefaultVolumePercent = Math.Clamp(DefaultVolumePercent, 0, 100);
    }
}
