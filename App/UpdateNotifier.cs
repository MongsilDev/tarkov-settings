using System;
using System.Net.Http;
using System.Windows.Forms;

namespace tarkov_settings
{
    public partial class UpdateNotifier : Form
    {
        private Version current, latest;
        private readonly Action exit;

        // Utilize repository's file as version notifier
        // I know it sounds very dangerous. but i am broke as hell.
        private string downloadUrl = @"https://github.com/MongsilDev/tarkov-settings/releases/latest";
        private string checkUrl = @"https://raw.githubusercontent.com/MongsilDev/tarkov-settings/main/version";
        public UpdateNotifier(Version current, Action exit)
        {
            InitializeComponent();
            this.current = current;
            this.exit = exit;
            this.CurrentVersionLabel.Text = current.ToString();
            CheckUpdate();
        }

        private async void CheckUpdate()
        {
            using (var client = new HttpClient())
            {
                try
                {
                    HttpResponseMessage response = await client.GetAsync(checkUrl);
                    response.EnsureSuccessStatusCode();

                    string version = await response.Content.ReadAsStringAsync();
                    this.LatestVersionLabel.Text = version;
                    latest = new Version(version);
                    if(latest > current)
                    {
                        // exit after the dialog is gone, not from inside its modal loop
                        if (this.ShowDialog() == DialogResult.OK)
                            exit();
                    }
                }
                catch (Exception)
                {
                    // offline or invalid version string - skip notification
                }
            }
        }

        private void UpdateCancelButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void UpdateButton_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(downloadUrl);
            this.DialogResult = DialogResult.OK;
        }
    }
}
