
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Definition;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;

namespace Edi.Core.Gallery.EStimAudio
{


    public class AudioRepository : RepositoryBase<AudioGallery>
    {
        public AudioRepository(DefinitionRepository definitions, ILogger<AudioRepository> _logger) : base(definitions, _logger)
        {

        }
        public override IEnumerable<string> Accept => new[] { "mp3" };

        public override AudioGallery ReadGallery(AssetEdi asset, DefinitionGallery definition)
        {
            return new AudioGallery
            {
                Name = definition.Name,
                Variant = asset.Variant,
                AudioPath = asset.File.FullName,
                Loop = definition.Loop,
                Duration = definition.Duration,
                StartTime = definition.StartTime
            };
        }
    }
}
