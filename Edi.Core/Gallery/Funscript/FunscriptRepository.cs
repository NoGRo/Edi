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
using Microsoft.Extensions.Logging;

namespace Edi.Core.Gallery.Funscript
{
    public class FunscriptRepository : RepositoryBase<FunscriptGallery>
    {
        private readonly ILogger _logger;
        private List<FunScriptFile> ToSave = new List<FunScriptFile>();
        private Dictionary<string, FunScriptFile> cacheFun = new();

        public FunscriptRepository(DefinitionRepository definition, ILogger<FunscriptRepository> logger) : base(definition, logger)
        {
            _logger = logger;
            _logger.LogInformation("FunscriptRepository initialized.");
            //Init(null).GetAwaiter().GetResult();
        }

        public override IEnumerable<string> Accept => new[] { "funscript" };
        public override IEnumerable<string> Reserve => Enum.GetNames(typeof(Axis));

        private void SyncChapterInfo(DefinitionGallery DefinitionGallery, FunScriptFile funscript)
        {
            if (!Definition.Config.GenerateChaptersFromDefinition)
            {
                return;
            }
            if (funscript.metadata == null)
                funscript.metadata = new FunScriptMetadata();

            var chapter = funscript.metadata?.chapters?.FirstOrDefault(x => x.name.StartsWith(DefinitionGallery.Name,StringComparison.InvariantCultureIgnoreCase));

            string chapterName = $"{DefinitionGallery.Name}{(DefinitionGallery.Loop ? "" : "[nonLoop]")}{(DefinitionGallery.Type == "gallery" ? "" : $"[{DefinitionGallery.Type}]")}";

            bool isNew = chapter == null;

            _logger.LogInformation($"{(isNew ? "Adding new" : "Updating")} chapter to FunScript: {chapterName}, Start: {DefinitionGallery.StartTime}, End: {DefinitionGallery.EndTime}");

            chapter ??= new();
            chapter.name = chapterName;
            chapter.StartTimeMilis = DefinitionGallery.StartTime;
            chapter.EndTimeMilis = DefinitionGallery.EndTime;

            if (isNew) funscript.metadata.chapters.Add(chapter);
            ToSave.Add(funscript);

        }

        private static FunscriptGallery ParseActions(string variant, Axis axis, DefinitionGallery DefinitionGallery, ref IEnumerable<FunScriptAction> actions)
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
                Duration = DefinitionGallery.Duration
            };
            sb.TrimTimeTo(DefinitionGallery.Duration);

            gallery.AxesCommands[axis] = sb.Generate();

            return gallery;
        }

        public override FunscriptGallery ReadGallery(AssetEdi asset, DefinitionGallery definition)
        {
            //_logger.LogInformation($"Reading FunScriptGallery for Asset: {asset.File.Name}, Definition: {definition.Name}");

            if (!cacheFun.ContainsKey(asset.File.FullName))
            {
                //_logger.LogInformation($"Caching FunScript file: {asset.File.FullName}");
                cacheFun.Add(asset.File.FullName, FunScriptFile.TryRead(asset.File.FullName));
            }

            var funscript = cacheFun[asset.File.FullName];

            if (funscript == null || funscript.actions?.Any() != true)
            {
                _logger.LogWarning($"No actions found in FunScript file: {asset.File.FullName} for Gallery {definition.Name}");
                return null;
            }

            var actions = funscript.actions
                .Where(x => x.at >= definition.StartTime
                            && x.at <= definition.EndTime);

            if (!actions.Any())
            {
                _logger.LogWarning($"Filtered actions are empty for Definition: {definition.Name}, File: {asset.File.FullName}");
                return null;
            }

            SyncChapterInfo(definition, funscript);

            var gallery = ParseActions(asset.Variant, funscript.axis, definition, ref actions);

            if (Galleries.ContainsKey(definition.Name))
            {
                var existingGallery = Galleries[definition.Name].Find(g => g.Variant == gallery.Variant);
                if (existingGallery != null)
                {
                    _logger.LogInformation($"Updating existing gallery for Definition: {definition.Name}, Variant: {gallery.Variant}");
                    existingGallery.AxesCommands[funscript.axis] = gallery.AxesCommands[funscript.axis];
                    return null;
                }
            }

           // _logger.LogInformation($"Gallery created for Definition: {definition.Name}, Variant: {gallery.Variant}");
            return gallery;
        }

        protected override void ReadEnd()
        {
            _logger.LogInformation("Finalizing reading process in FunscriptRepository.");

            if (Definition.Config.GenerateChaptersFromDefinition)
            {
                _logger.LogInformation($"Saving updated FunScript files. Total: {ToSave.Count}");
                ToSave.Distinct().ToList().ForEach(x => x.Save(x.path));
            }
            ToSave.Clear();
            cacheFun.Clear(); 
        }
    }
}
