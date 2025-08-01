using CsvHelper.Configuration.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionReadDto 
    {
        [Required]
        public string Name { get; set; }

        [Required]
        public string FileName { get; set; }
        public string StartTime { get; set; }
        [Required]
        public string EndTime { get; set; }

        [Required]
        [RegularExpression("filler|gallery|reaction")]
        public string Type { get; set; }
        public string Loop { get; set; }

        [Optional]
        public string Description { get; set; }
    }
}
 