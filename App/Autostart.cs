using Microsoft.Win32;
using System.Windows.Forms;

namespace tarkov_settings
{
    static class Autostart
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string VALUE_NAME = "tarkov-settings";

        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                    return key?.GetValue(VALUE_NAME) != null;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RUN_KEY))
                {
                    if (value)
                        key.SetValue(VALUE_NAME, $"\"{Application.ExecutablePath}\"");
                    else
                        key.DeleteValue(VALUE_NAME, false);
                }
            }
        }
    }
}
