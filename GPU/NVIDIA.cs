using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;
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

                // the handle goes stale on an RDP switch or monitor sleep while the display name
                // stays the same, so no Load follows: fetch a fresh handle and retry once.
                // the init level is not re-read - it may already hold the applied value
                try
                {
                    DisplayApi.SetDVCLevel(displayHandle, value);
                    this.currentSaturation = value;
                }
                catch (Exception e) when (IsNvApiError(e))
                {
                    try
                    {
                        displayHandle = DisplayApi.GetAssociatedNvidiaDisplayHandle(loadedDisplay);
                        DisplayApi.SetDVCLevel(displayHandle, value);
                        this.currentSaturation = value;
                    }
                    catch (Exception retry) when (IsNvApiError(retry)) { }
                }
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
