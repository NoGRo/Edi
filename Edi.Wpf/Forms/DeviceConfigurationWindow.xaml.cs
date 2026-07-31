using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Edi.Core.Device;
using Edi.Core.Device.DgLab;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Funscript.Command;

namespace Edi.Forms;

public partial class DeviceConfigurationWindow : Window
{
    public DeviceConfigurationWindow(IDevice device)
    {
        InitializeComponent();
        ViewModel = new DeviceConfigurationViewModel(device);
        DataContext = ViewModel;
        Title = $"Device configuration - {device.Name}";
    }

    public DeviceConfigurationViewModel ViewModel { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Normalize();
        DialogResult = true;
    }
}

public sealed class DeviceConfigurationViewModel
    : INotifyPropertyChanged
{
    private int rangeMin;
    private int rangeMax;
    private int powerMin;
    private int powerMax;
    private int frequencyMinHz = 1;
    private int frequencyMaxHz = 100;
    private int offsetMilliseconds;
    private int pulseWidthMicroseconds;
    private int defaultVolumePercent;

    public DeviceConfigurationViewModel(IDevice device)
    {
        DeviceName = device.Name;
        if (device is IRange range)
        {
            rangeMin = Math.Clamp(range.Min, 0, 100);
            rangeMax = Math.Clamp(range.Max, rangeMin, 100);
        }
        else
        {
            rangeMax = 100;
        }

        if (device is DgLabDevice dgLabDevice)
        {
            HasDgLab = true;
            var configuration = dgLabDevice.DgLabConfiguration.Clone();
            configuration.Normalize();
            powerMin = configuration.PowerMin;
            powerMax = configuration.PowerMax;
            FrequencyMinHz = configuration.FrequencyMinHz;
            FrequencyMaxHz = configuration.FrequencyMaxHz;
            PulseWidthMicroseconds =
                configuration.PulseWidthMicroseconds;
            SpeedForMaximumFrequency =
                configuration.SpeedForMaximumFrequency;
            DefaultVolumePercent =
                configuration.DefaultVolumePercent;
        }

        if (device is OSRDevice osrDevice)
        {
            HasOsr = true;
            var configuration = osrDevice.OsrConfiguration.Clone();
            configuration.Normalize();
            OsrEnableMultiAxis = configuration.EnableMultiAxis;
            OsrUpdateRate = configuration.UpdateRate;
            OsrAxes =
            [
                AxisRangeViewModel.From(
                    "Linear",
                    "Normal FunScript / TCode L0 stroke axis.",
                    configuration.RangeLimits.Linear),
                AxisRangeViewModel.From(
                    "Surge",
                    ".surge.funscript / TCode L1 forward-back axis.",
                    configuration.RangeLimits.Surge),
                AxisRangeViewModel.From(
                    "Sway",
                    ".sway.funscript / TCode L2 side-to-side axis.",
                    configuration.RangeLimits.Sway),
                AxisRangeViewModel.From(
                    "Twist",
                    ".twist.funscript / TCode R0 rotation axis.",
                    configuration.RangeLimits.Twist),
                AxisRangeViewModel.From(
                    "Roll",
                    ".roll.funscript / TCode R1 roll axis.",
                    configuration.RangeLimits.Roll),
                AxisRangeViewModel.From(
                    "Pitch",
                    ".pitch.funscript / TCode R2 pitch axis.",
                    configuration.RangeLimits.Pitch)
            ];
        }

        if (device is IDeviceWithOffsetConfiguration offsetDevice)
        {
            HasOffset = true;
            offsetMilliseconds = offsetDevice.OffsetMilliseconds;
        }
    }

    public string DeviceName { get; }
    public bool HasDgLab { get; }
    public bool HasOsr { get; }
    public bool HasOffset { get; }
    public int OffsetMilliseconds
    {
        get => offsetMilliseconds;
        set
        {
            offsetMilliseconds = DeviceOffset.Normalize(value);
            OnPropertyChanged();
        }
    }
    public bool OsrEnableMultiAxis { get; set; }
    public int OsrUpdateRate { get; set; } = 200;
    public IReadOnlyList<AxisRangeViewModel> OsrAxes { get; private set; } = [];

    public int RangeMin
    {
        get => rangeMin;
        set
        {
            rangeMin = Math.Clamp(value, 0, RangeMax);
            OnPropertyChanged();
        }
    }

    public int RangeMax
    {
        get => rangeMax;
        set
        {
            rangeMax = Math.Clamp(value, RangeMin, 100);
            OnPropertyChanged();
        }
    }

    public int PowerMin
    {
        get => powerMin;
        set
        {
            powerMin = Math.Clamp(value, 0, PowerMax);
            OnPropertyChanged();
        }
    }

    public int PowerMax
    {
        get => powerMax;
        set
        {
            powerMax = Math.Clamp(
                value,
                PowerMin,
                DgLabChannelConfig.MaximumPower);
            OnPropertyChanged();
        }
    }

    public int FrequencyMinHz
    {
        get => frequencyMinHz;
        set
        {
            frequencyMinHz = Math.Clamp(value, 1, FrequencyMaxHz);
            OnPropertyChanged();
        }
    }

    public int FrequencyMaxHz
    {
        get => frequencyMaxHz;
        set
        {
            frequencyMaxHz = Math.Clamp(value, FrequencyMinHz, 100);
            OnPropertyChanged();
        }
    }
    public int PulseWidthMicroseconds
    {
        get => pulseWidthMicroseconds;
        set
        {
            pulseWidthMicroseconds = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
        }
    }
    public int SpeedForMaximumFrequency { get; set; }
    public int DefaultVolumePercent
    {
        get => defaultVolumePercent;
        set
        {
            defaultVolumePercent = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
        }
    }

    public DgLabChannelConfig CreateDgLabConfiguration()
    {
        var configuration = new DgLabChannelConfig
        {
            PowerMin = PowerMin,
            PowerMax = PowerMax,
            FrequencyMinHz = FrequencyMinHz,
            FrequencyMaxHz = FrequencyMaxHz,
            PulseWidthMicroseconds = PulseWidthMicroseconds,
            SpeedForMaximumFrequency = SpeedForMaximumFrequency,
            DefaultVolumePercent = DefaultVolumePercent
        };
        configuration.Normalize();
        return configuration;
    }

    public OsrDeviceConfig CreateOsrConfiguration()
    {
        var ranges = OsrAxes.ToDictionary(axis => axis.Label);
        var configuration = new OsrDeviceConfig
        {
            EnableMultiAxis = OsrEnableMultiAxis,
            UpdateRate = OsrUpdateRate,
            RangeLimits = new RangeConfiguration
            {
                Linear = ranges["Linear"].CreateRange(),
                Surge = ranges["Surge"].CreateRange(),
                Sway = ranges["Sway"].CreateRange(),
                Twist = ranges["Twist"].CreateRange(),
                Roll = ranges["Roll"].CreateRange(),
                Pitch = ranges["Pitch"].CreateRange()
            }
        };
        configuration.Normalize();
        return configuration;
    }

    public void Normalize()
    {
        RangeMin = Math.Clamp(RangeMin, 0, 100);
        RangeMax = Math.Clamp(RangeMax, RangeMin, 100);
        if (HasDgLab)
        {
            var configuration = CreateDgLabConfiguration();
            PowerMin = configuration.PowerMin;
            PowerMax = configuration.PowerMax;
            FrequencyMinHz = configuration.FrequencyMinHz;
            FrequencyMaxHz = configuration.FrequencyMaxHz;
            PulseWidthMicroseconds =
                configuration.PulseWidthMicroseconds;
            SpeedForMaximumFrequency =
                configuration.SpeedForMaximumFrequency;
            DefaultVolumePercent =
                configuration.DefaultVolumePercent;
        }

        if (HasOsr)
        {
            var configuration = CreateOsrConfiguration();
            OsrUpdateRate = configuration.UpdateRate;
        }

        if (HasOffset)
        {
            OffsetMilliseconds =
                DeviceOffset.Normalize(OffsetMilliseconds);
        }

        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class AxisRangeViewModel : INotifyPropertyChanged
{
    private int lower;
    private int upper = 100;

    private AxisRangeViewModel(
        string label,
        string toolTip,
        int lower,
        int upper)
    {
        Label = label;
        ToolTip = toolTip;
        this.lower = Math.Clamp(lower, 0, 100);
        this.upper = Math.Clamp(upper, this.lower, 100);
    }

    public string Label { get; }
    public string ToolTip { get; }

    public int Lower
    {
        get => lower;
        set
        {
            lower = Math.Clamp(value, 0, Upper);
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Lower)));
        }
    }

    public int Upper
    {
        get => upper;
        set
        {
            upper = Math.Clamp(value, Lower, 100);
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Upper)));
        }
    }

    public static AxisRangeViewModel From(
        string label,
        string toolTip,
        CmdRange range)
        => new(
            label,
            toolTip,
            range.LowerLimit,
            range.UpperLimit);

    public CmdRange CreateRange()
    {
        var range = new CmdRange
        {
            UpperLimit = 100,
            LowerLimit = Lower
        };
        range.UpperLimit = Upper;
        return range;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
