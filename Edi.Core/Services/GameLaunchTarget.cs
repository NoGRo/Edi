namespace Edi.Core.Services;

public static class GameLaunchTarget
{
    public static string Resolve(
        string commandOrPath,
        string gameConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandOrPath);

        var target = commandOrPath.Trim();
        if (IsWebAddress(target) || Path.IsPathRooted(target))
        {
            return target;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gameConfigPath);

        var configDirectory = Path.GetDirectoryName(
            Path.GetFullPath(gameConfigPath));
        if (string.IsNullOrEmpty(configDirectory))
        {
            return Path.GetFullPath(target);
        }

        return Path.GetFullPath(
            Path.Combine(configDirectory, target));
    }

    public static bool IsWebAddress(string target)
        => target.StartsWith(
               "http://",
               StringComparison.OrdinalIgnoreCase)
           || target.StartsWith(
               "https://",
               StringComparison.OrdinalIgnoreCase);
}
