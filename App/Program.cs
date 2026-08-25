using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using tarkov_settings.GPU;

namespace tarkov_settings
{
    static class Program
    {
        private static MainForm mForm;

        // Broadcast to notify the running instance when a duplicate launch occurs
        public static readonly int WM_ALREADY_RUNNING = RegisterWindowMessage("TARKOV_SETTINGS_ALREADY_RUNNING");
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        static void Main()
        {
            using (Mutex mutex = new Mutex(true, "tarkov-settings-instance", out bool createdNew))
            {
                if (!createdNew)
                {
                    PostMessage(HWND_BROADCAST, WM_ALREADY_RUNNING, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Run();
            }
        }

        static void Run()
        {
            IGPU gpu = null;
            try
            {
                gpu = GPUDevice.Instance;
                if(gpu.Vendor == GPUVendor.AMD)
                {
                    /* AMD Saturation (equals to Digital Vibrance of Nvidia) is not supported yet. */
                    System.Windows.Forms.MessageBox.Show(
                            "AMD Device Detected - Saturation is not supported yet.",
                            "Warning",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning
                        );
                }
            } catch (NotImplementedException)
            {
                System.Windows.Forms.MessageBox.Show(
                        "Intel/Nvidia Optimus/Etc Device Detected - Will be supported soon",
                        "Nvidia GPU is not found!",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error
                    );
                // Application.Exit() is a no-op before the message loop starts
                return;
            }

            // Open Main Form
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            mForm = new MainForm();
            Application.Run(mForm);

            // Unload NvAPI dll after Application.Exit()
            if(gpu != null)
                gpu.Close();
        }
    }
}
