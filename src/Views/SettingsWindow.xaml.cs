using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using SSF2ModManager.Services;

namespace SSF2ModManager.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settings = new();
        private bool _ready;

        public SettingsWindow()
        {
            InitializeComponent();
            CmbLanguage.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
            CmbLanguage.Items.Add(new ComboBoxItem { Content = "Español", Tag = "es" });
            var lang = _settings.GetLanguage() ?? "en";
            foreach (ComboBoxItem item in CmbLanguage.Items)
            {
                if ((string?)item.Tag == lang)
                    CmbLanguage.SelectedItem = item;
            }
            if (CmbLanguage.SelectedIndex < 0)
                CmbLanguage.SelectedIndex = 0;

            foreach (var theme in LauncherWindow.Themes.GetAvailableThemes())
                CmbTheme.Items.Add(new ComboBoxItem { Content = theme, Tag = theme });
            var current = _settings.GetTheme();
            foreach (ComboBoxItem item in CmbTheme.Items)
            {
                if ((string?)item.Tag == current)
                    CmbTheme.SelectedItem = item;
            }
            TxtEventCreator.Text = _settings.GetEventCreator();
            ChkUpdateAuto.IsChecked = _settings.GetUpdateAutomatically();
            ChkClockAmPm.IsChecked = _settings.GetClockUseAmPm();
            ChkPauseMusic.IsChecked = _settings.GetPauseMusicOnLaunch();
            ChkSyncMusic.IsChecked = _settings.GetSyncMusicWithGame();
            ChkBlurPaths.IsChecked = _settings.GetBlurFilePaths();
            RefreshShellFolderText();
            CmbIde.Items.Add(new ComboBoxItem { Content = "Visual Studio Code", Tag = "vscode" });
            CmbIde.Items.Add(new ComboBoxItem { Content = "Cursor", Tag = "cursor" });
            var ide = _settings.GetDebugIde();
            if (string.IsNullOrWhiteSpace(ide))
                ide = ModdingToolchainService.FindVsCodeCli() != null ? "vscode" : "cursor";
            foreach (ComboBoxItem item in CmbIde.Items)
            {
                if (string.Equals(item.Tag as string, ide, StringComparison.OrdinalIgnoreCase))
                    CmbIde.SelectedItem = item;
            }
            if (CmbIde.SelectedIndex < 0)
                CmbIde.SelectedIndex = 0;
            var shell = ResolveShell();
            ChkCompressData.IsChecked = !string.IsNullOrWhiteSpace(shell) && DataFolderCompression.IsCompressed(shell);
            TxtCompress.Text = ChkCompressData.IsChecked == true
                ? "Data files are .ssf."
                : "Data files are .swf.";
            RefreshPathBoxes();
            LoadMenuBackgrounds();
            ChkManagerBgMatch.IsChecked = _settings.GetManagerBackgroundMatchesMenu();
            ApplyManagerBgMatchUi();
            _ready = true;
        }

        private void TxtEventCreator_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_ready)
                return;
            _settings.SetEventCreator(TxtEventCreator.Text);
        }

        private void RefreshShellFolderText() => RefreshPathBoxes();

        private void RefreshPathBoxes()
        {
            var path = _settings.GetLauncherInstallFolder();
            if (string.IsNullOrWhiteSpace(path))
                path = ResolveShell() ?? "";
            var hide = _settings.GetBlurFilePaths();
            TxtShellFolder.Text = hide
                ? (string.IsNullOrWhiteSpace(path) ? "" : "SSF2 Shell")
                : path;
            var n2f = _settings.GetNext2FlashFolder()
                      ?? AppPaths.Next2FlashRoot
                      ?? Next2FlashSetup.InstallDirectory;
            TxtNext2Flash.Text = hide
                ? (string.IsNullOrWhiteSpace(n2f) ? "" : "Next2Flash")
                : n2f ?? "";
        }

        private string? ResolveShell()
        {
            var root = _settings.GetLauncherInstallFolder();
            if (!string.IsNullOrWhiteSpace(root))
            {
                if (ShellWorkspaceService.IsShellFolder(root))
                    return root;
                var nested = Path.Combine(root, "SSF2 Shell");
                if (ShellWorkspaceService.IsShellFolder(nested))
                    return nested;
            }
            return ShellWorkspaceService.FindShellFolder();
        }

        private void LoadMenuBackgrounds()
        {
            var shell = ResolveShell();
            var stages = string.IsNullOrWhiteSpace(shell) ? new List<string>() : MenuStageIds(shell);
            FillStageCombo(CmbMenuBg, stages, shell == null);
            FillStageCombo(CmbManagerBg, stages, shell == null);
            if (string.IsNullOrWhiteSpace(shell))
                return;
            SelectStage(CmbMenuBg, MenuBackgroundStore.ReadStageId(shell));
            SelectStage(CmbManagerBg, _settings.GetManagerBackgroundStageId());
        }

        private static void FillStageCombo(System.Windows.Controls.ComboBox combo, List<string> stages, bool disabled)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });
            combo.IsEnabled = !disabled;
            foreach (var id in stages)
                combo.Items.Add(new ComboBoxItem { Content = id, Tag = id });
        }

        private static void SelectStage(System.Windows.Controls.ComboBox combo, string stageId)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (string.Equals(item.Tag as string, stageId, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private void ApplyManagerBgMatchUi()
        {
            var match = ChkManagerBgMatch.IsChecked == true;
            LblManagerBg.Visibility = match ? Visibility.Collapsed : Visibility.Visible;
            LblManagerBgHint.Visibility = match ? Visibility.Collapsed : Visibility.Visible;
            ManagerBgRow.Visibility = match ? Visibility.Collapsed : Visibility.Visible;
            RefreshManagerBgHint();
        }

        private static List<string> MenuStageIds(string shell)
        {
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ShellConfigureLaunchService.ListStages(shell))
                ids.Add(id);
            var exp = ShellWorkspaceService.StageExpansionsDirectory(shell);
            if (Directory.Exists(exp))
            {
                foreach (var dir in Directory.GetDirectories(exp))
                {
                    foreach (var swf in Directory.GetFiles(dir, "*.swf"))
                    {
                        var id = Path.GetFileNameWithoutExtension(swf);
                        if (!id.Equals("assets", StringComparison.OrdinalIgnoreCase))
                            ids.Add(id);
                    }
                }
            }
            return ids.ToList();
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            if (CmbLanguage.SelectedItem is not ComboBoxItem item || item.Tag is not string lang)
                return;
            if (lang == _settings.GetLanguage())
                return;
            _settings.SetLanguage(lang);
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            Process.Start(new ProcessStartInfo(exe, "--lang=" + lang) { UseShellExecute = true });
            App.RequestFullExit();
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            if (CmbTheme.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
                return;
            LauncherWindow.Themes.ApplyTheme(theme);
            _settings.SetTheme(theme);
        }

        private void ChkUpdateAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.SetUpdateAutomatically(ChkUpdateAuto.IsChecked == true);
        }

        private void ChkPauseMusic_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.SetPauseMusicOnLaunch(ChkPauseMusic.IsChecked == true);
        }

        private void ChkSyncMusic_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.SetSyncMusicWithGame(ChkSyncMusic.IsChecked == true);
        }

        private void ChkClockAmPm_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.SetClockUseAmPm(ChkClockAmPm.IsChecked == true);
        }

        private void ChkBlurPaths_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.SetBlurFilePaths(ChkBlurPaths.IsChecked == true);
            RefreshPathBoxes();
            RefreshShellFolderText();
        }

        private void BtnShellFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select the SSF2 Mod Manager install folder",
                UseDescriptionForTitle = true
            };
            var current = _settings.GetLauncherInstallFolder();
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                dlg.SelectedPath = current;
            if (dlg.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                return;
            _settings.SetLauncherInstallFolder(dlg.SelectedPath);
            RefreshShellFolderText();
            var wasReady = _ready;
            _ready = false;
            LoadMenuBackgrounds();
            _ready = wasReady;
        }

        private async void ChkCompressData_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var compress = ChkCompressData.IsChecked == true;
            var shell = ResolveShell();
            if (string.IsNullOrWhiteSpace(shell))
            {
                TxtCompress.Text = "SSF2 Shell was not found.";
                return;
            }
            ChkCompressData.IsEnabled = false;
            var launcher = System.Windows.Application.Current?.Windows.OfType<LauncherWindow>().FirstOrDefault();
            var busy = compress ? "Compressing data folder…" : "Uncompressing data folder…";
            TxtCompress.Text = busy;
            launcher?.LockForWork(true, busy);
            try
            {
                var count = await Task.Run(() => DataFolderCompression.Apply(shell, compress));
                _settings.SetCompressData(compress);
                TxtCompress.Text = compress
                    ? count + " file(s) are now .ssf."
                    : count + " file(s) are now .swf.";
            }
            catch (Exception ex)
            {
                TxtCompress.Text = ex.Message;
            }
            finally
            {
                launcher?.LockForWork(false, "");
                ChkCompressData.IsEnabled = true;
            }
        }

        private async void CmbIde_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            if (CmbIde.SelectedItem is not ComboBoxItem item || item.Tag is not string ide)
                return;
            _settings.SetDebugIde(ide);
            if (ide == "vscode" && ModdingToolchainService.FindVsCodeCli() == null)
            {
                var install = MessageBox.Show(
                    "Visual Studio Code is not installed. Install it now?",
                    "Debug editor", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (install != MessageBoxResult.Yes)
                    return;
                var (ok, msg) = await ModdingToolchainService.InstallVsCodeAsync();
                MessageBox.Show(msg, "Debug editor", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            else if (ide == "cursor" && ModdingToolchainService.FindCursorCli() == null)
            {
                MessageBox.Show("Cursor is not installed.", "Debug editor",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnNext2Flash_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Next2Flash folder",
                UseDescriptionForTitle = true
            };
            var current = _settings.GetNext2FlashFolder() ?? AppPaths.Next2FlashRoot ?? "";
            if (Directory.Exists(current))
                dialog.SelectedPath = current;
            if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;
            _settings.SetNext2FlashFolder(dialog.SelectedPath);
            RefreshPathBoxes();
        }

        private void BtnSetup_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is LauncherWindow launcher)
                launcher.OpenSetup();
            else
                new SetupWindow { Owner = this }.ShowDialog();
            TxtNext2Flash.Text = _settings.GetNext2FlashFolder()
                ?? AppPaths.Next2FlashRoot
                ?? Next2FlashSetup.InstallDirectory;
        }

        private void CmbMenuBg_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            if (CmbMenuBg.SelectedItem is not ComboBoxItem item || item.Tag is not string stageId)
                return;
            var shell = ResolveShell();
            if (string.IsNullOrWhiteSpace(shell))
                return;
            try
            {
                MenuBackgroundStore.WriteStageId(shell, stageId);
                SettingsService.NotifyChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Menu background", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ChkManagerBgMatch_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var match = ChkManagerBgMatch.IsChecked == true;
            if (!match && string.IsNullOrEmpty(_settings.GetManagerBackgroundStageId()))
            {
                var menuId = (CmbMenuBg.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                _ready = false;
                SelectStage(CmbManagerBg, menuId);
                _ready = true;
                _settings.SetManagerBackgroundStageId(menuId);
            }
            _settings.SetManagerBackgroundMatchesMenu(match);
            ApplyManagerBgMatchUi();
        }

        private void CmbManagerBg_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            if (CmbManagerBg.SelectedItem is not ComboBoxItem item || item.Tag is not string stageId)
                return;
            _settings.SetManagerBackgroundCustomPath("");
            _settings.SetManagerBackgroundStageId(stageId);
            RefreshManagerBgHint();
        }

        private void BtnManagerBgUpload_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.OpenFileDialog
            {
                Title = "Mod Manager background",
                Filter = "Pictures and video|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|All files|*.*"
            };
            if (dlg.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
                return;
            try
            {
                var dest = ManagerBackgroundMedia.Install(dlg.FileName, AppPaths.AppDataDir);
                _settings.SetManagerBackgroundCustomPath(dest);
                RefreshManagerBgHint();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mod Manager background", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshManagerBgHint()
        {
            var path = _settings.GetManagerBackgroundCustomPath();
            LblManagerBgHint.Text = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? "Custom: " + Path.GetFileName(path) + ". Pick a stage to use the dropdown again."
                : "None is the SSF2 logo. A stage shows that backdrop with the logo peeking up.";
        }
    }
}
