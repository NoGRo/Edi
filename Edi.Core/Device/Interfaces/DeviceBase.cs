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
        protected DeviceBase(TRepository repository) 
        {
            this.repository = repository;

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
        public int CurrentTime => currentGallery == null ? 0 : Convert.ToInt32(((DateTime.Now - SyncSend).TotalMilliseconds + SeekTime) % currentGallery.Duration) ;
        private CancellationTokenSource cancelTokenSource = new CancellationTokenSource();
        public virtual async Task PlayGallery(string name, long seek = 0)
        {
            var localCts = new CancellationTokenSource(); // Crear un nuevo CTS para esta ejecución específica
            var previousCts = Interlocked.Exchange(ref cancelTokenSource, localCts); // Intercambia el CTS global con el nuevo, de forma atómica
            previousCts.Cancel(); // Cancela cualquier tarea anterior

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

            var interval = gallery.Duration - seek;
            await PlayGallery(gallery, seek);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(interval), localCts.Token);
            }
            catch (TaskCanceledException)
            {
                // Lógica después de la cancelación, si es necesario
                return;
            }

            // Verifica que el token no haya sido cancelado por otra invocación
            if (localCts.Token.IsCancellationRequested) return;

            if (currentGallery?.Loop == true && !IsPause)
            {
                await PlayGallery(currentGallery.Name);
            }
            else
            {
                await Stop();
            }
        }

        public abstract Task PlayGallery(TGallery gallery, long seek = 0);
 
        public virtual async Task  Stop()
        {
            var previousCts = Interlocked.Exchange(ref cancelTokenSource, new CancellationTokenSource());
            if (previousCts != null)
            {
                previousCts.Cancel();
                previousCts.Dispose(); // Es importante disponer el CTS antiguo para liberar recursos.
            }

            currentGallery = null;
            IsPause = true;

            // Llama al método abstracto StopGallery, que debe ser implementado por las clases derivadas.
            await StopGallery();


        }
        public abstract Task StopGallery();
    }
}
