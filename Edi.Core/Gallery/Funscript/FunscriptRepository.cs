
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
using System.Collections.Immutable;

namespace Edi.Core.Gallery.CmdLineal
{
    public class FunscriptRepository : RepositoryBase<FunscriptGallery>
    {
        public FunscriptRepository(DefinitionRepository definition) : base(definition)
        {
        }

        public override IEnumerable<string> Accept => new[] { "funscript" };
        public override IEnumerable<string> Reserve => Enum.GetNames(typeof(Axis));

        private List<FunScriptFile> ToSave = new List<FunScriptFile>();

        private void SyncChapterInfo(DefinitionGallery DefinitionGallery, FunScriptFile? funscript)
        {
            if (funscript.metadata == null)
                funscript.metadata = new FunScriptMetadata();
            
            var chapter = funscript.metadata?.chapters?.FirstOrDefault(x => x.name == DefinitionGallery.Name);
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


        private static FunscriptGallery ParseActions(string variant, Axis axis, DefinitionGallery DefinitionGallery,ref IEnumerable<FunScriptAction> actions)
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
                Duration = DefinitionGallery.Duration,

            };
            sb.TrimTimeTo(DefinitionGallery.Duration);

            gallery.AxesCommands[axis] = sb.Generate();

            return gallery;
        }


        public override FunscriptGallery? Get(string name, string variant = null)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 

            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault();

            if (gallery is null) 
                return null;


            return gallery;
        }

        private Dictionary<string, FunScriptFile> cacheFun = new();
        public override FunscriptGallery ReadGallery(AssetEdi asset, DefinitionGallery definition)
        {
            if (!cacheFun.ContainsKey(asset.File.FullName))
                cacheFun.Add(asset.File.FullName, FunScriptFile.TryRead(asset.File.FullName));
            

            var funscript = cacheFun[asset.File.FullName];

            if (funscript == null || funscript.actions?.Any() != true)
                return null;
            var actions = funscript.actions
                .Where(x => x.at >= definition.StartTime
                            && x.at <= definition.EndTime);
            
            if (!actions.Any())
            {
                Debug.WriteLine($"FunscriptRepository Empty ignored: {definition.Name}");
                return null;
            }

            SyncChapterInfo(definition, funscript);


            var gallery = ParseActions(asset.Variant, funscript.axis, definition, ref actions);
            if (Galleries.ContainsKey(definition.Name))
            {
                var existingGallery = Galleries[definition.Name].Find(g => g.Variant == gallery.Variant);
                if (existingGallery != null)
                {
                    existingGallery.AxesCommands[funscript.axis] = gallery.AxesCommands[funscript.axis];
                    return null;
                }
            }

            return gallery;
        }
        protected override void ReadEnd()
        {
            if (Definition.Config.GenerateChaptersFromDefinition)
            {
                ToSave.Distinct().ToList().ForEach(x => x.Save(x.path));
            }
        }
    }
}
