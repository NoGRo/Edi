using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edi.Core.Funscript;

using System.Reflection;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Gallery.Index
{
    public class GalleryBundler
    {
        private List<IndexGallery> Galleries = new List<IndexGallery>();



        public GalleryBundler(ConfigurationManager configuration)
        {
            Config = configuration.Get<GalleryBundlerConfig>();
        }

        public GalleryBundlerConfig Config { get; set; }
        private ScriptBuilder sb { get; set; } = new ScriptBuilder();

        public void Clear()
        {
            sb = new ScriptBuilder();
            sb.AddCommandMillis(Config.SpacerDuration, 0);
        }
        public IndexGallery Add(FunscriptGallery gallery, string bundleName )
        {
            
            var startTime = sb.TotalTime;

            sb.addCommands(gallery.Commands.Clone());

            var indexGallery = new IndexGallery
            {
                Name = gallery.Name,
                Loop = gallery.Loop,
                Variant = gallery.Variant,
                StartTime = startTime,
                Duration = Convert.ToInt32(sb.TotalTime - startTime),
                Bundle = bundleName
            };

            if (indexGallery.Duration == 0)
            {
                sb.AddCommandMillis(gallery.Loop ? Config.MinRepeatDuration : Config.SpacerDuration, sb.lastValue);
                indexGallery.Duration = Convert.ToInt32(sb.TotalTime - startTime);
            }
            else if (gallery.Loop)
            {
                var originalDuration = indexGallery.Duration;
                var newDuration = (int)Math.Ceiling((double)Config.MinRepeatDuration / originalDuration) * originalDuration;
                var NewTotalTime = startTime + newDuration + Config.RepeatDuration;

                sb.addCommands(gallery.Commands.Clone());
                while (sb.TotalTime < NewTotalTime)
                {
                    sb.addCommands(gallery.Commands.Clone());
                }
                sb.TrimTimeTo(NewTotalTime);

                indexGallery.Duration = newDuration;

            }
            else if (Config.SpacerDuration > 0) // extra, no movement
                sb.AddCommandMillis(Config.SpacerDuration, sb.lastValue);

            Galleries.Add(indexGallery);

            return indexGallery;
        }
        public Dictionary<string, FileInfo> GenerateBundle(string variant)
        {
            var cmds = sb.Generate();

            var final = new Dictionary<string, FileInfo>();

            // Borrar solo el contenido de la carpeta bundles si existe, o crearla si no existe
            var bundlesDir = Path.Combine(Edi.OutputDir, "bundles");

            var funscript = new FunScriptFile();
            funscript.actions = cmds.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = (int)Math.Round(x.Value) }).ToList();

            var filePath = Path.Combine(bundlesDir, $"bundle.{variant}.funscript");
            funscript.Save(filePath);
            final.Add(variant + ".funscript", new FileInfo(filePath));

            var csv = new FunScriptCsv(cmds);
            var csvPath = Path.Combine(bundlesDir, $"bundle.{variant}.csv");
            csv.Save(csvPath);
            final.Add(variant + ".csv", new FileInfo(csvPath));

            Galleries.ForEach(x => x.Assets = final);

            return final;
        }
    }
}
