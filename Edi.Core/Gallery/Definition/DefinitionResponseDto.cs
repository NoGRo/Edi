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
    public record DefinitionResponseDto(string Name,
                                        string Type,
                                        string FileName,
                                        long StartTime,
                                        long EndTime,
                                        int Duration,
                                        bool Loop,
                                        string Description)
    {
        // Constructor adicional que acepta un DefinitionGallery
        public DefinitionResponseDto(DefinitionGallery gallery)
            : this(gallery.Name,
                   gallery.Type,
                   gallery.FileName,
                   gallery.StartTime,
                   gallery.EndTime,
                   gallery.Duration,
                   gallery.Loop,
                   gallery.Description)
        {
        }
    }
}
