using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace tarkov_settings
{
    static class ServerLog
    {
        // 2026-08-28 12:49:34.308|1.1.0.1.46911|Info|network-connection|Connect (address: 87.249.128.208:17008)
        private static readonly Regex ConnectPattern = new Regex(
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+\|[^|]*\|[^|]*\|network-connection\|Connect \(address: (?<ip>[\d.]+):(?<port>\d+)\)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public class Entry
        {
            public DateTime Time;
            public string Ip;
            public int Port;
        }

        public static string DetectLogsPath()
        {
            try
            {
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov"))
                    {
                        string install = key?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(install))
                        {
                            string logs = Path.Combine(install, "Logs");
                            if (Directory.Exists(logs))
                                return logs;
                        }
                    }
                }

                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed)
                        continue;
                    string logs = Path.Combine(drive.Name, "Battlestate Games", "Escape from Tarkov", "Logs");
                    if (Directory.Exists(logs))
                        return logs;
                }
            }
            catch (Exception) { }
            return "";
        }

        // a picked game root still works: fall through to its Logs subfolder
        public static string NormalizeLogsPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            string sub = Path.Combine(path, "Logs");
            if (Directory.Exists(sub) && !Directory.GetDirectories(path, "log_*").Any())
                return sub;
            return path;
        }

        // log_2026.08.28_12-49-01_<version>; hours may lack a leading zero
        private static readonly Regex FolderTimePattern = new Regex(
            @"log_(\d{4})\.(\d{2})\.(\d{2})_(\d{1,2})-(\d{1,2})-(\d{1,2})", RegexOptions.Compiled);

        public static List<Entry> Read(string logsPath, int max, TimeSpan window)
        {
            DateTime since = DateTime.Now - window;
            var entries = new List<Entry>();
            // session folder names sort chronologically: log_2026.08.28_12-49-01_<version>
            foreach (string dir in Directory.GetDirectories(logsPath, "log_*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // a session started well before the window cannot contain raids inside it
                Match folderTime = FolderTimePattern.Match(Path.GetFileName(dir));
                if (folderTime.Success)
                {
                    var started = new DateTime(
                        int.Parse(folderTime.Groups[1].Value), int.Parse(folderTime.Groups[2].Value), int.Parse(folderTime.Groups[3].Value),
                        int.Parse(folderTime.Groups[4].Value), int.Parse(folderTime.Groups[5].Value), int.Parse(folderTime.Groups[6].Value));
                    if (started < since - TimeSpan.FromHours(24))
                        break;
                }
                foreach (string file in Directory.GetFiles(dir, "*network-connection*.log"))
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch (IOException) { continue; }

                    foreach (Match m in ConnectPattern.Matches(text))
                    {
                        entries.Add(new Entry
                        {
                            Time = DateTime.ParseExact(m.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            Ip = m.Groups["ip"].Value,
                            Port = int.Parse(m.Groups["port"].Value),
                        });
                    }
                }
                if (entries.Count(e => e.Time >= since) >= max)
                    break;
            }
            return entries.Where(e => e.Time >= since).OrderByDescending(e => e.Time).Take(max).ToList();
        }
    }
}
