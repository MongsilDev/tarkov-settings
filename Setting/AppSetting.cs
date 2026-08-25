using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tarkov_settings.Setting
{
    class AppSetting : Settings<AppSetting>
    {
        public double brightness = 0.55;
        public double contrast = 0.75;
        public double gamma = 2.0;
        public int saturation = 0;
        public HashSet<string> pTargets = new HashSet<string>{
            "EscapeFromTarkov",
            "EscapeFromTarkovArena"
        };
        public string display = @"\\.\DISPLAY1";
        public bool minimizeOnStart = true;
        public bool autostart = false;
        public int volumeStep = 5;
    }
}
