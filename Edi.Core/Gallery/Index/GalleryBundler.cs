using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Edi.Core.Gallery.CmdLineal;

namespace Edi.Core.Gallery.Index
{
    public class GalleryBundler
    {
        private List<IndexGallery> Galleries = new List<IndexGallery>();



        public GalleryBundler(IConfiguration configuration)
        {
            Config = new GalleryBundlerConfig();
            configuration.GetSection(GalleryBundlerConfig.Secction).Bind(Config);
        }

        public GalleryBundlerConfig Config { get; set; }
        private ScriptBuilder sb { get; set; } = new ScriptBuilder();

        public IndexGallery Add(FunscriptGallery gallery, bool repeats)
        {
            gallery.Loop = repeats;


            var startTime = sb.TotalTime;

            sb.addCommands(gallery.Commands);

            var indexGallery = new IndexGallery
            {
                Name = gallery.Name,
                Loop = gallery.Loop,
                Variant = gallery.Variant,
                Duration = Convert.ToInt32(sb.TotalTime - startTime),
                StartTime = startTime
            };

            //6 seconds repear in script bundle for loop msg delay
            if (gallery.Loop)
            {
                
                var NewTotalTime = sb.TotalTime + Config.RepearDuration;

                sb.addCommands(gallery.Commands.Clone());
                while (sb.TotalTime <= NewTotalTime)
                {
                    sb.addCommands(gallery.Commands.Clone());
                }
                sb.TrimTimeTo(NewTotalTime);
            }
            else if (Config.SpacerDuration > 0) // extra, no movement
                sb.AddCommandMillis(Config.SpacerDuration, sb.lastValue);

            Galleries.Add(indexGallery);

            return indexGallery;
        }
        public Dictionary<string, FileInfo> GenerateBundle()
        {
            var cmds = sb.Generate();

            var final = new Dictionary<string, FileInfo>();

            var funscript = new FunScriptFile();
            funscript.actions = cmds.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = x.Value }).ToList();

            var filePath = Config.OutputFolder + "\\bundle.funscript";
            funscript.Save(filePath);
            final.Add("funscript", new FileInfo(filePath));

            var csv = new FunScriptCsv(cmds);
            var csvPath = Config.OutputFolder + "\\bundle.csv";
            csv.Save(csvPath);
            final.Add("csv", new FileInfo(csvPath));

            Galleries.ForEach(x => x.Assets = final);

            return final;
        }
    }
}
