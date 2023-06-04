
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.CmdLineal;
using NAudio.Wave;
using Edi.Core.Gallery.Definition;

namespace Edi.Core.Gallery.EStimAudio
{


    public class AudioRepository : IGalleryRepository<AudioGallery>
    {
        public AudioRepository(IConfiguration configuration, IGalleryRepository<DefinitionGallery> definitions)
        {
            Config = new GalleryConfig();
            configuration.GetSection("Gallery").Bind(Config);
            Definitions = definitions;
        }

        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        private List<string> Variants { get; set; } = new List<string>();
        private GalleryConfig Config { get; set; }
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


            var variants = Directory.GetDirectories($"{GalleryPath}");
            Variants.AddRange(variants);


            var definitions = Definitions.GetAll();

            foreach (var variant in variants)
            {

                // Obtener las definiciones de fragmentos de audio


                // Recorrer cada una de las definiciones de fragmentos de audio
                foreach (var definition in definitions)
                {
                    // Abrir el archivo de audio original
                    
                    var filePath = $"{Config.GalleryPath}\\{variant}\\{definition.FileName}.Mp3";
                    // Crear un nuevo archivo de audio para almacenar el fragmento
                    

                    var file = new FileInfo(filePath);

                    if (filePath is null || !file.Exists)
                        continue;

                    var reader = new Mp3FileReader(file.FullName);

                    if (!reader.CanSeek)
                    {
                        reader.Close();
                        continue;
                    }

                    reader.CurrentTime = TimeSpan.FromMilliseconds(definition.StartTime);
                    reader.Close();


                    // Crear una nueva galería para almacenar el fragmento de audio
                    AudioGallery gallery = new AudioGallery
                    {
                        Name = definition.Name,
                        Variant = variant,
                        AudioPath = filePath,
                        Loop = definition.Loop,
                        Duration = definition.Duration,
                        StartTime = definition.StartTime
                    };

                    // Añadir la galería a la lista de galerías
                    if (!Galleries.ContainsKey(definition.Name))
                        Galleries.Add(definition.Name, new List<AudioGallery>());

                    Galleries[definition.Name].Add(gallery);
                }
            }
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
