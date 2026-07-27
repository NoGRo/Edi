using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Funscript.Command;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;
using PropertyChanged;

namespace Edi.Core.Device.Simulator
{
    [AddINotifyPropertyChangedInterface]
    public class PreviewDevice : DeviceBase<FunscriptRepository, FunscriptGallery>, IRange
    {
        private readonly ILogger _logger;
        private readonly DefinitionRepository definitionRepository;
        private CmdLinear _currentCmd;

        public CmdLinear CurrentCmd
        {
            get => _currentCmd;
            set => Interlocked.Exchange(ref _currentCmd, value);
        }

        private DateTime lastUpdateAt { get; set; }
        public int currentCmdIndex { get; set; }

        public int CurrentCmdTime
        {
            get
            {
                CmdLinear localCmd = null;
                Interlocked.CompareExchange(ref localCmd, _currentCmd, null);
                return localCmd == null
                    ? 0
                    : Math.Min(localCmd.Millis, Convert.ToInt32(CurrentTime - (localCmd.AbsoluteTime - localCmd.Millis)));
            }
        }

        public int ReminingCmdTime
        {
            get
            {
                CmdLinear localCmd = null;
                Interlocked.CompareExchange(ref localCmd, _currentCmd, null);
                return localCmd == null
                    ? 0
                    : Math.Max(0, Convert.ToInt32(localCmd.AbsoluteTime - CurrentTime));
            }
        }

        // Valor actual del progress bar (0-100)
        public double ProgressValue { get; set; }
        public string GalleryName { get; set; } = "-";
        public string GalleryType { get; set; } = "-";
        public string GalleryLoop { get; set; } = "-";
        public string GallerySeek { get; set; } = "-";
        public string GalleryDuration { get; set; } = "-";

        private const int REFRESH_RATE_MS = 16; // ~60 FPS (1000ms / 60 ≈ 16.67ms)

        public PreviewDevice(
            FunscriptRepository repository,
            DefinitionRepository definitionRepository,
            ILogger<PreviewDevice> logger)
            : base(repository, logger)
        {
            _logger = logger;
            this.definitionRepository = definitionRepository;
            Name = "Preview Device";
            lastUpdateAt = DateTime.Now;
            _logger.LogInformation($"ProgressBarSimulator initialized");
        }

        public override string DefaultVariant()
            => Variants.FirstOrDefault() ?? base.DefaultVariant();

        public override Task PlayGallery(FunscriptGallery gallery, long seek = 0)
            => PlayGallery(gallery, seek, playCancelTokenSource.Token);

        protected override async Task PlayGallery(
            FunscriptGallery gallery,
            long seek,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Starting PlayGallery with Simulator: {Name}, Gallery: {gallery?.Name ?? "Unknown"}, Seek: {seek}");
            SetGalleryInfo(gallery, seek);

            var cmds = gallery?.Commands;
            if (cmds == null) return;

            currentCmdIndex = Math.Max(0, cmds.FindIndex(x => x.AbsoluteTime > CurrentTime));
            lastUpdateAt = DateTime.Now;

            while (currentCmdIndex >= 0 && currentCmdIndex < cmds.Count)
            {
                CurrentCmd = cmds[currentCmdIndex];

                // Calcular posición interpolada
                await UpdateProgressBar();

                try
                {
                    // Actualizar a 60 FPS
                    var timeSinceLastUpdate = (DateTime.Now - lastUpdateAt).TotalMilliseconds;
                    var delayMs = Math.Max(0, REFRESH_RATE_MS - timeSinceLastUpdate);
                    await Task.Delay((int)delayMs, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    // Verificar si necesitamos pasar al siguiente comando
                    if (CurrentTime >= CurrentCmd.AbsoluteTime)
                    {
                        currentCmdIndex++;
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning($"PlayGallery canceled for Simulator: {Name}");
                    ProgressValue = 0;
                    return;
                }
            }

            ProgressValue = 0; // Resetear al finalizar
            _logger.LogInformation($"PlayGallery completed for Simulator: {Name}");
        }

        private async Task UpdateProgressBar()
        {
            if (CurrentCmd == null) return;

            // Calcular la posición interpolada basada en el tiempo actual
            double progress = (CurrentTime - (CurrentCmd.AbsoluteTime - CurrentCmd.Millis)) / (double)CurrentCmd.Millis;
            progress = Math.Clamp(progress, 0, 1);

            // Interpolar entre la posición anterior y la actual
            double targetPosition = CurrentCmd.Value;
            var lastPosition = CurrentCmd.InitialValue;
            double interpolatedPosition = lastPosition + (targetPosition - lastPosition) * progress;

            // Actualizar el valor del progress bar (0-100)
            ProgressValue = ScalePosition(interpolatedPosition, Min, Max);

            lastUpdateAt = DateTime.Now;
        }

        internal static int ScalePosition(double position, int min, int max)
        {
            var normalizedPosition = Math.Clamp(position, 0, 100);
            var scaledPosition = min + (max - min) * normalizedPosition / 100;
            return (int)Math.Round(scaledPosition);
        }

        private void SetGalleryInfo(FunscriptGallery gallery, long seek)
        {
            if (gallery == null)
            {
                GalleryName = "Unavailable";
                GalleryType = "-";
                GalleryLoop = "-";
                GallerySeek = "-";
                GalleryDuration = "-";
                return;
            }

            var definition = definitionRepository.Get(gallery.Name);
            GalleryName = gallery.Name;
            GalleryType = definition?.Type ?? "gallery";
            GalleryLoop = gallery.Loop ? "Yes" : "No";
            GallerySeek = $"{seek} ms";
            GalleryDuration = $"{gallery.Duration} ms";
        }

        public override async Task StopGallery()
        {
            _logger.LogInformation($"Stopping gallery playback for Simulator: {Name}");
            ProgressValue = 0;
            await Task.CompletedTask;
        }
    }
}
