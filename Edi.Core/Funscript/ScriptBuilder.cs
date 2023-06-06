
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Funscript
{
    public class ScriptBuilder
    {
        private List<CmdLinear> Sequence { get; set; } = new List<CmdLinear>();

        public List<CmdLinear> Generate()
        {
            var resul = Sequence.ToList();
            Clear();
            return resul;
        }
        public int lastValue => Sequence.LastOrDefault()?.Value ?? 0;
        public long TotalTime { get; private set; }
        public void Clear()
        {
            Sequence.Clear();
            TotalTime = 0;
        }

        //go to a value at speed (Use starting point to calculate speed)
        public void addCommand(CmdLinear cmd)
        {
            TotalTime += cmd.Millis;
            cmd.AbsoluteTime = TotalTime;
            Sequence.Add(cmd);
        }
        public void addCommands(IEnumerable<CmdLinear> cmds)
        {
            foreach (var cmd in cmds)
            {
                addCommand(cmd);
            }
        }

        public void AddCommandSpeed(int speed, int value, int? currentValue = null)
        {
            var cmd = CmdLinear.GetCommandSpeed(speed, value, currentValue ?? lastValue);
            addCommand(cmd);
        }
        //go to a value in Milliseconds 
        public void AddCommandMillis(int millis, int value)
        {
            var cmd = CmdLinear.GetCommandMillis(millis, value);
            addCommand(cmd);
        }

     
        internal void AddCommandSpeed(double speed, int value)
        => AddCommandSpeed(Convert.ToInt32(speed), value);

        public void MergeCommands()
        {
            var final = new List<CmdLinear>();

            if (Sequence.Count < 2)
                return;

            var last = Sequence.First();
            final.Add(last);

            for (int i = 1; i < Sequence.Count; i++)
            {
                var comNext = Sequence[i];

                if (last.Speed == comNext.Speed && last.Direction == comNext.Direction)
                {
                    last.Value = comNext.Value;
                    last.Millis += comNext.Millis;
                }
                else
                {
                    final.Add(comNext);
                    last = comNext;
                }
            }
            Sequence = final.Where(x => x.Millis > 0).ToList();
            TotalTime = last.AbsoluteTime;
        }



        public void TrimTimeTo(long maxTime)
        {

            var final = Sequence.Where(x => x.AbsoluteTime <= maxTime);

            if (!final.Any())
                return;

            var last = final.Last();
            if (last.AbsoluteTime != maxTime)
                last.Millis += Convert.ToInt32(maxTime - last.AbsoluteTime);

            Sequence = final.ToList();
            TotalTime = last.AbsoluteTime; 
        }

    }
}
