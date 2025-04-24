using Edi.Core.Gallery.Definition;
using Timer = System.Timers.Timer;
using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Edi.Core.Players
{

    public class RfgPlayer : ProxyPlayer
    {
        public RfgPlayer(DefinitionRepository repository, DevicePlayer devicePlayer, ConfigurationManager configurationManager): base(devicePlayer)
        {
            TimerGalleryStop = new Timer();
            TimerGalleryStop.Elapsed += TimerGalleryStop_ElapsedAsync;

            TimerReactStop = new Timer();
            TimerReactStop.Elapsed += TimerReactStop_ElapsedAsync;

            this.repository = repository;
            this.devicePlayer = devicePlayer;

            config = configurationManager.Get<EdiConfig>();
        }
        public string Channel {  get; set; }
        private EdiConfig config;

        public event IEdi.ChangeStatusHandler OnChangeStatus;

        public async Task Play(string name, long seek = 0)
        {
            var gallery = repository.Get(name);

            if (gallery == null)
            {
                changeStatus($"Ignored not found [{name}]");
                return;
            }

            switch (gallery.Type)
            {
                case "filler":
                    if (!config.Filler)
                        break;

                    await SendFiller(gallery.Name);
                    break;
                case "gallery":
                    if (!config.Gallery)
                        break;

                    await SendGallery(gallery, seek);
                    break;
                case "reaction":
                    if (!config.Reactive)
                        break;

                    await PlayReaction(gallery);
                    break;
                default:
                    break;
            }

        }
        public async Task Stop()
        {
            if (ReactSendGallery != null)
                await StopReaction();
            else if (LastGallery?.Type == "gallery")
                await StopGallery();
        }

        private string CurrentFiller { get; set; }

        private DefinitionGallery LastGallery { get; set; }
        private DateTime? GallerySendTime { get; set; }
        private long seekTime;
        private readonly DefinitionRepository repository;
        private readonly IPlayBack devicePlayer;

        private DefinitionGallery? ReactSendGallery { get; set; }
        private Timer TimerGalleryStop { get; set; }
        private Timer TimerReactStop { get; set; }

        private void changeStatus(string message)
        {
            if (OnChangeStatus is null) return;
            OnChangeStatus($"[{DateTime.Now.ToShortTimeString()}] {message}");
        }

        private async Task StopDevice()
        {
            changeStatus("Device Stop");
            await devicePlayer.Stop();

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

            await devicePlayer.Play(gallery.Name);
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
                    _ = Stop();
                    return;
                }
            }

            GallerySendTime = DateTime.Now;
            LastGallery = gallery;
            seekTime = seek;
            // If the gallery does not repeat, then start a timer to stop the gallery after its duration.
            if (!gallery.Loop)
            {
                TimerGalleryStop.Interval = Math.Abs(gallery.Duration);
                TimerGalleryStop.Start();
            }
            changeStatus($"Device Play [{gallery.Name}] at {seek}, Type:[{gallery.Type}], Loop:[{gallery.Loop}]");

            await devicePlayer.Play(gallery.Name, seek);
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
                var seekBack = Convert.ToInt64((DateTime.Now - GallerySendTime.Value).TotalMilliseconds + seekTime);
                await SendGallery(LastGallery, seekBack);

            }
            else
            {
                await SendFiller(CurrentFiller);
            }

        }


        private async Task StopGallery()
        {
            LastGallery = null;
            await SendFiller(CurrentFiller);
        }
        private async Task SendFiller(string name, long seek = 0)
        {
            CurrentFiller = name;
            if (!config.Filler || string.IsNullOrEmpty(name))
            {
                LastGallery = null;
                await StopDevice();
                return;
            }

            await SendGallery(name, seek);
        }


        private async Task SendGallery(string name, long seek = 0)
        {
            if (string.IsNullOrEmpty(name))
                return;
            await SendGallery(repository.Get(name), seek);
        }


        private async void TimerGalleryStop_ElapsedAsync(object? sender, ElapsedEventArgs e)
        {
            TimerGalleryStop.Stop();
            await Stop();
        }
    }
}
