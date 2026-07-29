using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Simulator;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edi.Forms;

public partial class SimulateGame : Window, INotifyPropertyChanged
{
    private const double CollapsedMinWidth = 170;
    private const double ExpandedMinWidth = 480;
    private const double CollapsedMinHeight = 330;
    private const double ExpandedMinHeight = 560;

    private readonly IEdi edi = App.Edi;
    private readonly DeviceCollector deviceCollector;
    private readonly DeviceConfiguration deviceConfiguration;
    private readonly EdiConfig config;
    private readonly GamesConfig gamesConfig;
    private readonly ILogger<SimulateGame> logger;
    private readonly PreviewWindowConfig windowConfig;
    private RecorderSlot? selectedRecorderSlot;
    private bool canEditRecorders = true;
    private bool isCloseCleanupRunning;
    private bool isClosingAfterCleanup;

    public SimulateGame()
    {
        InitializeComponent();
        windowConfig = edi.ConfigurationManager.Get<PreviewWindowConfig>();
        config = edi.ConfigurationManager.Get<EdiConfig>();
        gamesConfig = edi.ConfigurationManager.Get<GamesConfig>();
        logger = App.ServiceProvider.GetRequiredService<ILogger<SimulateGame>>();
        RestoreWindowPlacement();

        SimulatorDevice = new PreviewDevice(
            App.ServiceProvider.GetRequiredService<FunscriptRepository>(),
            App.ServiceProvider.GetRequiredService<DefinitionRepository>(),
            App.ServiceProvider.GetRequiredService<ILogger<PreviewDevice>>());
        deviceCollector = edi.DeviceCollector;
        deviceConfiguration =
            App.ServiceProvider.GetRequiredService<DeviceConfiguration>();

        foreach (var channel in edi.Player.Channels)
            Channels.Add(channel);
        foreach (var variant in SimulatorDevice.Variants)
            Variants.Add(variant);
        AddRecorderSlot();

        DataContext = this;
        Loaded += SimulateGame_Loaded;
        Closing += SimulateGame_Closing;
        edi.Player.ChannelsChanged += Player_ChannelsChanged;
        if (config is INotifyPropertyChanged notifier)
            notifier.PropertyChanged += Config_PropertyChanged;
        if (gamesConfig is INotifyPropertyChanged gamesNotifier)
            gamesNotifier.PropertyChanged += GamesConfig_PropertyChanged;
        UpdateChannelVisibility();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PreviewDevice SimulatorDevice { get; private set; }
    public SimulatorDevice DisplayDevice =>
        (SimulatorDevice?)SelectedRecorderSlot?.Device ?? SimulatorDevice;
    public ObservableCollection<RecorderSlot> RecorderSlots { get; } = [];
    public ObservableCollection<string> Channels { get; } = [];
    public ObservableCollection<string> Variants { get; } = [];

    public RecorderSlot? SelectedRecorderSlot
    {
        get => selectedRecorderSlot;
        set
        {
            if (ReferenceEquals(selectedRecorderSlot, value))
                return;
            selectedRecorderSlot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayDevice));
        }
    }

    public bool CanEditRecorders
    {
        get => canEditRecorders;
        private set
        {
            if (canEditRecorders == value)
                return;
            canEditRecorders = value;
            OnPropertyChanged();
        }
    }

    private void SimulateGame_Loaded(object sender, RoutedEventArgs e)
        => deviceCollector.LoadDevice(SimulatorDevice);

    private async void SimulateGame_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (isClosingAfterCleanup)
            return;

        e.Cancel = true;
        if (isCloseCleanupRunning)
            return;

        isCloseCleanupRunning = true;
        edi.Player.ChannelsChanged -= Player_ChannelsChanged;
        if (config is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= Config_PropertyChanged;
        if (gamesConfig is INotifyPropertyChanged gamesNotifier)
            gamesNotifier.PropertyChanged -= GamesConfig_PropertyChanged;

        try
        {
            SaveWindowPlacement();
            await StopRecorders();
            await SimulatorDevice.Stop();
            deviceCollector.UnloadDevice(SimulatorDevice);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Preview Player failed while closing.");
        }
        finally
        {
            isClosingAfterCleanup = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void AddRecorder_Click(object sender, RoutedEventArgs e)
        => AddRecorderSlot();

    private void RemoveRecorder_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditRecorders || SelectedRecorderSlot == null)
            return;

        RecorderSlots.Remove(SelectedRecorderSlot);
        SelectedRecorderSlot = RecorderSlots.LastOrDefault();
    }

    private async void StartRecording_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!CanEditRecorders)
            return;
        if (RecorderSlots.Count == 0)
            AddRecorderSlot();

        SelectedRecorderSlot = RecorderSlots[0];
        CanEditRecorders = false;
        StartRecordingButton.IsEnabled = false;

        try
        {
            foreach (var slot in RecorderSlots)
            {
                var recorder =
                    App.ServiceProvider.GetRequiredService<RecorderDevice>();
                recorder.Name = slot.Name;
                PrepareSavedConfiguration(
                    recorder.Name,
                    slot.Channel,
                    slot.Variant);
                var outputPath = recorder.StartRecording();
                deviceCollector.LoadDevice(recorder);

                if (recorder.Channel != slot.Channel)
                    await deviceConfiguration.SelectChannel(recorder, slot.Channel);

                slot.Device = recorder;
                slot.OutputFilePath = outputPath;
                slot.Status = "Recording";
                if (ReferenceEquals(slot, SelectedRecorderSlot))
                    OnPropertyChanged(nameof(DisplayDevice));
            }

            StopRecordingButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            await StopRecorders();
            MessageBox.Show(
                this,
                $"Recording could not be started:\n{ex.Message}",
                "Recording",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void StopRecording_Click(
        object sender,
        RoutedEventArgs e)
        => await StopRecorders();

    private void OpenRecordingsFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var recordingsDirectory = Path.Combine(
                global::Edi.Core.Edi.OutputDir,
                "Recordings");
            Directory.CreateDirectory(recordingsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = recordingsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open the recordings folder.");
        }
    }

    private async Task StopRecorders()
    {
        StopRecordingButton.IsEnabled = false;
        foreach (var slot in RecorderSlots)
        {
            var recorder = slot.Device;
            if (recorder == null)
                continue;

            try
            {
                await recorder.Stop();
                await recorder.StopRecording();
                slot.Status =
                    $"Saved ({recorder.RecordedActionCount} points)";
                slot.OutputFilePath = recorder.OutputFilePath;
            }
            catch (Exception ex)
            {
                slot.Status = $"Error: {ex.Message}";
            }
            finally
            {
                deviceCollector.UnloadDevice(recorder);
                slot.Device = null;
                if (ReferenceEquals(slot, SelectedRecorderSlot))
                    OnPropertyChanged(nameof(DisplayDevice));
            }
        }

        CanEditRecorders = true;
        StartRecordingButton.IsEnabled = true;
    }

    private void PrepareSavedConfiguration(
        string deviceName,
        string channel,
        string? variant)
    {
        var devicesConfig =
            edi.ConfigurationManager.Get<DevicesConfig>();
        if (!devicesConfig.Devices.TryGetValue(deviceName, out var config))
        {
            config = new DeviceConfig();
            devicesConfig.Devices[deviceName] = config;
        }

        config.Channel = channel;
        config.Variant = variant;
        edi.ConfigurationManager.Save(devicesConfig);
    }

    private void AddRecorderSlot()
    {
        if (!CanEditRecorders)
            return;

        var nextNumber = 1;
        while (RecorderSlots.Any(
                   slot => slot.Name == $"Preview Recorder {nextNumber}"))
        {
            nextNumber++;
        }

        var channel = Channels.Count == 0
            ? "main"
            : Channels[RecorderSlots.Count % Channels.Count];
        var slot = new RecorderSlot
        {
            Name = $"Preview Recorder {nextNumber}",
            Channel = channel,
            Variant = Variants.FirstOrDefault(),
            Status = "Ready"
        };
        RecorderSlots.Add(slot);
        SelectedRecorderSlot = slot;
    }

    private void Player_ChannelsChanged(List<string> channels)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var channel in channels.Where(
                         channel => !Channels.Contains(channel)))
            {
                Channels.Add(channel);
            }

            foreach (var oldChannel in Channels
                         .Where(channel => !channels.Contains(channel))
                         .ToList())
            {
                Channels.Remove(oldChannel);
            }

            foreach (var slot in RecorderSlots.Where(
                         slot => !Channels.Contains(slot.Channel)))
            {
                slot.Channel = Channels.FirstOrDefault() ?? "main";
            }
        });
    }

    private void Config_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EdiConfig.UseChannels))
            return;

        _ = Dispatcher.BeginInvoke(
            new Action(UpdateChannelVisibility));
    }

    private void GamesConfig_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GamesConfig.SelectedGameinfo))
            return;

        _ = Dispatcher.BeginInvoke(
            new Action(() =>
                Observe(
                    RefreshVariantsAfterGameChange(),
                    "refreshing preview and recorder variants")));
    }

    private async Task RefreshVariantsAfterGameChange()
    {
        var availableVariants = SimulatorDevice.Variants
            .Distinct()
            .ToList();

        Variants.Clear();
        foreach (var variant in availableVariants)
            Variants.Add(variant);

        var devicesConfig = edi.ConfigurationManager.Get<DevicesConfig>();
        await ApplyValidVariant(
            SimulatorDevice,
            SimulatorDevice.SelectedVariant,
            availableVariants,
            devicesConfig);

        foreach (var slot in RecorderSlots)
        {
            var preferredVariant = GetPreferredVariant(
                slot.Name,
                slot.Variant,
                availableVariants,
                devicesConfig);
            slot.Variant = preferredVariant;

            if (slot.Device != null && preferredVariant != null)
            {
                await deviceConfiguration.SelectVariant(
                    slot.Device,
                    preferredVariant);
            }
        }
    }

    private async Task ApplyValidVariant(
        PreviewDevice device,
        string? currentVariant,
        IReadOnlyCollection<string> availableVariants,
        DevicesConfig devicesConfig)
    {
        var preferredVariant = GetPreferredVariant(
            device.Name,
            currentVariant,
            availableVariants,
            devicesConfig);
        if (preferredVariant != null)
            await deviceConfiguration.SelectVariant(device, preferredVariant);
    }

    private static string? GetPreferredVariant(
        string deviceName,
        string? currentVariant,
        IReadOnlyCollection<string> availableVariants,
        DevicesConfig devicesConfig)
    {
        var configuredVariant =
            devicesConfig.Devices.TryGetValue(deviceName, out var deviceConfig)
                ? deviceConfig.Variant
                : null;

        if (configuredVariant != null
            && availableVariants.Contains(configuredVariant))
        {
            return configuredVariant;
        }

        if (currentVariant != null
            && availableVariants.Contains(currentVariant))
        {
            return currentVariant;
        }

        return availableVariants.FirstOrDefault();
    }

    private void UpdateChannelVisibility()
    {
        var visibility = config.UseChannels
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviewChannelRow.Visibility = visibility;
        RecorderChannelColumn.Visibility = visibility;
    }

    private void Observe(Task task, string operation)
        => _ = ObserveCore(task, operation);

    private async Task ObserveCore(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Preview Player failed while {Operation}.",
                operation);
        }
    }

    private void RecordingExpander_Expanded(
        object sender,
        RoutedEventArgs e)
    {
        MinWidth = ExpandedMinWidth;
        MinHeight = ExpandedMinHeight;
    }

    private void RecordingExpander_Collapsed(
        object sender,
        RoutedEventArgs e)
    {
        MinWidth = CollapsedMinWidth;
        MinHeight = CollapsedMinHeight;
    }

    private void RestoreWindowPlacement()
    {
        var width = windowConfig.Width ?? Width;
        var height = windowConfig.Height ?? Height;
        if (double.IsFinite(width) && width >= MinWidth)
            Width = width;
        if (double.IsFinite(height) && height >= MinHeight)
            Height = height;

        if (windowConfig.Left is not double left
            || windowConfig.Top is not double top
            || !IsPlacementVisible(left, top, Width, Height))
        {
            return;
        }

        Left = left;
        Top = top;
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        if (!double.IsFinite(bounds.Left)
            || !double.IsFinite(bounds.Top)
            || !double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height))
        {
            return;
        }

        windowConfig.Left = bounds.Left;
        windowConfig.Top = bounds.Top;
        windowConfig.Width = bounds.Width;
        windowConfig.Height = bounds.Height;
        edi.ConfigurationManager.Save(windowConfig);
    }

    private static bool IsPlacementVisible(
        double left,
        double top,
        double width,
        double height)
    {
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height))
        {
            return false;
        }

        var savedBounds = new Rect(left, top, width, height);
        var virtualDesktop = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        savedBounds.Intersect(virtualDesktop);
        return savedBounds.Width >= 50 && savedBounds.Height >= 50;
    }

    internal void OnAlwaysOnTopChecked(
        object sender,
        RoutedEventArgs e)
        => Topmost = true;

    internal void OnAlwaysOnTopUnchecked(
        object sender,
        RoutedEventArgs e)
        => Topmost = false;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class RecorderSlot : INotifyPropertyChanged
{
    private string channel = "main";
    private string? variant;
    private string status = "Ready";
    private string? outputFilePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; init; } = "";

    public string Channel
    {
        get => channel;
        set
        {
            if (channel == value)
                return;
            channel = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => status;
        set
        {
            if (status == value)
                return;
            status = value;
            OnPropertyChanged();
        }
    }

    public string? Variant
    {
        get => variant;
        set
        {
            if (variant == value)
                return;
            variant = value;
            OnPropertyChanged();
        }
    }

    public string? OutputFilePath
    {
        get => outputFilePath;
        set
        {
            if (outputFilePath == value)
                return;
            outputFilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFileName));
        }
    }

    public string? OutputFileName => string.IsNullOrWhiteSpace(OutputFilePath)
        ? null
        : Path.GetFileName(OutputFilePath);

    internal RecorderDevice? Device { get; set; }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

[UserConfig]
public class PreviewWindowConfig
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}
