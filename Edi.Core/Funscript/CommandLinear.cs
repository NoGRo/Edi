using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Schema;

namespace Edi.Core.Funscript
{
    public class CmdLinear
    {

        public static int SpeedLimit => 400;

        #region move to some creation pattern
        public static CmdLinear GetCommandSpeed(int speed, int value, int initialValue)
        {
            speed = speed > SpeedLimit ? SpeedLimit : speed;

            var millis = Convert.ToInt32(Math.Abs(initialValue - value) * ((double)1000 / speed));

            return new CmdLinear
            {
                Millis = millis,
                InitialValue = Convert.ToByte(initialValue),
                Value = Convert.ToByte(value),
            };
        }
        public static CmdLinear GetCommand(int millis, int value, int initialValue = 0)
        {
            return new CmdLinear
            {
                InitialValue = Convert.ToByte(initialValue),
                Millis = millis,
                Value = Convert.ToByte(value)
            };
        }
        public static CmdLinear GetCommand(uint millis, int value, int initialValue = 0)
        {
            return new CmdLinear
            {
                InitialValue = initialValue,
                Millis = Convert.ToInt32(millis),
                Value = value
            };
        }
        public static List<CmdLinear> ParseFunscript(FunScriptFile script)
        {
            var resul = new List<CmdLinear>();
            CmdLinear last = null;

            if (script == null)
                return resul;

            script.actions = script.actions.OrderBy(a => a.at).ToList();
            foreach (var action in script.actions)
            {
                var cmd = new CmdLinear
                {
                    Prev = last,
                    AbsoluteTime = action.at,
                    Millis = Convert.ToInt32(action.at - last?.AbsoluteTime ?? 0),
                    Value = Convert.ToInt32(action.pos)
                };
                if (last != null)
                    last.Next = cmd;

                last = cmd;
                resul.Add(cmd); ;
            }
            return resul;
        }
        public static CmdLinear GetCommandMillis(int millis, int value)
        {

            return new CmdLinear
            {
                Millis = millis,
                Value = Convert.ToByte(value)
            };
        }

        #endregion 
        public CmdLinear Prev { get; set; }
        public CmdLinear Next { get; set; }

        public long AbsoluteTime { get; set; }
        public int Millis { get; set; }
        public int Speed => Millis == 0 ? 0 : Convert.ToInt32(Math.Abs(InitialValue - Value) / (double)Millis * 1000);

        public bool Direction => Value > InitialValue;
        public int Value { get; set; }

        private int initialValue;
        public int InitialValue
        {
            get => Prev?.Value ?? initialValue;
            set => initialValue = value;
        }

        public double LinearValue => Math.Min(1.0, Math.Max(0, Value / (double)100));
        public double VibrateValue => Math.Min(1.0, Math.Max(0, Speed / (double)SpeedLimit));

        public uint buttplugMillis => (uint)Millis;

        public DateTime? Sent { get; set; }
        public bool Cancel { get; set; }
        public short Distance => (short)Math.Abs(Direction ? Value - InitialValue : InitialValue - Value);

    }
    public static class CmdLinearExtend
    {
        public static List<CmdLinear> Clone(this IEnumerable<CmdLinear> cmds)
            => cmds.Select(x => CmdLinear.GetCommandMillis(x.Millis, x.Value)).ToList();

        public static void AddAbsoluteTime(this List<CmdLinear> cmds)
            {lock (cmds)
            {
                var at = 0;
                foreach (var cmd in cmds)
                {
                    at += cmd.Millis;
                    cmd.AbsoluteTime = at;
                }
            }
        }
    }
}
