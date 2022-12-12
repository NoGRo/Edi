using Edi.Core.Funscript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core
{
    public interface IDevice
    {
        public string Name { get; }
        Task SendCmd(CmdLinear cmd);
    }
}
