using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Services;
using System.ComponentModel;

namespace Edi.Core.Players
{
    public class DevicePlayer : IPlayer
    {
        private readonly SyncPlaybackFactory syncFactory;
        private readonly PlayerLogService logService;
        private readonly DevicesConfig config;
        public DevicePlayer(
            SyncPlaybackFactory syncFactory,
            ConfigurationManager configuration,
            PlayerLogService logService)
        {
            this.syncFactory = syncFactory;
            this.logService = logService;
            config = configuration.Get<DevicesConfig>();

        }

        private readonly object devicesLock = new();
        private readonly List<IDevice> Devices = new();
        private readonly Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler> subscriptions = new();
        private readonly Dictionary<IDevice, DeviceDispatchState> dispatchStates = new();
        private readonly SemaphoreSlim stateLock = new(1, 1);
        private bool isHardPause;
        private bool isPause;
        private SyncPlayback syncPlayback;

        public void Add(IDevice device)
        {
            lock (devicesLock)
            {
                if (Devices.Contains(device))
                    return;

                Devices.Add(device);

                if (device is INotifyPropertyChanged notifier)
                {
                    PropertyChangedEventHandler handler = (sender, args) =>
                    {
                        if (sender is not IDevice changedDevice
                            || args.PropertyName is not nameof(IDevice.SelectedVariant)
                                and not nameof(IDevice.IsReady)
                            || !Contains(changedDevice))
                        {
                            return;
                        }

                        Observe(Sync(changedDevice), $"syncing device '{changedDevice.Name}' after a property change");
                    };

                    notifier.PropertyChanged += handler;
                    subscriptions[notifier] = handler;
                }
            }

            Observe(Sync(device), $"initial sync for device '{device.Name}'");
        }

        public void Remove(IDevice device)
        {
            lock (devicesLock)
            {
                Devices.Remove(device);

                if (device is INotifyPropertyChanged notifier
                    && subscriptions.Remove(notifier, out var handler))
                {
                    notifier.PropertyChanged -= handler;
                }

                if (dispatchStates.TryGetValue(device, out var dispatch))
                    dispatch.Pending = null;
            }
        }

        private bool Contains(IDevice device)
        {
            lock (devicesLock)
            {
                return Devices.Contains(device);
            }
        }

        private List<IDevice> Snapshot(IDevice device = null)
        {
            lock (devicesLock)
            {
                if (device == null)
                    return Devices.ToList();

                return Devices.Contains(device) ? new List<IDevice> { device } : new List<IDevice>();
            }
        }

        private bool isStopState(IDevice device)
        {
            return device.SelectedVariant == "None"
                || device is IRange r && r.Min == r.Max;
        }


        public async Task Sync(IDevice device = null, bool atCurrentTime = true)
        {
            await stateLock.WaitAsync();
            try
            {
                SyncCore(device, atCurrentTime);
            }
            finally
            {
                stateLock.Release();
            }
        }

        public async Task Stop()
        {
            await stateLock.WaitAsync();
            try
            {
                syncPlayback = null;
                Dispatch(Snapshot(), device => device.Stop(), "stopping");
            }
            finally
            {
                stateLock.Release();
            }
        }

        public async Task Play(string name, long seek = 0)
        {
            await stateLock.WaitAsync();
            try
            {
                syncPlayback = syncFactory.Create(name, seek);
                if (isHardPause)
                    return;

                isPause = false;
                Dispatch(
                    Snapshot().Where(device => !isStopState(device)),
                    device => device.PlayGallery(name, seek),
                    $"playing gallery '{name}'");
            }
            finally
            {
                stateLock.Release();
            }
        }

        public async Task Pause(bool untilResume = false)
        {
            await stateLock.WaitAsync();
            try
            {
                isHardPause = untilResume;
                isPause = true;
                if (syncPlayback != null)
                    syncPlayback = syncFactory.Create(syncPlayback.GalleryName, syncPlayback.CurrentTime);

                logService.AddLog($"Pause, until resume: {untilResume}");

                Dispatch(Snapshot(), device => device.Stop(), "pausing");
            }
            finally
            {
                stateLock.Release();
            }
        }

        public async Task Resume(bool atCurrentTime = false)
        {
            await stateLock.WaitAsync();
            try
            {
                isHardPause = false;
                isPause = false;
                if (syncPlayback?.IsFinished(atCurrentTime) == false)
                {
                    var resumeTime = syncPlayback.ResumeTime(atCurrentTime);
                    logService.AddLog($"Resume [{syncPlayback.GalleryName}] at {resumeTime}");
                    SyncCore(atCurrentTime: atCurrentTime);
                    syncPlayback = syncFactory.Create(syncPlayback.GalleryName, resumeTime);
                }
                else
                {
                    logService.AddLog("Resume, Stop");
                    syncPlayback = null;
                    Dispatch(Snapshot(), device => device.Stop(), "stopping after resume");
                }
            }
            finally
            {
                stateLock.Release();
            }
        }

        public async Task Intensity(int max)
        {
            await stateLock.WaitAsync();
            try
            {
                foreach (var device in Snapshot().OfType<IRange>())
                {
                    var rangeDevice = (IDevice)device;
                    if (config.Devices.TryGetValue(rangeDevice.Name, out var configuredRange))
                    {
                        device.Max = configuredRange.Min
                                     + (configuredRange.Max - configuredRange.Min) * max / 100;
                    }
                }
            }
            finally
            {
                stateLock.Release();
            }
        }

        private void SyncCore(IDevice device = null, bool atCurrentTime = true)
        {
            foreach (var target in Snapshot(device).Where(candidate => candidate.IsReady))
            {
                if (!isHardPause
                    && !isPause
                    && syncPlayback?.IsFinished(atCurrentTime) == false
                    && !isStopState(target))
                {
                    var galleryName = syncPlayback.GalleryName;
                    var resumeTime = syncPlayback.ResumeTime(atCurrentTime);
                    Dispatch(target, () => target.PlayGallery(galleryName, resumeTime),
                        $"syncing gallery '{galleryName}'");
                }
                else
                {
                    Dispatch(target, target.Stop, "sync stop");
                }
            }
        }

        private void Dispatch(
            IEnumerable<IDevice> devices,
            Func<IDevice, Task> command,
            string operation)
        {
            foreach (var device in devices)
            {
                Dispatch(device, () => command(device), operation);
            }
        }

        private void Dispatch(IDevice device, Func<Task> command, string operation)
        {
            DeviceDispatchState dispatch;
            lock (devicesLock)
            {
                if (!dispatchStates.TryGetValue(device, out dispatch))
                    dispatchStates[device] = dispatch = new();

                dispatch.Pending = (command, operation);
                if (dispatch.IsRunning)
                    return;

                dispatch.IsRunning = true;
            }

            Observe(
                Task.Run(() => ProcessDispatch(device, dispatch)),
                $"dispatching commands on device '{device.Name}'");
        }

        private void ProcessDispatch(IDevice device, DeviceDispatchState dispatch)
        {
            while (true)
            {
                (Func<Task> Command, string Operation)? next;
                lock (devicesLock)
                {
                    next = dispatch.Pending;
                    dispatch.Pending = null;
                    if (next == null)
                    {
                        dispatch.IsRunning = false;
                        if (dispatchStates.TryGetValue(device, out var current)
                            && ReferenceEquals(current, dispatch))
                        {
                            dispatchStates.Remove(device);
                        }

                        return;
                    }
                }

                try
                {
                    Observe(
                        next.Value.Command(),
                        $"{next.Value.Operation} on device '{device.Name}'");
                }
                catch (Exception ex)
                {
                    logService.AddLog(
                        $"Error {next.Value.Operation} on device [{device.Name}]: {ex.Message}");
                }
            }
        }

        private void Observe(Task task, string operation)
        {
            if (task == null)
                return;

            _ = ObserveCore(task, operation);
        }

        private async Task ObserveCore(Task task, string operation)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // A newer player command superseded this operation.
            }
            catch (Exception ex)
            {
                logService.AddLog($"Error while {operation}: {ex.Message}");
            }
        }

        private sealed class DeviceDispatchState
        {
            public (Func<Task> Command, string Operation)? Pending { get; set; }
            public bool IsRunning { get; set; }
        }
    }
}
