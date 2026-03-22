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
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

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
        private readonly GameBananaApiClient _apiClient;
        private readonly ModManagerService _modManager;
        private int _currentPage = 1;
        private string _currentSearch = "";
        private string _currentSort = "default";
        private string _searchMode = "text"; // "text", "author", "category"

        public MainWindow()
        {
            InitializeComponent();

            _apiClient = new GameBananaApiClient();
            _modManager = new ModManagerService(_apiClient);

            RefreshVersionsList();
            RefreshPlayButton();
            DebugLogger.Log("Application started");

            Loaded += async (s, e) => await LoadBrowseModsAsync();
        }

        // Called by App.xaml.cs for protocol handler
        public async void InstallModFromProtocol(string archiveUrl, string modType, string modId)
        {
            try
            {
                MessageBox.Show($"Received 1-Click Mod Install:\nURL: {archiveUrl}\nType: {modType}\nID: {modId}", "1-Click Install", MessageBoxButton.OK, MessageBoxImage.Information);
                // TODO: Download and install mod using archiveUrl/modId
                // You can call your existing download/install logic here
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to install mod from 1-click URL:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            PageBrowse.Visibility = page == "browse" ? Visibility.Visible : Visibility.Collapsed;
            PageInstalled.Visibility = page == "installed" ? Visibility.Visible : Visibility.Collapsed;
            PageBuilds.Visibility = page == "builds" ? Visibility.Visible : Visibility.Collapsed;
            PageCostumes.Visibility = page == "costumes" ? Visibility.Visible : Visibility.Collapsed;
            PageEvents.Visibility = page == "events" ? Visibility.Visible : Visibility.Collapsed;
            PageModSSF2.Visibility = page == "modssf2" ? Visibility.Visible : Visibility.Collapsed;
            PageResources.Visibility = page == "resources" ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = page == "log" ? Visibility.Visible : Visibility.Collapsed;

            BtnBrowse.Style = (Style)FindResource(page == "browse" ? "SidebarButtonActive" : "SidebarButton");
            BtnInstalled.Style = (Style)FindResource(page == "installed" ? "SidebarButtonActive" : "SidebarButton");
            BtnBuilds.Style = (Style)FindResource(page == "builds" ? "SidebarButtonActive" : "SidebarButton");
            BtnCostumes.Style = (Style)FindResource(page == "costumes" ? "SidebarButtonActive" : "SidebarButton");
            BtnEvents.Style = (Style)FindResource(page == "events" ? "SidebarButtonActive" : "SidebarButton");
            BtnModSSF2.Style = (Style)FindResource(page == "modssf2" ? "SidebarButtonActive" : "SidebarButton");
            BtnResources.Style = (Style)FindResource(page == "resources" ? "SidebarButtonActive" : "SidebarButton");
            BtnSettings.Style = (Style)FindResource(page == "settings" ? "SidebarButtonActive" : "SidebarButton");
            BtnLog.Style = (Style)FindResource(page == "log" ? "SidebarButtonActive" : "SidebarButton");
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e) => SetActivePage("browse");

        private void BtnInstalled_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("installed");
            RefreshInstalledMods();
        }

        private void BtnCostumes_Click(object sender, RoutedEventArgs e) => SetActivePage("costumes");
        private void BtnEvents_Click(object sender, RoutedEventArgs e) => SetActivePage("events");
        private void BtnModSSF2_Click(object sender, RoutedEventArgs e) => SetActivePage("modssf2");
        private void BtnResources_Click(object sender, RoutedEventArgs e) => SetActivePage("resources");

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
                var fileList = string.Join("\n", mod.Files.Select((f, i) => $"{i + 1}. {f.FileName} ({f.FileSizeFormatted})"));
                var result = MessageBox.Show(
                    $"This mod has multiple files:\n\n{fileList}\n\nInstall the first file?",
                    "Select File", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
                selectedFile = mod.Files[0];
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

            // Check for existing mod with same GameBanana ID
            var existingMod = _modManager.InstalledMods.FirstOrDefault(m => m.GameBananaId == mod.Id);
            if (existingMod != null)
            {
                var overrideResult = MessageBox.Show(
                    $"\"{mod.Name}\" is already installed.\n\n" +
                    $"Version: {existingMod.TargetVersion}\n" +
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
                    var items = archiveContents.Select(a => new FileSelectionItem
                    {
                        Path = a.Path,
                        DisplaySize = FormatFileSize(a.Size),
                        IsSelected = true
                    });
                    var dialog = new FileSelectionDialog(mod.Name,
                        $"Select which files to install from \"{selectedFile.FileName}\":", items);
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

                // Check file-level conflicts with other enabled mods
                if (!installed.IsGameFiles && installed.BackedUpFiles.Count > 0)
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
                                _modManager.PartialDisableMod(conflictMod, overlappingFiles);
                            }
                            DebugLogger.Log($"Partially disabled {conflictMods.Count} conflicting mod(s)");
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
            var result = MessageBox.Show(
                $"Are you sure you want to remove \"{mod.Name}\"?\nOriginal game files will be restored.",
                "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _modManager.UninstallModAsync(mod);
                }
                catch (Exception ex)
                {
                    DebugLogger.Error($"Uninstall failed: {mod.Name}", ex);
                    MessageBox.Show($"Error removing mod:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                RefreshInstalledMods();
            }
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

            // Version filter
            var versions = new List<string> { "All Versions" };
            versions.AddRange(mods.Select(m => m.TargetVersion).Where(v => !string.IsNullOrEmpty(v)).Distinct().OrderBy(v => v));
            var prevVersion = CmbFilterVersion.SelectedItem as ComboBoxItem;
            CmbFilterVersion.Items.Clear();
            foreach (var v in versions)
                CmbFilterVersion.Items.Add(new ComboBoxItem { Content = v });
            CmbFilterVersion.SelectedIndex = 0;

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

            var versionFilter = (CmbFilterVersion.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(versionFilter) && versionFilter != "All Versions")
                mods = mods.Where(m => m.TargetVersion == versionFilter);

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
