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
        public string filename { get; set; }

        [JsonIgnore]
        public string name => filename.Split('.').First();

        private string _variant;
        [JsonIgnore]
        public string variant
        {
            get
            {
                if (_variant != null)
                    return _variant;
                if (filename is null)
                    return "";

                var nameSplit = filename.Split('.');
                if (nameSplit.Length == 3)
                    return nameSplit[1];
                else if (nameSplit.Length == 2)
                    return axis == Axis.Default ? nameSplit[1] : "";

                return "";
            }
            set => _variant = value;
        }

        [JsonIgnore]
        public Axis axis
        {
            get
            {
                var axis = filename?.Split('.').Last();

                if (axis != null && !int.TryParse(axis, out _))
                {
                    Axis parsedAxis;
                    var parsed = Enum.TryParse(axis, true, out parsedAxis);
                    return parsed ? parsedAxis : Axis.Default;
                }

                return Axis.Default;
            }
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
            FunScriptFile result;
            try
            {
                result  = JsonConvert.DeserializeObject<FunScriptFile>(File.ReadAllText(path));
            }
            catch {
                return null;
            }
            result.path = path;
            result.filename = Path.GetFileNameWithoutExtension(path);
            return result;
        }

        public void Save(string filename)
        {
            string content = JsonConvert.SerializeObject(this);

            File.WriteAllText(filename, content, new UTF8Encoding(false));
        }
    }

}