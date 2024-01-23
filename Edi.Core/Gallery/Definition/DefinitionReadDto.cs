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
        //[Index(0)]
        public string Name { get; set; }

        [Required]
        //[Index(1)]
        public string FileName { get; set; }
        //[Index(2)]
        public string StartTime { get; set; }
        [Required]
        //[Index(3)]
        public string EndTime { get; set; }

        [Required]
        [RegularExpression("filler|gallery|reaction")]
        //[Index(4)]
        public string Type { get; set; }
        //[Index(5)]
        public string Loop { get; set; }
    }
}
