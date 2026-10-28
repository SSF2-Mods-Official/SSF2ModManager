using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SSF2ModManager.Services;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class SetupWindow : Window
    {
        private readonly SettingsService _settings = new();
        private readonly bool _gate;
        private SetupService.Status? _status;
        private bool _busy;
        private int _step;
        private DispatcherTimer? _spin;

        /// <summary>True after a build finished, so the launcher can refresh Play.</summary>
        public bool ShellBuilt { get; private set; }

        /// <summary>True when the done step's Play button closed the window.</summary>
        public bool Completed { get; private set; }

        public SetupWindow(bool gate = false)
        {
            _gate = gate;
            InitializeComponent();
            ShowInTaskbar = _gate;
            Closing += (_, e) =>
            {
                if (_busy)
                    e.Cancel = true;
            };
            Refresh();
            ShowStep(FirstIncomplete());
        }

        private int FirstIncomplete()
        {
            var s = _status;
            if (s == null || s.Next2Flash == null) return 0;
            if (s.VanillaCheck == ShellBaseService.VanillaCheck.Missing) return 1;
            if (s.Shell == null) return 2;
            return 4;
        }

        private void ShowStep(int step)
        {
            _step = Math.Clamp(step, 0, 4);
            StepN2f.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
            StepVanilla.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
            StepShell.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
            StepDebug.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
            StepDone.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
            TxtStepCount.Text = _step == 4 ? "Done" : "Step " + (_step + 1) + " of 4";
            BtnBack.Visibility = _step == 0 || _step == 4 ? Visibility.Collapsed : Visibility.Visible;
            BtnNext.Content = _step == 3 ? "Skip" : "Next";
            UpdateButtons();
        }

        private void Refresh()
        {
            var s = _status = SetupService.Read(_settings);
            TxtN2f.Text = s.Next2Flash != null ? "Found: " + s.Next2Flash : "Not installed.";
            BtnN2fDownload.Content = s.Next2Flash != null ? "Update" : "Download";

            TxtVanilla.Text = s.VanillaCheck switch
            {
                ShellBaseService.VanillaCheck.Ok => "Found 1.4.0.1 (" + (s.VanillaLabel ?? "known build") + "): " + s.Vanilla
                    + (s.VanillaLabel != null && s.VanillaLabel.StartsWith("32-bit")
                        ? " This folder's AIR is 32-bit. The shell needs 64-bit AIR 32."
                        : ""),
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

        private bool StepReady()
        {
            var s = _status;
            return _step switch
            {
                0 => s?.Next2Flash != null,
                1 => s != null && s.VanillaCheck != ShellBaseService.VanillaCheck.Missing,
                2 => s?.Shell != null,
                3 => true,
                _ => false
            };
        }

        private void UpdateButtons()
        {
            var s = _status;
            BtnN2fDownload.IsEnabled = !_busy;
            BtnN2fFolder.IsEnabled = !_busy;
            BtnVanilla.IsEnabled = !_busy;
            BtnBuild.IsEnabled = !_busy && s != null && !s.DevShell && s.Next2Flash != null
                && s.VanillaCheck != ShellBaseService.VanillaCheck.Missing;
            BtnDebug.IsEnabled = !_busy && s?.Next2Flash != null;
            BtnNext.Visibility = SetupService.ShowNext(_step, StepReady())
                ? Visibility.Visible
                : Visibility.Collapsed;
            BtnNext.IsEnabled = !_busy && StepReady();
            BtnBack.IsEnabled = !_busy;
            BtnPlay.IsEnabled = !_busy;
        }

        private async Task RunAsync(string title, Func<IProgress<string>, Task<string>> work)
        {
            _busy = true;
            BusyOverlay.Visibility = Visibility.Visible;
            _spin ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _spin.Tick -= OnSpin;
            _spin.Tick += OnSpin;
            _spin.Start();
            UpdateButtons();
            var progress = new Progress<string>(ShowProgress);
            try
            {
                TxtProgress.Text = await work(progress);
                TxtBusy.Text = TxtProgress.Text;
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
                _spin?.Stop();
                BusySpin.Angle = 0;
                BusyOverlay.Visibility = Visibility.Collapsed;
                Refresh();
            }
        }

        private void OnSpin(object? sender, EventArgs e) => BusySpin.Angle = (BusySpin.Angle + 8) % 360;

        private void ShowProgress(string message)
        {
            var text = message ?? "";
            var cut = text.IndexOf('|');
            if (cut > 0 && int.TryParse(text[..cut], out var pct))
            {
                SetupBar.Value = Math.Clamp(pct, 0, 100);
                TxtBusyPct.Text = pct + "%";
                text = text[(cut + 1)..];
            }
            TxtProgress.Text = text;
            TxtBusy.Text = text;
        }

        private async void BtnN2fDownload_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("Next2Flash", async p =>
            {
                p.Report(SetupService.Say(5, "Downloading the tools. This is the big download."));
                await Next2FlashSetup.EnsureAsync(update: _status?.Next2Flash != null, progress: p);
                return PortableToolchain.HasPython() && PortableToolchain.HasJre()
                    ? "Next2Flash is ready (portable Python + JRE)."
                    : "Next2Flash is ready.";
            });
            if (_status?.Next2Flash != null)
                ShowStep(1);
        }

        private async void BtnN2fFolder_Click(object sender, RoutedEventArgs e)
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
            await RunAsync("Next2Flash", async p =>
            {
                p.Report("Checking portable Python and JRE…");
                await PortableToolchain.EnsureAsync();
                return "Portable Python and JRE are ready.";
            });
            if (_status?.Next2Flash != null)
                ShowStep(1);
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
            if (ShellBaseService.CheckVanilla(dlg.SelectedPath) == ShellBaseService.VanillaCheck.Missing)
            {
                MessageBox.Show("That folder has no SSF2.swf.", "SSF2 Beta 1.4.0.1",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.SetVanillaSsf2Folder(dlg.SelectedPath);
            Refresh();
            if (_status != null && _status.VanillaCheck != ShellBaseService.VanillaCheck.Missing)
                ShowStep(2);
        }

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            var s = _status;
            if (s?.Vanilla == null)
                return;
            await RunAsync("SSF2 Shell", async p =>
            {
                p.Report(SetupService.Say(5, "Looking up the latest SSF2 Shell."));
                var latest = await ShellBaseService.GetLatestAsync()
                    ?? throw new InvalidOperationException("No SSF2 Shell Base release with .ssfpatch files was found.");
                var target = s.Shell ?? SetupService.DefaultShellTarget();
                await VanillaHashPrompt.BuildAsync(latest, s.Vanilla, target, p, _settings, AskVanilla);
                p.Report(SetupService.Say(82, "Saving costume colors so they open faster. The game may open for a moment."));
                await PaletteCacheWarmService.WarmAsync(target, null, p, default);
                p.Report(SetupService.Say(90, "Getting songs ready so the music player and visualizer work."));
                await new MusicLibraryCacheService().WarmInstallAsync(target, p, default);
                ShellBuilt = true;
                return "SSF2 Shell " + latest.Version + " is ready.";
            });
            if (ShellBuilt)
                ShowStep(3);
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
            ShowStep(4);
        }

        private bool AskVanilla(string question) =>
            MessageBox.Show(question, "SSF2 Beta 1.4.0.1", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

        private void BtnBack_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (!StepReady())
                return;
            ShowStep(_step + 1);
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            Completed = true;
            DialogResult = true;
        }
    }
}
