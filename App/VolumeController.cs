using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace tarkov_settings
{
    static class VolumeController
    {
        /**
         * Toggle audio session volume of every target process between low and high (0.0 ~ 1.0).
         * Direction follows the loudest target session: above low goes low, otherwise high.
         * All active render devices are scanned, since the game may be routed to a non-default one.
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
                // one process snapshot instead of one lookup per audio session
                var names = new Dictionary<int, string>();
                foreach (Process process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try { names[process.Id] = process.ProcessName.ToLower(); }
                        catch (Exception) { }
                    }
                }

                // two passes: the direction comes from the loudest session on any device, so a
                // game split across two outputs never ends up toggled both ways
                float loudest = -1f;
                ForEachTargetSession(names, volume => loudest = Math.Max(loudest, volume.Volume));
                if (loudest < 0f)
                    return;

                float level = loudest > low + 0.005f ? low : high;
                ForEachTargetSession(names, volume => volume.Volume = level);
                Console.WriteLine("[volume] -> {0:P0}", level);
            }
            catch (Exception e)
            {
                // no audio device or session enumeration failure - ignore
                Console.WriteLine("[volume] {0}", e.Message);
            }
        }

        private static void ForEachTargetSession(Dictionary<int, string> names, Action<SimpleAudioVolume> action)
        {
            using (var enumerator = new MMDeviceEnumerator())
            {
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    using (device)
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];
                            if (names.TryGetValue((int)session.GetProcessID, out string pName)
                                && ProcessMonitor.Instance.IsTarget(pName))
                                action(session.SimpleAudioVolume);
                        }
                    }
                }
            }
        }
    }
}
