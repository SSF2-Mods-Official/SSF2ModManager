using System;
using System.Threading.Tasks;
using System.Windows;
using SSF2ModManager.Services;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class SetupWindow : Window
    {
        private readonly SettingsService _settings = new();
        private SetupService.Status? _status;
        private bool _busy;

        /// <summary>True after a build finished, so the launcher can refresh Play.</summary>
        public bool ShellBuilt { get; private set; }

        public SetupWindow()
        {
            InitializeComponent();
            Closing += (_, e) =>
            {
                if (_busy)
                    e.Cancel = true;
            };
            Refresh();
        }

        private void Refresh()
        {
            var s = _status = SetupService.Read(_settings);
            TxtN2f.Text = s.Next2Flash != null ? "Found: " + s.Next2Flash : "Not installed.";
            BtnN2fDownload.Content = s.Next2Flash != null ? "Update" : "Download";

            TxtVanilla.Text = s.VanillaCheck switch
            {
                ShellBaseService.VanillaCheck.Ok => "Found 1.4.0.1: " + s.Vanilla,
                ShellBaseService.VanillaCheck.WrongVersion => "This folder is not 1.4.0.1: " + s.Vanilla,
                _ => "Not found. Choose the folder that has SSF2.swf."
            };

            if (s.DevShell)
                TxtShell.Text = "Development copy at " + s.Shell + ". It is maintained by hand and is not rebuilt here.";
            else if (s.Shell != null)
                TxtShell.Text = "Installed " + (s.ShellVersion ?? "(unknown version)") + ": " + s.Shell;
            else
                TxtShell.Text = "Not built yet. It goes to " + SetupService.DefaultShellTarget();
            BtnBuild.Content = s.Shell != null ? "Rebuild" : "Build";

            TxtDebug.Text = s.Editor != null ? "Editor: " + s.Editor : "No VS Code or Cursor found.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var s = _status;
            BtnN2fDownload.IsEnabled = !_busy;
            BtnN2fFolder.IsEnabled = !_busy;
            BtnVanilla.IsEnabled = !_busy;
            BtnBuild.IsEnabled = !_busy && s != null && !s.DevShell && s.Next2Flash != null
                && s.VanillaCheck == ShellBaseService.VanillaCheck.Ok;
            BtnDebug.IsEnabled = !_busy && s?.Next2Flash != null;
            BtnClose.IsEnabled = !_busy;
        }

        private async Task RunAsync(string title, Func<IProgress<string>, Task<string>> work)
        {
            _busy = true;
            Bar.Visibility = Visibility.Visible;
            UpdateButtons();
            var progress = new Progress<string>(m => TxtProgress.Text = m);
            try
            {
                TxtProgress.Text = await work(progress);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[setup] {title} failed: {ex.Message}");
                TxtProgress.Text = title + " failed.";
                MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
                Bar.Visibility = Visibility.Collapsed;
                Refresh();
            }
        }

        private async void BtnN2fDownload_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("Next2Flash", async p =>
            {
                p.Report("Downloading Next2Flash…");
                await Next2FlashSetup.EnsureAsync(update: _status?.Next2Flash != null);
                return "Next2Flash is ready.";
            });
        }

        private void BtnN2fFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Pick the Next2Flash folder.",
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() != Forms.DialogResult.OK)
                return;
            if (!AppPaths.IsNext2FlashRoot(dlg.SelectedPath))
            {
                MessageBox.Show("That folder is not a Next2Flash install.", "Next2Flash",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.SetNext2FlashFolder(dlg.SelectedPath);
            Refresh();
        }

        private void BtnVanilla_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Pick the SSF2 Beta 1.4.0.1 folder (the one with SSF2.swf).",
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() != Forms.DialogResult.OK)
                return;
            switch (ShellBaseService.CheckVanilla(dlg.SelectedPath))
            {
                case ShellBaseService.VanillaCheck.Missing:
                    MessageBox.Show("That folder has no SSF2.swf.", "SSF2 Beta 1.4.0.1",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                case ShellBaseService.VanillaCheck.WrongVersion:
                    MessageBox.Show("That SSF2.swf is not version 1.4.0.1. Install SSF2 Beta 1.4.0.1 and pick that folder.",
                        "SSF2 Beta 1.4.0.1", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
            }
            _settings.SetVanillaSsf2Folder(dlg.SelectedPath);
            Refresh();
        }

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            var s = _status;
            if (s?.Vanilla == null)
                return;
            await RunAsync("SSF2 Shell", async p =>
            {
                p.Report("Checking the latest SSF2 Shell Base release…");
                var latest = await ShellBaseService.GetLatestAsync()
                    ?? throw new InvalidOperationException("No SSF2 Shell Base release with .ssfpatch files was found.");
                var target = s.Shell ?? SetupService.DefaultShellTarget();
                await ShellBaseService.BuildAsync(latest, s.Vanilla, target, p);
                ShellBuilt = true;
                return "SSF2 Shell " + latest.Version + " is ready.";
            });
        }

        private async void BtnDebug_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("Debugging", async p =>
            {
                var (ok, msg) = await ModdingToolchainService.SetupModdingToolsAsync(p);
                if (!ok)
                    throw new InvalidOperationException(msg);
                return "Debugging is set up.";
            });
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
