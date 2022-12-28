
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;

namespace Edi.Core.Gallery.Index
{
    public class IndexRepository : IGalleryRepository<IndexGallery>
    {
        public IndexRepository(IConfiguration configuration, GalleryBundler bundler, IGalleryRepository<CmdLinealGallery> Cmdlineals)
        {
            Config = new GalleryConfig();
            configuration.GetSection("Gallery").Bind(Config);
            this.Cmdlineals = Cmdlineals;
        }

        private Dictionary<string, List<IndexGallery>> Galleries { get; set; } = new Dictionary<string, List<IndexGallery>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } = new List<string>();
        private GalleryConfig Config { get; set; }
        private GalleryBundler Bundler { get; set; }
        public IGalleryRepository<CmdLinealGallery> Cmdlineals { get; }

        public async Task Init()
        {
            LoadGalleryv();
        }


        private void LoadGalleryv()
        {
            var CmdGalleries = Cmdlineals.GetAll();
            foreach (var cmdGallery in CmdGalleries)
            {

                IndexGallery index = new IndexGallery
                {
                    Name = cmdGallery.Name,
                    Repeats = cmdGallery.Repeats,
                    Variant = cmdGallery.Variant,
                };
                if (!Galleries.ContainsKey(cmdGallery.Name))
                    Galleries.Add(cmdGallery.Name, new List<IndexGallery>());

                Bundler.Add(cmdGallery, cmdGallery.Repeats);
                Galleries[cmdGallery.Name].Add(index);
            }
            Assets = Bundler.GenerateBundle();
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
        private static IndexGallery ParseActions(string variant, DefinitionGallery DefinitionGallery, IEnumerable<FunScriptAction> actions)
        {
            var sb = new ScriptBuilder();
            foreach (var action in actions)
            {
                sb.AddCommandMillis(
                    millis: Convert.ToInt32(action.at - DefinitionGallery.StartTime - sb.TotalTime),
                    value: action.pos);
            }
            var gallery = new IndexGallery
            {
                Name = DefinitionGallery.Name,
                Variant = variant,
                //Definition = DefinitionGallery,
                Duration = Convert.ToInt32(DefinitionGallery.EndTime - DefinitionGallery.StartTime)
            };
            sb.TrimTimeTo(gallery.Duration);



            return gallery;
        }

        public List<string> GetVariants()
            => Variants;
        public List<IndexGallery> GetAll()
            => Galleries.Values.SelectMany(x => x).ToList();


        public IndexGallery? Get(string name, string variant = null)
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
