using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using tarkov_settings.GPU;

namespace tarkov_settings
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public UInt16[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public UInt16[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public UInt16[] Blue;
    }

    class Display
    {
        static IGPU gpu;

        [DllImport("gdi32")]
        public static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32")]
        public static extern bool DeleteDC([In] IntPtr hdc);

        public readonly static List<string> displays;
        private static string _primary;

        public static string Primary
        {
            get => _primary;
            set
            {
                string target = displays.Contains(value) ? value : displays[0];
                if (target == _primary)
                    return;

                var cController = ColorController.Instance;

                // restore the outgoing display first, so its backup ramp is never
                // written to the new display and DVC init levels stay uncontaminated
                if (_primary != null && cController.IsApplied)
                {
                    cController.ChangeColorRamp(reset: true);
                    cController.ResetDVL();
                }

                _primary = target;
                try
                {
                    gpu.Load(_primary);
                }
                catch (NotImplementedException) { }

                // re-backup the gamma ramp of the newly selected display
                cController.Init();
            }
        }

        static Display()
        {
            displays = GetWinDisplays();
            gpu = GPUDevice.Instance;
        }

        private static List<string> GetWinDisplays()
        {
            List<string> list = new List<string>();
            foreach (Screen screen in Screen.AllScreens)
            {
                list.Add(screen.DeviceName);
            }
            return list;
        }
    }
}
