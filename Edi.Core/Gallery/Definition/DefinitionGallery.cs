using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

using System.Threading.Tasks;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionGallery : IGallery
    {
        [Required]
        public string Name { get; set; }

        [Required]
        [RegularExpression("filler|gallery|reaction")]
        public string Type { get; set; }

        [Required]
        public string FileName { get; set; }
        public long StartTime { get; set; }
        [Required]
        public long EndTime { get; set; }
        public int Duration => Convert.ToInt32(EndTime - StartTime);
        public bool Loop { get; set; }
        [JsonIgnore]
        public string Variant { get; set; }
        public string Description { get; set; }
        [JsonIgnore]
        public int Speed { get; set; } = 0;
        
    }
}
