using System;
using System.Net.Http;
using System.Windows.Forms;

namespace tarkov_settings
{
    public partial class UpdateNotifier : Form
    {
        private Version current, latest;

        // Utilize repository's file as version notifier
        // I know it sounds very dangerous. but i am broke as hell.
        private string downloadUrl = @"https://github.com/MongsilDev/tarkov-settings/releases/latest";
        private string checkUrl = @"https://raw.githubusercontent.com/MongsilDev/tarkov-settings/main/version";
        public UpdateNotifier(Version current)
        {
            InitializeComponent();
            this.current = current;
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
                        this.ShowDialog();
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
            Application.Exit();
        }
    }
}
