using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;
using NAudio.Midi;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Serialization;

namespace Edi.Core
{
    public class Repacker
    {
        private readonly IEdi edi;
        private string basePath => edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath;
        private string outputFilePath;
        private string outputVideoName;
        private List<DefinitionGallery> galleries =  new();
        
        private string targetFolderPath;
        private DefinitionRepository defRepo => edi.GetRepository<DefinitionRepository>();
        private FunscriptRepository funRepo => edi.GetRepository<FunscriptRepository>();

        public async Task Repack(string videoName = "", List<DefinitionGallery>? _galleries = null)
        {
            galleries = _galleries ?? edi.Definitions.ToList();
           
          /*
            await CutVideosAsync();
            foreach (var key in galleries.Select(x => x.FileName.Substring(0, 1)).Distinct().ToList())
            {
                _galleries = galleries.Where(x => x.FileName.StartsWith(key)).ToList();
                await GenerateFileListForConcatenation(key, _galleries);
                await GenerateVideo(key);
                await ApplyDurationFromVideos(key, _galleries);
                WriteFunscripts(key, _galleries);
            }
            WriteDefinitionsCsv();
            */

            //;
            // Crear una lista para FFmpeg
            /*
                        var videoTask = CutVideosAsync();

                        await ApplyDurationFromVideos();

                        WriteFunscripts();
                        WriteDefinitionsCsv();
            */
            //await videoTask;

        }

       
        public Repacker (IEdi edi)
        {
            this.edi = edi;
        }

        public async Task CutVideosAsync()
        {
            foreach (var gallery in galleries)
            {
                string inputFile = Path.Combine(basePath, $"videos\\{gallery.FileName}.mp4");
                string outputFile = Path.Combine(basePath, $"{gallery.Name}.mp4");

                await CutVideoSegment(inputFile, outputFile, gallery.StartTime, gallery.EndTime);
            }
        }


        private async Task CutVideoSegment(string inputPath, string outputPath, long startTime, long endTime)
        {
            string startTimeFormatted = TimeSpan.FromMilliseconds(startTime).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            string durationFormatted = TimeSpan.FromMilliseconds(endTime - startTime).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

            string ffmpegCmd = $"-y -ss {startTimeFormatted} -i \"{inputPath}\" -t {durationFormatted}  -c:v libx264 -c:a aac \"{outputPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegCmd,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            await process.WaitForExitAsync();
        }

        private async Task GenerateFileListForConcatenation(string key, List<DefinitionGallery> _galleries)
        {
            string fileListPath = Path.Combine(basePath, $"{key}-filelist.txt");
            using (var writer = new StreamWriter(fileListPath, false))
            {
                foreach (var gallery in _galleries.Where(x => x.FileName.StartsWith(key)))
                {
                    string videoFilePath = Path.Combine(basePath, $"{gallery.Name}.mp4").Replace("\\", "/");
                    await writer.WriteLineAsync($"file '{videoFilePath}'");
                }
            }

        }

        private async Task GenerateVideo(string key)
        {
            string fileListPath = Path.Combine(basePath + $"\\{key}-filelist.txt").Replace("\\", "/");

            // Asegúrate de que las rutas estén correctamente escapadas para el comando.
            string outputPath = Path.Combine(basePath, $"{key}.mp4").Replace("\\", "/");

            var ffmpegCmd = $"-y -f concat -safe 0 -i \"{fileListPath}\" -c copy \"{outputPath}\"";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegCmd,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            await process.WaitForExitAsync();
        }
        public async Task<int> GetVideoDuration(string videoPath)
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{videoPath.Replace("\\", "/")}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = process.StandardError.ReadToEnd();
            await process.WaitForExitAsync();

            var durationMatch = System.Text.RegularExpressions.Regex.Match(output, @"Duration: (\d{2}):(\d{2}):(\d{2}\.\d{2})");
            if (durationMatch.Success)
            {
                int hours = int.Parse(durationMatch.Groups[1].Value);
                int minutes = int.Parse(durationMatch.Groups[2].Value);
                double seconds = double.Parse(durationMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return (int)((hours * 3600 + minutes * 60 + seconds) * 1000);  // Convertir a milisegundos
            }
            return 0;
        }



        private async Task ApplyDurationFromVideos(string key,List<DefinitionGallery> _galleries)
        {
            long accumulatedTime = 0;

            foreach (var gallery in _galleries)
            {
                var originalDuration = gallery.Duration;
                var duration = await GetVideoDuration(Path.Combine(basePath, $"{gallery.Name}.mp4"));  // Obtener la duración en milisegundos
                gallery.StartTime = accumulatedTime;
                accumulatedTime += duration;
                gallery.EndTime = gallery.StartTime + originalDuration;
                gallery.FileName = key;
            }
        }

        private void WriteDefinitionsCsv()
        {
            using (var writer = new StreamWriter(Path.Combine(basePath, "definitions.csv")))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.WriteRecords(
                    galleries
                    .Select(x => new DefinitionWriteDto 
                    { 
                        Name = x.Name,
                        FileName = x.FileName,
                        StartTime = TimeSpan.FromMilliseconds(x.StartTime).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
                        EndTime = TimeSpan.FromMilliseconds(x.EndTime).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
                        Type = x.Type,
                        Loop = x.Loop.ToString()
                    })
                );
            }
        }


        private void WriteFunscripts(string Key, List<DefinitionGallery> _galleries)
        {
            var isFirst = true;
            var variants = funRepo.GetVariants();
            foreach (var varian in variants)
            {
                var sb = new ScriptBuilder();

                foreach (var gallery in _galleries)
                {
                    var funscipt = funRepo.Get(gallery.Name, varian);
                    sb.addCommands(funscipt?.Commands);
                    sb.TrimTimeTo(gallery.EndTime);
                }
                var actions = sb.Generate();
                new FunScriptFile()
                {
                    actions = actions.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = (int)Math.Round(x.Value) }).ToList(),
                    metadata = new()
                    {
                        chapters = _galleries.Select(x => new FunScriptChapter
                        {
                            name = x.Name,
                            StartTimeMilis = x.StartTime,
                            EndTimeMilis = x.EndTime
                        }).ToList()
                    }
                }.Save(Path.Combine(basePath, $"{Key}{(isFirst ? "" : $".{varian}")}.funscript"));
                isFirst = false;
            }
        }


    }
}