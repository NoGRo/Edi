using Edi.Core.Funscript;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        public virtual IEnumerable<string> Reserve => Array.Empty<string>();

        protected List<string> Variants { get; set; } = new List<string>();
        protected string defualtVariant => "default";
        protected Dictionary<string, List<T>> Galleries { get; set; } = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        protected DefinitionRepository Definition { get; }


        public virtual T Get(string name, string? variant = null)
        {
            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null || string.IsNullOrEmpty(variant) || variant == "None")
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
            var Assets = this.Discover(path);
            Read(Assets);
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
            Variants.Add("None");
            ReadEnd();
        }
        protected virtual void ReadEnd() { }

        protected virtual void ReadGalleries(DefinitionGallery DefinitionGallery, IEnumerable<AssetEdi> assets)
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
        public abstract T? ReadGallery(AssetEdi asset, DefinitionGallery definition);        


    }
}
