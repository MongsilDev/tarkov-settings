using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        private const int HOTKEY_KILL = 3;
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

        private static readonly int[] HOTKEY_IDS = { HOTKEY_VOLUME_TOGGLE, HOTKEY_GAMMA_TOGGLE, HOTKEY_KILL };

        private string GetHotkey(int id)
        {
            switch (id)
            {
                case HOTKEY_GAMMA_TOGGLE: return appSetting.gammaToggleHotkey;
                case HOTKEY_KILL: return appSetting.killHotkey;
                default: return appSetting.volumeToggleHotkey;
            }
        }

        private void SetHotkey(int id, string hotkey)
        {
            switch (id)
            {
                case HOTKEY_GAMMA_TOGGLE: appSetting.gammaToggleHotkey = hotkey; break;
                case HOTKEY_KILL: appSetting.killHotkey = hotkey; break;
                default: appSetting.volumeToggleHotkey = hotkey; break;
            }
        }

        private int HotkeyIdOf(TextBox box)
        {
            if (box == gammaHotkeyTextBox) return HOTKEY_GAMMA_TOGGLE;
            if (box == killHotkeyTextBox) return HOTKEY_KILL;
            return HOTKEY_VOLUME_TOGGLE;
        }

        public void SetHotkeysActive(bool active)
        {
            hotkeysActive = active;
            foreach (int id in HOTKEY_IDS)
            {
                UnregisterHotKey(this.Handle, id);
                if (active)
                    TryRegisterHotkey(id, GetHotkey(id));
            }
            displayFollowTimer.Enabled = active;
        }

        // the focus hook only fires on focus changes; a game dragged to another monitor
        // while focused is caught by this 1 s poll
        private readonly Timer displayFollowTimer = new Timer { Interval = 1000 };

        private void DisplayFollowTimer_Tick(object sender, EventArgs e)
        {
            IntPtr hWnd = pMonitor.FocusedTargetHwnd;
            if (hWnd == IntPtr.Zero || Screen.FromHandle(hWnd).DeviceName == Display.Primary)
                return;
            FollowWindowDisplay(hWnd);
            // switching Display.Primary resets IsApplied, so apply unconditionally
            pMonitor.ApplyCurrent();
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
            foreach (int id in HOTKEY_IDS)
                UnregisterHotKey(this.Handle, id);
            base.OnHandleDestroyed(e);
        }

        private bool killing;

        // no prompt here - the user already confirmed when binding the key
        private void KillFocusedGame()
        {
            if (killing)
                return;
            int pid = pMonitor.FocusedTargetPid;
            string name = pMonitor.FocusedTargetName;

            // nothing to do for a game that already exited (its pid may have been reused)
            if (!pMonitor.IsTarget(pid, name))
                return;

            killing = true;
            try
            {
                if (!pMonitor.KillTarget(pid, name))
                {
                    // TopMost owner on the game's monitor so the notice shows over a borderless window
                    Screen screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Display.Primary) ?? Screen.PrimaryScreen;
                    using (var owner = new Form { TopMost = true, StartPosition = FormStartPosition.Manual, Bounds = screen.Bounds })
                    {
                        MessageBox.Show(owner,
                            "Could not terminate " + name + ".exe (already exited or access denied).",
                            "Kill game",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            finally
            {
                killing = false;
            }
        }

        // binding is the moment to warn: pressing the key later ends the game without asking
        private bool ConfirmKillBinding(string hotkey)
        {
            DialogResult answer = MessageBox.Show(this,
                "Pressing " + hotkey + " while the game is focused will end the game process immediately, without asking again.\n" +
                "Unsaved progress will be lost.\n\nBind this key?",
                "Kill game hotkey",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return answer == DialogResult.Yes;
        }

        private void ToggleGamma()
        {
            double low = Math.Min(appSetting.gammaLow, appSetting.gammaHigh);
            double high = Math.Max(appSetting.gammaLow, appSetting.gammaHigh);
            Gamma = Gamma >= (low + high) / 2 ? low : high;
        }

        private static string BuildHotkeyString(Keys modifiers, Keys key)
        {
            string hotkey = "";
            if ((modifiers & Keys.Control) != 0) hotkey += "Ctrl+";
            if ((modifiers & Keys.Alt) != 0) hotkey += "Alt+";
            if ((modifiers & Keys.Shift) != 0) hotkey += "Shift+";
            return hotkey + KeyName(key);
        }

        // Keys has duplicate members (PageDown/Next, PageUp/Prior...) and ToString() may
        // pick either name; pin the ones users actually see
        private static string KeyName(Keys key)
        {
            switch (key)
            {
                case Keys.PageDown: return "PageDown";
                case Keys.PageUp: return "PageUp";
                case Keys.Capital: return "CapsLock";
                default: return key.ToString();
            }
        }

        private static bool SameHotkey(string a, string b)
        {
            return TryParseHotkey(a, out uint ma, out uint va)
                && TryParseHotkey(b, out uint mb, out uint vb)
                && ma == mb && va == vb;
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
            // while capturing, a currently registered key must reach the box, not fire the toggle
            foreach (int id in HOTKEY_IDS)
                UnregisterHotKey(this.Handle, id);
            var box = (TextBox)sender;
            box.BackColor = SystemColors.Window;
            box.Text = "Press a key";
        }

        private void HotkeyTextBox_Leave(object sender, EventArgs e)
        {
            if (hotkeysActive)
                SetHotkeysActive(true);
            var box = (TextBox)sender;
            box.BackColor = SystemColors.Control;
            box.Text = HotkeyDisplay(GetHotkey(HotkeyIdOf(box)));
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

            int id = HotkeyIdOf(box);
            string previous = GetHotkey(id);
            string hotkey;

            if (key == Keys.Back || key == Keys.Delete)
            {
                hotkey = "";
            }
            else if (e.Modifiers == Keys.None && (id == HOTKEY_KILL || NeedsModifier(key)))
            {
                // the kill key always needs a modifier so a stray keypress can never trigger it
                hintToolTip.Show(id == HOTKEY_KILL ? "Kill key needs Ctrl/Alt/Shift" : "Use F-keys or add Ctrl/Alt/Shift", box, 0, -22, 2000);
                return;
            }
            else
            {
                hotkey = BuildHotkeyString(e.Modifiers, key);

                foreach (int other in HOTKEY_IDS)
                {
                    if (other != id && SameHotkey(GetHotkey(other), hotkey))
                    {
                        hintToolTip.Show("Already used by another hotkey", box, 0, -22, 2000);
                        return;
                    }
                }

                // probe availability; the live registration happens only while a game is focused
                UnregisterHotKey(this.Handle, id);
                bool available = TryRegisterHotkey(id, hotkey);
                UnregisterHotKey(this.Handle, id);
                if (!available)
                {
                    hintToolTip.Show("That key is already in use", box, 0, -22, 2000);
                    hotkey = previous;
                }
                else if (id == HOTKEY_KILL && !ConfirmKillBinding(hotkey))
                {
                    hotkey = previous;
                }
            }

            SetHotkey(id, hotkey);
            if (hotkeysActive)
                SetHotkeysActive(true);

            // Leave may already have fired (a dialog took focus), so refresh explicitly
            box.BackColor = SystemColors.Control;
            box.Text = HotkeyDisplay(hotkey);
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

            this.topMostCheckBox.Checked = appSetting.alwaysOnTop;
            this.TopMost = appSetting.alwaysOnTop;

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
            // last used display; overridden whenever a game window gains focus
            Display.Primary = appSetting.display;
            #endregion

            displayFollowTimer.Tick += DisplayFollowTimer_Tick;

            logsWatcher.SynchronizingObject = this;
            logsWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            logsWatcher.Changed += LogsWatcher_Changed;
            logsWatcher.Created += LogsWatcher_Changed;
            logsChangedTimer.Tick += LogsChangedTimer_Tick;
            liveRaidTimer.Tick += LiveRaidTimer_Tick;

            tabFontRegular = colorTabButton.Font;
            tabFontBold = new Font(colorTabButton.Font, FontStyle.Bold);
            colorTabButton.FlatAppearance.BorderColor = SystemColors.Highlight;
            serversTabButton.FlatAppearance.BorderColor = SystemColors.Highlight;
            colorTabButton.GotFocus += TabButton_GotFocus;
            colorTabButton.LostFocus += TabButton_LostFocus;
            serversTabButton.GotFocus += TabButton_GotFocus;
            serversTabButton.LostFocus += TabButton_LostFocus;
            SelectTab(servers: false);

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
            // a hand-edited settings file must not bypass the modifier rule for the kill key
            if (!TryParseHotkey(appSetting.killHotkey, out uint killModifiers, out _)
                || (killModifiers & (MOD_CONTROL | MOD_ALT | MOD_SHIFT)) == 0)
                appSetting.killHotkey = "";
            this.killHotkeyTextBox.Text = HotkeyDisplay(appSetting.killHotkey);
            decimal gammaLow = ClampToNum(gammaLowNum, appSetting.gammaLow);
            decimal gammaHigh = ClampToNum(gammaHighNum, appSetting.gammaHigh);
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
            set => BrightnessBar.Value = ClampToBar(BrightnessBar, (int)Math.Round(value * 100));
        }

        public double Contrast
        {
            get => ContrastBar.Value / 100.0;
            set => ContrastBar.Value = ClampToBar(ContrastBar, (int)Math.Round(value * 100));
        }

        public double Gamma
        {
            get => GammaBar.Value / 100.0;
            set => GammaBar.Value = ClampToBar(GammaBar, (int)Math.Round(value * 100));
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

            // live preview while the game is focused (no-op otherwise)
            pMonitor.Reapply();
        }
        // follow the game window to whichever monitor it is on
        public void FollowWindowDisplay(IntPtr hWnd)
        {
            Display.Primary = Screen.FromHandle(hWnd).DeviceName;
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
                    case HOTKEY_KILL:
                        KillFocusedGame();
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
            appSetting.display = Display.Primary;
            appSetting.minimizeOnStart = minimizeOnStart;
            appSetting.autostart = autostartCheckBox.Checked;
            if (arenaCheckBox.Checked)
                appSetting.pTargets.Add(ARENA_PROCESS);
            else
                appSetting.pTargets.Remove(ARENA_PROCESS);
            appSetting.Save();
        }

        // FormClosing also fires for WM_QUERYENDSESSION, which the user (or another app)
        // may still cancel - so only the idempotent save happens here
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        // reached only when the form really closes (Exit menu, confirmed shutdown)
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            logsWatcher.Dispose();
            logsChangedTimer.Stop();
            liveRaidTimer.Stop();
            displayFollowTimer.Stop();
            this.trayIcon.Dispose();
            Console.WriteLine("[mainForm] Closing pMonitor");
            pMonitor.Close();
            base.OnFormClosed(e);
        }

        private void CheckOnMinimizeToTray(object sender, EventArgs e)
        {
            this.minimizeOnStart = this.minimizeStartCheckBox.Checked;
        }

        private void CheckOnAutostart(object sender, EventArgs e)
        {
            Autostart.Enabled = this.autostartCheckBox.Checked;
        }

        private void TopMostCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            appSetting.alwaysOnTop = topMostCheckBox.Checked;
            this.TopMost = topMostCheckBox.Checked;
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

        // clamp before the decimal cast: NaN/Infinity/huge values from a hand-edited file would throw
        private static decimal ClampToNum(NumericUpDown num, double value)
        {
            if (double.IsNaN(value) || value < (double)num.Minimum)
                return num.Minimum;
            if (value > (double)num.Maximum)
                return num.Maximum;
            return (decimal)value;
        }

        // identity values - the display looks exactly as Windows renders it
        private void DefaultButton_Click(object sender, EventArgs e)
        {
            Brightness = 0.5;
            Contrast = 0.5;
            Gamma = 1.0;
            DVL = 0;
        }

        #region Servers Tab
        private bool serversLoaded;
        private bool refreshingServers;

        // live raid: ping every 5 s, re-read its session folder every 10 s so Wait,
        // map and the end of the raid show up without pressing Refresh
        private readonly Timer liveRaidTimer = new Timer { Interval = 5000 };
        private ServerLog.Entry liveRaid;
        private ListViewItem liveRaidItem;
        private GeoIp.Info liveRaidGeo;
        private int liveTicks;
        private bool liveBusy;

        private static readonly Color LiveColor = Color.FromArgb(0, 110, 0);

        private async void LiveRaidTimer_Tick(object sender, EventArgs e)
        {
            if (liveBusy)
                return;
            ServerLog.Entry raid = liveRaid;
            ListViewItem item = liveRaidItem;
            if (raid == null || item == null || item.ListView == null)
            {
                liveRaidTimer.Stop();
                return;
            }

            liveBusy = true;
            try
            {
                long ms = await PingAsync(raid.Ip);
                if (item.ListView != null)
                    item.SubItems[5].Text = ms >= 0 ? ms + " ms" : "-";

                liveTicks++;
                if (liveTicks % 2 != 0 || item.ListView == null)
                    return;

                string dir = raid.SessionDir;
                var session = await Task.Run(() => ServerLog.ReadSession(dir));
                ServerLog.Entry updated = session.FirstOrDefault(x =>
                    x.Ip == raid.Ip && x.Port == raid.Port && Math.Abs((x.Time - raid.Time).TotalSeconds) < 5);
                if (updated == null || item.ListView == null)
                    return;

                if (updated.Ended || DateTime.Now - updated.Time >= TimeSpan.FromHours(1))
                {
                    // raid is over: rebuild so the row gets its session rtt and loses the live style
                    await RefreshServers();
                    return;
                }

                liveRaid = updated;
                item.SubItems[1].Text = updated.Map;
                item.SubItems[2].Text = updated.Region;
                item.SubItems[4].Text = updated.TotalSec >= 0 ? updated.TotalSec.ToString("F0") + "s" : "-";
                item.Tag = updated;
                item.ToolTipText = BuildRaidDetail(updated, liveRaidGeo) + "\nLive raid, ping updates every 5 s";
                if (item.Selected)
                    ServerListView_SelectedIndexChanged(serverListView, EventArgs.Empty);
            }
            finally
            {
                liveBusy = false;
            }
        }

        // kernel change notifications, filtered to raid connection logs - idle cost is zero
        private readonly FileSystemWatcher logsWatcher = new FileSystemWatcher { IncludeSubdirectories = true };
        private readonly Timer logsChangedTimer = new Timer { Interval = 2000 };

        private void LogsWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            // the game writes output/application logs constantly; only connection
            // events (one per raid) matter here
            if (e.Name == null || e.Name.IndexOf("network-connection", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            if (!serversPanel.Visible)
            {
                // re-read when the tab is opened instead of working in the background
                serversLoaded = false;
                return;
            }
            logsChangedTimer.Stop();
            logsChangedTimer.Start();
        }

        private async void LogsChangedTimer_Tick(object sender, EventArgs e)
        {
            logsChangedTimer.Stop();
            await RefreshServers();
        }

        private void WatchLogsFolder(string path)
        {
            try
            {
                logsWatcher.EnableRaisingEvents = false;
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return;
                logsWatcher.Path = path;
                logsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception) { }
        }
        private Font tabFontRegular;
        private Font tabFontBold;

        private void SelectTab(bool servers)
        {
            ColorPanel.Visible = !servers;
            serversPanel.Visible = servers;
            colorTabButton.BackColor = servers ? Color.AliceBlue : Color.White;
            serversTabButton.BackColor = servers ? Color.White : Color.AliceBlue;
            colorTabButton.Font = servers ? tabFontRegular : tabFontBold;
            serversTabButton.Font = servers ? tabFontBold : tabFontRegular;
        }

        // flat buttons draw no focus rectangle, so show keyboard focus with a border
        private void TabButton_GotFocus(object sender, EventArgs e)
        {
            ((Button)sender).FlatAppearance.BorderSize = 1;
        }

        private void TabButton_LostFocus(object sender, EventArgs e)
        {
            ((Button)sender).FlatAppearance.BorderSize = 0;
        }

        private void ColorTab_Click(object sender, EventArgs e)
        {
            SelectTab(servers: false);
        }

        private async void ServersTab_Click(object sender, EventArgs e)
        {
            SelectTab(servers: true);
            if (!serversLoaded)
                await RefreshServers();
        }

        private async void RefreshServersButton_Click(object sender, EventArgs e)
        {
            await RefreshServers();
        }

        private async void BrowseLogsButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the EFT Logs folder" })
            {
                if (Directory.Exists(appSetting.logsPath))
                    dialog.SelectedPath = appSetting.logsPath;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                appSetting.logsPath = ServerLog.NormalizeLogsPath(dialog.SelectedPath);
            }
            await RefreshServers();
        }

        private async Task RefreshServers()
        {
            if (refreshingServers)
                return;
            serversLoaded = true;

            if (string.IsNullOrEmpty(appSetting.logsPath) || !Directory.Exists(appSetting.logsPath))
                appSetting.logsPath = ServerLog.DetectLogsPath();
            logsPathText.Text = appSetting.logsPath;
            hintToolTip.SetToolTip(logsPathText, string.IsNullOrEmpty(appSetting.logsPath) ? "EFT Logs folder" : appSetting.logsPath);

            if (string.IsNullOrEmpty(appSetting.logsPath))
            {
                // retried automatically the next time the tab is opened
                serversLoaded = false;
                serverStatusLabel.Text = "Logs folder not found. Pick it with the ... button, or Refresh to retry";
                return;
            }

            WatchLogsFolder(appSetting.logsPath);

            refreshingServers = true;
            refreshServersButton.Enabled = false;
            browseLogsButton.Enabled = false;
            try
            {
                serverDetailLabel.Text = "";
                serverStatusLabel.Text = "Reading logs";
                string logsPath = appSetting.logsPath;
                var entries = await Task.Run(() => ServerLog.Read(logsPath, 15, TimeSpan.FromHours(72)));

                // live = newest raid, started within the last hour, no session rtt yet,
                // no end marker, and the game process still running
                ServerLog.Entry newest = entries.FirstOrDefault();
                bool hasLive = newest != null && !newest.Ended && newest.SessionRtt < 0
                    && DateTime.Now - newest.Time < TimeSpan.FromHours(1)
                    && await Task.Run(() => pMonitor.AnyTargetRunning());

                bool geoFailed = false;
                var geo = new System.Collections.Generic.Dictionary<string, GeoIp.Info>();
                if (entries.Count > 0)
                {
                    serverStatusLabel.Text = "Looking up locations";
                    try
                    {
                        geo = await GeoIp.LookupAsync(entries.Select(entry => entry.Ip));
                    }
                    catch (Exception)
                    {
                        geoFailed = true;
                    }
                }

                liveRaidTimer.Stop();
                liveRaid = null;
                liveRaidItem = null;
                liveRaidGeo = null;
                liveTicks = 0;

                serverListView.BeginUpdate();
                serverListView.Items.Clear();
                foreach (ServerLog.Entry entry in entries)
                {
                    bool live = hasLive && entry == newest;
                    geo.TryGetValue(entry.Ip, out GeoIp.Info info);
                    // total entry time only; the queue/load breakdown lives in the detail line
                    string wait = entry.TotalSec >= 0 ? entry.TotalSec.ToString("F0") + "s" : "-";
                    string pingNote2 = live
                        ? "Live raid, ping updates every 5 s"
                        : entry.SessionRtt >= 0
                            ? "Ping is the session average measured by the game"
                            : "No session ping in the log";
                    var item = new ListViewItem(new[]
                    {
                        live ? "LIVE " + entry.Time.ToString("HH:mm") : entry.Time.ToString("MM-dd HH:mm"),
                        entry.Map,
                        entry.Region,
                        LocationDisplay(info),
                        wait,
                        live ? "..." : entry.SessionRtt >= 0 ? entry.SessionRtt.ToString("F0") + " ms" : "-",
                    })
                    {
                        Tag = entry,
                        ToolTipText = BuildRaidDetail(entry, info) + "\n" + pingNote2,
                    };
                    if (live)
                    {
                        item.Font = tabFontBold;
                        item.ForeColor = LiveColor;
                        liveRaid = entry;
                        liveRaidItem = item;
                        liveRaidGeo = info;
                    }
                    serverListView.Items.Add(item);
                }
                serverListView.EndUpdate();
                if (liveRaidItem != null)
                {
                    liveRaidTimer.Start();
                    LiveRaidTimer_Tick(liveRaidTimer, EventArgs.Empty);
                }
                if (serverListView.Items.Count > 0)
                    serverListView.Items[0].Selected = true;

                // trim to what actually fits so the list never scrolls (row height varies with DPI)
                if (serverListView.Items.Count > 0)
                {
                    var first = serverListView.GetItemRect(0);
                    int capacity = Math.Max(1, (serverListView.ClientSize.Height - first.Top) / Math.Max(1, first.Height));
                    while (serverListView.Items.Count > capacity)
                        serverListView.Items.RemoveAt(serverListView.Items.Count - 1);
                }

                int shown = serverListView.Items.Count;
                int unique = serverListView.Items.Cast<ListViewItem>().Select(item => ((ServerLog.Entry)item.Tag).Ip).Distinct().Count();
                serverStatusLabel.Text = (shown == 0
                    ? "No raids in the last 72 hours"
                    : "Raids (72h): " + shown + (unique > 1 ? "   Servers: " + unique : ""))
                    + (geoFailed ? "   (location lookup failed)" : "")
                    + (liveRaidItem != null ? "   Live raid" : "");
            }
            catch (Exception e)
            {
                serverStatusLabel.Text = "Failed to read logs: " + e.Message;
            }
            finally
            {
                refreshingServers = false;
                refreshServersButton.Enabled = true;
                browseLogsButton.Enabled = true;
            }
        }
        #endregion

        // "Hong Kong" when country and city coincide, "Japan/Tokyo" otherwise
        private static string LocationDisplay(GeoIp.Info info)
        {
            if (info == null || string.IsNullOrEmpty(info.country))
                return "";
            if (string.IsNullOrEmpty(info.city) || info.city == info.country)
                return info.country;
            return info.country + "/" + info.city;
        }

        // two explicit lines, so values never wrap in the middle
        private static string BuildRaidDetail(ServerLog.Entry entry, GeoIp.Info info)
        {
            var first = new System.Collections.Generic.List<string>();
            if (entry.Mode != "")
                first.Add(entry.Mode);
            if (entry.GameTime != null)
                first.Add((entry.GameTime.Value.Hour >= 6 && entry.GameTime.Value.Hour < 22 ? "Day " : "Night ")
                    + entry.GameTime.Value.ToString("HH:mm"));
            if (entry.ShortId != "")
                first.Add(entry.ShortId);
            first.Add(entry.Ip + ":" + entry.Port);

            var second = new System.Collections.Generic.List<string>();
            if (entry.QueueSec >= 0)
            {
                string timing = "queue " + entry.QueueSec.ToString("F0") + "s";
                if (entry.LoadSec >= 0)
                    timing += ", load " + entry.LoadSec.ToString("F0") + "s";
                if (entry.TotalSec >= 0)
                    timing += ", total " + entry.TotalSec.ToString("F0") + "s";
                second.Add(timing);
            }
            string location = LocationDisplay(info);
            if (location != "")
                second.Add(location);

            string detail = string.Join("  |  ", first);
            if (second.Count > 0)
                detail += "\n" + string.Join("  |  ", second);
            return detail;
        }

        private void ServerListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (serverListView.SelectedItems.Count == 0)
            {
                serverDetailLabel.Text = "";
                return;
            }
            // everything except the trailing ping note
            string[] lines = serverListView.SelectedItems[0].ToolTipText.Split('\n');
            serverDetailLabel.Text = string.Join("\n", lines.Take(lines.Length - 1));
        }

        // measured now, not the ping at raid time; -1 when ICMP is blocked or times out
        private static async Task<long> PingAsync(string ip)
        {
            try
            {
                using (var ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(ip, 1500);
                    if (reply.Status == IPStatus.Success)
                        return reply.RoundtripTime;
                }
            }
            catch (Exception) { }
            return -1;
        }

        private void RecommendButton_Click(object sender, EventArgs e)
        {
            var recommended = new AppSetting();
            Brightness = recommended.brightness;
            Contrast = recommended.contrast;
            Gamma = recommended.gamma;
            DVL = recommended.saturation;
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
