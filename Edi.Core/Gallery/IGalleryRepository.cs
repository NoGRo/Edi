
namespace Edi.Core.Gallery
{
    public interface IGalleryRepository<T>
        where T : class, IGallery
    {
        Task Init();
        T Get(string name, string? variant = null);
        List<T> GetAll();
        List<string> GetVariants();
        Dictionary<string, FileInfo> Assets { get; set; }
        
    }
}