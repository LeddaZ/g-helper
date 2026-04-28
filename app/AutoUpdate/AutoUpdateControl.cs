using GHelper.Helpers;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GHelper.AutoUpdate
{
    public class AutoUpdateControl
    {
        private readonly SettingsForm settings;

        public string versionUrl = "https://github.com/LeddaZ/g-helper/releases";
        public bool update = false;

        static long lastUpdate;

        public AutoUpdateControl(SettingsForm settingsForm)
        {
            settings = settingsForm;
            Version appVersion = new((Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString());
            settings.SetVersionLabel(Properties.Strings.VersionLabel + $": {appVersion.Major}.{appVersion.Minor}.{appVersion.Build}");
        }

        public void CheckForUpdates()
        {
            // Run update once per 12 hours
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeSeconds() - lastUpdate) < 43200) return;
            lastUpdate = DateTimeOffset.Now.ToUnixTimeSeconds();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                CheckForUpdatesAsync();
            });
        }

        public void Update()
        {
            if (update)
            {
                Task.Run(() =>
                {
                    CheckForUpdatesAsync(true);
                });
            } else
            {
                LoadReleases();
            }
        }

        public void LoadReleases()
        {
            try
            {
                Process.Start(new ProcessStartInfo(versionUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to open releases page:" + ex.Message);
            }
        }

        private async void CheckForUpdatesAsync(bool force = false)
        {
            if (AppConfig.Is("skip_updates")) return;

            try
            {
                using HttpClient httpClient = new();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "G-Helper App");
                string json = await httpClient.GetStringAsync("https://api.github.com/repos/LeddaZ/g-helper/releases/latest");
                JsonElement config = JsonSerializer.Deserialize<JsonElement>(json);
                string tag = config.GetProperty("tag_name").ToString().Replace("v", "");
                JsonElement assets = config.GetProperty("assets");

                string url = string.Empty;

                for (int i = 0; i < assets.GetArrayLength(); i++)
                {
                    if (assets[i].GetProperty("browser_download_url").ToString().Contains(".exe"))
                        url = assets[i].GetProperty("browser_download_url").ToString();
                }

                url ??= assets[0].GetProperty("browser_download_url").ToString();

                Version gitVersion = new(tag);
                Version appVersion = new((Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString());

                if (gitVersion.CompareTo(appVersion) > 0)
                {
                    versionUrl = url;
                    update = true;
                    settings.SetVersionLabel(Properties.Strings.DownloadUpdate + $": {appVersion.Major}.{appVersion.Minor}.{appVersion.Build} → {tag}", true);

                    string[] args = Environment.GetCommandLineArgs();
                    if (force || args.Length > 1 && args[1] == "autoupdate")
                    {
                        AutoUpdate(url);
                        return;
                    }

                    if (AppConfig.GetString("skip_version") != tag)
                    {
                        DialogResult dialogResult = DialogResult.No;

                        settings.Invoke((System.Windows.Forms.MethodInvoker)delegate
                        {
                            dialogResult = MessageBox.Show(settings, Properties.Strings.DownloadUpdate + ": G-Helper " + tag + "?", "Update", MessageBoxButtons.YesNo);
                        });

                        if (dialogResult == DialogResult.Yes)
                            AutoUpdate(url);
                        else
                            AppConfig.Set("skip_version", tag);
                    }
                }
                else
                {
                    Logger.WriteLine($"Latest version {appVersion}");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to check for updates: {ex.Message}");
            }

        }

        public static string EscapeString(string input)
        {
            return Regex.Replace(Regex.Replace(input, @"\[|\]", "`$0"), @"\'", "''");
        }

        private async void AutoUpdate(string requestUri)
        {

            Uri uri = new(requestUri);
            string newExeName = Path.GetFileName(uri.LocalPath.Replace("GHelper.exe", "NewGHelper.exe"));

            string exeLocation = Application.ExecutablePath;
            string exeName = Path.GetFileName(exeLocation);
            string newExeLocation = Path.GetDirectoryName(exeLocation) + "\\" + newExeName;

            using HttpClient client = new();
            Logger.WriteLine(requestUri);
            Logger.WriteLine(Path.GetDirectoryName(exeLocation));
            Logger.WriteLine(newExeName);
            Logger.WriteLine(exeName);

            try
            {
                using HttpResponseMessage response = await client.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                using FileStream fileStream = new(newExeLocation, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
                if (!ProcessHelper.IsUserAdministrator())
                {
                    ProcessHelper.RunAsAdmin("autoupdate");
                    Application.Exit();
                }
                else
                {
                    LoadReleases();
                }
                return;
            }

            string command = $"$ErrorActionPreference = \"Stop\"; Set-Location -Path '{EscapeString(Path.GetDirectoryName(exeLocation))}'; Wait-Process -Name \"GHelper\"; Remove-Item \"{exeName}\" -Force; Rename-Item -Path \"{newExeName}\" -NewName \"{exeName}\"; \"{exeName}\"; ";
            Logger.WriteLine(command);

            try
            {
                Process cmd = new();
                cmd.StartInfo.WorkingDirectory = Path.GetDirectoryName(exeLocation);
                cmd.StartInfo.UseShellExecute = false;
                cmd.StartInfo.CreateNoWindow = true;
                cmd.StartInfo.FileName = "powershell";
                cmd.StartInfo.Arguments = command;
                if (ProcessHelper.IsUserAdministrator()) cmd.StartInfo.Verb = "runas";
                cmd.Start();
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }

            Application.Exit();
        }
    }
}
