using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Models;
using SSF2ModManager.Services;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;

namespace SSF2ModManager.Views
{
    public partial class LauncherWindow : Window
    {
        private readonly SettingsService _settings = new();
        private static ThemeManager? _themes;
        public static ThemeManager Themes => _themes ??= new ThemeManager();

        internal static void DisposeThemes()
        {
            try { _themes?.Dispose(); } catch { }
            _themes = null;
        }
        private bool _busy;
        private bool _workLock;
        private DispatcherTimer? _workTimer;
        private static LauncherLogWindow? _logWindow;
        private DispatcherTimer? _alertTimer;
        private DispatcherTimer? _gameTimer;
        private string? _lastAlertStamp;
        private bool _sawGame;
        private bool _logOpenedByFollow;
        private readonly List<Window> _hostedWindows = new();
        private readonly List<Crumb> _crumbs = new();
        private MakeModsWindow? _makeMods;
        private ManageModsWindow? _manageMods;
        private string? _installFolderPath;

        public LauncherWindow()
        {
            InitializeComponent();
            TxtVersion.Text = AppInfo.DisplayVersion;
            var theme = _settings.GetTheme();
            if (!string.IsNullOrWhiteSpace(theme))
                Themes.ApplyTheme(theme);
            LoadInstallFolder();
            ApplyPathBlur();
            SettingsService.Changed += OnSettingsChanged;
            RefreshPlayLabel();
            Activated += (_, _) => RefreshPlayLabel();
            SetStatus("Ready", "Pick Play to start SSF2.");
            ContentRendered += OnFirstRender;
            Closing += (_, e) =>
            {
                if (_busy || _workLock)
                    e.Cancel = true;
            };
            Closed += (_, _) =>
            {
                SettingsService.Changed -= OnSettingsChanged;
                if (_alertTimer != null)
                {
                    _alertTimer.Stop();
                    _alertTimer = null;
                }
                if (_gameTimer != null)
                {
                    _gameTimer.Stop();
                    _gameTimer = null;
                }
                try { _logWindow?.Close(); } catch { }
                try { ReleaseHosted(); } catch { }
                App.RequestFullExit();
            };
        }

        private void OnFirstRender(object? sender, EventArgs e)
        {
            ContentRendered -= OnFirstRender;
            StartAlertWatcher();
            StartGameWatcher();
            if (_settings.GetUpdateAutomatically())
                _ = RunUpdateAsync(download: true, quiet: true, lockUi: false);
            if (SetupService.IsNeeded(SetupService.Read(_settings)))
                OpenSetup();
            else
                _ = OfferShellBaseAsync();
        }

        public void OpenSetup()
        {
            var setup = new SetupWindow { Owner = this };
            setup.ShowDialog();
            if (setup.ShellBuilt)
                RefreshPlayLabel();
        }

        private async Task OfferShellBaseAsync()
        {
            ShellBaseService.Release? latest;
            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell == null || ShellBaseService.IsDevShell(shell))
            {
                DebugLogger.Log("[shellbase] skip update offer shell=" + (shell ?? "none"));
                return;
            }
            try
            {
                latest = await ShellBaseService.GetLatestAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[shellbase] latest check failed: " + ex.Message);
                return;
            }
            var installed = ShellBaseService.InstalledVersion(shell);
            DebugLogger.Log($"[shellbase] latest={latest?.Version ?? "none"} installed={installed ?? "none"} shell={shell ?? "none"}");
            if (latest == null || installed == latest.Version || _busy)
                return;

            var question = $"SSF2 Shell {latest.Version} is available (installed: {installed ?? "unknown"}). Rebuild now? Your mods and settings are kept.";
            if (MessageBox.Show(question, "SSF2 Shell", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var vanilla = SetupService.FindVanilla(_settings.GetVanillaSsf2Folder());
            if (ShellBaseService.CheckVanilla(vanilla) != ShellBaseService.VanillaCheck.Ok)
            {
                OpenSetup();
                return;
            }
            SetBusy(true);
            Progress.IsIndeterminate = true;
            try
            {
                await ShellBaseService.BuildAsync(latest, vanilla!, shell, new Progress<string>(s => SetStatus("SSF2 Shell", s)));
                SetStatus("SSF2 Shell ready", $"Version {latest.Version} installed.", 1);
                RefreshPlayLabel();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[shellbase] build fail " + ex.Message);
                SetStatus("SSF2 Shell build failed", ex.Message);
                MessageBox.Show(ex.Message, "SSF2 Shell", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                Progress.IsIndeterminate = false;
            }
        }

        private void StartAlertWatcher()
        {
            if (_alertTimer != null)
                return;
            _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _alertTimer.Tick += (_, _) => PollGameAlert();
            _alertTimer.Start();
        }

        private void PollGameAlert()
        {
            try
            {
                var shell = ResolveShellRoot();
                if (string.IsNullOrWhiteSpace(shell))
                    return;
                var path = Path.Combine(ShellWorkspaceService.StateDirectory(shell), "alert.json");
                if (!File.Exists(path))
                    return;
                string raw;
                try
                {
                    raw = File.ReadAllText(path);
                }
                catch
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(raw))
                    return;
                JObject? obj;
                try
                {
                    obj = JObject.Parse(raw);
                }
                catch
                {
                    return;
                }
                var type = (string?)obj["type"];
                if (!string.Equals(type, "uncaught", StringComparison.OrdinalIgnoreCase))
                    return;
                var stamp = (string?)obj["stamp"] ?? raw;
                if (stamp == _lastAlertStamp)
                    return;
                _lastAlertStamp = stamp;
                try { File.Delete(path); } catch { }
                BringToFront();
                var msg = (string?)obj["message"] ?? "";
                var meaning = (string?)obj["meaning"] ?? "";
                var errorId = (string?)obj["errorId"] ?? "";
                var logPath = (string?)obj["logPath"] ?? "";
                if (string.IsNullOrWhiteSpace(logPath) && !string.IsNullOrWhiteSpace(shell))
                    logPath = Path.Combine(ShellWorkspaceService.Ssf2mmDirectory(shell), "game.log");
                OpenLogWindow(ssf2ErrorsOnly: true);
                ShowUncaughtErrorNotify(msg, meaning, errorId, logPath);
                if (!string.IsNullOrWhiteSpace(msg))
                    SetStatus("Game uncaught error", msg.Length > 120 ? msg[..120] + "…" : msg);
            }
            catch
            {
            }
        }

        private void ShowUncaughtErrorNotify(string message, string meaning, string errorId, string logPath)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrWhiteSpace(errorId) && errorId != "0")
                    sb.Append("Error #").Append(errorId);
                else
                    sb.Append("Uncaught error");
                sb.AppendLine();
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(meaning))
                    sb.AppendLine(meaning.Trim());
                else if (!string.IsNullOrWhiteSpace(message))
                    sb.AppendLine(message.Split('\n')[0].Trim());
                sb.AppendLine();
                sb.AppendLine("Check the log for more details:");
                sb.Append(string.IsNullOrWhiteSpace(logPath)
                    ? "data/ssf2mm/game.log"
                    : logPath);
                MessageBox.Show(
                    sb.ToString().TrimEnd(),
                    "SSF2 Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        private void BringToFront()
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    SetForegroundWindow(hwnd);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void LoadInstallFolder()
        {
            var saved = _settings.GetLauncherInstallFolder();
            if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
            {
                SetInstallFolder(saved, save: false);
                return;
            }

            var exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var nestedShell = Path.Combine(exeDir, "SSF2 Shell");
            if (Directory.Exists(nestedShell) || File.Exists(Path.Combine(exeDir, "SSF2ModManager.exe")))
            {
                SetInstallFolder(exeDir);
                return;
            }

            var shell = ShellWorkspaceService.FindShellFolder();
            if (!string.IsNullOrWhiteSpace(shell))
            {
                var parent = Directory.GetParent(shell)?.FullName;
                var root = parent != null && File.Exists(Path.Combine(parent, "SSF2ModManager.exe"))
                    ? parent
                    : shell;
                SetInstallFolder(root);
                return;
            }

            SetInstallFolder(exeDir);
        }

        private void SetInstallFolder(string path, bool save = true)
        {
            _installFolderPath = path;
            TxtInstallFolder.Text = _settings.GetBlurFilePaths() ? "SSF2 Shell" : path;
            if (save)
                _settings.SetLauncherInstallFolder(path);
            ApplyPathBlur();
        }

        private void OnSettingsChanged()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _settings.Reload();
                if (!string.IsNullOrWhiteSpace(_installFolderPath))
                    SetInstallFolder(_installFolderPath, save: false);
            });
        }

        private void ApplyPathBlur()
        {
            TxtInstallFolder.Effect = null;
        }

        private string InstallRoot =>
            string.IsNullOrWhiteSpace(_installFolderPath)
                ? AppDomain.CurrentDomain.BaseDirectory
                : _installFolderPath.Trim();

        private string? ResolveShellRoot()
        {
            var root = InstallRoot;
            if (ShellWorkspaceService.IsShellFolder(root))
                return root;
            var nested = Path.Combine(root, "SSF2 Shell");
            if (ShellWorkspaceService.IsShellFolder(nested))
                return nested;
            return ShellWorkspaceService.FindShellFolder();
        }

        public void ReceiveOneClick(string url, string? modName = null) => _ = ReceiveOneClickAsync(url, modName);

        private System.Threading.CancellationTokenSource? _downloadCancel;

        private void BtnBusyCancel_Click(object sender, RoutedEventArgs e)
        {
            BtnBusyCancel.IsEnabled = false;
            TxtBusy.Text = "Canceling…";
            _downloadCancel?.Cancel();
        }

        private static async System.Threading.Tasks.Task SaveGameBananaShotsAsync(GameBananaMod? details, OneClickReceipt receipt)
        {
            var images = details?.PreviewMedia?.Images;
            if (images == null || images.Count == 0)
                return;
            var folder = Path.Combine(receipt.ExtractedRoot, OneClickReceive.GameBananaShotsFolder);
            Directory.CreateDirectory(folder);
            using var api = new GameBananaApiClient();
            var n = 0;
            foreach (var image in images.Take(8))
            {
                try
                {
                    var bytes = await api.GetBytesAsync(image.FullUrl);
                    var ext = Path.GetExtension(image.File);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                    await File.WriteAllBytesAsync(Path.Combine(folder, $"screenshot {++n}{ext}"), bytes);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("[oneclick] screenshot skip " + ex.Message);
                }
            }
        }

        private async System.Threading.Tasks.Task ReceiveOneClickAsync(string url, string? modName)
        {
            if (!ProtocolService.TryParse(url, out var req))
            {
                MessageBox.Show("That 1-click link could not be read.", "1-Click Install",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GameBananaUrlHelper.TryExtractFileId(req.ArchiveUrl, out var fileId);
            DebugLogger.Log($"[oneclick] receive file={fileId}");
            var shell = ResolveShellRoot();
            if (string.IsNullOrWhiteSpace(shell))
            {
                MessageBox.Show("SSF2 Shell folder was not found.", "1-Click Install",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OneClickReceipt? receipt = null;
            string? download = null;
            GameBananaMod? details = null;
            try
            {
                Activate();
                LockForWork(true, "Checking the download on GameBanana…");
                using (var api = new GameBananaApiClient())
                {
                    if (fileId > 0)
                    {
                        try
                        {
                            var list = await api.GetRawFileListAsync(fileId);
                            var game = list.Count(p => OneClickReceive.RawListHasGameFiles(new[] { p }));
                            DebugLogger.Log($"[oneclick] rawlist file={fileId} files={list.Count} game={game}");
                            if (!OneClickReceive.RawListHasGameFiles(list))
                            {
                                LockForWork(false, "");
                                SetStatus("Not installed", "No SSF2 game files in the download.");
                                MessageBox.Show(OneClickReceive.NothingInFileListMessage(list, modName),
                                    "Can't install this mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            DebugLogger.Log("[oneclick] rawlist skip " + ex.Message);
                        }
                    }
                    if (req.ModId is int modId && modId > 0)
                    {
                        try { details = await api.GetModDetailsAsync(modId); }
                        catch (Exception ex) { DebugLogger.Log("[oneclick] details skip " + ex.Message); }
                    }
                }
                if (string.IsNullOrWhiteSpace(modName))
                    modName = details?.Name;

                LockForWork(true, "Downloading…");
                BusyProgress.IsIndeterminate = false;
                BusyProgress.Value = 0;
                var downloadProgress = new Progress<OneClickReceive.DownloadProgress>(p =>
                {
                    TxtBusy.Text = OneClickReceive.FormatDownload(p.Received, p.Total)
                        + "\nSaving to a temporary folder: " + OneClickReceive.TempFolderPattern;
                    if (p.Total > 0)
                    {
                        BusyProgress.IsIndeterminate = false;
                        BusyProgress.Maximum = 1;
                        BusyProgress.Value = (double)p.Received / p.Total;
                    }
                    else
                    {
                        BusyProgress.IsIndeterminate = true;
                    }
                });
                _downloadCancel = new System.Threading.CancellationTokenSource();
                BtnBusyCancel.IsEnabled = true;
                BtnBusyCancel.Visibility = Visibility.Visible;
                try
                {
                    download = await OneClickReceive.DownloadAsync(req.ArchiveUrl, downloadProgress, _downloadCancel.Token);
                }
                finally
                {
                    BtnBusyCancel.Visibility = Visibility.Collapsed;
                }
                LockForWork(true, "Reading the download…");
                receipt = OneClickReceive.ClassifyArchive(download);
                if (!receipt.IsPack)
                {
                    LockForWork(true, "Looking for alts of characters you already have…");
                    var taken = await OneClickReceive.TakenCharacterIdsAsync(shell);
                    var classified = receipt;
                    await System.Threading.Tasks.Task.Run(() => OneClickReceive.ClassifyAlts(classified, taken));
                }
                LockForWork(false, "");
                var nothing = OneClickReceive.NothingToInstallMessage(receipt, modName);
                if (nothing != null)
                {
                    SetStatus("Not installed", "No supported files in the download.");
                    MessageBox.Show(nothing, "Can't install this mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (receipt.Items.Any(i => i.Kind == OneClickKind.Stage) && receipt.DeleteExtractOnCleanup)
                {
                    LockForWork(true, "Getting GameBanana screenshots…");
                    await SaveGameBananaShotsAsync(details, receipt);
                }
                LockForWork(false, "");
                var dlg = new OneClickReceiveWindow(receipt, modName) { Owner = this };
                if (dlg.ShowDialog() != true)
                    return;
                LockForWork(true, "Importing…");
                var progress = new Progress<string>(message => TxtBusy.Text = message);
                await OneClickReceive.InstallAsync(shell, receipt, dlg.Chosen, progress,
                    gameBananaModId: req.ModId ?? 0, gameBananaFileId: fileId, modName: modName);
                LockForWork(false, "");
                (PageContent.Content as DownloadModsPage)?.RefreshInstalled();
                SetStatus("1-Click", "Import finished.");
                MessageBox.Show("Import finished.", "1-Click Install",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) when (_downloadCancel?.IsCancellationRequested == true)
            {
                LockForWork(false, "");
                DebugLogger.Log($"[oneclick] canceled file={fileId}");
                SetStatus("Download canceled", OneClickReceive.TempNote(true));
            }
            catch (Exception ex)
            {
                LockForWork(false, "");
                DebugLogger.Error("1-click receive failed", ex);
                MessageBox.Show(ex.Message, "1-Click Install",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                receipt?.Cleanup();
                if (!string.IsNullOrWhiteSpace(download))
                {
                    try { Directory.Delete(Path.GetDirectoryName(download)!, true); } catch { }
                }
            }
        }

        private void SetStatus(string status, string? detail = null, double? progress = null)
        {
            TxtStatus.Text = status;
            TxtStatusDetail.Text = detail ?? "";
            if (progress.HasValue)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = Math.Clamp(progress.Value, 0, 1);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (busy)
                Progress.IsIndeterminate = true;
        }

        public void LockForWork(bool on, string message)
        {
            _workLock = on;
            BusyOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            TxtBusy.Text = string.IsNullOrWhiteSpace(message) ? "Working…" : message;
            HomeView.IsEnabled = !on;
            PageHost.IsEnabled = !on;
            if (on)
            {
                _workTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _workTimer.Tick -= OnWorkTick;
                _workTimer.Tick += OnWorkTick;
                _workTimer.Start();
            }
            else
            {
                _workTimer?.Stop();
                BusySpin.Angle = 0;
            }
        }

        private void OnWorkTick(object? sender, EventArgs e)
        {
            BusySpin.Angle = (BusySpin.Angle + 8) % 360;
        }

        private void BtnChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select the SSF2 Mod Manager install folder",
                UseDescriptionForTitle = true,
                SelectedPath = InstallRoot
            };
            if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
            SetInstallFolder(dlg.SelectedPath);
            SetStatus("Install folder updated", dlg.SelectedPath);
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e) =>
            await RunUpdateAsync(download: true, quiet: false, lockUi: true);

        private async Task RunUpdateAsync(bool download, bool quiet, bool lockUi)
        {
            if (_busy) return;
            if (lockUi) SetBusy(true);
            else Progress.IsIndeterminate = true;
            try
            {
                var progress = new Progress<(string status, double? fraction)>(p =>
                {
                    SetStatus(p.status, null, p.fraction);
                    if (p.fraction.HasValue)
                        Progress.IsIndeterminate = false;
                });
                var result = await LauncherUpdateService.CheckAndUpdateAsync(InstallRoot, download, progress);
                SetStatus(result.UpToDate ? "Up to date" : (download ? "Update finished" : "Update available"),
                    result.Message, result.UpToDate || download ? 1 : null);
                if (!quiet && !result.Ok)
                    MessageBox.Show(result.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                SetStatus("Update failed", ex.Message);
                if (!quiet)
                    MessageBox.Show(ex.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (lockUi) SetBusy(false);
                Progress.IsIndeterminate = false;
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shell = ResolveShellRoot();
                if (shell == null)
                {
                    MessageBox.Show("Could not find SSF2 Shell. Use Change to pick the install folder.",
                        "Play", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var prefs = _settings.GetLaunchConfigure();
                ShowLogBeforeGame();
                if (prefs.UseOnEveryLaunch)
                {
                    ControllerProfilesSyncService.MergeIntoPrefs(shell, prefs);
                    ControllerProfilesSyncService.WriteFromPrefs(shell, prefs);
                    ShellConfigureLaunchService.Launch(shell, prefs);
                }
                else
                    ShellDirectLaunchService.Launch(shell);
                SetStatus("Playing", "Launched SSF2.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Play", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnConfigure_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shell = ResolveShellRoot();
                if (shell == null)
                {
                    MessageBox.Show("Could not find SSF2 Shell. Use Change to pick the install folder.",
                        "Configure", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var dlg = new ConfigureLaunchWindow(_settings, shell)
                {
                    IsHosted = true
                };
                dlg.HostedClose += (_, _) =>
                {
                    RefreshPlayLabel();
                    LeavePage();
                };
                ShowHosted("Configure", dlg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Configure", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnDebug_Click(object sender, RoutedEventArgs e) =>
            await RunStreamCommandAsync("ssf2Stream.launchDebug", "Debug");

        private async Task RunStreamCommandAsync(string command, string label)
        {
            if (_busy) return;
            SetBusy(true);
            SetStatus(label, "Opening editor + SSF2 Stream…");
            try
            {
                var status = ModdingToolchainService.GetStatus();
                if (!status.StreamInstalled || !status.As3mxmlInstalled || ModdingToolchainService.BundledStreamNeedsInstall())
                {
                    var setup = await ModdingToolchainService.SetupModdingToolsAsync(
                        new Progress<string>(s => SetStatus(label, s)));
                    if (!setup.Ok)
                    {
                        MessageBox.Show(setup.Message, label, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var (ok, msg) = await ModdingToolchainService.OpenShellAndRunStreamCommandAsync(command);
                SetStatus(label, msg, 1);
                if (!ok)
                    MessageBox.Show(msg, label, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, label, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                Progress.IsIndeterminate = false;
            }
        }

        private void BtnMakeMods_Click(object sender, RoutedEventArgs e)
        {
            var shell = ResolveShellRoot();
            if (shell == null)
            {
                MessageBox.Show("Could not find SSF2 Shell. Use Change to pick the install folder.",
                    "Make Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _makeMods = new MakeModsWindow(shell);
            _makeMods.SectionChanged += SyncMakeTrail;
            ShowHosted("Make Mods", _makeMods);
            SyncMakeTrail();
        }

        private void BtnManageMods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shell = ResolveShellRoot();
                if (shell == null)
                {
                    MessageBox.Show("Could not find SSF2 Shell. Use Change to pick the install folder.",
                        "Manage Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _manageMods = new ManageModsWindow(shell) { IsHosted = true };
                _manageMods.HostedClose += (_, _) => LeavePage();
                _manageMods.SectionChanged += SyncManageTrail;
                ShowHosted("Manage Mods", _manageMods);
                SyncManageTrail();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Manage Mods", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDownloadMods_Click(object sender, RoutedEventArgs e)
        {
            var shell = ResolveShellRoot();
            if (shell == null)
            {
                MessageBox.Show("Could not find SSF2 Shell. Use Change to pick the install folder.",
                    "Download Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ShowPage("Download Mods", new DownloadModsPage(shell));
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowHosted("Settings", new SettingsWindow());
        }

        private void BtnCredits_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Credits", new CreditsPage(ResolveShellRoot()));
        }

        private void BtnNews_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("News", new NewsPage());
        }

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/yVPkqKQsx2") { UseShellExecute = true });
        }

        private void BtnFeedback_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://forms.gle/hhJgq2jsRFkGoysU9") { UseShellExecute = true });
        }

        private void BtnSupport_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/justwex") { UseShellExecute = true });
        }

        private WikiWindow? _wikiWindow;

        private void BtnOpenDocs_Click(object sender, RoutedEventArgs e)
        {
            if (_wikiWindow != null)
            {
                if (_wikiWindow.WindowState == WindowState.Minimized)
                    _wikiWindow.WindowState = WindowState.Normal;
                _wikiWindow.Activate();
                return;
            }
            _wikiWindow = new WikiWindow(InstallRoot);
            _wikiWindow.Closed += (_, _) => _wikiWindow = null;
            _wikiWindow.Show();
        }

        private static string FormatLogError(Exception ex)
        {
            var inner = ex;
            while (inner.InnerException != null)
                inner = inner.InnerException;
            return inner == ex ? ex.Message : ex.Message + "\n\n" + inner.Message;
        }

        private void RefreshPlayLabel()
        {
            if (BtnPlay == null)
                return;
            BtnPlay.Content = _settings.GetLaunchConfigure().UseOnEveryLaunch ? "Play*" : "Play";
        }

        private void LinkOpenLog_Click(object sender, RoutedEventArgs e) => OpenLogWindow();

        /// <summary>
        /// Opens the log first when "open with SSF2" is on, without taking focus,
        /// so the game window that starts next stays on top.
        /// </summary>
        public static void ShowLogBeforeGame()
        {
            foreach (Window w in System.Windows.Application.Current.Windows)
            {
                if (w is LauncherWindow launcher)
                {
                    launcher.ShowLogBeforeGameInstance();
                    return;
                }
            }
        }

        private void ShowLogBeforeGameInstance()
        {
            if (!_settings.GetOpenLogWithGame())
                return;
            if (_logWindow == null)
            {
                OpenLogWindow(activate: false);
                _logOpenedByFollow = true;
            }
            _sawGame = true;
        }

        private void OpenLogWindow(bool ssf2ErrorsOnly = false, bool activate = true)
        {
            try
            {
                DevFileLog.SetEnabled(true);
                var shell = ResolveShellRoot();
                if (_logWindow != null)
                {
                    if (_logWindow.WindowState == WindowState.Minimized)
                        _logWindow.WindowState = WindowState.Normal;
                    if (ssf2ErrorsOnly)
                        _logWindow.ShowSsf2ErrorsOnly();
                    _logWindow.SetFollow(_settings.GetOpenLogWithGame());
                    if (activate)
                        _logWindow.Activate();
                    return;
                }
                _logWindow = new LauncherLogWindow(shell, ssf2ErrorsOnly);
                _logWindow.ShowActivated = activate;
                _logWindow.SetFollow(_settings.GetOpenLogWithGame());
                _logWindow.FollowChanged += (_, on) => _settings.SetOpenLogWithGame(on);
                _logWindow.Closed += (_, _) =>
                {
                    _logWindow = null;
                };
                _logWindow.Show();
                if (activate)
                    _logWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(FormatLogError(ex), "Open log", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StartGameWatcher()
        {
            if (_gameTimer != null)
                return;
            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _gameTimer.Tick += (_, _) => PollGamePresence();
            _gameTimer.Start();
        }

        private void PollGamePresence()
        {
            var running = IsSsf2Running();
            if (!_settings.GetOpenLogWithGame())
            {
                _sawGame = false;
                return;
            }
            if (running && !_sawGame)
            {
                _sawGame = true;
                if (_logWindow == null)
                {
                    OpenLogWindow(activate: false);
                    _logOpenedByFollow = true;
                    RaiseGameWindow();
                }
            }
            else if (!running && _sawGame)
            {
                _sawGame = false;
                if (_logOpenedByFollow)
                {
                    _logOpenedByFollow = false;
                    try { _logWindow?.Close(); } catch { }
                }
            }
        }

        private static void RaiseGameWindow()
        {
            try
            {
                foreach (var name in new[] { "Super Smash Flash 2 Beta", "Super Smash Flash 2", "SSF2", "ssf2" })
                {
                    Process[] procs;
                    try { procs = Process.GetProcessesByName(name); }
                    catch { continue; }
                    foreach (var proc in procs)
                    {
                        var hwnd = proc.MainWindowHandle;
                        if (hwnd == IntPtr.Zero)
                            continue;
                        SetForegroundWindow(hwnd);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsSsf2Running()
        {
            foreach (var name in new[] { "Super Smash Flash 2 Beta", "Super Smash Flash 2", "SSF2", "ssf2" })
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                        return true;
                }
                catch
                {
                }
            }
            return false;
        }

        private void ShowPage(string title, UIElement page)
        {
            ReleaseHosted();
            _crumbs.Clear();
            _crumbs.Add(new Crumb("Home", LeavePage));
            _crumbs.Add(new Crumb(title, () => { }));
            PageContent.Content = page;
            ShowPageChrome();
        }

        private void ShowHosted(string title, Window window)
        {
            ReleaseHosted();
            var page = window.Content as UIElement ?? new TextBlock { Text = title };
            window.Content = null;
            _hostedWindows.Add(window);
            _crumbs.Clear();
            _crumbs.Add(new Crumb("Home", LeavePage));
            _crumbs.Add(new Crumb(title, () => { }));
            PageContent.Content = page;
            ShowPageChrome();
        }

        private void SyncMakeTrail()
        {
            if (_makeMods == null || !_hostedWindows.Contains(_makeMods))
                return;
            _crumbs.Clear();
            _crumbs.Add(new Crumb("Home", LeavePage));
            foreach (var step in _makeMods.GetTrail())
            {
                var id = step.Id;
                _crumbs.Add(new Crumb(step.Label, () => _makeMods.OpenPanel(id)));
            }
            RenderCrumbs();
        }

        private void SyncManageTrail()
        {
            if (_manageMods == null || !_hostedWindows.Contains(_manageMods))
                return;
            _crumbs.Clear();
            _crumbs.Add(new Crumb("Home", LeavePage));
            foreach (var step in _manageMods.GetTrail())
            {
                var id = step.Id;
                _crumbs.Add(new Crumb(step.Label, () => _manageMods.OpenSection(id)));
            }
            RenderCrumbs();
        }

        private void ShowPageChrome()
        {
            HomeView.Visibility = Visibility.Collapsed;
            PageHost.Visibility = Visibility.Visible;
            RenderCrumbs();
        }

        private void LeavePage()
        {
            PageContent.Content = null;
            ReleaseHosted();
            _crumbs.Clear();
            PageHost.Visibility = Visibility.Collapsed;
            HomeView.Visibility = Visibility.Visible;
            RefreshPlayLabel();
        }

        private void ReleaseHosted()
        {
            PageContent.Content = null;
            var windows = _hostedWindows.ToList();
            _hostedWindows.Clear();
            if (_makeMods != null && windows.Contains(_makeMods))
                _makeMods = null;
            if (_manageMods != null && windows.Contains(_manageMods))
                _manageMods = null;
            foreach (var w in windows)
            {
                try { w.Close(); } catch { }
            }
        }

        private void RenderCrumbs()
        {
            Breadcrumb.Children.Clear();
            for (var i = 0; i < _crumbs.Count; i++)
            {
                if (i > 0)
                {
                    Breadcrumb.Children.Add(new TextBlock
                    {
                        Text = "  /  ",
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = FindBrush("TextSecondaryBrush")
                    });
                }
                var crumb = _crumbs[i];
                var last = i == _crumbs.Count - 1;
                if (last)
                {
                    Breadcrumb.Children.Add(new TextBlock
                    {
                        Text = crumb.Label,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = FindBrush("TextPrimaryBrush")
                    });
                    continue;
                }
                var btn = new Button
                {
                    Content = crumb.Label,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Foreground = FindBrush("AccentBrush"),
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var action = crumb.Activate;
                btn.Click += (_, _) => action();
                Breadcrumb.Children.Add(btn);
            }
        }

        private Brush FindBrush(string key)
        {
            return TryFindResource(key) as Brush ?? Brushes.Black;
        }

        private sealed class Crumb
        {
            public Crumb(string label, Action activate)
            {
                Label = label;
                Activate = activate;
            }

            public string Label { get; }
            public Action Activate { get; }
        }
    }
}
