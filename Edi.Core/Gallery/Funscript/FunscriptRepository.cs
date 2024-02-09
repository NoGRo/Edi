
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Definition;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace Edi.Core.Gallery.CmdLineal
{
    public class FunscriptRepository : IGalleryRepository<FunscriptGallery>
    {
        public FunscriptRepository(ConfigurationManager configuration, DefinitionRepository definition)
        {
            Config = configuration.Get<GalleryConfig>();
            Definition = definition;
        }
        private Dictionary<string, List<FunscriptGallery>> Galleries { get; set; } = new Dictionary<string, List<FunscriptGallery>>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Accept => new[] { "funscript" };
        private List<string> Variants { get; set; } = new List<string>();
        public  GalleryConfig Config { get; set; }
        public DefinitionRepository Definition { get; }

        public async Task Init()
        {
            LoadFromFunscripts();
        }

        private List<FunScriptFile> ToSave = new List<FunScriptFile>();
        private void LoadFromFunscripts()
        {
            var GalleryPath = Config.GalleryPath;

            Galleries.Clear();
            ToSave.Clear();
            Variants.Clear();


            var Assets  = this.Discover(GalleryPath);
            
            foreach (var DefinitionGallery in Definition.GetAll())
            {
                Galleries.Add(DefinitionGallery.Name, new());

                var assets = Assets.Where(x => x.Name == DefinitionGallery.FileName)
                                        ;
                foreach (var asset in assets)
                {

                    var funscript = FunScriptFile.TryRead(asset.File.FullName);
                    
                    if (funscript == null || funscript.actions?.Any() != true)
                        continue;

                    var actions = funscript.actions
                        .Where(x => x.at >= DefinitionGallery.StartTime
                                 && x.at <= DefinitionGallery.EndTime);

                    if (!actions.Any())
                    {
                        Debug.WriteLine($"FunscriptRepository Empty ignored: {DefinitionGallery.Name}");
                        continue;
                    }

                    SyncChapterInfo(DefinitionGallery, funscript);

                    FunscriptGallery gallery = ParseActions(asset.Variant, DefinitionGallery, actions);
                    
                    Galleries[DefinitionGallery.Name].Add(gallery);
                }
            }

            //SyncChapterInfo
            ToSave.Distinct().ToList().ForEach(x => x.Save(x.path));

            Variants = Galleries.SelectMany(x => x.Value.Select(y => y.Variant)).Distinct().ToList();
        }

        private void SyncChapterInfo(DefinitionGallery DefinitionGallery, FunScriptFile? funscript)
        {
            var chapter = funscript.metadata.chapters.FirstOrDefault(x => x.name == DefinitionGallery.Name);
            if (chapter == null)
            {
                funscript.metadata.chapters.Add(new()
                {
                    name = DefinitionGallery.Name,
                    StartTimeMilis = DefinitionGallery.StartTime,
                    EndTimeMilis = DefinitionGallery.EndTime
                });
                ToSave.Add(funscript);
            }
            else
            {
                if (DefinitionGallery.StartTime != chapter.StartTimeMilis
                 && DefinitionGallery.EndTime != chapter.EndTimeMilis)
                {
                    chapter.StartTimeMilis = DefinitionGallery.StartTime;
                    chapter.EndTimeMilis = DefinitionGallery.EndTime;
                    ToSave.Add(funscript);

                }

            }
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
                //Duration = DefinitionGallery.Duration,

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

            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault();


            if (gallery is null) 
                return null;

            gallery = new FunscriptGallery
            {
                Name = gallery.Name,
                Variant = gallery.Variant,
                Loop = gallery.Loop,
                Commands = gallery.Commands.Clone(),
            };
            return gallery;

        }

    }
}
