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
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using System.Net.Http;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using SSF2ModManager.Views;
using Newtonsoft.Json.Linq;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;

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
        private string _activePageName = "home";
        private string _currentSearch = "";
        private string _currentSort = "default";
        private string _searchMode = "text"; // "text", "author", "category"

        private readonly MappingsEditService _mappingsEdit = new();
        private readonly MenuSlotService _menuSlots = new();
        private readonly CharacterAssetService _charAssets = new();
        private readonly CharacterPackageService _charPackages = new();
        private readonly RosterWizardService _rosterWizard = new();
        private CrazyCssService? _crazyCssField;
        private CrazyCssService CrazyCss =>
            _crazyCssField ??= new CrazyCssService(_menuSlots, _mappingsEdit);
        private readonly MusicEditService _musicEdit = new();
        private readonly MusicLibraryCacheService _musicCacheSvc = new();
        private readonly ShellCharacterCloneService _shellClone = new();
        private readonly AppMusicPlayerService _appPlayer = new();
        private readonly SystemMediaControlsService _systemMedia = new();
        private HwndSource? _hwndSource;
        private MusicLibraryCache? _musicCache;
        private CancellationTokenSource? _musicCacheCts;
        private int _musicCacheGen;
        private bool _musicCacheRunning;
        private readonly List<CachedSongInfo> _playlistEditorTracks = new();
        private bool _miniSeekDragging;
        private DateTime _lastMiniSeekUtc = DateTime.MinValue;
        private DateTime _suppressMiniProgressUntilUtc = DateTime.MinValue;
        private bool _miniPlayerLoading;
        private bool _playMusicFiltersReady;
        private enum PlayMusicFilterKind { None, Search, Artist, Stage }
        private PlayMusicFilterKind _playMusicFilterKind = PlayMusicFilterKind.None;
        private List<CachedSongInfo> _filteredLibrarySongs = new();
        private JObject? _mappingsDoc;
        private string? _mappingsLoadedRoot;
        private MenuSymbolIndex? _menuSymbolIndex;
        private List<List<string>> _arrangeRows = new();
        private (int Row, int Col)? _selectedArrangeChip;
        private System.Windows.Point? _arrangeDragStart;
        private (int Row, int Col)? _arrangeDragFrom;
        private bool _arrangeDidDrag;
        private const string ArrangeChipDataFormat = "SSF2MM.ArrangeChip";
        private string _mapHubSection = "hub"; // hub | category | charAdd | stageAdd | musicAdd | arrange | playlists | playMusic
        private string _mapCategory = ""; // characters | stages | music
        private bool _arrangeIsCss = true;
        private List<StagePlaylistInfo> _stagePlaylists = new();
        private readonly MediaPlayer _musicPreviewPlayer = new();
        private DispatcherTimer? _musicPreviewTimer;
        private bool _musicPreviewLoopMode;
        private TimeSpan _musicPreviewLoopPoint;
        private TimeSpan _musicPreviewStartPoint;
        private TimeSpan _musicPreviewWindowEnd;
        private TimeSpan? _musicSourceDuration;
        private bool _syncingMusicMarkers;

        // Character wizard (Clone / New / Edit)
        private int _mapCharStep = 1;
        private CharacterWizardMode _mapCharMode = CharacterWizardMode.Clone;
        private string _mapCharActiveSlot = "select";
        private readonly Dictionary<string, string?> _mapCharOverlayPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _mapCharPlaceholderPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _mapCharExportPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SlotExportInfo> _mapCharSlotInfos = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ArtPlacementTransform> _mapCharTransforms = new(StringComparer.OrdinalIgnoreCase);
        private string? _mapCharWorkDir;
        private bool _mapCharSyncingArtFields;
        private bool _mapCharCanvasHooked;

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

            _appPlayer.TrackChanged += (_, _) => Dispatcher.Invoke(UpdateMiniPlayerUi);
            _appPlayer.PlaybackStateChanged += (_, _) => Dispatcher.Invoke(UpdateMiniPlayerUi);
            _appPlayer.PositionChanged += (_, _) => Dispatcher.Invoke(UpdateMiniPlayerProgress);
            _appPlayer.VisualizerUpdated += (_, _) => Dispatcher.Invoke(UpdateMiniPlayerVisualizer);
            _appPlayer.EnsurePlayableAsync = async song =>
            {
                try
                {
                    var path = await _musicCacheSvc.EnsurePreviewMp3Async(song);
                    if (path == null) return false;
                    if (_musicCache != null) _musicCacheSvc.Save(_musicCache);
                    return true;
                }
                catch { return false; }
            };
            UpdateMiniPlayerUi();

            SourceInitialized += (_, _) => AttachSystemMediaControls();
            Closed += (_, _) =>
            {
                try { _hwndSource?.RemoveHook(WndProc); } catch { /* ignore */ }
                _hwndSource = null;
                _systemMedia.Dispose();
                _appPlayer.Dispose();
                try { _themeManager?.Dispose(); } catch { /* ignore */ }
                _themeManager = null;
            };

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
            this.Title = AppInfo.ProductName;
            if (FindName("BtnHome") is Button btnHome)
                btnHome.Content = "🏠  Home";
            BtnBrowse.Content = "🌐  " + Localization.Get("BrowseMods");
            BtnInstalled.Content = "📦  " + Localization.Get("InstalledMods");
            BtnBuilds.Content = "🎮  " + Localization.Get("InstalledBuilds");
            BtnCloneCharacter.Content = "Clone character";
            BtnGettingStarted.Content = "📘  " + "Getting Started";
            BtnSettings.Content = "⚙️  " + Localization.Get("Settings");
            BtnLog.Content = "📋  " + Localization.Get("DebugLog");

            // Page headers
            SetTextBlock("PageBrowse", 0, Localization.Get("BrowseMods"));
            SetTextBlock("PageInstalled", 0, Localization.Get("InstalledMods"));
            SetTextBlock("PageBuilds", 0, Localization.Get("InstalledBuilds"));
            if (FindName("TxtCloneCharacterTitle") is TextBlock txtCloneTitle)
                txtCloneTitle.Text = Localization.Get("CloneCharacter");
            if (FindName("TxtPressorTitle") is TextBlock txtPressorTitle)
                txtPressorTitle.Text = Localization.Get("Pressor");
            if (FindName("TxtMappingsTitle") is TextBlock txtMappingsTitle)
                txtMappingsTitle.Text = Localization.Get("Mappings");
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
            try { ShellWorkspaceService.EnsureRegistered(_modManager); } catch { }

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
            ApplyLegacySidebarVisibility();
            RefreshHomePage();
            SetActivePage("home");
            DebugLogger.Log("Application started");

            try
            {
                PageNews.BindSettings(_settingsService!);
                PageNews.ArticlesChanged += UpdateNewsBadge;
            }
            catch { }

            Loaded += async (s, e) =>
            {
                try
                {
                    DevFileLog.Write( $"[MainWindow] Loaded handler start\n");
                    if (FindName("ChkLegacyModManager") is CheckBox chkLegacy && _settingsService != null)
                        chkLegacy.IsChecked = _settingsService.GetLegacyModManager();
                    ApplyLegacySidebarVisibility();
                    RefreshHomePage();
                    BindShellInstallRoot();
                    _ = WarmMappingsAndMusicOnStartupAsync();
                    await LoadBrowseModsAsync();
                    await SyncNewsOnStartupAsync();
                    DevFileLog.Write( $"[MainWindow] Loaded handler complete\n");
                }
                catch (Exception ex)
                {
                    try { DevFileLog.Write( $"[MainWindow] Loaded EX: {ex}\n"); } catch { }
                }
            };
            // Check for updates on startup; only notify if a newer version exists
            _ = CheckProgramVersionAsync(silent: true);
            DevFileLog.Write( $"[MainWindow] ctor: End\n");
        }

        private async void BtnCheckModUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn || btn.Tag is not InstalledMod inst)
                    return;

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

                ShowOverlay("Checking for updates...");
                try
                {
                    var files = await _apiClient.GetModFilesAsync(inst.GameBananaId);
                    var latest = files?.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                    if (latest == null)
                    {
                        MessageBox.Show("No files found for this mod on GameBanana.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    DateTime latestDate;
                    try { latestDate = DateTimeOffset.FromUnixTimeSeconds(latest.DateAdded).LocalDateTime; }
                    catch { latestDate = DateTime.Now; }

                    if (inst.GameBananaFileId > 0 && latest.Id == inst.GameBananaFileId)
                    {
                        MessageBox.Show("No updates found. This mod is up-to-date.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (inst.GameBananaFileId <= 0 && latestDate <= inst.InstalledDate)
                    {
                        MessageBox.Show("No updates found. This mod is up-to-date.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var res = MessageBox.Show(
                        $"Update available: {latest.FileName} (added {latestDate:g}).\nDo you want to download and install it now?",
                        "Update Available", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (res == MessageBoxResult.Yes)
                        await UpdateInstalledModWithUiAsync(inst);
                    else if (res == MessageBoxResult.No)
                    {
                        var ignore = MessageBox.Show("Ignore future update checks for this mod?", "Ignore Updates", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (ignore == MessageBoxResult.Yes)
                        {
                            inst.IgnoreUpdates = true;
                            try { _modManager.SaveDatabase(); } catch { }
                            MessageBox.Show("This mod will be ignored for future update checks.", "Ignore Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                            RefreshInstalledMods();
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { DevFileLog.Write($"[CheckModUpdates] EX: {ex}\n"); } catch { }
                    MessageBox.Show($"Failed to check updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    HideOverlay();
                }
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write($"[BtnCheckModUpdates_Click] EX: {ex}\n"); } catch { }
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
                            var newer = inst.GameBananaFileId > 0
                                ? latest.Id != inst.GameBananaFileId
                                : latestDate > inst.InstalledDate;
                            if (newer)
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

                HideOverlay();

                foreach (var u in updates)
                    await UpdateInstalledModWithUiAsync(u.inst);

                MessageBox.Show("Bulk update process completed.", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                try { DevFileLog.Write($"[BtnCheckAllInstalledUpdates_Click] EX: {ex}\n"); } catch { }
                HideOverlay();
                MessageBox.Show($"Failed to check/install updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task<InstalledMod?> UpdateInstalledModWithUiAsync(InstalledMod inst)
        {
            DownloadOverlay.Visibility = Visibility.Visible;
            TxtDownloadStatus.Text = $"Updating {inst.Name}...";
            DownloadProgress.Value = 0;
            TxtDownloadPercent.Text = "0%";

            var progress = new Progress<double>(p =>
            {
                DownloadProgress.Value = p;
                TxtDownloadPercent.Text = $"{p:F0}%";
            });

            try
            {
                TxtDownloadStatus.Text = $"Downloading update for {inst.Name}...";
                var updated = await _modManager.UpdateInstalledModAsync(inst, progress);
                if (updated == null)
                {
                    MessageBox.Show($"Failed to update \"{inst.Name}\".", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                TxtDownloadStatus.Text = "Complete";
                TxtDownloadPercent.Text = "100%";
                RefreshInstalledMods();
                return updated;
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Update failed: {inst.Name}", ex);
                try { DevFileLog.Write($"[UpdateInstalled] EX: {ex}\n"); } catch { }
                MessageBox.Show($"Failed to update \"{inst.Name}\":\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            finally
            {
                DownloadOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task<bool> TryToggleModAsync(InstalledMod mod)
        {
            try
            {
                _modManager.ToggleMod(mod);
                return true;
            }
            catch (ModFolderNotFoundException ex)
            {
                return await HandleModFolderMissingAsync(ex.Mod, ex.ModPath);
            }
        }

        private async System.Threading.Tasks.Task<bool> HandleModFolderMissingAsync(InstalledMod mod, string modPath)
        {
            var message =
                $"Cannot enable \"{mod.Name}\" — mod files are missing.\n\nExpected folder:\n{modPath}";

            if (mod.GameBananaId > 0)
            {
                var result = MessageBox.Show(
                    message + "\n\nWould you like to re-download this mod from GameBanana?",
                    "Mod Files Missing", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return false;

                var updated = await UpdateInstalledModWithUiAsync(mod);
                if (updated == null)
                    return false;

                try
                {
                    _modManager.ToggleMod(updated);
                    return updated.Enabled;
                }
                catch (ModFolderNotFoundException ex2)
                {
                    MessageBox.Show(
                        $"Download finished but mod files are still missing:\n{ex2.ModPath}",
                        "Mod Files Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            MessageBox.Show(
                message + "\n\nRe-install the mod manually or remove it from your library.",
                "Mod Files Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private async System.Threading.Tasks.Task SyncNewsOnStartupAsync()
        {
            if (_settingsService == null) return;
            try
            {
                await PageNews.RefreshAsync(forceSync: false);
                UpdateNewsBadge();
                await PromptUnreadReleaseNewsAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Startup news sync failed", ex);
            }
        }

        private void UpdateNewsBadge()
        {
            if (_settingsService == null) return;
            try
            {
                var articles = NewsService.LoadMergedArticles();
                var unread = NewsService.CountUnread(articles, _settingsService);
                BtnNews.Content = unread > 0 ? $"📰  News ({unread})" : "📰  News";
            }
            catch { }
        }

        private async System.Threading.Tasks.Task PromptUnreadReleaseNewsAsync()
        {
            if (_settingsService == null) return;
            if (string.Equals(_settingsService.GetLastNewsPromptVersion(), AppInfo.Version, StringComparison.Ordinal))
                return;

            var articles = NewsService.LoadMergedArticles();
            var unreadRelease = NewsService.CountUnread(articles, _settingsService, releaseTaggedOnly: true);
            if (unreadRelease <= 0) return;

            var result = MessageBox.Show(
                $"There {(unreadRelease == 1 ? "is 1 unread release note" : $"are {unreadRelease} unread release notes")}.\n\nOpen News now?",
                "News", MessageBoxButton.YesNo, MessageBoxImage.Information);

            _settingsService.SetLastNewsPromptVersion(AppInfo.Version);
            if (result == MessageBoxResult.Yes)
                await OpenNewsAsync();
        }

        public async System.Threading.Tasks.Task OpenNewsAsync(string? newsPath = null)
        {
            SetActivePage("news");
            try
            {
                if (!string.IsNullOrWhiteSpace(newsPath))
                    PageNews.LoadLocal(newsPath);
                else
                    await PageNews.RefreshAsync(forceSync: false);
                UpdateNewsBadge();
            }
            catch (Exception ex)
            {
                DevFileLog.Write($"[OpenNews] Failed: {ex}\n");
                MessageBox.Show($"Failed to open news:\n{ex.Message}", "News",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowOverlay(string message, string? title = null, double? progressPercent = null, string? step = null)
        {
            try
            {
                var wasHidden = FindName("OverlayLoading") is Grid og0 && og0.Visibility != Visibility.Visible;
                if (FindName("TxtOverlayTitle") is TextBlock titleTb)
                {
                    if (!string.IsNullOrWhiteSpace(title))
                        titleTb.Text = title!;
                    else if (wasHidden)
                        titleTb.Text = "Working…";
                }
                if (FindName("TxtOverlayMessage") is TextBlock tb) tb.Text = message ?? "";
                if (FindName("TxtOverlayStep") is TextBlock stepTb)
                {
                    if (step != null)
                        stepTb.Text = step;
                    else if (wasHidden)
                        stepTb.Text = "";
                }
                if (FindName("OverlayProgress") is ProgressBar pb)
                {
                    if (progressPercent is { } p)
                    {
                        pb.IsIndeterminate = false;
                        pb.Value = Math.Clamp(p, 0, 100);
                    }
                    else if (wasHidden)
                    {
                        pb.IsIndeterminate = true;
                    }
                }
                if (FindName("OverlayLoading") is Grid g) { g.Visibility = Visibility.Visible; g.IsHitTestVisible = true; }
                try
                {
                    if (FindName("OverlaySpinner") is Ellipse el && el.RenderTransform is RotateTransform rt)
                    {
                        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
                        {
                            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                        };
                        rt.BeginAnimation(RotateTransform.AngleProperty, anim);
                    }
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }

        private void UpdateOverlayProgress(string message, double percent, string? step = null)
        {
            ShowOverlay(message, title: null, progressPercent: percent, step: step);
        }

        private void HideOverlay()
        {
            try
            {
                if (FindName("OverlayLoading") is Grid g) { g.Visibility = Visibility.Collapsed; g.IsHitTestVisible = false; }
                try
                {
                    if (FindName("OverlaySpinner") is Ellipse el && el.RenderTransform is RotateTransform rt)
                        rt.BeginAnimation(RotateTransform.AngleProperty, null);
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }

        private async System.Threading.Tasks.Task RunBusyWorkAsync(
            string title,
            Func<IProgress<(string Message, double Percent, string? Step)>, System.Threading.Tasks.Task> work)
        {
            ShowOverlay("Starting…", title, 0, null);
            var progress = new Progress<(string Message, double Percent, string? Step)>(p =>
                UpdateOverlayProgress(p.Message, p.Percent, p.Step));
            try
            {
                await work(progress);
                // Progress<T> posts to the UI thread asynchronously; flush so the last Report
                // cannot re-show the overlay after HideOverlay in finally.
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            finally
            {
                HideOverlay();
            }
        }

        private async System.Threading.Tasks.Task CheckProgramVersionAsync(bool silent = false)
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var remote = await http.GetStringAsync(AppInfo.VersionCheckUrl);
                if (string.IsNullOrWhiteSpace(remote))
                {
                    if (!silent)
                        MessageBox.Show("Could not read remote version information.", "Version Check", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var remoteVer = remote.Trim().Split('\n')[0].Trim();
                var localVer = AppInfo.Version;

                if (AppInfo.IsUpToDate(localVer, remoteVer))
                {
                    if (!silent)
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
                if (!silent)
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
                            System.Windows.Application.Current.Dispatcher.Invoke(() => App.RequestFullExit());
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

        private void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
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

        /// <summary>
        /// Always shows the target-version picker when multiple builds exist (used by 1-Click install).
        /// Returns null with ModPackSelected set when the user picks mod-pack install.
        /// </summary>
        private SSF2VersionEntry? PickTargetVersionForMod(GameBananaMod mod, out bool modPackSelected)
        {
            modPackSelected = false;
            if (_modManager.Versions.Count == 0) return null;
            if (_modManager.Versions.Count == 1) return _modManager.Versions[0];

            var versionDisplayNames = _modManager.Versions
                .Select(v => $"{v.Nickname}  —  {v.VersionName}  —  {v.FolderPath}").ToList();
            var picker = new ListPickerDialog("Target Version",
                $"Which SSF2 version should \"{mod.Name}\" be installed to?",
                versionDisplayNames, showModPackButton: true);
            picker.Owner = this;
            if (picker.ShowDialog() != true)
                return null;

            if (picker.ModPackSelected)
            {
                modPackSelected = true;
                return null;
            }

            if (picker.SelectedItem == null)
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
                BringToFront();

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
                    file = GameBananaUrlHelper.MatchProtocolFile(archiveUrl, files);
                    if (file != null && !string.IsNullOrEmpty(file.DownloadUrl))
                        downloadUrl = file.DownloadUrl;
                    else if (GameBananaUrlHelper.TryExtractFileId(archiveUrl, out _))
                        downloadUrl = GameBananaUrlHelper.NormalizeSchemeTypos(archiveUrl);
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

                var targetEntry = PickTargetVersionForMod(mod, out var modPackSelected);
                if (modPackSelected)
                {
                    await InstallAsModPackAsync(mod, file);
                    return;
                }
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
                RefreshInstalledMods(repopulateFilters: true);
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

                await LoadBrowseModsAsync();
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
                    if (string.IsNullOrWhiteSpace(cli.NewsPath))
                        _ = OpenNewsAsync();
                    else
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
                        App.EnsureLauncher().ReceiveOneClick(proto.RawUrl);
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
                    if (mod == null || mod.Enabled) continue;
                    try
                    {
                        _modManager.ToggleMod(mod);
                    }
                    catch (ModFolderNotFoundException ex)
                    {
                        DebugLogger.Error($"Enable failed: {ex.Message}");
                        MessageBox.Show(ex.Message, "Mod Files Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
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
                        App.EnsureLauncher().ReceiveOneClick(proto.RawUrl);
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
                                Dispatcher.Invoke(() => RefreshInstalledMods(repopulateFilters: true));
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

                if (cli.NewsCacheClear)
                    NewsSyncService.ClearCache();

                if (cli.RefreshNews)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            if (_settingsService == null) return;
                            var sync = await NewsSyncService.SyncAsync(_settingsService, force: true);
                            Dispatcher.Invoke(() =>
                            {
                                if (!sync.Success)
                                {
                                    MessageBox.Show($"News sync failed:\n{sync.ErrorMessage}", "News Sync",
                                        MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                                else
                                {
                                    DebugLogger.Log($"News sync OK — {sync.DownloadedCount} article(s) updated");
                                    UpdateNewsBadge();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                                MessageBox.Show($"News sync failed:\n{ex.Message}", "News Sync",
                                    MessageBoxButton.OK, MessageBoxImage.Error));
                        }
                    });
                }

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
            RefreshInstalledMods(repopulateFilters: true);
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

        private static readonly string[] CharacterPages = { "clonecharacter", "addcharacter", "testcharacter", "managecss" };
        private static readonly string[] StagePages = { "clonestage", "addstage", "managesss", "teststage" };
        private static readonly string[] MusicPages = { "addmusic", "managemusic" };

        private void SetActivePage(string page)
        {
            // Map launcher-friendly aliases
            page = page?.Trim().ToLowerInvariant() switch
            {
                "make" or "makemods" => "clonecharacter",
                "download" or "downloadmods" => "browse",
                "manage" or "managemods" => "installed",
                _ => page?.Trim().ToLowerInvariant() ?? "home"
            };

            if (page is "browse" or "installed")
            {
                _settingsService?.SetLegacyModManager(true);
                ApplyLegacySidebarVisibility();
                if (page == "browse")
                    _ = LoadBrowseModsAsync();
            }

            _activePageName = page;
            ShowNamedPage("PageHome", page == "home");
            PageBrowse.Visibility = page == "browse" ? Visibility.Visible : Visibility.Collapsed;
            PageInstalled.Visibility = page == "installed" ? Visibility.Visible : Visibility.Collapsed;
            PageBuilds.Visibility = page == "builds" ? Visibility.Visible : Visibility.Collapsed;
            PageCloneCharacter.Visibility = page == "clonecharacter" ? Visibility.Visible : Visibility.Collapsed;
            ShowNamedPage("PageAddCharacter", page == "addcharacter");
            ShowNamedPage("PageTestCharacter", page == "testcharacter");
            ShowNamedPage("PageManageCss", page == "managecss");
            ShowNamedPage("PageCloneStage", page == "clonestage");
            ShowNamedPage("PageAddStage", page == "addstage");
            ShowNamedPage("PageManageSss", page == "managesss");
            ShowNamedPage("PageTestStage", page == "teststage");
            ShowNamedPage("PageAddMusic", page == "addmusic");
            ShowNamedPage("PageManageMusic", page == "managemusic");
            ShowNamedPage("PageCostumes", page == "costumes");
            ShowNamedPage("PageEvents", page == "events");
            PagePressor.Visibility = page == "pressor" ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("PageMappings") is UIElement pageMappings)
                pageMappings.Visibility = page == "mappings" ? Visibility.Visible : Visibility.Collapsed;
            PageGettingStarted.Visibility = page == "gettingstarted" ? Visibility.Visible : Visibility.Collapsed;
            PageNews.Visibility = page == "news" ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = page == "log" ? Visibility.Visible : Visibility.Collapsed;

            try
            {
                if (page == "browse" && BrowseScrollViewer != null)
                    BrowseScrollViewer.ScrollToVerticalOffset(0);
                if (page == "installed" && InstalledScrollViewer != null)
                    InstalledScrollViewer.ScrollToVerticalOffset(0);
                if (page == "settings" && SettingsScrollViewer != null)
                {
                    SettingsScrollViewer.ScrollToVerticalOffset(0);
                    RefreshModdingToolchainStatus();
                }
                if (page == "log" && LogScrollViewer != null)
                    LogScrollViewer.ScrollToVerticalOffset(0);
            }
            catch { }

            if (FindName("ExpCharacter") is Expander expChar)
                expChar.IsExpanded = expChar.IsExpanded || CharacterPages.Contains(page);
            if (FindName("ExpStage") is Expander expStage)
                expStage.IsExpanded = expStage.IsExpanded || StagePages.Contains(page);
            if (FindName("ExpMusic") is Expander expMusic)
                expMusic.IsExpanded = expMusic.IsExpanded || MusicPages.Contains(page);
            if (FindName("ExpCostumes") is Expander expCos)
                expCos.IsExpanded = expCos.IsExpanded || page == "costumes";
            if (FindName("ExpEvents") is Expander expEv)
                expEv.IsExpanded = expEv.IsExpanded || page == "events";

            StyleSidebar("BtnHome", page == "home", sub: false);
            StyleSidebar("BtnBrowse", page == "browse", sub: false);
            StyleSidebar("BtnInstalled", page == "installed", sub: false);
            StyleSidebar("BtnBuilds", page == "builds", sub: false);
            StyleSidebar("BtnCloneCharacter", page == "clonecharacter", sub: true);
            StyleSidebar("BtnAddCharacter", page == "addcharacter", sub: true);
            StyleSidebar("BtnTestCharacter", page == "testcharacter", sub: true);
            StyleSidebar("BtnManageCss", page == "managecss", sub: true);
            StyleSidebar("BtnCloneStage", page == "clonestage", sub: true);
            StyleSidebar("BtnAddStage", page == "addstage", sub: true);
            StyleSidebar("BtnManageSss", page == "managesss", sub: true);
            StyleSidebar("BtnTestStage", page == "teststage", sub: true);
            StyleSidebar("BtnAddMusic", page == "addmusic", sub: true);
            StyleSidebar("BtnManageMusic", page == "managemusic", sub: true);
            StyleSidebar("BtnMakeCostumes", page == "costumes", sub: true);
            StyleSidebar("BtnMakeEvents", page == "events", sub: true);
            StyleSidebar("BtnGettingStarted", page == "gettingstarted", sub: false);
            StyleSidebar("BtnNews", page == "news", sub: false);
            StyleSidebar("BtnSettings", page == "settings", sub: false);
            StyleSidebar("BtnLog", page == "log", sub: false);
        }

        private void ShowNamedPage(string name, bool visible)
        {
            if (FindName(name) is UIElement el)
                el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StyleSidebar(string name, bool active, bool sub)
        {
            if (FindName(name) is not Button btn) return;
            var styleKey = sub
                ? (active ? "SidebarSubButtonActive" : "SidebarSubButton")
                : (active ? "SidebarButtonActive" : "SidebarButton");
            try { btn.Style = (Style)FindResource(styleKey); } catch { }
            Brush activeFg = TryFindResource("SelectionForegroundBrush") as Brush
                ?? TryFindResource("TextPrimaryBrush") as Brush
                ?? Brushes.White;
            Brush inactiveFg = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            btn.Foreground = active ? activeFg : inactiveFg;
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            RefreshHomePage();
            SetActivePage("home");
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e) => SetActivePage("browse");

        private void BtnInstalled_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("installed");
            RefreshInstalledMods(repopulateFilters: true);
        }

        private void BtnNews_Click(object sender, RoutedEventArgs e) => _ = OpenNewsAsync();

        // Legacy sync helper for CLI --news-path=
        public void OpenNews(string? newsPath = null)
        {
            SetActivePage("news");
            try
            {
                var path = string.IsNullOrWhiteSpace(newsPath) ? AppPaths.NewsFolder : newsPath;
                PageNews.LoadLocal(path);
                UpdateNewsBadge();
            }
            catch (Exception ex)
            {
                DevFileLog.Write($"[OpenNews] Failed: {ex}\n");
            }
        }
        private void BtnModSSF2_Click(object sender, RoutedEventArgs e) => SetActivePage("gettingstarted");

        private void BtnGettingStarted_Click(object sender, RoutedEventArgs e) => SetActivePage("gettingstarted");

        private void BtnPressor_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("pressor");
            try
            {
                var last = _settingsService?.GetPressorLastDirectory();
                if (!string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(TxtPressorInstallRoot.Text))
                    TxtPressorInstallRoot.Text = last;
            }
            catch { }
        }

        private void BtnMappings_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("mappings");
            try
            {
                var last = _settingsService?.GetPressorLastDirectory();
                if (!string.IsNullOrWhiteSpace(last))
                    SetMappingsInstallRoot(last);
                else if (!string.IsNullOrWhiteSpace(TxtPressorInstallRoot.Text))
                    SetMappingsInstallRoot(TxtPressorInstallRoot.Text.Trim());
            }
            catch { }
            _ = RefreshMappingsPageAsync();
        }

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

        private void BtnCloneCharacter_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("clonecharacter");
            _ = LoadShellClonePageAsync();
        }

        private void BtnAddCharacter_Click(object sender, RoutedEventArgs e) => SetActivePage("addcharacter");
        private void BtnTestCharacter_Click(object sender, RoutedEventArgs e) => SetActivePage("testcharacter");
        private void BtnManageCss_Click(object sender, RoutedEventArgs e) => SetActivePage("managecss");
        private void BtnCloneStage_Click(object sender, RoutedEventArgs e) => SetActivePage("clonestage");
        private void BtnAddStage_Click(object sender, RoutedEventArgs e) => SetActivePage("addstage");
        private void BtnManageSss_Click(object sender, RoutedEventArgs e) => SetActivePage("managesss");
        private void BtnTestStage_Click(object sender, RoutedEventArgs e) => SetActivePage("teststage");
        private void BtnAddMusic_Click(object sender, RoutedEventArgs e) => SetActivePage("addmusic");
        private void BtnManageMusic_Click(object sender, RoutedEventArgs e)
        {
            SetActivePage("managemusic");
            _ = RefreshShellMusicListAsync();
        }
        private void BtnMakeCostumes_Click(object sender, RoutedEventArgs e) => SetActivePage("costumes");
        private void BtnMakeEvents_Click(object sender, RoutedEventArgs e) => SetActivePage("events");

        private string? _lastCloneOutputPath;

        private void ApplyLegacySidebarVisibility()
        {
            // Browse / Installed stay visible for launcher Download / Manage (and newbies).
            // Builds remains behind the legacy toggle.
            var buildsOn = _settingsService?.GetLegacyModManager() == true;
            if (FindName("BtnBrowse") is Button b1) b1.Visibility = Visibility.Visible;
            if (FindName("BtnInstalled") is Button b2) b2.Visibility = Visibility.Visible;
            if (FindName("BtnBuilds") is Button b3)
                b3.Visibility = buildsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!buildsOn && _activePageName == "builds")
                SetActivePage("home");
        }

        private void ChkLegacyModManager_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService == null) return;
            var on = FindName("ChkLegacyModManager") is CheckBox chk && chk.IsChecked == true;
            _settingsService.SetLegacyModManager(on);
            ApplyLegacySidebarVisibility();
            if (on)
                _ = LoadBrowseModsAsync();
        }

        private string? GetShellInstallRoot()
        {
            var build = ShellWorkspaceService.FindBuildFolder();
            if (!string.IsNullOrWhiteSpace(build) && Directory.Exists(build))
                return build;
            var active = _modManager.GetActiveVersionPath();
            if (!string.IsNullOrWhiteSpace(active) && Directory.Exists(active))
                return active;
            return GetMappingsInstallRoot();
        }

        private void BindShellInstallRoot()
        {
            var root = GetShellInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            SetMappingsInstallRoot(root);
            if (FindName("TxtPressorInstallRoot") is TextBox pressor && string.IsNullOrWhiteSpace(pressor.Text))
                pressor.Text = root;
        }

        private void RefreshHomePage()
        {
            var build = GetShellInstallRoot();
            if (FindName("TxtHomeShellDir") is TextBox dirBox)
                dirBox.Text = build ?? "SSF2 Shell was not found next to SSF2ModManager.exe";
            var exe = string.IsNullOrWhiteSpace(build) ? null : ShellWorkspaceService.FindGameExecutable(build);
            if (FindName("TxtHomeExeStatus") is TextBlock status)
            {
                status.Text = exe != null
                    ? "Ready to play: " + exe
                    : "Could not find Super Smash Flash 2 Beta.exe in the Shell build folder.";
            }
            if (FindName("BtnHomePlay") is Button play)
                play.IsEnabled = exe != null;
        }

        private void BtnHomePlay_Click(object sender, RoutedEventArgs e)
        {
            var root = GetShellInstallRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                MessageBox.Show("SSF2 Shell build folder was not found.", "Play",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                ShellDirectLaunchService.Launch(root, ShellDirectLaunchService.ScreenHome);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Play", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnHomeOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var root = GetShellInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show("SSF2 Shell folder was not found.", "Open folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }

        private void LaunchShellScreen(string screen)
        {
            var root = GetShellInstallRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                MessageBox.Show("SSF2 Shell build folder was not found.", "Launch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                ShellDirectLaunchService.Launch(root, screen);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Launch", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnTestCharacterLaunch_Click(object sender, RoutedEventArgs e) =>
            LaunchShellScreen(ShellDirectLaunchService.ScreenCss);
        private void BtnManageCssLaunch_Click(object sender, RoutedEventArgs e) =>
            LaunchShellScreen(ShellDirectLaunchService.ScreenCss);
        private void BtnManageSssLaunch_Click(object sender, RoutedEventArgs e) =>
            LaunchShellScreen(ShellDirectLaunchService.ScreenSssEdit);
        private void BtnTestStageLaunch_Click(object sender, RoutedEventArgs e) =>
            LaunchShellScreen(ShellDirectLaunchService.ScreenSss);

        private void BtnManageCssHelp_Click(object sender, RoutedEventArgs e)
        {
            var root = ShellWorkspaceService.FindBuildFolder();
            var readme = root != null
                ? Path.Combine(root, "src", "mods", "menu_editor", "README.md")
                : null;
            if (string.IsNullOrWhiteSpace(readme) || !File.Exists(readme))
                readme = root != null
                    ? Path.Combine(root, "expansions", "mods", "css_editor", "README.md")
                    : null;
            if (!string.IsNullOrWhiteSpace(readme) && File.Exists(readme))
                Process.Start(new ProcessStartInfo { FileName = readme, UseShellExecute = true });
            else
                MessageBox.Show("In-game: F1 → Edit Character Select (CSS Editor pack).", "CSS Editor",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async System.Threading.Tasks.Task LoadShellClonePageAsync()
        {
            var root = GetShellInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || FindName("CmbCloneSourceCharacter") is not ComboBox combo)
                return;
            try
            {
                BindShellInstallRoot();
                if (_mappingsDoc == null || !string.Equals(_mappingsLoadedRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(root, exportIfMissing: false);
                    _mappingsLoadedRoot = root;
                }
                var selected = (combo.SelectedItem as ShellRosterCharacter)?.Id;
                var list = ShellCharacterCloneService.ListPlayableCharacters(root, _mappingsDoc);
                combo.ItemsSource = list;
                if (list.Count == 0)
                {
                    AppendCloneLog("No character SWFs found under data/character. The roster list needs files on disk.");
                    return;
                }
                combo.SelectedItem = list.FirstOrDefault(c => c.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))
                                     ?? list[0];
            }
            catch (Exception ex)
            {
                AppendCloneLog("Could not load roster: " + ex.Message);
            }
        }

        private void AppendCloneLog(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendCloneLog(line));
                return;
            }
            if (FindName("TxtCloneLog") is not TextBox log) return;
            if (log.Text.Length > 0)
                log.AppendText(Environment.NewLine);
            log.AppendText(line);
            log.CaretIndex = log.Text.Length;
            log.ScrollToEnd();
        }

        private async void BtnCloneRun_Click(object sender, RoutedEventArgs e)
        {
            var root = GetShellInstallRoot();
            var source = (FindName("CmbCloneSourceCharacter") as ComboBox)?.SelectedItem as ShellRosterCharacter;
            var newName = TxtCloneNewName.Text?.Trim() ?? "";
            var displayName = TxtCloneDisplayName.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(root))
            {
                MessageBox.Show("SSF2 Shell build folder was not found.", "Clone Character",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (source == null)
            {
                MessageBox.Show("Pick a character that is already in the game.", "Clone Character",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Enter a character name (this becomes the expansion folder, e.g. Clone Dee).", "Clone Character",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var jpexs = new JpexsCliService();
            if (!jpexs.IsAvailable(null))
            {
                MessageBox.Show(
                    "The bundled JPEXS FFDec could not be found next to the app.\n\nReinstall SSF2 Mod Manager so the 'ffdec' folder is present (and 'jre' for the bundled Java runtime).",
                    "FFDec required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnCloneRun.IsEnabled = false;
            BtnCloneOpenOutput.IsEnabled = false;
            _lastCloneOutputPath = null;
            TxtCloneLog.Text = "";
            AppendCloneLog($"Cloning {source.Label} → {newName}…");
            AppendCloneLog("JPEXS renames the fighter SWF, then always exports FLA (CS6 XFL).");

            var progress = new Progress<string>(msg => AppendCloneLog(msg));
            try
            {
                var keepOld = ChkCloneKeepOldAssets.IsChecked == true;
                var result = await Task.Run(() => _shellClone.CloneIntoShellAsync(
                    root, source.Id, newName, displayName, null, progress,
                    keepOldAssets: keepOld));
                _lastCloneOutputPath = result.ExpansionFolder;
                foreach (var note in result.Notes)
                    AppendCloneLog(note);
                BtnCloneOpenOutput.IsEnabled = true;
                MessageBox.Show(
                    $"Clone finished.\n\n{result.ExpansionFolder}",
                    "Clone Character",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OutOfMemoryException ex)
            {
                AppendCloneLog("Error: ran out of memory while cloning. " + ex.Message);
                DebugLogger.Log($"[Clone] OOM: {ex}");
                MessageBox.Show(
                    "Clone ran out of memory. Close other apps and try again — the rewrite now streams the SWF XML instead of loading it all at once.",
                    "Clone Character",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                AppendCloneLog("Error: " + ex.Message);
                DebugLogger.Log($"[Clone] Failed: {ex}");
                MessageBox.Show("Clone failed:\n\n" + ex.Message, "Clone Character",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCloneRun.IsEnabled = true;
            }
        }

        private void BtnCloneOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = _lastCloneOutputPath;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    return;
                }
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                    return;
                }
                var expansions = GetShellInstallRoot();
                if (!string.IsNullOrWhiteSpace(expansions))
                {
                    var folder = ShellWorkspaceService.ExpansionsDirectory(expansions);
                    if (Directory.Exists(folder))
                        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open Output", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AppendShellMusicLog(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendShellMusicLog(line));
                return;
            }
            if (FindName("TxtShellMusicLog") is not TextBox log) return;
            if (log.Text.Length > 0)
                log.AppendText(Environment.NewLine);
            log.AppendText(line);
            log.CaretIndex = log.Text.Length;
            log.ScrollToEnd();
        }

        private void BtnShellMusicBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select MP3",
                Filter = "MP3|*.mp3|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            if (FindName("TxtShellMusicMp3") is TextBox box)
                box.Text = dlg.FileName;
            if (FindName("TxtShellMusicName") is TextBox nameBox && string.IsNullOrWhiteSpace(nameBox.Text))
                nameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (FindName("TxtShellMusicId") is TextBox idBox && string.IsNullOrWhiteSpace(idBox.Text))
                idBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }

        private async void BtnShellMusicAdd_Click(object sender, RoutedEventArgs e)
        {
            var root = GetShellInstallRoot();
            var mp3 = (FindName("TxtShellMusicMp3") as TextBox)?.Text?.Trim() ?? "";
            var id = (FindName("TxtShellMusicId") as TextBox)?.Text?.Trim() ?? "";
            var song = (FindName("TxtShellMusicName") as TextBox)?.Text?.Trim() ?? "";
            var artist = (FindName("TxtShellMusicArtist") as TextBox)?.Text?.Trim() ?? "";
            var loopText = (FindName("TxtShellMusicLoop") as TextBox)?.Text?.Trim() ?? "0";
            if (string.IsNullOrWhiteSpace(root))
            {
                MessageBox.Show("SSF2 Shell build folder was not found.", "Add Music",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(mp3) || string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("Pick an MP3 and enter a track id.", "Add Music",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(loopText, out var loopMs) || loopMs < 0) loopMs = 0;

            if (FindName("BtnShellMusicAdd") is Button addBtn) addBtn.IsEnabled = false;
            if (FindName("TxtShellMusicLog") is TextBox log) log.Text = "";
            AppendShellMusicLog($"Adding '{id}'…");
            try
            {
                BindShellInstallRoot();
                if (_mappingsDoc == null || !string.Equals(_mappingsLoadedRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(root, exportIfMissing: false);
                    _mappingsLoadedRoot = root;
                }

                var progress = new Progress<string>(AppendShellMusicLog);
                var outPath = await _musicEdit.CreateMusicFromMp3Async(
                    root, mp3, id, loopMs, song, artist, _mappingsDoc, null, progress);
                AppendShellMusicLog("Saving mappings.json…");
                await _mappingsEdit.SaveDocumentAsync(root, _mappingsDoc);
                try
                {
                    await _mappingsEdit.CommitAsync(root, _mappingsDoc, null, progress);
                }
                catch (Exception commitEx)
                {
                    AppendShellMusicLog("mappings.swf apply skipped: " + commitEx.Message);
                }
                AppendShellMusicLog("Wrote " + outPath);
                MessageBox.Show($"Song created:\n{outPath}", "Add Music",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendShellMusicLog("Error: " + ex.Message);
                MessageBox.Show(ex.Message, "Add Music", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (FindName("BtnShellMusicAdd") is Button addBtn2) addBtn2.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task RefreshShellMusicListAsync()
        {
            var root = GetShellInstallRoot();
            if (FindName("LstShellMusic") is not ListBox list) return;
            if (string.IsNullOrWhiteSpace(root))
            {
                list.ItemsSource = null;
                return;
            }
            try
            {
                BindShellInstallRoot();
                if (_mappingsDoc == null || !string.Equals(_mappingsLoadedRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(root, exportIfMissing: false);
                    _mappingsLoadedRoot = root;
                }
                var songs = new List<CachedSongInfo>();
                if (_mappingsDoc["music"] is JObject music)
                {
                    foreach (var prop in music.Properties())
                    {
                        var obj = prop.Value as JObject;
                        songs.Add(new CachedSongInfo
                        {
                            Id = prop.Name,
                            Name = obj?["name"]?.ToString() ?? prop.Name,
                            SwfPath = MappingsEditService.ResolveEntrySwfPath(root, _mappingsDoc, "music", prop.Name) ?? ""
                        });
                    }
                }
                list.ItemsSource = songs.OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                list.ItemsSource = new[] { new CachedSongInfo { Name = "Could not load music: " + ex.Message } };
            }
        }

        private async void BtnManageMusicRefresh_Click(object sender, RoutedEventArgs e) =>
            await RefreshShellMusicListAsync();

        private void BtnManageMusicOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var root = GetShellInstallRoot();
            var sound = root != null ? MusicEditService.FindSoundFolder(root) : null;
            if (string.IsNullOrWhiteSpace(sound) || !Directory.Exists(sound))
            {
                MessageBox.Show("data/sound was not found in SSF2 Shell.", "Manage Music",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = sound, UseShellExecute = true });
        }

        // ── Pressor ─────────────────────────────────────────────

        private void SetPressorStatus(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetPressorStatus(text));
                return;
            }
            TxtPressorStatus.Text = text;
        }

        private void AppendPressorLog(string msg)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendPressorLog(msg));
                return;
            }
            if (TxtPressorDatLog.Text.Length > 0)
                TxtPressorDatLog.AppendText(Environment.NewLine);
            TxtPressorDatLog.AppendText(msg);
            TxtPressorDatLog.ScrollToEnd();
        }

        private string? GetPressorStartDirectory()
        {
            try
            {
                var last = _settingsService?.GetPressorLastDirectory();
                if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last)) return last;
            }
            catch { }
            var root = TxtPressorInstallRoot.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) return root;
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void RememberPressorDirectory(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    _settingsService?.SetPressorLastDirectory(dir);
            }
            catch { }
        }

        private async void BtnPressorUncompressFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select .ssf files to uncompress",
                Filter = "SSF files|*.ssf;*.ssfrec|All files|*.*",
                Multiselect = true,
                InitialDirectory = GetPressorStartDirectory()
            };
            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;
            RememberPressorDirectory(dlg.FileNames[0]);
            await RunPressorAsync(async (svc, progress) =>
                await svc.UncompressFilesAsync(dlg.FileNames, progress));
        }

        private async void BtnPressorUncompressFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder of .ssf files to uncompress",
                ShowNewFolderButton = false,
                SelectedPath = GetPressorStartDirectory() ?? ""
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            RememberPressorDirectory(dlg.SelectedPath);
            await RunPressorAsync(async (svc, progress) =>
                await svc.UncompressFolderAsync(dlg.SelectedPath, progress));
        }

        private async void BtnPressorCompressFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select .swf files to compress",
                Filter = "SWF files|*.swf|All files|*.*",
                Multiselect = true,
                InitialDirectory = GetPressorStartDirectory()
            };
            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;
            RememberPressorDirectory(dlg.FileNames[0]);
            await RunPressorAsync(async (svc, progress) =>
                await svc.CompressFilesAsync(dlg.FileNames, useGarbageHeader: true, progress));
        }

        private async void BtnPressorCompressFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder of .swf files to compress",
                ShowNewFolderButton = false,
                SelectedPath = GetPressorStartDirectory() ?? ""
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            RememberPressorDirectory(dlg.SelectedPath);
            await RunPressorAsync(async (svc, progress) =>
                await svc.CompressFolderAsync(dlg.SelectedPath, useGarbageHeader: true, progress));
        }

        private async Task RunPressorAsync(Func<PressorService, IProgress<string>, Task<PressorBatchResult>> action)
        {
            SetPressorStatus("Working…");
            var svc = new PressorService();
            var progress = new Progress<string>(SetPressorStatus);
            try
            {
                var result = await action(svc, progress);
                SetPressorStatus(
                    $"Pressor finished in {result.ElapsedSeconds:0.00}s — {result.SuccessCount} ok, {result.FailCount} failed.");
                if (result.FailCount > 0)
                {
                    var errs = string.Join("\n", result.Files.Where(f => !f.Success).Select(f =>
                        $"{Path.GetFileName(f.SourcePath)}: {f.Error}"));
                    System.Windows.MessageBox.Show(this, errs, "Pressor — some files failed",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                SetPressorStatus("Error: " + ex.Message);
                System.Windows.MessageBox.Show(this, ex.Message, "Pressor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPressorBrowseInstall_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select SSF2 install root (folder containing SSF2.swf and data/)",
                ShowNewFolderButton = false,
                SelectedPath = GetPressorStartDirectory() ?? ""
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            TxtPressorInstallRoot.Text = dlg.SelectedPath;
            RememberPressorDirectory(dlg.SelectedPath);
            SetPressorStatus(GamePrepareService.IsPrepared(dlg.SelectedPath)
                ? "Install is prepared for modding."
                : "Install selected. Ready to prepare.");
        }

        private Views.GameDebugConsoleWindow? _gameDebugConsole;

        private void BtnPressorDebugConsole_Click(object sender, RoutedEventArgs e)
        {
            if (_gameDebugConsole != null)
            {
                _gameDebugConsole.Activate();
                return;
            }

            _gameDebugConsole = new Views.GameDebugConsoleWindow(
                () =>
                {
                    var pressor = TxtPressorInstallRoot.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(pressor) && Directory.Exists(pressor))
                        return pressor;
                    return GetMappingsInstallRoot();
                });
            _gameDebugConsole.Owner = this;
            _gameDebugConsole.Closed += (_, _) => _gameDebugConsole = null;
            _gameDebugConsole.Show();
        }

        private async void BtnPressorDebugLaunch_Click(object sender, RoutedEventArgs e)
        {
            var root = TxtPressorInstallRoot.Text?.Trim();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this, "Browse to an SSF2 install folder first.",
                    "Launch + debug CMD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!GamePrepareService.IsPrepared(root))
            {
                System.Windows.MessageBox.Show(this, "Prepare the install first, then install/upgrade the debug engine patch.",
                    "Launch + debug CMD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnPressorDebugLaunch.IsEnabled = false;
            GamePrepareService.DebugConsolePatchStatus status;
            try
            {
                var rootCopy = root;
                status = await Task.Run(() => GamePrepareService.GetDebugConsolePatchStatus(rootCopy));
            }
            finally
            {
                BtnPressorDebugLaunch.IsEnabled = true;
            }

            if (status == GamePrepareService.DebugConsolePatchStatus.Missing)
            {
                var go = System.Windows.MessageBox.Show(this,
                    "The debug engine patch is not installed in SSF2.swf.\n\nOpen Game Debug Console to install it now?",
                    "Launch + debug CMD", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (go == MessageBoxResult.Yes)
                    BtnPressorDebugConsole_Click(sender, e);
                return;
            }

            if (status == GamePrepareService.DebugConsolePatchStatus.PrintOnly)
            {
                var upgrade = System.Windows.MessageBox.Show(this,
                    "Patch is print-only (won't capture many crashes).\n\nUpgrade patch for uncaught errors first?",
                    "Launch + debug CMD", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (upgrade == MessageBoxResult.Cancel) return;
                if (upgrade == MessageBoxResult.Yes)
                {
                    BtnPressorDebugConsole_Click(sender, e);
                    return;
                }
            }

            // Close in-app console so it doesn't hold the port.
            try { _gameDebugConsole?.Close(); } catch { /* ignore */ }

            try
            {
                GameDebugCmdHost.StartDetached(root, launchGame: true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Launch + debug CMD",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnPressorPrepare_Click(object sender, RoutedEventArgs e)
        {
            var root = TxtPressorInstallRoot.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this, "Browse to an SSF2 install folder first.", "Prepare for modding",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (GamePrepareService.IsPrepared(root))
            {
                var repair = System.Windows.MessageBox.Show(this,
                    "This install is already prepared.\n\nRe-apply the engine patch only?\n" +
                    "(Restores Config/Main/Resource + CSS/SSS icon scaling from backup.\n" +
                    "Choose No, then use Revert + Prepare for a full redo.)",
                    "Repair engine patch", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (repair != MessageBoxResult.Yes) return;

                TxtPressorDatLog.Text = "";
                BtnPressorPrepare.IsEnabled = false;
                BtnPressorRevert.IsEnabled = false;
                SetPressorStatus("Repairing engine…");
                try
                {
                    var svc = new GamePrepareService();
                    var result = await svc.RepairEngineAsync(root, null, new Progress<string>(AppendPressorLog));
                    SetPressorStatus(result.Message);
                    AppendPressorLog(result.Message);
                    System.Windows.MessageBox.Show(this, result.Message, "Repair engine patch",
                        MessageBoxButton.OK,
                        result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    AppendPressorLog("Error: " + ex.Message);
                    SetPressorStatus("Error: " + ex.Message);
                    System.Windows.MessageBox.Show(this, ex.Message, "Repair engine patch",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnPressorPrepare.IsEnabled = true;
                    BtnPressorRevert.IsEnabled = true;
                }
                return;
            }

            var confirm = System.Windows.MessageBox.Show(this,
                "This will modify the selected install in place:\n\n" +
                "• Backup and patch SSF2.swf (encrypt_files=false, safe raw SWF loads)\n" +
                "• Decompress all DAT###.ssf files to friendly .swf paths\n" +
                "• Rewrite the embedded manifest / mappings to use .swf names\n\n" +
                "Use a copy of your install if you want to keep a pristine Beta.\nContinue?",
                "Prepare for modding", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            TxtPressorDatLog.Text = "";
            BtnPressorPrepare.IsEnabled = false;
            BtnPressorRevert.IsEnabled = false;
            SetPressorStatus("Preparing…");
            var progress = new Progress<string>(AppendPressorLog);

            try
            {
                var svc = new GamePrepareService();
                var result = await svc.PrepareAsync(root, null, progress);
                SetPressorStatus(result.Message);
                AppendPressorLog(result.Message);
                if (result.Errors.Count > 0)
                    AppendPressorLog(string.Join(Environment.NewLine, result.Errors));
                System.Windows.MessageBox.Show(this, result.Message, "Prepare for modding",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AppendPressorLog("Error: " + ex.Message);
                SetPressorStatus("Error: " + ex.Message);
                System.Windows.MessageBox.Show(this, ex.Message, "Prepare for modding",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnPressorPrepare.IsEnabled = true;
                BtnPressorRevert.IsEnabled = true;
            }
        }

        private async void BtnPressorRevert_Click(object sender, RoutedEventArgs e)
        {
            var root = TxtPressorInstallRoot.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this, "Browse to an SSF2 install folder first.", "Revert to DAT",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!GamePrepareService.IsPrepared(root))
            {
                System.Windows.MessageBox.Show(this,
                    "No prepare state found for this install.", "Revert to DAT",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(this,
                "Recompress all .swf files back to DAT###.ssf, restore original names, and restore SSF2.swf from backup?",
                "Revert to DAT", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            TxtPressorDatLog.Text = "";
            BtnPressorPrepare.IsEnabled = false;
            BtnPressorRevert.IsEnabled = false;
            SetPressorStatus("Reverting…");
            var progress = new Progress<string>(AppendPressorLog);

            try
            {
                var svc = new GamePrepareService();
                var result = await svc.RevertAsync(root, progress);
                SetPressorStatus(result.Message);
                AppendPressorLog(result.Message);
                System.Windows.MessageBox.Show(this, result.Message, "Revert to DAT",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AppendPressorLog("Error: " + ex.Message);
                SetPressorStatus("Error: " + ex.Message);
                System.Windows.MessageBox.Show(this, ex.Message, "Revert to DAT",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnPressorPrepare.IsEnabled = true;
                BtnPressorRevert.IsEnabled = true;
            }
        }

        private async void BtnPressorConvertOneDat_Click(object sender, RoutedEventArgs e)
        {
            var root = TxtPressorInstallRoot.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this,
                    "Browse to an SSF2 install folder first (needed to resolve DAT names).",
                    "Convert file → DAT", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a .swf to convert to DAT (original is kept)",
                Filter = "SWF files|*.swf|All files|*.*",
                Multiselect = false,
                InitialDirectory = GetPressorStartDirectory()
            };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName)) return;
            RememberPressorDirectory(dlg.FileName);

            SetPressorStatus("Converting…");
            AppendPressorLog($"Converting {Path.GetFileName(dlg.FileName)}…");
            try
            {
                var svc = new GamePrepareService();
                var result = await svc.ConvertFileToDatAlongsideAsync(
                    root, dlg.FileName, useGarbageHeader: true,
                    progress: new Progress<string>(AppendPressorLog));
                if (result.Success)
                {
                    SetPressorStatus($"Wrote {Path.GetFileName(result.OutputPath)} (source .swf kept).");
                    AppendPressorLog($"OK → {result.OutputPath}");
                    System.Windows.MessageBox.Show(this,
                        $"Wrote:\n{result.OutputPath}\n\nThe original .swf was not replaced.",
                        "Convert file → DAT", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    SetPressorStatus("Convert failed: " + result.Error);
                    System.Windows.MessageBox.Show(this, result.Error ?? "Convert failed.",
                        "Convert file → DAT", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AppendPressorLog("Error: " + ex.Message);
                SetPressorStatus("Error: " + ex.Message);
                System.Windows.MessageBox.Show(this, ex.Message, "Convert file → DAT",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                        using var archive = ArchiveFactory.OpenArchive(mstream);
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
                    using var archive = ArchiveFactory.OpenArchive(stream);
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
                await TryToggleModAsync(vm.InstalledVersion);
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

        private void RefreshInstalledMods(bool repopulateFilters = false)
        {
            if (repopulateFilters)
                PopulateInstalledFilters();
            ApplyInstalledFilters();
        }

        private static string? GetFilterComboSelection(System.Windows.Controls.ComboBox cmb) =>
            (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString();

        private static void RestoreFilterComboSelection(System.Windows.Controls.ComboBox cmb, string? previous)
        {
            if (string.IsNullOrEmpty(previous))
            {
                cmb.SelectedIndex = 0;
                return;
            }

            for (var i = 0; i < cmb.Items.Count; i++)
            {
                if ((cmb.Items[i] as ComboBoxItem)?.Content?.ToString() == previous)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }

            cmb.SelectedIndex = 0;
        }

        private async void BtnToggleMod_Click(object sender, RoutedEventArgs e)
        {
            var mod = (InstalledMod)((Button)sender).Tag;
            try
            {
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

                await TryToggleModAsync(mod);
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
            RefreshInstalledMods(repopulateFilters: true);
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

        private void RefreshModdingToolchainStatus()
        {
            try
            {
                var status = ModdingToolchainService.GetStatus();
                if (TxtModdingToolchainStatus != null)
                    TxtModdingToolchainStatus.Text = status.Summary;
            }
            catch (Exception ex)
            {
                if (TxtModdingToolchainStatus != null)
                    TxtModdingToolchainStatus.Text = "Status error: " + ex.Message;
            }
        }

        private void BtnModdingToolchainRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshModdingToolchainStatus();
            if (TxtModdingToolchainLog != null)
                TxtModdingToolchainLog.Text = "Status refreshed.";
        }

        private async void BtnModdingInstallVsCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TxtModdingToolchainLog != null)
                    TxtModdingToolchainLog.Text = "Installing VS Code…";
                var progress = new Progress<string>(m =>
                {
                    if (TxtModdingToolchainLog != null) TxtModdingToolchainLog.Text = m;
                });
                var (ok, msg) = await ModdingToolchainService.InstallVsCodeAsync(progress);
                if (TxtModdingToolchainLog != null)
                    TxtModdingToolchainLog.Text = msg;
                RefreshModdingToolchainStatus();
                if (!ok)
                    MessageBox.Show(msg, "Install VS Code", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Install VS Code", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnModdingInstallExtensions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TxtModdingToolchainLog != null)
                    TxtModdingToolchainLog.Text = "Installing extensions…";
                var progress = new Progress<string>(m =>
                {
                    if (TxtModdingToolchainLog != null) TxtModdingToolchainLog.Text = m;
                });
                var (ok, msg) = await ModdingToolchainService.InstallExtensionsAsync(progress);
                if (TxtModdingToolchainLog != null)
                    TxtModdingToolchainLog.Text = msg;
                RefreshModdingToolchainStatus();
                MessageBox.Show(msg, "Install extensions", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Install extensions", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnModdingWriteSettings_Click(object sender, RoutedEventArgs e)
        {
            var (ok, msg) = ModdingToolchainService.WriteShellSettings();
            if (TxtModdingToolchainLog != null)
                TxtModdingToolchainLog.Text = msg;
            RefreshModdingToolchainStatus();
            MessageBox.Show(msg, "Write Shell settings", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void BtnModdingOpenShell_Click(object sender, RoutedEventArgs e)
        {
            // Ensure settings point at bundled toolchain before opening.
            ModdingToolchainService.WriteShellSettings();
            var (ok, msg) = ModdingToolchainService.OpenShellInEditor();
            if (TxtModdingToolchainLog != null)
                TxtModdingToolchainLog.Text = msg;
            if (!ok)
                MessageBox.Show(msg, "Open SSF2 Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var prevBuild = GetFilterComboSelection(CmbFilterBuild);
            CmbFilterBuild.Items.Clear();
            foreach (var b in builds)
                CmbFilterBuild.Items.Add(new ComboBoxItem { Content = b });
            RestoreFilterComboSelection(CmbFilterBuild, prevBuild);

            // Version filter removed — handled via Build filter

            // Category filter
            var categories = new List<string> { "All Types" };
            categories.AddRange(mods.Select(m => m.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c));
            var prevCategory = GetFilterComboSelection(CmbFilterCategory);
            CmbFilterCategory.Items.Clear();
            foreach (var c in categories)
                CmbFilterCategory.Items.Add(new ComboBoxItem { Content = c });
            RestoreFilterComboSelection(CmbFilterCategory, prevCategory);

            // Creator filter
            var creators = new List<string> { "All Creators" };
            creators.AddRange(mods.Select(m => m.Author).Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a));
            var prevCreator = GetFilterComboSelection(CmbFilterCreator);
            CmbFilterCreator.Items.Clear();
            foreach (var a in creators)
                CmbFilterCreator.Items.Add(new ComboBoxItem { Content = a });
            RestoreFilterComboSelection(CmbFilterCreator, prevCreator);
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
                        ? FontWeights.Normal : FontWeights.Normal
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

        // ── Guided Mappings editor ───────────────────────────────

        private void SetMappingsInstallRoot(string root)
        {
            var prev = GetMappingsInstallRoot();
            if (FindName("TxtMappingsInstallRoot") is TextBox a)
                a.Text = root;
            if (FindName("TxtMappingsInstallRootActive") is TextBox b)
                b.Text = root;

            if (!string.Equals(prev, root, StringComparison.OrdinalIgnoreCase))
            {
                _mappingsDoc = null;
                _mappingsLoadedRoot = null;
                _menuSymbolIndex = null;
            }
        }

        private string? GetMappingsInstallRoot()
        {
            var active = (FindName("TxtMappingsInstallRootActive") as TextBox)?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(active) && Directory.Exists(active)) return active;
            var typed = (FindName("TxtMappingsInstallRoot") as TextBox)?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(typed) && Directory.Exists(typed)) return typed;
            return ShellWorkspaceService.FindBuildFolder();
        }

        /// <summary>
        /// Load mappings.json + seed the music library as soon as the app starts
        /// (uses the last prepared install). Menu symbol scan stays deferred until Mappings opens.
        /// </summary>
        private async System.Threading.Tasks.Task WarmMappingsAndMusicOnStartupAsync()
        {
            try
            {
                var last = GetShellInstallRoot() ?? _settingsService?.GetPressorLastDirectory();
                if (string.IsNullOrWhiteSpace(last) || !Directory.Exists(last))
                    return;
                var mappingsJson = MappingsEditService.GetPlaintextPath(last);
                if (!File.Exists(mappingsJson) && !GamePrepareService.IsPrepared(last))
                    return;

                SetMappingsInstallRoot(last);
                if (FindName("TxtPressorInstallRoot") is TextBox pressor &&
                    string.IsNullOrWhiteSpace(pressor.Text))
                    pressor.Text = last;

                if (_mappingsDoc == null ||
                    !string.Equals(_mappingsLoadedRoot, last, StringComparison.OrdinalIgnoreCase))
                {
                    _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(last, exportIfMissing: File.Exists(mappingsJson) ? false : true);
                    _mappingsLoadedRoot = last;
                }

                await EnsureMusicCacheAsync(force: false);

                // Prefetch last-played preview so the first mini-player Play is instant.
                var lastId = _musicCache?.LastPlayedSongId;
                if (!string.IsNullOrWhiteSpace(lastId) && _musicCache != null)
                {
                    var song = _musicCache.Songs.FirstOrDefault(s =>
                        s.Id.Equals(lastId, StringComparison.OrdinalIgnoreCase));
                    if (song != null)
                    {
                        await _musicCacheSvc.EnsurePreviewMp3Async(song);
                        _musicCacheSvc.Save(_musicCache);
                    }
                }

                UpdateMiniPlayerUi();
                DebugLogger.Log(
                    $"Startup warm: mappings ready, {_musicCache?.Songs.Count ?? 0} song(s) cached.");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Startup mappings/music warm failed", ex);
            }
        }

        private void SetMappingsStatus(string text)
        {
            if (FindName("TxtMappingsStatus") is TextBlock tb)
                tb.Text = text;
        }

        private void BtnMappingsGoPressor_Click(object sender, RoutedEventArgs e) => BtnPressor_Click(sender, e);

        private void BtnMappingsBrowseInstall_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select prepared SSF2 install (contains SSF2.swf + data/)",
                UseDescriptionForTitle = true,
                SelectedPath = GetMappingsInstallRoot() ?? GetPressorStartDirectory() ?? ""
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            SetMappingsInstallRoot(dlg.SelectedPath);
            RememberPressorDirectory(dlg.SelectedPath);
            _ = RefreshMappingsPageAsync();
        }

        private async System.Threading.Tasks.Task RefreshMappingsPageAsync()
        {
            var root = GetMappingsInstallRoot();
            var prepared = !string.IsNullOrWhiteSpace(root) && GamePrepareService.IsPrepared(root!);
            if (FindName("MappingsNotPreparedPanel") is UIElement notPrep)
                notPrep.Visibility = prepared ? Visibility.Collapsed : Visibility.Visible;
            if (FindName("MappingsEditorPanel") is UIElement editor)
                editor.Visibility = prepared ? Visibility.Visible : Visibility.Collapsed;
            if (!prepared)
            {
                SetMappingsStatus("Prepare the game first (Pressor → Prepare).");
                return;
            }

            try
            {
                SetMappingsStatus("Loading mappings…");
                var needDoc = _mappingsDoc == null ||
                              !string.Equals(_mappingsLoadedRoot, root, StringComparison.OrdinalIgnoreCase);
                if (needDoc)
                {
                    _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(root!, exportIfMissing: true);
                    _mappingsLoadedRoot = root;
                    _menuSymbolIndex = null;
                }

                // Heavy menu SWF scan — only when needed (arrange / validate), and reuse if warm.
                _menuSymbolIndex ??= await _menuSlots.LoadSymbolIndexAsync(root!);

                PopulateGuidedDropdowns();
                // Preserve guided sub-panels (e.g. playMusic) instead of always bouncing to hub.
                ShowMapPanel(string.IsNullOrWhiteSpace(_mapHubSection) ? "hub" : _mapHubSection);
                var chars = MappingsEditService.CountExistingEntries(root!, _mappingsDoc!, "character");
                var stages = MappingsEditService.CountExistingEntries(root!, _mappingsDoc!, "stage");
                var music = MappingsEditService.CountExistingEntries(root!, _mappingsDoc!, "music");
                SetMappingsStatus($"Loaded. Characters={chars}, Stages={stages}, Music={music} (on disk).");

                // Keep music cache in sync if user opened Mappings before startup warm finished.
                if (_musicCache == null || needDoc)
                    _ = EnsureMusicCacheAsync(force: false);
            }
            catch (Exception ex)
            {
                SetMappingsStatus("Error: " + ex.Message);
            }
        }

        private void PopulateGuidedDropdowns()
        {
            if (_mappingsDoc == null) return;
            var root = GetMappingsInstallRoot();
            var stageIds = string.IsNullOrWhiteSpace(root)
                ? new List<string>()
                : MappingsEditService.ListExistingIds(root!, _mappingsDoc, "stage");

            if (FindName("ListMapMusicStages") is ListBox lst)
            {
                lst.Items.Clear();
                foreach (var id in stageIds) lst.Items.Add(id);
            }
            // Stage playlist combo is bound from music cache (CachedStagePlaylist), not raw ids.
        }

        private void ShowMapPanel(string panel)
        {
            if (_mapHubSection == "musicAdd" && panel != "musicAdd")
                StopMusicPreview();

            _mapHubSection = panel;
            void Vis(string name, bool on)
            {
                if (FindName(name) is UIElement el)
                    el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            Vis("MapPanelHub", panel == "hub");
            Vis("MapPanelCategory", panel == "category");
            Vis("MapPanelCharAdd", panel == "charAdd");
            Vis("MapPanelCharUpdate", panel == "charUpdate");
            Vis("MapPanelStageAdd", panel == "stageAdd");
            Vis("MapPanelMusicAdd", panel == "musicAdd");
            Vis("MapPanelArrange", panel == "arrange");
            Vis("MapPanelPlaylists", panel == "playlists");
            Vis("MapPanelPlayMusic", panel == "playMusic");
            if (FindName("BtnMappingsBack") is Button back)
                back.Visibility = panel == "hub" ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OpenCategory(string category)
        {
            _mapCategory = category;
            if (FindName("TxtMapCategoryTitle") is TextBlock title)
            {
                title.Text = category switch
                {
                    "characters" => "Characters",
                    "stages" => "Stages",
                    "music" => "Music",
                    _ => category
                };
            }
            if (FindName("BtnMapCatAdd") is Button add)
            {
                add.Content = category switch
                {
                    "characters" => "Clone Character",
                    "stages" => "Add stage",
                    "music" => "Add song",
                    _ => "Add"
                };
                add.Visibility = Visibility.Visible;
            }
            if (FindName("BtnMapCatEdit") is Button edit)
            {
                edit.Content = "Update Character";
                edit.Visibility = category == "characters" ? Visibility.Visible : Visibility.Collapsed;
            }
            if (FindName("BtnMapCatArrange") is Button arrange)
            {
                arrange.Visibility = category is "characters" or "stages" ? Visibility.Visible : Visibility.Collapsed;
                arrange.Content = category == "stages" ? "Arrange SSS" : "Arrange CSS";
            }
            if (FindName("BtnMapCatPlaylists") is Button playlists)
                playlists.Visibility = category == "music" ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("BtnMapCatPlayMusic") is Button playMusic)
                playMusic.Visibility = category == "music" ? Visibility.Visible : Visibility.Collapsed;
            ShowMapPanel("category");
        }

        private void BtnMappingsBack_Click(object sender, RoutedEventArgs e)
        {
            if ((_mapHubSection is "charAdd" or "arrange") && _mapCategory == "characters")
            {
                OpenCategory("characters");
                return;
            }
            if ((_mapHubSection is "stageAdd" or "arrange") && _mapCategory == "stages")
            {
                OpenCategory("stages");
                return;
            }
            if (_mapHubSection is "musicAdd" or "playlists" or "playMusic")
            {
                OpenCategory("music");
                return;
            }
            if (_mapHubSection == "category")
            {
                ShowMapPanel("hub");
                return;
            }
            ShowMapPanel("hub");
        }

        private void BtnMapHubCharacters_Click(object sender, RoutedEventArgs e) => OpenCategory("characters");
        private void BtnMapHubStages_Click(object sender, RoutedEventArgs e) => OpenCategory("stages");
        private void BtnMapHubMusic_Click(object sender, RoutedEventArgs e) => OpenCategory("music");

        private void BtnMapCatAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_mapCategory == "characters")
            {
                OpenCharacterWizard(CharacterWizardMode.Clone);
            }
            else if (_mapCategory == "stages") ShowMapPanel("stageAdd");
            else if (_mapCategory == "music")
            {
                PopulateGuidedDropdowns();
                ShowMapPanel("musicAdd");
            }
        }

        private void BtnMapCatEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_mapCategory == "characters")
                OpenCharacterUpdatePanel();
        }

        private void BtnMapCatArrange_Click(object sender, RoutedEventArgs e) =>
            OpenArrange(_mapCategory != "stages");

        private void BtnMapCatPlaylists_Click(object sender, RoutedEventArgs e) => OpenPlaylists();
        private void BtnMapCatPlayMusic_Click(object sender, RoutedEventArgs e) => OpenPlayMusic();

        private void OpenArrange(bool css)
        {
            if (_mappingsDoc == null) return;
            _arrangeIsCss = css;
            _arrangeRows = css
                ? MappingsEditService.GetCssRows(_mappingsDoc)
                : MappingsEditService.GetSssNormalRows(_mappingsDoc);
            _selectedArrangeChip = null;
            if (FindName("TxtMapArrangeTitle") is TextBlock t)
                t.Text = css ? "Arrange character select (CSS)" : "Arrange stage select (SSS)";
            var crazyVis = css ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("BtnMapArrangeCrazy") is Button crazyBtn)
            {
                crazyBtn.Visibility = crazyVis;
                crazyBtn.Content = Localization.Get("CrazyCss");
            }
            if (FindName("BtnMapArrangeVanilla") is Button vanillaBtn)
            {
                vanillaBtn.Visibility = crazyVis;
                vanillaBtn.Content = Localization.Get("VanillaCss");
            }
            LoadArrangeLayoutFields();
            RebuildArrangeGrid();
            UpdateArrangeFitHint();
            ShowMapPanel("arrange");
        }

        private void LoadArrangeLayoutFields()
        {
            if (_mappingsDoc == null) return;
            var prefix = _arrangeIsCss ? "character_select_screen" : "stage_select_screen.normal";
            SetTb("TxtMapLayoutPosX", MappingsEditService.GetLayoutNumber(_mappingsDoc, prefix + ".position.x")?.ToString() ?? "55");
            SetTb("TxtMapLayoutPosY", MappingsEditService.GetLayoutNumber(_mappingsDoc, prefix + ".position.y")?.ToString() ?? "80");
            SetTb("TxtMapLayoutIconW", MappingsEditService.GetLayoutNumber(_mappingsDoc, prefix + ".icon_size.width")?.ToString()
                ?? MappingsEditService.GetLayoutNumber(_mappingsDoc, prefix + ".icon_size")?.ToString()
                ?? "42");
            SetTb("TxtMapLayoutIconH", MappingsEditService.GetLayoutNumber(_mappingsDoc, prefix + ".icon_size.height")?.ToString()
                ?? MappingsEditService.GetLayoutNumber(_mappingsDoc, prefix + ".icon_size")?.ToString()
                ?? "42");
        }

        private void SetTb(string name, string value)
        {
            if (FindName(name) is TextBox tb) tb.Text = value;
        }

        private string GetTb(string name) => (FindName(name) as TextBox)?.Text?.Trim() ?? "";

        private void ApplyArrangeLayoutFields()
        {
            if (_mappingsDoc == null) return;
            var prefix = _arrangeIsCss ? "character_select_screen" : "stage_select_screen.normal";
            if (double.TryParse(GetTb("TxtMapLayoutPosX"), out var x))
                MappingsEditService.SetLayoutNumber(_mappingsDoc, prefix + ".position.x", x);
            if (double.TryParse(GetTb("TxtMapLayoutPosY"), out var y))
                MappingsEditService.SetLayoutNumber(_mappingsDoc, prefix + ".position.y", y);
            if (double.TryParse(GetTb("TxtMapLayoutIconW"), out var iw))
                MappingsEditService.SetLayoutNumber(_mappingsDoc, prefix + ".icon_size.width", iw);
            if (double.TryParse(GetTb("TxtMapLayoutIconH"), out var ih))
                MappingsEditService.SetLayoutNumber(_mappingsDoc, prefix + ".icon_size.height", ih);
        }

        private void RebuildArrangeGrid()
        {
            if (FindName("ArrangeRowsHost") is not StackPanel host) return;
            host.Children.Clear();

            Brush surface;
            Brush text;
            Brush border;
            Brush accent;
            try { surface = (Brush)FindResource("SurfaceBrush"); }
            catch { surface = Brushes.DimGray; }
            try { text = (Brush)FindResource("TextPrimaryBrush"); }
            catch { text = Brushes.White; }
            try { border = (Brush)FindResource("BorderBrush"); }
            catch { border = Brushes.Gray; }
            try { accent = (Brush)FindResource("AccentBrush"); }
            catch { accent = Brushes.Orange; }

            for (int r = 0; r < _arrangeRows.Count; r++)
            {
                var rr = r;
                var rowHost = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
                rowHost.Children.Add(new TextBlock
                {
                    Text = $"R{r + 1}",
                    Width = 28,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextSecondaryBrush")
                });
                DockPanel.SetDock(rowHost.Children[^1], Dock.Left);

                // Wrap chips so narrow windows don't force a horizontal clip.
                var rowPanel = new WrapPanel
                {
                    AllowDrop = true,
                    MinHeight = 34,
                    Background = Brushes.Transparent,
                    Tag = rr
                };
                rowPanel.DragOver += ArrangeRow_DragOver;
                rowPanel.Drop += (_, e) => ArrangeChipDrop(e, rr, append: true);

                for (int c = 0; c < _arrangeRows[r].Count; c++)
                {
                    var id = _arrangeRows[r][c];
                    var cc = c;
                    var selected = _selectedArrangeChip is { } sel && sel.Row == rr && sel.Col == cc;
                    var chip = new Border
                    {
                        Background = surface,
                        BorderBrush = selected ? accent : border,
                        BorderThickness = new Thickness(selected ? 2 : 1),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 5, 8, 5),
                        Margin = new Thickness(2),
                        MinWidth = 72,
                        Cursor = System.Windows.Input.Cursors.SizeAll,
                        AllowDrop = true,
                        Tag = (rr, cc),
                        Child = new TextBlock
                        {
                            Text = id,
                            Foreground = text,
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };

                    chip.PreviewMouseLeftButtonDown += (_, e) =>
                    {
                        _arrangeDragStart = e.GetPosition(null);
                        _arrangeDragFrom = (rr, cc);
                        _arrangeDidDrag = false;
                        _selectedArrangeChip = (rr, cc);
                    };
                    chip.PreviewMouseMove += (s, e) =>
                    {
                        if (e.LeftButton != MouseButtonState.Pressed ||
                            _arrangeDragFrom is not { } from ||
                            _arrangeDragStart is not { } start)
                            return;
                        var pos = e.GetPosition(null);
                        if (Math.Abs(pos.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                            Math.Abs(pos.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                            return;
                        _arrangeDidDrag = true;
                        var data = new System.Windows.DataObject(ArrangeChipDataFormat, from);
                        DragDrop.DoDragDrop((DependencyObject)s, data, System.Windows.DragDropEffects.Move);
                        _arrangeDragStart = null;
                        _arrangeDragFrom = null;
                    };
                    chip.PreviewMouseLeftButtonUp += (_, _) =>
                    {
                        if (!_arrangeDidDrag)
                            RebuildArrangeGrid(); // refresh selection border
                        _arrangeDragStart = null;
                        _arrangeDragFrom = null;
                        _arrangeDidDrag = false;
                    };
                    chip.DragOver += ArrangeRow_DragOver;
                    chip.Drop += (_, e) => ArrangeChipDrop(e, rr, append: false, insertCol: cc);

                    rowPanel.Children.Add(chip);
                }

                rowHost.Children.Add(rowPanel);
                host.Children.Add(rowHost);
            }

            UpdateArrangeFitHint();
        }

        private void UpdateArrangeFitHint()
        {
            if (FindName("TxtMapArrangeFitHint") is not TextBlock hint) return;
            var rowCount = _arrangeRows.Count(r => r.Count > 0);
            var maxCols = rowCount == 0 ? 0 : _arrangeRows.Max(r => r.Count);
            if (maxCols <= 0)
            {
                hint.Text = "";
                return;
            }

            var suggest = MappingsEditService.SuggestIconSize(maxCols, rowCount, _arrangeIsCss);
            if (_arrangeIsCss)
            {
                var tip = maxCols >= MappingsEditService.CssMaxColsPerRow
                    ? $" at max ~{MappingsEditService.CssMaxColsPerRow}/row"
                    : suggest <= 14
                        ? $" (max ~{MappingsEditService.CssMaxColsPerRow}/row, {MappingsEditService.CssMaxRows} rows)"
                        : $" — max {MappingsEditService.CssMaxRows} rows above HUD";
                hint.Text =
                    $"Grid {maxCols}×{rowCount} — save fits cell ~{suggest:0}, auto X{tip}.";
            }
            else
            {
                hint.Text =
                    $"Grid {maxCols}×{rowCount} — save fits cell ~{suggest:0} (fills chip area; art scales with spacing).";
            }
        }

        private static void ArrangeRow_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(ArrangeChipDataFormat))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
                e.Handled = true;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        }

        private void ArrangeChipDrop(System.Windows.DragEventArgs e, int toRow, bool append, int insertCol = 0)
        {
            if (!e.Data.GetDataPresent(ArrangeChipDataFormat)) return;
            if (e.Data.GetData(ArrangeChipDataFormat) is not ValueTuple<int, int> from) return;
            e.Handled = true;

            if (toRow < 0 || toRow >= _arrangeRows.Count) return;
            var toCol = append ? _arrangeRows[toRow].Count : insertCol;

            // Drop on right half of a chip → insert after it.
            if (!append && e.Source is Border chip && chip.ActualWidth > 0)
            {
                var x = e.GetPosition(chip).X;
                if (x > chip.ActualWidth * 0.5)
                    toCol = insertCol + 1;
            }

            MoveArrangeChip(from, toRow, toCol);
        }

        private void MoveArrangeChip((int Row, int Col) from, int toRow, int toCol)
        {
            if (from.Row < 0 || from.Row >= _arrangeRows.Count) return;
            if (from.Col < 0 || from.Col >= _arrangeRows[from.Row].Count) return;
            toRow = Math.Clamp(toRow, 0, Math.Max(0, _arrangeRows.Count - 1));
            if (_arrangeRows.Count == 0) return;

            var id = _arrangeRows[from.Row][from.Col];
            if (from.Row == toRow && toCol > from.Col) toCol--;
            if (from.Row == toRow && toCol == from.Col)
            {
                _selectedArrangeChip = from;
                RebuildArrangeGrid();
                return;
            }

            _arrangeRows[from.Row].RemoveAt(from.Col);
            if (toRow >= _arrangeRows.Count) toRow = _arrangeRows.Count - 1;
            if (toRow < 0) toRow = 0;
            toCol = Math.Clamp(toCol, 0, _arrangeRows[toRow].Count);
            _arrangeRows[toRow].Insert(toCol, id);

            // Drop empty rows (keep at least one).
            for (var i = _arrangeRows.Count - 1; i >= 0; i--)
            {
                if (_arrangeRows[i].Count == 0 && _arrangeRows.Count > 1)
                    _arrangeRows.RemoveAt(i);
            }

            // Reselect moved chip.
            _selectedArrangeChip = null;
            for (var r = 0; r < _arrangeRows.Count; r++)
            {
                var c = _arrangeRows[r].IndexOf(id);
                if (c >= 0)
                {
                    _selectedArrangeChip = (r, c);
                    break;
                }
            }
            RebuildArrangeGrid();
        }

        private List<string> GetArrangeCandidateIds()
        {
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = GetMappingsInstallRoot();
            if (_mappingsDoc != null && !string.IsNullOrWhiteSpace(root))
            {
                var category = _arrangeIsCss ? "character" : "stage";
                foreach (var id in MappingsEditService.ListExistingIds(root!, _mappingsDoc, category))
                    ids.Add(id);
            }

            if (_arrangeIsCss)
            {
                ids.Add("random");
                ids.Add("cxp");
            }

            foreach (var row in _arrangeRows)
            foreach (var id in row)
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);

            return ids.ToList();
        }

        /// <summary>
        /// Picker with dropdown of available ids. Returns null if cancelled.
        /// Empty string when <paramref name="allowEmpty"/> and user leaves blank.
        /// </summary>
        private string? PromptForArrangeId(string title, string prompt, bool allowEmpty)
        {
            var candidates = GetArrangeCandidateIds();
            var onRoster = new HashSet<string>(
                _arrangeRows.SelectMany(r => r),
                StringComparer.OrdinalIgnoreCase);

            var win = new Window
            {
                Title = title,
                Width = 420,
                Height = 200,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush)FindResource("CardBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            };

            var combo = new System.Windows.Controls.ComboBox
            {
                Margin = new Thickness(12, 8, 12, 8),
                IsEditable = true,
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true,
                Style = TryFindResource("DarkComboBox") as Style
                    ?? TryFindResource("ModernComboBox") as Style
            };
            foreach (var id in candidates)
            {
                var label = onRoster.Contains(id) ? $"{id}  (on roster — will move)" : id;
                combo.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = label,
                    Tag = id
                });
            }

            var ok = new Button
            {
                Content = "OK",
                IsDefault = true,
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                Style = TryFindResource("ModernButton") as Style
            };
            var cancel = new Button
            {
                Content = "Cancel",
                IsCancel = true,
                Width = 80,
                Style = TryFindResource("ModernButton") as Style
            };
            string? result = null;
            ok.Click += (_, _) =>
            {
                if (combo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
                    result = tag;
                else
                    result = combo.Text?.Trim();
                // Strip display suffix if user typed/kept the label.
                if (!string.IsNullOrEmpty(result))
                {
                    var cut = result.IndexOf("  (on roster", StringComparison.Ordinal);
                    if (cut >= 0) result = result[..cut].Trim();
                }
                win.DialogResult = true;
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var root = new DockPanel { Background = (Brush)FindResource("CardBrush") };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(new StackPanel
            {
                Margin = new Thickness(12, 12, 12, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = prompt,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("TextPrimaryBrush"),
                        Margin = new Thickness(0, 0, 0, 6)
                    },
                    new TextBlock
                    {
                        Text = "Each id can appear only once. Picking one already on the roster moves it.",
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 6)
                    },
                    combo
                }
            });
            win.Content = root;
            win.Loaded += (_, _) =>
            {
                combo.Focus();
                if (combo.IsEditable)
                    combo.ApplyTemplate();
            };
            if (win.ShowDialog() != true) return null;
            if (string.IsNullOrWhiteSpace(result))
                return allowEmpty ? "" : null;
            return MenuSlotService.SanitizeId(result);
        }

        private void BtnMapArrangeInsert_Click(object sender, RoutedEventArgs e)
        {
            var id = PromptForArrangeId(
                "Insert",
                "Pick an id to insert after the selection (or at the end of the last row):",
                allowEmpty: false);
            if (string.IsNullOrWhiteSpace(id)) return;

            string? afterId = null;
            if (_selectedArrangeChip is { } sel &&
                sel.Row >= 0 && sel.Row < _arrangeRows.Count &&
                sel.Col >= 0 && sel.Col < _arrangeRows[sel.Row].Count)
            {
                afterId = _arrangeRows[sel.Row][sel.Col];
            }

            MappingsEditService.InsertAfterId(_arrangeRows, afterId, id);

            _selectedArrangeChip = null;
            for (var r = 0; r < _arrangeRows.Count; r++)
            {
                var c = _arrangeRows[r].FindIndex(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (c < 0) continue;
                _selectedArrangeChip = (r, c);
                break;
            }
            RebuildArrangeGrid();
        }

        private void BtnMapArrangeNewRow_Click(object sender, RoutedEventArgs e)
        {
            if (_arrangeIsCss)
            {
                var nonEmpty = _arrangeRows.Count(r => r.Count > 0);
                if (nonEmpty >= MappingsEditService.CssMaxRows)
                {
                    MessageBox.Show(
                        $"CSS allows at most {MappingsEditService.CssMaxRows} rows so icons stay above the player boxes.\n" +
                        "Move chips into existing rows instead.",
                        "New row",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            var id = PromptForArrangeId(
                "New row",
                "Optional id for the new row (leave blank for an empty row):",
                allowEmpty: true);
            if (id == null) return; // cancelled

            if (string.IsNullOrWhiteSpace(id))
            {
                _arrangeRows.Add(new List<string>());
            }
            else
            {
                MappingsEditService.RemoveIdFromRows(_arrangeRows, id);
                _arrangeRows.Add(new List<string> { id });
                _selectedArrangeChip = (_arrangeRows.Count - 1, 0);
            }
            RebuildArrangeGrid();
        }

        private void BtnMapArrangeRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedArrangeChip is not { } sel) return;
            var id = _arrangeRows[sel.Row][sel.Col];
            if (MessageBox.Show($"Remove '{id}' from the select screen?", "Arrange",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _arrangeRows[sel.Row].RemoveAt(sel.Col);
            if (_arrangeRows[sel.Row].Count == 0 && _arrangeRows.Count > 1)
                _arrangeRows.RemoveAt(sel.Row);
            _selectedArrangeChip = null;
            RebuildArrangeGrid();
        }

        private async void BtnMapArrangeDone_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            try
            {
                ApplyArrangeLayoutFields();
                MappingsEditService.EnsureUniqueRowIds(_arrangeRows);
                if (_arrangeIsCss) MappingsEditService.SetCssRows(_mappingsDoc, _arrangeRows);
                else MappingsEditService.SetSssNormalRows(_mappingsDoc, _arrangeRows);
                var fitMsg = MappingsEditService.FitSelectLayout(_mappingsDoc, _arrangeIsCss, _arrangeRows);
                LoadArrangeLayoutFields(); // show fitted icon/position numbers
                UpdateArrangeFitHint();
                SetMappingsStatus("Saving + applying…");
                await _mappingsEdit.CommitAsync(root!, _mappingsDoc, null, new Progress<string>(SetMappingsStatus));
                _menuSymbolIndex = await _menuSlots.LoadSymbolIndexAsync(root!);
                SetMappingsStatus("Select screen updated." + (string.IsNullOrEmpty(fitMsg) ? "" : " " + fitMsg));
                MessageBox.Show("Saved and applied to mappings.swf." + (string.IsNullOrEmpty(fitMsg) ? "" : "\n\n" + fitMsg),
                    "Arrange", MessageBoxButton.OK, MessageBoxImage.Information);
                BtnMappingsBack_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Arrange", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<CrazyCssDesign?> PromptCrazyCssDesignerAsync()
        {
            var root = GetMappingsInstallRoot();
            if (_mappingsDoc == null || string.IsNullOrWhiteSpace(root)) return null;

            var candidates = MappingsEditService.ListExistingIds(root!, _mappingsDoc, "character")
                .Where(id => !CrazyCssService.IsCrazyAliasId(id)
                             && !id.Equals("random", StringComparison.OrdinalIgnoreCase)
                             && !id.Equals("cxp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                MessageBox.Show(Localization.Get("CrazyCssNoCharacters"), Localization.Get("CrazyCss"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            SetMappingsStatus("Loading CSS chip list for Crazy CSS designer…");
            _menuSymbolIndex ??= await _menuSlots.LoadSymbolIndexAsync(root!);
            var artCapable = candidates
                .Where(id => _menuSymbolIndex!.CharacterSelect.Contains(id))
                .ToList();
            if (artCapable.Count == 0)
            {
                MessageBox.Show(Localization.Get("CrazyCssNoArt"), Localization.Get("CrazyCss"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            string DisplayName(string id)
            {
                var name = _mappingsDoc!["character"]?[id]?["name"]?.ToString();
                return string.IsNullOrWhiteSpace(name) ? id : name!;
            }

            var win = new CrazyCssDesignerWindow(
                candidates,
                DisplayName,
                artCapable)
            {
                Owner = this,
                Title = Localization.Get("CrazyCss")
            };
            return win.ShowDialog() == true ? win.ResultDesign : null;
        }

        private async void BtnMapArrangeCrazy_Click(object sender, RoutedEventArgs e)
        {
            if (!_arrangeIsCss) return;
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;

            CrazyCssDesign? design;
            try
            {
                design = await PromptCrazyCssDesignerAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Localization.Get("CrazyCss"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (design == null) return;

            try
            {
                if (FindName("BtnMapArrangeCrazy") is Button b1) b1.IsEnabled = false;
                if (FindName("BtnMapArrangeVanilla") is Button b2) b2.IsEnabled = false;

                // Shared playable IDs across many chips need the roster-hash engine patch.
                if (GamePrepareService.IsPrepared(root!))
                {
                    SetMappingsStatus("Ensuring CSS roster-hash engine patch…");
                    var prep = new GamePrepareService();
                    await prep.PatchCssRosterHashOnInstallAsync(
                        root!, null, new Progress<string>(SetMappingsStatus));
                }

                SetMappingsStatus("Crazy CSS in progress…");
                await CrazyCss.ApplyDesignAsync(
                    root!, _mappingsDoc, design, null, new Progress<string>(SetMappingsStatus));
                _arrangeRows = MappingsEditService.GetCssRows(_mappingsDoc);
                _selectedArrangeChip = null;
                LoadArrangeLayoutFields();
                RebuildArrangeGrid();
                _menuSymbolIndex = await _menuSlots.LoadSymbolIndexAsync(root!);
                MessageBox.Show(Localization.Get("CrazyCssDone"), Localization.Get("CrazyCss"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Localization.Get("CrazyCss"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (FindName("BtnMapArrangeCrazy") is Button b1) b1.IsEnabled = true;
                if (FindName("BtnMapArrangeVanilla") is Button b2) b2.IsEnabled = true;
            }
        }

        private async void BtnMapArrangeVanilla_Click(object sender, RoutedEventArgs e)
        {
            if (!_arrangeIsCss) return;
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;

            if (CrazyCssService.LoadState(root!)?.VanillaRows is not { Count: > 0 })
            {
                MessageBox.Show(Localization.Get("VanillaCssMissing"), Localization.Get("VanillaCss"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                    Localization.Get("VanillaCssConfirm"),
                    Localization.Get("VanillaCss"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                if (FindName("BtnMapArrangeCrazy") is Button b1) b1.IsEnabled = false;
                if (FindName("BtnMapArrangeVanilla") is Button b2) b2.IsEnabled = false;
                SetMappingsStatus("Restoring vanilla CSS…");
                await CrazyCss.RestoreVanillaAsync(
                    root!, _mappingsDoc, null, new Progress<string>(SetMappingsStatus));
                _arrangeRows = MappingsEditService.GetCssRows(_mappingsDoc);
                _selectedArrangeChip = null;
                LoadArrangeLayoutFields();
                RebuildArrangeGrid();
                _menuSymbolIndex = await _menuSlots.LoadSymbolIndexAsync(root!);
                MessageBox.Show(Localization.Get("VanillaCssDone"), Localization.Get("VanillaCss"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Localization.Get("VanillaCss"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (FindName("BtnMapArrangeCrazy") is Button b1) b1.IsEnabled = true;
                if (FindName("BtnMapArrangeVanilla") is Button b2) b2.IsEnabled = true;
            }
        }

        private async void OpenPlaylists()
        {
            ShowMapPanel("playlists");
            await EnsureMusicCacheAsync(force: false);
            BindPlaylistStageCombo();
            BindSongAddCombo();
            SetMappingsStatus(_musicCacheRunning
                ? "Cache still building in background — available songs shown now."
                : "Pick a stage to edit its playlist.");
            if (FindName("CmbMapPlaylistStage") is ComboBox cmb && cmb.SelectedItem != null)
                LoadPlaylistEditorFromCache();
        }

        private async void OpenPlayMusic()
        {
            ShowMapPanel("playMusic");
            await EnsureMusicCacheAsync(force: false);
            BindPlayMusicLibrary();
            BindUserPlaylistsCombo();
        }

        private async System.Threading.Tasks.Task EnsureMusicCacheAsync(bool force)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;

            // Always seed UI immediately from mappings / existing cache.
            var existing = _musicCacheSvc.Load(root!);
            _musicCache = _musicCacheSvc.SeedFromMappings(root!, _mappingsDoc!, existing);
            BindSongAddCombo();
            BindPlayMusicLibrary();
            BindPlaylistStageCombo();
            BindUserPlaylistsCombo();

            var needsBuild = force || existing == null || !existing.EnrichmentComplete ||
                             existing.Songs.Count == 0 ||
                             existing.SchemaVersion < MusicLibraryCache.CurrentSchema;

            if (!needsBuild)
            {
                _musicCache = existing ?? _musicCache;
                SetMusicCacheUi(null);
                return;
            }

            if (_musicCacheRunning && !force) return;

            _musicCacheCts?.Cancel();
            _musicCacheCts = new CancellationTokenSource();
            var ct = _musicCacheCts.Token;
            var gen = Interlocked.Increment(ref _musicCacheGen);
            _musicCacheRunning = true;

            var progress = new Progress<MusicCacheProgress>(p =>
            {
                if (gen != _musicCacheGen) return;
                if (p.Snapshot != null)
                {
                    // Preserve user playlists / last-played from live cache while swapping song/stage data.
                    var userPl = _musicCache?.UserPlaylists;
                    var lastSong = _musicCache?.LastPlayedSongId;
                    var lastQ = _musicCache?.LastPlayedQueueIds;
                    _musicCache = p.Snapshot;
                    if (userPl != null) _musicCache.UserPlaylists = userPl;
                    if (lastSong != null) _musicCache.LastPlayedSongId = lastSong;
                    if (lastQ != null) _musicCache.LastPlayedQueueIds = lastQ;
                }
                SetMusicCacheUi(p);
                BindSongAddCombo();
                BindPlayMusicLibrary();
                BindPlaylistStageCombo();
                if (_mapHubSection == "playlists")
                    LoadPlaylistEditorFromCache();
            });

            try
            {
                var built = await System.Threading.Tasks.Task.Run(
                    () => _musicCacheSvc.BuildOrRefreshAsync(root!, _mappingsDoc!, forceFull: force, progress, ct),
                    ct);
                if (gen != _musicCacheGen) return;
                _musicCache = built;
                BindSongAddCombo();
                BindPlayMusicLibrary();
                BindPlaylistStageCombo();
                BindUserPlaylistsCombo();
                SetMappingsStatus($"Music cache ready — {_musicCache.Songs.Count} songs.");
            }
            catch (OperationCanceledException) { /* superseded */ }
            catch (Exception ex)
            {
                SetMappingsStatus("Music cache error: " + ex.Message);
            }
            finally
            {
                if (gen == _musicCacheGen)
                {
                    _musicCacheRunning = false;
                    SetMusicCacheUi(null);
                }
            }
        }

        private void SetMusicCacheUi(MusicCacheProgress? p)
        {
            var show = _musicCacheRunning;
            var pct = p?.Percent ?? 0;
            var label = p == null
                ? "Caching…"
                : (p.Value.Total > 0 ? $"{p.Value.Done}/{p.Value.Total}" : p.Value.Message);

            if (FindName("MiniCacheProgress") is ProgressBar miniPb)
            {
                miniPb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                miniPb.IsIndeterminate = show && (p == null || p.Value.Total <= 0);
                if (!miniPb.IsIndeterminate) miniPb.Value = pct;
            }
            if (FindName("TxtMiniCacheStatus") is TextBlock miniTxt)
            {
                miniTxt.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                miniTxt.Text = show ? $"Caching {label}" : "";
            }
            if (FindName("PlayMusicCacheStatusPanel") is FrameworkElement panel)
                panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("PlayMusicCacheProgress") is ProgressBar pb)
            {
                pb.IsIndeterminate = show && (p == null || p.Value.Total <= 0);
                if (!pb.IsIndeterminate) pb.Value = pct;
            }
            if (FindName("TxtPlayMusicCacheStatus") is TextBlock st)
            {
                st.Text = !show ? "" :
                    p == null ? "Caching…" :
                    p.Value.Total > 0 ? $"{p.Value.Done}/{p.Value.Total}" : p.Value.Message;
            }
        }

        private async void BtnMapMusicCacheRefresh_Click(object sender, RoutedEventArgs e) =>
            await EnsureMusicCacheAsync(force: true);

        private void BindPlaylistStageCombo()
        {
            if (FindName("CmbMapPlaylistStage") is not ComboBox cmb) return;
            var prev = (cmb.SelectedItem as CachedStagePlaylist)?.StageId;
            cmb.ItemsSource = null;
            cmb.ItemsSource = _musicCache?.StagePlaylists ?? new List<CachedStagePlaylist>();
            if (!string.IsNullOrEmpty(prev))
            {
                var hit = _musicCache?.StagePlaylists.FirstOrDefault(s =>
                    s.StageId.Equals(prev, StringComparison.OrdinalIgnoreCase));
                if (hit != null) cmb.SelectedItem = hit;
            }
            if (cmb.SelectedItem == null && cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private void BindSongAddCombo()
        {
            if (FindName("CmbMapPlaylistAddSong") is ComboBox cmb)
                cmb.ItemsSource = _musicCache?.Songs ?? new List<CachedSongInfo>();
        }

        private void BindPlayMusicLibrary()
        {
            EnsurePlayMusicFilterCombos();
            ApplyPlayMusicFilters();
        }

        private void EnsurePlayMusicFilterCombos()
        {
            if (_musicCache == null) return;

            if (FindName("CmbPlayMusicArtist") is ComboBox art)
            {
                var prev = art.SelectedItem as string;
                var artists = _musicCache.Songs
                    .Select(s => string.IsNullOrWhiteSpace(s.Artist) ? "(Unknown artist)" : s.Artist.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                artists.Insert(0, "All artists");
                _playMusicFiltersReady = false;
                art.ItemsSource = artists;
                art.SelectedItem = prev != null && artists.Contains(prev) ? prev : "All artists";
            }

            if (FindName("CmbPlayMusicStage") is ComboBox stg)
            {
                var prevId = (stg.SelectedItem as CachedStagePlaylist)?.StageId;
                var stages = new List<CachedStagePlaylist>
                {
                    new() { StageId = "", StageName = "All stages" }
                };
                stages.AddRange(_musicCache.StagePlaylists);
                _playMusicFiltersReady = false;
                stg.ItemsSource = stages;
                stg.SelectedItem = stages.FirstOrDefault(s => s.StageId.Equals(prevId ?? "", StringComparison.OrdinalIgnoreCase))
                                   ?? stages[0];
            }

            _playMusicFiltersReady = true;
        }

        private void PlayMusicFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_playMusicFiltersReady) return;

            // Only one filter mode at a time — whichever control changed last wins.
            if (ReferenceEquals(sender, FindName("TxtPlayMusicSearch")))
            {
                var q = (FindName("TxtPlayMusicSearch") as TextBox)?.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(q))
                {
                    if (_playMusicFilterKind == PlayMusicFilterKind.Search)
                        _playMusicFilterKind = PlayMusicFilterKind.None;
                }
                else
                {
                    _playMusicFilterKind = PlayMusicFilterKind.Search;
                    ResetPlayMusicFilterControlsExcept(PlayMusicFilterKind.Search);
                }
            }
            else if (ReferenceEquals(sender, FindName("CmbPlayMusicArtist")))
            {
                var artist = (FindName("CmbPlayMusicArtist") as ComboBox)?.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(artist) ||
                    artist.Equals("All artists", StringComparison.OrdinalIgnoreCase))
                {
                    if (_playMusicFilterKind == PlayMusicFilterKind.Artist)
                        _playMusicFilterKind = PlayMusicFilterKind.None;
                }
                else
                {
                    _playMusicFilterKind = PlayMusicFilterKind.Artist;
                    ResetPlayMusicFilterControlsExcept(PlayMusicFilterKind.Artist);
                }
            }
            else if (ReferenceEquals(sender, FindName("CmbPlayMusicStage")))
            {
                var stage = (FindName("CmbPlayMusicStage") as ComboBox)?.SelectedItem as CachedStagePlaylist;
                if (stage == null || string.IsNullOrWhiteSpace(stage.StageId))
                {
                    if (_playMusicFilterKind == PlayMusicFilterKind.Stage)
                        _playMusicFilterKind = PlayMusicFilterKind.None;
                }
                else
                {
                    _playMusicFilterKind = PlayMusicFilterKind.Stage;
                    ResetPlayMusicFilterControlsExcept(PlayMusicFilterKind.Stage);
                }
            }

            ApplyPlayMusicFilters();
        }

        private void ResetPlayMusicFilterControlsExcept(PlayMusicFilterKind keep)
        {
            _playMusicFiltersReady = false;
            try
            {
                if (keep != PlayMusicFilterKind.Search && FindName("TxtPlayMusicSearch") is TextBox tb)
                    tb.Text = "";
                if (keep != PlayMusicFilterKind.Artist && FindName("CmbPlayMusicArtist") is ComboBox art &&
                    art.Items.Count > 0)
                    art.SelectedIndex = 0;
                if (keep != PlayMusicFilterKind.Stage && FindName("CmbPlayMusicStage") is ComboBox stg &&
                    stg.Items.Count > 0)
                    stg.SelectedIndex = 0;
            }
            finally
            {
                _playMusicFiltersReady = true;
            }
        }

        private void ApplyPlayMusicFilters()
        {
            if (FindName("LstPlayMusicLibrary") is not ListBox lst) return;
            var songs = _musicCache?.Songs ?? new List<CachedSongInfo>();
            IEnumerable<CachedSongInfo> filtered = songs;

            switch (_playMusicFilterKind)
            {
                case PlayMusicFilterKind.Search:
                {
                    var q = (FindName("TxtPlayMusicSearch") as TextBox)?.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        filtered = filtered.Where(s =>
                            s.DisplayLabel.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            s.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase));
                    }
                    break;
                }
                case PlayMusicFilterKind.Artist:
                {
                    var artist = (FindName("CmbPlayMusicArtist") as ComboBox)?.SelectedItem as string;
                    if (!string.IsNullOrWhiteSpace(artist) &&
                        !artist.Equals("All artists", StringComparison.OrdinalIgnoreCase))
                    {
                        if (artist.Equals("(Unknown artist)", StringComparison.OrdinalIgnoreCase))
                            filtered = filtered.Where(s => string.IsNullOrWhiteSpace(s.Artist));
                        else
                            filtered = filtered.Where(s => s.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase));
                    }
                    break;
                }
                case PlayMusicFilterKind.Stage:
                {
                    var stage = (FindName("CmbPlayMusicStage") as ComboBox)?.SelectedItem as CachedStagePlaylist;
                    if (stage != null && !string.IsNullOrWhiteSpace(stage.StageId))
                    {
                        var ids = new HashSet<string>(stage.TrackIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                        filtered = filtered.Where(s => ids.Contains(s.Id));
                    }
                    break;
                }
            }

            _filteredLibrarySongs = filtered.ToList();
            lst.ItemsSource = _filteredLibrarySongs;
        }

        private void BindUserPlaylistsCombo()
        {
            if (FindName("CmbUserPlaylists") is not ComboBox cmb || _musicCache == null) return;
            cmb.ItemsSource = null;
            cmb.ItemsSource = _musicCache.UserPlaylists;
            if (cmb.Items.Count > 0 && cmb.SelectedItem == null) cmb.SelectedIndex = 0;
            BindUserPlaylistTracks();
        }

        private void BindUserPlaylistTracks()
        {
            if (FindName("LstUserPlaylistTracks") is not ListBox lst || _musicCache == null) return;
            var pl = (FindName("CmbUserPlaylists") as ComboBox)?.SelectedItem as UserMusicPlaylist;
            if (pl == null) { lst.ItemsSource = null; return; }
            lst.ItemsSource = pl.TrackIds
                .Select(id => _musicCache.Songs.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                              ?? new CachedSongInfo { Id = id, Name = id })
                .ToList();
        }

        private void CmbMapPlaylistStage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mapHubSection != "playlists") return;
            LoadPlaylistEditorFromCache();
        }

        private void LoadPlaylistEditorFromCache()
        {
            _playlistEditorTracks.Clear();
            if (FindName("CmbMapPlaylistStage") is not ComboBox cmb ||
                cmb.SelectedItem is not CachedStagePlaylist stage ||
                _musicCache == null)
            {
                if (FindName("LstMapPlaylistTracks") is ListBox empty) empty.ItemsSource = null;
                return;
            }

            foreach (var id in stage.TrackIds)
            {
                var song = _musicCache.Songs.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                           ?? new CachedSongInfo { Id = id, Name = id };
                _playlistEditorTracks.Add(song);
            }
            if (FindName("LstMapPlaylistTracks") is ListBox lst)
            {
                lst.ItemsSource = null;
                lst.ItemsSource = _playlistEditorTracks.ToList();
            }
            SetMappingsStatus($"{stage.StageName}: {_playlistEditorTracks.Count} track(s).");
        }

        private void BtnMapPlaylistAddTrack_Click(object sender, RoutedEventArgs e)
        {
            if ((FindName("CmbMapPlaylistAddSong") as ComboBox)?.SelectedItem is not CachedSongInfo song) return;
            if (_playlistEditorTracks.Any(t => t.Id.Equals(song.Id, StringComparison.OrdinalIgnoreCase))) return;
            _playlistEditorTracks.Add(song);
            if (FindName("LstMapPlaylistTracks") is ListBox lst)
            {
                lst.ItemsSource = null;
                lst.ItemsSource = _playlistEditorTracks.ToList();
            }
        }

        private void BtnMapPlaylistRemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            if ((FindName("LstMapPlaylistTracks") as ListBox)?.SelectedItem is not CachedSongInfo song) return;
            _playlistEditorTracks.RemoveAll(t => t.Id.Equals(song.Id, StringComparison.OrdinalIgnoreCase));
            if (FindName("LstMapPlaylistTracks") is ListBox lst)
            {
                lst.ItemsSource = null;
                lst.ItemsSource = _playlistEditorTracks.ToList();
            }
        }

        private async void BtnMapPlaylistPreview_Click(object sender, RoutedEventArgs e)
        {
            if ((FindName("LstMapPlaylistTracks") as ListBox)?.SelectedItem is not CachedSongInfo song) return;
            await PreviewCachedSongAsync(song);
        }

        private async void BtnMapPlaylistPreviewAddSong_Click(object sender, RoutedEventArgs e)
        {
            if ((FindName("CmbMapPlaylistAddSong") as ComboBox)?.SelectedItem is not CachedSongInfo song) return;
            await PreviewCachedSongAsync(song);
        }

        private async void BtnMapPlaylistSave_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            if ((FindName("CmbMapPlaylistStage") as ComboBox)?.SelectedItem is not CachedStagePlaylist stage)
                return;
            var swfPath = MusicEditService.ResolveStageSwfPath(root!, _mappingsDoc, stage.StageId);
            if (string.IsNullOrWhiteSpace(swfPath))
            {
                MessageBox.Show("Stage SWF path not found.", "Playlists", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var tracks = _playlistEditorTracks.Select(t => t.Id).ToList();
            try
            {
                await RunBusyWorkAsync("Saving stage playlist", async progress =>
                {
                    progress.Report(("Writing playlist into stage SWF…", 40, stage.StageId));
                    await _musicEdit.WritePlaylistAsync(swfPath!, tracks, null,
                        new Progress<string>(m => progress.Report((m, 70, null))));
                    stage.TrackIds = tracks.ToList();
                    if (_musicCache != null)
                    {
                        var cached = _musicCache.StagePlaylists.FirstOrDefault(s =>
                            s.StageId.Equals(stage.StageId, StringComparison.OrdinalIgnoreCase));
                        if (cached != null) cached.TrackIds = tracks.ToList();
                        _musicCacheSvc.Save(_musicCache);
                    }
                    progress.Report(("Done.", 100, null));
                });
                SetMappingsStatus($"Playlist saved for {stage.StageName}.");
                MessageBox.Show($"Playlist saved to {Path.GetFileName(swfPath)}.", "Playlists",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Playlists", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task PreviewCachedSongAsync(CachedSongInfo song)
        {
            try { await PlayCachedSongAsync(song, _filteredLibrarySongs.Count > 0 ? _filteredLibrarySongs : new List<CachedSongInfo> { song }); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Preview", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task PlayCachedSongAsync(CachedSongInfo song, IEnumerable<CachedSongInfo>? queue = null)
        {
            StopMusicPreview();
            _miniPlayerLoading = true;
            if (FindName("TxtMiniPlayerTrack") is TextBlock track)
                track.Text = "Loading track…";
            if (FindName("TxtMiniPlayerArtist") is TextBlock artist)
            {
                artist.Text = song.DisplayLabel;
                artist.Visibility = Visibility.Visible;
            }

            try
            {
                var path = await _musicCacheSvc.EnsurePreviewMp3Async(song, new Progress<string>(SetMappingsStatus));
                if (path == null || !File.Exists(path))
                    throw new InvalidOperationException($"Could not extract audio for '{song.DisplayLabel}'.");

                // Prefer currently filtered library as the next/prev context.
                var q = (queue ?? (_filteredLibrarySongs.Count > 0 ? _filteredLibrarySongs : new List<CachedSongInfo> { song })).ToList();
                if (!q.Any(s => s.Id.Equals(song.Id, StringComparison.OrdinalIgnoreCase)))
                    q.Insert(0, song);
                RememberLastPlayed(song, q);
                if (_musicCache != null) _musicCacheSvc.Save(_musicCache);
                _appPlayer.PlayTrack(song, q);
                SetMappingsStatus($"Playing: {song.DisplayLabel}");
            }
            finally
            {
                _miniPlayerLoading = false;
                UpdateMiniPlayerUi();
            }
        }

        private void RememberLastPlayed(CachedSongInfo song, IReadOnlyList<CachedSongInfo> queue)
        {
            if (_musicCache == null) return;
            _musicCache.LastPlayedSongId = song.Id;
            _musicCache.LastPlayedQueueIds = queue.Select(s => s.Id).ToList();
        }

        private void UpdateMiniPlayerUi()
        {
            var cur = _appPlayer.Current;
            if (FindName("TxtMiniPlayerTrack") is TextBlock track)
            {
                if (_miniPlayerLoading && cur == null)
                    track.Text = "Loading track…";
                else
                    track.Text = cur == null ? "No track playing" : (string.IsNullOrWhiteSpace(cur.Name) ? cur.Id : cur.Name);
            }
            if (FindName("TxtMiniPlayerArtist") is TextBlock artist)
            {
                var a = cur?.Artist ?? "";
                if (_miniPlayerLoading && cur == null) a = "Please wait";
                artist.Text = a;
                artist.Visibility = string.IsNullOrWhiteSpace(a) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (FindName("MiniPlayerVisualizer") is Canvas viz)
                viz.Visibility = (_appPlayer.IsPlaying || cur != null) ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("BtnMiniPlayerPlay") is Button play)
                play.Content = _appPlayer.IsPlaying ? "⏸" : "▶";
            if (FindName("BtnMiniPlayerLoop") is Button loop)
            {
                loop.Content = _appPlayer.RepeatMode switch
                {
                    MusicRepeatMode.One => "🔂",
                    MusicRepeatMode.All => "🔁",
                    MusicRepeatMode.Shuffle => "🔀",
                    _ => "➡"
                };
                loop.ToolTip = _appPlayer.RepeatMode switch
                {
                    MusicRepeatMode.One => "Loop: one track (seamless)",
                    MusicRepeatMode.All => "Loop: playlist in order",
                    MusicRepeatMode.Shuffle => "Loop: playlist shuffled",
                    _ => "Loop: off"
                };
            }
            if (FindName("BtnMiniPlayerExtend") is Button ext)
            {
                ext.ToolTip = _appPlayer.IsExtending
                    ? $"Extending to {FormatTrackTime(_appPlayer.DisplayDuration)} — click for options"
                    : "Extend loop";
            }
            UpdateMiniPlayerProgress();
        }

        private static string FormatTrackTime(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        private void UpdateMiniPlayerProgress()
        {
            var pos = _appPlayer.Position;
            var displayDur = _appPlayer.DisplayDuration;
            if (FindName("TxtMiniPlayerDuration") is TextBlock tDur)
                tDur.Text = displayDur > TimeSpan.Zero ? FormatTrackTime(displayDur) : "0:00";

            // While scrubbing / right after a seek, don't let the timer yank the thumb back.
            if (_miniSeekDragging || DateTime.UtcNow < _suppressMiniProgressUntilUtc)
            {
                if (FindName("TxtMiniPlayerTime") is TextBlock tHold && FindName("SldMiniPlayerSeek") is Slider sHold
                    && displayDur.TotalMilliseconds > 0)
                {
                    var holdMs = sHold.Value / 1000.0 * displayDur.TotalMilliseconds;
                    tHold.Text = FormatTrackTime(TimeSpan.FromMilliseconds(holdMs));
                }
                return;
            }

            if (FindName("TxtMiniPlayerTime") is TextBlock tPos)
                tPos.Text = FormatTrackTime(pos);
            if (FindName("SldMiniPlayerSeek") is Slider sld)
            {
                if (displayDur.TotalMilliseconds > 0)
                    sld.Value = Math.Clamp(pos.TotalMilliseconds / displayDur.TotalMilliseconds * 1000.0, 0, 1000);
                else
                    sld.Value = 0;
            }
        }

        private void ApplySeekFromSlider(double sliderValue, bool force = true)
        {
            var dur = _appPlayer.DisplayDuration;
            if (dur.TotalMilliseconds <= 0) return;
            if (!force && (DateTime.UtcNow - _lastMiniSeekUtc).TotalMilliseconds < 50)
                return;
            _lastMiniSeekUtc = DateTime.UtcNow;
            _suppressMiniProgressUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
            var ms = Math.Clamp(sliderValue, 0, 1000) / 1000.0 * dur.TotalMilliseconds;
            if (FindName("TxtMiniPlayerTime") is TextBlock tPos)
                tPos.Text = FormatTrackTime(TimeSpan.FromMilliseconds(ms));
            _appPlayer.SeekDisplay(TimeSpan.FromMilliseconds(ms));
        }

        private Slider? GetMiniSeekSlider() => FindName("SldMiniPlayerSeek") as Slider;

        private void SetSeekFromHitX(double x, double width, bool forceSeek)
        {
            var slider = GetMiniSeekSlider();
            if (slider == null) return;
            var ratio = Math.Clamp(x / Math.Max(1.0, width), 0, 1);
            slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
            ApplySeekFromSlider(slider.Value, force: forceSeek);
        }

        private void EndMiniSeekDrag(UIElement? hitArea, bool seek)
        {
            if (seek && GetMiniSeekSlider() is Slider slider)
                ApplySeekFromSlider(slider.Value, force: true);
            if (hitArea != null && hitArea.IsMouseCaptured)
                hitArea.ReleaseMouseCapture();
            _miniSeekDragging = false;
            _suppressMiniProgressUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
        }

        private void UpdateMiniPlayerVisualizer()
        {
            if (FindName("MiniPlayerVisualizer") is not Canvas canvas) return;
            var w = canvas.ActualWidth;
            var h = canvas.ActualHeight;
            if (w <= 1 || h <= 1) return;
            var bars = _appPlayer.VisualizerBars;
            var n = bars.Length;
            var gap = 2.0;
            var barW = Math.Max(2, (w - gap * (n - 1)) / n);
            Brush fill;
            try { fill = (Brush)FindResource("AccentBrush"); }
            catch { fill = Brushes.DodgerBlue; }
            while (canvas.Children.Count < n)
            {
                canvas.Children.Add(new WpfRectangle
                {
                    RadiusX = 1.5,
                    RadiusY = 1.5
                });
            }
            for (var i = 0; i < n; i++)
            {
                if (canvas.Children[i] is not WpfRectangle rect) continue;
                rect.Fill = fill;
                var bh = Math.Max(2, bars[i] * h);
                rect.Width = barW;
                rect.Height = bh;
                Canvas.SetLeft(rect, i * (barW + gap));
                Canvas.SetTop(rect, h - bh);
            }
        }

        private void MiniSeekHitArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement hit) return;
            _miniSeekDragging = true;
            var pos = e.GetPosition(hit);
            var width = hit is FrameworkElement fe ? fe.ActualWidth : 0;
            SetSeekFromHitX(pos.X, width, forceSeek: true);
            hit.CaptureMouse();
            e.Handled = true;
        }

        private void MiniSeekHitArea_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_miniSeekDragging || sender is not UIElement hit) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(hit);
            var width = hit is FrameworkElement fe ? fe.ActualWidth : 0;
            SetSeekFromHitX(pos.X, width, forceSeek: false);
        }

        private void MiniSeekHitArea_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndMiniSeekDrag(sender as UIElement, seek: true);
            e.Handled = true;
        }

        private void MiniSeekHitArea_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_miniSeekDragging)
                EndMiniSeekDrag(sender as UIElement, seek: true);
        }

        private async void MiniPlayerBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            // Ignore double-clicks on controls inside the bar.
            if (e.OriginalSource is DependencyObject d &&
                (FindAncestor<Button>(d) != null || FindAncestor<Slider>(d) != null))
                return;
            e.Handled = true;
            await OpenPlayMusicFromMiniPlayerAsync();
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            while (start != null)
            {
                if (start is T match) return match;
                start = System.Windows.Media.VisualTreeHelper.GetParent(start);
            }
            return null;
        }

        private async System.Threading.Tasks.Task OpenPlayMusicFromMiniPlayerAsync()
        {
            try
            {
                var last = _settingsService?.GetPressorLastDirectory();
                if (!string.IsNullOrWhiteSpace(last))
                    SetMappingsInstallRoot(last!);
                else if (!string.IsNullOrWhiteSpace(TxtPressorInstallRoot.Text))
                    SetMappingsInstallRoot(TxtPressorInstallRoot.Text.Trim());

                SetActivePage("mappings");
                OpenCategory("music");
                ShowMapPanel("playMusic");
                if (_mappingsDoc == null)
                    await RefreshMappingsPageAsync();
                // Keep play music visible after refresh.
                OpenCategory("music");
                ShowMapPanel("playMusic");
                await EnsureMusicCacheAsync(force: false);
                BindPlayMusicLibrary();
                BindUserPlaylistsCombo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Play music", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AttachSystemMediaControls()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                _systemMedia.Attach(hwnd, _appPlayer, Dispatcher);
                _hwndSource = HwndSource.FromHwnd(hwnd);
                _hwndSource?.AddHook(WndProc);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Attach system media controls failed", ex);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Backup path when the window is focused (SMTC covers the global case).
            const int wmAppCommand = 0x0319;
            if (msg != wmAppCommand) return IntPtr.Zero;

            int cmd = ((int)((long)lParam >> 16)) & 0xFFF;
            switch (cmd)
            {
                case 14: // APPCOMMAND_MEDIA_PLAY_PAUSE
                    _appPlayer.TogglePlayPause();
                    handled = true;
                    break;
                case 46: // APPCOMMAND_MEDIA_PLAY
                    _appPlayer.Play();
                    handled = true;
                    break;
                case 47: // APPCOMMAND_MEDIA_PAUSE
                    _appPlayer.Pause();
                    handled = true;
                    break;
                case 11: // APPCOMMAND_MEDIA_NEXTTRACK
                    _appPlayer.Next();
                    handled = true;
                    break;
                case 12: // APPCOMMAND_MEDIA_PREVIOUSTRACK
                    _appPlayer.Previous();
                    handled = true;
                    break;
                case 13: // APPCOMMAND_MEDIA_STOP
                    _appPlayer.Stop();
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        private void BtnMiniPlayerPrev_Click(object sender, RoutedEventArgs e) => _appPlayer.Previous();
        private async void BtnMiniPlayerPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_appPlayer.Current != null)
            {
                _appPlayer.TogglePlayPause();
                return;
            }
            try { await PlayDefaultOrLastAsync(); }
            catch (Exception ex)
            {
                _miniPlayerLoading = false;
                UpdateMiniPlayerUi();
                MessageBox.Show(ex.Message, "Play", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void BtnMiniPlayerNext_Click(object sender, RoutedEventArgs e) => _appPlayer.Next();
        private void BtnMiniPlayerLoop_Click(object sender, RoutedEventArgs e)
        {
            _appPlayer.ToggleRepeat();
            UpdateMiniPlayerUi();
        }

        private void BtnMiniPlayerExtend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnPlayMusicExtendMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void MiniExtend15_Click(object sender, RoutedEventArgs e) => ApplyExtendMinutes(15);
        private void MiniExtend30_Click(object sender, RoutedEventArgs e) => ApplyExtendMinutes(30);
        private void MiniExtend60_Click(object sender, RoutedEventArgs e) => ApplyExtendMinutes(60);
        private void MiniExtendStop_Click(object sender, RoutedEventArgs e) => ApplyExtendMinutes(0);

        private void MiniExtendCustom_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Extend track", "Minutes to keep looping (seamless):") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            if (!int.TryParse(dlg.InputText.Trim(), out var mins) || mins <= 0)
            {
                MessageBox.Show("Enter a positive number of minutes.", "Extend", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ApplyExtendMinutes(mins);
        }

        private void ApplyExtendMinutes(int minutes)
        {
            if (minutes <= 0)
            {
                _appPlayer.ExtendMinutes(0);
                SetMappingsStatus("Extend cleared — back to normal track length.");
                UpdateMiniPlayerUi();
                return;
            }
            if (_appPlayer.Current == null)
            {
                MessageBox.Show("Play a track first, then extend.", "Extend", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _appPlayer.ExtendMinutes(minutes);
            SetMappingsStatus($"Extending current track for {minutes} minutes (seamless loop).");
            UpdateMiniPlayerUi();
        }

        private async System.Threading.Tasks.Task PlayDefaultOrLastAsync()
        {
            _miniPlayerLoading = true;
            UpdateMiniPlayerUi();

            try
            {
                var last = _settingsService?.GetPressorLastDirectory();
                if (!string.IsNullOrWhiteSpace(last))
                    SetMappingsInstallRoot(last!);
                else if (!string.IsNullOrWhiteSpace(TxtPressorInstallRoot.Text))
                    SetMappingsInstallRoot(TxtPressorInstallRoot.Text.Trim());
            }
            catch { /* ignore */ }

            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                _miniPlayerLoading = false;
                UpdateMiniPlayerUi();
                throw new InvalidOperationException("Set a prepared install on the Mappings page first.");
            }

            // Go straight to Play Music (not the mappings hub).
            SetActivePage("mappings");
            OpenCategory("music");
            ShowMapPanel("playMusic");

            if (_mappingsDoc == null ||
                !string.Equals(_mappingsLoadedRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(root!, exportIfMissing: true);
                _mappingsLoadedRoot = root;
            }

            OpenCategory("music");
            ShowMapPanel("playMusic");
            await EnsureMusicCacheAsync(force: false);
            BindPlayMusicLibrary();
            BindUserPlaylistsCombo();

            if (_musicCache == null || _musicCache.Songs.Count == 0)
                _musicCache = _musicCacheSvc.Load(root!) ?? _musicCache;

            var songs = (_musicCache?.Songs ?? new List<CachedSongInfo>())
                .Where(s => !string.IsNullOrWhiteSpace(s.SwfPath) && File.Exists(s.SwfPath))
                .ToList();
            if (songs.Count == 0)
            {
                _miniPlayerLoading = false;
                UpdateMiniPlayerUi();
                throw new InvalidOperationException("No playable songs found (SWF files missing under data/sound).");
            }

            try
            {
                var context = _filteredLibrarySongs.Count > 0 ? _filteredLibrarySongs : songs;
                if (!string.IsNullOrWhiteSpace(_musicCache?.LastPlayedSongId))
                {
                    var lastId = _musicCache!.LastPlayedSongId!;
                    var song = context.FirstOrDefault(s => s.Id.Equals(lastId, StringComparison.OrdinalIgnoreCase))
                               ?? songs.FirstOrDefault(s => s.Id.Equals(lastId, StringComparison.OrdinalIgnoreCase));
                    if (song != null)
                    {
                        await PlayCachedSongAsync(song, context);
                        return;
                    }
                }

                var pick = context[Random.Shared.Next(context.Count)];
                await PlayCachedSongAsync(pick, context);
            }
            finally
            {
                _miniPlayerLoading = false;
                UpdateMiniPlayerUi();
            }
        }

        private async void LstPlayMusicLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((FindName("LstPlayMusicLibrary") as ListBox)?.SelectedItem is CachedSongInfo song)
            {
                try { await PlayCachedSongAsync(song, _filteredLibrarySongs); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Play music", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }

        private async void BtnPlayMusicPlay_Click(object sender, RoutedEventArgs e)
        {
            if ((FindName("LstPlayMusicLibrary") as ListBox)?.SelectedItem is not CachedSongInfo song) return;
            try { await PlayCachedSongAsync(song, _filteredLibrarySongs); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Play music", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private async void BtnPlayMusicPreview_Click(object sender, RoutedEventArgs e)
        {
            if ((FindName("LstPlayMusicLibrary") as ListBox)?.SelectedItem is not CachedSongInfo song) return;
            await PreviewCachedSongAsync(song);
        }


        private void CmbUserPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            BindUserPlaylistTracks();

        private void BtnUserPlaylistNew_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null)
            {
                MessageBox.Show("Build the music cache first.", "Playlists", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new InputDialog("New playlist", "Playlist name:") { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
            var pl = new UserMusicPlaylist
            {
                Id = "mm_" + Guid.NewGuid().ToString("N")[..8],
                Name = dlg.InputText.Trim()
            };
            _musicCache.UserPlaylists.Add(pl);
            _musicCacheSvc.Save(_musicCache);
            BindUserPlaylistsCombo();
            if (FindName("CmbUserPlaylists") is ComboBox cmb) cmb.SelectedItem = pl;
        }

        private void BtnUserPlaylistAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null) return;
            if ((FindName("CmbUserPlaylists") as ComboBox)?.SelectedItem is not UserMusicPlaylist pl) return;
            if ((FindName("LstPlayMusicLibrary") as ListBox)?.SelectedItem is not CachedSongInfo song) return;
            if (pl.TrackIds.Any(id => id.Equals(song.Id, StringComparison.OrdinalIgnoreCase))) return;
            pl.TrackIds.Add(song.Id);
            _musicCacheSvc.Save(_musicCache);
            BindUserPlaylistTracks();
        }

        private void BtnUserPlaylistAddAllFiltered_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null) return;
            if ((FindName("CmbUserPlaylists") as ComboBox)?.SelectedItem is not UserMusicPlaylist pl) return;
            if (_filteredLibrarySongs.Count == 0)
            {
                MessageBox.Show("No songs in the current library filter.", "Add all filtered",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var added = 0;
            foreach (var song in _filteredLibrarySongs)
            {
                if (pl.TrackIds.Any(id => id.Equals(song.Id, StringComparison.OrdinalIgnoreCase))) continue;
                pl.TrackIds.Add(song.Id);
                added++;
            }
            _musicCacheSvc.Save(_musicCache);
            BindUserPlaylistTracks();
            SetMappingsStatus($"Added {added} song(s) to '{pl.Name}'.");
        }

        private void BtnUserPlaylistRename_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null) return;
            if ((FindName("CmbUserPlaylists") as ComboBox)?.SelectedItem is not UserMusicPlaylist pl) return;
            var dlg = new InputDialog("Rename playlist", "New playlist name:") { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
            pl.Name = dlg.InputText.Trim();
            _musicCacheSvc.Save(_musicCache);
            BindUserPlaylistsCombo();
            if (FindName("CmbUserPlaylists") is ComboBox cmb) cmb.SelectedItem = pl;
        }

        private void BtnUserPlaylistsExport_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null || _musicCache.UserPlaylists.Count == 0)
            {
                MessageBox.Show("No Mod Manager playlists to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Playlist pack|*.json|All|*.*",
                FileName = "ssf2mm-playlists.json"
            };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, Newtonsoft.Json.JsonConvert.SerializeObject(
                _musicCache.UserPlaylists, Newtonsoft.Json.Formatting.Indented));
            MessageBox.Show($"Exported {_musicCache.UserPlaylists.Count} playlist(s).", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnUserPlaylistsImport_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null)
            {
                MessageBox.Show("Build the music library first.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Playlist pack|*.json|All|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<UserMusicPlaylist>>(
                    File.ReadAllText(dlg.FileName));
                if (list == null || list.Count == 0)
                {
                    MessageBox.Show("No playlists found in that file.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var merged = 0;
                foreach (var incoming in list)
                {
                    if (string.IsNullOrWhiteSpace(incoming.Id))
                        incoming.Id = "mm_" + Guid.NewGuid().ToString("N")[..8];
                    var existing = _musicCache.UserPlaylists.FirstOrDefault(p =>
                        p.Id.Equals(incoming.Id, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(incoming.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Name = incoming.Name;
                        existing.TrackIds = incoming.TrackIds?.ToList() ?? new List<string>();
                    }
                    else
                    {
                        _musicCache.UserPlaylists.Add(incoming);
                    }
                    merged++;
                }
                _musicCacheSvc.Save(_musicCache);
                BindUserPlaylistsCombo();
                MessageBox.Show($"Imported {merged} playlist(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnUserPlaylistRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null) return;
            if ((FindName("CmbUserPlaylists") as ComboBox)?.SelectedItem is not UserMusicPlaylist pl) return;
            if ((FindName("LstUserPlaylistTracks") as ListBox)?.SelectedItem is not CachedSongInfo song) return;
            pl.TrackIds.RemoveAll(id => id.Equals(song.Id, StringComparison.OrdinalIgnoreCase));
            _musicCacheSvc.Save(_musicCache);
            BindUserPlaylistTracks();
        }

        private async void BtnUserPlaylistPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_musicCache == null) return;
            if ((FindName("CmbUserPlaylists") as ComboBox)?.SelectedItem is not UserMusicPlaylist pl) return;
            var tracks = pl.TrackIds
                .Select(id => _musicCache.Songs.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                .Where(s => s != null)
                .Cast<CachedSongInfo>()
                .ToList();
            if (tracks.Count == 0)
            {
                MessageBox.Show("Playlist is empty.", "Play playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var playable = new List<CachedSongInfo>();
                await RunBusyWorkAsync("Preparing playlist", async progress =>
                {
                    for (var i = 0; i < tracks.Count; i++)
                    {
                        var song = tracks[i];
                        progress.Report(($"Extracting {song.DisplayLabel}…", 10 + 80.0 * i / tracks.Count, song.Id));
                        try
                        {
                            var path = await _musicCacheSvc.EnsurePreviewMp3Async(song);
                            if (path != null) playable.Add(song);
                        }
                        catch (Exception ex)
                        {
                            DevFileLog.Write($"[PlayPlaylist] skip {song.Id}: {ex.Message}\n");
                        }
                    }
                    _musicCacheSvc.Save(_musicCache);
                    progress.Report(("Starting playback…", 100, null));
                });
                if (playable.Count == 0)
                {
                    MessageBox.Show(
                        "Could not extract audio for any track in this playlist.",
                        "Play playlist", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RememberLastPlayed(playable[0], playable);
                if (_musicCache != null) _musicCacheSvc.Save(_musicCache);
                _appPlayer.PlayTrack(playable[0], playable);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Play playlist", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowsePathInto(string textBoxName, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == true && FindName(textBoxName) is TextBox tb)
                tb.Text = dlg.FileName;
        }

        private void BtnMapCharBrowseSwf_Click(object sender, RoutedEventArgs e)
        {
            BrowsePathInto("TxtMapCharSwf", "Flash character|*.swf;*.ssf|SWF|*.swf|SSF|*.ssf|All|*.*");
            var swf = GetTb("TxtMapCharSwf");
            if (string.IsNullOrWhiteSpace(swf)) return;
            var suggested = CharacterCloneService.SuggestOldNameFromPath(swf);
            if (FindName("CmbMapCharDonor") is ComboBox donor && string.IsNullOrWhiteSpace(donor.Text))
                donor.Text = suggested;
            if (string.IsNullOrWhiteSpace(GetTb("TxtMapCharWizId")))
                SetTb("TxtMapCharWizId", suggested + "clone");
        }

        private void OpenCharacterWizard(CharacterWizardMode mode, string? prefillId = null, string? prefillName = null, string? prefillSwf = null)
        {
            _mapCharMode = CharacterWizardMode.Clone;
            _ = mode;
            _mapCharStep = 1; // Identity (mode step removed)
            _mapCharActiveSlot = "select";
            _mapCharOverlayPaths.Clear();
            _mapCharPlaceholderPaths.Clear();
            _mapCharExportPaths.Clear();
            _mapCharSlotInfos.Clear();
            _mapCharTransforms.Clear();
            EnsureCharWizardWorkDir();
            PopulateCharWizardSlotCombo();
            if (FindName("ChkMapCharLastRow") is CheckBox lastRow) lastRow.IsChecked = true;
            if (FindName("ChkMapCharNewRow") is CheckBox newRow) newRow.IsChecked = false;

            if (FindName("RadMapCharModeClone") is RadioButton rc) rc.IsChecked = true;
            if (FindName("RadMapCharModeNew") is RadioButton rn) rn.IsChecked = false;
            if (FindName("RadMapCharModeEdit") is RadioButton re) re.IsChecked = false;

            PopulateCharacterWizardDropdowns();
            SetTb("TxtMapCharSwf", prefillSwf ?? "");
            SetTb("TxtMapCharWizId", prefillId ?? "");
            SetTb("TxtMapCharWizName", prefillName ?? "");
            SetTb("TxtMapCharStock", "");
            SetTb("TxtMapCharPa", "");
            SetTb("TxtMapCharChip", "");

            if (FindName("MapCharArtCanvas") is ArtPlacementCanvas canvas && !_mapCharCanvasHooked)
            {
                canvas.TransformChanged += (_, _) =>
                {
                    SaveActiveCharSlotTransform();
                    SyncCharArtFieldsFromCanvas();
                };
                _mapCharCanvasHooked = true;
            }

            ShowMapPanel("charAdd");
            RefreshCharacterWizardStepUi();
        }

        private void OpenCharacterUpdatePanel()
        {
            var root = GetMappingsInstallRoot();
            if (FindName("ListMapCharUpdate") is not ListBox list) return;
            list.Items.Clear();
            if (string.IsNullOrWhiteSpace(root))
            {
                MessageBox.Show("Load mappings / install first.", "Update Character", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var pkg in CharacterPackageService.ScanPackages(root!))
            {
                var prefs = _settingsService?.GetCharacterUpdatePrefs(pkg.Manifest.Id) ?? new CharacterUpdatePrefs();
                var row = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 8) };
                var header = new CheckBox
                {
                    Content = $"{pkg.Manifest.DisplayName}  ({pkg.Manifest.Id})",
                    IsChecked = true,
                    FontWeight = FontWeights.Normal,
                    Tag = pkg
                };
                var opts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 4, 0, 0) };
                var chkAssets = new CheckBox
                {
                    Content = "Update assets",
                    IsChecked = prefs.UpdateAssets,
                    Margin = new Thickness(0, 0, 16, 0),
                    Tag = "assets"
                };
                var chkSwf = new CheckBox
                {
                    Content = "Update character SWF",
                    IsChecked = prefs.UpdateSwf,
                    Tag = "swf"
                };
                void Persist()
                {
                    _settingsService?.SetCharacterUpdatePrefs(
                        pkg.Manifest.Id,
                        chkAssets.IsChecked == true,
                        chkSwf.IsChecked == true);
                }
                chkAssets.Checked += (_, _) => Persist();
                chkAssets.Unchecked += (_, _) => Persist();
                chkSwf.Checked += (_, _) => Persist();
                chkSwf.Unchecked += (_, _) => Persist();
                opts.Children.Add(chkAssets);
                opts.Children.Add(chkSwf);
                row.Children.Add(header);
                row.Children.Add(opts);
                list.Items.Add(row);
            }

            if (list.Items.Count == 0)
            {
                list.Items.Add(new TextBlock
                {
                    Text = "No character packages found. Clone a character first — packages live in data/character/{Display Name}/.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4)
                });
            }

            ShowMapPanel("charUpdate");
        }

        private void BtnMapCharUpdateBack_Click(object sender, RoutedEventArgs e) => ShowMapPanel("category");

        private async void BtnMapCharUpdateRun_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            if (FindName("ListMapCharUpdate") is not ListBox list) return;

            var items = new List<CharacterUpdateItem>();
            foreach (var obj in list.Items)
            {
                if (obj is not StackPanel row || row.Children.Count < 2) continue;
                if (row.Children[0] is not CheckBox header || header.IsChecked != true) continue;
                if (header.Tag is not CharacterPackageInfo pkg) continue;
                var opts = row.Children[1] as StackPanel;
                var updateAssets = true;
                var updateSwf = true;
                if (opts != null)
                {
                    foreach (var c in opts.Children)
                    {
                        if (c is not CheckBox cb) continue;
                        if (cb.Tag as string == "assets") updateAssets = cb.IsChecked == true;
                        if (cb.Tag as string == "swf") updateSwf = cb.IsChecked == true;
                    }
                }
                if (!updateAssets && !updateSwf) continue;
                _settingsService?.SetCharacterUpdatePrefs(pkg.Manifest.Id, updateAssets, updateSwf);
                items.Add(new CharacterUpdateItem
                {
                    PackageFolder = pkg.FolderPath,
                    UpdateAssets = updateAssets,
                    UpdateSwf = updateSwf
                });
            }

            if (items.Count == 0)
            {
                MessageBox.Show("Select at least one package with an update option checked.", "Update Character",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                WizardResult? result = null;
                await RunBusyWorkAsync("Update Character", async progress =>
                {
                    var steps = new WizardStepProgress(progress, WizardStepProgress.UpdateCharacterSteps);
                    steps.BeginStep(0, "Preparing…");
                    steps.CompleteStep();
                    steps.BeginStep(1, "Updating…");
                    result = await _rosterWizard.UpdateCharactersAsync(
                        root!, _mappingsDoc, items, null, steps.AsStringProgress());
                    steps.CompleteStep();
                    steps.BeginStep(3, "Refreshing…");
                    _menuSymbolIndex = await _menuSlots.LoadSymbolIndexAsync(root!);
                    steps.Finish(result?.Message ?? "Done");
                });
                PopulateGuidedDropdowns();
                MessageBox.Show(result?.Message ?? "Done.", "Update Character", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMapPanel("hub");
            }
            catch (Exception ex)
            {
                HideOverlay();
                MessageBox.Show(ex.Message, "Update Character", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureCharWizardWorkDir()
        {
            if (!string.IsNullOrWhiteSpace(_mapCharWorkDir) && Directory.Exists(_mapCharWorkDir))
                return;
            _mapCharWorkDir = Path.Combine(Path.GetTempPath(), "ssf2mm-charwiz-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_mapCharWorkDir);
        }

        private void PopulateCharacterWizardDropdowns()
        {
            var ids = GetMappedCharacterIds();
            if (FindName("CmbMapCharDonor") is ComboBox donor)
            {
                donor.Items.Clear();
                foreach (var id in ids) donor.Items.Add(id);
                if (ids.Contains("mario", StringComparer.OrdinalIgnoreCase))
                    donor.Text = "mario";
                else if (ids.Count > 0)
                    donor.Text = ids[0];
            }
            if (FindName("CmbMapCharEditId") is ComboBox edit)
            {
                edit.Items.Clear();
                foreach (var id in ids) edit.Items.Add(id);
                if (ids.Count > 0) edit.SelectedIndex = 0;
            }
            _ = PopulateSeriesSymbolCombosAsync();
        }

        private async System.Threading.Tasks.Task PopulateSeriesSymbolCombosAsync()
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                // Prefer icons actually used in mappings (short, accurate); merge SWF hits sparingly.
                var fromMappings = _mappingsDoc != null
                    ? MappingsEditService.ListSeriesIcons(_mappingsDoc)
                    : new List<string>();
                var fromSwf = await _menuSlots.ListSeriesSymbolLinkagesAsync(root!);
                var symbols = fromMappings
                    .Concat(fromSwf)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                // If SWF filter still balloons, keep mappings + common smash fallback.
                if (symbols.Count > 80 && fromMappings.Count > 0)
                {
                    symbols = fromMappings
                        .Concat(new[] { "smash_symbol", "smash_icon" })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                void Fill(string name, string prefer)
                {
                    if (FindName(name) is not ComboBox cb) return;
                    var cur = cb.Text;
                    cb.Items.Clear();
                    foreach (var s in symbols) cb.Items.Add(s);
                    if (!string.IsNullOrWhiteSpace(cur)
                        && symbols.Any(x => x.Equals(cur, StringComparison.OrdinalIgnoreCase)))
                        cb.Text = cur;
                    else if (symbols.Any(x => x.Equals(prefer, StringComparison.OrdinalIgnoreCase)))
                        cb.Text = prefer;
                    else if (symbols.Any(x => x.Equals("smash_symbol", StringComparison.OrdinalIgnoreCase)))
                        cb.Text = "smash_symbol";
                    else if (symbols.Count > 0)
                        cb.Text = symbols[0];
                }
                Fill("CmbMapCharSeriesIcon", "smash_symbol");
                Fill("CmbMapCharSeriesTemplate", "smash_symbol");
                SyncSeriesIconFromDonor();
            }
            catch
            {
                /* ignore — combos stay empty / editable */
            }
        }

        private void CmbMapCharDonor_SelectionChanged(object sender, RoutedEventArgs e) =>
            SyncSeriesIconFromDonor();

        private void SyncSeriesIconFromDonor()
        {
            if (_mappingsDoc == null) return;
            var donor = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(donor)) return;
            var icon = _mappingsDoc["character"]?[donor]?["seriesIcon"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(icon)) return;
            if (FindName("CmbMapCharSeriesIcon") is ComboBox cb)
            {
                if (!cb.Items.Cast<object>().Any(x =>
                        string.Equals(x?.ToString(), icon, StringComparison.OrdinalIgnoreCase)))
                    cb.Items.Add(icon);
                cb.Text = icon;
            }
        }

        private void ChkMapCharNewSeriesSymbol_Changed(object sender, RoutedEventArgs e)
        {
            var on = (FindName("ChkMapCharNewSeriesSymbol") as CheckBox)?.IsChecked == true;
            if (FindName("PanelMapCharNewSeriesSymbol") is UIElement panel)
                panel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on && string.IsNullOrWhiteSpace(GetTb("TxtMapCharNewSeriesName")))
            {
                var id = GetTb("TxtMapCharWizId");
                if (!string.IsNullOrWhiteSpace(id))
                    SetTb("TxtMapCharNewSeriesName", id + "_symbol");
            }
        }

        private void BtnMapCharBrowseSeriesPng_Click(object sender, RoutedEventArgs e) =>
            BrowsePathInto("TxtMapCharSeriesPng", "PNG|*.png|All|*.*");

        private List<string> GetMappedCharacterIds()
        {
            var list = new List<string>();
            if (_mappingsDoc?["character"] is JObject chars)
            {
                foreach (var p in chars.Properties())
                {
                    if (!string.IsNullOrWhiteSpace(p.Name))
                        list.Add(p.Name);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private CharacterWizardMode ReadCharacterWizardMode() => CharacterWizardMode.Clone;

        private void RefreshCharacterWizardStepUi()
        {
            _mapCharMode = CharacterWizardMode.Clone;
            void Vis(string name, bool on)
            {
                if (FindName(name) is UIElement el)
                    el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            Vis("MapCharStepMode", false);
            // Identity-only clone flow (assets / review panels unused).
            Vis("MapCharStepIdentity", true);
            Vis("MapCharStepAssets", false);
            Vis("MapCharStepReview", false);

            if (FindName("TxtMapCharWizStep") is TextBlock stepTb)
            {
                stepTb.Text =
                    "Pick donor, id, name, and symbol — then clone. Editable assets land in the package folder for Update Character.";
            }
            if (FindName("TxtMapCharWizTitle") is TextBlock title)
                title.Text = "Clone Character";
            Vis("LblMapCharDonor", true);
            Vis("CmbMapCharDonor", true);
            Vis("LblMapCharEditId", false);
            Vis("CmbMapCharEditId", false);
            Vis("LblMapCharSwf", true);
            Vis("PanelMapCharSwf", true);
            Vis("LblMapCharWizId", true);
            Vis("TxtMapCharWizId", true);
            Vis("ChkMapCharNewRow", true);
            Vis("ChkMapCharRandom", true);
            if (FindName("LblMapCharDonor") is TextBlock donorLbl)
                donorLbl.Text = "Donor character (UI / assets source)";
            if (FindName("CmbMapCharDonor") is ComboBox donorCb)
                donorCb.IsEnabled = true;
        }

        private void BtnMapCharStepBack_Click(object sender, RoutedEventArgs e)
        {
            if (_mapCharStep > 1)
            {
                _mapCharStep--;
                RefreshCharacterWizardStepUi();
            }
            else
                ShowMapPanel("category");
        }

        private async void BtnMapCharStepNext_Click(object sender, RoutedEventArgs e)
        {
            if (_mapCharStep == 1)
            {
                if (!TryValidateCharWizardIdentity(out var err))
                {
                    MessageBox.Show(err, "Clone Character", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _mapCharStep = 2;
                RefreshCharacterWizardStepUi();
                await LoadCharWizardPlaceholdersAsync();
                await ApplyCharWizardActiveSlotAsync();
                return;
            }

            if (_mapCharStep == 2)
            {
                ExportActiveCharSlotArt();
                _mapCharStep = 3;
                RefreshCharacterWizardStepUi();
                await RefreshCharWizardChecklistAsync();
                UpdateCharWizardReviewSummary();
            }
        }

        private bool TryValidateCharWizardIdentity(out string error)
        {
            error = "";
            _mapCharMode = CharacterWizardMode.Clone;
            var id = GetTb("TxtMapCharWizId");
            var name = GetTb("TxtMapCharWizName");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                error = "Id and display name are required.";
                return false;
            }
            var donor = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim() ?? "";
            var swf = GetTb("TxtMapCharSwf");
            var root = GetMappingsInstallRoot();
            var donorId = MenuSlotService.SanitizeId(donor);
            var donorSwf = string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(donorId)
                ? null
                : Path.Combine(root, "data", "character", donorId + ".swf");
            // Also accept package-folder SWFs.
            if ((donorSwf == null || !File.Exists(donorSwf)) && !string.IsNullOrWhiteSpace(root))
            {
                var pkg = CharacterPackageService.ScanPackages(root!)
                    .FirstOrDefault(p => p.Manifest.Id.Equals(donorId, StringComparison.OrdinalIgnoreCase));
                if (pkg != null && pkg.SwfExists) donorSwf = pkg.SwfPath;
            }
            if (string.IsNullOrWhiteSpace(swf) && (donorSwf == null || !File.Exists(donorSwf)))
            {
                error = "Pick a donor in the install, or browse a source .ssf / .swf.";
                return false;
            }

            try
            {
                var sanitized = MenuSlotService.SanitizeId(id);
                if (_mappingsDoc?["character"]?[sanitized] != null)
                {
                    var overwrite = MessageBox.Show(
                        $"Character id '{sanitized}' already exists in mappings.\n\nOverwrite / re-import it?",
                        "Clone Character", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (overwrite != MessageBoxResult.Yes)
                    {
                        error = "Cancelled — pick a new id.";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        private void PopulateCharWizardSlotCombo()
        {
            if (FindName("CmbMapCharSlot") is not ComboBox cmb) return;
            cmb.Items.Clear();
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["select"] = "CSS chip",
                ["pa"] = "PA (charselect / classic / allstar / trophy)",
                ["stock"] = "Stock icon",
                ["event"] = "Event PA (name + symbol)",
                ["congrats"] = "Classic congrats",
                ["allstar_congrats"] = "All-Star congrats"
            };
            foreach (var key in CharacterAssetService.WizardPreviewSlotKeys)
            {
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = labels.GetValueOrDefault(key, key),
                    Tag = key
                });
            }
            cmb.SelectedIndex = 0;
            _mapCharActiveSlot = "select";
        }

        private static string SlotKeyFromComboItem(object? item)
        {
            if (item is ComboBoxItem cbi && cbi.Tag is string key && !string.IsNullOrWhiteSpace(key))
                return key;
            return item?.ToString() ?? "select";
        }

        private async Task LoadCharWizardPlaceholdersAsync()
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            EnsureCharWizardWorkDir();

            string artId;
            if (_mapCharMode == CharacterWizardMode.Edit)
                artId = GetTb("TxtMapCharWizId");
            else if (_mapCharMode == CharacterWizardMode.New)
                artId = RosterWizardService.DefaultNewUiDonorId;
            else
                artId = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim()
                        ?? RosterWizardService.DefaultNewUiDonorId;

            // Eager CSS / PA (cssp) / stock — selective FFDec + disk cache.
            var slotKeys = CharacterAssetService.EagerPlaceholderSlotKeys.ToList();
            await RunBusyWorkAsync("Loading art placeholders", async progress =>
            {
                progress.Report(("Loading CSS / PA / stock…", 20, artId));
                var bulk = await _charAssets.ExportSlotsBulkAsync(
                    root!, artId, slotKeys, _mapCharWorkDir!, null,
                    new Progress<string>(m => progress.Report((m, 55, null))));
                foreach (var kv in bulk)
                {
                    _mapCharSlotInfos[kv.Key] = kv.Value;
                    _mapCharPlaceholderPaths[kv.Key] = kv.Value.PngPath;
                }
                // Virtual "pa" preview uses cssp (or charselect) art.
                if (bulk.TryGetValue("cssp", out var cssp) && !string.IsNullOrWhiteSpace(cssp.PngPath))
                    _mapCharPlaceholderPaths["pa"] = cssp.PngPath;
                else if (bulk.TryGetValue("charselect", out var cs) && !string.IsNullOrWhiteSpace(cs.PngPath))
                    _mapCharPlaceholderPaths["pa"] = cs.PngPath;
                progress.Report(("Done.", 100, null));
            });
        }

        private async Task EnsureCharWizardSlotPlaceholderAsync(string slotKey)
        {
            if (_mapCharPlaceholderPaths.TryGetValue(slotKey, out var existing) &&
                !string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
                return;

            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            EnsureCharWizardWorkDir();

            var artId = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim()
                        ?? RosterWizardService.DefaultNewUiDonorId;
            var exportKey = slotKey.Equals("pa", StringComparison.OrdinalIgnoreCase) ? "cssp" : slotKey;
            if (exportKey.Equals("event", StringComparison.OrdinalIgnoreCase))
                exportKey = "event";

            await RunBusyWorkAsync($"Loading {slotKey}", async progress =>
            {
                progress.Report(($"Exporting {exportKey}…", 30, artId));
                var bulk = await _charAssets.ExportSlotsBulkAsync(
                    root!, artId, new[] { exportKey }, _mapCharWorkDir!, null,
                    new Progress<string>(m => progress.Report((m, 60, null))));
                foreach (var kv in bulk)
                {
                    _mapCharSlotInfos[kv.Key] = kv.Value;
                    _mapCharPlaceholderPaths[kv.Key] = kv.Value.PngPath;
                }
                if (slotKey.Equals("pa", StringComparison.OrdinalIgnoreCase)
                    && bulk.TryGetValue("cssp", out var cssp))
                    _mapCharPlaceholderPaths["pa"] = cssp.PngPath;
                progress.Report(("Done.", 100, null));
            });
        }

        private async Task ApplyCharWizardActiveSlotAsync()
        {
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            var slot = _mapCharActiveSlot;
            canvas.PreviewMode = slot switch
            {
                "select" => ArtPlacementPreviewMode.CssChip,
                "event" => ArtPlacementPreviewMode.EventPa,
                _ => ArtPlacementPreviewMode.Plain
            };
            canvas.DisplayName = GetTb("TxtMapCharWizName");
            canvas.NameBarHeight = ArtPlacementCanvas.DefaultNameBarHeight;
            if (double.TryParse(GetTb("TxtMapCharNameFont"), out var fs) && fs > 0)
                canvas.NameFontSize = fs;
            else
                canvas.NameFontSize = ArtPlacementCanvas.DefaultNameFontSize;
            canvas.SeriesSymbolSource = null;

            if (_mapCharTransforms.TryGetValue(slot, out var saved))
                canvas.SetTransform(saved);
            else
                canvas.SetTransform(null);

            canvas.LoadPlaceholder(_mapCharPlaceholderPaths.GetValueOrDefault(slot));
            // Event PA is name/symbol preview — no art overlay editing.
            if (slot.Equals("event", StringComparison.OrdinalIgnoreCase))
                canvas.LoadOverlay(null);
            else
                canvas.LoadOverlay(_mapCharOverlayPaths.GetValueOrDefault(slot));
            if (FindName("ChkMapCharFlipH") is CheckBox fh) fh.IsChecked = canvas.FlipH;
            if (FindName("ChkMapCharFlipV") is CheckBox fv) fv.IsChecked = canvas.FlipV;
            SyncCharArtFieldsFromCanvas();
            await Task.CompletedTask;
        }

        private void SaveActiveCharSlotTransform()
        {
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            if (string.IsNullOrWhiteSpace(_mapCharActiveSlot)) return;
            _mapCharTransforms[_mapCharActiveSlot] = canvas.GetTransform();
        }

        private void ExportActiveCharSlotArt()
        {
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            SaveActiveCharSlotTransform();
            if (_mapCharActiveSlot.Equals("event", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.IsNullOrWhiteSpace(_mapCharOverlayPaths.GetValueOrDefault(_mapCharActiveSlot)))
            {
                _mapCharExportPaths[_mapCharActiveSlot] = null;
                return;
            }
            EnsureCharWizardWorkDir();
            var path = Path.Combine(_mapCharWorkDir!, $"export_{_mapCharActiveSlot}.png");
            canvas.ExportFlattenedPng(path);
            _mapCharExportPaths[_mapCharActiveSlot] = path;
            if (_mapCharActiveSlot.Equals("pa", StringComparison.OrdinalIgnoreCase))
            {
                // One PA image fans out to all shared host slots on finish.
                foreach (var key in CharacterAssetService.SharedPaSlotKeys)
                    _mapCharExportPaths[key] = path;
            }
        }

        private async void CmbMapCharSlot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mapCharStep != 2) return;
            ExportActiveCharSlotArt();
            if (FindName("CmbMapCharSlot") is ComboBox cmb)
                _mapCharActiveSlot = SlotKeyFromComboItem(cmb.SelectedItem);
            try
            {
                await EnsureCharWizardSlotPlaceholderAsync(_mapCharActiveSlot);
            }
            catch (Exception ex)
            {
                SetMappingsStatus("Placeholder: " + ex.Message);
            }
            await ApplyCharWizardActiveSlotAsync();
        }

        private void BtnMapCharChooseAssetFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose folder with select.png / pa.png / stock.png …"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var loaded = CharacterPackageService.LoadAssetsFromFolder(dlg.SelectedPath);
            if (loaded.Count == 0)
            {
                MessageBox.Show(
                    "No matching PNGs found. Expected select.png, pa.png (or cssp.png), stock.png, congrats.png, symbol.png.",
                    "Choose folder with assets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (loaded.TryGetValue("symbol", out var sym))
                SetTb("TxtMapCharSeriesPng", sym);
            foreach (var kv in loaded)
            {
                if (kv.Key.Equals("symbol", StringComparison.OrdinalIgnoreCase)) continue;
                _mapCharOverlayPaths[kv.Key] = kv.Value;
                _mapCharExportPaths[kv.Key] = kv.Value;
                if (kv.Key.Equals("pa", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var sk in CharacterAssetService.SharedPaSlotKeys)
                    {
                        _mapCharOverlayPaths[sk] = kv.Value;
                        _mapCharExportPaths[sk] = kv.Value;
                    }
                }
            }
            MessageBox.Show($"Loaded {loaded.Count} asset file(s). Continue to Assets to adjust placement.",
                "Choose folder with assets", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnMapCharGenerateDonorAssets_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            var donor = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(donor))
            {
                MessageBox.Show("Pick a donor character first.", "Generate donor assets",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Folder to write donor asset PNGs into"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                await RunBusyWorkAsync("Generate donor assets", async progress =>
                {
                    progress.Report(("Exporting…", 20, donor));
                    await _charPackages.ExportDonorAssetFolderAsync(
                        root!, donor, dlg.SelectedPath, null,
                        new Progress<string>(m => progress.Report((m, 60, null))));
                    progress.Report(("Done.", 100, null));
                });
                MessageBox.Show(
                    $"Donor assets written to:\n{dlg.SelectedPath}\n\nEdit the PNGs, then use Choose folder with assets.",
                    "Generate donor assets", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                HideOverlay();
                MessageBox.Show(ex.Message, "Generate donor assets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtMapCharNameFont_LostFocus(object sender, RoutedEventArgs e)
        {
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            if (double.TryParse(GetTb("TxtMapCharNameFont"), out var fs) && fs > 0)
            {
                canvas.NameFontSize = fs;
                canvas.InvalidateVisual();
            }
        }

        /// <summary>Last-row and new-row are mutually exclusive; default is last row.</summary>
        private void ChkMapCharCssRow_Changed(object sender, RoutedEventArgs e)
        {
            if (FindName("ChkMapCharLastRow") is not CheckBox last) return;
            if (FindName("ChkMapCharNewRow") is not CheckBox neu) return;
            if (sender == last && last.IsChecked == true)
                neu.IsChecked = false;
            else if (sender == neu && neu.IsChecked == true)
                last.IsChecked = false;
            else if (last.IsChecked != true && neu.IsChecked != true)
                last.IsChecked = true; // always keep one placement mode
        }

        /// <summary>Pure helper for unit tests — mutual exclusion of CSS row placement modes.</summary>
        public static void ApplyCssRowCheckboxMutualExclusion(bool? lastChecked, bool? newChecked, out bool last, out bool neu)
        {
            last = lastChecked == true;
            neu = newChecked == true;
            if (last && neu) { neu = false; }
            if (!last && !neu) last = true;
        }

        private void BtnMapCharLoadOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_mapCharActiveSlot.Equals("event", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Event PA uses your display name and series symbol — no separate art upload.",
                    "Event PA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new OpenFileDialog { Filter = "PNG|*.png|All|*.*" };
            if (dlg.ShowDialog() != true) return;
            _mapCharOverlayPaths[_mapCharActiveSlot] = dlg.FileName;
            if (FindName("MapCharArtCanvas") is ArtPlacementCanvas canvas)
            {
                canvas.SetTransform(null);
                canvas.LoadOverlay(dlg.FileName);
                canvas.FitOverlay();
                SaveActiveCharSlotTransform();
                SyncCharArtFieldsFromCanvas();
            }
        }

        private void BtnMapCharClearOverlay_Click(object sender, RoutedEventArgs e)
        {
            _mapCharOverlayPaths[_mapCharActiveSlot] = null;
            _mapCharExportPaths[_mapCharActiveSlot] = null;
            _mapCharTransforms.Remove(_mapCharActiveSlot);
            if (FindName("MapCharArtCanvas") is ArtPlacementCanvas canvas)
                canvas.ClearOverlay();
        }

        private void BtnMapCharArtFit_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("MapCharArtCanvas") is ArtPlacementCanvas canvas)
            {
                canvas.FitOverlay();
                SyncCharArtFieldsFromCanvas();
            }
        }

        private void BtnMapCharArtReset_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("MapCharArtCanvas") is ArtPlacementCanvas canvas)
            {
                canvas.ResetTransform();
                SyncCharArtFieldsFromCanvas();
            }
        }

        private void ChkMapCharFlip_Changed(object sender, RoutedEventArgs e)
        {
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            canvas.FlipH = (FindName("ChkMapCharFlipH") as CheckBox)?.IsChecked == true;
            canvas.FlipV = (FindName("ChkMapCharFlipV") as CheckBox)?.IsChecked == true;
            canvas.InvalidateVisual();
        }

        private void TxtMapCharArtNumeric_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_mapCharSyncingArtFields) return;
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            if (double.TryParse(GetTb("TxtMapCharArtX"), out var x)) canvas.OffsetX = x;
            if (double.TryParse(GetTb("TxtMapCharArtY"), out var y)) canvas.OffsetY = y;
            if (double.TryParse(GetTb("TxtMapCharArtScale"), out var s) && s > 0) canvas.Scale = s;
            canvas.InvalidateVisual();
        }

        private void SyncCharArtFieldsFromCanvas()
        {
            if (FindName("MapCharArtCanvas") is not ArtPlacementCanvas canvas) return;
            _mapCharSyncingArtFields = true;
            SetTb("TxtMapCharArtX", canvas.OffsetX.ToString("0.###"));
            SetTb("TxtMapCharArtY", canvas.OffsetY.ToString("0.###"));
            SetTb("TxtMapCharArtScale", canvas.Scale.ToString("0.###"));
            _mapCharSyncingArtFields = false;
        }

        private void TxtMapCharWizName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (FindName("MapCharArtCanvas") is ArtPlacementCanvas canvas)
            {
                canvas.DisplayName = GetTb("TxtMapCharWizName");
                canvas.InvalidateVisual();
            }
        }

        private void CmbMapCharEditId_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mapCharMode != CharacterWizardMode.Edit) return;
            var id = (FindName("CmbMapCharEditId") as ComboBox)?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(id)) return;
            SetTb("TxtMapCharWizId", id);
            if (_mappingsDoc?["character"]?[id] is JObject ent)
                SetTb("TxtMapCharWizName", ent["name"]?.ToString() ?? id);
        }

        private async Task RefreshCharWizardChecklistAsync()
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            var scanId = _mapCharMode == CharacterWizardMode.Edit
                ? GetTb("TxtMapCharWizId")
                : ((FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim()
                   ?? RosterWizardService.DefaultNewUiDonorId);
            if (string.IsNullOrWhiteSpace(scanId)) scanId = RosterWizardService.DefaultNewUiDonorId;

            CharacterAssetScanResult? scan = null;
            try
            {
                await RunBusyWorkAsync("Scanning asset slots", async progress =>
                {
                    progress.Report(("Scanning…", 30, scanId));
                    scan = await _charAssets.ScanCharacterAsync(root!, scanId, _mappingsDoc);
                    progress.Report(("Done.", 100, null));
                });
            }
            catch
            {
                scan = null;
            }

            if (FindName("ListMapCharChecklist") is not ListBox list) return;
            list.Items.Clear();
            var edited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _mapCharOverlayPaths)
                if (!string.IsNullOrWhiteSpace(kv.Value)) edited.Add(kv.Key);

            if (scan != null)
            {
                foreach (var slot in scan.Slots)
                {
                    var extra = slot.Def.IsEditable && edited.Contains(slot.Def.Key)
                        ? " · art ready"
                        : "";
                    list.Items.Add($"{slot.Def.DisplayName}: {slot.StatusLabel}{extra}");
                }
            }
            else
            {
                foreach (var def in CharacterAssetService.GetAssetManifest())
                    list.Items.Add($"{def.DisplayName}: (scan unavailable)");
            }
        }

        private void UpdateCharWizardReviewSummary()
        {
            var id = GetTb("TxtMapCharWizId");
            var name = GetTb("TxtMapCharWizName");
            var donor = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim() ?? "";
            var artBits = string.Join(", ",
                _mapCharOverlayPaths.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
            if (string.IsNullOrWhiteSpace(artBits)) artBits = "(no custom art — donor/placeholders only)";
            if (FindName("TxtMapCharReviewSummary") is TextBlock tb)
            {
                tb.Text = _mapCharMode switch
                {
                    CharacterWizardMode.Edit =>
                        $"Edit '{id}' ({name}). Slots with custom art: {artBits}. GUID unchanged.",
                    CharacterWizardMode.New =>
                        $"New '{id}' ({name}) from Mario placeholders. Art: {artBits}. Manager allocates GUID.",
                    _ =>
                        $"Clone '{donor}' → '{id}' ({name}). Art: {artBits}. Manager allocates GUID."
                };
            }
        }

        private void BtnMapStageBrowseSwf_Click(object sender, RoutedEventArgs e) =>
            BrowsePathInto("TxtMapStageSwf", "SWF|*.swf|All|*.*");
        private void BtnMapStageBrowsePng_Click(object sender, RoutedEventArgs e) =>
            BrowsePathInto("TxtMapStagePng", "PNG|*.png|All|*.*");
        private async void BtnMapMusicBrowseMp3_Click(object sender, RoutedEventArgs e)
        {
            BrowsePathInto("TxtMapMusicMp3", "Audio|*.mp3;*.wav|All|*.*");
            var path = GetTb("TxtMapMusicMp3");
            if (!string.IsNullOrWhiteSpace(path))
                await LoadMusicSourceDurationAsync(path);
        }

        private async System.Threading.Tasks.Task LoadMusicSourceDurationAsync(string path)
        {
            StopMusicPreview();
            _musicSourceDuration = null;
            if (FindName("TxtMapMusicDuration") is TextBlock dur)
                dur.Text = "Duration: reading…";
            if (FindName("TxtMapMusicPreviewStatus") is TextBlock st)
                st.Text = MusicEditService.FindFfmpeg() == null
                    ? "ffmpeg not found — reinstall the app (bundled runtime\\ffmpeg\\) or run scripts\\deps\\fetch-native-tools.ps1."
                    : "Loading waveform…";

            try
            {
                if (FindName("MusicWaveform") is AudioWaveformControl wave)
                {
                    await wave.LoadFromAudioFileAsync(path);
                    _musicSourceDuration = TimeSpan.FromMilliseconds(wave.DurationMs);
                    _syncingMusicMarkers = true;
                    wave.TrimStartMs = 0;
                    wave.TrimEndMs = wave.DurationMs;
                    wave.LoopMs = 0;
                    wave.PlayheadMs = 0;
                    SetTb("TxtMapMusicTrimStart", "0");
                    SetTb("TxtMapMusicTrimEnd", ((int)wave.DurationMs).ToString());
                    SetTb("TxtMapMusicLoop", "0");
                    _syncingMusicMarkers = false;
                    if (FindName("TxtMapMusicDuration") is TextBlock d)
                        d.Text = $"Duration: {(int)wave.DurationMs} ms";
                    if (FindName("TxtMapMusicPreviewStatus") is TextBlock s)
                        s.Text = "Drag markers on the waveform, or edit the ms fields.";
                }
            }
            catch (Exception ex)
            {
                if (FindName("TxtMapMusicDuration") is TextBlock d)
                    d.Text = "Duration: error — " + ex.Message;
            }
        }

        private void MusicWaveform_MarkersChanged(object? sender, EventArgs e)
        {
            if (_syncingMusicMarkers) return;
            if (FindName("MusicWaveform") is not AudioWaveformControl wave) return;
            _syncingMusicMarkers = true;
            SetTb("TxtMapMusicTrimStart", ((int)wave.TrimStartMs).ToString());
            SetTb("TxtMapMusicTrimEnd", ((int)wave.TrimEndMs).ToString());
            SetTb("TxtMapMusicLoop", ((int)wave.LoopMs).ToString());
            _syncingMusicMarkers = false;
        }

        private void TxtMapMusicMarker_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncingMusicMarkers) return;
            if (FindName("MusicWaveform") is not AudioWaveformControl wave) return;
            _syncingMusicMarkers = true;
            if (double.TryParse(GetTb("TxtMapMusicTrimStart"), out var ts)) wave.TrimStartMs = ts;
            if (double.TryParse(GetTb("TxtMapMusicTrimEnd"), out var te)) wave.TrimEndMs = te;
            if (double.TryParse(GetTb("TxtMapMusicLoop"), out var lp)) wave.LoopMs = lp;
            _syncingMusicMarkers = false;
        }

        private bool TryGetMusicTrimWindow(out int trimStartMs, out int trimEndMs, out string error)
        {
            trimStartMs = 0;
            trimEndMs = 0;
            error = "";
            if (!int.TryParse(GetTb("TxtMapMusicTrimStart"), out trimStartMs) || trimStartMs < 0)
            {
                error = "Trim start must be a non-negative integer (ms).";
                return false;
            }
            var endText = GetTb("TxtMapMusicTrimEnd");
            if (string.IsNullOrWhiteSpace(endText))
            {
                if (_musicSourceDuration is { } dur)
                    trimEndMs = (int)dur.TotalMilliseconds;
                else
                {
                    error = "Trim end is empty and song duration is unknown. Browse the MP3 again or enter trim end.";
                    return false;
                }
            }
            else if (!int.TryParse(endText, out trimEndMs) || trimEndMs <= 0)
            {
                error = "Trim end must be a positive integer (ms), or blank for song end.";
                return false;
            }

            if (trimEndMs <= trimStartMs)
            {
                error = "Trim end must be greater than trim start.";
                return false;
            }
            return true;
        }

        private void EnsureMusicPreviewTimer()
        {
            if (_musicPreviewTimer != null) return;
            _musicPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _musicPreviewTimer.Tick += (_, _) =>
            {
                if (!_musicPreviewPlayer.NaturalDuration.HasTimeSpan) return;
                var pos = _musicPreviewPlayer.Position;
                if (FindName("MusicWaveform") is AudioWaveformControl wave)
                    wave.PlayheadMs = pos.TotalMilliseconds;
                if (FindName("TxtMapMusicPreviewStatus") is TextBlock st)
                {
                    var mode = _musicPreviewLoopMode ? "Loop preview" : "Play trim";
                    st.Text = $"{mode}: {(int)pos.TotalMilliseconds} ms  ({pos:mm\\:ss\\.fff})";
                }

                if (pos >= _musicPreviewWindowEnd - TimeSpan.FromMilliseconds(30))
                {
                    if (_musicPreviewLoopMode)
                        _musicPreviewPlayer.Position = _musicPreviewLoopPoint;
                    else
                        StopMusicPreview(updateStatus: "Stopped at trim end.");
                }
            };
        }

        private void StopMusicPreview(string? updateStatus = null)
        {
            try { _musicPreviewPlayer.Stop(); } catch { /* ignore */ }
            try { _musicPreviewPlayer.Close(); } catch { /* ignore */ }
            _musicPreviewTimer?.Stop();
            _musicPreviewLoopMode = false;
            if (updateStatus != null && FindName("TxtMapMusicPreviewStatus") is TextBlock st)
                st.Text = updateStatus;
        }

        private void StartMusicPreview(bool loopMode)
        {
            var path = GetTb("TxtMapMusicMp3");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("Choose an MP3 first.", "Music preview", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!TryGetMusicTrimWindow(out var trimStartMs, out var trimEndMs, out var err))
            {
                MessageBox.Show(err, "Music preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(GetTb("TxtMapMusicLoop"), out var loopMs) || loopMs < 0) loopMs = 0;

            var windowStart = TimeSpan.FromMilliseconds(trimStartMs);
            var windowEnd = TimeSpan.FromMilliseconds(trimEndMs);
            var loopAbs = TimeSpan.FromMilliseconds(trimStartMs + loopMs);
            if (loopMode && (loopAbs < windowStart || loopAbs >= windowEnd))
            {
                MessageBox.Show(
                    "Loop start must fall inside the trimmed clip (0 … trim length).",
                    "Music preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Start from the waveform playhead (clamped into the trim window) so you can scrub near the end.
            var playheadMs = FindName("MusicWaveform") is AudioWaveformControl wave
                ? wave.PlayheadMs
                : trimStartMs;
            if (playheadMs < trimStartMs) playheadMs = trimStartMs;
            if (playheadMs >= trimEndMs)
                playheadMs = Math.Max(trimStartMs, trimEndMs - 1);

            StopMusicPreview();
            _musicPreviewLoopMode = loopMode;
            _musicPreviewLoopPoint = loopMode ? loopAbs : windowStart;
            _musicPreviewStartPoint = TimeSpan.FromMilliseconds(playheadMs);
            _musicPreviewWindowEnd = windowEnd;
            EnsureMusicPreviewTimer();

            _musicPreviewPlayer.MediaOpened += OnMusicPreviewOpened;
            _musicPreviewPlayer.MediaFailed += OnMusicPreviewFailed;
            _musicPreviewPlayer.Open(new Uri(path));
        }

        private void OnMusicPreviewOpened(object? sender, EventArgs e)
        {
            _musicPreviewPlayer.MediaOpened -= OnMusicPreviewOpened;
            _musicPreviewPlayer.Position = _musicPreviewStartPoint;
            _musicPreviewPlayer.Play();
            _musicPreviewTimer?.Start();
        }

        private void OnMusicPreviewFailed(object? sender, ExceptionEventArgs e)
        {
            _musicPreviewPlayer.MediaFailed -= OnMusicPreviewFailed;
            StopMusicPreview("Preview failed: " + (e.ErrorException?.Message ?? "unknown"));
        }

        private void BtnMapMusicPlayTrim_Click(object sender, RoutedEventArgs e) => StartMusicPreview(loopMode: false);
        private void BtnMapMusicPreviewLoop_Click(object sender, RoutedEventArgs e) => StartMusicPreview(loopMode: true);
        private void BtnMapMusicStop_Click(object sender, RoutedEventArgs e) =>
            StopMusicPreview("Stopped.");

        private void BtnMapMusicMarkLoop_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMusicTrimWindow(out var trimStartMs, out _, out var err))
            {
                MessageBox.Show(err, "Mark loop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var absMs = FindName("MusicWaveform") is AudioWaveformControl wave
                ? (int)wave.PlayheadMs
                : (int)_musicPreviewPlayer.Position.TotalMilliseconds;
            if (absMs < trimStartMs) absMs = trimStartMs;
            var rel = Math.Max(0, absMs - trimStartMs);
            SetTb("TxtMapMusicLoop", rel.ToString());
            if (FindName("MusicWaveform") is AudioWaveformControl w) w.LoopMs = rel;
            if (FindName("TxtMapMusicPreviewStatus") is TextBlock st)
                st.Text = $"Loop start set to {rel} ms into the trimmed clip (source {absMs} ms).";
        }

        private async void BtnMapCharFinish_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;

            if (!TryValidateCharWizardIdentity(out var err))
            {
                MessageBox.Show(err, "Clone Character", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var idRaw = GetTb("TxtMapCharWizId");
            string id;
            try { id = MenuSlotService.SanitizeId(idRaw); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Clone Character", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.Equals(idRaw.Trim(), id, StringComparison.Ordinal))
                SetTb("TxtMapCharWizId", id);

            var name = GetTb("TxtMapCharWizName");
            var donor = (FindName("CmbMapCharDonor") as ComboBox)?.Text?.Trim()
                        ?? RosterWizardService.DefaultNewUiDonorId;

            try
            {
                var createNewSeries = (FindName("ChkMapCharNewSeriesSymbol") as CheckBox)?.IsChecked == true;
                var seriesIcon = createNewSeries
                    ? (GetTb("TxtMapCharNewSeriesName").Trim().Length > 0
                        ? GetTb("TxtMapCharNewSeriesName").Trim()
                        : id + "_symbol")
                    : ((FindName("CmbMapCharSeriesIcon") as ComboBox)?.Text?.Trim() ?? "smash_symbol");
                var seriesTemplate = (FindName("CmbMapCharSeriesTemplate") as ComboBox)?.Text?.Trim()
                                     ?? (FindName("CmbMapCharSeriesIcon") as ComboBox)?.Text?.Trim()
                                     ?? "smash_symbol";

                var req = new CharacterWizardRequest
                {
                    Mode = CharacterWizardMode.Clone,
                    SourceSwfPath = GetTb("TxtMapCharSwf"),
                    Id = id,
                    DonorId = donor,
                    DonorChipId = donor,
                    DisplayName = string.IsNullOrWhiteSpace(name) ? id : name,
                    SeriesIcon = string.IsNullOrWhiteSpace(seriesIcon) ? "smash_symbol" : seriesIcon,
                    CreateNewSeriesSymbol = createNewSeries,
                    SeriesSymbolTemplate = string.IsNullOrWhiteSpace(seriesTemplate) ? "smash_symbol" : seriesTemplate,
                    SeriesSymbolPng = NullIfEmpty(GetTb("TxtMapCharSeriesPng")),
                    NewCssRow = (FindName("ChkMapCharNewRow") as CheckBox)?.IsChecked == true,
                    AddToRandom = (FindName("ChkMapCharRandom") as CheckBox)?.IsChecked == true,
                    WritePackageFolder = true,
                    ExportFla = true
                };

                WizardResult? result = null;
                await RunBusyWorkAsync("Clone Character", async progress =>
                {
                    var steps = new WizardStepProgress(progress, WizardStepProgress.CloneCharacterSteps);
                    result = await _rosterWizard.RunCharacterWizardAsync(
                        root!, _mappingsDoc, req, null, steps.AsStringProgress(), default, steps);
                    progress.Report(("Refreshing menu symbols…", 97, "Almost done"));
                    _menuSymbolIndex = await _menuSlots.LoadSymbolIndexAsync(root!);
                    steps.Finish(result?.Message ?? "Done");
                });
                PopulateGuidedDropdowns();
                var pkgHint = CharacterPackageService.PackageFolderPath(root!, req.DisplayName);
                var msg = (result?.Message ?? "Done.")
                          + $"\n\nPackage folder:\n{pkgHint}"
                          + (string.IsNullOrEmpty(result?.Warning) ? "" : "\n\n" + result!.Warning);
                MessageBox.Show(msg, "Clone Character", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMapPanel("hub");
            }
            catch (Exception ex)
            {
                HideOverlay();
                MessageBox.Show(ex.Message, "Clone Character", MessageBoxButton.OK, MessageBoxImage.Error);
                SetMappingsStatus("Error: " + ex.Message);
            }
        }

        private async void BtnMapStageFinish_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            var swf = GetTb("TxtMapStageSwf");
            var id = GetTb("TxtMapStageWizId");
            var name = GetTb("TxtMapStageWizName");
            if (string.IsNullOrWhiteSpace(swf) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("SWF, id, and display name are required.", "Add stage", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var req = new StageWizardRequest
                {
                    SourceSwfPath = swf,
                    Id = id,
                    DisplayName = name,
                    StageSelectPng = NullIfEmpty(GetTb("TxtMapStagePng")),
                    NewSssRow = (FindName("ChkMapStageNewRow") as CheckBox)?.IsChecked == true,
                    TrainingOnly = (FindName("ChkMapStageTraining") as CheckBox)?.IsChecked == true,
                    AddToRandom = (FindName("ChkMapStageRandom") as CheckBox)?.IsChecked == true
                };
                WizardResult? result = null;
                await RunBusyWorkAsync("Adding stage", async progress =>
                {
                    progress.Report(("Cloning / registering stage…", 15, req.Id));
                    result = await _rosterWizard.AddStageAsync(
                        root!, _mappingsDoc, req, null,
                        new Progress<string>(m => progress.Report((m, 55, null))));
                    progress.Report(("Refreshing menu symbols…", 85, null));
                    _menuSymbolIndex = await _menuSlots.LoadSymbolIndexAsync(root!);
                    progress.Report(("Done.", 100, null));
                });
                PopulateGuidedDropdowns();
                var msg = (result?.Message ?? "Done.") + (string.IsNullOrEmpty(result?.Warning) ? "" : "\n\n" + result!.Warning);
                MessageBox.Show(msg, "Add stage", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMapPanel("hub");
            }
            catch (Exception ex)
            {
                HideOverlay();
                MessageBox.Show(ex.Message, "Add stage", MessageBoxButton.OK, MessageBoxImage.Error);
                SetMappingsStatus("Error: " + ex.Message);
            }
        }

        private async void BtnMapMusicFinish_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            var mp3 = GetTb("TxtMapMusicMp3");
            var id = GetTb("TxtMapMusicWizId");
            if (string.IsNullOrWhiteSpace(mp3) || string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("MP3 and song id are required.", "Add song", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(GetTb("TxtMapMusicLoop"), out var loopMs) || loopMs < 0) loopMs = 0;
            if (!TryGetMusicTrimWindow(out var trimStartMs, out var trimEndMs, out var trimErr))
            {
                MessageBox.Show(trimErr, "Add song", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var stages = new List<string>();
            if (FindName("ListMapMusicStages") is ListBox lst)
            {
                foreach (var item in lst.SelectedItems)
                    if (item != null) stages.Add(item.ToString()!);
            }
            try
            {
                StopMusicPreview();
                string musicId = "";
                await RunBusyWorkAsync("Creating song", async progress =>
                {
                    progress.Report(("Preparing audio / cloning template…", 10, id));
                    var outPath = await _musicEdit.CreateMusicFromMp3Async(
                        root!, mp3, id, loopMs,
                        GetTb("TxtMapMusicSongName"), GetTb("TxtMapMusicArtist"),
                        _mappingsDoc, null,
                        new Progress<string>(m => progress.Report((m, 45, null))),
                        CancellationToken.None, trimStartMs, trimEndMs);
                    musicId = Path.GetFileNameWithoutExtension(outPath);

                    var i = 0;
                    foreach (var stageId in stages)
                    {
                        i++;
                        progress.Report(($"Attaching to stage {stageId}…", 55 + (30.0 * i / Math.Max(1, stages.Count)), stageId));
                        await _musicEdit.AppendTrackToStageAsync(
                            root!, _mappingsDoc, stageId, musicId, null,
                            new Progress<string>(m => progress.Report((m, 70, stageId))));
                    }

                    progress.Report(("Applying mappings.swf…", 90, null));
                    await _mappingsEdit.CommitAsync(root!, _mappingsDoc, null,
                        new Progress<string>(m => progress.Report((m, 95, null))));
                    progress.Report(("Refreshing music cache…", 97, null));
                    _musicCache = await _musicCacheSvc.BuildOrRefreshAsync(
                        root!, _mappingsDoc!, forceFull: false,
                        progress: new Progress<MusicCacheProgress>(p =>
                            SetMappingsStatus(p.Total > 0 ? $"Cache {p.Done}/{p.Total}" : p.Message)));
                    progress.Report(("Done.", 100, musicId));
                });
                PopulateGuidedDropdowns();
                MessageBox.Show($"Song '{musicId}' created and mappings applied." +
                                (stages.Count > 0 ? $" Attached to {stages.Count} stage(s)." : " Register-only (attach later)."),
                    "Add song", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowMapPanel("hub");
            }
            catch (Exception ex)
            {
                HideOverlay();
                MessageBox.Show(ex.Message, "Add song", MessageBoxButton.OK, MessageBoxImage.Error);
                SetMappingsStatus("Error: " + ex.Message);
            }
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private void BtnMappingsOpenJson_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                MappingsEditService.OpenInDefaultApp(MappingsEditService.GetPlaintextPath(root!));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open JSON", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnMappingsExplorer_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                MappingsEditService.OpenInExplorer(Path.Combine(root!, "data", "misc"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnMappingsReload_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                await _mappingsEdit.ExportPlaintextAsync(root!);
                _mappingsDoc = await _mappingsEdit.LoadDocumentAsync(root!, exportIfMissing: true);
                PopulateGuidedDropdowns();
                SetMappingsStatus("Reloaded mappings from SWF.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Reload", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnMappingsValidate_Click(object sender, RoutedEventArgs e)
        {
            var root = GetMappingsInstallRoot();
            if (string.IsNullOrWhiteSpace(root) || _mappingsDoc == null) return;
            try
            {
                _menuSymbolIndex ??= await _menuSlots.LoadSymbolIndexAsync(root!);
                var issues = _mappingsEdit.Validate(_mappingsDoc, root!, _menuSymbolIndex);
                if (FindName("ListMappingsIssues") is ListBox list)
                {
                    list.Items.Clear();
                    foreach (var issue in issues)
                        list.Items.Add($"[{issue.Severity}] {issue.Category}/{issue.Id}: {issue.Message}");
                    list.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                var errors = issues.Count(i => i.Severity == "error");
                MessageBox.Show(
                    issues.Count == 0 ? "No issues found." : $"{issues.Count} issue(s), {errors} error(s). See list below Advanced.",
                    "Validation", MessageBoxButton.OK,
                    errors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Validation", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }

}
