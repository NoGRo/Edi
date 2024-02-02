using Edi.Core.Gallery.Definition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Edi.Core.Funscript
{
    public class FunScriptChapter
    {
        
        public string endTime { get; set; }
        public string name { get; set; }
        public string startTime { get; set; }
        [JsonIgnore]
        public long StartTimeMilis
        {
            get => Convert.ToInt64(TimeSpan.Parse(startTime ?? "0").TotalMilliseconds); 
            set => startTime = $"{TimeSpan.FromMilliseconds(value):hh\\:mm\\:ss\\.fff}";

        }
        [JsonIgnore]
        public long EndTimeMilis
        {
            get => Convert.ToInt64(TimeSpan.Parse(endTime ?? "0").TotalMilliseconds);
            set => endTime = $"{TimeSpan.FromMilliseconds(value):hh\\:mm\\:ss\\.fff}";

        }

    }
}
