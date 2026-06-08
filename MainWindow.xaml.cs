using SSF2ModManager.Dialogs;
using SSF2ModManager.Models;
using SSF2ModManager.Services;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using System.Net.Http;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SSF2ModManager.Views;

namespace SSF2ModManager
{
    public class BrowseModViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public GameBananaMod Mod { get; set; } = null!;
        public string Name => Mod.Name;
        public string Description => StripHtml(Mod.Description ?? "");
        public string FullDescription => StripHtml(Mod.Description ?? "", false);
        public string AuthorName => Mod.Submitter?.Name ?? "Unknown";
        public string ThumbnailUrl => Mod.PreviewMedia?.Images?.FirstOrDefault()?.ThumbnailUrl ?? "";
        public string CategoryName => Mod.Category?.Name ?? Mod.RootCategory?.Name ?? "";
        public string StatsText => $"👍 {Mod.Likes}  👁 {Mod.Views}  ⬇ {Mod.Downloads}";
        public InstalledMod? InstalledVersion { get; set; }
        public bool IsInstalled => InstalledVersion != null;
        public bool IsNotInstalled => InstalledVersion == null;
        public string ToggleText => InstalledVersion?.Enabled == true ? "⏸ Disable" : "▶ Enable";

        public string ToggleTextLocalized => InstalledVersion?.Enabled == true ? ("⏸ " + Localization.Get("Disable")) : ("▶ " + Localization.Get("Enable"));

        // Lazily fetched description text (from _sText API call)
        public string? CachedDescriptionText { get; set; }
        public string? CachedVideoUrl { get; set; }
        public bool DescriptionFetched { get; set; }

        private bool _hasVideo;
        public bool HasVideo
        {
            get => _hasVideo;
            set { if (_hasVideo != value) { _hasVideo = value; OnPropertyChanged(nameof(HasVideo)); } }
        }

        private static string StripHtml(string html, bool truncate = true)
        {
            if (string.IsNullOrEmpty(html)) return html;
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (truncate && text.Length > 200) return text[..200] + "...";
            return text;
        }

        public static string? ExtractYouTubeUrl(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            // Match YouTube URLs in various forms: iframe src, raw links, youtu.be
            var patterns = new[]
            {
                @"(?:https?://)?(?:www\.)?youtube\.com/watch\?v=([\w-]{11})",
                @"(?:https?://)?(?:www\.)?youtube\.com/embed/([\w-]{11})",
                @"(?:https?://)?youtu\.be/([\w-]{11})"
            };
            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(html, pattern);
                if (match.Success)
                    return $"https://www.youtube.com/watch?v={match.Groups[1].Value}";
            }
            return null;
        }
    }

    public class BuildViewModel
    {
        public SSF2VersionEntry Entry { get; set; } = null!;
        public string VersionName => Entry.VersionName;
        public string DisplayName => Entry.DisplayName;
        public string FolderPath => Entry.FolderPath;
        public int ModCount { get; set; }
        public string ModCountText => $"{ModCount} mod{(ModCount != 1 ? "s" : "")} installed";
        public List<string> CategoryBreakdown { get; set; } = new();
        public string? ExePath { get; set; }
        public string ExeStatusText => ExePath != null ? $"✅ {Path.GetFileName(ExePath)}" : "⚠ No executable found";
        public Brush ExeStatusColor => ExePath != null 
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50")) 
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFAB40"));
    }

        public partial class MainWindow : Window
        {
            private bool _languageComboInitialized = false;
        private readonly GameBananaApiClient _apiClient;
        private readonly ModManagerService _modManager;
        private ThemeManager? _themeManager;
        private SettingsService? _settingsService;
        private int _currentPage = 1;
        private string _activePageName = "browse";
        private string _currentSearch = "";
        private string _currentSort = "default";
        private string _searchMode = "text"; // "text", "author", "category"

        public MainWindow()
        {
            DevFileLog.Write( $"[MainWindow] ctor: Start\n");
            try
            {
                InitializeComponent();
                DevFileLog.Write( $"[MainWindow] InitializeComponent succeeded\n");
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[MainWindow] InitializeComponent EX: {ex}\n"); } catch { }
                throw;
            }
            DevFileLog.Write( $"[MainWindow] ctor: InitializeComponent done\n");

            if (FindName("TxtAppVersion") is TextBlock txtAppVersion)
                txtAppVersion.Text = AppInfo.DisplayVersion;

            // Set language ComboBox to current language
            if (FindName("CmbLanguage") is System.Windows.Controls.ComboBox cmb)
            {
                foreach (ComboBoxItem item in cmb.Items)
                {
                    if ((string?)item.Tag == Localization.CurrentLanguage)
                    {
                        cmb.SelectedItem = item;
                        break;
                    }
                }
                _languageComboInitialized = true;
            }

            // Set window title and sidebar/page UI text from localization
            this.Title = Localization.Get("AppTitle");
            BtnBrowse.Content = "🌐  " + Localization.Get("BrowseMods");
            BtnInstalled.Content = "📦  " + Localization.Get("InstalledMods");
            BtnBuilds.Content = "🎮  " + Localization.Get("InstalledBuilds");
            BtnCostumes.Content = "👗  " + Localization.Get("ManageCostumes");
            BtnEvents.Content = "🎉  " + Localization.Get("ManageEvents");
            BtnGettingStarted.Content = "📘  " + "Getting Started";
            BtnSettings.Content = "⚙️  " + Localization.Get("Settings");
            BtnLog.Content = "📋  " + Localization.Get("DebugLog");

            // Page headers
            SetTextBlock("PageBrowse", 0, Localization.Get("BrowseMods"));
            SetTextBlock("PageInstalled", 0, Localization.Get("InstalledMods"));
            SetTextBlock("PageBuilds", 0, Localization.Get("InstalledBuilds"));
            SetTextBlock("PageCostumes", 0, Localization.Get("ManageCostumes"));
            SetTextBlock("PageEvents", 0, Localization.Get("ManageEvents"));
            SetTextBlock("PageModSSF2", 0, "Getting Started");
            SetTextBlock("PageSettings", 0, Localization.Get("Settings"));
            SetTextBlock("PageLog", 0, Localization.Get("DebugLog"));

            // Search placeholder
            TxtSearch.ToolTip = Localization.Get("SearchPlaceholder");

            // No results/empty states
            TxtNoResults.Text = Localization.Get("NoModsFound");
            TxtNoMods.Text = Localization.Get("NoModsInstalled");
            TxtNoBuilds.Text = Localization.Get("NoBuildsConfigured");
            TxtNoVersions.Text = Localization.Get("NoVersionsConfigured");

            // Add Version, Open Folders
            SetButtonContent("BtnAddVersion", "➕ " + Localization.Get("AddVersion"));
            SetButtonContent("BtnOpenModsFolder", "📂 " + Localization.Get("OpenModsFolder"));
            SetButtonContent("BtnOpenSSF2Folder", "📂 " + Localization.Get("OpenSSF2Folder"));

            DevFileLog.Write( $"[MainWindow] ctor: UI text set\n");
            _apiClient = new GameBananaApiClient();
            _modManager = new ModManagerService(_apiClient);

            // Theme manager: load themes from Themes/ and populate settings UI
            try
            {
                _settingsService = new SettingsService();
                _themeManager = new ThemeManager();
                _themeManager.ThemeApplied += () =>
                {
                    try
                    {
                        Dispatcher.Invoke(() => { SetActivePage(_activePageName); });
                    }
                    catch { }
                };
                if (FindName("CmbTheme") is System.Windows.Controls.ComboBox themeCombo)
                {
                    themeCombo.Items.Clear();
                    foreach (var t in _themeManager.GetAvailableThemes())
                    {
                        // Display nicer name by stripping leading "theme-" if present
                    // Populate Mod SSF2 page version dropdown
                    try
                    {
                        if (FindName("CmbInfoSSF2Version") is System.Windows.Controls.ComboBox ver)
                        {
                            ver.Items.Clear();
                            foreach (var v in ModManagerService.KnownVersions)
                                ver.Items.Add(new ComboBoxItem() { Content = v });
                            ver.SelectedIndex = 0;
                        }
                        if (FindName("CmbInfoModType") is System.Windows.Controls.ComboBox mt && mt.Items.Count > 0)
                            mt.SelectedIndex = 0;
                    }
                    catch { }

                        var display = t.StartsWith("theme-", StringComparison.OrdinalIgnoreCase) ? t[6..] : t;
                        var item = new ComboBoxItem() { Content = display, Tag = t };
                        themeCombo.Items.Add(item);
                    }

                    // If saved theme exists, select it
                    var saved = _settingsService.GetTheme();
                    if (!string.IsNullOrEmpty(saved))
                    {
                        for (int i = 0; i < themeCombo.Items.Count; i++)
                        {
                            if (themeCombo.Items[i] is ComboBoxItem ci && ci.Tag is string tag && tag.Equals(saved, StringComparison.OrdinalIgnoreCase))
                            {
                                themeCombo.SelectedIndex = i;
                                _themeManager.ApplyTheme(tag);
                                try { DumpThemeDebug(tag); } catch { }
                                break;
                            }
                        }
                    }
                    else if (themeCombo.Items.Count > 0)
                    {
                        themeCombo.SelectedIndex = 0;
                    }
                }

                _themeManager.ThemesChanged += () =>
                {
                    if (FindName("CmbTheme") is System.Windows.Controls.ComboBox cmb)
                    {
                        var sel = (cmb.SelectedItem as ComboBoxItem)?.Tag as string;
                        cmb.Items.Clear();
                        foreach (var t in _themeManager.GetAvailableThemes())
                        {
                            var display = t.StartsWith("theme-", StringComparison.OrdinalIgnoreCase) ? t[6..] : t;
                            var item = new ComboBoxItem() { Content = display, Tag = t };
                            cmb.Items.Add(item);
                        }
                        if (!string.IsNullOrEmpty(sel))
                        {
                            for (int i = 0; i < cmb.Items.Count; i++)
                                if (cmb.Items[i] is ComboBoxItem ci && ci.Tag is string tag && tag.Equals(sel, StringComparison.OrdinalIgnoreCase)) { cmb.SelectedIndex = i; break; }
                        }
                    }
                };
            }
            catch { }

            RefreshVersionsList();
            RefreshPlayButton();
            DebugLogger.Log("Application started");

            Loaded += async (s, e) =>
            {
                try
                {
                    DevFileLog.Write( $"[MainWindow] Loaded handler start\n");
                    await LoadBrowseModsAsync();
                    DevFileLog.Write( $"[MainWindow] Loaded handler complete\n");
                }
                catch (Exception ex)
                {
                    try { DevFileLog.Write( $"[MainWindow] Loaded EX: {ex}\n"); } catch { }
                }
            };
            // Check program version on startup (non-blocking)
            _ = CheckProgramVersionAsync();
            DevFileLog.Write( $"[MainWindow] ctor: End\n");
        }

        private void BtnCheckModUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is InstalledMod inst)
                {
                    if (inst.IgnoreUpdates)
                    {
                        MessageBox.Show("Update checks are ignored for this mod.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (inst.GameBananaId <= 0)
                    {
                        MessageBox.Show("No GameBanana ID available for this mod.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var files = await _apiClient.GetModFilesAsync(inst.GameBananaId);
                            var latest = files?.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                            if (latest == null)
                            {
                                Dispatcher.Invoke(() => MessageBox.Show("No files found for this mod on GameBanana.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information));
                                return;
                            }

                            DateTime latestDate;
                            try { latestDate = DateTimeOffset.FromUnixTimeSeconds(latest.DateAdded).LocalDateTime; } catch { latestDate = DateTime.Now; }

                            if (latestDate > inst.InstalledDate)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    var res = MessageBox.Show($"Update available: {latest.FileName} (added {latestDate:g}).\nDo you want to download and install it now?", "Update Available", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                                    if (res == MessageBoxResult.Yes)
                                    {
                                        _ = System.Threading.Tasks.Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await _modManager.UpdateInstalledModAsync(inst);
                                                Dispatcher.Invoke(() => RefreshInstalledMods());
                                            }
                                            catch (Exception ex)
                                            {
                                                try { DevFileLog.Write( $"[UpdateInstalled] EX: {ex}\n"); } catch { }
                                                Dispatcher.Invoke(() => MessageBox.Show($"Failed to update mod: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                                            }
                                        });
                                    }
                                    else if (res == MessageBoxResult.No)
                                    {
                                        var ignore = MessageBox.Show("Ignore future update checks for this mod?", "Ignore Updates", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                        if (ignore == MessageBoxResult.Yes)
                                        {
                                            inst.IgnoreUpdates = true;
                                            try { _modManager.SaveDatabase(); } catch { }
                                            MessageBox.Show("This mod will be ignored for future update checks.", "Ignore Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                                            Dispatcher.Invoke(() => RefreshInstalledMods());
                                        }
                                    }
                                });
                            }
                            else
                            {
                                Dispatcher.Invoke(() => MessageBox.Show("No updates found. This mod is up-to-date.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information));
                            }
                        }
                        catch (Exception ex)
                        {
                            try { DevFileLog.Write( $"[CheckModUpdates] EX: {ex}\n"); } catch { }
                            Dispatcher.Invoke(() => MessageBox.Show($"Failed to check updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[BtnCheckModUpdates_Click] EX: {ex}\n"); } catch { }
            }
        }

        private async void BtnCheckProgramUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await CheckProgramVersionAsync();
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[BtnCheckProgramUpdate_Click] EX: {ex}\n"); } catch { }
                MessageBox.Show($"Failed to check program version: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCheckAllInstalledUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowOverlay("Checking for updates...");
                var installed = _modManager.InstalledMods.Where(m => m.GameBananaId > 0 && !m.IgnoreUpdates).ToList();
                if (installed.Count == 0)
                {
                    HideOverlay();
                    MessageBox.Show("No installed mods with GameBanana IDs found to check.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Check for available updates
                var updates = new List<(InstalledMod inst, string fileName, DateTime added)>();
                foreach (var inst in installed)
                {
                    try
                    {
                        var files = await _apiClient.GetModFilesAsync(inst.GameBananaId);
                        var latest = files?.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                        if (latest != null)
                        {
                            DateTime latestDate;
                            try { latestDate = DateTimeOffset.FromUnixTimeSeconds(latest.DateAdded).LocalDateTime; } catch { latestDate = DateTime.Now; }
                            if (latestDate > inst.InstalledDate)
                                updates.Add((inst, latest.FileName, latestDate));
                        }
                    }
                    catch { }
                }

                if (updates.Count == 0)
                {
                    HideOverlay();
                    MessageBox.Show("No updates found. All installed mods are up-to-date.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var summary = string.Join("\n", updates.Select(u => $"{u.inst.Name}: {u.fileName} (added {u.added:g})"));
                var dr = MessageBox.Show($"Updates found:\n{summary}\n\nInstall all updates now?", "Updates Available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (dr != MessageBoxResult.Yes)
                {
                    HideOverlay();
                    return;
                }

                // Install updates sequentially (overlay remains visible)
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        foreach (var u in updates)
                        {
                            try
                            {
                                await _modManager.UpdateInstalledModAsync(u.inst);
                                Dispatcher.Invoke(() => RefreshInstalledMods());
                            }
                            catch (Exception ex)
                            {
                                try { DevFileLog.Write( $"[BulkUpdate] EX for {u.inst.Name}: {ex}\n"); } catch { }
                            }
                        }
                        Dispatcher.Invoke(() => MessageBox.Show("Bulk update process completed.", "Updates", MessageBoxButton.OK, MessageBoxImage.Information));
                    }
                    finally
                    {
                        Dispatcher.Invoke(() => HideOverlay());
                    }
                });
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[BtnCheckAllInstalledUpdates_Click] EX: {ex}\n"); } catch { }
                MessageBox.Show($"Failed to check/install updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowOverlay(string message)
        {
            try
            {
                if (FindName("TxtOverlayMessage") is TextBlock tb) tb.Text = message;
                if (FindName("OverlayLoading") is Grid g) { g.Visibility = Visibility.Visible; g.IsHitTestVisible = true; }
                try
                {
                    if (FindName("OverlaySpinner") is System.Windows.Shapes.Ellipse el && el.RenderTransform is RotateTransform rt)
                    {
                        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
                        rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, anim);
                    }
                }
                catch { }
            }
            catch { }
        }

        private void HideOverlay()
        {
            try
            {
                if (FindName("OverlayLoading") is Grid g) { g.Visibility = Visibility.Collapsed; g.IsHitTestVisible = false; }
                try
                {
                    if (FindName("OverlaySpinner") is System.Windows.Shapes.Ellipse el && el.RenderTransform is RotateTransform rt)
                        rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                }
                catch { }
            }
            catch { }
        }

        private async System.Threading.Tasks.Task CheckProgramVersionAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var remote = await http.GetStringAsync(AppInfo.VersionCheckUrl);
                if (string.IsNullOrWhiteSpace(remote))
                {
                    MessageBox.Show("Could not read remote version information.", "Version Check", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var remoteVer = remote.Trim().Split('\n')[0].Trim();
                var localVer = AppInfo.Version;

                if (AppInfo.IsUpToDate(localVer, remoteVer))
                {
                    MessageBox.Show($"You are running the latest version ({AppInfo.DisplayVersion}).", "Version Check", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var dr = MessageBox.Show(
                        $"A newer version is available:\nRemote: {remoteVer}\nLocal: {AppInfo.DisplayVersion}\n\nOpen GitHub Releases to download?",
                        "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (dr == MessageBoxResult.Yes)
                        Process.Start(new ProcessStartInfo(AppInfo.GitHubRepo + "/releases") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                DevFileLog.Write($"[CheckProgramVersionAsync] EX: {ex}\n");
                MessageBox.Show($"Failed to check remote version: {ex.Message}", "Version Check", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (!_languageComboInitialized)
                    return;
                if (sender is System.Windows.Controls.ComboBox cmb && cmb.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
                {
                    DevFileLog.Write( $"[MainWindow] Language change requested: {langCode}\n");

                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exe))
                    {
                        DevFileLog.Write( "[MainWindow] Relaunch failed: exe path empty\n");
                        return;
                    }

                    var args = "--lang=" + langCode;
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = System.TimeSpan.FromMilliseconds(800)
                    };
                    timer.Tick += (s, ev) =>
                    {
                        try
                        {
                            timer.Stop();
                            DevFileLog.Write( $"[MainWindow] Starting new process: {exe} {args}\n");
                            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true };
                            System.Diagnostics.Process.Start(psi);
                            DevFileLog.Write( "[MainWindow] New process started successfully\n");
                            System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
                        }
                        catch (Exception ex)
                        {
                            try { DevFileLog.Write( $"[MainWindow] Relaunch EX: {ex}\n"); } catch { }
                        }
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[MainWindow] SelectionChanged EX: {ex}\n"); } catch { }
            }
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.ComboBox cmb && cmb.SelectedItem is ComboBoxItem item && item.Tag is string themeName)
                {
                    DevFileLog.Write( $"[MainWindow] Theme selected: {themeName}\n");
                    _themeManager?.ApplyTheme(themeName);
                    try { _settingsService?.SetTheme(themeName); } catch { }
                    try { DumpThemeDebug(themeName); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[MainWindow] Theme change EX: {ex}\n"); } catch { }
            }
        }

        private void BtnSaveInfoJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var creator = (FindName("TxtInfoCreator") as System.Windows.Controls.TextBox)?.Text ?? "";
                var ver = (FindName("CmbInfoSSF2Version") as System.Windows.Controls.ComboBox)?.SelectedItem as ComboBoxItem;
                var verText = ver?.Content?.ToString() ?? "";
                var mt = (FindName("CmbInfoModType") as System.Windows.Controls.ComboBox)?.SelectedItem as ComboBoxItem;
                var mtText = mt?.Content?.ToString() ?? "";

                var obj = new
                {
                    creator = string.IsNullOrWhiteSpace(creator) ? null : creator,
                    ssf2_version = string.IsNullOrWhiteSpace(verText) ? null : verText,
                    mod_type = string.IsNullOrWhiteSpace(mtText) ? null : mtText
                };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented,
                    new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });

                var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save info.json", Filter = "JSON files|*.json", FileName = "info.json" };
                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, json);
                    MessageBox.Show($"Saved info.json to:\n{dlg.FileName}", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save info.json:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFillDefaults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fill creator with current user / app author and leave other defaults
                var creatorBox = FindName("TxtInfoCreator") as System.Windows.Controls.TextBox;
                if (creatorBox != null && string.IsNullOrWhiteSpace(creatorBox.Text))
                    creatorBox.Text = Environment.UserName ?? "";
            }
            catch { }
        }

        private void BtnTutorial_Click(object sender, RoutedEventArgs e)
        {
            var w = new Views.TutorialWindow();
            // Subscribe before setting steps so the initial step triggers navigation
            w.StepChanged += (page) =>
            {
                if (string.IsNullOrWhiteSpace(page)) return;
                // Map tutorial labels to internal page keys used by SetActivePage
                var key = page.ToLowerInvariant() switch
                {
                    "mods" => "browse",
                    "news" => "news",
                    "settings" => "settings",
                    "tools" => "resources",
                    "getting started" => "gettingstarted",
                    _ => page.ToLowerInvariant()
                };
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        SetActivePage(key);
                        // Try to focus the corresponding sidebar button if present
                        string? btnName = key switch
                        {
                            "browse" => "BtnBrowse",
                            "news" => "BtnNews",
                            "settings" => "BtnSettings",
                            "gettingstarted" => "BtnGettingStarted",
                            _ => null
                        };
                        if (!string.IsNullOrWhiteSpace(btnName) && FindName(btnName) is System.Windows.Controls.Control c)
                        {
                            c.Focus();
                        }
                    }
                    catch { }
                });
            };

            var steps = new List<(string title, string body, string? page)>
            {
                ("Welcome","Welcome to SSF2 Mod Manager! This tutorial will walk you through setting up your first version and installing a mod. Let's get started!", null),
                ("Step 1: Add a Version","First, go to Settings and add a version. A 'version' is a game build folder (like SSF2 v1.3 or a mod build). Click 'Add Version' and select your SSF2 folder.", "Settings"),
                ("Why Multiple Versions?","You can have multiple SSF2 versions (vanilla, modded builds, different releases). Each version has its own mods list, so you can keep different setups organized without conflicts.", "Settings"),
                ("Step 2: Browse Mods","Now let's find a mod to install! Click 'Browse Mods' in the sidebar. This page shows mods from GameBanana that you can install.", "Mods"),
                ("Step 3: Choose a Mod","Browse the mods list or use the search box to find something you like. Each mod shows its name, author, category, and download stats. Hover over a mod card to see more info.", "Mods"),
                ("Step 4: Install a Mod","When you find a mod you want, click the 'Install' button. You'll be asked to choose which version to install it to - this is why we set up a version first!", "Mods"),
                ("Step 5: Check Installed Mods","After installing, go to the 'Installed' page to see your mods. They're organized by version, so you can see exactly what's in each build.", "Installed"),
                ("Step 6: Manage Your Mods","From the Installed page, you can enable/disable mods, uninstall them, or check for updates. You can also change which version a mod is assigned to.", "Installed"),
                ("Step 7: Start Playing!","Once you have mods installed, you can launch your game from the 'Installed Builds' page or use the Play button. Select your version and click 'Play' to start with your mods!", "Installed"),
                ("Step 8: Pro Tips","You can have multiple versions with different mod loadouts. This is perfect for testing mods, trying different characters, or keeping a clean vanilla version alongside modded builds.", "Builds"),
                ("That's It!","You're all set! Browse mods, build your collection, and have fun with SSF2. If you need help, check the Getting Started page for documentation and resources.", "Getting Started")
            };

            w.SetSteps(steps);
            w.Owner = this;
            w.ShowDialog();
        }

        private void DumpThemeDebug(string themeName)
        {
            if (!DevFileLog.Enabled) return;
            try
            {
                var app = System.Windows.Application.Current;
                var md = app.Resources.MergedDictionaries;
                var sb = new StringBuilder();
                sb.AppendLine($"Applied theme: {themeName}");
                sb.AppendLine($"Merged dictionaries: {md.Count}");
                for (int i = 0; i < md.Count; i++)
                {
                    var rd = md[i];
                    var src = rd.Source != null ? rd.Source.OriginalString : "(inline)";
                    sb.AppendLine($"  [{i}] {src}");
                }

                // Keys we care about and their resolved values
                var keys = new[] { "BackgroundBrush", "TextPrimaryBrush", "TextSecondaryBrush", "AccentBrush", "CardBrush", "PrimaryBrush", "SidebarBrush", "SurfaceBrush" };
                var present = new List<string>();
                var missing = new List<string>();
                foreach (var k in keys)
                {
                    object? resolved = null;
                    // Prefer values provided by merged dictionaries (themes) when present
                    for (int i = md.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            var rd = md[i];
                            if (rd.Contains(k))
                            {
                                resolved = rd[k];
                                break;
                            }
                        }
                        catch { }
                    }
                    // Fall back to application-level resource if not found in merged dictionaries
                    if (resolved == null && app.Resources.Contains(k)) resolved = app.Resources[k];

                    if (resolved != null)
                    {
                        present.Add(k);
                        // Describe the value
                        string desc;
                        switch (resolved)
                        {
                            case SolidColorBrush scb:
                                desc = $"SolidColorBrush {scb.Color}";
                                break;
                            case Color c:
                                desc = $"Color {c}";
                                break;
                            case Brush b:
                                desc = $"Brush ({b.GetType().Name})";
                                break;
                            case Style s:
                                desc = $"Style ({s.TargetType?.Name ?? "?"})";
                                break;
                            default:
                                desc = resolved.ToString() ?? "(null)";
                                break;
                        }
                        sb.AppendLine($"Key: {k} => {desc}");
                        // Also list which dictionaries provide this key
                        for (int i = 0; i < md.Count; i++)
                        {
                            try
                            {
                                var rd = md[i];
                                if (rd.Contains(k))
                                {
                                    var src = rd.Source != null ? rd.Source.OriginalString : "(inline)";
                                    sb.AppendLine($"    provider: [{i}] {src}");
                                }
                            }
                            catch { }
                        }
                        if (app.Resources.Contains(k))
                            sb.AppendLine($"    provider: application resources");
                        // Also report the result of TryFindResource (what WPF would resolve at runtime)
                        try
                        {
                            var tryRes = this.TryFindResource(k);
                            sb.AppendLine($"    TryFindResource: {(tryRes != null ? tryRes.GetType().Name : "(null)")}");
                        }
                        catch { }
                    }
                    else
                    {
                        missing.Add(k);
                        sb.AppendLine($"Key: {k} => MISSING");
                    }
                }

                sb.AppendLine($"Keys present: {string.Join(", ", present)}");
                if (missing.Count > 0) sb.AppendLine($"Keys missing: {string.Join(", ", missing)}");

                var txt = sb.ToString();
                try { DevFileLog.Write( $"[MainWindow] DumpThemeDebug:\n{txt}\n"); } catch { }

                if (FindName("TxtThemeDebug") is TextBlock tb)
                {
                    var shortPresent = present.Count > 0 ? string.Join(", ", present) : "(none)";
                    tb.Text = $"Applied: {themeName} — dicts: {md.Count} — keys: {shortPresent}";
                }

                // Inspect a few live UI element values to show what the window is actually using
                try
                {
                    var wndBg = this.Background;
                    var liveSb = new StringBuilder();
                    liveSb.AppendLine($"Window.Background: {(wndBg is SolidColorBrush sbg ? sbg.Color.ToString() : wndBg?.GetType().Name ?? "(null)")}");
                    if (FindName("BtnBrowse") is Button btn)
                    {
                        var bbg = btn.Background as SolidColorBrush;
                        var bfg = btn.Foreground as SolidColorBrush;
                        liveSb.AppendLine($"BtnBrowse.Background: {(bbg != null ? bbg.Color.ToString() : btn.Background?.GetType().Name ?? "(null)")}");
                        liveSb.AppendLine($"BtnBrowse.Foreground: {(bfg != null ? bfg.Color.ToString() : btn.Foreground?.GetType().Name ?? "(null)")}");
                    }
                    try { DevFileLog.Write( $"[MainWindow] LiveValues:\n{liveSb}\n"); } catch { }
                }
                catch { }
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write( $"[MainWindow] DumpThemeDebug EX: {ex}\n"); } catch { }
            }
        }

        // Helper to set TextBlock text by parent grid and child index
        private void SetTextBlock(string gridName, int childIndex, string text)
        {
            if (FindName(gridName) is Grid grid && grid.Children.Count > childIndex && grid.Children[childIndex] is TextBlock tb)
                tb.Text = text;
        }

        // Helper to set Button content by name
        private void SetButtonContent(string btnName, string text)
        {
            if (FindName(btnName) is Button btn)
                btn.Content = text;
        }

        private SSF2VersionEntry? ResolveTargetVersionEntry()
        {
            if (_modManager.Versions.Count == 0) return null;

            if (!string.IsNullOrEmpty(_modManager.ActiveVersion))
            {
                var active = _modManager.GetVersionEntry(_modManager.ActiveVersion);
                if (active != null) return active;
            }

            if (_modManager.Versions.Count == 1)
                return _modManager.Versions[0];

            var versionDisplayNames = _modManager.Versions
                .Select(v => $"{v.Nickname}  —  {v.VersionName}  —  {v.FolderPath}").ToList();
            var picker = new ListPickerDialog("Target Version",
                "Which SSF2 build should this mod be installed to?",
                versionDisplayNames);
            picker.Owner = this;
            if (picker.ShowDialog() != true || picker.SelectedItem == null)
                return null;

            var targetNickname = picker.SelectedItem.Split("  —  ", 3)[0].Trim();
            return _modManager.GetVersionEntry(targetNickname);
        }

        private InstalledMod? FindInstalledModByTarget(string target) =>
            _modManager.InstalledMods.FirstOrDefault(m =>
                m.Id.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                m.Name.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                m.GameBananaId.ToString() == target);

        public void InstallModFromProtocol(string archiveUrl, string modType, string modId) =>
            _ = InstallModFromProtocolAsync(archiveUrl, modType, modId);

        public async System.Threading.Tasks.Task InstallModFromProtocolAsync(string archiveUrl, string modType, string modId)
        {
            try
            {
                if (_modManager.Versions.Count == 0)
                {
                    MessageBox.Show(
                        "No SSF2 builds configured.\n\nAdd a version in Settings before using 1-Click install.",
                        "1-Click Install", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SetActivePage("settings");
                    return;
                }

                GameBananaMod? mod = null;
                GameBananaFile? file = null;
                var downloadUrl = archiveUrl;

                if (int.TryParse(modId, out var gbId) && gbId > 0)
                {
                    mod = await _apiClient.GetModAsync(gbId);
                    var files = await _apiClient.GetModFilesAsync(gbId);
                    file = files?.FirstOrDefault(f =>
                        (!string.IsNullOrEmpty(f.DownloadUrl) && archiveUrl.Contains(f.DownloadUrl, StringComparison.OrdinalIgnoreCase)) ||
                        archiveUrl.Contains(f.FileName, StringComparison.OrdinalIgnoreCase))
                        ?? files?.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                    if (file != null && !string.IsNullOrEmpty(file.DownloadUrl))
                        downloadUrl = file.DownloadUrl;
                }

                if (mod == null)
                {
                    var fallbackName = "1-Click Mod";
                    if (Uri.TryCreate(archiveUrl, UriKind.Absolute, out var uri))
                        fallbackName = Path.GetFileNameWithoutExtension(uri.LocalPath) ?? fallbackName;

                    mod = new GameBananaMod
                    {
                        Id = int.TryParse(modId, out var parsedId) ? parsedId : 0,
                        Name = fallbackName,
                        Category = new GameBananaCategory { Name = string.IsNullOrWhiteSpace(modType) ? "Other" : modType }
                    };
                }

                if (file == null)
                {
                    var fileName = "download.zip";
                    if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var dlUri))
                        fileName = Path.GetFileName(dlUri.LocalPath);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = "download.zip";

                    file = new GameBananaFile
                    {
                        FileName = fileName,
                        DownloadUrl = downloadUrl
                    };
                }

                if (file.HasScanWarning)
                {
                    var warn = MessageBox.Show(
                        $"⚠ Scan warning for \"{file.FileName}\":\n{file.ScanSummary}\n\nInstall anyway?",
                        "1-Click Install", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (warn != MessageBoxResult.Yes) return;
                }

                var confirm = MessageBox.Show(
                    $"Install this mod via 1-Click?\n\nMod: {mod.Name}\nFile: {file.FileName}\nType: {modType}\nBuild: (choose next if multiple)",
                    "1-Click Install", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                var targetEntry = ResolveTargetVersionEntry();
                if (targetEntry == null) return;

                DownloadOverlay.Visibility = Visibility.Visible;
                TxtDownloadStatus.Text = $"Downloading {mod.Name}...";
                DownloadProgress.Value = 0;
                TxtDownloadPercent.Text = "0%";

                var progress = new Progress<double>(p =>
                {
                    DownloadProgress.Value = p;
                    TxtDownloadPercent.Text = $"{p:F0}%";
                });

                byte[] fileBytes;
                try
                {
                    fileBytes = await _apiClient.DownloadFileAsync(downloadUrl, progress);
                }
                catch (Exception dlEx)
                {
                    DownloadOverlay.Visibility = Visibility.Collapsed;
                    DebugLogger.Error($"1-Click download failed: {mod.Name}", dlEx);
                    MessageBox.Show($"Failed to download mod:\n{dlEx.Message}", "1-Click Install",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                TxtDownloadStatus.Text = $"Installing {mod.Name}...";
                TxtDownloadPercent.Text = "Installing...";

                var installed = await _modManager.InstallModAsync(
                    mod, file, targetEntry.VersionName, progress, preDownloadedBytes: fileBytes);

                _modManager.ActiveVersion = targetEntry.Nickname;
                RefreshPlayButton();
                RefreshInstalledMods();
                DownloadOverlay.Visibility = Visibility.Collapsed;

                if (installed.IsGameFiles)
                {
                    MessageBox.Show(
                        $"\"{mod.Name}\" was downloaded but not deployed (Game files mod).\nDeploy it from Installed Mods when ready.",
                        "1-Click Install", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"\"{mod.Name}\" installed to {targetEntry.DisplayName}.",
                        "1-Click Install", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                SetActivePage("installed");
            }
            catch (Exception ex)
            {
                DownloadOverlay.Visibility = Visibility.Collapsed;
                DebugLogger.Error("InstallModFromProtocol failed", ex);
                MessageBox.Show($"Failed to install mod from 1-click URL:\n{ex.Message}", "1-Click Install",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyCliOptions(CliOptions cli)
        {
            try
            {
                DevFileLog.Write($"[CLI] Applying options: {System.Text.Json.JsonSerializer.Serialize(cli)}\n");

                if (cli.RegisterProtocol) ProtocolService.Register();
                if (cli.UnregisterProtocol) ProtocolService.Unregister();

                if (!string.IsNullOrWhiteSpace(cli.Theme))
                    _themeManager?.ApplyTheme(cli.Theme);

                if (!string.IsNullOrWhiteSpace(cli.OpenPage))
                    SetActivePage(cli.OpenPage);

                if (cli.OpenNews)
                {
                    OpenNews(cli.NewsPath);
                    if (cli.NewsPreviewOnly)
                    {
                        System.Threading.Tasks.Task.Delay(800).ContinueWith(_ =>
                            Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));
                        return;
                    }
                }

                foreach (var t in cli.Install)
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (ProtocolService.TryParse(t, out var proto))
                        _ = InstallModFromProtocolAsync(proto.ArchiveUrl, proto.ModType, proto.ModId?.ToString() ?? "");
                    else if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        _ = InstallModFromProtocolAsync(t, "", "");
                    else if (File.Exists(t))
                        _ = InstallLocalFileFromCliAsync(t);
                    else
                        DevFileLog.Write($"[CLI] Unknown install target: {t}\n");
                }

                foreach (var t in cli.Uninstall)
                {
                    var mod = FindInstalledModByTarget(t);
                    if (mod != null) _ = _modManager.UninstallModAsync(mod);
                }

                foreach (var t in cli.Enable)
                {
                    var mod = FindInstalledModByTarget(t);
                    if (mod != null && !mod.Enabled) _modManager.ToggleMod(mod);
                }

                foreach (var t in cli.Disable)
                {
                    var mod = FindInstalledModByTarget(t);
                    if (mod != null && mod.Enabled) _modManager.ToggleMod(mod);
                }

                foreach (var t in cli.Update)
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (ProtocolService.TryParse(t, out var proto))
                    {
                        _ = InstallModFromProtocolAsync(proto.ArchiveUrl, proto.ModType, proto.ModId?.ToString() ?? "");
                        continue;
                    }
                    if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = InstallModFromProtocolAsync(t, "", "");
                        continue;
                    }
                    if (int.TryParse(t, out var gbId))
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            var modInfo = await _apiClient.GetModAsync(gbId);
                            var files = await _apiClient.GetModFilesAsync(gbId);
                            var file = files?.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                            var entry = ResolveTargetVersionEntry();
                            if (modInfo != null && file != null && entry != null)
                            {
                                await _modManager.InstallModAsync(modInfo, file, entry.VersionName);
                                Dispatcher.Invoke(RefreshInstalledMods);
                            }
                        });
                        continue;
                    }
                    var inst = FindInstalledModByTarget(t);
                    if (inst != null)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            await _modManager.UpdateInstalledModAsync(inst);
                            Dispatcher.Invoke(RefreshInstalledMods);
                        });
                    }
                }

                if (cli.CheckUpdates)
                    _ = CheckProgramVersionAsync();

                if (!string.IsNullOrWhiteSpace(cli.DumpLog))
                {
                    try { File.Copy(AppPaths.DebugLogFile, cli.DumpLog, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                DevFileLog.Write($"[CLI] ApplyCliOptions EX: {ex}\n");
                DebugLogger.Error("ApplyCliOptions failed", ex);
            }
        }

        private async System.Threading.Tasks.Task InstallLocalFileFromCliAsync(string path)
        {
            if (_modManager.Versions.Count == 0)
            {
                MessageBox.Show("Add an SSF2 build in Settings before installing.", "CLI Install",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var entry = ResolveTargetVersionEntry();
            if (entry == null) return;

            var fileName = Path.GetFileName(path);
            var modName = Path.GetFileNameWithoutExtension(path) ?? "Local Mod";
            var fileBytes = await File.ReadAllBytesAsync(path);
            await _modManager.InstallLocalModAsync(modName, "Other", fileName, fileBytes, entry.VersionName);
            RefreshInstalledMods();
        }

        // ── Title Bar ────────────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                BtnMaximize_Click(sender, e);
            else
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            BtnMaximize.Content = WindowState == WindowState.Maximized ? "\u2750" : "\u2610";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/yVPkqKQsx2") { UseShellExecute = true });
        }

        private void BtnFeedback_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://forms.gle/hhJgq2jsRFkGoysU9") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnSupport_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/justwex") { UseShellExecute = true });
        }

        private void BtnResourceLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        // ── Navigation ──────────────────────────────────────────

        private void SetActivePage(string page)
        {
            _activePageName = page;
            PageBrowse.Visibility = page == "browse" ? Visibility.Visible : Visibility.Collapsed;
            PageInstalled.Visibility = page == "installed" ? Visibility.Visible : Visibility.Collapsed;
            PageBuilds.Visibility = page == "builds" ? Visibility.Visible : Visibility.Collapsed;
            PageCostumes.Visibility = page == "costumes" ? Visibility.Visible : Visibility.Collapsed;
            PageEvents.Visibility = page == "events" ? Visibility.Visible : Visibility.Collapsed;
            // Getting Started uses a dedicated page that contains the info.json tool + resource cards
            PageGettingStarted.Visibility = page == "gettingstarted" ? Visibility.Visible : Visibility.Collapsed;
            PageModSSF2.Visibility = page == "modssf2" ? Visibility.Visible : Visibility.Collapsed;
            PageNews.Visibility = page == "news" ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = page == "log" ? Visibility.Visible : Visibility.Collapsed;

            // Reset scroll position to top when switching pages
            try
            {
                if (page == "browse" && BrowseScrollViewer != null)
                    BrowseScrollViewer.ScrollToVerticalOffset(0);
                if (page == "installed" && InstalledScrollViewer != null)
                    InstalledScrollViewer.ScrollToVerticalOffset(0);
                if (page == "settings" && SettingsScrollViewer != null)
                    SettingsScrollViewer.ScrollToVerticalOffset(0);
                if (page == "log" && LogScrollViewer != null)
                    LogScrollViewer.ScrollToVerticalOffset(0);
            }
            catch { }

            BtnBrowse.Style = (Style)FindResource(page == "browse" ? "SidebarButtonActive" : "SidebarButton");
            BtnInstalled.Style = (Style)FindResource(page == "installed" ? "SidebarButtonActive" : "SidebarButton");
            BtnBuilds.Style = (Style)FindResource(page == "builds" ? "SidebarButtonActive" : "SidebarButton");
            BtnCostumes.Style = (Style)FindResource(page == "costumes" ? "SidebarButtonActive" : "SidebarButton");
            BtnEvents.Style = (Style)FindResource(page == "events" ? "SidebarButtonActive" : "SidebarButton");
            BtnGettingStarted.Style = (Style)FindResource(page == "gettingstarted" ? "SidebarButtonActive" : "SidebarButton");
            BtnNews.Style = (Style)FindResource(page == "news" ? "SidebarButtonActive" : "SidebarButton");
            BtnSettings.Style = (Style)FindResource(page == "settings" ? "SidebarButtonActive" : "SidebarButton");
            BtnLog.Style = (Style)FindResource(page == "log" ? "SidebarButtonActive" : "SidebarButton");

            // Ensure sidebar button foregrounds follow the active/inactive selection brushes (some themes override hard-coded values)
            Brush activeFg = TryFindResource("SelectionForegroundBrush") as Brush
                ?? TryFindResource("TextPrimaryBrush") as Brush
                ?? Brushes.White;
            Brush inactiveFg = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

            BtnBrowse.Foreground = page == "browse" ? activeFg : inactiveFg;
            BtnInstalled.Foreground = page == "installed" ? activeFg : inactiveFg;
            BtnBuilds.Foreground = page == "builds" ? activeFg : inactiveFg;
            BtnCostumes.Foreground = page == "costumes" ? activeFg : inactiveFg;
            BtnEvents.Foreground = page == "events" ? activeFg : inactiveFg;
            BtnGettingStarted.Foreground = page == "gettingstarted" ? activeFg : inactiveFg;
            BtnNews.Foreground = page == "news" ? activeFg : inactiveFg;
            BtnSettings.Foreground = page == "settings" ? activeFg : inactiveFg;
            BtnLog.Foreground = page == "log" ? activeFg : inactiveFg;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e) => SetActivePage("browse");

        private void BtnInstalled_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("installed");
            RefreshInstalledMods();
        }

        private void BtnCostumes_Click(object sender, RoutedEventArgs e) => SetActivePage("costumes");
        private void BtnEvents_Click(object sender, RoutedEventArgs e) => SetActivePage("events");
        private void BtnNews_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("news");
            try
            {
                var np = FindName("PageNews") as dynamic;
                if (np != null)
                {
                    // Use local News folder in repo for now (explicit workspace path)
                    var newsPath = @"C:\Users\glwex\Documents\GitHub\SSF2ModManager\News";
                    np.LoadLocal(newsPath);
                }
            }
            catch { }
        }

        // Public helper to open News page programmatically (used by CLI handlers)
        public void OpenNews(string? newsPath = null)
        {
            SetActivePage("news");
            try
            {
                var np = FindName("PageNews") as dynamic;
                if (np != null)
                {
                    var path = newsPath;
                    if (string.IsNullOrWhiteSpace(path))
                        path = System.IO.Path.Combine(Environment.CurrentDirectory, "News");
                    try { np.LoadLocal(path); } catch { }
                }
            }
            catch { }
        }
        private void BtnModSSF2_Click(object sender, RoutedEventArgs e) => SetActivePage("gettingstarted");

        private void BtnGettingStarted_Click(object sender, RoutedEventArgs e) => SetActivePage("gettingstarted");

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SetActivePage("settings");

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("log");
            RefreshDebugLog();
        }

        private void BtnBuilds_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("builds");
            RefreshBuildsList();
        }

        // ── Browse Mods ─────────────────────────────────────────

        private async System.Threading.Tasks.Task LoadBrowseModsAsync()
        {
            TxtLoading.Visibility = Visibility.Visible;
            TxtNoResults.Visibility = Visibility.Collapsed;
            ModBrowserList.ItemsSource = null;

            try
            {
                List<BrowseModViewModel> viewModels;
                bool hasMore = false;
                int totalCount = 0;

                if (_searchMode == "author" && !string.IsNullOrEmpty(_currentSearch))
                {
                    // Client-side filter by author name
                    var allMods = await _apiClient.GetAllModsAsync();
                    var filtered = allMods
                        .Where(m => m.Submitter?.Name != null &&
                                    m.Submitter.Name.Equals(_currentSearch, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    totalCount = filtered.Count;
                    var page = filtered.Skip((_currentPage - 1) * 15).Take(15).ToList();
                    hasMore = _currentPage * 15 < totalCount;
                    viewModels = page.Select(m => new BrowseModViewModel { Mod = m }).ToList();
                }
                else if (_searchMode == "category" && !string.IsNullOrEmpty(_currentSearch))
                {
                    // Client-side filter by category name
                    var allMods = await _apiClient.GetAllModsAsync();
                    var filtered = allMods
                        .Where(m => (m.RootCategory?.Name ?? m.Category?.Name ?? "")
                                    .Equals(_currentSearch, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    totalCount = filtered.Count;
                    var page = filtered.Skip((_currentPage - 1) * 15).Take(15).ToList();
                    hasMore = _currentPage * 15 < totalCount;
                    viewModels = page.Select(m => new BrowseModViewModel { Mod = m }).ToList();
                }
                else
                {
                    // Normal text search or browse
                    var (mods, more, count) = await _apiClient.SearchModsAsync(_currentSearch, _currentPage, 15, _currentSort);
                    hasMore = more;
                    totalCount = count;
                    viewModels = mods.Select(m => new BrowseModViewModel { Mod = m }).ToList();
                }

                ModBrowserList.ItemsSource = viewModels;

                // Cross-reference with installed mods
                foreach (var vm in viewModels)
                {
                    vm.InstalledVersion = _modManager.InstalledMods
                        .FirstOrDefault(m => m.GameBananaId == vm.Mod.Id);
                }

                TxtNoResults.Visibility = viewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                BtnPrevPage.IsEnabled = _currentPage > 1;
                BtnNextPage.IsEnabled = hasMore;
                int totalPages = totalCount > 0 ? (int)Math.Ceiling(totalCount / 15.0) : _currentPage;
                TxtPageInfo.Text = $"Page {_currentPage} of {totalPages}";
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Failed to load mods", ex);
                TxtNoResults.Text = $"Error loading mods: {ex.Message}";
                TxtNoResults.Visibility = Visibility.Visible;
            }
            finally
            {
                TxtLoading.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            _searchMode = "text";
            _currentSearch = TxtSearch.Text.Trim();
            _currentPage = 1;
            UpdateClearButton();
            await LoadBrowseModsAsync();
        }

        private async void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            _currentSearch = "";
            _searchMode = "text";
            _currentPage = 1;
            UpdateClearButton();
            await LoadBrowseModsAsync();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateClearButton();
        }

        private void UpdateClearButton()
        {
            if (BtnClearSearch != null)
                BtnClearSearch.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                _currentSearch = TxtSearch.Text.Trim();
                _currentPage = 1;
                await LoadBrowseModsAsync();
            }
        }

        private async void CmbSort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbSort.SelectedIndex < 0) return;
            _currentSort = CmbSort.SelectedIndex switch
            {
                1 => "new",
                2 => "updated",
                3 => "likes",
                4 => "downloads",
                _ => "default"
            };
            _currentPage = 1;
            await LoadBrowseModsAsync();
        }

        private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadBrowseModsAsync();
            }
        }

        private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await LoadBrowseModsAsync();
        }

        // ── Install Mod ────────────────────────────────────────

        private async void BtnInstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (_modManager.Versions.Count == 0)
            {
                MessageBox.Show("Please add at least one SSF2 version in Settings first.",
                    "No Versions Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetActivePage("settings");
                return;
            }

            var button = (Button)sender;
            var vm = (BrowseModViewModel)button.Tag;
            var mod = vm.Mod;

            // Fetch files if not loaded
            if (mod.Files == null || mod.Files.Count == 0)
            {
                try
                {
                    mod.Files = await _apiClient.GetModFilesAsync(mod.Id);
                }
                catch (Exception ex)
                {
                    DebugLogger.Error($"Failed to fetch files for {mod.Name}", ex);
                    MessageBox.Show($"Failed to fetch mod files:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // (no-op for theme changes here)
            }

            if (mod.Files == null || mod.Files.Count == 0)
            {
                MessageBox.Show("This mod has no downloadable files.", "No Files",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pick file
            GameBananaFile selectedFile;
            if (mod.Files.Count == 1)
            {
                selectedFile = mod.Files[0];
            }
            else
            {
                // Let user choose which file to install
                var fileOptions = mod.Files
                    .Select(f => $"{f.FileName}  —  {f.FileSizeFormatted}")
                    .ToList();
                
                var filePicker = new ListPickerDialog("Select File",
                    $"\"{mod.Name}\" has multiple files. Which one do you want to install?",
                    fileOptions);
                filePicker.Owner = this;
                
                if (filePicker.ShowDialog() != true || filePicker.SelectedItem == null)
                    return;
                
                // Extract the selected file by matching the filename
                var selectedFileName = filePicker.SelectedItem.Split("  —  ", 2)[0].Trim();
                selectedFile = mod.Files.First(f => f.FileName == selectedFileName);
            }

            // Show virus scan report
            var scanInfo = $"🔬 Virus Scan Report for \"{selectedFile.FileName}\":\n\n{selectedFile.ScanSummary}";
            if (selectedFile.HasScanWarning)
            {
                // First confirmation
                var warn1 = MessageBox.Show(
                    $"⚠ SCAN WARNING ⚠\n\n{scanInfo}\n\n" +
                    $"This file has a scan warning. Are you sure you want to install it?",
                    "Virus Scan Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (warn1 != MessageBoxResult.Yes) return;

                // Second confirmation (double-confirm)
                var warn2 = MessageBox.Show(
                    $"⚠ FINAL CONFIRMATION ⚠\n\n" +
                    $"You are about to install a file with a security warning.\n" +
                    $"File: {selectedFile.FileName}\n" +
                    $"Scan: {selectedFile.ScanSummary}\n\n" +
                    $"Are you REALLY sure?",
                    "Confirm Installation", MessageBoxButton.YesNo, MessageBoxImage.Stop);
                if (warn2 != MessageBoxResult.Yes) return;
            }

            // Pick target version
            var versionDisplayNames = _modManager.Versions
                .Select(v => $"{v.Nickname}  —  {v.VersionName}  —  {v.FolderPath}").ToList();
            var picker = new ListPickerDialog("Target Version",
                $"Which SSF2 version should \"{mod.Name}\" be installed to?",
                versionDisplayNames, showModPackButton: true);
            picker.Owner = this;

            if (picker.ShowDialog() != true)
                return;

            // Handle mod pack installation
            if (picker.ModPackSelected)
            {
                await InstallAsModPackAsync(mod, selectedFile);
                return;
            }

            if (picker.SelectedItem == null)
                return;

            // Extract nickname from "nickname  —  version  —  path" format
            var targetNickname = picker.SelectedItem.Split("  —  ", 3)[0].Trim();
            var targetEntry = _modManager.GetVersionEntry(targetNickname);
            if (targetEntry == null) return;
            var targetVersion = targetEntry.VersionName;

            // Check for existing mod with same GameBanana ID in the SAME build
            var existingMod = _modManager.InstalledMods.FirstOrDefault(m => 
                m.GameBananaId == mod.Id && m.TargetVersion == targetVersion);
            if (existingMod != null)
            {
                var overrideResult = MessageBox.Show(
                    $"\"{mod.Name}\" is already installed in {targetEntry.DisplayName}.\n\n" +
                    $"Status: {(existingMod.Enabled ? "Enabled" : "Disabled")}\n\n" +
                    $"Reinstalling will remove the existing installation first.\nContinue?",
                    "Mod Already Installed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (overrideResult != MessageBoxResult.Yes) return;
            }

            // Download & install
            DownloadOverlay.Visibility = Visibility.Visible;
            TxtDownloadStatus.Text = $"Downloading {mod.Name}...";
            DownloadProgress.Value = 0;
            TxtDownloadPercent.Text = "0%";

            var progress = new Progress<double>(p =>
            {
                DownloadProgress.Value = p;
                TxtDownloadPercent.Text = $"{p:F0}%";
            });

            try
            {
                // Download file first
                byte[] fileBytes;
                try
                {
                    fileBytes = await _apiClient.DownloadFileAsync(selectedFile.DownloadUrl, progress);
                }
                catch (Exception dlEx)
                {
                    DebugLogger.Error($"Download failed: {mod.Name}", dlEx);
                    MessageBox.Show($"Failed to download mod:\n{dlEx.Message}", "Download Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                DownloadOverlay.Visibility = Visibility.Collapsed;

                // Preview archive contents and let user select files
                HashSet<string>? selectedFiles = null;
                var archiveContents = _modManager.PreviewArchiveContents(fileBytes, selectedFile.FileName);
                if (archiveContents.Count > 1)
                {
                    // Try to extract info.json from the archive and pass metadata into the selection dialog
                    FileSelectionDialog.InfoMetadata? metadata = null;
                    try
                    {
                        using var mstream = new System.IO.MemoryStream(fileBytes);
                        using var archive = ArchiveFactory.Open(mstream);
                        var infoEntry = archive.Entries.FirstOrDefault(en => !en.IsDirectory && (en.Key?.Equals("info.json", StringComparison.OrdinalIgnoreCase) == true || en.Key?.EndsWith("/info.json", StringComparison.OrdinalIgnoreCase) == true || en.Key?.EndsWith("\\info.json", StringComparison.OrdinalIgnoreCase) == true));
                        if (infoEntry != null)
                        {
                            using var s = infoEntry.OpenEntryStream();
                            using var sr = new System.IO.StreamReader(s);
                            var infoText = sr.ReadToEnd();
                            try
                            {
                                dynamic? info = Newtonsoft.Json.JsonConvert.DeserializeObject(infoText);
                                if (info != null)
                                {
                                    metadata = new FileSelectionDialog.InfoMetadata();
                                    if (info.creator != null) metadata.Creator = (string)info.creator;
                                    if (info.ssf2_version != null) metadata.Ssf2Version = (string)info.ssf2_version;
                                    if (info.mod_type != null) metadata.ModType = (string)info.mod_type;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    var items = archiveContents.Select(a => new FileSelectionItem
                    {
                        Path = a.Path,
                        DisplaySize = FormatFileSize(a.Size),
                        IsSelected = true
                    });
                    var dialog = new FileSelectionDialog(mod.Name,
                        $"Select which files to install from \"{selectedFile.FileName}\":", items, metadata);
                    dialog.Owner = this;
                    if (dialog.ShowDialog() != true)
                        return;
                    if (dialog.SelectedFiles.Count == 0)
                    {
                        MessageBox.Show("No files selected. Installation cancelled.", "Cancelled",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    selectedFiles = new HashSet<string>(dialog.SelectedFiles, StringComparer.OrdinalIgnoreCase);
                }

                // Pre-install: inspect archive for .ssf entries and pre-handle conflicts
                var preHandledConflicts = false;
                try
                {
                    var ssfEntries = archiveContents
                        .Where(a => a.Path.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                        .Select(a => a.Path).ToList();
                    if (ssfEntries.Count > 0)
                    {
                        // If user filtered selected files, respect that selection
                        if (selectedFiles != null)
                        {
                            ssfEntries = ssfEntries.Where(p => selectedFiles.Contains(p)).ToList();
                        }

                        var ssfFileNames = ssfEntries
                            .Select(p => Path.GetFileName(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (ssfFileNames.Count > 0)
                        {
                            var preConflicts = _modManager.GetFileConflicts(targetVersion, ssfFileNames, mod.Id);
                            if (preConflicts.Count > 0)
                            {
                                var conflictLines = new List<string>();
                                foreach (var (file, mods2) in preConflicts)
                                {
                                    var modNames = string.Join(", ", mods2.Select(m => m.Name));
                                    conflictLines.Add($"  • {file} → conflicts with: {modNames}");
                                }
                                var conflictText = string.Join("\n", conflictLines);
                                var conflictMods = preConflicts.Values.SelectMany(m => m).Distinct().ToList();

                                var conflictResult = MessageBox.Show(
                                    $"⚠ File Conflicts Detected (Before Install)!\n\n" +
                                    $"\"{mod.Name}\" contains files that are also used by other mods:\n\n" +
                                    $"{conflictText}\n\n" +
                                    $"The new mod takes priority. Would you like to partially disable the conflicting mods BEFORE installing?\n\n" +
                                    $"• YES = Disable only the overlapping files in conflicting mods\n" +
                                    $"• NO = Leave as-is (you can handle conflicts after install)",
                                    "Pre-Install File Conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                                if (conflictResult == MessageBoxResult.Yes)
                                {
                                    foreach (var conflictMod in conflictMods)
                                    {
                                        var overlappingFiles = preConflicts
                                            .Where(kv => kv.Value.Contains(conflictMod))
                                            .Select(kv => kv.Key).ToList();
                                        _modManager.PartialDisableMod(conflictMod, overlappingFiles, false);
                                    }
                                    DebugLogger.Log($"Pre-install: Partially disabled {conflictMods.Count} conflicting mod(s)");
                                    RefreshInstalledMods();
                                    preHandledConflicts = true;
                                }
                            }
                        }
                    }
                }
                catch (Exception preEx)
                {
                    DebugLogger.Error("Pre-install conflict check failed", preEx);
                }

                // Re-show overlay for install phase
                DownloadOverlay.Visibility = Visibility.Visible;
                TxtDownloadStatus.Text = $"Installing {mod.Name}...";
                DownloadProgress.Value = 100;
                TxtDownloadPercent.Text = "Installing...";

                var installed = await _modManager.InstallModAsync(mod, selectedFile, targetVersion, progress,
                    selectedFiles: selectedFiles, preDownloadedBytes: fileBytes);

                // Update play button to target version
                _modManager.ActiveVersion = targetNickname;
                RefreshPlayButton();

                // Check file-level conflicts with other enabled mods (skip if pre-handled)
                if (!preHandledConflicts && !installed.IsGameFiles && installed.BackedUpFiles.Count > 0)
                {
                    var deployedFileNames = installed.BackedUpFiles
                        .Select(b => Path.GetFileName(b.OriginalRelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var conflicts = _modManager.GetFileConflicts(targetVersion, deployedFileNames, mod.Id);

                    if (conflicts.Count > 0)
                    {
                        var conflictLines = new List<string>();
                        foreach (var (file, mods2) in conflicts)
                        {
                            var modNames = string.Join(", ", mods2.Select(m => m.Name));
                            conflictLines.Add($"  • {file} → conflicts with: {modNames}");
                        }
                        var conflictText = string.Join("\n", conflictLines);
                        var conflictMods = conflicts.Values.SelectMany(m => m).Distinct().ToList();

                        var conflictResult = MessageBox.Show(
                            $"⚠ File Conflicts Detected!\n\n" +
                            $"\"{mod.Name}\" has replaced files that are also used by other mods:\n\n" +
                            $"{conflictText}\n\n" +
                            $"The new mod takes priority. Would you like to partially disable the conflicting mods?\n\n" +
                            $"• YES = Disable only the overlapping files in conflicting mods\n" +
                            $"• NO = Leave as-is (new mod's files are already active)",
                            "File Conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        if (conflictResult == MessageBoxResult.Yes)
                        {
                            foreach (var conflictMod in conflictMods)
                            {
                                var overlappingFiles = conflicts
                                    .Where(kv => kv.Value.Contains(conflictMod))
                                    .Select(kv => kv.Key).ToList();
                                _modManager.PartialDisableMod(conflictMod, overlappingFiles, false);
                            }
                            DebugLogger.Log($"Partially disabled {conflictMods.Count} conflicting mod(s)");
                            // Refresh installed mods list so disabled states are reflected
                            RefreshInstalledMods();
                        }
                    }
                }

                if (installed.IsGameFiles)
                {
                    DownloadOverlay.Visibility = Visibility.Collapsed;

                    // Show "not deployed" popup with Install Anyway option
                    var files = _modManager.GetModFileStructure(installed);
                    var preview = string.Join("\n", files.Take(20));
                    if (files.Count > 20) preview += $"\n... and {files.Count - 20} more files";

                    var gameFilesResult = MessageBox.Show(
                        $"⚠ \"{mod.Name}\" is a Game files mod.\n\n" +
                        $"It was downloaded but NOT deployed to your build.\n" +
                        $"Game files mods replace core game files and need manual confirmation.\n\n" +
                        $"Files in this mod:\n{preview}\n\n" +
                        $"Click YES to deploy files now, or NO to skip (you can deploy later from Installed Mods).",
                        "Game Files — Not Installed",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (gameFilesResult == MessageBoxResult.Yes)
                    {
                        try
                        {
                            _modManager.DeployGameFilesMod(installed);
                            MessageBox.Show("Files deployed successfully!", "Done",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception deployEx)
                        {
                            DebugLogger.Error($"Deploy game files failed: {mod.Name}", deployEx);
                            MessageBox.Show($"Error deploying files:\n{deployEx.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    TxtDownloadStatus.Text = "✅ Installed!";
                    TxtDownloadPercent.Text = "Complete";
                    await System.Threading.Tasks.Task.Delay(1200);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Install failed: {mod.Name}", ex);
                MessageBox.Show($"Failed to install mod:\n{ex.Message}", "Install Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task InstallAsModPackAsync(GameBananaMod mod, GameBananaFile file)
        {
            // Pick SSF2 version for this mod pack
            var versionPicker = new ListPickerDialog("Mod Pack Version",
                $"Which SSF2 version is \"{mod.Name}\" based on?",
                ModManagerService.KnownVersions);
            versionPicker.Owner = this;
            if (versionPicker.ShowDialog() != true || versionPicker.SelectedItem == null)
                return;

            var versionName = versionPicker.SelectedItem;
            if (versionName == "Custom...")
            {
                var inputDialog = new InputDialog("Custom Version Name", "Enter a name for this version:");
                inputDialog.Owner = this;
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.InputText))
                    return;
                versionName = inputDialog.InputText.Trim();
            }

            // Pick destination folder
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Choose where to extract \"{mod.Name}\" as a new build",
                ShowNewFolderButton = true
            };
            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var destFolder = folderDialog.SelectedPath;

            // Always require a nickname (max 10 chars) — used as folder name and build name
            string nickname;
            while (true)
            {
                var nicknameDialog = new InputDialog("Build Nickname",
                    $"Enter a nickname for this mod pack (max 10 characters).\nThis will be the folder name and build name.", maxLength: 10);
                nicknameDialog.Owner = this;
                if (nicknameDialog.ShowDialog() != true)
                    return;

                nickname = nicknameDialog.InputText?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(nickname))
                {
                    MessageBox.Show("Nickname is required.", "Nickname Required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                if (nickname.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show("Nickname contains invalid characters for a folder name.",
                        "Invalid Nickname", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                if (_modManager.Versions.Any(v => v.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"A build named \"{nickname}\" already exists. Choose a different nickname.",
                        "Duplicate Nickname", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                break;
            }

            // Create subfolder using nickname
            destFolder = Path.Combine(destFolder, nickname);
            Directory.CreateDirectory(destFolder);

            // Download
            DownloadOverlay.Visibility = Visibility.Visible;
            TxtDownloadStatus.Text = $"Downloading mod pack: {mod.Name}...";
            DownloadProgress.Value = 0;
            TxtDownloadPercent.Text = "0%";

            var progress = new Progress<double>(p =>
            {
                DownloadProgress.Value = p;
                TxtDownloadPercent.Text = $"{p:F0}%";
            });

            try
            {
                var fileBytes = await _apiClient.DownloadFileAsync(file.DownloadUrl, progress);
                DebugLogger.Log($"Mod pack downloaded: {fileBytes.Length} bytes");

                if (fileBytes.Length == 0)
                {
                    MessageBox.Show("Download returned empty file. The mod may not have a valid download URL.",
                        "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                TxtDownloadStatus.Text = "Extracting mod pack...";
                TxtDownloadPercent.Text = "Extracting...";
                DownloadProgress.Value = 100;

                // Extract to destination
                int extractedCount = 0;
                await System.Threading.Tasks.Task.Run(() =>
                {
                    using var stream = new MemoryStream(fileBytes);
                    using var archive = ArchiveFactory.Open(stream);
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        entry.WriteToDirectory(destFolder, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                        extractedCount++;
                    }
                });

                DebugLogger.Log($"Mod pack extracted {extractedCount} files to {destFolder}");

                if (extractedCount == 0)
                {
                    MessageBox.Show("No files were found in the archive. The download may have failed.",
                        "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Register as a new build
                _modManager.AddVersion(versionName, destFolder, nickname);

                // Set as active version
                _modManager.ActiveVersion = nickname;
                RefreshPlayButton();
                RefreshVersionsList();
                RefreshBuildsList();

                TxtDownloadStatus.Text = $"✅ Mod pack installed as build: {nickname}";
                TxtDownloadPercent.Text = "Complete";
                await System.Threading.Tasks.Task.Delay(1500);

                DebugLogger.Log($"Mod pack installed: {mod.Name} as build '{nickname}' ({versionName}) at {destFolder}");
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Mod pack install failed: {mod.Name}", ex);
                MessageBox.Show($"Failed to install mod pack:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnViewOnGB_Click(object sender, RoutedEventArgs e)
        {
            var vm = (BrowseModViewModel)((Button)sender).Tag;
            if (!string.IsNullOrEmpty(vm.Mod.ProfileUrl))
            {
                Process.Start(new ProcessStartInfo(vm.Mod.ProfileUrl) { UseShellExecute = true });
            }
        }

        private async void BtnBrowseToggleMod_Click(object sender, RoutedEventArgs e)
        {
            var vm = (BrowseModViewModel)((Button)sender).Tag;
            if (vm.InstalledVersion == null) return;
            try
            {
                _modManager.ToggleMod(vm.InstalledVersion);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Toggle failed: {vm.Name}", ex);
                MessageBox.Show($"Error toggling mod:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            await LoadBrowseModsAsync();
        }

        // ── Preview Panel (hover description) ──────────────────

        private BrowseModViewModel? _currentPreviewVm;

        private async void BrowseCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var border = (Border)sender;
            var vm = border.Tag as BrowseModViewModel;
            if (vm == null || vm == _currentPreviewVm) return;
            _currentPreviewVm = vm;

            TxtPreviewTitle.Text = vm.Name;
            PreviewPanel.Visibility = Visibility.Visible;

            if (vm.DescriptionFetched)
            {
                TxtPreviewDescription.Text = vm.CachedDescriptionText ?? "No description available.";
                return;
            }

            TxtPreviewDescription.Text = "Loading description...";

            try
            {
                var htmlText = await _apiClient.GetModTextAsync(vm.Mod.Id);
                vm.CachedVideoUrl = BrowseModViewModel.ExtractYouTubeUrl(htmlText);
                vm.HasVideo = vm.CachedVideoUrl != null;
                var stripped = Regex.Replace(htmlText ?? "", "<[^>]+>", " ");
                stripped = System.Net.WebUtility.HtmlDecode(stripped);
                stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
                vm.CachedDescriptionText = string.IsNullOrEmpty(stripped) ? "No description available." : stripped;
                vm.DescriptionFetched = true;

                // Only update UI if still hovering this mod
                if (_currentPreviewVm == vm)
                {
                    TxtPreviewDescription.Text = vm.CachedDescriptionText;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Failed to fetch description for {vm.Name}", ex);
                if (_currentPreviewVm == vm)
                    TxtPreviewDescription.Text = "Failed to load description.";
                vm.DescriptionFetched = true;
                vm.CachedDescriptionText = "Failed to load description.";
            }
        }

        private void BtnCardVideo_Click(object sender, RoutedEventArgs e)
        {
            var vm = (BrowseModViewModel)((Button)sender).Tag;
            if (!string.IsNullOrEmpty(vm.CachedVideoUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(vm.CachedVideoUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("Failed to open video", ex);
                }
            }
        }

        // ── Install Local Mod ───────────────────────────────────

        private async void BtnInstallLocal_Click(object sender, RoutedEventArgs e)
        {
            if (_modManager.Versions.Count == 0)
            {
                MessageBox.Show("Please add at least one SSF2 version in Settings first.",
                    "No Versions Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetActivePage("settings");
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Select a mod archive to install",
                Filter = "Mod Archives (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            var filePath = dialog.FileName;
            var fileName = Path.GetFileName(filePath);

            // Ask for mod name
            var nameDialog = new InputDialog("Mod Name",
                $"Enter a name for this mod:\n\nFile: {fileName}");
            nameDialog.Owner = this;
            if (nameDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDialog.InputText))
                return;
            var modName = nameDialog.InputText.Trim();

            // Pick category
            var categories = new List<string> { "Characters", "Maps", "Skins", "Effects", "Sounds", "UI", "Game files", "Other" };
            var catPicker = new ListPickerDialog("Category", "Select a category for this mod:", categories);
            catPicker.Owner = this;
            if (catPicker.ShowDialog() != true || catPicker.SelectedItem == null) return;
            var category = catPicker.SelectedItem;

            // Pick target version
            var versionDisplayNames = _modManager.Versions
                .Select(v => $"{v.VersionName}  —  {v.FolderPath}").ToList();
            var vPicker = new ListPickerDialog("Target Version",
                $"Which SSF2 version should \"{modName}\" be installed to?", versionDisplayNames);
            vPicker.Owner = this;
            if (vPicker.ShowDialog() != true || vPicker.SelectedItem == null) return;
            var targetVersion = vPicker.SelectedItem.Split("  —  ", 2)[0].Trim();

            try
            {
                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var installed = await _modManager.InstallLocalModAsync(modName, category, fileName, fileBytes, targetVersion);

                if (installed.IsGameFiles)
                {
                    var files = _modManager.GetModFileStructure(installed);
                    var preview = string.Join("\n", files.Take(20));
                    if (files.Count > 20) preview += $"\n... and {files.Count - 20} more files";

                    var gameFilesResult = MessageBox.Show(
                        $"⚠ \"{modName}\" is a Game files mod.\n\n" +
                        $"Files in this mod:\n{preview}\n\n" +
                        $"Click YES to deploy files now, or NO to skip.",
                        "Game Files — Not Installed",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (gameFilesResult == MessageBoxResult.Yes)
                    {
                        _modManager.DeployGameFilesMod(installed);
                        MessageBox.Show("Files deployed successfully!", "Done",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show($"✅ \"{modName}\" installed successfully!", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Local install failed: {modName}", ex);
                MessageBox.Show($"Failed to install mod:\n{ex.Message}", "Install Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Installed Mods ──────────────────────────────────────

        private void RefreshInstalledMods()
        {
            PopulateInstalledFilters();
            ApplyInstalledFilters();
        }

        private void BtnToggleMod_Click(object sender, RoutedEventArgs e)
        {
            var mod = (InstalledMod)((Button)sender).Tag;
            try
            {
                // If enabling a disabled mod, check for file conflicts with other enabled mods
                if (!mod.Enabled)
                {
                    try
                    {
                        var modPath = System.IO.Path.Combine(_modManager.ModsBaseDir, mod.Category, mod.FolderName);
                        var ssfNames = _modManager.GetSsfFileNames(modPath);
                        var conflicts = _modManager.GetFileConflicts(mod.TargetVersion, ssfNames, mod.GameBananaId);
                        if (conflicts.Count > 0)
                        {
                            var conflictLines = new List<string>();
                            foreach (var (file, mods2) in conflicts)
                            {
                                var modNames = string.Join(", ", mods2.Select(m => m.Name));
                                conflictLines.Add($"  • {file} → conflicts with: {modNames}");
                            }
                            var conflictText = string.Join("\n", conflictLines);
                            var conflictMods = conflicts.Values.SelectMany(m => m).Distinct().ToList();

                            var conflictResult = MessageBox.Show(
                                $"⚠ File Conflicts Detected!\n\n" +
                                $"\"{mod.Name}\" will replace files that are also used by other mods:\n\n" +
                                $"{conflictText}\n\n" +
                                $"The new mod takes priority. Would you like to partially disable the conflicting mods?\n\n" +
                                $"• YES = Disable only the overlapping files in conflicting mods\n" +
                                $"• NO = Leave as-is (new mod's files will overwrite)",
                                "File Conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                            if (conflictResult == MessageBoxResult.Yes)
                            {
                                foreach (var conflictMod in conflictMods)
                                {
                                    var overlappingFiles = conflicts
                                        .Where(kv => kv.Value.Contains(conflictMod))
                                        .Select(kv => kv.Key).ToList();
                                    _modManager.PartialDisableMod(conflictMod, overlappingFiles, false);
                                }
                                DebugLogger.Log($"Partially disabled {conflictMods.Count} conflicting mod(s)");
                                RefreshInstalledMods();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Failed conflict check before enabling", ex);
                    }
                }

                _modManager.ToggleMod(mod);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Toggle failed: {mod.Name}", ex);
                MessageBox.Show($"Error toggling mod:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RefreshInstalledMods();
        }

        private async void BtnUninstallMod_Click(object sender, RoutedEventArgs e)
        {
            var mod = (InstalledMod)((Button)sender).Tag;

            // Check if this mod is installed in multiple builds
            var allInstances = _modManager.InstalledMods
                .Where(m => m.GameBananaId == mod.GameBananaId && m.GameBananaId > 0)
                .ToList();

            bool removeFromAll = false;

            if (allInstances.Count > 1)
            {
                // Mod is in multiple builds - ask which ones to remove
                var builds = string.Join(", ", allInstances.Select(m => 
                {
                    var entry = _modManager.Versions.FirstOrDefault(v => v.VersionName == m.TargetVersion);
                    return entry?.DisplayName ?? m.TargetVersion;
                }));

                var choice = MessageBox.Show(
                    $"\"{mod.Name}\" is installed in {allInstances.Count} builds:\n{builds}\n\n" +
                    $"Do you want to remove it from ALL builds?\n\n" +
                    $"• YES = Remove from all builds\n" +
                    $"• NO = Remove from this build only\n" +
                    $"• CANCEL = Don't remove",
                    "Remove from Multiple Builds?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel)
                    return;

                removeFromAll = (choice == MessageBoxResult.Yes);
            }
            else
            {
                // Single build - standard confirmation
                var result = MessageBox.Show(
                    $"Are you sure you want to remove \"{mod.Name}\"?\nOriginal game files will be restored.",
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                if (removeFromAll)
                {
                    // Remove all instances
                    foreach (var instance in allInstances.ToList())
                    {
                        await _modManager.UninstallModAsync(instance);
                    }
                }
                else
                {
                    // Remove only this instance
                    await _modManager.UninstallModAsync(mod);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Uninstall failed: {mod.Name}", ex);
                MessageBox.Show($"Error removing mod:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RefreshInstalledMods();
        }

        private void BtnInstalledModSettings_Click(object sender, RoutedEventArgs e)
        {
            var mod = (InstalledMod)((Button)sender).Tag;
            var dialog = new InstalledModSettingsDialog(_modManager, mod);
            dialog.Owner = this;
            dialog.ShowDialog();

            if (dialog.Changed)
            {
                RefreshInstalledMods();

                if (dialog.ReinstallRequested && mod.GameBananaId > 0)
                {
                    // Open browse page and trigger search for this mod
                    TxtSearch.Text = mod.Name;
                    _currentSearch = mod.Name;
                    _searchMode = "text";
                    _currentPage = 1;
                    UpdateClearButton();
                    SetActivePage("browse");
                    _ = LoadBrowseModsAsync();
                }
            }
        }

        // ── Settings - Version Management ───────────────────────

        private void RefreshVersionsList()
        {
            VersionsList.ItemsSource = null;
            VersionsList.ItemsSource = _modManager.Versions;
            TxtNoVersions.Visibility = _modManager.Versions.Count == 0 
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAddVersion_Click(object sender, RoutedEventArgs e)
        {
            // Step 1: Pick folder
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the SSF2 installation folder for this version",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var folderPath = folderDialog.SelectedPath;

            // Step 2: Pick version from known list
            var picker = new ListPickerDialog("SSF2 Version",
                $"Which SSF2 version is installed at:\n{folderPath}",
                ModManagerService.KnownVersions);
            picker.Owner = this;

            if (picker.ShowDialog() != true || picker.SelectedItem == null)
                return;

            var versionName = picker.SelectedItem;

            // Handle custom version name
            if (versionName == "Custom...")
            {
                var inputDialog = new InputDialog("Custom Version Name",
                    "Enter a name for this version:");
                inputDialog.Owner = this;
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.InputText))
                    return;
                versionName = inputDialog.InputText.Trim();
            }

            // Check if same folder path already exists
            if (_modManager.Versions.Any(v => v.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A build with this folder path already exists.",
                    "Duplicate Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Always require a nickname
            string nickname;
            while (true)
            {
                var nicknameDialog = new InputDialog("Build Nickname",
                    $"Enter a nickname for this build (required):\n\nVersion: {versionName}\nPath: {folderPath}");
                nicknameDialog.Owner = this;
                if (nicknameDialog.ShowDialog() != true)
                    return;

                nickname = nicknameDialog.InputText?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(nickname))
                {
                    MessageBox.Show("Nickname is required for all builds.",
                        "Nickname Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                // Check for duplicate nickname
                if (_modManager.Versions.Any(v => v.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"A build with nickname \"{nickname}\" already exists. Please choose a different nickname.",
                        "Duplicate Nickname", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                break;
            }

            _modManager.AddVersion(versionName, folderPath, nickname);

            // Check if there are existing mods for this version (from a previous build)
            var existingMods = _modManager.GetModsForVersion(versionName);
            if (existingMods.Count > 0)
            {
                MessageBox.Show(
                    $"Found {existingMods.Count} previously installed mod{(existingMods.Count != 1 ? "s" : "")} for \"{versionName}\".\n\n" +
                    $"These mods are still tracked and can be managed from the Installed Mods page.",
                    "Mods Restored", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            RefreshVersionsList();
            RefreshBuildsList();
            RefreshPlayButton();
            DebugLogger.Log($"Version added via UI: {versionName} at {folderPath}");
        }

        private void BtnRemoveVersion_Click(object sender, RoutedEventArgs e)
        {
            var version = (SSF2VersionEntry)((Button)sender).Tag;
            var result = MessageBox.Show(
                $"Remove version \"{version.DisplayName}\"?\nInstalled mods won't be affected.",
                "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _modManager.RemoveVersion(version);
                RefreshVersionsList();
                RefreshBuildsList();
                RefreshPlayButton();
            }
        }

        private void BtnOpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            var modsDir = _modManager.ModsBaseDir;
            if (Directory.Exists(modsDir))
            {
                Process.Start(new ProcessStartInfo(modsDir) { UseShellExecute = true });
            }
        }

        private void BtnOpenSSF2Folder_Click(object sender, RoutedEventArgs e)
        {
            var path = _modManager.GetActiveVersionPath();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("No active version set, or folder not found.", "No Path",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── Installed Builds ────────────────────────────────────

        private void RefreshBuildsList()
        {
            var versions = _modManager.Versions;
            var viewModels = versions.Select(v =>
            {
                var mods = _modManager.GetModsForVersion(v.VersionName);
                var categories = mods.GroupBy(m => m.Category)
                    .Select(g => $"  • {g.Key}: {g.Count()}")
                    .ToList();

                return new BuildViewModel
                {
                    Entry = v,
                    ModCount = mods.Count,
                    CategoryBreakdown = categories,
                    ExePath = _modManager.FindSSF2Executable(v)
                };
            }).ToList();

            BuildsList.ItemsSource = viewModels;
            TxtBuildCount.Text = $"{versions.Count} build{(versions.Count != 1 ? "s" : "")} configured";
            TxtNoBuilds.Visibility = versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnPlayBuild_Click(object sender, RoutedEventArgs e)
        {
            var build = (BuildViewModel)((Button)sender).Tag;
            LaunchVersion(build.Entry.Nickname);
        }

        private void BtnBuildSettings_Click(object sender, RoutedEventArgs e)
        {
            var build = (BuildViewModel)((Button)sender).Tag;
            var dialog = new BuildSettingsDialog(_modManager, build.Entry);
            dialog.Owner = this;
            dialog.ShowDialog();

            if (dialog.Changed)
            {
                RefreshBuildsList();
                RefreshVersionsList();
                RefreshPlayButton();
            }
        }

        // ── Install Anyway (Game files) ─────────────────────────

        private void BtnInstallAnyway_Click(object sender, RoutedEventArgs e)
        {
            var mod = (InstalledMod)((Button)sender).Tag;
            var files = _modManager.GetModFileStructure(mod);

            var hasData = files.Any(f => f.StartsWith("data", StringComparison.OrdinalIgnoreCase) ||
                                         f.Contains(@"\data\", StringComparison.OrdinalIgnoreCase));
            var hasSwf = files.Any(f => f.EndsWith("ssf2.swf", StringComparison.OrdinalIgnoreCase));

            var preview = string.Join("\n", files.Take(30));
            if (files.Count > 30) preview += $"\n... and {files.Count - 30} more files";

            var info = "This will deploy files from this mod into your SSF2 build folder:\n\n";
            if (hasData) info += "• data/ folder contents → build's data/ folder\n";
            if (hasSwf) info += "• ssf2.swf → replaces build's ssf2.swf\n";
            info += "\nOriginal files will be backed up.\n\nFile structure:\n" + preview;

            var result = MessageBox.Show(info, $"Install Anyway: {mod.Name}",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _modManager.DeployGameFilesMod(mod);
                    MessageBox.Show("Files deployed successfully!", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    DebugLogger.Error($"Deploy game files failed: {mod.Name}", ex);
                    MessageBox.Show($"Error deploying files:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                RefreshInstalledMods();
            }
        }

        // ── Installed Mods Filters ──────────────────────────────

        private void PopulateInstalledFilters()
        {
            var mods = _modManager.InstalledMods;

            // Populate BuildDisplayName for each mod
            foreach (var mod in mods)
            {
                var matchingBuilds = _modManager.Versions
                    .Where(v => v.VersionName == mod.TargetVersion || v.Nickname == mod.TargetVersion)
                    .ToList();
                mod.BuildDisplayName = matchingBuilds.Count > 0
                    ? string.Join(", ", matchingBuilds.Select(b => b.DisplayName))
                    : mod.TargetVersion;
            }

            // Build filter
            var builds = new List<string> { "All Builds" };
            builds.AddRange(_modManager.Versions.Select(v => v.DisplayName).OrderBy(n => n));
            var prevBuild = CmbFilterBuild.SelectedItem as ComboBoxItem;
            CmbFilterBuild.Items.Clear();
            foreach (var b in builds)
                CmbFilterBuild.Items.Add(new ComboBoxItem { Content = b });
            CmbFilterBuild.SelectedIndex = 0;

            // Version filter removed — handled via Build filter

            // Category filter
            var categories = new List<string> { "All Types" };
            categories.AddRange(mods.Select(m => m.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c));
            CmbFilterCategory.Items.Clear();
            foreach (var c in categories)
                CmbFilterCategory.Items.Add(new ComboBoxItem { Content = c });
            CmbFilterCategory.SelectedIndex = 0;

            // Creator filter
            var creators = new List<string> { "All Creators" };
            creators.AddRange(mods.Select(m => m.Author).Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a));
            CmbFilterCreator.Items.Clear();
            foreach (var a in creators)
                CmbFilterCreator.Items.Add(new ComboBoxItem { Content = a });
            CmbFilterCreator.SelectedIndex = 0;
        }

        private void CmbFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyInstalledFilters();
        }

        private void ApplyInstalledFilters()
        {
            var mods = _modManager.InstalledMods.AsEnumerable();

            var buildFilter = (CmbFilterBuild.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(buildFilter) && buildFilter != "All Builds")
            {
                var entry = _modManager.Versions.FirstOrDefault(v => v.DisplayName == buildFilter);
                if (entry != null)
                    mods = mods.Where(m => m.TargetVersion == entry.VersionName || m.TargetVersion == entry.Nickname);
            }

            // Version filter removed — no per-version filtering

            var categoryFilter = (CmbFilterCategory.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(categoryFilter) && categoryFilter != "All Types")
                mods = mods.Where(m => m.Category == categoryFilter);

            var creatorFilter = (CmbFilterCreator.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(creatorFilter) && creatorFilter != "All Creators")
                mods = mods.Where(m => m.Author == creatorFilter);

            var filtered = mods.ToList();
            InstalledModsList.ItemsSource = null;
            InstalledModsList.ItemsSource = filtered;
            TxtModCount.Text = $"{filtered.Count} of {_modManager.InstalledMods.Count} mod{(_modManager.InstalledMods.Count != 1 ? "s" : "")}";
            TxtNoMods.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Browse: Search by Creator/Category ──────────────────

        private async void BtnSearchByCreator_Click(object sender, RoutedEventArgs e)
        {
            var hyperlink = (System.Windows.Documents.Hyperlink)sender;
            var vm = (BrowseModViewModel)hyperlink.Tag;
            TxtSearch.Text = $"Author: {vm.AuthorName}";
            _currentSearch = vm.AuthorName;
            _searchMode = "author";
            _currentPage = 1;
            UpdateClearButton();
            SetActivePage("browse");
            await LoadBrowseModsAsync();
        }

        private async void BtnSearchByCategory_Click(object sender, RoutedEventArgs e)
        {
            var hyperlink = (System.Windows.Documents.Hyperlink)sender;
            var vm = (BrowseModViewModel)hyperlink.Tag;
            TxtSearch.Text = $"Category: {vm.CategoryName}";
            _currentSearch = vm.CategoryName;
            _searchMode = "category";
            _currentPage = 1;
            UpdateClearButton();
            SetActivePage("browse");
            await LoadBrowseModsAsync();
        }

        // ── Play Button ─────────────────────────────────────────

        private void RefreshPlayButton()
        {
            var activeVersion = _modManager.ActiveVersion;
            if (string.IsNullOrEmpty(activeVersion) || _modManager.Versions.Count == 0)
            {
                PlayButtonPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var activeEntry = _modManager.GetVersionEntry(activeVersion);
            var activeDisplay = activeEntry?.DisplayName ?? activeVersion;
            PlayButtonPanel.Visibility = Visibility.Visible;
            BtnPlay.Content = $"▶  Play {activeDisplay}";

            // Populate dropdown menu
            PlayVersionMenu.Items.Clear();
            foreach (var version in _modManager.Versions)
            {
                var item = new MenuItem
                {
                    Header = version.DisplayName,
                    Tag = version.Nickname,
                    FontWeight = version.Nickname == activeVersion 
                        ? FontWeights.Bold : FontWeights.Normal
                };
                item.Click += PlayVersionMenuItem_Click;
                PlayVersionMenu.Items.Add(item);
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            var activeVersion = _modManager.ActiveVersion;
            if (!string.IsNullOrEmpty(activeVersion))
            {
                LaunchVersion(activeVersion);
            }
        }

        private void BtnPlayDropdown_Click(object sender, RoutedEventArgs e)
        {
            RefreshPlayButton();
            PlayVersionMenu.IsOpen = true;
        }

        private void PlayVersionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            var nickname = (string)menuItem.Tag;
            _modManager.ActiveVersion = nickname;
            RefreshPlayButton();
        }

        private void LaunchVersion(string nickname)
        {
            var entry = _modManager.GetVersionEntry(nickname);
            if (entry == null)
            {
                MessageBox.Show($"Could not find build \"{nickname}\".",
                    "Build Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var exePath = _modManager.FindSSF2Executable(entry);
            if (exePath == null)
            {
                MessageBox.Show(
                    $"Could not find an executable in the \"{entry.DisplayName}\" folder.\n\n" +
                    $"Path: {entry.FolderPath}",
                    "Executable Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DebugLogger.Log($"Launching: {exePath}");
                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Failed to launch {nickname}", ex);
                MessageBox.Show($"Failed to launch game:\n{ex.Message}", "Launch Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Debug Log ───────────────────────────────────────────

        private void RefreshDebugLog()
        {
            TxtDebugLog.Text = DebugLogger.GetFullLog();
            LogScrollViewer.ScrollToEnd();
        }

        private void BtnRefreshLog_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugLog();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            DebugLogger.Clear();
            RefreshDebugLog();
        }

        private static string FormatFileSize(long size)
        {
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            return $"{size / (1024.0 * 1024.0):F1} MB";
        }
    }

}
