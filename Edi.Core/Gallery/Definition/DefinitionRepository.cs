
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Configuration;
using System.Reflection.Metadata.Ecma335;

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
        private Dictionary<string, DefinitionGallery> dicDefinitions { get; set; } = new Dictionary<string, DefinitionGallery>(StringComparer.OrdinalIgnoreCase);

        public async Task Init()
        {
            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            var csvFile = new FileInfo($"{GalleryPath}Definitions.csv");
            if (!csvFile.Exists)
                return;

            List<DefinitionDto> definitionsDtos;
            Assets.Add("csv", csvFile);
            using (var reader = csvFile.OpenText())
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                definitionsDtos = csv.GetRecords<DefinitionDto>().ToList();
            }
            int linesCount = 0;

            foreach (var definitionDto in definitionsDtos)
            {
                linesCount++;
                var def = new DefinitionGallery
                {
                    Name = definitionDto.Name,
                    FileName = definitionDto.FileName,
                    Type = definitionDto.Type,
                    Loop = definitionDto.Loop,
                };

                long time;
                if (parseTimeField(definitionDto.StartTime, out time))
                    def.StartTime = time;
                else
                    throw new Exception($"Can't convert the value StartTime: [{def.StartTime}] to a valid TimeSpan, in line [{linesCount}] gallery name [{def.Name}] of csv definition file. use format: (22:50:30.333) hh:mm:ss.nnn");

                if (parseTimeField(definitionDto.EndTime, out time))
                    def.EndTime = time;
                else
                    throw new Exception($"Can't convert the value EndTime: [{def.EndTime}] to a valid Time, in line [{linesCount}] gallery name [{def.Name}] of csv definition file. use format: (22:50:30.333) hh:mm:ss.nnn");

                if (dicDefinitions.ContainsKey(def.Name))
                    throw new Exception($"Can't have two galleries with the same name, check [{def.Name}] duplicate in line [{linesCount}]");

                dicDefinitions.Add(def.Name, def);
            }
        }
        

        private bool parseTimeField(string field, out long millis)
        {
            millis = 0;
            if (string.IsNullOrEmpty(field)) 
                return false;
            try
            {
                millis = field.Contains(":") || field.Contains(".")
                    ? Convert.ToInt64(TimeSpan.Parse(field, DateTimeFormatInfo.InvariantInfo).TotalMilliseconds)
                    : long.Parse(field);
            }
            catch { return false; }

            return true;
        }
        public List<string> GetVariants()
            => Variants;
        public List<DefinitionGallery> GetAll()
            => dicDefinitions.Values.ToList();

        public DefinitionGallery? Get(string name, string variant = null)
            => dicDefinitions.GetValueOrDefault(name);


    }
}
