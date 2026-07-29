using Edi.Core;
using Edi.Core.Device;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Edi.Core.Device.Buttplug;
using Edi.Core.Device.EStim;
using Edi.Core.Device.Handy;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device.OSR;
using Edi.Core.Gallery;
using Path = System.IO.Path;

namespace Edi.Forms
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IEdi edi = App.Edi;
        public EdiConfig config;
        public GalleryConfig galleryConfig;
        public HandyConfig handyConfig;
        public ButtplugConfig buttplugConfig;
        public EStimConfig estimConfig;
        public OSRConfig osrConfig;
        private Timer timer;
        private bool launched;
        private record AudioDevice(int id, string name);
        private record ComPort(string name, string? value);
        private record ChannelsNames(string name, string? value);

        public MainWindow()
        {
            config = edi.ConfigurationManager.Get<EdiConfig>();
            handyConfig = edi.ConfigurationManager.Get<HandyConfig>();
            galleryConfig = edi.ConfigurationManager.Get<GalleryConfig>();
            buttplugConfig = edi.ConfigurationManager.Get<ButtplugConfig>();
            estimConfig = edi.ConfigurationManager.Get<EStimConfig>();
            osrConfig = edi.ConfigurationManager.Get<OSRConfig>();
            gamesConfig = edi.ConfigurationManager.Get<GamesConfig>();
            gamesConfig.UpgradeLegacyPathNames();
            List<Core.Gallery.Definition.DefinitionGallery> galleries = ReloadGalleries();

            viewModel = new MainWindowViewModel
            {
                config = config,
                handyConfig = handyConfig,
                buttplugConfig = buttplugConfig,
                galleryConfig = galleryConfig,
                estimConfig = estimConfig,
                osrConfig = osrConfig,
                gamesConfig = gamesConfig,
                devices = GetVisibleDevices(),
                channels = edi.Player.Channels,
                galleries = galleries,
            };
            this.DataContext = viewModel;
            InitializeComponent();

            DevicesGrid.Loaded += (s, e) =>
            {
                UpdateChannelColumnVisibility();
            };

            // Add property change handler for viewModel
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.config))
                {
                    UpdateChannelColumnVisibility();
                }
            };

            edi.DeviceCollector.OnloadDevice += DeviceCollector_OnloadDeviceAsync;
            edi.DeviceCollector.OnUnloadDevice += DeviceCollector_OnUnloadDevice;
            edi.OnChangeStatus += Edi_OnChangeStatus;

            timer = new Timer(RefrehGrid);
            timer.Change(3000, 3000);

            Closing += MainWindow_Closing;

            edi.Player.ChannelsChanged += (channels) => viewModel.UpdateChannels(channels);

            LoadForm();
        }

        private void UpdateChannelColumnVisibility()
        {
            if (ChannelColumn != null && viewModel?.config != null)
            {
                try
                {
                    ChannelColumn.Visibility = viewModel.config.UseChannels ? Visibility.Visible : Visibility.Collapsed;
                }
                catch { }
            }
        }

        private List<Core.Gallery.Definition.DefinitionGallery> ReloadGalleries()
        {
            var galleries = edi.Definitions.Where(x => x.Type != "filler").ToList();

            galleries.Insert(0, new Core.Gallery.Definition.DefinitionGallery { Name = "" });
            galleries.Insert(1, new Core.Gallery.Definition.DefinitionGallery { Name = "(Random)" });
            galleries.InsertRange(2, edi.Definitions.Where(x => x.Type == "filler"));
            return galleries;
        }
        private void RefrehGrid(object? o)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (edi.DeviceCollector.Devices.Any(x => x.IsReady) 
                    && !launched
                    && !string.IsNullOrEmpty(config.ExecuteOnReady)
                    )
                {
                    launched = true;
                    lblStatus.Content = "launched: " + config.ExecuteOnReady;
                    ExecuteCommandOrOpenPath(config.ExecuteOnReady);
                }
            });
        }
        private void ExecuteCommandOrOpenPath(string commandOrPath)
        {
            try
            {
                if (commandOrPath.StartsWith("http://") || commandOrPath.StartsWith("https://"))
                {
                    // Abrir URL en el navegador predeterminado
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = commandOrPath,
                        UseShellExecute = true
                    });
                }
                else if (File.Exists(commandOrPath) || Directory.Exists(commandOrPath))
                {
                    // Ejecutar archivo o abrir directorio
                    Process.Start(new ProcessStartInfo(commandOrPath)
                    {
                        UseShellExecute = true
                    });
                }
                else
                {
                    throw new FileNotFoundException($"El archivo o comando no existe: {commandOrPath}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al ejecutar el comando o abrir la ruta: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadForm()
        {
            var audios = new List<AudioDevice>() { new AudioDevice(-1, "None") };
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                audios.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
            }
            audioDevicesComboBox.ItemsSource = audios;
            loadOSRPorts();
            DevicesGrid.ItemsSource = GetVisibleDevices();
        }

        private void loadOSRPorts()
        {
            var comPorts = new HashSet<ComPort>() { new ComPort("None", null) };
            try
            {
                foreach (var port in SerialPort.GetPortNames())
                {
                    comPorts.Add(new ComPort(port, port));
                }
            }
            catch (Exception)
            {
            }

            comPortsComboBox.ItemsSource = comPorts;
        }
        private void Edi_OnChangeStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                lblStatus.Content = message;
            });
        }

        private async void DeviceCollector_OnUnloadDevice(IDevice device, List<IDevice> devices)
        {
            await Task.Delay(1000);
            await Dispatcher.InvokeAsync(() =>
            {
                DevicesGrid.ItemsSource = GetVisibleDevices();

                //DevicesGrid.Items.Refresh();
            });

        }

        private async void DeviceCollector_OnloadDeviceAsync(IDevice device, List<IDevice> devices)
        {
            await Task.Delay(500);

            await Dispatcher.InvokeAsync(() =>
            {
                DevicesGrid.ItemsSource = GetVisibleDevices();
                //DevicesGrid.Items.Refresh();
            });
        }


        private void Variants_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox? comboBox = sender as ComboBox;
            
            var device = comboBox.DataContext as IDevice;
            _ = edi.DeviceConfiguration.SelectVariant(device, (string)comboBox.SelectedValue);
        }

        private void Channels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox? comboBox = sender as ComboBox;

            var device = comboBox.DataContext as IDevice;
            
            _ = edi.DeviceConfiguration.SelectChannel(device, (string)comboBox.SelectedValue);
        }


        private void AddGameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (TryChooseGameFile(null, out var selectedPath))
            {
                ShowGameEditor(null, selectedPath);
            }
        }

        private ObservableCollection<IDevice> GetVisibleDevices()
            => new(edi.Devices.Where(device => device is not IHiddenDevice));

        private void EditGameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (GamesComboBox.SelectedItem is GameInfo selectedGame)
            {
                ShowGameEditor(selectedGame, selectedGame.Path);
            }
        }

        private void ChangeGamePathButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var previousSuggestedName = SuggestGameName(
                GamePathTextBox.Text);
            if (!TryChooseGameFile(
                    GamePathTextBox.Text,
                    out var selectedPath))
            {
                return;
            }

            GamePathTextBox.Text = selectedPath;
            if (_gameBeingEdited is null
                && (string.IsNullOrWhiteSpace(GameNameTextBox.Text)
                    || string.Equals(
                        GameNameTextBox.Text,
                        previousSuggestedName,
                        StringComparison.Ordinal)))
            {
                GameNameTextBox.Text = SuggestGameName(selectedPath);
            }

            ClearGameEditorError();
        }

        private async void SaveGameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var name = GameNameTextBox.Text.Trim();
            var path = GamePathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowGameEditorError(
                    "Enter the name you want to see in the game list.");
                GameNameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(path)
                || (!File.Exists(path) && !Directory.Exists(path)))
            {
                ShowGameEditorError(
                    "The selected game file or folder no longer exists. Choose it again.");
                return;
            }

            if (gamesConfig.ContainsPath(path, _gameBeingEdited))
            {
                ShowGameEditorError(
                    "This game file is already in your saved list.");
                return;
            }

            GameInfo savedGame;
            _isUpdatingGameList = true;
            try
            {
                savedGame = gamesConfig.UpsertGame(
                    new GameInfo(name, path),
                    _gameBeingEdited);
            }
            finally
            {
                _isUpdatingGameList = false;
            }

            SaveGameButton.IsEnabled = false;
            try
            {
                await SelectGameAsync(savedGame);
                HideGameEditor();
            }
            catch (Exception ex)
            {
                ShowGameEditorError(
                    $"The game was saved, but EDI could not load it: {ex.Message}");
            }
            finally
            {
                SaveGameButton.IsEnabled = true;
            }
        }

        private void CancelGameEditorButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            HideGameEditor();
        }

        private async void DeleteGameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_gameBeingEdited is null)
            {
                return;
            }

            var confirmation = MessageBox.Show(
                $"Remove \"{_gameBeingEdited.Name}\" from your saved games?",
                "Remove game",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var removedSelectedGame = GamesComboBox.SelectedItem is GameInfo selectedGame
                                      && PathsEqual(
                                          selectedGame.Path,
                                          _gameBeingEdited.Path);
            gamesConfig.RemoveGame(_gameBeingEdited);
            HideGameEditor();

            if (removedSelectedGame
                && gamesConfig.GamesInfo.FirstOrDefault() is GameInfo nextGame)
            {
                await SelectGameAsync(nextGame);
            }
        }

        public async void GamesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingGameList)
            {
                return;
            }

            if (!_isSelectingGame
                && GameEditorPanel is not null
                && GameEditorPanel.Visibility == Visibility.Visible)
            {
                HideGameEditor();
            }

            if (GamesComboBox.SelectedItem is not GameInfo selectedGame)
            {
                if (EditGameButton is not null)
                {
                    EditGameButton.IsEnabled = false;
                }
                return;
            }

            if (EditGameButton is not null)
            {
                EditGameButton.IsEnabled = true;
            }
            try
            {
                await SelectGameAsync(selectedGame);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"EDI could not load this game: {ex.Message}",
                    "Game could not be loaded",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task SelectGameAsync(GameInfo selectedGame)
        {
            if (_isSelectingGame)
            {
                return;
            }

            _isSelectingGame = true;
            GamesComboBox.IsEnabled = false;
            if (EditGameButton is not null)
            {
                EditGameButton.IsEnabled = false;
            }
            try
            {
                var resolvedGame = await edi.SelectGame(selectedGame);
                GamesComboBox.SelectedItem = resolvedGame;
                viewModel.galleries = ReloadGalleries();
            }
            finally
            {
                _isSelectingGame = false;
                GamesComboBox.IsEnabled = true;
                if (EditGameButton is not null)
                {
                    EditGameButton.IsEnabled =
                        GamesComboBox.SelectedItem is GameInfo;
                }
            }
        }

        private void ShowGameEditor(
            GameInfo? game,
            string path)
        {
            _gameBeingEdited = game;
            GameEditorTitle.Text =
                game is null ? "Add game" : "Edit game";
            GameNameTextBox.Text =
                game?.Name ?? SuggestGameName(path);
            GamePathTextBox.Text = path;
            DeleteGameButton.Visibility =
                game is null ? Visibility.Collapsed : Visibility.Visible;
            ClearGameEditorError();
            GameEditorPanel.Visibility = Visibility.Visible;
            GameNameTextBox.Focus();
            GameNameTextBox.SelectAll();
        }

        private void HideGameEditor()
        {
            _gameBeingEdited = null;
            GameEditorPanel.Visibility = Visibility.Collapsed;
            ClearGameEditorError();
        }

        private void ShowGameEditorError(string message)
        {
            GameEditorError.Text = message;
            GameEditorError.Visibility = Visibility.Visible;
        }

        private void ClearGameEditorError()
        {
            GameEditorError.Text = string.Empty;
            GameEditorError.Visibility = Visibility.Collapsed;
        }

        private static bool TryChooseGameFile(
            string? currentPath,
            out string selectedPath)
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Choose an EDI game file",
                Filter = "EDI game files|EdiConfig.json;Definitions.csv;Definition.csv|EDI configuration|EdiConfig.json|Gallery definitions|Definitions.csv;Definition.csv",
                FilterIndex = 1,
                CheckFileExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                var initialDirectory = Directory.Exists(currentPath)
                    ? currentPath
                    : Path.GetDirectoryName(currentPath);
                if (Directory.Exists(initialDirectory))
                {
                    dialog.InitialDirectory = initialDirectory;
                }
            }

            if (dialog.ShowDialog()
                == System.Windows.Forms.DialogResult.OK)
            {
                selectedPath = dialog.FileName;
                return true;
            }

            selectedPath = string.Empty;
            return false;
        }

        private static string SuggestGameName(string path)
        {
            return GamesConfig.SuggestNameFromPath(path);
        }

        private static bool PathsEqual(
            string first,
            string second)
        {
            return string.Equals(
                first.Trim(),
                second.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private async void ReconnectButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            ReconnectButton.IsEnabled = false;
            try
            {
                loadOSRPorts();
                await edi.InitDevices();
            }
            finally
            {
                ReconnectButton.IsEnabled = true;
            }
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c start http://localhost:5000/swagger/index.html") { CreateNoWindow = true });
           
        }

        private async void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                var selected = cmbGallerie.Text;
                if (selected == "(Random)")
                    selected = edi.Definitions.OrderBy(x => Guid.NewGuid()).FirstOrDefault()?.Name ?? "";

                await edi.Player.Play(selected, 0, GetSelectedChannels());
            });
        }

        private async void btnStop_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Stop(GetSelectedChannels());
            });
        }

        private async void btnPause_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Pause(
                    channels: GetSelectedChannels());
            });
        }

        private async void btnResume_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Resume(
                    false,
                    GetSelectedChannels());
            });
        }

        private static SimulateGame _simulateGame; // Quitamos readonly y la inicialización inmediata
        private MainWindowViewModel viewModel;
        private GamesConfig gamesConfig;
        private GameInfo? _gameBeingEdited;
        private bool _isSelectingGame;
        private bool _isUpdatingGameList;
        private bool _isCloseCleanupRunning;
        private bool _isClosingAfterCleanup;
        // ...
    
        private void btnSimulator_Click(object sender, RoutedEventArgs e)
        {
            if (_simulateGame == null || !_simulateGame.IsLoaded)
            {
                _simulateGame = new SimulateGame();
                _simulateGame.Closed += (s, args) => _simulateGame = null;
                _simulateGame.Show();
                _simulateGame.Activate();
            }
            else
            {
                _simulateGame.Close();
            }
        }

     
        public override async void EndInit()
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Pause();
            });
            await Task.Delay(1000); 
            base.EndInit();
        }

        private async void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Player.Intensity(
                    Convert.ToInt32(sliderIntensity.Value),
                    GetSelectedChannels());
            });
        }

        private string[]? GetSelectedChannels()
        {
            var selectedChannel = viewModel.selectedChannel;
            if (!config.UseChannels
                || string.IsNullOrWhiteSpace(selectedChannel))
            {
                return null;
            }

            return [selectedChannel];
        }

        private void HandyOffsetIncrease_Click(
            object sender,
            RoutedEventArgs e)
        {
            handyConfig.OffsetMS += HandyConfig.OffsetStepMS;
        }

        private void HandyOffsetDecrease_Click(
            object sender,
            RoutedEventArgs e)
        {
            handyConfig.OffsetMS -= HandyConfig.OffsetStepMS;
        }

        private void HandyOffsetTextBox_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            handyConfig.OffsetMS += e.Delta > 0
                ? HandyConfig.OffsetStepMS
                : -HandyConfig.OffsetStepMS;
            e.Handled = true;
        }

        private void btnOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("explorer.exe",Edi.Core.Edi.OutputDir) { UseShellExecute = true });
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingAfterCleanup)
            {
                return;
            }

            e.Cancel = true;
            if (_isCloseCleanupRunning)
            {
                return;
            }

            _isCloseCleanupRunning = true;
            timer.Dispose();

            try
            {
                try
                {
                    await edi.Player.Pause();
                }
                finally
                {
                    await CloseSimulatorAsync();
                }
            }
            finally
            {
                _isClosingAfterCleanup = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }

        private static async Task CloseSimulatorAsync()
        {
            var simulator = _simulateGame;
            if (simulator is null || !simulator.IsLoaded)
            {
                return;
            }

            var closed = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void SimulatorClosed(object? sender, EventArgs e)
            {
                simulator.Closed -= SimulatorClosed;
                closed.TrySetResult(null);
            }

            simulator.Closed += SimulatorClosed;
            try
            {
                simulator.Close();
                await closed.Task;
            }
            finally
            {
                simulator.Closed -= SimulatorClosed;
            }
        }
    }
    public class BoolToReadyIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? "✅" : "🚫";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
