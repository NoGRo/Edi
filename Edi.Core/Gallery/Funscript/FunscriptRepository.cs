
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Definition;

namespace Edi.Core.Gallery.CmdLineal
{
    public class FunscriptRepository : IGalleryRepository<FunscriptGallery>
    {
        public FunscriptRepository(IConfiguration configuration, DefinitionRepository definition)
        {
            Config = new GalleryConfig();
            configuration.GetSection(GalleryConfig.Secction).Bind(Config);
            Definition = definition;
        }
        private Dictionary<string, List<FunscriptGallery>> Galleries { get; set; } = new Dictionary<string, List<FunscriptGallery>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } = new List<string>();
        public  GalleryConfig Config { get; set; }
        public DefinitionRepository Definition { get; }

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
                foreach (var DefinitionGallery in Definition.GetAll())
                {
                    var filePath = $"{Config.GalleryPath}\\{variant}\\{DefinitionGallery.FileName}.funscript";

                    if (!FunscriptCache.ContainsKey(filePath))
                        continue;

                    var funscript = FunscriptCache[filePath];

                    var actions = funscript.actions
                        .Where(x => x.at > DefinitionGallery.StartTime
                                 && x.at <= DefinitionGallery.EndTime);

                    if (!actions.Any())
                        continue;

                    FunscriptGallery gallery = ParseActions(variant, DefinitionGallery, actions);

                    if (!Galleries.ContainsKey(DefinitionGallery.Name))
                        Galleries.Add(DefinitionGallery.Name, new List<FunscriptGallery>());

                    Galleries[gallery.Name].Add(gallery);
                }
            }
            Variants = Galleries.SelectMany(x=> x.Value.Select(y=> y.Variant)).Distinct().ToList();
        }

        private Dictionary<string, FunScriptFile> GetGalleryFunscripts()
        {
            var FunscriptCache = new Dictionary<string, FunScriptFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var variantPath in Variants)
            {
                var variant = new DirectoryInfo(variantPath).Name;
                foreach (var DefinitionGallery in Definition.GetAll())
                {
                    var filePath = $"{Config.GalleryPath}\\{variant}\\{DefinitionGallery.FileName}.funscript";
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
        private static FunscriptGallery ParseActions(string variant, DefinitionGallery DefinitionGallery, IEnumerable<FunScriptAction> actions)
        {
            var sb = new ScriptBuilder();
            foreach (var action in actions)
            {
                sb.AddCommandMillis(
                    millis: Convert.ToInt32(action.at - DefinitionGallery.StartTime - sb.TotalTime),
                    value: action.pos);
            }
            var gallery = new FunscriptGallery
            {
                Name = DefinitionGallery.Name,
                Variant = variant,
                Loop = DefinitionGallery.Loop,

            };
            sb.TrimTimeTo(DefinitionGallery.Duration);

            gallery.Commands = sb.Generate();

            return gallery;
        }

        public List<string> GetVariants()
            => Variants;
        public List<FunscriptGallery> GetAll()
            => Galleries.Values.SelectMany(x => x).ToList();

        public FunscriptGallery? Get(string name, string variant = null)
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
