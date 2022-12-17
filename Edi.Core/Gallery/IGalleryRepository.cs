using Edi.Core.Gallery.models;

namespace Edi.Core.Gallery
{
    public interface IGalleryRepository
    {
        Dictionary<string, FileInfo> Assets { get; set; }

        GalleryIndex Get(string name, string? variant = null);
        List<string> GetNames();
        List<string> GetVariants();
        List<GalleryDefinition> GetDefinitions();
        Task Init();
    }
}