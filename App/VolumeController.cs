using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace tarkov_settings
{
    static class VolumeController
    {
        /**
         * Adjust audio session volume of every target process (delta: -1.0 ~ 1.0)
         */
        public static void Adjust(float delta)
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        string pName = GetProcessName(session.GetProcessID);
                        if (pName == null || !ProcessMonitor.Instance.IsTarget(pName))
                            continue;

                        var volume = session.SimpleAudioVolume;
                        volume.Volume = Math.Max(0f, Math.Min(1f, volume.Volume + delta));
                        Console.WriteLine("[volume] {0} -> {1:P0}", pName, volume.Volume);
                    }
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
