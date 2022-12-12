using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;


namespace Edi.Core.Funscript
{

    public class FunScriptCsv
    {
        public FunScriptCsv(IEnumerable<CmdLinear> cmds)
        {
            actions = cmds.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = x.Value }).ToList();
        }


        public FunScriptCsv()
        {
            actions = new List<FunScriptAction>();
        }

        public List<FunScriptAction> actions { get; set; }


        public void Save(string filename)
        {
            File.WriteAllText(path: filename,
                              contents: string.Join("\r\n", actions.Select(x => $"{x.at},{x.pos}")),
                              encoding: new UTF8Encoding(true));
        }
    }

}