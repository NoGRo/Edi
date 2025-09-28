using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Services;

namespace Edi.Core
{
    [AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class GamesConfig
    {
        public GameInfo SelectedGameinfo { get; set; }
        public ObservableCollection<GameInfo> GamesInfo { get; set; } = new();
    }

    public record GameInfo(string Name, string Path);
}
    