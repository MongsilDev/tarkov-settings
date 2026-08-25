using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using tarkov_settings.Setting;
using tarkov_settings.GPU;

namespace tarkov_settings
{
    public partial class MainForm : Form
    {
        private const string ARENA_PROCESS = "EscapeFromTarkovArena";

        #region Volume Hotkey (Ctrl+Alt+PageUp / Ctrl+Alt+PageDown)
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_VOLUME_UP = 1;
        private const int HOTKEY_VOLUME_DOWN = 2;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ShowInTaskbar toggling recreates the handle, so (re)register here
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotKey(this.Handle, HOTKEY_VOLUME_UP, MOD_CONTROL | MOD_ALT, (uint)Keys.PageUp);
            RegisterHotKey(this.Handle, HOTKEY_VOLUME_DOWN, MOD_CONTROL | MOD_ALT, (uint)Keys.PageDown);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(this.Handle, HOTKEY_VOLUME_UP);
            UnregisterHotKey(this.Handle, HOTKEY_VOLUME_DOWN);
            base.OnHandleDestroyed(e);
        }
        #endregion

        private ProcessMonitor pMonitor = ProcessMonitor.Instance;
        private IGPU gpu = GPUDevice.Instance;
        private AppSetting appSetting;

        private bool minimizeOnStart = false;

        public MainForm()
        {
            InitializeComponent();

            #region Load App Settings
            // Load Settings
            appSetting = AppSetting.Load();

            Brightness = appSetting.brightness;
            Contrast = appSetting.contrast;
            Gamma = appSetting.gamma;
            DVL = appSetting.saturation;
            minimizeOnStart = appSetting.minimizeOnStart;
            this.minimizeStartCheckBox.Checked = minimizeOnStart;

            this.autostartCheckBox.Checked = appSetting.autostart;
            // handler fires only on change, so force-sync the registry with the saved state
            Autostart.Enabled = appSetting.autostart;
            #endregion
            
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = String.Format("Tarkov Settings {0}", version);
            _ = new UpdateNotifier(version);

            // Saturation Initialize
            if (gpu.Vendor != GPUVendor.NVIDIA)
                DVLGroupBox.Enabled = false;

            #region Initialize Display
            // Initialize Display Dropdown
            foreach (string display in Display.displays)
            {
                DisplayCombo.Items.Add(display);
            }
            
            if(DisplayCombo.FindString(appSetting.display) != -1)
                DisplayCombo.SelectedIndex = DisplayCombo.FindString(appSetting.display);

            Display.Primary = (string)DisplayCombo.SelectedItem;
            #endregion

            // Initialize Process Monitor
            pMonitor.Parent = this;
            foreach (string pTarget in appSetting.pTargets)
            {
                pMonitor.Add(pTarget.ToLower());
            }
            pMonitor.Init();

            this.arenaCheckBox.Checked = appSetting.pTargets.Contains(ARENA_PROCESS);
        }

        #region BCGS Getter/Setter
        public double Brightness
        {
            get => BrightnessBar.Value / 100.0;
            set => BrightnessBar.Value = (int)(value * 100);
        }

        public double Contrast
        {
            get => ContrastBar.Value / 100.0;
            set => ContrastBar.Value = (int)(value * 100);
        }

        public double Gamma
        {
            get => GammaBar.Value / 100.0;
            set => GammaBar.Value = (int)(value * 100);
        }

        public int DVL
        {
            get => DVLBar.Value;
            set => DVLBar.Value = value;
        }

        public (double, double, double, int) GetColorValue()
        {
            return (
                BrightnessBar.Value / 100.0,
                ContrastBar.Value / 100.0,
                GammaBar.Value / 100.0,
                DVLBar.Value
                );
        }
        #endregion

        public bool IsEnabled { get=> this.enableToolStripMenuItem.Checked;}

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (minimizeOnStart)
            {
                this.Visible = false;
                this.ShowInTaskbar = false;
                this.trayIcon.ShowBalloonTip(
                    2500,
                    "Tarkov Settings Initailized!",
                    "Check out tray to modify your color setting",
                    ToolTipIcon.Info
                    );
            }
        }

        #region Control Event Handlers
        private void ColorLabel_DClick(object sender, EventArgs e)
        {
            var label = sender as Label;
            var def = new AppSetting();

            if (label.Equals(BrightnessLabel))
            {
                BrightnessBar.Value = (int)(def.brightness * 100);
            }
            else if (label.Equals(ContrastLabel))
            {
                ContrastBar.Value = (int)(def.contrast * 100);
            }
            else if (label.Equals(GammaLabel))
            {
                GammaBar.Value = (int)(def.gamma * 100);
            }
            else if (label.Equals(DVLLabel))
            {
                DVLBar.Value = def.saturation;
            }
        }
        private void TrackBar_ValueChanged(object sender, EventArgs e)
        {
            var trackBar = sender as TrackBar;

            if (trackBar.Equals(BrightnessBar))
            {
                BrightnessText.Text = (BrightnessBar.Value / 100.0).ToString("0.00");
            }
            else if (trackBar.Equals(ContrastBar))
            {
                ContrastText.Text = (ContrastBar.Value / 100.0).ToString("0.00");
            }
            else if (trackBar.Equals(GammaBar))
            {
                GammaText.Text = (GammaBar.Value / 100.0).ToString("0.00");
            }
            else if (trackBar.Equals(DVLBar))
            {
                DVLText.Text = DVLBar.Value.ToString();
            }
        }
        private void DisplayCombo_SelectedValueChanged(object sender, EventArgs e)
        {
            string selectedDisplay = (string)DisplayCombo.SelectedItem;
            Display.Primary = selectedDisplay;

            if(Display.Primary != selectedDisplay)
            {
                DisplayCombo.SelectedIndex = DisplayCombo.FindString(Display.Primary);
            }
        }
        #endregion

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_ALREADY_RUNNING)
            {
                this.trayIcon.ShowBalloonTip(
                    2500,
                    "Tarkov Settings is already running!",
                    "Check out tray to modify your color setting",
                    ToolTipIcon.Info
                    );
            }
            else if (m.Msg == WM_HOTKEY)
            {
                float step = appSetting.volumeStep / 100f;
                if ((int)m.WParam == HOTKEY_VOLUME_UP)
                    VolumeController.Adjust(step);
                else if ((int)m.WParam == HOTKEY_VOLUME_DOWN)
                    VolumeController.Adjust(-step);
            }
            base.WndProc(ref m);
        }

        private void ShowForm(object sender, EventArgs e)
        {
            this.Visible = true;
            this.ShowInTaskbar = true;
        }

        private void ExitFormClicked(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void SaveSettings()
        {
            appSetting.brightness = Brightness;
            appSetting.contrast = Contrast;
            appSetting.gamma = Gamma;
            appSetting.saturation = DVL;
            // keep the last saved display when nothing is selected (e.g. monitor unplugged)
            if (DisplayCombo.SelectedItem != null)
                appSetting.display = (string)DisplayCombo.SelectedItem;
            appSetting.minimizeOnStart = minimizeOnStart;
            appSetting.autostart = autostartCheckBox.Checked;
            if (arenaCheckBox.Checked)
                appSetting.pTargets.Add(ARENA_PROCESS);
            else
                appSetting.pTargets.Remove(ARENA_PROCESS);
            appSetting.Save();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                SaveSettings();
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                // covers windows shutdown as well, not only the tray Exit menu
                SaveSettings();

                Console.WriteLine(e.CloseReason);
                this.trayIcon.Dispose();
                Console.WriteLine("[mainForm] Closing pMonitor");
                pMonitor.Close();
            }
        }

        private void CheckOnMinimizeToTray(object sender, EventArgs e)
        {
            this.minimizeOnStart = this.minimizeStartCheckBox.Checked;
        }

        private void CheckOnAutostart(object sender, EventArgs e)
        {
            Autostart.Enabled = this.autostartCheckBox.Checked;
        }

        // applied at the next focus change, no restart needed
        private void CheckOnArena(object sender, EventArgs e)
        {
            if (this.arenaCheckBox.Checked)
                pMonitor.Add(ARENA_PROCESS.ToLower());
            else
                pMonitor.Remove(ARENA_PROCESS.ToLower());
        }
    }
}
