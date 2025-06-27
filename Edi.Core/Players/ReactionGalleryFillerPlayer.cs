using Edi.Core.Gallery.Definition;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Diagnostics;

namespace Edi.Core.Players
{

    public class ReactionGalleryFillerPlayer : ProxyPlayer
    {
        private readonly DefinitionRepository repository;
        private readonly IPlayer devicePlayer;
        private readonly SyncPlaybackFactory syncPlaybackFactory;
        private readonly EdiConfig config;

        private SyncPlayback gallerySync;
        private bool isReactionMode;
        private Timer galleryStoper;
        private Timer reactStoper;

        public string Channel { get; set; }
        public event IEdi.ChangeStatusHandler OnChangeStatus;
        private DefinitionGallery CurrentFiller;

        public ReactionGalleryFillerPlayer(DefinitionRepository repo, DevicePlayer dp, ConfigurationManager cfg, SyncPlaybackFactory spf)
            : base(dp)
        {
            repository = repo;
            devicePlayer = dp;
            syncPlaybackFactory = spf;
            config = cfg.Get<EdiConfig>();

            galleryStoper = SetupTimer(StopGallery);
            reactStoper = SetupTimer(StopReaction);
        }

        private Timer SetupTimer(Func<Task> action)
        {
            var timer = new Timer { AutoReset = false };
            timer.Elapsed += async (_, _) => await action();
            return timer;
        }

        public override async Task Play(string name, long seek = 0)
        {
            var gallery = repository.Get(name);
            if (gallery == null)
            {
                Log($"Ignored not found [{name}]");
                return;
            }

            if (!IsTypeEnabled(gallery.Type))
            {
                if (gallery.Type == "filler")
                {
                    Log($"Filler [{name}] not enabled, stopping playback");
                    await StopGallery();
                }
                return;
            }

            switch (gallery.Type)
            {
                case "filler": await SendFiller(gallery); break;
                case "gallery": await PlayGallery(gallery, seek); break;
                case "reaction": await PlayReaction(gallery); break;
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
            if (isReactionMode)
                await StopReaction();
            else if (gallerySync != null)
                await StopGallery();
        }


        private async Task PlayGallery(DefinitionGallery gallery, long seek = 0)
        {
            if (gallery == null || gallery.Duration <= 0)
                return;

            isReactionMode = false;
            galleryStoper.Stop();
            reactStoper.Stop();

            gallerySync = syncPlaybackFactory.Create(gallery.Name, seek);
            if (gallerySync.IsFinished)
            {
                gallerySync = null;
                await Stop();
                return;
            }
            seek = gallerySync.Seek;

            if (!gallery.Loop)
            {
                galleryStoper.Interval = gallery.Duration;
                galleryStoper.Start();
            }

            Log($"Device Play [{gallery.Name}] at {seek}, Type:[{gallery.Type}], Loop:[{gallery.Loop}]");
            await devicePlayer.Play(gallery.Name, seek);
        }

        private async Task PlayReaction(DefinitionGallery gallery)
        {
            isReactionMode = true;

            galleryStoper.Stop();
            reactStoper.Stop();
            if (!gallery.Loop)
            {
                reactStoper.Interval = gallery.Duration;
                reactStoper.Start();
            }
            
            Log($"Device Reaction [{gallery.Name}], loop:{gallery.Loop}");
            await devicePlayer.Play(gallery.Name);
        }

        private async Task StopReaction()
        {
            reactStoper.Stop();
            isReactionMode = false;
            Log("Stop Reaction");

            if (gallerySync?.IsFinished == false)
                await PlayGallery(gallerySync.Gallery, gallerySync.CurrentTime);
            else
                await StopGallery();
        }

        private async Task StopGallery()
        {
            gallerySync = null;
            Log("Stop Gallery");
            await SendFiller(CurrentFiller);
        }

        private async Task SendFiller(DefinitionGallery filler, long seek = 0)
        {
            CurrentFiller = filler;
            if (!config.Filler || filler == null)
                await devicePlayer.Stop();
            else
                await PlayGallery(filler, seek);
        }

        private void Log(string msg) => Debug.WriteLine($"[{DateTime.Now:T}] {msg}");

   }

}
