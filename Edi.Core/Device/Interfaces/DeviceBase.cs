using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Edi.Core.Device.Interfaces
{
    public abstract class DeviceBase<TRepository, TGallery> : IDevice
        where TRepository : class , IGalleryRepository<TGallery>
        where TGallery : class, IGallery
    {
        protected TRepository repository { get; }
        protected TGallery currentGallery;

        public bool IsPause { get; set; }
        public Timer timerGalleryEnd;
        protected DeviceBase(TRepository repository) 
        {
            this.repository = repository;

            timerGalleryEnd = new Timer(TimerGalleryEndCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public virtual bool IsReady { get; set; } = true;

        internal string selectedVariant;
        public virtual string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                selectedVariant = value;
                if (currentGallery != null && !IsPause)
                    PlayGallery(currentGallery.Name, CurrentTime).GetAwaiter();
            }
        }


        public virtual IEnumerable<string> Variants => repository.GetVariants();

        
        public  string Name { get; set; }
        public DateTime SyncSend { get; private set; }
        public long SeekTime { get; private set; }
        public int CurrentTime => Convert.ToInt32(((DateTime.Now - SyncSend).TotalMilliseconds + SeekTime) % currentGallery.Duration) ;
        
        public virtual async Task PlayGallery(string name, long seek = 0)
        {
            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
            {
                await Stop();
                return;
            }

            SyncSend = DateTime.Now;
            SeekTime = seek;
            currentGallery = gallery;
            IsPause = false;

            long interval = gallery.Duration - seek;
            timerGalleryEnd.Change(interval, Timeout.Infinite);

            await PlayGallery(name, seek);
        }

        public abstract Task PlayGallery(TGallery gallery, long seek = 0);
        private async void TimerGalleryEndCallback(object state)
        {
            // Ejecuta lógica de finalización en una tarea para permitir operaciones asíncronas
                
            if (currentGallery?.Loop == true && !IsPause)
            {
                await PlayGallery(currentGallery.Name);
            }
            else
            {
                await Stop();
            }
        }

        public virtual async Task  Stop()
        {
            currentGallery = null;
            IsPause = true;
            timerGalleryEnd.Change(Timeout.Infinite, Timeout.Infinite);
            await StopGallery();


        }
        public abstract Task StopGallery();
    }
}
