using System;
using System.Drawing;
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

        #region Toggle Hotkeys (volume / gamma)
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_VOLUME_TOGGLE = 1;
        private const int HOTKEY_GAMMA_TOGGLE = 2;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // "Ctrl+Alt+PageDown" style, key names follow the WinForms Keys enum
        private static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrEmpty(hotkey))
                return false;

            foreach (string part in hotkey.Split('+'))
            {
                switch (part.Trim().ToLower())
                {
                    case "ctrl":
                    case "control": modifiers |= MOD_CONTROL; break;
                    case "alt": modifiers |= MOD_ALT; break;
                    case "shift": modifiers |= MOD_SHIFT; break;
                    case "win": modifiers |= MOD_WIN; break;
                    default:
                        if (!Enum.TryParse(part.Trim(), true, out Keys key))
                            return false;
                        vk = (uint)key;
                        break;
                }
            }
            return vk != 0;
        }

        // hotkeys are only registered while a target game is focused, so plain keys
        // like PageDown keep working in every other app
        private bool hotkeysActive;

        private bool TryRegisterHotkey(int id, string hotkey)
        {
            return TryParseHotkey(hotkey, out uint modifiers, out uint vk)
                && RegisterHotKey(this.Handle, id, modifiers | MOD_NOREPEAT, vk);
        }

        public void SetHotkeysActive(bool active)
        {
            hotkeysActive = active;
            UnregisterHotKey(this.Handle, HOTKEY_VOLUME_TOGGLE);
            UnregisterHotKey(this.Handle, HOTKEY_GAMMA_TOGGLE);
            if (active)
            {
                TryRegisterHotkey(HOTKEY_VOLUME_TOGGLE, appSetting.volumeToggleHotkey);
                TryRegisterHotkey(HOTKEY_GAMMA_TOGGLE, appSetting.gammaToggleHotkey);
            }
        }

        // ShowInTaskbar toggling recreates the handle, so (re)register here
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (appSetting != null && hotkeysActive)
                SetHotkeysActive(true);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(this.Handle, HOTKEY_VOLUME_TOGGLE);
            UnregisterHotKey(this.Handle, HOTKEY_GAMMA_TOGGLE);
            base.OnHandleDestroyed(e);
        }

        private void ToggleGamma()
        {
            double low = Math.Min(appSetting.gammaLow, appSetting.gammaHigh);
            double high = Math.Max(appSetting.gammaLow, appSetting.gammaHigh);
            Gamma = Gamma >= (low + high) / 2 ? low : high;
            pMonitor.Reapply();
        }

        private static string BuildHotkeyString(Keys modifiers, Keys key)
        {
            string hotkey = "";
            if ((modifiers & Keys.Control) != 0) hotkey += "Ctrl+";
            if ((modifiers & Keys.Alt) != 0) hotkey += "Alt+";
            if ((modifiers & Keys.Shift) != 0) hotkey += "Shift+";
            return hotkey + key.ToString();
        }

        private static string HotkeyDisplay(string hotkey)
        {
            return string.IsNullOrEmpty(hotkey) ? "None" : hotkey;
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            // arrows etc. reach KeyDown to be captured; Tab keeps moving focus
            e.IsInputKey = e.KeyCode != Keys.Tab;
        }

        private void HotkeyTextBox_Enter(object sender, EventArgs e)
        {
            var box = (TextBox)sender;
            box.BackColor = SystemColors.Window;
            box.Text = "Press a key";
        }

        private void HotkeyTextBox_Leave(object sender, EventArgs e)
        {
            var box = (TextBox)sender;
            box.BackColor = SystemColors.Control;
            box.Text = HotkeyDisplay(box == gammaHotkeyTextBox ? appSetting.gammaToggleHotkey : appSetting.volumeToggleHotkey);
        }

        // unmodified letters/digits would swallow normal typing in every app
        private static bool NeedsModifier(Keys key)
        {
            return (key >= Keys.A && key <= Keys.Z)
                || (key >= Keys.D0 && key <= Keys.D9)
                || key == Keys.Space
                || (key >= Keys.Oem1 && key <= Keys.OemBackslash);
        }

        private void HotkeyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            var box = (TextBox)sender;
            Keys key = e.KeyCode;

            if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey
                || key == Keys.LWin || key == Keys.RWin)
                return;

            if (key == Keys.Escape || key == Keys.Return)
            {
                this.ActiveControl = null;
                return;
            }

            bool isGamma = box == gammaHotkeyTextBox;
            int id = isGamma ? HOTKEY_GAMMA_TOGGLE : HOTKEY_VOLUME_TOGGLE;
            string previous = isGamma ? appSetting.gammaToggleHotkey : appSetting.volumeToggleHotkey;
            string hotkey;

            if (key == Keys.Back || key == Keys.Delete)
            {
                hotkey = "";
            }
            else if (e.Modifiers == Keys.None && NeedsModifier(key))
            {
                hintToolTip.Show("Use F-keys or add Ctrl/Alt/Shift", box, 0, -22, 2000);
                return;
            }
            else
            {
                hotkey = BuildHotkeyString(e.Modifiers, key);
                // probe availability; the live registration happens only while a game is focused
                UnregisterHotKey(this.Handle, id);
                bool available = TryRegisterHotkey(id, hotkey);
                UnregisterHotKey(this.Handle, id);
                if (!available)
                {
                    hintToolTip.Show("That key is already in use", box, 0, -22, 2000);
                    hotkey = previous;
                }
            }

            if (isGamma)
                appSetting.gammaToggleHotkey = hotkey;
            else
                appSetting.volumeToggleHotkey = hotkey;
            if (hotkeysActive)
                SetHotkeysActive(true);

            this.ActiveControl = null;
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
            this.hotkeyTextBox.Text = HotkeyDisplay(appSetting.volumeToggleHotkey);
            // capture both before assigning - the shared ValueChanged handler writes the
            // sibling control's (not yet initialized) value back into appSetting
            int volumeLow = Math.Min(100, Math.Max(0, appSetting.volumeLow));
            int volumeHigh = Math.Min(100, Math.Max(0, appSetting.volumeHigh));
            this.volumeLowNum.Value = volumeLow;
            this.volumeHighNum.Value = volumeHigh;
            appSetting.volumeLow = volumeLow;
            appSetting.volumeHigh = volumeHigh;

            this.gammaHotkeyTextBox.Text = HotkeyDisplay(appSetting.gammaToggleHotkey);
            decimal gammaLow = ClampToNum(gammaLowNum, (decimal)appSetting.gammaLow);
            decimal gammaHigh = ClampToNum(gammaHighNum, (decimal)appSetting.gammaHigh);
            this.gammaLowNum.Value = gammaLow;
            this.gammaHighNum.Value = gammaHigh;
            appSetting.gammaLow = (double)gammaLow;
            appSetting.gammaHigh = (double)gammaHigh;
        }

        #region BCGS Getter/Setter
        // out-of-range saved values (e.g. from older builds) must not crash the TrackBar setter
        private static int ClampToBar(TrackBar bar, int value)
        {
            return Math.Min(Math.Max(value, bar.Minimum), bar.Maximum);
        }

        public double Brightness
        {
            get => BrightnessBar.Value / 100.0;
            set => BrightnessBar.Value = ClampToBar(BrightnessBar, (int)(value * 100));
        }

        public double Contrast
        {
            get => ContrastBar.Value / 100.0;
            set => ContrastBar.Value = ClampToBar(ContrastBar, (int)(value * 100));
        }

        public double Gamma
        {
            get => GammaBar.Value / 100.0;
            set => GammaBar.Value = ClampToBar(GammaBar, (int)(value * 100));
        }

        public int DVL
        {
            get => DVLBar.Value;
            set => DVLBar.Value = ClampToBar(DVLBar, value);
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
            // a fresh install always shows the window once, whatever the minimize default is
            if (minimizeOnStart && !AppSetting.FirstRun)
            {
                this.Visible = false;
                this.ShowInTaskbar = false;
                this.trayIcon.ShowBalloonTip(
                    2500,
                    "Tarkov Settings is running in the tray",
                    "Double-click the tray icon to open settings",
                    ToolTipIcon.Info
                    );
            }
        }

        #region Control Event Handlers
        private void ColorLabel_DClick(object sender, EventArgs e)
        {
            var label = sender as Label;

            // identity values - the display looks exactly as Windows renders it
            if (label.Equals(BrightnessLabel))
            {
                BrightnessBar.Value = 50;
            }
            else if (label.Equals(ContrastLabel))
            {
                ContrastBar.Value = 50;
            }
            else if (label.Equals(GammaLabel))
            {
                GammaBar.Value = 100;
            }
            else if (label.Equals(DVLLabel))
            {
                DVLBar.Value = 0;
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
        // follow the game window to whichever monitor it is on
        public void FollowWindowDisplay(IntPtr hWnd)
        {
            string device = Screen.FromHandle(hWnd).DeviceName;
            int index = DisplayCombo.FindStringExact(device);
            if (index != -1 && index != DisplayCombo.SelectedIndex)
                DisplayCombo.SelectedIndex = index;
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
                    "Tarkov Settings is already running",
                    "Double-click the tray icon to open settings",
                    ToolTipIcon.Info
                    );
            }
            else if (m.Msg == WM_HOTKEY)
            {
                switch ((int)m.WParam)
                {
                    case HOTKEY_VOLUME_TOGGLE:
                        VolumeController.Toggle(appSetting.volumeLow / 100f, appSetting.volumeHigh / 100f);
                        break;
                    case HOTKEY_GAMMA_TOGGLE:
                        ToggleGamma();
                        break;
                }
            }
            base.WndProc(ref m);
        }

        private void ShowForm(object sender, EventArgs e)
        {
            this.Visible = true;
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.Activate();
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

        private void VolumeLevel_ValueChanged(object sender, EventArgs e)
        {
            appSetting.volumeLow = (int)volumeLowNum.Value;
            appSetting.volumeHigh = (int)volumeHighNum.Value;
        }

        private void GammaLevel_ValueChanged(object sender, EventArgs e)
        {
            appSetting.gammaLow = (double)gammaLowNum.Value;
            appSetting.gammaHigh = (double)gammaHighNum.Value;
        }

        private static decimal ClampToNum(NumericUpDown num, decimal value)
        {
            return Math.Min(Math.Max(value, num.Minimum), num.Maximum);
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
