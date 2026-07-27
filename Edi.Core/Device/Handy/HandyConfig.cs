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
        public const int MinimumOffsetMS = -7000;
        public const int MaximumOffsetMS = 7000;
        public const int OffsetStepMS = 10;

        private int _offsetMS = -80;

        public string Key { get; set; }
        public string ApiKey { get; set; } = "B8v2-Qr2mjNO8J3wyfSeJDslofcBLWNz";
        public int OffsetMS
        {
            get => _offsetMS;
            set => _offsetMS = NormalizeOffset(value);
        }

        internal static int NormalizeOffset(int value)
        {
            var clamped = Math.Clamp(
                value,
                MinimumOffsetMS,
                MaximumOffsetMS);
            var rounded = clamped >= 0
                ? ((clamped + OffsetStepMS / 2) / OffsetStepMS) * OffsetStepMS
                : ((clamped - OffsetStepMS / 2) / OffsetStepMS) * OffsetStepMS;

            return Math.Clamp(
                rounded,
                MinimumOffsetMS,
                MaximumOffsetMS);
        }
    }
}
