using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Funscript.FileJson
{
    public class FunScriptMetadata
    {
        //public List<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();
        public List<FunScriptChapter> chapters { get; set; } = new List<FunScriptChapter>();
        public string creator { get; set; } = "";
        public string description { get; set; } = "";
        public int duration { get; set; } = 0;
        public string license { get; set; } = "";
        public string notes { get; set; } = "";
        public List<string> performers { get; set; } = new List<string>();
        public string scriptUrl { get; set; } = "";
        public List<string> tags { get; set; } = new List<string>();
        public string title { get; set; } = "";
        public string type { get; set; } = "";
        public string videoUrl { get; set; } = "";
    }
}
