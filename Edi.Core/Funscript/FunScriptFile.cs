using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;



namespace Edi.Core.Funscript
{
    /// <summary>
    /// https://godoc.org/github.com/funjack/launchcontrol/protocol/funscript
    /// 
    /// Example:
    /// 
    /// {
    ///	"version": "1.0",
    ///	"inverted": false,
    ///	"range": 90,
    ///	"actions": [
    ///		{"pos": 0, "at": 100},
    ///		{"pos": 100, "at": 500},
    ///		...
    ///	]
    ///}
    ///
    ///version: funscript version (optional, default="1.0")
    ///inverted: positions are inverted (0=100,100=0) (optional, default=false)
    ///range: range of moment to use in percent (0-100) (optional, default=90)
    ///actions: script for a Launch
    ///  pos: position in percent (0-100)
    ///  at : time to be at position in milliseconds
    /// </summary>
    public class FunScriptFile
    {

        public string version { get; set; }
        public bool inverted { get; set; }
        public int range { get; set; }
        public string path { get; set; }

        public List<FunScriptAction> actions { get; set; }


        public FunScriptFile()
        {
            inverted = false;
            version = "1.0";
            range = 99;
            actions = new List<FunScriptAction>();
        }

        public static FunScriptFile Load(string path)
        {
            path = Path.GetFullPath(path);
            return JsonConvert.DeserializeObject<FunScriptFile>(File.ReadAllText(path));
        }

        public void Save(string filename)
        {
            string content = JsonConvert.SerializeObject(this);

            File.WriteAllText(filename, content, new UTF8Encoding(false));
        }
    }

}