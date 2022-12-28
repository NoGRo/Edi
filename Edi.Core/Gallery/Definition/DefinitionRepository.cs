
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Index;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionRepository : IGalleryRepository<DefinitionGallery>
    {
        public DefinitionRepository(IConfiguration configuration, GalleryBundler bundler)
        {
            Config = new GalleryConfig();
            configuration.GetSection("Gallery").Bind(Config);

            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            var csvFile = new FileInfo($"{GalleryPath}Definitions.csv");
            if (!csvFile.Exists)
                return;

            Assets.Add("csv", csvFile);
            using (var reader = csvFile.OpenText())
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                Config.Definitions = csv.GetRecords<DefinitionGallery>().ToList();
            }
            Bundler = bundler;
        }

        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } = new List<string>();
        private GalleryConfig Config { get; set; }
        private GalleryBundler Bundler { get; set; }

        public async Task Init()
        {

        }
        public List<string> GetVariants()
            => Variants;
        public List<DefinitionGallery> GetAll()
            => Config.Definitions;

        public DefinitionGallery? Get(string name, string variant = null)
            => Config.Definitions.FirstOrDefault(x => x.Name == name);


    }
}
