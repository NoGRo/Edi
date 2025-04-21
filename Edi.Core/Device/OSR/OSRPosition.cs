using Edi.Core.Funscript;
using System.Text;

namespace Edi.Core.Device.OSR
{
    internal class OSRPosition
    {
        public ushort? L0 { get; set; }
        public ushort? L1 { get; set; }
        public ushort? L2 { get; set; }
        public ushort? R0 { get; set; }
        public ushort? R1 { get; set; }
        public ushort? R2 { get; set; }
        public ushort? V0 { get; set; }
        public ushort? A0 { get; set; }
        public ushort? A1 { get; set; }

        public long? DeltaMillis { get; set; }

        public static OSRPosition ZeroedPosition() => new OSRPosition
        {
            L0 = 0,
            V0 = 0,

            L1 = 5000,
            L2 = 5000,
            R0 = 5000,
            R1 = 5000,
            R2 = 5000,
            A0 = 5000,
            A1 = 5000
        };

        public static OSRPosition FromAxisDictionary(Dictionary<Axis, ushort?> axisValues)
        {
            var pos = new OSRPosition();
            pos.L0 = axisValues[Axis.Default];
            pos.L1 = axisValues[Axis.Surge];
            pos.L2 = axisValues[Axis.Sway];
            pos.R0 = axisValues[Axis.Twist];
            pos.R1 = axisValues[Axis.Roll];
            pos.R2 = axisValues[Axis.Pitch];
            pos.V0 = axisValues[Axis.Vibrate];
            pos.A0 = axisValues[Axis.Valve];
            pos.A1 = axisValues[Axis.Suction];

            return pos;
        }

        public void Merge(OSRPosition other)
        {
            L0 ??= other.L0;
            L1 ??= other.L1;
            L2 ??= other.L2;
            R0 ??= other.R0;
            R1 ??= other.R1;
            R2 ??= other.R2;
            V0 ??= other.V0;
            A0 ??= other.A0;
            A1 ??= other.A1;
        }

        public string OSRCommandString(OSRPosition? prevPos = null)
        {
            if (DeltaMillis == null)
                return string.Empty;

            StringBuilder sb = new();
            if (L0.HasValue && (prevPos == null || L0 != prevPos.L0))
                sb.Append($"L0{L0.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (L1.HasValue && (prevPos == null || L1 != prevPos.L1))
                sb.Append($"L1{L1.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (L2.HasValue && (prevPos == null || L2 != prevPos.L2))
                sb.Append($"L2{L2.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (R0.HasValue && (prevPos == null || R0 != prevPos.R0))
                sb.Append($"R0{R0.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (R1.HasValue && (prevPos == null || R1 != prevPos.R1))
                sb.Append($"R1{R1.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (R2.HasValue && (prevPos == null || R2 != prevPos.R2))
                sb.Append($"R2{R2.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (V0.HasValue && (prevPos == null || V0 != prevPos.V0))
                sb.Append($"V0{V0.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (A0.HasValue && (prevPos == null || A0 != prevPos.A0))
                sb.Append($"A0{A0.ToString().PadLeft(4, '0')}I{DeltaMillis} ");
            if (A1.HasValue && (prevPos == null || A1 != prevPos.A1))
                sb.Append($"A1{A1.ToString().PadLeft(4, '0')}I{DeltaMillis} ");

            return sb.ToString().Trim();
        }

        public OSRPosition Clone()
        {
            return new OSRPosition
            {
                L0 = L0,
                L1 = L1,
                L2 = L2,
                R0 = R0,
                R1 = R1,
                R2 = R2,
                V0 = V0,
                A0 = A0,
                A1 = A1,
                DeltaMillis = DeltaMillis,
            };
        }

        public void UpdateRanges(RangeConfiguration ranges)
        {
            if (L0.HasValue)
                L0 = (ushort)Math.Min(9999, ranges.Linear.LowerLimit / 100f * 9999 + ranges.Linear.RangeDelta() / 100f * L0.GetValueOrDefault());

            if (L1.HasValue)
                L1 = (ushort)Math.Min(9999, ranges.Surge.LowerLimit / 100f * 9999f + ranges.Surge.RangeDelta() / 100f * L1.GetValueOrDefault());

            if (L2.HasValue)
                L2 = (ushort)Math.Min(9999, ranges.Sway.LowerLimit / 100f * 9999f + ranges.Sway.RangeDelta() / 100f * L2.GetValueOrDefault());

            if (R0.HasValue)
                R0 = (ushort)Math.Min(9999, ranges.Twist.LowerLimit / 100f * 9999f + ranges.Twist.RangeDelta() / 100f * R0.GetValueOrDefault());

            if (R1.HasValue)
                R1 = (ushort)Math.Min(9999, ranges.Roll.LowerLimit / 100f * 9999f + ranges.Roll.RangeDelta() / 100f * R1.GetValueOrDefault());

            if (R2.HasValue)
                R2 = (ushort)Math.Min(9999, ranges.Pitch.LowerLimit / 100f * 9999f + ranges.Pitch.RangeDelta() / 100f * R2.GetValueOrDefault());
        }

        public ushort? GetAxisValue(Axis axis)
        {
            switch (axis)
            {
                case Axis.Default:
                    return L0;
                case Axis.Surge:
                    return L1;
                case Axis.Sway:
                    return L2;
                case Axis.Twist:
                    return R0;
                case Axis.Roll:
                    return R1;
                case Axis.Pitch:
                    return R2;
                case Axis.Vibrate:
                    return V0;
                case Axis.Valve:
                    return A0;
                case Axis.Suction:
                    return A1;
                default:
                    return null;
            }
        }
    }
}
