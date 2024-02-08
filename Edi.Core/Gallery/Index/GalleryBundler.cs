using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edi.Core.Funscript;

using System.Reflection;
using Edi.Core.Gallery.CmdLineal;

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
        }
        public IndexGallery Add(FunscriptGallery gallery, string bundleName )
        {
            
            var startTime = sb.TotalTime;

            sb.addCommands(gallery.Commands);

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
                sb.AddCommandMillis(gallery.Loop ? Config.MinRepearDuration : Config.SpacerDuration, sb.lastValue);
                indexGallery.Duration = Convert.ToInt32(sb.TotalTime - startTime);
            }
            else if (gallery.Loop)
            {
                var NewTotalTime = startTime + Math.Max(indexGallery.Duration , Config.MinRepearDuration) + Config.RepearDuration;

                sb.addCommands(gallery.Commands.Clone());
                while (sb.TotalTime <= NewTotalTime)
                {
                    sb.addCommands(gallery.Commands.Clone());
                }
                sb.TrimTimeTo(NewTotalTime);

                indexGallery.Duration = Convert.ToInt32(NewTotalTime - startTime - Config.RepearDuration);
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

            var funscript = new FunScriptFile();
            funscript.actions = cmds.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = x.Value }).ToList();

            var filePath = Edi.OutputDir + $"\\bundle.{variant}.funscript";
            funscript.Save(filePath);
            final.Add(variant+".funscript", new FileInfo(filePath));

            var csv = new FunScriptCsv(cmds);
            var csvPath = Edi.OutputDir + $"\\bundle.{variant}.csv";
            csv.Save(csvPath);
            final.Add(variant+".csv", new FileInfo(csvPath));

            Galleries.ForEach(x => x.Assets = final);

            return final;
        }
    }
}
