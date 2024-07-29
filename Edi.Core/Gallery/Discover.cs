using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public static class DiscoverExtension
    {
        // Regex pattern to extract the name and variant from a file name
        public static Regex variantRegex => new Regex(@"^(?<name>.*?)(\.(?<variant>[^.]+))?$");

        public static string defaultVariant => "default";

        // Method to discover assets in the repository based on the given path
        public static List<AssetEdi> Discover(this IRepository Repository, string path)
        {
            var GalleryDir = new DirectoryInfo(path);
            var files = new List<FileInfo>();

            // Iterate through accepted file types and gather matching files
            foreach (var item in Repository.Accept)
            {
                var mask = item.Contains("*.") ? item : $"*.{item}";
                files.AddRange(GalleryDir.EnumerateFiles(mask));
                files.AddRange(GalleryDir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles(mask)));
            }

            // Construct a regex pattern for reserved names like Axies
            string ReserveRx = Repository.Reserve.Any()
                                ? @"(?i)\." + string.Join("|", Repository.Reserve.Select(Regex.Escape))
                                : "";

            var assetEdis = new List<AssetEdi>();

            foreach (var file in files)
            {
                var fileName = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["name"].Value;
                var fileVariant = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["variant"].Value;

                // Remove any reserved names from the variant
                fileVariant = Regex.Replace(fileVariant, ReserveRx, string.Empty);

                var pathSplit = file.FullName.Replace(GalleryDir.FullName + "\\", "").Split('\\');
                var pathVariant = pathSplit.Length > 1 ? pathSplit[0] : null;

                fileVariant = !string.IsNullOrEmpty(fileVariant)
                                        ? fileVariant
                                        : pathVariant ?? defaultVariant;

                // Add the processed asset to the list
                assetEdis.Add(new(file, fileName, fileVariant));
            }

            // Return the list of discovered assets
            return assetEdis.ToList();
        }

    }

    // Record type to store file information, name, and variant
    public record AssetEdi(FileInfo File, string Name, string Variant);
}
