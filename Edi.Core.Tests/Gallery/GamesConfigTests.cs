using Edi.Core.Services;
using Newtonsoft.Json.Linq;

namespace Edi.Core.Tests.Gallery;

public class GamesConfigTests
{
    [Fact]
    public void EditingGameReplacesEntryAndKeepsItSelected()
    {
        var original = new GameInfo(
            "Old name",
            @"C:\Games\Demo\Definitions.csv");
        var config = new GamesConfig
        {
            GamesInfo = new([original]),
            SelectedGameinfo = original
        };

        var updated = config.UpsertGame(
            new GameInfo(
                "  Friendly name  ",
                @"  C:\Games\Demo\EdiConfig.json  "),
            original);

        var saved = Assert.Single(config.GamesInfo);
        Assert.Equal("Friendly name", saved.Name);
        Assert.Equal(
            @"C:\Games\Demo\EdiConfig.json",
            saved.Path);
        Assert.Same(updated, config.SelectedGameinfo);
    }

    [Fact]
    public void RemovingSelectedGameClearsSelection()
    {
        var game = new GameInfo(
            "Demo",
            @"C:\Games\Demo\EdiConfig.json");
        var config = new GamesConfig
        {
            GamesInfo = new([game]),
            SelectedGameinfo = game
        };

        var removed = config.RemoveGame(
            new GameInfo(
                "Ignored name",
                @"c:\games\demo\ediconfig.json"));

        Assert.True(removed);
        Assert.Empty(config.GamesInfo);
        Assert.Null(config.SelectedGameinfo);
    }

    [Fact]
    public void LegacyPathNamesBecomeFriendlyDirectoryNames()
    {
        var legacyGame = new GameInfo(
            @"C:\Games\Demo\EdiConfig.json",
            @"C:\Games\Demo\EdiConfig.json");
        var customGame = new GameInfo(
            "My custom name",
            @"C:\Games\Other\EdiConfig.json");
        var config = new GamesConfig
        {
            GamesInfo = new([legacyGame, customGame]),
            SelectedGameinfo = legacyGame
        };

        var changed = config.UpgradeLegacyPathNames();

        Assert.True(changed);
        Assert.Equal("Demo", config.GamesInfo[0].Name);
        Assert.Equal("My custom name", config.GamesInfo[1].Name);
        Assert.Same(config.GamesInfo[0], config.SelectedGameinfo);
    }

    [Fact]
    public void GameListChangesPersistInUserConfiguration()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-games-config-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var gameConfigPath = Path.Combine(
                temporaryDirectory,
                "EdiConfig.json");
            var userConfigPath = Path.Combine(
                temporaryDirectory,
                "UserConfig.json");
            File.WriteAllText(gameConfigPath, "{}");

            var manager = new ConfigurationManager(
                gameConfigPath,
                userConfigPath);
            var config = manager.Get<GamesConfig>();
            var game = config.UpsertGame(
                new GameInfo(
                    "Demo",
                    @"C:\Games\Demo\EdiConfig.json"));
            config.SelectedGameinfo = game;

            var saved = JObject.Parse(
                File.ReadAllText(userConfigPath));
            Assert.Equal(
                "Demo",
                saved["Games"]!["GamesInfo"]![0]!["Name"]!.Value<string>());

            config.RemoveGame(game);

            saved = JObject.Parse(
                File.ReadAllText(userConfigPath));
            Assert.Empty(saved["Games"]!["GamesInfo"]!);
            Assert.Equal(
                JTokenType.Null,
                saved["Games"]!["SelectedGameinfo"]!.Type);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
