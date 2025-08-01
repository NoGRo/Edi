using System.Text.Json;
using System.Globalization;
using CsvHelper;
using System.Reflection.Metadata.Ecma335;
using Edi.Core.Device.Handy;
using Edi.Core.Funscript;
using System.Text.RegularExpressions;

namespace Edi.Core.Gallery.Definition
{
    public class DefinitionRepository : IGalleryRepository<DefinitionGallery>
    {


        public DefinitionRepository(ConfigurationManager configuration)
        {
            Config = configuration.Get<GalleryConfig>();
            //Init(null).GetAwaiter().GetResult();
        }
        private List<string> Variants { get; set; } = new List<string>();
        public GalleryConfig Config { get; set; }
        private Dictionary<string, DefinitionGallery> dicDefinitions { get; set; } = new Dictionary<string, DefinitionGallery>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Accept => new[] { "Definitions.csv", "Definitions_auto.csv" };

        public bool IsInitialized {  get; set; }

        public async Task Init(string path)
        {
            path = path ?? Config.GalleryPath;
            var GalleryPath = $"{path}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            var csvFile = new FileInfo($"{GalleryPath}Definitions.csv");

            if (!csvFile.Exists) {
                GenerateDefinitions(GalleryPath);
                csvFile = new FileInfo($"{GalleryPath}Definitions_auto.csv");
                if (!csvFile.Exists)
                    return;
            }

            List<DefinitionReadDto> definitionsDtos;

            using (var reader = csvFile.OpenText())
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {

                definitionsDtos = csv.GetRecords<DefinitionReadDto>().ToList();
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
                    Description = definitionDto.Description?.Trim(),
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
            IsInitialized = true;
        }
        
        private void GenerateDefinitions(string GalleryPath)
        { 
            var dir = new DirectoryInfo(GalleryPath);
            var funscriptsFiles = dir.EnumerateFiles("*.funscript").ToList();

            funscriptsFiles.AddRange(dir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles("*.funscript")));

            if (!funscriptsFiles.Any())
                return;


            funscriptsFiles = funscriptsFiles.DistinctBy(x => x.Name).ToList();


            var newDefinitionFile = new List<DefinitionWriteDto>();
                
            foreach (var file in funscriptsFiles)  
            {

                Match matchFile = DiscoverExtension.variantRegex.Match(Path.GetFileNameWithoutExtension(file.FullName));
                var fileName = matchFile.Groups["name"].Value;
                var loop = matchFile.Groups["loop"].Value.ToLower() != "nonloop" ? "true" : "false";
                var type = matchFile.Groups["type"].Success ? matchFile.Groups["type"].Value.ToLower() : "gallery";

                var funscript = FunScriptFile.Read(file.FullName);
                if (Config.GenerateDefinitionFromChapters && funscript?.metadata?.chapters?.Any() == true )
                {
                    newDefinitionFile.AddRange(
                        funscript.metadata.chapters.Select(x => 
                        {

                            var mathChapter = DiscoverExtension.loopRegex.Match(Path.GetFileNameWithoutExtension(x.name));
                            return new DefinitionWriteDto
                            {
                                Name = mathChapter.Groups["name"].Value,
                                FileName = fileName,
                                Type = mathChapter.Groups["type"].Success ? mathChapter.Groups["type"].Value.ToLower() : type,
                                Loop = mathChapter.Groups["loop"].Success
                                        ? mathChapter.Groups["loop"].Value?.ToLower() != "nonloop" ? "true" : "false"
                                        : loop,
                                StartTime = x.startTime,
                                EndTime = x.endTime,
                            };
                        }).ToArray());
                    
                }
                else if(funscript?.actions.Any() == true)
                {
                    newDefinitionFile.Add( new() {
                            Name = fileName,
                            FileName = fileName,
                            Type = type,
                            Loop = loop,
                            StartTime = "0",
                            EndTime = (funscript?.actions?.Max(x => x.at) ?? 0).ToString(),
                    });
                }
            }

            //Detect Variants
            newDefinitionFile = newDefinitionFile.DistinctBy(x => x.Name).ToList();

            using (var csv = new CsvWriter(new FileInfo($"{GalleryPath}Definitions_auto.csv").CreateText(), CultureInfo.InvariantCulture))
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

        public DefinitionGallery Get(string name, string variant = null)
            => dicDefinitions.GetValueOrDefault(name);


    }
}
