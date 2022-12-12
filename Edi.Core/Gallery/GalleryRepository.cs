using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.Json;
using System.Resources;
using System.Collections;
using System.Reflection;
using System.Formats.Asn1;
using System.Globalization;
using CsvHelper;
using static System.Net.WebRequestMethods;
using File = System.IO.File;
using System.Xml.Linq;
using Edi.Core.Gallery;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery
{
    public class GalleryRepository : IGalleryRepository
    {

        private Dictionary<string, List<GalleryIndex>> Galleries { get; set; } = new Dictionary<string, List<GalleryIndex>>();
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } =  new List<string>();
        private GalleryConfig Config { get; set; }

        public async Task Init()
        {
            LoadGalleryFromCsv();
        }

        private void LoadGalleryFromCsv()
        {

            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            if (!File.Exists($"{GalleryPath}\\Definitions.csv"))
                return;

            using (var reader = File.OpenText($"{GalleryPath}Definitions.csv"))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                Config.Definitions = csv.GetRecords<GalleryDefinition>().ToList();
            }

            var bundler = new GalleryBundler();
            var FunscriptCache = new Dictionary<string, FunScriptFile>(StringComparer.OrdinalIgnoreCase);

            var variants = Directory.GetDirectories($"{GalleryPath}");
            Variants.AddRange(variants);
            foreach (var variantPath in variants)
            {
                var variant = new DirectoryInfo(variantPath).Name;
                foreach (var galleryDefinition in Config.Definitions)
                {
                    var filePath = $"{GalleryPath}\\{variant}\\{galleryDefinition.FileName}.funscript";
                    FunScriptFile funscript = null;
                    if (!FunscriptCache.ContainsKey(filePath))
                    {
                        try
                        {
                            funscript = JsonSerializer.Deserialize<FunScriptFile>(File.ReadAllText(filePath));
                            funscript.actions = funscript.actions.OrderBy(x => x.at).ToList();
                        }
                        catch
                        {
                            continue;
                        }
                        FunscriptCache.Add(filePath, funscript);
                    }
                    funscript = FunscriptCache[filePath];


                    var actions = funscript.actions
                        .Where(x => x.at > galleryDefinition.StartTime
                                 && x.at <= galleryDefinition.EndTime);

                    if (!actions.Any())
                        continue;

                    var sb = new ScriptBuilder();

                    foreach (var action in actions)
                    {
                        sb.AddCommandMillis(
                            millis: Convert.ToInt32(action.at - galleryDefinition.StartTime - sb.TotalTime),
                            value: action.pos);
                    }

                    var gallery = new GalleryIndex
                    {
                        Name = galleryDefinition.Name,
                        Variant = variant,
                        Duration = Convert.ToInt32(galleryDefinition.EndTime - galleryDefinition.StartTime)
                    };
                    sb.TrimTimeTo(gallery.Duration);

                    gallery.Commands = sb.Generate();


                    if (!Galleries.ContainsKey(galleryDefinition.Name))
                        Galleries.Add(galleryDefinition.Name, new List<GalleryIndex>());

                    bundler.Add(gallery, galleryDefinition.Loop, true);
                    Galleries[gallery.Name].Add(gallery);
                }


            }
            Assets = bundler.GenerateBundle();
        }

        public List<string> GetNames()
            => Galleries.Keys.ToList();
        public List<string> GetVariants()
            => Variants;


        public GalleryIndex Get(string name, string variant = null)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 
            variant = variant ?? Config.SelectedVariant ?? Config.DefaulVariant;

            var variants = Galleries.GetValueOrDefault(name);

            if (variant is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault(x => x.Variant == Config.SelectedVariant)
                        ?? variants.FirstOrDefault();
            return gallery;

        }

        
    }
}
