using Edi.Core.Funscript;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public abstract class RepositoryBase<T> : IGalleryRepository<T>
        where T : class, IGallery
    {
        protected RepositoryBase(DefinitionRepository definition)
        {
            Definition = definition;
        }

        public abstract IEnumerable<string> Accept { get; }
        protected List<string> Variants { get; set; } = new List<string>();
        protected string defualtVariant => "default";
        protected Dictionary<string, List<T>> Galleries { get; set; } = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        protected DefinitionRepository Definition { get; }


        public virtual T Get(string name, string? variant = null)
        {
            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault();


            if (gallery is null)
                return null;

            return gallery;
        }

        public virtual List<T> GetAll()
            => Galleries.Values.SelectMany(x => x).ToList();

        public virtual List<string> GetVariants()
        {
            return Variants;
        }

        public async Task Init(string path)
        {
            var Assets = Discover(path);
            Read(Assets);
        }

        public  List<AssetEdi> Discover(string path)
        {

            var GalleryDir = new DirectoryInfo(path);
            var files = new List<FileInfo>();
            foreach (var item in Accept)
            {
                var mask = item.Contains("*.") ? item : $"*.{item}";
                files.AddRange(GalleryDir.EnumerateFiles(mask));
                files.AddRange(GalleryDir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles(mask)));
            }

            var assetEdis = new List<AssetEdi>();
            foreach (var file in files)
            {
                var fileName = DiscoverExtencion.variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["name"].Value;
                var variant = DiscoverExtencion.variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["variant"].Value;

                var pathSplit = file.FullName.Replace(GalleryDir.FullName + "\\", "").Split('\\');
                var pathVariant = pathSplit.Length > 1 ? pathSplit[0] : null;
                variant = !string.IsNullOrEmpty(variant)
                                        ? variant
                                        : pathVariant ?? defualtVariant;

                assetEdis.Add(new(file, fileName, variant));
            }

            return assetEdis.DistinctBy(x => x.Variant).ToList();
        }
        protected virtual void Read(List<AssetEdi> Assets)
        {
            Galleries.Clear();
            Variants.Clear();

            foreach (var DefinitionGallery in Definition.GetAll())
            {
                Galleries.Add(DefinitionGallery.Name, new List<T>());

                var assets = Assets.Where(x => x.Name == DefinitionGallery.FileName);

                ReadGalleries(DefinitionGallery, assets);
            }

            Variants = Galleries.SelectMany(x => x.Value.Select(y => y.Variant)).Distinct().ToList();

            ReadEnd();
        }
        protected virtual void ReadEnd() { }
        protected virtual void ReadGalleries(DefinitionGallery DefinitionGallery, IEnumerable<RepositoryBase<T>.AssetEdi> assets)
        {
            foreach (var asset in assets)
            {
                var gallery = ReadGallery(asset, DefinitionGallery);
                if (gallery != null)
                {
                    gallery.Variant = asset.Variant;
                    Galleries[DefinitionGallery.Name].Add(gallery);
                }
            }
        }
        public abstract T ReadGallery(AssetEdi asset, DefinitionGallery definition);
        
        public record AssetEdi(FileInfo File, string Name, string Variant);


    }
}
