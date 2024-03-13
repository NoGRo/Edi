namespace Edi.Core.Gallery
{
    public interface IRepository
    {
        Task Init(string path);
        IEnumerable<string> Accept { get; }
        
        
    }
}