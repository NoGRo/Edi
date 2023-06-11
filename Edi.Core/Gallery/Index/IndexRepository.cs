
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
using System.Xml.Linq;
using NAudio.Dmo;

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

        private Dictionary<string, Dictionary<string, IndexGallery>> Galleries { get; set; } = new Dictionary<string, Dictionary<string, IndexGallery>>(StringComparer.OrdinalIgnoreCase);

        public GalleryConfig Config { get; set; }
        private GalleryBundler Bundler { get; set; } 
        public FunscriptRepository Cmdlineals { get; }

        public async Task Init()
        {
            LoadGallery();
        }

        public FileInfo GetBundle(string variant, string format) 
            => new FileInfo($"{Edi.OutputDir}/bundle.{variant}.{format}");

        private void LoadGallery()
        {
            foreach (var variant in GetVariants())
            {

            
                var finalGallery = Cmdlineals.GetAll().Where(x => x.Variant == Config.DefaulVariant).ToDictionary(x => x.Name, x => x);

                var variantGalleries = Cmdlineals.GetAll().Where(x => x.Variant == variant);
                foreach (var funscriptGallery in variantGalleries)
                {
                    if (finalGallery.ContainsKey(funscriptGallery.Name))
                        finalGallery[funscriptGallery.Name] = funscriptGallery;
                    else
                        finalGallery.Add(funscriptGallery.Name, funscriptGallery);
                }

                Bundler.Clear();
                Galleries.Add(variant, new Dictionary<string, IndexGallery>(StringComparer.OrdinalIgnoreCase));
                foreach (var gallery in finalGallery.Values)
                {
                    Galleries[variant].Add(gallery.Name, Bundler.Add(gallery));
                   
                }
                Bundler.GenerateBundle(variant);
            }


        }

        public List<string> GetVariants()
            => Cmdlineals.GetVariants();
        public List<IndexGallery> GetAll()
            => Galleries.Values.SelectMany(x => x.Values).ToList();

        public IndexGallery? Get(string name, string variant = null)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 
            variant = variant ?? Config.SelectedVariant ?? Config.DefaulVariant;

            return Galleries.GetValueOrDefault(variant)?.GetValueOrDefault(name);


        }


    }
}
