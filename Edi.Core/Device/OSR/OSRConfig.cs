using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Services;
using PropertyChanged;
using Edi.Core.Funscript.Command;


namespace Edi.Core.Device.OSR
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class OSRConfig
    {
        public string COMPort { get; set; } = null;
        public string UdpAddress { get; set; } = null;
        public bool EnableMultiAxis { get; set; } = false;
        public int UpdateRate { get; set; } = 200;
        public RangeConfiguration RangeLimits { get; set; } = new RangeConfiguration();
    }

    public class RangeConfiguration
    {
        public CmdRange Linear { get; set; } = new CmdRange();
        public CmdRange Roll { get; set; } = new CmdRange();
        public CmdRange Pitch { get; set; } = new CmdRange();
        public CmdRange Twist { get; set; } = new CmdRange();
        public CmdRange Sway { get; set; } = new CmdRange();
        public CmdRange Surge { get; set; } = new CmdRange();

        public RangeConfiguration Clone()
        {
            return new RangeConfiguration
            {
                Linear = Linear?.Clone() ?? new CmdRange(),
                Roll = Roll?.Clone() ?? new CmdRange(),
                Pitch = Pitch?.Clone() ?? new CmdRange(),
                Twist = Twist?.Clone() ?? new CmdRange(),
                Sway = Sway?.Clone() ?? new CmdRange(),
                Surge = Surge?.Clone() ?? new CmdRange()
            };
        }
    }

    [AddINotifyPropertyChangedInterface]
    public sealed class OsrDeviceConfig : INotifyPropertyChanged
    {
#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        public bool EnableMultiAxis { get; set; }
        public int UpdateRate { get; set; } = 200;
        public RangeConfiguration RangeLimits { get; set; } = new();

        public static OsrDeviceConfig FromDefaults(OSRConfig defaults)
            => new()
            {
                EnableMultiAxis = defaults.EnableMultiAxis,
                UpdateRate = defaults.UpdateRate,
                RangeLimits =
                    defaults.RangeLimits?.Clone()
                    ?? new RangeConfiguration()
            };

        public OsrDeviceConfig Clone()
            => new()
            {
                EnableMultiAxis = EnableMultiAxis,
                UpdateRate = UpdateRate,
                RangeLimits =
                    RangeLimits?.Clone()
                    ?? new RangeConfiguration()
            };

        public void CopyFrom(OsrDeviceConfig source)
        {
            ArgumentNullException.ThrowIfNull(source);
            EnableMultiAxis = source.EnableMultiAxis;
            UpdateRate = source.UpdateRate;
            RangeLimits = source.RangeLimits?.Clone()
                ?? new RangeConfiguration();
        }

        public void Normalize()
        {
            UpdateRate = Math.Clamp(UpdateRate, 1, 1000);
            RangeLimits ??= new RangeConfiguration();
            RangeLimits.Linear ??= new CmdRange();
            RangeLimits.Roll ??= new CmdRange();
            RangeLimits.Pitch ??= new CmdRange();
            RangeLimits.Twist ??= new CmdRange();
            RangeLimits.Sway ??= new CmdRange();
            RangeLimits.Surge ??= new CmdRange();
            NormalizeRange(RangeLimits.Linear);
            NormalizeRange(RangeLimits.Roll);
            NormalizeRange(RangeLimits.Pitch);
            NormalizeRange(RangeLimits.Twist);
            NormalizeRange(RangeLimits.Sway);
            NormalizeRange(RangeLimits.Surge);
        }

        private static void NormalizeRange(CmdRange range)
        {
            var lower = Math.Clamp(range.LowerLimit, 0, 100);
            var upper = Math.Clamp(range.UpperLimit, lower, 100);
            range.UpperLimit = 100;
            range.LowerLimit = lower;
            range.UpperLimit = upper;
        }
    }

    [GameConfig]
    public sealed class OsrDevicesConfig : INotifyPropertyChanged
    {
        private Dictionary<string, OsrDeviceConfig> devices = new();

        public event PropertyChangedEventHandler PropertyChanged;

        public Dictionary<string, OsrDeviceConfig> Devices
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

        public OsrDeviceConfig GetOrAdd(
            string deviceName,
            Func<OsrDeviceConfig> create)
        {
            if (devices.TryGetValue(deviceName, out var configuration))
                return configuration;

            configuration = create();
            devices.Add(deviceName, configuration);
            Subscribe(configuration);
            RaiseDevicesChanged();
            return configuration;
        }

        private void Subscribe(IEnumerable<OsrDeviceConfig> configurations)
        {
            foreach (var configuration in configurations)
                Subscribe(configuration);
        }

        private void Subscribe(OsrDeviceConfig configuration)
        {
            if (configuration is not INotifyPropertyChanged changed)
                return;

            changed.PropertyChanged -= ChildConfigurationChanged;
            changed.PropertyChanged += ChildConfigurationChanged;
        }

        private void Unsubscribe(IEnumerable<OsrDeviceConfig> configurations)
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
}
