
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
    public class CmdLinealRepository : IGalleryRepository<CmdLinealGallery>
    {
        public CmdLinealRepository(IConfiguration configuration, DefinitionRepository definition)
        {
            Config = new GalleryConfig();
            configuration.GetSection("Gallery").Bind(Config);
        }
        private Dictionary<string, List<CmdLinealGallery>> Galleries { get; set; } = new Dictionary<string, List<CmdLinealGallery>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } = new List<string>();
        private GalleryConfig Config { get; set; }

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
                foreach (var DefinitionGallery in Config.Definitions)
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

                    CmdLinealGallery gallery = ParseActions(variant, DefinitionGallery, actions);

                    if (!Galleries.ContainsKey(DefinitionGallery.Name))
                        Galleries.Add(DefinitionGallery.Name, new List<CmdLinealGallery>());

                    Galleries[gallery.Name].Add(gallery);
                }
            }
        }

        private Dictionary<string, FunScriptFile> GetGalleryFunscripts()
        {
            var FunscriptCache = new Dictionary<string, FunScriptFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var variantPath in Variants)
            {
                var variant = new DirectoryInfo(variantPath).Name;
                foreach (var DefinitionGallery in Config.Definitions)
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
        private static CmdLinealGallery ParseActions(string variant, DefinitionGallery DefinitionGallery, IEnumerable<FunScriptAction> actions)
        {
            var sb = new ScriptBuilder();
            foreach (var action in actions)
            {
                sb.AddCommandMillis(
                    millis: Convert.ToInt32(action.at - DefinitionGallery.StartTime - sb.TotalTime),
                    value: action.pos);
            }
            var gallery = new CmdLinealGallery
            {
                Name = DefinitionGallery.Name,
                Variant = variant,
                //Definition = DefinitionGallery,

            };
            sb.TrimTimeTo(DefinitionGallery.Duration);

            gallery.Commands = sb.Generate();

            return gallery;
        }

        public List<string> GetVariants()
            => Variants;
        public List<CmdLinealGallery> GetAll()
            => Galleries.Values.SelectMany(x => x).ToList();

        public CmdLinealGallery? Get(string name, string variant = null)
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
