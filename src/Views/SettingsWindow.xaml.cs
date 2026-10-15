using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Newtonsoft.Json.Linq;
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
            ChkUpdateAuto.IsChecked = _settings.GetUpdateAutomatically();
            ChkBlurPaths.IsChecked = _settings.GetBlurFilePaths();
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
            TxtNext2Flash.Text = _settings.GetNext2FlashFolder()
                ?? AppPaths.Next2FlashRoot
                ?? Next2FlashSetup.InstallDirectory;
            LoadMenuBackgrounds();
            _ready = true;
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
            CmbMenuBg.Items.Clear();
            CmbMenuBg.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });
            var shell = ResolveShell();
            if (string.IsNullOrWhiteSpace(shell))
            {
                CmbMenuBg.IsEnabled = false;
                return;
            }
            foreach (var id in MenuStageIds(shell))
                CmbMenuBg.Items.Add(new ComboBoxItem { Content = id, Tag = id });
            var current = ReadMenuBackground(shell);
            foreach (ComboBoxItem item in CmbMenuBg.Items)
            {
                if (string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
                {
                    CmbMenuBg.SelectedItem = item;
                    return;
                }
            }
            CmbMenuBg.SelectedIndex = 0;
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

        private static string ReadMenuBackground(string shell)
        {
            var path = MenuBackgroundInfoPath(shell);
            if (!File.Exists(path))
                return "";
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var row = obj["settings"]?.FirstOrDefault(s =>
                    string.Equals(s?["id"]?.ToString(), "stageId", StringComparison.OrdinalIgnoreCase));
                return row?["value"]?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void WriteMenuBackground(string shell, string stageId)
        {
            var path = MenuBackgroundInfoPath(shell);
            var dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);
            JObject obj;
            if (File.Exists(path))
            {
                try { obj = JObject.Parse(File.ReadAllText(path)); }
                catch { obj = new JObject(); }
            }
            else obj = new JObject();
            var settings = obj["settings"] as JArray ?? new JArray();
            JObject? row = null;
            foreach (var token in settings)
            {
                if (token is JObject o && string.Equals(o["id"]?.ToString(), "stageId", StringComparison.OrdinalIgnoreCase))
                {
                    row = o;
                    break;
                }
            }
            if (row == null)
            {
                row = new JObject
                {
                    ["id"] = "stageId",
                    ["label"] = "Stage",
                    ["type"] = "dropdown",
                    ["optionsSource"] = "stages"
                };
                settings.Add(row);
            }
            row["value"] = stageId ?? "";
            obj["settings"] = settings;
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static string MenuBackgroundInfoPath(string shell) =>
            Path.Combine(shell, "data", "ssf2mm", "menu_background", "mod.json");

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

        private void ChkBlurPaths_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _settings.SetBlurFilePaths(ChkBlurPaths.IsChecked == true);
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
            var current = TxtNext2Flash.Text;
            if (Directory.Exists(current))
                dialog.SelectedPath = current;
            if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;
            _settings.SetNext2FlashFolder(dialog.SelectedPath);
            TxtNext2Flash.Text = dialog.SelectedPath;
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
                WriteMenuBackground(shell, stageId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Menu background", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
