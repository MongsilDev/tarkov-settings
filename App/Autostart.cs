using System;
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
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                        return key?.GetValue(VALUE_NAME) != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            // an antivirus or policy blocking the Run key must not crash every start;
            // callers read Enabled back to see what actually happened
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RUN_KEY))
                    {
                        if (value)
                            key.SetValue(VALUE_NAME, $"\"{Application.ExecutablePath}\"");
                        else
                            key.DeleteValue(VALUE_NAME, false);
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
