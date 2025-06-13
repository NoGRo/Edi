using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Interfaces
{
    public interface IRange
    {
        public int Min { get; set; }
        public int Max { get; set; }


    }

    public static class RangeEx
    {
        public static void SetRange(this IRange Thisrange, IRange range)
        {
            Thisrange.Max = range.Max;
            Thisrange.Min = range.Min;
        }
        public static void SetRange(this IRange Thisrange, int min, int max)
        {
            Thisrange.Max = max;
            Thisrange.Min = min;
        }
    }
}
