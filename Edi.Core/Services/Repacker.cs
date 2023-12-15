using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Funscript;
using Edi.Core.Gallery;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Gallery.Definition;

namespace Edi.Core
{
    public class Repacker
    {
        private readonly IEdi edi;
        private string basePath => edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath;
        private string outputFilePath;
        private string outputVideoName;
        private List<DefinitionGallery> galleries =  new();

        public Repacker (IEdi edi)
        {
            this.edi = edi;
        }
        private DefinitionRepository defRepo => edi.GetRepo<DefinitionRepository>();
        private FunscriptRepository funRepo => edi.GetRepo<FunscriptRepository>();

        public async Task Repack(string videoName = "", List<DefinitionGallery>? _galleries = null)
        {
            galleries = _galleries ?? edi.Definitions.ToList();
            outputVideoName = videoName;
            outputFilePath = $"{basePath}/{outputVideoName}";

            // Crear una lista para FFmpeg

            var videoTask = GenerateVideo();
            await ApplyDurationFromVideos();

            WriteFunscripts();
            WriteDefinitionsCsv();

            await videoTask;

        }


        public async Task<int> GetVideoDuration(string videoPath)
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i {videoPath}",
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
        private async Task GenerateVideo()
        {
            var fileListPath = $"{basePath}/filelist.txt";
            using (var writer = new StreamWriter(fileListPath))
            {
                foreach (var record in galleries)
                {
                    writer.WriteLine($"file '{record.Name}.mp4'");
                }
            }

            // Utilizar FFmpeg para concatenar los videos
            var ffmpegCmd = $"-f concat -safe 0 -i {fileListPath} -c copy {outputFilePath}.mp4";
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegCmd,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                }
            };
            process.Start();
            await process.WaitForExitAsync();
        }
        private async Task ApplyDurationFromVideos()
        {
            long accumulatedTime = 0;

            foreach (var record in galleries)
            {
                var duration = await GetVideoDuration($"{basePath}/{record.Name}.mp4");  // Obtener la duración en milisegundos

                record.StartTime = accumulatedTime;
                accumulatedTime += duration;
                record.EndTime = accumulatedTime;
                record.FileName = outputVideoName;
            }
        }

        private void WriteDefinitionsCsv()
        {
            using (var writer = new StreamWriter($"{outputFilePath}.csv"))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)))
            {
                csv.WriteRecords(galleries);
            }
        }

        private void WriteFunscripts()
        {
            var variants = funRepo.GetVariants();
            foreach (var varian in variants)
            {
                var sb = new ScriptBuilder();


                foreach (var record in galleries)
                {
                    var funscipt = funRepo.Get(record.Name, varian);
                    sb.addCommands(funscipt?.Commands);
                    sb.TrimTimeTo(record.EndTime);
                }
                var actions = sb.Generate();
                new FunScriptFile()
                {
                    actions = actions.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = (int)Math.Round(x.Value) }).ToList(),
                    metadata = new()
                    {
                        chapters = galleries.Select(x => new FunScriptChapter
                        {
                            name = x.Name,
                            startTime = $"{TimeSpan.FromMilliseconds(x.StartTime):hh\\:mm\\:ss\\.fff}",
                            endTime = $"{TimeSpan.FromMilliseconds(x.EndTime):hh\\:mm\\:ss\\.fff}"
                        }).ToList()
                    }
                }.Save($"{outputFilePath}.{varian}.funscript");
            }
        }


    }
}