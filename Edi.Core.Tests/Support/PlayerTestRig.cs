using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Players;
using Microsoft.Extensions.Logging.Abstractions;
using ConfigurationManager = Edi.Core.Services.ConfigurationManager;

namespace Edi.Core.Tests.Support;

internal sealed class PlayerTestRig : IAsyncDisposable
{
    private PlayerTestRig(
        string temporaryDirectory,
        ConfigurationManager configuration,
        DefinitionRepository definitions,
        FunscriptRepository funscripts,
        SyncPlaybackFactory syncPlaybackFactory,
        PlayerLogService logs,
        DevicePlayer devicePlayer,
        ReactionGalleryFillerPlayer player,
        RecordingDevice device)
    {
        TemporaryDirectory = temporaryDirectory;
        Configuration = configuration;
        Definitions = definitions;
        Funscripts = funscripts;
        SyncPlaybackFactory = syncPlaybackFactory;
        Logs = logs;
        DevicePlayer = devicePlayer;
        Player = player;
        Device = device;
    }

    public string TemporaryDirectory { get; }
    public ConfigurationManager Configuration { get; }
    public DefinitionRepository Definitions { get; }
    public FunscriptRepository Funscripts { get; }
    public SyncPlaybackFactory SyncPlaybackFactory { get; }
    public PlayerLogService Logs { get; }
    public DevicePlayer DevicePlayer { get; }
    public ReactionGalleryFillerPlayer Player { get; }
    public RecordingDevice Device { get; }

    public static async Task<PlayerTestRig> CreateAsync(bool addDefaultDevice = true)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-player-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryDirectory);

        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "Galleries");

        foreach (var sourcePath in Directory.EnumerateFiles(fixtureDirectory))
        {
            File.Copy(
                sourcePath,
                Path.Combine(temporaryDirectory, Path.GetFileName(sourcePath)));
        }

        var configPath = Path.Combine(temporaryDirectory, "EdiConfig.json");
        var userConfigPath = Path.Combine(temporaryDirectory, "UserConfig.json");
        var configuration = new ConfigurationManager(configPath, userConfigPath);

        var definitions = new DefinitionRepository(configuration);
        await definitions.Init(temporaryDirectory);

        var funscripts = new FunscriptRepository(
            definitions,
            NullLogger<FunscriptRepository>.Instance);
        await funscripts.Init(temporaryDirectory);

        var syncPlaybackFactory = new SyncPlaybackFactory(definitions);
        var logs = new PlayerLogService();
        var devicePlayer = new DevicePlayer(syncPlaybackFactory, configuration, logs);
        var player = new ReactionGalleryFillerPlayer(
            definitions,
            devicePlayer,
            configuration,
            syncPlaybackFactory,
            logs);
        var device = new RecordingDevice(funscripts);

        if (addDefaultDevice)
            devicePlayer.Add(device);

        return new PlayerTestRig(
            temporaryDirectory,
            configuration,
            definitions,
            funscripts,
            syncPlaybackFactory,
            logs,
            devicePlayer,
            player,
            device);
    }

    public ValueTask DisposeAsync()
    {
        Player.Dispose();

        if (Directory.Exists(TemporaryDirectory))
            Directory.Delete(TemporaryDirectory, recursive: true);

        return ValueTask.CompletedTask;
    }
}
