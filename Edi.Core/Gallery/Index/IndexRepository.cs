
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
using Edi.Core.Services;

namespace Edi.Core.Gallery.Index
{
    public class IndexRepository : IGalleryRepository<IndexGallery>
    {
        public IndexRepository(ConfigurationManager configuration, GalleryBundler bundler, FunscriptRepository Cmdlineals, DefinitionRepository definitionRepository)
        {
            Config = configuration.Get<GalleryConfig>();
            BundlerConfig = configuration.Get<GalleryBundlerConfig>();
            Bundler = bundler;
            this.funRepo = Cmdlineals;
            DefinitionRepository = definitionRepository;
            //Init(Config.GalleryPath).GetAwaiter().GetResult();
        }
        public IEnumerable<string> Accept => new[] { "BundleDefinition*.txt" };
        private Dictionary<string, Dictionary<string, List<IndexGallery>>> Galleries { get; set; } = new Dictionary<string, Dictionary<string, List<IndexGallery>>>(StringComparer.OrdinalIgnoreCase);

        public GalleryConfig Config { get; set; }
        public GalleryBundlerConfig BundlerConfig { get; }
        private GalleryBundler Bundler { get; set; }
        private FunscriptRepository funRepo { get; }
        private DefinitionRepository DefinitionRepository { get; }

        public bool IsInitialized {set; get; }  

        public async Task Init(string path)
        {
            LoadGallery(path);
            IsInitialized = true;
        }

        public FileInfo GetBundle(string variant, string format)
            => new FileInfo($"{Edi.OutputDir}/Bundles/bundle.{variant}.{format}");

        public FileInfo GetBundle(
            string bundle,
            string variant,
            string format)
            => GetBundle(
                BundlerConfig.BundleVariantsTogether
                && !BundlerConfig.DisableBundler
                    ? $"{bundle}.variants"
                    : $"{bundle}.{variant}",
                format);

        private void LoadGallery(string path)
        {
            ClearOutputDirectory();

            Bundler.Clear();
            Galleries.Clear();
            var variants = GetVariants()
                .Where(variant => !variant.Equals(
                    "None",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (BundlerConfig.BundleVariantsTogether
                && !BundlerConfig.DisableBundler)
            {
                LoadVariantsTogether(variants, path);
                return;
            }

            foreach (var variant in variants)
            {

                var bundleConfigs = GetBundleDefinition(variant, path);
                var variantGalleries = funRepo.GetAll().Where(x => x.Variant == variant).ToList();

                if (BundlerConfig.DisableBundler)
                {
                    // Cada script se pone en su propio bundle individual
                    Bundler.Clear();
                    if (!Galleries.ContainsKey(variant))
                        Galleries.Add(variant, new Dictionary<string, List<IndexGallery>>(StringComparer.OrdinalIgnoreCase));

                    foreach (var gallery in variantGalleries)
                    {
                        // El nombre del bundle será igual al nombre del script
                        string bundleName = gallery.Name;
                        IndexGallery indexGallery = Bundler.Add(gallery, bundleName);

                        if (!Galleries[variant].ContainsKey(gallery.Name))
                            Galleries[variant].Add(gallery.Name, new() { indexGallery });
                        else
                            Galleries[variant][gallery.Name].Add(indexGallery);

                        Bundler.GenerateBundle($"{bundleName}.{variant}");
                    }
                    continue; // Saltar el procesamiento normal de bundles
                }
                foreach (var bundle in bundleConfigs)
                {

                    Bundler.Clear();
                    if (!Galleries.ContainsKey(variant))
                        Galleries.Add(variant, new Dictionary<string, List<IndexGallery>>(StringComparer.OrdinalIgnoreCase));

                    foreach (var gallery in GetBundleGalleries(
                                 variant,
                                 bundle))
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

        private void LoadVariantsTogether(
            IEnumerable<string> variants,
            string path)
        {
            var bundleEntries = new Dictionary<
                string,
                List<(string LookupVariant, FunscriptGallery Gallery)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var variant in variants)
            {
                Galleries.TryAdd(
                    variant,
                    new Dictionary<string, List<IndexGallery>>(
                        StringComparer.OrdinalIgnoreCase));

                foreach (var bundle in GetBundleDefinition(variant, path))
                {
                    if (!bundleEntries.TryGetValue(
                            bundle.BundleName,
                            out var entries))
                    {
                        entries = [];
                        bundleEntries.Add(bundle.BundleName, entries);
                    }

                    entries.AddRange(
                        GetBundleGalleries(variant, bundle)
                            .Select(gallery => (variant, gallery)));
                }
            }

            foreach (var (bundleName, entries) in bundleEntries)
            {
                Bundler.Clear();
                var sharedIndexes = new Dictionary<string, IndexGallery>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var (lookupVariant, gallery) in entries)
                {
                    var key = $"{gallery.Name}\u001f{gallery.Variant}";
                    if (!sharedIndexes.TryGetValue(key, out var index))
                    {
                        index = Bundler.Add(gallery, bundleName);
                        sharedIndexes.Add(key, index);
                    }

                    var galleriesByName = Galleries[lookupVariant];
                    if (!galleriesByName.TryGetValue(
                            gallery.Name,
                            out var indexes))
                    {
                        indexes = [];
                        galleriesByName.Add(gallery.Name, indexes);
                    }

                    if (!indexes.Contains(index))
                        indexes.Add(index);
                }

                Bundler.GenerateBundle($"{bundleName}.variants");
            }
        }

        private IEnumerable<FunscriptGallery> GetBundleGalleries(
            string variant,
            BundleDefinition bundle)
        {
            var galleries = funRepo.GetAll()
                .Where(gallery => gallery.Variant == "default"
                                  && bundle.Galleries.Contains(
                                      gallery.Name))
                .ToDictionary(gallery => gallery.Name, gallery => gallery);

            foreach (var gallery in funRepo.GetAll().Where(gallery =>
                         gallery.Variant == variant
                         && bundle.Galleries.Contains(gallery.Name)))
            {
                galleries[gallery.Name] = gallery;
            }

            return galleries.Values;
        }

        private static void ClearOutputDirectory()
        {
            var bundlesDir = Path.Combine(Edi.OutputDir, "Bundles");
            if (Directory.Exists(bundlesDir))
            {
                foreach (var file in Directory.GetFiles(bundlesDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(bundlesDir);
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
        public IndexGallery Get(string name, string variant = null)
            => Get(name, variant, "default");
        public IndexGallery Get(string name, string variant, string bundle)
        {
            var galls = Galleries.GetValueOrDefault(variant)?.GetValueOrDefault(name);
            return galls?.Find(x => x.Bundle == bundle) ?? galls?.FirstOrDefault();
        }


    }
}
