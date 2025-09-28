using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Definition;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Funscript.Command;

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

        private static FunscriptGallery ParseActions(string variant, Axis axis, DefinitionGallery DefinitionGallery, ref List<FunScriptAction> actions)
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
                            && x.at <= definition.EndTime)
                            .OrderBy(x => x.at)
                            .ToList();

            if (!actions.Any())
            {
                _logger.LogWarning($"Filtered actions are empty for Definition: {definition.Name}, File: {asset.File.FullName}");
                return null;
            }
         
            actions = inproveLoopAccion(actions, funscript.actions, definition);
           
            
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

        // Adjusts or inserts start/end points for loop actions.
        private List<FunScriptAction> inproveLoopAccion(List<FunScriptAction> actionsFiltred, List<FunScriptAction> allActions, DefinitionGallery definition)
        {
            if (!Definition.Config.InproveLoopDetection
                || actionsFiltred.Count == 0 
                || !definition.Loop)
                return actionsFiltred;

            const int tolerance = 100;

            // Validación: si el loop ya es perfecto (primer y último punto coinciden en tiempo y posición)
            if (actionsFiltred.First().at == definition.StartTime &&
                actionsFiltred.Last().at == definition.EndTime &&
                actionsFiltred.First().pos == actionsFiltred.Last().pos)
            {
                return actionsFiltred;
            }

            // Ajustar o insertar el primer punto
            if (Math.Abs(actionsFiltred.First().at - definition.StartTime) > tolerance)
            {
                // Buscar la acción previa antes de StartTime dentro de la tolerancia
                var prev = allActions
                    .Where(x => x.at < definition.StartTime && Math.Abs(x.at - definition.StartTime) <= tolerance)
                    .OrderByDescending(x => x.at)
                    .FirstOrDefault();
                actionsFiltred.Insert(0, new FunScriptAction { at = definition.StartTime, pos = prev?.pos ?? actionsFiltred.First().pos });
            }
            else
            {
                actionsFiltred.First().at = definition.StartTime;
            }

            // Adjust or insert the last point
            int lastIdx = actionsFiltred.Count - 1;
            if (Math.Abs(actionsFiltred.Last().at - definition.EndTime) > tolerance)
            {
                // Find the next action after EndTime within tolerance
                var next = allActions
                    .Where(x => x.at > definition.EndTime && Math.Abs(x.at - definition.EndTime) <= tolerance) // Get actions after EndTime within tolerance
                    .FirstOrDefault(); // Take the closest next action
                actionsFiltred.Add(new FunScriptAction { at = definition.EndTime, pos = next?.pos ?? actionsFiltred.Last().pos });
                lastIdx = actionsFiltred.Count - 1;
            }
            else
            {
                actionsFiltred.Last().at = definition.EndTime;
            }

            // Match position for loop
            if (actionsFiltred.First().pos != actionsFiltred.Last().pos)
                actionsFiltred.Last().pos = actionsFiltred.First().pos;

            return actionsFiltred;
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
