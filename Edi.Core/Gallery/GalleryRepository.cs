
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using Edi.Core.Gallery.models;
using System.Runtime.CompilerServices;

namespace Edi.Core.Gallery
{
    public class GalleryRepository : IGalleryRepository
    {
        public GalleryRepository(IConfiguration configuration, GalleryBundler bundler)
        {
            Config = new GalleryConfig();
            configuration.GetSection("Gallery").Bind(Config);

            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            if (!File.Exists($"{GalleryPath}\\Definitions.csv"))
                return;

            using (var reader = File.OpenText($"{GalleryPath}Definitions.csv"))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                Config.Definitions = csv.GetRecords<GalleryDefinition>().ToList();
            }
            Bundler = bundler;
        }

        private Dictionary<string, List<GalleryIndex>> Galleries { get; set; } = new Dictionary<string, List<GalleryIndex>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } = new List<string>();
        private GalleryConfig Config { get; set; }
        private GalleryBundler Bundler { get; set; }

        public async Task Init()
        {
            LoadGalleryFromCsv();
        }


        private void LoadGalleryFromCsv()
        {

            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            

            var variants = Directory.GetDirectories($"{GalleryPath}");
            Variants.AddRange(variants);


            var FunscriptCache = GetGalleryFunscripts();

            foreach (var variantPath in variants)
            {
                var variant = new DirectoryInfo(variantPath).Name;
                foreach (var galleryDefinition in Config.Definitions)
                {
                    var filePath = $"{Config.GalleryPath}\\{variant}\\{galleryDefinition.FileName}.funscript";

                    if (!FunscriptCache.ContainsKey(filePath))
                        continue;

                    var funscript = FunscriptCache[filePath];

                    var actions = funscript.actions
                        .Where(x => x.at > galleryDefinition.StartTime
                                 && x.at <= galleryDefinition.EndTime);

                    if (!actions.Any())
                        continue;

                    GalleryIndex gallery = ParseActions(variant, galleryDefinition, actions);

                    if (!Galleries.ContainsKey(galleryDefinition.Name))
                        Galleries.Add(galleryDefinition.Name, new List<GalleryIndex>());

                    Bundler.Add(gallery, galleryDefinition.Loop);
                    Galleries[gallery.Name].Add(gallery);
                }


            }
            Assets = Bundler.GenerateBundle();
        }

        private Dictionary<string, FunScriptFile> GetGalleryFunscripts()
        {
            var FunscriptCache = new Dictionary<string, FunScriptFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var variantPath in Variants)
            {
                var variant = new DirectoryInfo(variantPath).Name;
                foreach (var galleryDefinition in Config.Definitions)
                {
                    var filePath = $"{Config.GalleryPath}\\{variant}\\{galleryDefinition.FileName}.funscript";
                    FunScriptFile funscript;
                    if (!FunscriptCache.ContainsKey(filePath))
                    {
                        try
                        {
                            funscript = JsonSerializer.Deserialize<FunScriptFile>(File.ReadAllText(filePath));
                            funscript.actions = funscript.actions.OrderBy(x => x.at).ToList();
                        }
                        catch
                        {
                            continue;
                        }
                        FunscriptCache.Add(filePath, funscript);
                    }
                }
            }
            return FunscriptCache;

        }
        private static GalleryIndex ParseActions(string variant, GalleryDefinition galleryDefinition, IEnumerable<FunScriptAction> actions)
        {
            var sb = new ScriptBuilder();
            foreach (var action in actions)
            {
                sb.AddCommandMillis(
                    millis: Convert.ToInt32(action.at - galleryDefinition.StartTime - sb.TotalTime),
                    value: action.pos);
            }
            var gallery = new GalleryIndex
            {
                Name = galleryDefinition.Name,
                Variant = variant,
                Definition = galleryDefinition,
                Duration = Convert.ToInt32(galleryDefinition.EndTime - galleryDefinition.StartTime)
            };
            sb.TrimTimeTo(gallery.Duration);

            gallery.Commands = sb.Generate();

            return gallery;
        }

        public List<string> GetNames()
            => Galleries.Keys.ToList();
        public List<string> GetVariants()
            => Variants;
        public List<GalleryDefinition> GetDefinitions()
            => Config.Definitions;
        

        public GalleryIndex? Get(string name, string variant = null)
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
