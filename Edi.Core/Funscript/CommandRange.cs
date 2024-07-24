

namespace Edi.Core.Funscript
{
    public class CmdRange
    {
        private int _lowerLimit = 0;
        private int _upperLimit = 100;
        public int LowerLimit
        {
            get { return _lowerLimit; }
            set { _lowerLimit = Math.Max(Math.Min(_upperLimit, value), 0); }
        }

        public int UpperLimit
        {
            get { return _upperLimit; }
            set { _upperLimit = Math.Min(Math.Max(_lowerLimit, value), 200); }
        }

        public int RangeDelta()
        {
            return _upperLimit - _lowerLimit;
        }

        public CmdLinear ProcessCommand(CmdLinear command)
        {
            command.Value = Convert.ToByte(Math.Min(100, _lowerLimit + (RangeDelta() / 100 * command.Value)));

            return command;
        }

        public CmdRange Clone()
        {
            return new CmdRange
            {
                LowerLimit = LowerLimit,
                UpperLimit = UpperLimit
            };
        }
    }
}
