using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class GamesConfig
    {
        public List<GameInfo> GamesInfo { get; set; } = new();
    }

    public record GameInfo(string Name, string Path);
}
