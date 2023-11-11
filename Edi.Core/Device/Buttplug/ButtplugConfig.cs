﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyChanged;


namespace Edi.Core.Device.Buttplug
{
    
    [AddINotifyPropertyChangedInterface]
    public  class ButtplugConfig
    {
        public string Url { get; set; } = "ws://localhost:12345";
        public int CommandDelay{ get; set; } = 110;

    }
}
