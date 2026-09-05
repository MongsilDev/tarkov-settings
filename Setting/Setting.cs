using System;
using System.IO;
using Newtonsoft.Json;

namespace tarkov_settings.Setting
{
    internal class Settings<T> where T : class, new()
    {
        private const string DEFAULT_FILENAME = "settings.json";

        private static readonly string SettingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tarkov-settings");

        public void Save(string fileName = null)
        {
            // best-effort: a failing disk must not crash the exit path
            try
            {
                Directory.CreateDirectory(SettingDir);
                string path = GetPath(fileName);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            catch (Exception e)
            {
                Console.WriteLine("[settings] save failed: {0}", e.Message);
            }
        }

        // true when no settings file existed anywhere at load time
        public static bool FirstRun { get; private set; }

        public static T Load(string fileName = null)
        {
            string path = GetPath(fileName);

            // migrate from older versions that saved next to the exe
            if (!File.Exists(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName ?? DEFAULT_FILENAME);

            FirstRun = !File.Exists(path);

            T t = null;
            try
            {
                if (File.Exists(path))
                    t = JsonConvert.DeserializeObject<T>(ReadWithRetry(path), new JsonSerializerSettings
                    {
                        // replace prefilled collection defaults instead of merging saved items into them
                        ObjectCreationHandling = ObjectCreationHandling.Replace,
                    });
            }
            catch (Exception)
            {
                // corrupted settings file - fall back to defaults
            }
            return t ?? new T();
        }

        // antivirus/backup tools may hold the file briefly right after boot
        private static string ReadWithRetry(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException) when (attempt < 3)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        private static string GetPath(string fileName)
        {
            return Path.Combine(SettingDir, fileName ?? DEFAULT_FILENAME);
        }
    }
}
