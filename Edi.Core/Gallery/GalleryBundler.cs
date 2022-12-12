using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edi.Core.Gallery;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery
{
    public class GalleryBundler
    {

        public List<GalleryIndex> Galleries = new List<GalleryIndex>();

        private List<CmdLinear> cmds;
        public List<CmdLinear> Cmds { get => cmds; private set => cmds = value; }

        private ScriptBuilder sb = new ScriptBuilder();

        public GalleryConfig Config { get; set; }

        public void Add(GalleryIndex gallery, bool repeats, bool hasSpacer)
        {

            gallery.Repeats = repeats;
            gallery.HasSpacer = hasSpacer;

            var Index = gallery;

            var spacerDuration = 5000;
            var repearDuration = 6000;

            var startTime = sb.TotalTime;

            sb.addCommands(gallery.Commands);

            Index.Duration = sb.TotalTime - startTime;
            Index.StartTime = startTime;
            Index.EndTime = sb.TotalTime;

            //6 seconds repear in script bundle for loop msg delay
            if (gallery.Repeats)
            {
                sb.addCommands(gallery.Commands.Clone());
                sb.TrimTimeTo(sb.TotalTime + repearDuration);
            }

            if (Index.HasSpacer) // extra, no movement
                sb.AddCommandMillis(spacerDuration, sb.lastValue);

            Galleries.Add(Index);
        }


        public Dictionary<string, FileInfo> GenerateBundle()
        {
            cmds = sb.Generate();

            var final = new Dictionary<string, FileInfo>();

            //Cmds.AddAbsoluteTime();
            var funscript = new FunScriptFile();
            funscript.actions = cmds.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = x.Value }).ToList();

            var filePath = Config.UserDataPath + "bundle.funscript";
            funscript.Save(filePath);
            final.Add("funscript", new FileInfo(filePath));

            var csv = new FunScriptCsv(cmds);
            var csvPath = Config.UserDataPath + "bundle.csv";
            csv.Save(csvPath);
            final.Add("csv", new FileInfo(csvPath));

            Galleries.ForEach(x => x.Assets = final);

            return final;
        }
    }
}
