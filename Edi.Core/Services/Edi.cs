using System;
using System.Threading.Tasks;
using System.Timers;
using Edi.Core.Gallery;
using Edi.Core.Device.Interfaces;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery.Definition;
using NAudio.Wave.SampleProviders;
using CsvHelper;
using CsvHelper.Configuration;
using Edi.Core.Gallery.CmdLineal;
using Edi.Core.Funscript;

namespace Edi.Core
{
    public class Edi : IEdi
    {
        public  ConfigurationManager ConfigurationManager { get; set; }
        public DeviceManager DeviceManager { get; private set; }
        private readonly DefinitionRepository _repository;
        private readonly IEnumerable<IRepository> repos;
        private long resumePauseAt;

        public static string OutputDir => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Edi";

        public event IEdi.ChangeStatusHandler OnChangeStatus;


        public IEnumerable<IDevice> Devices => DeviceManager.Devices;
        public Edi(DeviceManager deviceManager, DefinitionRepository repository, IEnumerable<IRepository> repos, ConfigurationManager configuration)
        {
            if (!Directory.Exists(OutputDir))  
                Directory.CreateDirectory(OutputDir);
                
            DeviceManager = deviceManager;
            _repository = repository;
            this.repos = repos;
            

            TimerGalleryStop = new Timer();
            TimerGalleryStop.Elapsed += TimerGalleryStop_ElapsedAsync;

            TimerReactStop = new Timer();
            TimerReactStop.Elapsed += TimerReactStop_ElapsedAsync;
            ConfigurationManager = configuration;
            Config = configuration.Get<EdiConfig>();

        }

        public EdiConfig Config { get; set; }
        private string CurrentFiller { get; set; }
        private DefinitionGallery LastGallery { get; set; }
        private DateTime? GallerySendTime { get; set; }
        private DefinitionGallery? ReactSendGallery { get; set; }
        private Timer TimerGalleryStop { get; set; }
        private Timer TimerReactStop { get; set; }

        public IEnumerable<DefinitionGallery> Definitions => _repository.GetAll();

        public async Task Init()
        {
            //await _repository.Init();
            foreach (var repo in repos)
            {
                await repo.Init();
            }

            await DeviceManager.Init();
        }

        private void changeStatus(string message)
        {
            if (OnChangeStatus is null) return;
            OnChangeStatus($"[{DateTime.Now.ToShortTimeString()}] {message}");
        }

        public async Task Play(string name, long seek = 0)
        {

            var gallery = _repository.Get(name);

            if (gallery == null)
            {
                changeStatus($"Ignored not found [{name}]");
                return;
            }
            changeStatus($"recived [{name}] {gallery.Type}");

            switch (gallery.Type)
            {
                case "filler":
                    if (Config.Filler)
                    {
                        await SetFiller(gallery);
                    }
                    break;
                case "gallery":
                    if (Config.Gallery)
                    {
                        await SendGallery(gallery, seek);
                    }
                    break;
                case "reaction":
                    if (Config.Reactive)
                    {
                        await PlayReaction(gallery);
                    }
                    break;
                default:
                    break;
            }

        }

        private async Task PlayReaction(DefinitionGallery gallery)
        {
            ReactSendGallery = gallery;
            if (!gallery.Loop)
            {
                TimerReactStop.Interval = Math.Abs(gallery.Duration);
                TimerReactStop.Start();
            }
            changeStatus($"Device Reaction [{gallery.Name}], loop:{gallery.Loop}");

            await DeviceManager.PlayGallery(gallery.Name);
        }
        private async void TimerReactStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
            => await StopReaction();
        private async Task StopReaction()
        {
            TimerReactStop.Stop();
            if (ReactSendGallery == null)
                return;

            ReactSendGallery = null;

            changeStatus($"Stop Reaction");

            if (LastGallery != null)
            {
                var seekBack = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds + resumePauseAt);
                await SendGallery(LastGallery, seekBack);

            }
            else if (CurrentFiller != null)
            {
                await SendFiller(CurrentFiller);
            }
            else 
            {
                await Pause();
            }
        }
        public async Task Stop()
        {
            if (ReactSendGallery != null)
                await StopReaction();
            else if (LastGallery?.Type == "gallery")
                await StopGallery();
        }

        private async Task StopGallery()
        {
            LastGallery = null;
            await SendFiller(CurrentFiller);
        }

        private async Task SetFiller(DefinitionGallery gallery)
        {
            CurrentFiller = gallery.Name;
            await SendFiller(CurrentFiller);
        }
        private async Task SendFiller(string name, long seek = 0)
        {
            if (!Config.Filler || string.IsNullOrEmpty(name))
            {
                LastGallery = null;
                await Pause();
                return;
            }

            await SendGallery(name, seek);
        }

        public async Task Pause()
        {
            changeStatus("Device Pause");

            await DeviceManager.Pause();

            if (GallerySendTime is null || LastGallery is null)
            {
                resumePauseAt = -1;
                return;
            }
            
            resumePauseAt += Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds);

            if (resumePauseAt >= LastGallery.Duration && !LastGallery.Loop)
                resumePauseAt = -1;
        }

        public async Task Resume()
        {
            changeStatus("Device Resume");
            if(resumePauseAt >= 0)
                await SendGallery(LastGallery, resumePauseAt);
        }
        private async Task SendGallery(string name, long seek = 0)
        {
            if (string.IsNullOrEmpty(name))
                return;
            await SendGallery(_repository.Get(name), seek);
        }
        private async Task SendGallery(DefinitionGallery gallery, long seek = 0)
        {
            if (gallery == null || gallery.Duration <= 0)
                return;

            ReactSendGallery = null;
            TimerReactStop.Stop();
            TimerGalleryStop.Stop();
            // If the seek time is greater than the gallery time And it Repeats, then modulo the seek time by the gallery time to get the correct seek time.
            if (seek != 0 && seek > gallery.Duration)
            {
                if (gallery.Loop)
                    seek = Convert.ToInt64(seek % gallery.Duration);
                else
                {
                    //seek out of range StopGallery
                    await Stop();
                    return;
                }
            }

            GallerySendTime = DateTime.Now;
            LastGallery = gallery;
            resumePauseAt = seek;
            // If the gallery does not repeat, then start a timer to stop the gallery after its duration.
            if (!gallery.Loop)
            {
                TimerGalleryStop.Interval = Math.Abs(gallery.Duration);
                TimerGalleryStop.Start();
            }
            changeStatus($"Device Play [{gallery.Name}] at {seek}, loop:[{gallery.Loop}]");
            await DeviceManager.PlayGallery(gallery.Name, seek);
        }

        private async void TimerGalleryStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
        {
            TimerGalleryStop.Stop();
            await Stop();
        }

        public Task Repack()
        {
            var records = _repository.GetAll();
            var vasepath = "D:\\Juegos\\Agent_mirai-v3\\EdiDemo\\Gallery\\scripts";
            var outputVideoName = "mirai-full";
            var outputVideoPath = $"{vasepath}/{outputVideoName}";
            
            // Crear una lista para FFmpeg
            var fileListPath = $"{vasepath}/filelist.txt";
            using (var writer = new StreamWriter(fileListPath))
            {
                foreach (var record in records)
                {
                    writer.WriteLine($"file '{record.Name}.mp4'");
                }
            }

            // Utilizar FFmpeg para concatenar los videos
            var ffmpegCmd = $"-f concat -safe 0 -i {fileListPath} -c copy {outputVideoPath}.mp4";
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
            process.WaitForExit();


            var funRepo = repos.FirstOrDefault(x => x is FunscriptRepository) as FunscriptRepository;

            // Ajustar los valores de StartTime y EndTime
            long accumulatedTime = 0;
            
            foreach (var record in records)
            {
                
                var duration = GetVideoDuration($"{vasepath}/{record.Name}.mp4");  // Obtener la duración en milisegundos

                record.StartTime = accumulatedTime;
                accumulatedTime += duration;
                record.EndTime = accumulatedTime;
                record.FileName = outputVideoName;
                

            }
            var variants = funRepo.GetVariants();
            foreach (var varian in variants)
            {
                var sb = new ScriptBuilder();
                

                foreach (var record in records)
                {
                    var funscipt = funRepo.Get(record.Name, varian);
                    sb.addCommands(funscipt?.Commands);
                    sb.TrimTimeTo(record.EndTime);
                }
                var actions = sb.Generate();
                new FunScriptFile()
                {
                    actions = actions.Select(x => new FunScriptAction { at = x.AbsoluteTime, pos = x.Value }).ToList(),
                    metadata = new()
                    {
                        chapters = records.Select(x => new FunScriptChapter
                        {
                            name = x.Name,
                            startTime = $"{TimeSpan.FromMilliseconds(x.StartTime):hh\\:mm\\:ss\\.fff}",
                            endTime = $"{TimeSpan.FromMilliseconds(x.EndTime):hh\\:mm\\:ss\\.fff}"
                        }).ToList()
                    }
                }.Save($"{outputVideoPath}.{varian}.funscript");
            }
           

            // Reescribir el CSV
            using (var writer = new StreamWriter($"{outputVideoPath}.csv"))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)))
            {
                csv.WriteRecords(records);
            }

            // Eliminar el archivo filelist.txt temporal
            //File.Delete(fileListPath);
            return Task.CompletedTask;
        }

        public int GetVideoDuration(string videoPath)
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
            process.WaitForExit();

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
    }
}
