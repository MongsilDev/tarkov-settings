using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace tarkov_settings
{
    static class VolumeController
    {
        /**
         * Toggle audio session volume of every target process between low and high (0.0 ~ 1.0).
         * Direction follows the loudest target session: above low goes low, otherwise high.
         */
        public static void Toggle(float low, float high)
        {
            // settings are hand-edited; clamp and reorder so the toggle can never lock up
            low = Math.Max(0f, Math.Min(1f, low));
            high = Math.Max(0f, Math.Min(1f, high));
            if (low > high)
            {
                float swap = low;
                low = high;
                high = swap;
            }

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    var targets = new System.Collections.Generic.List<SimpleAudioVolume>();
                    float loudest = -1f;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        string pName = GetProcessName(session.GetProcessID);
                        if (pName == null || !ProcessMonitor.Instance.IsTarget(pName))
                            continue;

                        targets.Add(session.SimpleAudioVolume);
                        loudest = Math.Max(loudest, session.SimpleAudioVolume.Volume);
                    }

                    if (targets.Count == 0)
                        return;

                    float level = loudest > low + 0.005f ? low : high;
                    foreach (var volume in targets)
                        volume.Volume = Math.Max(0f, Math.Min(1f, level));
                    Console.WriteLine("[volume] -> {0:P0}", level);
                }
            }
            catch (Exception e)
            {
                // no audio device or session enumeration failure - ignore
                Console.WriteLine("[volume] {0}", e.Message);
            }
        }

        private static string GetProcessName(uint pid)
        {
            try
            {
                return Process.GetProcessById((int)pid).ProcessName.ToLower();
            }
            catch
            {
                return null;
            }
        }
    }
}
