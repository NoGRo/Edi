
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

        private List<FunScriptFile> ToSave = new List<FunScriptFile>();
        private Dictionary<string, List<FunscriptGallery>> GalleryCache = new();

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


        private FunscriptGallery ParseActions(string variant, DefinitionGallery DefinitionGallery,ref IEnumerable<FunScriptAction> actions, Axis axis)
        {
            var sb = new ScriptBuilder();
            foreach (var action in actions)
            {
                sb.AddCommandMillis(
                    millis: Convert.ToInt32(action.at - DefinitionGallery.StartTime - sb.TotalTime),
                    value: action.pos);
            }

            var galleryVariants = GalleryCache.GetValueOrDefault(DefinitionGallery.Name, new List<FunscriptGallery>());

            var gallery = galleryVariants.Find(g => g.Variant == variant);
            var newGallery = false;
            if (gallery == null)
            {
                gallery = new FunscriptGallery
                {
                    Name = DefinitionGallery.Name,
                    Loop = DefinitionGallery.Loop,
                    Variant = variant,
                    Duration = DefinitionGallery.Duration
                };

                galleryVariants.Add(gallery);
                GalleryCache[DefinitionGallery.Name] = galleryVariants;

                newGallery = true;
            }

            sb.TrimTimeTo(DefinitionGallery.Duration);
            gallery.AxisCommands[axis] = sb.Generate();

            if (newGallery) return gallery;
            else return null;
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

        public override FunscriptGallery ReadGallery(AssetEdi asset, DefinitionGallery definition)
        {
            var funscript = FunScriptFile.TryRead(asset.File.FullName);

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

            return ParseActions(asset.Variant, definition, ref actions, funscript.axis);

        }
        protected override void ReadEnd()
        {
            ToSave.Distinct().ToList().ForEach(x => x.Save(x.path));
        }
    }
}
