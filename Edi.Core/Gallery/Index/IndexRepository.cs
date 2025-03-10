
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using System;
using Edi.Core.Gallery.Definition;
using System.Xml.Linq;
using NAudio.Dmo;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Gallery.Index
{
    public class IndexRepository : IGalleryRepository<IndexGallery>
    {
        public IndexRepository(ConfigurationManager configuration, GalleryBundler bundler, FunscriptRepository Cmdlineals, DefinitionRepository definitionRepository)
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

        public async Task Init(string path)
        {
            LoadGallery(path);
        }

        public FileInfo GetBundle(string variant, string format)
            => new FileInfo($"{Edi.OutputDir}/bundle.{variant}.{format}");

        private void LoadGallery(string path)
        {
            Galleries.Clear();
            foreach (var variant in GetVariants())
            {
                var bundleConfigs = GetBundleDefinition(variant, path);
                var variantGalleries = funRepo.GetAll().Where(x => x.Variant == variant).ToList();

                foreach (var bundle in bundleConfigs)
                {

                    var finalGallery = funRepo.GetAll().Where(x => x.Variant == "default"
                                                                && bundle.Galleries.Contains(x.Name))
                                                       .ToDictionary(x => x.Name, x => x);

                    

                    var variantbundleGalleries = variantGalleries.Where(x => bundle.Galleries.Contains(x.Name)).ToList();

                    foreach (var funscriptGallery in variantbundleGalleries)
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
                        IndexGallery indexGallery = Bundler.Add(gallery, bundle.BundleName);

                        if (!Galleries[variant].ContainsKey(gallery.Name))
                            Galleries[variant].Add(gallery.Name, new() { indexGallery });
                        else
                            Galleries[variant][gallery.Name].Add(indexGallery);
                        
                    }
                    Bundler.GenerateBundle($"{bundle.BundleName}.{variant}");
                }
            }


        }
        private List<BundleDefinition> GetBundleDefinition(string variant,string path)
        {
            var bundlesDefault = new BundleDefinition() { Galleries = DefinitionRepository.GetAll().Select(x => x.Name).Distinct().ToList() };


            var GalleryDir = new DirectoryInfo(path);
            if (GalleryDir?.Exists != true)
                return new();

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
            => Galleries.Values.SelectMany(x => x.Values.SelectMany(y => y)).ToList();
        public IndexGallery? Get(string name, string variant = null)
            => Get(name, variant, "default");
        public IndexGallery? Get(string name, string variant, string bundle)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 
            
            var galls = Galleries.GetValueOrDefault(variant)?.GetValueOrDefault(name);

            return galls?.Find(x => x.Bundle == bundle) ?? galls?.FirstOrDefault();


        }


    }
}
