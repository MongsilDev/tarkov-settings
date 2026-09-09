using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tarkov_settings.Setting
{
    class AppSetting : Settings<AppSetting>
    {
        public double brightness = 0.75;
        public double contrast = 0.60;
        public double gamma = 1.5;
        public int saturation = 60;
        public HashSet<string> pTargets = new HashSet<string>{
            "EscapeFromTarkov",
            "EscapeFromTarkovArena"
        };
        public string display = @"\\.\DISPLAY1";
        public bool minimizeOnStart = true;
        public bool autostart = false;
        public string volumeToggleHotkey = "PageDown";
        public int volumeLow = 5;
        public int volumeHigh = 100;
        public string gammaToggleHotkey = "PageUp";
        public double gammaLow = 1.0;
        public double gammaHigh = 1.5;
        // unbound on purpose - the user must opt in
        public string killHotkey = "";
        public string logsPath = "";
        public bool alwaysOnTop = false;
    }
}
