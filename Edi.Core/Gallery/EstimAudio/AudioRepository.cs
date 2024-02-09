
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.CmdLineal;
using NAudio.Wave;
using Edi.Core.Gallery.Definition;
using System.Text.RegularExpressions;

namespace Edi.Core.Gallery.EStimAudio
{


    public class AudioRepository : IGalleryRepository<AudioGallery>
    {
        public AudioRepository(ConfigurationManager configuration, IGalleryRepository<DefinitionGallery> definitions)
        {
            Config = configuration.Get<GalleryConfig>();
            Definitios = definitions;
        }
        public IEnumerable<string> Accept => new[] { "mp3" };
        private List<string> Variants { get; set; } = new List<string>();
        public GalleryConfig Config { get; set; }
        private IGalleryRepository<DefinitionGallery> Definition { get; }
        private Dictionary<string, List<AudioGallery>> Galleries { get; set; } = new Dictionary<string, List<AudioGallery>>(StringComparer.OrdinalIgnoreCase);

        public async Task Init()
        {
            LoadGalleryFromDefinitions();
        }

        private void LoadGalleryFromDefinitions()
        {
            var GalleryPath = Config.GalleryPath;

            Galleries.Clear();
            Variants.Clear();


            var Assets = this.Discover(GalleryPath);

            foreach (var DefinitionGallery in Definition.GetAll())
            {
                Galleries.Add(DefinitionGallery.Name, new());

                var assets = Assets.Where(x => x.Name == DefinitionGallery.FileName)
                        ;
                foreach (var asset in assets)
                {
                    {
                    reader = new Mp3FileReader(file.FullName);
                }
                catch 
                {
                    continue;
                }
                
                if (!reader.CanSeek)
                {
                    reader.Close();
                    continue;
                }
                

                foreach (var DefinitionGallery in definitions.Where(x => x.FileName == fileName))
                {
                    try
                    {
                        reader.CurrentTime = TimeSpan.FromMilliseconds(DefinitionGallery.StartTime);
                    }
                    catch 
                    {
                        continue;
                    }

                    AudioGallery gallery = new AudioGallery
                    {
                        Name = DefinitionGallery.Name,
                        Variant = variant,
                        AudioPath = file.FullName,
                        Loop = DefinitionGallery.Loop,
                        Duration = DefinitionGallery.Duration,
                        StartTime = DefinitionGallery.StartTime
                    };

                    if (!Galleries.ContainsKey(DefinitionGallery.Name))
                        Galleries.Add(DefinitionGallery.Name, new List<AudioGallery>());

                    Galleries[DefinitionGallery.Name].Add(gallery);
                }
                reader.Close();
            }

            Variants = Galleries.SelectMany(x => x.Value.Select(y => y.Variant)).Distinct().ToList();
        }
        public List<string> GetVariants()
            => Variants;
        public List<AudioGallery> GetAll()
            => Galleries.Values.SelectMany(x=>x).ToList();

        public AudioGallery? Get(string name, string variant = null)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 

            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault();
            return gallery;

        }
    }
}
