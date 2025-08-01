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

        #region move to some creation pattern
        public static CmdLinear GetCommandSpeed(int speed, double value, double initialValue)
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
        public static CmdLinear GetCommand(int millis, double value, double initialValue = 0)
        {
            return new CmdLinear
            {
                InitialValue = Convert.ToByte(initialValue),
                Millis = millis,
                Value = Convert.ToByte(value)
            };
        }
        public static CmdLinear GetCommand(uint millis, double value, double initialValue = 0)
        {
            return new CmdLinear
            {
                InitialValue = initialValue,
                Millis = Convert.ToInt32(millis),
                Value = value
            };
        }

        public static CmdLinear GetCommandMillis(int millis, double value)
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
        public double Value { get; set; }


        public int Speed => Millis == 0 ? 0 : Convert.ToInt32(Math.Abs(InitialValue - Value) / (double)Millis * 1000);
        public uint buttplugMillis => (uint)Millis;
        public short Distance => (short)Math.Abs(Direction ? Value - InitialValue : InitialValue - Value);
        public bool Direction => Value > InitialValue;
        
        private double initialValue;
        public double InitialValue
        {
            get => Prev?.Value ?? initialValue;
            set => initialValue = value;
        }


        public int GetValueInRange(int min, int max)
        {
            return Convert.ToInt32(min + ((max - min) / ((double)100) * Value));

        }
        public CmdLinear Clone()
        {
            return new CmdLinear
            {
                AbsoluteTime = AbsoluteTime,
                Value = Value,
                Millis = Millis,
            };
        }

    }
    public static class CmdLinearExtend
    {
        public static List<CmdLinear> Clone(this IEnumerable<CmdLinear> cmds)
            => cmds.Select(x => x.Clone()).ToList();

        public static List<CmdLinear> AddAbsoluteTime(this List<CmdLinear> cmds)
        {
            var at = 0;
            CmdLinear last = cmds.LastOrDefault();
            foreach (var cmd in cmds)
            {
                cmd.Prev = last;
                at += cmd.Millis;
                cmd.AbsoluteTime = at;
                last = cmd;
            }
            return cmds;
        }
    }
}
