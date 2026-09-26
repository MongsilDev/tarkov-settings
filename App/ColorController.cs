using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using tarkov_settings.GPU;

namespace tarkov_settings
{
    class ColorController
    {
        IGPU gpu = GPUDevice.Instance;

        // Gamma Ramps
        private RAMP currentRamps;
        private RAMP originalRamps;

        /**
         * _canceller : Token Source to abort Async-Task (Gamma Value Change)
         * WHY : *I don't know why* set gamma ramp keeps revert soon after modified
         */
        private CancellationTokenSource _canceller;
        // serialises ramp writes between the apply loop and the UI thread
        private readonly object _gate = new object();

        // true while a custom ramp/DVL is applied; lets callers skip redundant resets
        public bool IsApplied { get; private set; }

        #region Singleton Pattern implement
        private static readonly Lazy<ColorController> instance =
            new Lazy<ColorController>(() => new ColorController());

        public static ColorController Instance
        {
            get
            {
                return instance.Value;
            }
        }
        #endregion

        #region Win32 API Calls
        [DllImport("gdi32")]
        private static extern bool GetDeviceGammaRamp(IntPtr hDc, ref RAMP lpRamp);

        [DllImport("gdi32")]
        private static extern bool SetDeviceGammaRamp(IntPtr hDc, ref RAMP lpRamp);
        #endregion

        public int DVL
        {
            get => gpu.Saturation;
            set
            {
                gpu.Saturation = value;
            }
        }

        private ColorController()
        {

        }

        public void Init()
        {
            // Backup Gamma Ramp
            var hdc = IntPtr.Zero;
            try
            {
                hdc = Display.CreateDC(null, Display.Primary, null, IntPtr.Zero);
                currentRamps = new RAMP();
                originalRamps = new RAMP();
                // a failed read (display not ready yet at autostart) would leave an all-zero
                // ramp as the restore target; fall back to the Windows default instead
                if (hdc == IntPtr.Zero || !GetDeviceGammaRamp(hdc, ref originalRamps))
                    originalRamps = IdentityRamp();
            }
            finally
            {
                if (!IntPtr.Zero.Equals(hdc))
                    Display.DeleteDC(hdc);
            }
        }

        public void ChangeColorRamp(double brightness = 0.5, double contrast = 0.5, double gamma = 1.0, bool reset = true)
        {
            // cancel the running apply loop without waiting for it: a blocking wait on
            // the UI thread pumps messages, and the focus hook re-enters this method.
            // the loop re-checks its token under _gate before every write, so once the
            // reset below holds the lock no stale ramp can land after it
            _canceller?.Cancel();
            _canceller = null;

            if (reset)
            {
                IsApplied = false;
                lock (_gate)
                    WriteRamp(Display.Primary, ref originalRamps);
                return;
            }

            IsApplied = true;
            ushort[] iArrayValue = CalculateLUT(brightness, contrast, gamma);
            currentRamps.Red = currentRamps.Blue = currentRamps.Green = iArrayValue;

            var canceller = new CancellationTokenSource();
            CancellationToken token = canceller.Token;
            string device = Display.Primary;
            RAMP ramps = currentRamps;
            _canceller = canceller;
            Task.Run(() =>
            {
                IntPtr hdc = IntPtr.Zero;
                try
                {
                    hdc = Display.CreateDC(null, device, null, IntPtr.Zero);
                    while (true)
                    {
                        lock (_gate)
                        {
                            if (token.IsCancellationRequested)
                                return;
                            SetDeviceGammaRamp(hdc, ref ramps);
                        }
                        // wakes immediately on cancellation instead of sleeping it out
                        if (token.WaitHandle.WaitOne(250))
                            return;
                    }
                }
                finally
                {
                    if (!IntPtr.Zero.Equals(hdc))
                        Display.DeleteDC(hdc);
                }
            });
        }

        private static RAMP IdentityRamp()
        {
            var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
            for (int i = 0; i < 256; i++)
                ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = (ushort)(i * 257);
            return ramp;
        }

        private static void WriteRamp(string device, ref RAMP ramp)
        {
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = Display.CreateDC(null, device, null, IntPtr.Zero);
                SetDeviceGammaRamp(hdc, ref ramp);
            }
            finally
            {
                if (!IntPtr.Zero.Equals(hdc))
                    Display.DeleteDC(hdc);
            }
        }

        /*
         * Code from
         * https://github.com/falahati/NvAPIWrapper/issues/20#issuecomment-634551206
         */
        private static ushort[] CalculateLUT(double brightness = 0.5, double contrast = 0.5, double gamma = 2.8)
        {
            const int dataPoints = 256;

            // Limit gamma in range [0.4-2.8]
            gamma = Math.Min(Math.Max(gamma, 0.4), 2.8);
            // Normalize contrast in range [-1,1]
            contrast = (Math.Min(Math.Max(contrast, 0), 1) - 0.5) * 2;
            // Normalize brightness in range [-1,1]
            brightness = (Math.Min(Math.Max(brightness, 0), 1) - 0.5) * 2;
            // Calculate curve offset resulted from contrast
            var offset = contrast > 0 ? contrast * -25.4 : contrast * -32;
            // Calculate the total range of curve
            var range = (dataPoints - 1) + offset * 2;
            // Add brightness to the curve offset
            offset += brightness * (range / 5);
            // Fill the gamma curve
            var result = new ushort[dataPoints];
            for (var i = 0; i < result.Length; i++)
            {
                var factor = (i + offset) / range;
                factor = Math.Pow(factor, 1 / gamma);
                factor = Math.Min(Math.Max(factor, 0), 1);
                result[i] = (ushort)Math.Round(factor * ushort.MaxValue);
            }
            return result;
        }

        public void ResetDVL()
        {
            try
            {
                gpu.ResetSaturation();
                Console.WriteLine("[DVL] Reset to : {0}", gpu.InitSaturation);
            }
            catch (NvAPIWrapper.Native.Exceptions.NVIDIAApiException) { }
        }

        internal void Close()
        {
            // nothing of ours is on screen: leave whatever was set meanwhile (night light,
            // driver panel) alone instead of forcing the startup values back
            if (!IsApplied)
                return;
            ResetDVL();
            ChangeColorRamp(reset: true);

        }

    }
}
