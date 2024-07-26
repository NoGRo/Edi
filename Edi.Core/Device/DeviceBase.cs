using Edi.Core.Gallery;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using System.Timers;
using PropertyChanged;
using Edi.Core.Device.Interfaces;

namespace Edi.Core.Device
{
    [AddINotifyPropertyChangedInterface]
    public abstract class DeviceBase<TRepository, TGallery> : IDevice, IRange
        where TRepository : class, IGalleryRepository<TGallery>
        where TGallery : class, IGallery
    {
        protected TRepository repository { get; }
        protected TGallery currentGallery;

        public bool IsPause { get; set; } = true;
        protected DeviceBase(TRepository repository)
        {
            this.repository = repository;
            timerRange.Elapsed += TimerRange_Elapsed;

        }

        public virtual bool IsReady { get; set; } = true;

        internal string selectedVariant;
        public virtual string SelectedVariant
        {
            get => selectedVariant;
            set
            {
                if (selectedVariant != value)
                {
                    selectedVariant = value;
                    SetVariant();
                    Resume();
                }
            }
        }

        public void Resume()
        {
            if (currentGallery != null && !IsPause)
            {
                PlayGallery(currentGallery.Name, CurrentTime).GetAwaiter();
            }
        }

        internal virtual void SetVariant()
        {

        }

        public virtual IEnumerable<string> Variants => repository.GetVariants();

        public string Name { get; set; }
        public DateTime SyncSend { get; private set; }
        public long SeekTime { get; internal set; }
        public int CurrentTime => currentGallery == null ? 0 : Convert.ToInt32(((DateTime.Now - SyncSend).TotalMilliseconds + SeekTime) );

        private System.Timers.Timer timerRange = new System.Timers.Timer(100);
        private Task TimerRangeTask;
        private int lastMin;
        private int lastMax = 100;

        internal virtual async Task applyRange() { }

        private async void TimerRange_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (TimerRangeTask != null && !TimerRangeTask.IsCompleted)
                return;

            if (min == lastMin && max == lastMax)
            {
                timerRange.Stop();
                return;
            }
            lastMax = max;
            lastMin = min;

            await TimerRangeTask;

            TimerRangeTask = applyRange();
        }
        public record SlideRequest(int min, int max);
        private int max = 100;
        private int min;
        public int Min
        {
            get => min;
            set
            {
                min = value;
                if (!timerRange.Enabled)
                    timerRange.Start();
            }
        }
        public int Max
        {
            get => max;
            set
            {
                max = value;
                if (!timerRange.Enabled)
                    timerRange.Start();
            }
        }


        internal CancellationTokenSource playCancelTokenSource = new CancellationTokenSource();
        public virtual async Task PlayGallery(string name, long seek = 0)
        {

            SyncSend = DateTime.Now;
            var previousCts = Interlocked.Exchange(ref playCancelTokenSource, new CancellationTokenSource()); // Intercambia el CTS global con el nuevo, de forma atómica
            previousCts?.Cancel(true); // Cancela cualquier tarea anterior

            var gallery = repository.Get(name, SelectedVariant);
            if (gallery == null)
            {
                await Stop();
                return;
            }


            SeekTime = seek;
            currentGallery = gallery;
            IsPause = false;

            var interval = gallery.Duration - seek;
            _ = PlayGallery(gallery, seek);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(interval), playCancelTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // Verifica que el token no haya sido cancelado por otra invocación
            if (playCancelTokenSource.Token.IsCancellationRequested)
                return;

            if (currentGallery?.Loop == true && !IsPause)
            {
                _ = PlayGallery(currentGallery.Name);
            }
            else
            {
                await Stop();
            }
        }

        public abstract Task PlayGallery(TGallery gallery, long seek = 0);

        public virtual async Task Stop()
        {
            var previousCts = Interlocked.Exchange(ref playCancelTokenSource, new CancellationTokenSource());

            previousCts?.Cancel(true);


            currentGallery = null;
            IsPause = true;

            // Llama al método abstracto StopGallery, que debe ser implementado por las clases derivadas.
            await StopGallery();


        }
        public abstract Task StopGallery();

        public virtual string ResolveDefaultVariant()
        => Variants.FirstOrDefault("");

    }
}
