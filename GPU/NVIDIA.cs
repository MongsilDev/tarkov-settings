using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;
using Newtonsoft.Json;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Display.Structures;

namespace tarkov_settings.GPU
{
    class NVIDIA : IGPU
    {
        private GPUVendor _vendor;
        private DisplayHandle displayHandle;
        private string loadedDisplay;

        private int _maxSaturation;
        private int _minSaturation;
        private int _initSaturation;
        private int currentSaturation;

        public GPUVendor Vendor
        {
            get => this._vendor;
        }

        public int MaxSaturation
        {
            get => _maxSaturation;
        }

        public int MinSaturation
        {
            get => _minSaturation;
        }

        public int InitSaturation
        {
            get => _initSaturation;
        }

        // false until Load succeeds for the current display; a failed Load must not
        // leave the previous display's handle in use
        private bool hasDisplay;

        // display -> level it had before this app changed it. the driver stores DVC and
        // brings it back after a reboot, so after a crash or BSOD with a custom level
        // applied the next start would read that level as the initial one and "reset"
        // to it from then on
        private static readonly string BackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tarkov-settings", "dvc-backup.json");

        // true once the loaded display's initial level is on disk; saves a file
        // round trip on every slider tick
        private bool backedUp;

        public int Saturation
        {
            get => currentSaturation;
            set
            {
                if (!hasDisplay)
                    return;
                if (value > this.MaxSaturation)
                    value = this.MaxSaturation;
                if (value < this.MinSaturation)
                    value = this.MinSaturation;

                // on disk before the level changes: a crash right after the write must
                // still find the level to return to
                if (value != _initSaturation)
                    SaveBackup();

                // the handle goes stale on an RDP switch or monitor sleep while the display name
                // stays the same, so no Load follows: fetch a fresh handle and retry once.
                // the init level is not re-read - it may already hold the applied value
                bool written = false;
                try
                {
                    DisplayApi.SetDVCLevel(displayHandle, value);
                    this.currentSaturation = value;
                    written = true;
                }
                catch (Exception e) when (IsNvApiError(e))
                {
                    try
                    {
                        displayHandle = DisplayApi.GetAssociatedNvidiaDisplayHandle(loadedDisplay);
                        DisplayApi.SetDVCLevel(displayHandle, value);
                        this.currentSaturation = value;
                        written = true;
                    }
                    catch (Exception retry) when (IsNvApiError(retry)) { }
                }

                // back at the initial level: nothing left to recover. a failed reset keeps
                // the entry so the next Load can finish it
                if (written && value == _initSaturation)
                    ClearBackup();
            }
        }

        public NVIDIA(GPUVendor vendor)
        {
            try
            { 
                NvAPIWrapper.NVIDIA.Initialize();
            }
            catch (Exception e) when (IsNvApiError(e))
            {
                MessageBox.Show("NvAPI Intialize Failed", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            this._vendor = vendor;
        }

        public void ResetSaturation()
        {
            this.Saturation = this.InitSaturation;
        }

        public void Load(string display) {
            // before the levels are read: whatever a previous run left applied must not
            // become this run's initial level
            RestoreBackups();
            backedUp = false;

            try
            {
                // commit fields only when every call succeeded, so handle and
                // levels can never end up describing different displays
                DisplayHandle handle = DisplayApi.GetAssociatedNvidiaDisplayHandle(display);
                PrivateDisplayDVCInfo dvcInfo = DisplayApi.GetDVCInfo(handle);
                this.displayHandle = handle;
                this.loadedDisplay = display;
                this._maxSaturation = dvcInfo.MaximumLevel;
                this._minSaturation = dvcInfo.MinimumLevel;
                this._initSaturation = this.currentSaturation = dvcInfo.CurrentLevel;
                this.hasDisplay = true;
            }
            catch (Exception e) when (IsNvApiError(e))
            {
                // this display has no DVC (or NvAPI is unavailable right now): disable
                // saturation until a later Load succeeds instead of driving the old handle
                this.hasDisplay = false;
                this._maxSaturation = this._minSaturation = this._initSaturation = this.currentSaturation = 0;
            }
        }

        private void SaveBackup()
        {
            if (backedUp)
                return;
            Dictionary<string, int> levels = ReadBackup();
            // an entry already there predates this run's init level, so it is the real original
            if (!levels.ContainsKey(loadedDisplay))
            {
                levels[loadedDisplay] = _initSaturation;
                WriteBackup(levels);
            }
            backedUp = true;
        }

        private void ClearBackup()
        {
            if (!backedUp)
                return;
            Dictionary<string, int> levels = ReadBackup();
            if (levels.Remove(loadedDisplay))
                WriteBackup(levels);
            backedUp = false;
        }

        // entries only outlive a session that ended without resetting (crash, BSOD, kill)
        private static void RestoreBackups()
        {
            Dictionary<string, int> levels = ReadBackup();
            if (levels.Count == 0)
                return;

            foreach (KeyValuePair<string, int> entry in levels.ToList())
            {
                try
                {
                    DisplayHandle handle = DisplayApi.GetAssociatedNvidiaDisplayHandle(entry.Key);
                    DisplayApi.SetDVCLevel(handle, entry.Value);
                    levels.Remove(entry.Key);
                    Console.WriteLine("[DVL] Recovered {0} to : {1}", entry.Key, entry.Value);
                }
                // display not connected right now: keep its entry for a later start
                catch (Exception e) when (IsNvApiError(e)) { }
            }
            WriteBackup(levels);
        }

        private static Dictionary<string, int> ReadBackup()
        {
            try
            {
                if (File.Exists(BackupPath))
                    return JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(BackupPath))
                        ?? new Dictionary<string, int>();
            }
            // unreadable or corrupted: nothing to recover
            catch (Exception) { }
            return new Dictionary<string, int>();
        }

        // best-effort: a failing disk must not stop the level from being applied
        private static void WriteBackup(Dictionary<string, int> levels)
        {
            try
            {
                if (levels.Count == 0)
                {
                    File.Delete(BackupPath);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
                string temp = BackupPath + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(JsonConvert.SerializeObject(levels));
                    writer.Flush();
                    // past the OS cache: a BSOD seconds later must still find it
                    stream.Flush(true);
                }
                if (File.Exists(BackupPath))
                    File.Replace(temp, BackupPath, null);
                else
                    File.Move(temp, BackupPath);
            }
            catch (Exception e)
            {
                Console.WriteLine("[DVL] backup write failed: {0}", e.Message);
            }
        }

        // NvAPIWrapper reports an unsupported call (no DVC on this display or driver) with a
        // separate exception type, not a subclass of NVIDIAApiException
        private static bool IsNvApiError(Exception e)
        {
            return e is NvAPIWrapper.Native.Exceptions.NVIDIAApiException
                || e is NvAPIWrapper.Native.Exceptions.NVIDIANotSupportedException;
        }

        public void Close() {
            try
            {
                NvAPIWrapper.NVIDIA.Unload();
            }
            catch (NvAPIWrapper.Native.Exceptions.NVIDIAApiException)
            {
                MessageBox.Show("NvAPI Unload Failed", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
