using Edi.Core.Services;

namespace Edi.Core.Gallery.Index
{
    [GameConfig]
    public class GalleryBundlerConfig
    {
        public int MinRepeatDuration { get; set; } = 7000;
        public int RepeatDuration { get; set; } = 2000;
        public int SpacerDuration { get; set; } = 5000;
        public bool DisableBundler { get; set; } = false;
    }
}
