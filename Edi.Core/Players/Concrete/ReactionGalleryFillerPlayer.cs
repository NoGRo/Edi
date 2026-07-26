using Edi.Core.Gallery.Definition;
using Edi.Core.Services;

namespace Edi.Core.Players
{

    public class ReactionGalleryFillerPlayer : ProxyPlayer, IDisposable
    {
        private readonly DefinitionRepository repository;
        private readonly IPlayer devicePlayer;
        private readonly SyncPlaybackFactory syncPlaybackFactory;
        private readonly EdiConfig config;
        private readonly PlayerLogService logService;

        private SyncPlayback gallerySync;
        private bool isReactionMode;
        private readonly SemaphoreSlim transitionLock = new(1, 1);
        private readonly object scheduleLock = new();
        private CancellationTokenSource galleryStopCancellation;
        private CancellationTokenSource reactionStopCancellation;

        public string Channel { get; set; }
        public event IEdi.ChangeStatusHandler OnChangeStatus;
        private DefinitionGallery CurrentFiller;

        public ReactionGalleryFillerPlayer(DefinitionRepository repo, DevicePlayer dp, ConfigurationManager cfg, SyncPlaybackFactory spf, PlayerLogService logService)
            : base(dp)
        {
            repository = repo;
            devicePlayer = dp;
            syncPlaybackFactory = spf;
            this.logService = logService;
            config = cfg.Get<EdiConfig>();
        }

        public override async Task Play(string name, long seek = 0)
        {
            await transitionLock.WaitAsync();
            try
            {
                await PlayCore(name, seek);
            }
            finally
            {
                transitionLock.Release();
            }
        }

        private async Task PlayCore(string name, long seek)
        {
            var gallery = repository.Get(name);
            if (gallery == null)
            {
                logService.AddLog($"Ignored not found [{name}]");
                return;
            }

            if (!IsTypeEnabled(gallery.Type))
            {
                if (gallery.Type == "filler")
                {
                    logService.AddLog($"Filler [{name}] not enabled, stopping playback");
                    await StopGalleryCore();
                }
                return;
            }

            switch (gallery.Type)
            {
                case "filler": await SendFillerCore(gallery); break;
                case "gallery": await PlayGalleryCore(gallery, seek); break;
                case "reaction": await PlayReactionCore(gallery); break;
            }
        }

        private bool IsTypeEnabled(string type) =>
            type switch
            {
                "filler" => config.Filler,
                "gallery" => config.Gallery,
                "reaction" => config.Reactive,
                _ => false
            };

        public override async Task Stop()
        {
            await transitionLock.WaitAsync();
            try
            {
                await StopCore();
            }
            finally
            {
                transitionLock.Release();
            }
        }

        private async Task StopCore()
        {
            if (isReactionMode)
                await StopReactionCore();
            else if (gallerySync != null)
                await StopGalleryCore();
            else
                await devicePlayer.Stop();
        }

        private async Task PlayGalleryCore(DefinitionGallery gallery, long seek = 0)
        {
            if (gallery == null || gallery.Duration <= 0)
                return;

            isReactionMode = false;
            CancelGalleryStop();
            CancelReactionStop();

            gallerySync = syncPlaybackFactory.Create(gallery.Name, seek);
            if (gallerySync.IsFinished())
            {
                gallerySync = null;
                await StopGalleryCore();
                return;
            }
            seek = gallerySync.Seek;

            if (!gallery.Loop)
                ScheduleGalleryStop(gallery.Duration);

            logService.AddLog($"Play [{gallery.Name}] at {seek}, Type:[{gallery.Type}], Loop:[{gallery.Loop}]");
            await devicePlayer.Play(gallery.Name, seek);
        }

        private async Task PlayReactionCore(DefinitionGallery gallery)
        {
            isReactionMode = true;

            CancelGalleryStop();
            CancelReactionStop();
            if (!gallery.Loop)
                ScheduleReactionStop(gallery.Duration);

            logService.AddLog($"Reaction [{gallery.Name}], loop:{gallery.Loop}");
            await devicePlayer.Play(gallery.Name);
        }

        private async Task StopReactionCore()
        {
            CancelReactionStop();
            isReactionMode = false;
            logService.AddLog("Stop Reaction");

            if (gallerySync?.IsFinished() == false)
                await PlayGalleryCore(gallerySync.Gallery, gallerySync.CurrentTime);
            else
                await StopGalleryCore();
        }

        private async Task StopGalleryCore()
        {
            CancelGalleryStop();
            gallerySync = null;
            logService.AddLog("Stop Gallery");
            await SendFillerCore(CurrentFiller);
        }

        private async Task SendFillerCore(DefinitionGallery filler, long seek = 0)
        {
            CurrentFiller = filler;
            if (!config.Filler || filler == null)
                await devicePlayer.Stop();
            else
                await PlayGalleryCore(filler, seek);
        }

        private void ScheduleGalleryStop(int durationMilliseconds)
        {
            var cancellation = ReplaceCancellation(ref galleryStopCancellation);
            ObserveScheduledTransition(durationMilliseconds, cancellation, StopGalleryCore, "gallery");
        }

        private void ScheduleReactionStop(int durationMilliseconds)
        {
            var cancellation = ReplaceCancellation(ref reactionStopCancellation);
            ObserveScheduledTransition(durationMilliseconds, cancellation, StopReactionCore, "reaction");
        }

        private void ObserveScheduledTransition(
            int durationMilliseconds,
            CancellationTokenSource cancellation,
            Func<Task> transition,
            string transitionName)
        {
            _ = RunScheduledTransition(
                TimeSpan.FromMilliseconds(durationMilliseconds),
                cancellation.Token,
                transition,
                transitionName);
        }

        private async Task RunScheduledTransition(
            TimeSpan delay,
            CancellationToken cancellationToken,
            Func<Task> transition,
            string transitionName)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                await transitionLock.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await transition();
                }
                finally
                {
                    transitionLock.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A newer playback command superseded this scheduled transition.
            }
            catch (Exception ex)
            {
                logService.AddLog($"Error stopping {transitionName}: {ex.Message}");
            }
        }

        private CancellationTokenSource ReplaceCancellation(ref CancellationTokenSource field)
        {
            CancellationTokenSource previous;
            CancellationTokenSource current;

            lock (scheduleLock)
            {
                previous = field;
                current = new CancellationTokenSource();
                field = current;
            }

            previous?.Cancel();
            previous?.Dispose();
            return current;
        }

        private void CancelGalleryStop()
            => Cancel(ref galleryStopCancellation);

        private void CancelReactionStop()
            => Cancel(ref reactionStopCancellation);

        private void Cancel(ref CancellationTokenSource field)
        {
            CancellationTokenSource previous;
            lock (scheduleLock)
            {
                previous = field;
                field = null;
            }

            previous?.Cancel();
            previous?.Dispose();
        }

        public void Dispose()
        {
            CancelGalleryStop();
            CancelReactionStop();
        }
    }
}
