using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionGallery : IValidatableObject
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
        public long Duration => StartTime - EndTime;
        public bool Loop { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var validations = new List<ValidationResult>();

            if (EndTime < StartTime)
                validations.Add(new ValidationResult("EndTime < StartTime"));

            return validations;
        }
    }
}
