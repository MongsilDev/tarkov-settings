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

        // TRACE-NetworkGameCreate profileStatus: '..., Ip: 178.249.208.40, Port: 17002, Location: Shoreline, Sid: CN-HK03G043_..., GameMode: deathmatch, shortId: CDMMV5'
        private static readonly Regex ProfileStatusPattern = new Regex(
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+\|.*profileStatus: '.*Ip: (?<ip>[\d.]+), Port: (?<port>\d+), Location: (?<map>[^,]+), Sid: (?<sid>[^,']+)(?:.*shortId: (?<short>\w+))?",
            RegexOptions.Compiled);

        // MatchingCompleted:10.81 real:18.08 diff:7.26 (real = seconds since matching started)
        private static readonly Regex TimingPattern = new Regex(
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+\|.*\|(?<kind>MatchingCompleted|LocationLoaded|GameStarted):[^ ]* real:(?<real>[\d.]+)",
            RegexOptions.Compiled);

        // Disconnect (address: ip:port) / Statistics (address: ip:port, rtt: 42.5, lose: 0, ...)
        private static readonly Regex EndPattern = new Regex(
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+\|[^|]*\|[^|]*\|network-connection\|(?:Disconnect \(address: (?<ip>[\d.]+):(?<port>\d+)\)|Statistics \(address: (?<ip>[\d.]+):(?<port>\d+), rtt: (?<rtt>[\d.]+), lose: (?<lose>\d+))",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex TimePrefixPattern = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled);

        private static readonly Regex SessionModePattern = new Regex(@"Session mode: (\w+)", RegexOptions.Compiled);

        // RealDateTime:09/09/2026 04:12:19  GameDateTime:09/09/2026 08:26:14  factor:7
        private static readonly Regex GameTimePattern = new Regex(
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+\|.*GameDateTime:(?<game>\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2})",
            RegexOptions.Compiled);

        private static readonly Regex SidRegionPattern = new Regex(@"^([A-Za-z]+-[A-Za-z]+)", RegexOptions.Compiled);

        // log_2026.08.28_12-49-01_<version>; hours may lack a leading zero
        private static readonly Regex FolderTimePattern = new Regex(
            @"log_(\d{4})\.(\d{2})\.(\d{2})_(\d{1,2})-(\d{1,2})-(\d{1,2})", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> MapNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bigmap", "Customs" },
            { "factory4_day", "Factory" },
            { "factory4_night", "Factory" },
            { "RezervBase", "Reserve" },
            { "laboratory", "Labs" },
            { "Sandbox", "Ground Zero" },
            { "Sandbox_high", "Ground Zero" },
            { "TarkovStreets", "Streets" },
        };

        public class Entry
        {
            public DateTime Time;
            public string Ip;
            public int Port;
            public string Map = "";
            public string Region = "";
            public string ShortId = "";
            public string Mode = "";
            public DateTime? GameTime;
            public double QueueSec = -1;
            public double LoadSec = -1;
            public double TotalSec = -1;
            public double SessionRtt = -1;
            public bool Ended;
            public string SessionDir = "";
        }

        private class RaidMeta
        {
            public DateTime Time;
            public string IpPort;
            public string Map;
            public string Region;
            public string ShortId;
        }

        private class EndEvent
        {
            public DateTime Time;
            public string IpPort;
            public double Rtt = -1;
        }

        private class TimingEvent
        {
            public DateTime Time;
            public string Kind;
            public double Real;
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

        public static List<Entry> Read(string logsPath, int max, TimeSpan window)
        {
            DateTime since = DateTime.Now - window;
            var entries = new List<Entry>();
            // session folder names sort chronologically: log_2026.08.28_12-49-01_<version>
            // parsed folder time, not name order: single-digit hours break lexicographic sort
            foreach (string dir in Directory.GetDirectories(logsPath, "log_*").OrderByDescending(FolderTime))
            {
                // a session started well before the window cannot contain raids inside it
                DateTime started = FolderTime(dir);
                if (started != DateTime.MinValue && started < since - TimeSpan.FromHours(24))
                    break;

                entries.AddRange(ReadSession(dir));

                if (entries.Count(e => e.Time >= since) >= max)
                    break;
            }
            return entries.Where(e => e.Time >= since).OrderByDescending(e => e.Time).Take(max).ToList();
        }

        public static List<Entry> ReadSession(string dir)
        {
            var raids = new List<Entry>();
            var ends = new List<EndEvent>();
            foreach (string file in Directory.GetFiles(dir, "*network-connection*.log"))
            {
                string text;
                try { text = ReadAllTextShared(file); }
                catch (IOException) { continue; }

                foreach (Match m in EndPattern.Matches(text))
                {
                    ends.Add(new EndEvent
                    {
                        Time = ParseTime(m.Groups["time"].Value),
                        IpPort = m.Groups["ip"].Value + ":" + m.Groups["port"].Value,
                        Rtt = m.Groups["rtt"].Success ? double.Parse(m.Groups["rtt"].Value, CultureInfo.InvariantCulture) : -1,
                    });
                }

                foreach (Match m in ConnectPattern.Matches(text))
                {
                    raids.Add(new Entry
                    {
                        Time = ParseTime(m.Groups["time"].Value),
                        Ip = m.Groups["ip"].Value,
                        Port = int.Parse(m.Groups["port"].Value),
                        SessionDir = dir,
                    });
                }
            }
            if (raids.Count == 0)
                return raids;

            var metas = new List<RaidMeta>();
            var timings = new List<TimingEvent>();
            var gameTimes = new List<KeyValuePair<DateTime, DateTime>>();
            var mapUnloads = new List<DateTime>();
            string mode = "";

            foreach (string file in Directory.GetFiles(dir, "*application*.log"))
            {
                foreach (string line in SafeReadLines(file))
                {
                    Match ps = ProfileStatusPattern.Match(line);
                    if (ps.Success)
                    {
                        Match region = SidRegionPattern.Match(ps.Groups["sid"].Value);
                        metas.Add(new RaidMeta
                        {
                            Time = ParseTime(ps.Groups["time"].Value),
                            IpPort = ps.Groups["ip"].Value + ":" + ps.Groups["port"].Value,
                            Map = MapNames.TryGetValue(ps.Groups["map"].Value, out string name) ? name : ps.Groups["map"].Value,
                            Region = region.Success ? region.Groups[1].Value.ToUpper() : "",
                            ShortId = ps.Groups["short"].Success ? ps.Groups["short"].Value : "",
                        });
                        continue;
                    }
                    Match timing = TimingPattern.Match(line);
                    if (timing.Success)
                    {
                        timings.Add(new TimingEvent
                        {
                            Time = ParseTime(timing.Groups["time"].Value),
                            Kind = timing.Groups["kind"].Value,
                            Real = double.Parse(timing.Groups["real"].Value, CultureInfo.InvariantCulture),
                        });
                        continue;
                    }
                    Match sessionMode = SessionModePattern.Match(line);
                    if (sessionMode.Success)
                        mode = ModeName(sessionMode.Groups[1].Value);
                }
            }

            foreach (string file in Directory.GetFiles(dir, "*output*.log"))
            {
                foreach (string line in SafeReadLines(file))
                {
                    Match gt = GameTimePattern.Match(line);
                    if (gt.Success)
                    {
                        gameTimes.Add(new KeyValuePair<DateTime, DateTime>(
                            ParseTime(gt.Groups["time"].Value),
                            DateTime.ParseExact(gt.Groups["game"].Value, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
                        continue;
                    }
                    // map unload marks the raid end when Disconnect was not logged
                    if (line.Contains("Disabling AcousticMap"))
                    {
                        Match prefix = TimePrefixPattern.Match(line);
                        if (prefix.Success)
                            mapUnloads.Add(ParseTime(prefix.Groups[1].Value));
                    }
                }
            }

            foreach (Entry raid in raids)
            {
                raid.Mode = mode;

                string ipPort = raid.Ip + ":" + raid.Port;
                RaidMeta meta = metas
                    .Where(m => m.IpPort == ipPort && Math.Abs((m.Time - raid.Time).TotalSeconds) <= 120)
                    .OrderBy(m => Math.Abs((m.Time - raid.Time).TotalSeconds))
                    .FirstOrDefault();
                if (meta != null)
                {
                    raid.Map = meta.Map;
                    raid.Region = meta.Region;
                    raid.ShortId = meta.ShortId;
                }

                // matching completes shortly before Connect; loading and start follow it
                TimingEvent queue = timings.LastOrDefault(t => t.Kind == "MatchingCompleted"
                    && t.Time <= raid.Time.AddSeconds(5) && t.Time >= raid.Time.AddMinutes(-10));
                TimingEvent loaded = timings.FirstOrDefault(t => t.Kind == "LocationLoaded"
                    && t.Time >= raid.Time.AddSeconds(-5) && t.Time <= raid.Time.AddMinutes(15));
                TimingEvent startedEvent = timings.FirstOrDefault(t => t.Kind == "GameStarted"
                    && t.Time >= raid.Time.AddSeconds(-5) && t.Time <= raid.Time.AddMinutes(30));

                if (queue != null)
                    raid.QueueSec = queue.Real;
                if (loaded != null && queue != null && loaded.Real >= queue.Real)
                    raid.LoadSec = loaded.Real - queue.Real;
                if (startedEvent != null)
                    raid.TotalSec = startedEvent.Real;

                var endsAfter = ends.Where(x => x.IpPort == ipPort && x.Time >= raid.Time).ToList();
                if (endsAfter.Count > 0)
                {
                    raid.Ended = true;
                    // Disconnect and Statistics share a timestamp; prefer the one carrying rtt
                    EndEvent withRtt = endsAfter.Where(x => x.Rtt >= 0).OrderBy(x => x.Time).FirstOrDefault();
                    if (withRtt != null)
                        raid.SessionRtt = withRtt.Rtt;
                }
                else if (mapUnloads.Any(u => u >= raid.Time.AddSeconds(30)))
                {
                    raid.Ended = true;
                }

                var gameTime = gameTimes.FirstOrDefault(g =>
                    g.Key >= raid.Time.AddSeconds(-5) && g.Key <= raid.Time.AddMinutes(15));
                if (gameTime.Key != default(DateTime))
                    raid.GameTime = gameTime.Value;
            }
            return raids;
        }

        private static string ModeName(string sessionMode)
        {
            switch (sessionMode)
            {
                case "Regular": return "PvP";
                case "PvpSeason": return "PvP Season";
                case "Pve": return "PvE";
                default: return sessionMode;
            }
        }

        private static DateTime FolderTime(string dir)
        {
            Match m = FolderTimePattern.Match(Path.GetFileName(dir));
            if (!m.Success)
                return DateTime.MinValue;
            return new DateTime(
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value));
        }

        private static DateTime ParseTime(string value)
        {
            return DateTime.ParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        // FileShare.ReadWrite: the game keeps the current session log open for writing
        private static string ReadAllTextShared(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        private static IEnumerable<string> ReadLinesShared(string file)
        {
            FileStream stream;
            try { stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (IOException) { yield break; }
            using (var reader = new StreamReader(stream))
            {
                while (true)
                {
                    string line;
                    try
                    {
                        line = reader.ReadLine();
                    }
                    catch (IOException)
                    {
                        yield break;
                    }
                    if (line == null)
                        yield break;
                    yield return line;
                }
            }
        }

        // output logs can be large; stream instead of loading whole files
        private static IEnumerable<string> SafeReadLines(string file)
        {
            IEnumerator<string> lines;
            try { lines = ReadLinesShared(file).GetEnumerator(); }
            catch (IOException) { yield break; }
            using (lines)
            {
                while (true)
                {
                    try
                    {
                        if (!lines.MoveNext())
                            yield break;
                    }
                    catch (IOException)
                    {
                        yield break;
                    }
                    yield return lines.Current;
                }
            }
        }
    }
}
