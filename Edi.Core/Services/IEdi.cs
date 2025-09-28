using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Players;
using Edi.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using System;
using System.Collections.ObjectModel;

namespace Edi.Core
{
    public interface IEdi 
    {

        public Task Init(string path = null);
        public Task InitDevices();
        public Task SelectGame(GameInfo game);
        public IPlayerChannels Player { get; }

        public DeviceCollector DeviceCollector { get; }
        public DeviceConfiguration DeviceConfiguration { get; }
        public IEnumerable<IRepository> repos { get; }

        public ConfigurationManager ConfigurationManager { get; }
        public IEnumerable<DefinitionGallery> Definitions { get; }
        public ObservableCollection<IDevice> Devices { get; }
                               
        public delegate void ChangeStatusHandler(string message);
        public event ChangeStatusHandler OnChangeStatus;
    }
}