using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionDto 
    {
        [Required]
        public string Name { get; set; }

        [Required]
        [RegularExpression("filler|gallery|reaction")]
        public string Type { get; set; }
        [Required]
        public string FileName { get; set; }
        public string StartTime { get; set; }
        [Required]
        public string EndTime { get; set; }
        public string Loop { get; set; }
    }
}
