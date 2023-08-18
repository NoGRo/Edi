
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using System.Reflection.Metadata.Ecma335;
using Edi.Core.Device.Handy;
using Edi.Core.Funscript;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionRepository : IGalleryRepository<DefinitionGallery>
    {
        public DefinitionRepository(ConfigurationManager configuration)
        {
            Config = configuration.Get<GalleryConfig>(); 
        }
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
                GenerateDefinitions(GalleryPath);

            if (!csvFile.Exists)
                return;

            List<DefinitionDto> definitionsDtos;

            using (var reader = csvFile.OpenText())
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                definitionsDtos = csv.GetRecords<DefinitionDto>().ToList();
            }
            int linesCount = 0;

            dicDefinitions.Clear();

            foreach (var definitionDto in definitionsDtos)
            {
                linesCount++;
                var def = new DefinitionGallery
                {
                    Name = definitionDto.Name,
                    FileName = definitionDto.FileName.Trim(),
                    Type = definitionDto.Type.ToLower().Trim(),
                    Loop = definitionDto.Loop.ToLower().Trim() == "true",
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
        
        private void GenerateDefinitions(string GalleryPath)
        { 
            var dir = new DirectoryInfo(GalleryPath);
            var funscriptsFiles = dir.EnumerateFiles("*.funscript");

            funscriptsFiles.Concat(dir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles("*.funscript")));

            if (!funscriptsFiles.Any())
                return;

            funscriptsFiles = funscriptsFiles.DistinctBy(x => x.Name);

            var newDefinitionFile = funscriptsFiles.Select(file =>
                new DefinitionDto
                {
                    Name = Path.GetFileNameWithoutExtension(file.FullName),
                    FileName = Path.GetFileNameWithoutExtension(file.FullName),
                    Type = "gallery",
                    Loop = "true",
                    StartTime = "0",
                    EndTime = (FunScriptFile.Load(file.FullName)?.actions.Max(x => x.at) ?? 0).ToString(),
                }
            );

            using (var csv = new CsvWriter(new FileInfo($"{GalleryPath}definitions.csv").CreateText(), CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(newDefinitionFile);
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
