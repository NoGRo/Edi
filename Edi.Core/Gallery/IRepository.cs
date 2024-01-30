namespace Edi.Core.Gallery
{
    public interface IRepository
    {
        Task Init();
        IEnumerable<string> Accept { get; }

        
    }
}