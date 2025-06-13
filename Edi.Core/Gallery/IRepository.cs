namespace Edi.Core.Gallery
{
    public interface IRepository
    {


        Task Init(string path);
        bool IsInitialized { get; }
        // File format: {Name}.{variant}.{axis?}.{extension}
        // Example: Punch-1.simple.sway.funscript 
        // [Name] Punch-1
        // [Variant] simple
        // [Axis?] sway (optional)
        // [Extension] funscript
        // Extension is determined based on accept 
        // Axis is optional and is removed and reserved for the repository to avoid confusion with the variant 

        // File masks or extensions accepted by the repository, e.g., [funscript] or [mp3]
        IEnumerable<string> Accept { get; }
        // Parameters of the files used by the repository, e.g., [Surge, Sway, Twist]
        IEnumerable<string> Reserve => Array.Empty<string>();
    }
}