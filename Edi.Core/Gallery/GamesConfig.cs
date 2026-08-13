using Edi.Core.Services;
using PropertyChanged;
using System.Collections.ObjectModel;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class GamesConfig
    {
        public GameInfo? SelectedGameinfo { get; set; }
        public ObservableCollection<GameInfo> GamesInfo { get; set; } = new();

        public GameInfo UpsertGame(
            GameInfo game,
            GameInfo? gameToReplace = null)
        {
            ArgumentNullException.ThrowIfNull(game);

            var normalizedGame = new GameInfo(
                game.Name.Trim(),
                game.Path.Trim());
            var games = GamesInfo.ToList();
            var gameToReplacePath = gameToReplace?.Path ?? normalizedGame.Path;
            var existingIndex = games.FindIndex(
                item => PathsEqual(item.Path, gameToReplacePath));

            if (existingIndex >= 0)
            {
                if (gameToReplace is null)
                {
                    normalizedGame = games[existingIndex];
                }
                else
                {
                    games[existingIndex] = normalizedGame;
                }
            }
            else
            {
                games.Add(normalizedGame);
            }

            GamesInfo = new ObservableCollection<GameInfo>(games);

            if (SelectedGameinfo is not null
                && PathsEqual(
                    SelectedGameinfo.Path,
                    gameToReplacePath))
            {
                SelectedGameinfo = normalizedGame;
            }

            return normalizedGame;
        }

        public bool RemoveGame(GameInfo game)
        {
            ArgumentNullException.ThrowIfNull(game);

            var games = GamesInfo
                .Where(item => !PathsEqual(item.Path, game.Path))
                .ToList();
            if (games.Count == GamesInfo.Count)
            {
                return false;
            }

            GamesInfo = new ObservableCollection<GameInfo>(games);
            if (SelectedGameinfo is not null
                && PathsEqual(SelectedGameinfo.Path, game.Path))
            {
                SelectedGameinfo = null;
            }

            return true;
        }

        public bool ContainsPath(
            string path,
            GameInfo? gameToIgnore = null)
        {
            return GamesInfo.Any(
                game => !PathsEqual(
                            game.Path,
                            gameToIgnore?.Path)
                        && PathsEqual(game.Path, path));
        }

        public bool UpgradeLegacyPathNames()
        {
            var upgradedAnyGame = false;
            var games = GamesInfo
                .Select(game =>
                {
                    if (!string.IsNullOrWhiteSpace(game.Name)
                        && !PathsEqual(game.Name, game.Path))
                    {
                        return game;
                    }

                    upgradedAnyGame = true;
                    return game with
                    {
                        Name = SuggestNameFromPath(game.Path)
                    };
                })
                .ToList();

            if (!upgradedAnyGame)
            {
                return false;
            }

            var selectedPath = SelectedGameinfo?.Path;
            GamesInfo = new ObservableCollection<GameInfo>(games);
            if (selectedPath is not null)
            {
                SelectedGameinfo = games.FirstOrDefault(
                    game => PathsEqual(game.Path, selectedPath));
            }

            return true;
        }

        public static string SuggestNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "New game";
            }

            var directoryPath = Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                var directoryName = new DirectoryInfo(
                    directoryPath).Name;
                if (!string.IsNullOrWhiteSpace(directoryName))
                {
                    return directoryName;
                }
            }

            return Path.GetFileNameWithoutExtension(path);
        }

        private static bool PathsEqual(
            string? first,
            string? second)
        {
            if (string.IsNullOrWhiteSpace(first)
                || string.IsNullOrWhiteSpace(second))
            {
                return string.Equals(
                    first?.Trim(),
                    second?.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                first = Path.GetFullPath(first.Trim());
                second = Path.GetFullPath(second.Trim());
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
            }

            return string.Equals(
                first,
                second,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public record GameInfo(string Name, string Path);
}
