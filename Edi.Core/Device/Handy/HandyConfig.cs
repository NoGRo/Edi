using Edi.Core.Services;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class HandyConfig
    {
        public const int MinimumOffsetMS = DeviceOffset.MinimumMilliseconds;
        public const int MaximumOffsetMS = DeviceOffset.MaximumMilliseconds;
        public const int OffsetStepMS = DeviceOffset.StepMilliseconds;

        private int _offsetMS = 0;

        public string Key { get; set; }
        public string ApiKey { get; set; } = "B8v2-Qr2mjNO8J3wyfSeJDslofcBLWNz";
        public int OffsetMS
        {
            get => _offsetMS;
            set => _offsetMS = NormalizeOffset(value);
        }

        public static int NormalizeOffset(int value)
            => DeviceOffset.Normalize(value);
    }
}
