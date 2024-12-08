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
        public static Regex variantRegex => new Regex(
            @"^(?<name>.*?)(\.(?<variant>[^.\[\]]+))?\s*(\[(?<loop>nonLoop|loop)\])?\s*(\[(?<gallery>Gallery|Filler|Reaction)\])?$",
            RegexOptions.IgnoreCase
        );


        public static Regex loopRegex => new Regex(
            @"^(?<name>.*?)\s*(\[(?<loop>nonLoop|loop)\])?\s*(\[(?<gallery>Gallery|Filler|Reaction)\])?$",
            RegexOptions.IgnoreCase
        );


        public static string defaultVariant => "default";

        // Method to discover assets in the repository based on the given path
        public static List<AssetEdi> Discover(this IRepository Repository, string path)
        {
            var GalleryDir = new DirectoryInfo(path);
            var files = new List<FileInfo>();
            if (!GalleryDir.Exists)
                throw new Exception($"Gallery Path not Fount {GalleryDir}");

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
                var match = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name));

                var fileName = match.Groups["name"].Value;
                var fileVariant = match.Groups["variant"].Value;

                // Remove any reserved names from the variant
                fileVariant = Regex.Replace(fileVariant, ReserveRx, string.Empty);
                var removePathBase = GalleryDir.FullName.EndsWith("\\") ? GalleryDir.FullName : GalleryDir.FullName + "\\";
                var pathSplit = file.FullName.Replace(removePathBase, "").Split('\\');
                var pathVariant = pathSplit.Length > 1 ? pathSplit[0] : null;

                fileVariant = !string.IsNullOrEmpty(fileVariant)
                                        ? fileVariant
                                        : pathVariant ?? defaultVariant;

                var loop = match.Groups["loop"].Value.ToLower() != "nonloop";

                var type = match.Groups["type"].Value.ToLower() ;
                // Add the processed asset to the list
                assetEdis.Add(new(file, fileName, fileVariant, loop, type));
            }

            // Return the list of discovered assets
            return assetEdis.ToList();
        }

    }

    // Record type to store file information, name, and variant
    public record AssetEdi(FileInfo File, string Name, string Variant,bool Loop = true,string Type = "");
}
