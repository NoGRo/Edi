
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using System;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
using System.Xml.Linq;
using NAudio.Dmo;
using System.Security.Cryptography.X509Certificates;

namespace Edi.Core.Gallery.Index
{
    public class IndexRepository : IGalleryRepository<IndexGallery>
    {
        public IndexRepository(ConfigurationManager configuration, GalleryBundler bundler, FunscriptRepository Cmdlineals,DefinitionRepository definitionRepository)
        {
            Config = configuration.Get<GalleryConfig>();
            Bundler = bundler;
            this.funRepo = Cmdlineals;
            DefinitionRepository = definitionRepository;
        }
        public IEnumerable<string> Accept => new[] { "BundleDefinition*.txt" };
        private Dictionary<string, Dictionary<string, List<IndexGallery>>> Galleries { get; set; } = new Dictionary<string, Dictionary<string, List<IndexGallery>>>(StringComparer.OrdinalIgnoreCase);

        public GalleryConfig Config { get; set; }
        private GalleryBundler Bundler { get; set; } 
        public FunscriptRepository funRepo { get; }
        public DefinitionRepository DefinitionRepository { get; }

        public async Task Init()
        {
            LoadGallery();
        }

        public FileInfo GetBundle(string variant, string format) 
            => new FileInfo($"{Edi.OutputDir}/bundle.{variant}.{format}");

        private void LoadGallery()
        {
            Galleries.Clear();
            foreach (var variant in GetVariants())
            {
                var bundleConfigs = GetBundleDefinition(variant);

                foreach (var bundle in bundleConfigs)
                {

                    var finalGallery = funRepo.GetAll().Where(x => x.Variant == Config.DefaulVariant
                                                                && bundle.Galleries.Contains(x.Name))
                                                       .ToDictionary(x => x.Name, x => x);

                    var variantGalleries = funRepo.GetAll().Where(x => x.Variant == variant);

                    variantGalleries = variantGalleries.Where(x => bundle.Galleries.Contains(x.Name));

                    foreach (var funscriptGallery in variantGalleries)
                    {
                        if (finalGallery.ContainsKey(funscriptGallery.Name))
                            finalGallery[funscriptGallery.Name] = funscriptGallery;
                        else
                            finalGallery.Add(funscriptGallery.Name, funscriptGallery);
                    }

                    Bundler.Clear();
                    if (!Galleries.ContainsKey(variant))
                        Galleries.Add(variant, new Dictionary<string, List<IndexGallery>>(StringComparer.OrdinalIgnoreCase));

                    var sortedGalleries = finalGallery.Values;

                    foreach (var gallery in sortedGalleries)
                    {
                        if (!Galleries[variant].ContainsKey(gallery.Name))
                            Galleries[variant].Add(gallery.Name, new() { Bundler.Add(gallery, bundle.BundleName) });
                        else
                            Galleries[variant][gallery.Name].Add(Bundler.Add(gallery, bundle.BundleName));
                    }
                    Bundler.GenerateBundle($"{bundle.BundleName}.{variant}");
                }
            }


        }
        private List<BundleDefinition> GetBundleDefinition(string variant)
        {
            var bundlesDefault = new BundleDefinition() { Galleries = DefinitionRepository.GetAll().Select(x => x.Name).Distinct().ToList() };


            var GalleryDir = new DirectoryInfo(Config.GalleryPath);


            var BundleDefinition = GalleryDir.EnumerateFiles("BundleDefinition*.txt").ToList();
            BundleDefinition.AddRange(GalleryDir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles("BundleDefinition*.txt")));

            if (!BundleDefinition.Any())
                return new List<BundleDefinition>() { bundlesDefault };

            var Default = BundleDefinition.FirstOrDefault(X => X.Name.ToLower().Equals("bundledefinition.txt"));
            var Variant = BundleDefinition.FirstOrDefault(X => X.Name.ToLower().Equals($"bundledefinition.{variant}.txt"));

            if (Default is null && Variant is null)
                return new List<BundleDefinition>() { bundlesDefault };

            
            var definitionPath = Variant?.FullName ?? Default.FullName;

            List<BundleDefinition> bundles = ReadBundleConfig(definitionPath);


            var inBundles = bundles.Where(x => x.BundleName != "default").SelectMany(x => x.Galleries).ToHashSet();
            var inDefualt = bundles.Where(x => x.BundleName == "default").SelectMany(x => x.Galleries).ToHashSet();

            bundlesDefault.Galleries = bundlesDefault.Galleries.Where(x => inDefualt.Contains(x) || !inBundles.Contains(x)).ToList();

            bundles.RemoveAll(x => x.BundleName == "default");
            bundles.Add(bundlesDefault);

            return bundles;


        }

        private static List<BundleDefinition> ReadBundleConfig(string definitionPath)
        {
            var bundles = new List<BundleDefinition>();
            BundleDefinition currentBundle = null;
            foreach (var line in File.ReadLines(definitionPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue; // Skip comments and empty lines

                if (line.StartsWith("-"))
                {
                    // New bundle
                    if (currentBundle != null)
                        bundles.Add(currentBundle);

                    currentBundle = new BundleDefinition
                    {
                        BundleName = line.TrimStart('-').Trim(),
                        Galleries = new List<string>()
                    };
                }
                else if (currentBundle != null)
                {
                    // Add gallery to current bundle
                    currentBundle.Galleries.Add(line.Trim());
                }
            }

            // Add the last bundle
            if (currentBundle != null)
                bundles.Add(currentBundle);
            return bundles;
        }

        public List<string> GetVariants()
            => funRepo.GetVariants();
        public List<IndexGallery> GetAll()
            => Galleries.Values.SelectMany(x => x.Values.SelectMany(y=> y)).ToList();
        public  IndexGallery? Get(string name, string variant = null)
            => Get(name, variant,"default");
        public IndexGallery? Get(string name, string variant, string bundle = "default")
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 
            variant = variant ?? Config.SelectedVariant ?? Config.DefaulVariant;

            var galls = Galleries.GetValueOrDefault(variant)?.GetValueOrDefault(name);
            
            return galls?.Find(x => x.Bundle == bundle) ?? galls?.FirstOrDefault();


        }


    }
}
