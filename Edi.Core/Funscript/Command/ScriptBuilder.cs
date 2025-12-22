using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Funscript.Command
{
    public class ScriptBuilder
    {
        private List<CmdLinear> Sequence { get; set; } = new List<CmdLinear>();

        public List<CmdLinear> Generate(long offset = 0)
        {
            var resul = Sequence.ToList();
            if(offset != 0)
                resul.ForEach(x => x.AbsoluteTime += offset);
            
            Clear();
            return resul;
        }
        
        public double lastValue => Sequence.LastOrDefault()?.Value ?? 0;
        public CmdLinear lastCmd => Sequence.LastOrDefault();
        public long TotalTime { get; private set; }
        public void Clear()
        {
            Sequence.Clear();
            Sequence = null;
            Sequence = new List<CmdLinear>();
            TotalTime = 0;
        }


        private void addCommand(CmdLinear cmd)
        {

            cmd.Prev = Sequence.LastOrDefault();
            if (cmd.Prev != null)
                cmd.Prev.Next = cmd;

            TotalTime += cmd.Millis;
            cmd.AbsoluteTime = TotalTime;
            Sequence.Add(cmd);
        }
        public void AddTime(long time)
        {
            TotalTime += time;
        }
        //go to a value at speed (Use starting point to calculate speed)
        public void AddCommand(CmdLinear cmd)
        {
            addCommand(CmdLinear.GetCommandMillis(cmd.Millis, cmd.Value));
        }
        public void addCommands(IEnumerable<CmdLinear> cmds)
        {
            foreach (var cmd in cmds)
            {
                addCommand(CmdLinear.GetCommandMillis(cmd.Millis, cmd.Value));
            }
        }

        public void AddCommandSpeed(int speed, int value, int? currentValue = null)
        {
            var cmd = CmdLinear.GetCommandSpeed(speed, value, currentValue ?? lastValue);
            addCommand(cmd);
        }
        //go to a value in Milliseconds 
        public void AddCommandMillis(long millis, double value)
        {

            var cmd = CmdLinear.GetCommandMillis(Convert.ToInt32(millis), value);
            addCommand(cmd);
        }
        public void AddCommandMillis(int millis, double value)
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
            if (lastCmd is null) return;
            Sequence.RemoveAll(x => x.AbsoluteTime > maxTime);
            TotalTime = lastCmd.AbsoluteTime;

            if (!Sequence.Any())
                return;

            if (lastCmd.AbsoluteTime != maxTime)
                AddCommandMillis(Convert.ToInt32(maxTime - TotalTime), lastCmd.Value);

            TotalTime = lastCmd.AbsoluteTime;
        }

        public void CutToTime(long maxTime)
        {
            if (lastCmd is null) return;
            var nextFinal = Sequence.FirstOrDefault(x => x.AbsoluteTime > maxTime);
            Sequence.RemoveAll(x => x.AbsoluteTime > maxTime);

            TotalTime = lastCmd.AbsoluteTime;

            if (!Sequence.Any())
                return;

            if (lastCmd.AbsoluteTime != maxTime)
            {


                if (nextFinal is null)
                {
                    AddCommandMillis(maxTime - TotalTime, lastValue);

                }
                else
                {
                    var newTime = nextFinal.Millis - Convert.ToInt32(nextFinal.AbsoluteTime - maxTime);
                    AddCommandMillis(newTime, nextFinal.GetValueInTime(newTime));
                }
            }


            TotalTime = lastCmd.AbsoluteTime;
        }

    }
}
