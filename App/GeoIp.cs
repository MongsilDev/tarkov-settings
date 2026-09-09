using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace tarkov_settings
{
    static class GeoIp
    {
        public class Info
        {
            public string country;
            public string city;
            public string timezone;
        }

        private class ApiRow
        {
            public string status;
            public string query;
            public string country;
            public string city;
            public string timezone;
        }

        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tarkov-settings", "geoip.json");

        private static Dictionary<string, Info> cache;

        /**
         * Resolve country/city/timezone for the given IPs. Cached to disk, so only
         * unseen IPs hit the API (ip-api.com batch endpoint, 100 per request).
         */
        public static async Task<Dictionary<string, Info>> LookupAsync(IEnumerable<string> ips)
        {
            if (cache == null)
                cache = LoadCache();

            List<string> missing = ips.Distinct().Where(ip => !cache.ContainsKey(ip)).ToList();
            for (int i = 0; i < missing.Count; i += 100)
            {
                var chunk = missing.Skip(i).Take(100)
                    .Select(ip => new { query = ip, fields = "status,query,country,city,timezone" });
                var body = new StringContent(JsonConvert.SerializeObject(chunk), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await client.PostAsync("http://ip-api.com/batch", body);
                response.EnsureSuccessStatusCode();
                var rows = JsonConvert.DeserializeObject<List<ApiRow>>(await response.Content.ReadAsStringAsync());

                foreach (ApiRow row in rows)
                {
                    if (row.status == "success" && row.query != null)
                        cache[row.query] = new Info { country = row.country, city = row.city, timezone = row.timezone };
                }
            }

            if (missing.Count > 0)
                SaveCache();
            return cache;
        }

        private static Dictionary<string, Info> LoadCache()
        {
            try
            {
                if (File.Exists(CachePath))
                    return JsonConvert.DeserializeObject<Dictionary<string, Info>>(File.ReadAllText(CachePath))
                        ?? new Dictionary<string, Info>();
            }
            catch (Exception) { }
            return new Dictionary<string, Info>();
        }

        private static void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, JsonConvert.SerializeObject(cache));
            }
            catch (Exception) { }
        }
    }
}
