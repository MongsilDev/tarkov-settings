using System;

namespace tarkov_settings.GPU
{
    /**
     * Saturation is not implemented for AMD. Everything here is a no-op so the
     * gamma-ramp path keeps working and shutdown never throws.
     */
    class AMD : IGPU
    {
        private GPUVendor _vendor;

        public GPUVendor Vendor
        {
            get => this._vendor;
        }

        public int MaxSaturation => 0;
        public int MinSaturation => 0;
        public int InitSaturation => 0;
        public int Saturation { get; set; }

        public AMD(GPUVendor vendor)
        {
            this._vendor = vendor;
        }

        public void ResetSaturation() { }

        public void Load(string display) { }

        public void Close() { }
    }
}
