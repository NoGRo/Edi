using System.Diagnostics;

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

    internal static ProcessStartInfo StartInfo(string commandOrPath, string gameConfigPath)
    {
        var target = Resolve(commandOrPath, gameConfigPath);
        if (!IsWebAddress(target) && !File.Exists(target) && !Directory.Exists(target))
            throw new FileNotFoundException("The configured launch target does not exist.");
        return new ProcessStartInfo(target) { UseShellExecute = true };
    }

    public static bool IsWebAddress(string target)
        => target.StartsWith(
               "http://",
               StringComparison.OrdinalIgnoreCase)
           || target.StartsWith(
               "https://",
               StringComparison.OrdinalIgnoreCase);
}
