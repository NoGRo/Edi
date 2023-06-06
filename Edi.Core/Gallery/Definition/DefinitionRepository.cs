
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Configuration;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionRepository : IGalleryRepository<DefinitionGallery>
    {
        public DefinitionRepository(IConfiguration configuration)
        {
            Config = new GalleryConfig();
            configuration.GetSection(GalleryConfig.Secction).Bind(Config);


        }

        public Dictionary<string, FileInfo> Assets { get; set; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        private List<string> Variants { get; set; } = new List<string>();
        private GalleryConfig Config { get; set; }


        public async Task Init()
        {
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
        }
        public List<string> GetVariants()
            => Variants;
        public List<DefinitionGallery> GetAll()
            => Config.Definitions;

        public DefinitionGallery? Get(string name, string variant = null)
            => Config.Definitions.FirstOrDefault(x => x.Name == name);


    }
}
