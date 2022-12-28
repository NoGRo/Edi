using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edi.Core.Funscript;
using Edi.Core.Gallery.models;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Edi.Core.Gallery
{
    public partial class GalleryBundler
    {
        private List<GalleryIndex> Galleries = new List<GalleryIndex>();

        

        public GalleryBundler(IConfiguration configuration)
        {
            Config = new GalleryBundlerConfig();
            configuration.GetSection("GalleryBundler").Bind(Config);
        }

        public GalleryBundlerConfig Config { get; set; }
        private ScriptBuilder sb { get; set; } = new ScriptBuilder();

        public void Add(GalleryIndex gallery, bool repeats)
        {
            gallery.Repeats = repeats;

            var Index = gallery;

            var startTime = sb.TotalTime;

            sb.addCommands(gallery.Commands);

            Index.Duration = sb.TotalTime - startTime;
            Index.StartTime = startTime;
            Index.EndTime = sb.TotalTime;

            //6 seconds repear in script bundle for loop msg delay
            if (gallery.Repeats)
            {
                int loopDuration = Index.Duration + Config.RepearDuration;

                sb.addCommands(gallery.Commands.Clone());
                while (sb.TotalTime <= loopDuration)
                {
                    sb.addCommands(gallery.Commands.Clone());
                }
                sb.TrimTimeTo(loopDuration);
            }

            if (Config.SpacerDuration > 0 ) // extra, no movement
                sb.AddCommandMillis(Config.SpacerDuration, sb.lastValue);

            Galleries.Add(Index);
        }
        public Dictionary<string, FileInfo> GenerateBundle()
        {
            var cmds = sb.Generate();

            var final = new Dictionary<string, FileInfo>();

            var funscript = new FunScriptFile();
            funscript.actions = cmds.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = x.Value }).ToList();

            var filePath = Config.UserDataPath + "\\bundle.funscript";
            funscript.Save(filePath);
            final.Add("funscript", new FileInfo(filePath));

            var csv = new FunScriptCsv(cmds);
            var csvPath = Config.UserDataPath + "\\bundle.csv";
            csv.Save(csvPath);
            final.Add("csv", new FileInfo(csvPath));

            Galleries.ForEach(x => x.Assets = final);

            return final;
        }
    }
}
