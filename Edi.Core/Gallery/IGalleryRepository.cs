
namespace Edi.Core.Gallery
{
    public interface IGalleryRepository<T> : IRepository
        where T : class, IGallery
    {
        T Get(string name, string variant = null);
        List<T> GetAll();
        List<string> GetVariants();
        
    }
}