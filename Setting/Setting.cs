using System;
using System.IO;
using Newtonsoft.Json;

namespace tarkov_settings.Setting
{
    internal class Settings<T> where T : new()
    {
        private const string DEFAULT_FILENAME = "settings.json";

        private static readonly string SettingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tarkov-settings");

        public void Save(string fileName = null)
        {
            Directory.CreateDirectory(SettingDir);
            File.WriteAllText(GetPath(fileName), JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static T Load(string fileName = null)
        {
            string path = GetPath(fileName);

            // migrate from older versions that saved next to the exe
            if (!File.Exists(path))
                path = fileName ?? DEFAULT_FILENAME;

            T t = new T();
            if (File.Exists(path))
                t = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            return t;
        }

        private static string GetPath(string fileName)
        {
            return Path.Combine(SettingDir, fileName ?? DEFAULT_FILENAME);
        }
    }
}
