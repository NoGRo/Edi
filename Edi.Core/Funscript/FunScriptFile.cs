using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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


        private Regex regex = new Regex(@"^(?<name>.*?)(\.(?<variant>[^.]+))?$");

        public string name => path is null ? "" : regex.Match(Path.GetFileNameWithoutExtension(path)).Groups["name"].Value;

        private string _variant = null;
        public string variant
        {
            get => _variant ??( path is null ? "" :  regex.Match(Path.GetFileNameWithoutExtension(path)).Groups["variant"].Value);
            set => _variant = value;
        }

        public List<FunScriptAction> actions { get; set; }
        public FunScriptMetadata metadata { get; set; }

        public FunScriptFile()
        {
            inverted = false;
            version = "1.0";
            range = 99;
            actions = new List<FunScriptAction>();
        }

        public static FunScriptFile TryRead(string path)
        {
            try
            {
                return Read(path);
            }
            catch {
                return null;
            } 
        }



        public static FunScriptFile Read(string path)
        {
            path = Path.GetFullPath(path);
            var result = JsonConvert.DeserializeObject<FunScriptFile>(File.ReadAllText(path));
            result.path  = path;
            return result;
        }

        public void Save(string filename)
        {
            string content = JsonConvert.SerializeObject(this);

            File.WriteAllText(filename, content, new UTF8Encoding(false));
        }
    }

}