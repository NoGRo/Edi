using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public static class DiscoverExtencion
    {


        public static Regex variantRegex => new Regex(@"^(?<name>.*?)(\.(?<variant>[^.]+))?$");
        public static string defualtVariant => "default";

        public static List<AssetEdi> Discover(this IRepository Repository, string path)
        {

            var GalleryDir = new DirectoryInfo(path);
            var files = new List<FileInfo>();
            foreach (var item in Repository.Accept)
            {
                var mask = item.Contains("*.") ? item : $"*.{item}";
                files.AddRange(GalleryDir.EnumerateFiles(mask));
                files.AddRange(GalleryDir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles(mask)));
            }

            var assetEdis = new List<AssetEdi>();
            foreach (var file in files)
            {
                var fileName = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["name"].Value;
                var variant = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["variant"].Value;

                var pathSplit = file.FullName.Replace(GalleryDir.FullName + "\\", "").Split('\\');
                var pathVariant = pathSplit.Length > 1 ? pathSplit[0] : null;
                variant = !string.IsNullOrEmpty(variant)
                                        ? variant
                                        : pathVariant ?? defualtVariant;

                assetEdis.Add(new(file, fileName, variant));
            }

            return assetEdis.DistinctBy(x => x.Variant).ToList();
        }


        public static List<AssetEdi> Discover<tGallery>(this IGalleryRepository<tGallery> Repository, )
             where tGallery : class, IGallery
        {
            var GalleryDir = new DirectoryInfo(path);
            var files = new List<FileInfo>();
            foreach (var item in Repository.Accept)
            {
                var mask = item.Contains("*.") ? item : $"*.{item}";
                files.AddRange(GalleryDir.EnumerateFiles(mask));
                files.AddRange(GalleryDir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles(mask)));
            }

            var assetEdis = new List<AssetEdi>();
            foreach (var file in files)
            {
                var fileName = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["name"].Value;
                var variant = variantRegex.Match(Path.GetFileNameWithoutExtension(file.Name)).Groups["variant"].Value;

                var pathSplit = file.FullName.Replace(GalleryDir.FullName + "\\", "").Split('\\');
                var pathVariant = pathSplit.Length > 1 ? pathSplit[0] : null;
                variant = !string.IsNullOrEmpty(variant)
                                        ? variant
                                        : pathVariant ?? defualtVariant;

                assetEdis.Add(new(file, fileName, variant));
            }

            return assetEdis.DistinctBy(x => x.Variant).ToList();
        }

        public record AssetEdi(FileInfo File, string Name, string Variant);
 
    }
}
