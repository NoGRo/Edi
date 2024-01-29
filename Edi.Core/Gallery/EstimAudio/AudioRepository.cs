
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
            Definitions = definitions;
        }

        private List<string> Variants { get; set; } = new List<string>();
        public GalleryConfig Config { get; set; }
        private IGalleryRepository<DefinitionGallery> Definitions { get; }
        private Dictionary<string, List<AudioGallery>> Galleries { get; set; } = new Dictionary<string, List<AudioGallery>>(StringComparer.OrdinalIgnoreCase);

        public async Task Init()
        {
            LoadGalleryFromDefinitions();
        }

        private void LoadGalleryFromDefinitions()
        {
            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            var definitions = Definitions.GetAll();

            var dir = new DirectoryInfo(GalleryPath);
            var mp3Files = dir.EnumerateFiles("*.mp3").ToList();

            mp3Files.AddRange(dir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles("*.mp3")));

            if (!mp3Files.Any())
                return;

            var regex = new Regex(@"^(?<nombre>.*?)(\.(?<variante>[^.]+))?$");

            mp3Files = mp3Files.DistinctBy(x => x.Name).ToList();

            foreach (var file in mp3Files)
            {
                var fileName = regex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["nombre"].Value;
                var variant = regex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["variante"].Value;

                var pathSplit = file.FullName.Replace(GalleryPath + "\\", "").Split('\\');
                var pathVariant = pathSplit.Length > 1 ? pathSplit[0] : null;

                variant = !string.IsNullOrEmpty(variant)
                                        ? variant
                                        : pathVariant ?? Config.DefaulVariant;

                Mp3FileReader reader;
                try
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
            variant = variant ?? Config.SelectedVariant ?? Config.DefaulVariant;

            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault(x => x.Variant == Config.SelectedVariant)
                        ?? variants.FirstOrDefault();
            return gallery;

        }
    }
}
