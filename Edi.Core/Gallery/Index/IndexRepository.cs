
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;

namespace Edi.Core.Gallery.Index
{
    public class IndexRepository : IGalleryRepository<IndexGallery>
    {
        public IndexRepository(IConfiguration configuration, GalleryBundler bundler, FunscriptRepository Cmdlineals)
        {
            Config = new GalleryConfig();
            configuration.GetSection("Gallery").Bind(Config);
            Bundler = bundler;
            this.Cmdlineals = Cmdlineals;
        }

        private Dictionary<string, List<IndexGallery>> Galleries { get; set; } = new Dictionary<string, List<IndexGallery>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        public GalleryConfig Config { get; set; }
        private GalleryBundler Bundler { get; set; } 
        public FunscriptRepository Cmdlineals { get; }

        public async Task Init()
        {
            LoadGallery();
        }
        
        private void LoadGallery()
        {
            var CmdGalleries = Cmdlineals.GetAll();
            foreach (var cmdGallery in CmdGalleries)
            {
                if (!Galleries.ContainsKey(cmdGallery.Name))
                    Galleries.Add(cmdGallery.Name, new List<IndexGallery>());

                var index = Bundler.Add(cmdGallery, cmdGallery.Loop);
                Galleries[cmdGallery.Name].Add(index);
            }
            Assets = Bundler.GenerateBundle();
        }

        public List<string> GetVariants()
            => Cmdlineals.GetVariants();
        public List<IndexGallery> GetAll()
            => Galleries.Values.SelectMany(x => x).ToList();


        public IndexGallery? Get(string name, string variant = null)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 
            variant = variant ?? Config.SelectedVariant ?? Config.DefaulVariant;

            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault(x => x.Variant == Config.SelectedVariant)
                        ?? variants.FirstOrDefault();
            return gallery;

        }


    }
}
