
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Funscript;
using NAudio.Wave;
using Edi.Core.Gallery.Definition;
using System.Text.RegularExpressions;

namespace Edi.Core.Gallery.EStimAudio
{


    public class AudioRepository : RepositoryBase<AudioGallery>
    {
        public AudioRepository(DefinitionRepository definitions) : base(definitions)
        {

        }
        public override IEnumerable<string> Accept => new[] { "mp3" };

        public override AudioGallery ReadGallery(AssetEdi asset, DefinitionGallery definition)
        {
            //Validate(asset, definition);

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

        private static bool Validate(AssetEdi asset, DefinitionGallery definition)
        {
            Mp3FileReader reader;

            try
            {
                reader = new Mp3FileReader(asset.File.FullName);
            }
            catch
            {
                return false;
            }

            if (!reader.CanSeek)
            {
                reader.Close();
                return false;
            }

            try
            {
                reader.CurrentTime = TimeSpan.FromMilliseconds(definition.StartTime);
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
