using Edi.Core.Funscript;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Definition;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public abstract class RepositoryBase<T> : IGalleryRepository<T>
        where T : class, IGallery
    {
        private readonly ILogger _logger;

        protected RepositoryBase(DefinitionRepository definition, ILogger logger)
        {
            Definition = definition;
            _logger = logger;
            _logger.LogInformation($"RepositoryBase initialized for {this.GetType().Name}.");
        }

        public abstract IEnumerable<string> Accept { get; }
        public virtual IEnumerable<string> Reserve => Array.Empty<string>();

        protected List<string> Variants { get; set; } = new List<string>();
        protected string defualtVariant => "default";
        protected Dictionary<string, List<T>> Galleries { get; set; } = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        protected DefinitionRepository Definition { get; }

        public virtual T Get(string name, string? variant = null)
        {
            _logger.LogInformation($"Fetching gallery with Name: {name}, Variant: {variant}.");
            var variants = Galleries.GetValueOrDefault(name);

            if (string.IsNullOrEmpty(variant) || variant == "None")
            {
                _logger.LogWarning($"Gallery not found for Name: {name}, Variant: {variant}.");
                return null;
            }

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault();

            if (gallery is null)
            {
                _logger.LogWarning($"No matching gallery found for Name: {name}, Variant: {variant}.");
            }

            return gallery;
        }

        public virtual List<T> GetAll()
        {
            //_logger.LogInformation($"Fetching all galleries for {this.GetType().Name}.");
            return Galleries.Values.SelectMany(x => x).ToList();
        }

        public virtual List<string> GetVariants()
        {
            _logger.LogInformation("Fetching available variants.");
            return Variants;
        }

        public async Task Init(string path)
        {
            _logger.LogInformation($"Initializing for {this.GetType().Name} with path: {path}.");
            try
            {
                var assets = this.Discover(path);
                _logger.LogInformation($"Discovered {assets.Count} assets in path: {path}.");
                Read(assets);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during for {this.GetType().Name} initialization: {ex.Message}");
                throw;
            }
        }

        protected virtual void Read(List<AssetEdi> Assets)
        {
            //_logger.LogInformation("Reading assets and building galleries.");

            Galleries.Clear();
            Variants.Clear();

            foreach (var definitionGallery in Definition.GetAll())
            {
                //_logger.LogInformation($"Processing DefinitionGallery: {definitionGallery.Name}.");

                Galleries.Add(definitionGallery.Name, new List<T>());

                var assets = Assets.Where(x => x.Name == definitionGallery.FileName);
                ReadGalleries(definitionGallery, assets);
            }

            Variants = Galleries.SelectMany(x => x.Value.Select(y => y.Variant)).Distinct().ToList();
            Variants.Add("None");
            _logger.LogInformation($"Variants discovered: {string.Join(", ", Variants)}.");
            ReadEnd();
        }

        protected virtual void ReadEnd()
        {
            _logger.LogInformation($"Finished reading galleries for {this.GetType().Name}.");
        }

        protected virtual void ReadGalleries(DefinitionGallery definitionGallery, IEnumerable<AssetEdi> assets)
        {
            //_logger.LogInformation($"Reading galleries for DefinitionGallery: {definitionGallery.Name} with {assets.Count()} assets.");

            foreach (var asset in assets)
            {
                try
                {
                    var gallery = ReadGallery(asset, definitionGallery);
                    if (gallery != null)
                    {
                        gallery.Variant = asset.Variant;
                        Galleries[definitionGallery.Name].Add(gallery);
                        _logger.LogInformation($"Gallery added for {typeof(T).Name}: {definitionGallery.Name}, Variant: {asset.Variant}.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error reading gallery for asset: {asset.Name}. Exception: {ex.Message}");
                }
            }
        }

        public abstract T? ReadGallery(AssetEdi asset, DefinitionGallery definition);
    }
}
