using Edi.Core.Services;
using Newtonsoft.Json.Linq;

namespace Edi.Core.Tests.Services;

public class GameLaunchTargetTests
{
    [Fact]
    public void RelativeExecutableIsResolvedFromGameConfigDirectory()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            "edi-game",
            "EdiConfig.json");

        var resolved = GameLaunchTarget.Resolve(
            Path.Combine("bin", "Game.exe"),
            configPath);

        Assert.Equal(
            Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(configPath)!,
                    "bin",
                    "Game.exe")),
            resolved);
    }

    [Fact]
    public void AbsoluteExecutableIsPreserved()
    {
        var executablePath = Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                "edi-game",
                "Game.exe"));

        var resolved = GameLaunchTarget.Resolve(
            executablePath,
            Path.Combine(
                Path.GetTempPath(),
                "other-game",
                "EdiConfig.json"));

        Assert.Equal(executablePath, resolved);
    }

    [Fact]
    public void WebAddressIsPreserved()
    {
        const string address = "https://example.test/play";

        var resolved = GameLaunchTarget.Resolve(
            address,
            Path.Combine(
                Path.GetTempPath(),
                "edi-game",
                "EdiConfig.json"));

        Assert.Equal(address, resolved);
    }

    [Fact]
    public void AutoLaunchDefaultsToDisabled()
    {
        Assert.False(new EdiConfig().AutoLaunch);
    }

    [Fact]
    public void NewGameConfigurationWritesAutoLaunchDisabled()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-launch-config-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var gameConfigPath = Path.Combine(
                temporaryDirectory,
                "game",
                "EdiConfig.json");
            var manager = new ConfigurationManager(
                Path.Combine(
                    temporaryDirectory,
                    "initial",
                    "EdiConfig.json"),
                Path.Combine(
                    temporaryDirectory,
                    "UserConfig.json"));

            manager.SetGamePath(gameConfigPath);

            var saved = JObject.Parse(
                File.ReadAllText(gameConfigPath));
            Assert.False(
                saved["Edi"]!["AutoLaunch"]!.Value<bool>());
        }
        finally
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true);
        }
    }
}
