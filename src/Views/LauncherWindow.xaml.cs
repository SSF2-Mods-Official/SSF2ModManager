using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Models;
using SSF2ModManager.Services;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using WpfRectangle = System.Windows.Shapes.Rectangle;

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
        private readonly AppMusicPlayerService _miniPlayer = new();
        private readonly MusicLibraryCacheService _musicCacheSvc = new();
        private MusicLibraryCache? _musicCache;
        private bool _launcherSeekDragging;
        private DateTime _suppressLauncherProgressUntilUtc = DateTime.MinValue;
        private DateTime _lastLauncherSeekUtc = DateTime.MinValue;
        private bool _launcherSongComboQuiet;
        private DispatcherTimer? _musicSyncTimer;
        private bool _musicSyncMuted;
        private bool _musicSyncWasPlaying;
        private bool _musicSyncStartedPlayback;
        private bool _musicSyncApplying;
        private bool _musicSyncPushed;
        private int _musicSyncHoldTicks;
        private string? _musicSyncSentId;
        private DateTime _musicSyncCommandUtc = DateTime.MinValue;
        private DateTime _musicSyncLaunchUtc = DateTime.MinValue;
        private DispatcherTimer? _marqueeTimer;
        private double _marqueeOffset;
        private string _marqueeText = "";
        private int _marqueeHoldFrames;
        private int _prefetchGen;

        public LauncherWindow()
        {
            InitializeComponent();
            ShellDirectLaunchService.GameStarting += PauseMusicForLaunch;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
                new ToolTipEventHandler((_, e) => e.Handled = true));
            TxtVersion.Text = AppInfo.DisplayVersion;
            var theme = _settings.GetTheme();
            if (!string.IsNullOrWhiteSpace(theme))
                Themes.ApplyTheme(theme);
            FitSsf2ClientSize();
            LoadInstallFolder();
            SettingsService.Changed += OnSettingsChanged;
            ApplyLauncherBanner();
            StartSidebarClock();
            RefreshPlayLabel();
            Activated += (_, _) => RefreshPlayLabel();
            SetStatus("");
            _lastActivityUtc = DateTime.UtcNow;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _idleTimer.Tick += (_, _) =>
            {
                if (_logoBounce || HomeTiles.Visibility != Visibility.Visible)
                    return;
                if (DateTime.UtcNow - _lastActivityUtc >= TimeSpan.FromMinutes(5))
                    StartLogoBounce();
            };
            _idleTimer.Start();
            _musicSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _musicSyncTimer.Tick += (_, _) => PollMusicSync();
            _musicSyncTimer.Start();
            ContentRendered += OnFirstRender;
            _miniPlayer.TrackChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                UpdateLauncherMiniPlayer();
                if (_miniQueueReady)
                    RememberMiniSong();
                _ = PrefetchNeighborPreviewsAsync();
                if (_musicSyncMuted && !_musicSyncApplying && _miniPlayer.Current != null)
                    PushSyncedSong(_miniPlayer.Current.Id);
            });
            _miniPlayer.PlaybackStateChanged += (_, _) => Dispatcher.Invoke(UpdateLauncherMiniPlayer);
            _miniPlayer.PositionChanged += (_, _) => Dispatcher.Invoke(UpdateLauncherMiniProgress);
            _miniPlayer.VisualizerUpdated += (_, _) => Dispatcher.Invoke(UpdateLauncherMiniVisualizer);
            _miniPlayer.EnsurePlayableAsync = EnsureMiniPreviewAsync;
            var savedVolume = _settings.GetMiniPlayerVolume();
            if (SldLauncherMiniVolume != null)
                SldLauncherMiniVolume.Value = savedVolume;
            _miniPlayer.Volume = savedVolume;
            _previewBase = LauncherMusicUi.PreviewIdleText(AppInfo.ProductName, AppInfo.DisplayVersion);
            if (TxtStatus != null)
                TxtStatus.Text = _previewBase;
            MouseLeave += (_, _) =>
            {
                if (!_logoBounce)
                    ShowPreviewIdle();
            };
            UpdateLauncherMiniPlayer();
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
                if (_marqueeTimer != null)
                {
                    _marqueeTimer.Stop();
                    _marqueeTimer = null;
                }
                if (_musicSyncTimer != null)
                {
                    _musicSyncTimer.Stop();
                    _musicSyncTimer = null;
                }
                try { _logWindow?.Close(); } catch { }
                try { ReleaseHosted(); } catch { }
                try { RememberMiniSong(); } catch { }
                try { _miniPlayer.Dispose(); } catch { }
                App.RequestFullExit();
            };
        }

        private void OnFirstRender(object? sender, EventArgs e)
        {
            ContentRendered -= OnFirstRender;
            StartAlertWatcher();
            StartGameWatcher();
            _ = WarmMiniPlayerAsync();
            _ = WarmPaletteCacheAsync();
            if (SetupService.IsNeeded(SetupService.Read(_settings)))
                OpenSetup();
            else
                _ = OfferProductUpdatesAsync(reportCurrent: false);
        }

        /// <summary>Seed the song combo as soon as the Shell path is known — don't wait for Play.</summary>
        private async Task WarmMiniPlayerAsync()
        {
            try
            {
                await EnsureMiniQueueAsync();
                UpdateLauncherMiniPlayer();
            }
            catch
            {
                /* Shell may not be ready yet */
            }
        }

        public void OpenSetup()
        {
            var setup = new SetupWindow { Owner = this };
            setup.ShowDialog();
            if (setup.ShellBuilt)
            {
                RefreshPlayLabel();
                _miniQueueReady = false;
                _miniPlayer.SetQueue(Array.Empty<CachedSongInfo>());
                _ = WarmMiniPlayerAsync();
            }
        }

        private async Task WarmPaletteCacheAsync()
        {
            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell == null || PaletteCacheWarmService.IsDone(shell) || ShellConfigureLaunchService.IsGameRunning())
                return;
            LockForWork(true, "Caching costumes…");
            try
            {
                await PaletteCacheWarmService.WarmAsync(shell, null, new Progress<string>(s =>
                {
                    if (TxtBusy != null)
                        TxtBusy.Text = s;
                }), default);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[palette-cache] " + ex.Message);
            }
            finally
            {
                LockForWork(false, "");
            }
        }

        private async Task OfferProductUpdatesAsync(bool reportCurrent)
        {
            if (_busy)
                return;
            var lines = new List<string>();
            var manager = false;
            var n2f = false;
            var shellUpdate = false;
            ShellBaseService.Release? latest = null;
            try
            {
                var check = await LauncherUpdateService.CheckAndUpdateAsync(InstallRoot, download: false);
                if (check.Ok && !check.UpToDate)
                {
                    manager = true;
                    lines.Add("SSF2 Beta Mod Manager " + check.RemoteVersion);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[update] manager check " + ex.Message);
            }
            try
            {
                var status = await Next2FlashSetup.UpdateStatusAsync();
                if (status.Behind && AppPaths.Next2FlashRoot != null)
                {
                    n2f = true;
                    lines.Add("Next2Flash " + status.Detail);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[n2f] update check " + ex.Message);
            }
            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell == null || ShellBaseService.IsDevShell(shell))
                DebugLogger.Log("[shellbase] skip update offer shell=" + (shell ?? "none"));
            else
            {
                try
                {
                    latest = await ShellBaseService.GetLatestAsync();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("[shellbase] latest check failed: " + ex.Message);
                }
                var installed = ShellBaseService.InstalledVersion(shell);
                DebugLogger.Log($"[shellbase] latest={latest?.Version ?? "none"} installed={installed ?? "none"} shell={shell}");
                if (latest != null && installed != latest.Version)
                {
                    shellUpdate = true;
                    lines.Add("SSF2 Shell " + latest.Version);
                }
            }
            if (lines.Count == 0)
            {
                if (reportCurrent)
                    SetStatus("Up to date", "SSF2 Beta Mod Manager, Next2Flash, and SSF2 Shell.");
                return;
            }
            var question = "These can be updated:\n\n" + string.Join("\n", lines) + "\n\nUpdate them now?";
            if (MessageBox.Show(question, "Updates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            LockForWork(true, "Updating…");
            try
            {
                if (n2f)
                    await Next2FlashSetup.EnsureAsync(update: true);
                if (shellUpdate && latest != null && shell != null)
                {
                    var vanilla = SetupService.FindVanilla(_settings.GetVanillaSsf2Folder());
                    if (!ShellBaseService.IsVanillaFolder(vanilla))
                        OpenSetup();
                    else
                    {
                        await VanillaHashPrompt.BuildAsync(latest, vanilla!, shell, new Progress<string>(s => SetStatus("SSF2 Shell", s)),
                            _settings, AskVanilla);
                        RefreshPlayLabel();
                    }
                }
                if (manager)
                {
                    var result = await LauncherUpdateService.CheckAndUpdateAsync(InstallRoot, download: true,
                        new Progress<(string status, double? fraction)>(p =>
                        {
                            SetStatus(p.status, null, p.fraction);
                            if (TxtBusy != null)
                                TxtBusy.Text = p.status;
                            if (BusyProgress != null && p.fraction.HasValue)
                            {
                                BusyProgress.IsIndeterminate = false;
                                BusyProgress.Value = p.fraction.Value;
                            }
                        }));
                    SetStatus("Update finished", result.Message, 1);
                    if (result.Ok && LauncherUpdateService.RestartRequired)
                    {
                        MessageBox.Show(
                            "The Mod Manager is updated and will now restart.",
                            "Updates",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        LauncherUpdateService.ApplyStaged(InstallRoot);
                        LauncherUpdateService.Relaunch();
                        return;
                    }
                }
                else
                    SetStatus("Update finished", string.Join(", ", lines), 1);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[update] failed " + ex.Message);
                SetStatus("Update failed", ex.Message);
                MessageBox.Show(ex.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LockForWork(false, "");
            }
        }

        public Task CheckUpdatesAsync() => OfferProductUpdatesAsync(reportCurrent: true);

        private bool AskVanilla(string question) =>
            MessageBox.Show(question, "SSF2 Beta 1.4.0.1", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

        private void BtnJpexs_Click(object sender, RoutedEventArgs e)
        {
            var dir = AppPaths.FfdecDirectory;
            var jar = dir == null ? null : Path.Combine(dir, "ffdec.jar");
            var java = AppPaths.JreJavaExe;
            if (string.IsNullOrWhiteSpace(jar) || !File.Exists(jar) || string.IsNullOrWhiteSpace(java))
            {
                MessageBox.Show("JPEXS was not found. Download Next2Flash from Setup.", "JPEXS",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var javaw = Path.Combine(Path.GetDirectoryName(java)!, "javaw.exe");
            if (File.Exists(javaw))
                java = javaw;
            var psi = new ProcessStartInfo
            {
                FileName = java,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(jar);
            Process.Start(psi);
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
                    logPath = ShellWorkspaceService.GameLogPath(shell);
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
                    ? "expansions/state/game.log"
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

        private void FitSsf2ClientSize()
        {
            var frame = SystemParameters.WindowNonClientFrameThickness;
            if (frame.Top < 8)
                frame = new Thickness(8, 31, 8, 8);
            Width = StageBackdrop.ClientWidth + frame.Left + frame.Right;
            Height = StageBackdrop.ClientHeight + frame.Top + frame.Bottom;
            MinWidth = Width;
            MinHeight = Height;
        }

        private void SetInstallFolder(string path, bool save = true)
        {
            _installFolderPath = path;
            if (save)
                _settings.SetLauncherInstallFolder(path);
            ApplyLauncherBanner();
        }

        private void OnSettingsChanged()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _settings.Reload();
                var saved = _settings.GetLauncherInstallFolder();
                if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                    _installFolderPath = saved;
                ApplyLauncherBanner();
                UpdateSidebarClock();
            });
        }

        private DispatcherTimer? _bannerTimer;
        private double _bannerPhase;

        private List<GifFrame>? _gifFrames;
        private int _gifIndex;
        private DispatcherTimer? _gifTimer;

        private void ApplyLauncherBanner()
        {
            if (ImgLauncherStage == null) return;
            StopCustomBackground();
            var custom = _settings.GetManagerBackgroundCustomPath();
            if (!_settings.GetManagerBackgroundMatchesMenu()
                && !string.IsNullOrWhiteSpace(custom)
                && File.Exists(custom))
            {
                ShowCustomBackground(custom);
                return;
            }
            string stageId = "";
            var shell = ResolveShellRoot();
            if (!string.IsNullOrWhiteSpace(shell))
            {
                stageId = _settings.GetManagerBackgroundMatchesMenu()
                    ? MenuBackgroundStore.ReadStageId(shell)
                    : _settings.GetManagerBackgroundStageId();
            }
            var file = string.IsNullOrWhiteSpace(stageId) || shell == null
                ? null
                : MenuBackgroundStore.FindStageFile(shell, stageId);
            var shot = StageBackdrop.LoadSnapshot(file);
            if (shot == null)
            {
                ImgLauncherStage.Source = null;
                ImgLauncherStage.Visibility = Visibility.Collapsed;
                StopBannerDrift();
                return;
            }
            ImgLauncherStage.Source = shot;
            ImgLauncherStage.Visibility = Visibility.Visible;
            if (BannerScale != null)
            {
                BannerScale.ScaleX = 1.08;
                BannerScale.ScaleY = 1.08;
            }
            StartBannerDrift();
        }

        private void ShowCustomBackground(string path)
        {
            StopBannerDrift();
            if (BannerScale != null)
            {
                BannerScale.ScaleX = 1;
                BannerScale.ScaleY = 1;
            }
            var kind = ManagerBackgroundMedia.KindFor(path);
            if (kind == ManagerBackgroundKind.Video)
            {
                ImgLauncherStage.Visibility = Visibility.Collapsed;
                VidLauncherStage.Visibility = Visibility.Visible;
                VidLauncherStage.Source = new Uri(path);
                VidLauncherStage.Play();
                return;
            }
            if (kind == ManagerBackgroundKind.Gif)
            {
                var frames = ManagerBackgroundMedia.DecodeGif(path);
                if (frames.Count > 0)
                {
                    _gifFrames = frames;
                    _gifIndex = 0;
                    ImgLauncherStage.Source = frames[0].Image;
                    ImgLauncherStage.Visibility = Visibility.Visible;
                    _gifTimer ??= new DispatcherTimer();
                    _gifTimer.Interval = TimeSpan.FromMilliseconds(frames[0].DelayMs);
                    _gifTimer.Tick -= AdvanceGif;
                    _gifTimer.Tick += AdvanceGif;
                    _gifTimer.Start();
                    return;
                }
            }
            ShowStillBackground(path);
            Dispatcher.BeginInvoke(DeleteOldBackgrounds, DispatcherPriority.Background);
        }

        private void ShowStillBackground(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            bmp.EndInit();
            bmp.Freeze();
            ImgLauncherStage.Source = bmp;
            ImgLauncherStage.Visibility = Visibility.Visible;
            if (BannerScale != null)
            {
                BannerScale.ScaleX = 1.08;
                BannerScale.ScaleY = 1.08;
            }
            StartBannerDrift();
        }

        private void AdvanceGif(object? sender, EventArgs e)
        {
            if (_gifFrames == null || _gifFrames.Count == 0)
                return;
            _gifIndex = (_gifIndex + 1) % _gifFrames.Count;
            var frame = _gifFrames[_gifIndex];
            ImgLauncherStage.Source = frame.Image;
            if (_gifTimer != null)
                _gifTimer.Interval = TimeSpan.FromMilliseconds(frame.DelayMs);
        }

        private void StopCustomBackground()
        {
            _gifTimer?.Stop();
            _gifFrames = null;
            if (VidLauncherStage == null)
                return;
            VidLauncherStage.Stop();
            VidLauncherStage.Source = null;
            VidLauncherStage.Visibility = Visibility.Collapsed;
            Dispatcher.BeginInvoke(DeleteOldBackgrounds, DispatcherPriority.Background);
        }

        private void DeleteOldBackgrounds()
        {
            var keep = _settings.GetManagerBackgroundCustomPath();
            if (string.IsNullOrWhiteSpace(keep))
                return;
            ManagerBackgroundMedia.DeleteOthers(AppPaths.AppDataDir, keep);
        }

        private void VidLauncherStage_Ended(object sender, RoutedEventArgs e)
        {
            if (VidLauncherStage.Source == null)
                return;
            VidLauncherStage.Position = TimeSpan.Zero;
            VidLauncherStage.Play();
        }

        private void VidLauncherStage_Failed(object sender, ExceptionRoutedEventArgs e)
        {
            VidLauncherStage.Visibility = Visibility.Collapsed;
        }

        private bool _railExpanded;

        private void BtnRailExpand_Click(object sender, RoutedEventArgs e)
        {
            _railExpanded = !_railExpanded;
            if (SidebarColumn != null)
                SidebarColumn.Width = new GridLength(_railExpanded ? 112 : 52);
            var labels = new TextBlock?[]
            {
                LblRailMusic, LblRailNews, LblRailSettings, LblRailCredits, LblRailDiscord,
                LblRailSupport, LblRailJpexs, LblRailLog, LblRailExpand
            };
            var show = _railExpanded ? Visibility.Visible : Visibility.Collapsed;
            foreach (var label in labels)
            {
                if (label != null)
                    label.Visibility = show;
            }
            if (IcoRailExpand != null)
                IcoRailExpand.Text = _railExpanded ? "\uE76B" : "\uE76C";
            if (LblRailExpand != null)
                LblRailExpand.Text = _railExpanded ? "Collapse" : "Expand";
            if (BtnRailExpand != null)
                BtnRailExpand.Tag = _railExpanded ? "Collapse the sidebar." : "Show names on the sidebar.";
        }

        private DispatcherTimer? _clockTimer;

        private void StartSidebarClock()
        {
            UpdateSidebarClock();
            _clockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clockTimer.Tick -= OnSidebarClock;
            _clockTimer.Tick += OnSidebarClock;
            _clockTimer.Start();
        }

        private void OnSidebarClock(object? sender, EventArgs e) => UpdateSidebarClock();

        private void UpdateSidebarClock()
        {
            if (TxtSidebarClock != null)
                TxtSidebarClock.Text = DateTime.Now.ToString(_settings.GetClockUseAmPm() ? "h:mm tt" : "HH:mm");
        }

        private void RoundClip_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
                return;
            var radius = sender is Border border ? border.CornerRadius.TopLeft : 8;
            fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), radius, radius);
        }

        private void StartBannerDrift()
        {
            _bannerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _bannerTimer.Tick -= OnBannerDrift;
            _bannerTimer.Tick += OnBannerDrift;
            _bannerTimer.Start();
        }

        private void StopBannerDrift()
        {
            _bannerTimer?.Stop();
            if (BannerShift != null)
            {
                BannerShift.X = 0;
                BannerShift.Y = 0;
            }
        }

        private void OnBannerDrift(object? sender, EventArgs e)
        {
            _bannerPhase += 0.012;
            if (BannerShift == null) return;
            BannerShift.X = Math.Sin(_bannerPhase) * 14;
            BannerShift.Y = Math.Cos(_bannerPhase * 0.65) * 8;
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
                if (download.EndsWith(".ssfe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!SsfeSeal.Accept(download))
                    {
                        LockForWork(false, "");
                        SetStatus("Not installed", "The .ssfe package did not check out.");
                        MessageBox.Show("That .ssfe package did not check out.", "Can't install this mod",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (new SettingsService().GetWarnOnLooseMods())
                {
                    var warn = MessageBox.Show(
                        "This download is not an .ssfe package. It may have problems, may be unsupported, or may be stolen.\n\nInstall it anyway?",
                        "Semi-supported mod",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (warn != MessageBoxResult.Yes)
                    {
                        LockForWork(false, "");
                        SetStatus("Not installed", "Skipped a mod that is not an .ssfe package.");
                        return;
                    }
                }
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

        private string _previewBase = "";
        private bool _miniQueueReady;
        private DateTime _lastActivityUtc;
        private DispatcherTimer? _idleTimer;
        private bool _logoBounce;
        private double _bx, _by, _bvx, _bvy;
        private int _hueFrame;
        private TimeSpan _lastRender;
        private System.Windows.Point _lastPointer;
        private bool _hasPointer;
        private ImageSource[]? _hueFrames;
        private readonly List<Button> _bounceHiddenTiles = new();
        private bool _aimCorner;
        private bool _cornerLine;
        private bool _rainbow;
        private double _rainbowT;
        private int _rainbowStart;
        private int _rainbowShown;
        private const int HueStep = 32;
        private const double LogoSize = 84;
        private const double RainbowSeconds = 10;
        private bool _cornerLatched;

        private void NotePointer(System.Windows.Point p)
        {
            if (_hasPointer && Math.Abs(p.X - _lastPointer.X) < 2 && Math.Abs(p.Y - _lastPointer.Y) < 2)
                return;
            _lastPointer = p;
            _hasPointer = true;
            _lastActivityUtc = DateTime.UtcNow;
        }

        private void Shell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_logoBounce)
                return;
            StopLogoBounce(true);
            _lastActivityUtc = DateTime.UtcNow;
            e.Handled = true;
        }

        private void Shell_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (key == Key.F2 && shift)
            {
                if (!_logoBounce && HomeTiles.Visibility == Visibility.Visible)
                    StartLogoBounce();
                if (_logoBounce)
                    _aimCorner = true;
                _lastActivityUtc = DateTime.UtcNow;
                e.Handled = true;
                return;
            }
            if (_logoBounce)
            {
                StopLogoBounce(true);
                _lastActivityUtc = DateTime.UtcNow;
                e.Handled = true;
                return;
            }
            _lastActivityUtc = DateTime.UtcNow;
            if (key == Key.F1 && HomeTiles.Visibility == Visibility.Visible)
            {
                StartLogoBounce();
                e.Handled = true;
            }
        }

        private void StartLogoBounce()
        {
            if (_logoBounce || HomeTiles.Visibility != Visibility.Visible)
                return;
            EnsureHueFrames();
            if (_hueFrames == null)
                return;
            _logoBounce = true;
            _hueFrame = 0;
            _lastRender = TimeSpan.Zero;
            ImgBounceLogo.Source = _hueFrames[0];
            _aimCorner = false;
            _cornerLine = false;
            _rainbow = false;
            _cornerLatched = false;
            HideHomeForBounce();
            ShowBouncePreview();
            LogoBounce.Visibility = Visibility.Visible;
            var rng = new Random();
            _bvx = rng.Next(2) == 0 ? 92 : -92;
            _bvy = rng.Next(2) == 0 ? 64 : -64;
            _bx = 0;
            _by = 0;
            LogoShift.X = 0;
            LogoShift.Y = 0;
            CompositionTarget.Rendering += BounceFrame;
        }

        private void StopLogoBounce(bool restoreHome)
        {
            if (!_logoBounce && LogoBounce.Visibility != Visibility.Visible)
                return;
            _logoBounce = false;
            _aimCorner = false;
            _cornerLine = false;
            _rainbow = false;
            _cornerLatched = false;
            CompositionTarget.Rendering -= BounceFrame;
            LogoBounce.Visibility = Visibility.Collapsed;
            foreach (var button in _bounceHiddenTiles)
                button.Visibility = Visibility.Visible;
            _bounceHiddenTiles.Clear();
            if (restoreHome && PageHost.Visibility != Visibility.Visible)
                HomeTiles.Visibility = Visibility.Visible;
            ShowPreviewIdle();
        }

        private void HideHomeForBounce()
        {
            _bounceHiddenTiles.Clear();
            if (_miniPlayer.IsPlaying && PlayerCard != null)
            {
                foreach (var button in ButtonsUnder(HomeTiles))
                {
                    if (IsUnder(button, PlayerCard) || button.Visibility != Visibility.Visible)
                        continue;
                    button.Visibility = Visibility.Collapsed;
                    _bounceHiddenTiles.Add(button);
                }
                return;
            }
            HomeTiles.Visibility = Visibility.Collapsed;
        }

        private void ShowBouncePreview()
        {
            if (TxtStatus == null)
                return;
            var text = _cornerLine ? LogoBounceMath.CornerPreview : LogoBounceMath.ExitPreview;
            if (!string.Equals(TxtStatus.Text, text, StringComparison.Ordinal))
                TxtStatus.Text = text;
        }

        private static IEnumerable<Button> ButtonsUnder(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Button button)
                    yield return button;
                foreach (var nested in ButtonsUnder(child))
                    yield return nested;
            }
        }

        private static bool IsUnder(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (ReferenceEquals(child, parent))
                    return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private void BounceFrame(object? sender, EventArgs e)
        {
            if (!_logoBounce || e is not RenderingEventArgs args)
                return;
            if (args.RenderingTime == _lastRender)
                return;
            var dt = _lastRender == TimeSpan.Zero ? 0.016 : (args.RenderingTime - _lastRender).TotalSeconds;
            _lastRender = args.RenderingTime;
            if (dt <= 0 || dt > 0.05)
                dt = 0.016;
            var maxX = LogoBounce.ActualWidth - LogoSize;
            var maxY = LogoBounce.ActualHeight - LogoSize;
            if (maxX < 1 || maxY < 1)
                return;
            var step = LogoBounceMath.Step(_bx, _by, _bvx, _bvy, maxX, maxY, dt);
            _bx = step.X;
            _by = step.Y;
            _bvx = step.Vx;
            _bvy = step.Vy;
            LogoShift.X = _bx;
            LogoShift.Y = _by;
            var corner = LogoBounceMath.IsCornerHit(_bx, _by, maxX, maxY, step.Hits);
            if (corner && !_cornerLatched)
            {
                _cornerLatched = true;
                BeginCornerRainbow();
            }
            else if (!corner)
                _cornerLatched = false;
            if (step.Hits == 1 && !corner)
            {
                if (_aimCorner)
                {
                    _aimCorner = false;
                    var aimed = LogoBounceMath.AimAtCorner(_bx, _by, _bvx, _bvy, maxX, maxY);
                    _bvx = aimed.Vx;
                    _bvy = aimed.Vy;
                }
                if (!_rainbow && _hueFrames != null)
                {
                    _hueFrame = (_hueFrame + 1) % _hueFrames.Length;
                    ImgBounceLogo.Source = _hueFrames[_hueFrame];
                }
            }
            if (_rainbow && _hueFrames != null)
            {
                _rainbowT += dt;
                var n = _hueFrames.Length;
                if (_rainbowT >= RainbowSeconds)
                {
                    _rainbow = false;
                    _cornerLine = false;
                    _hueFrame = _rainbowStart % n;
                    ImgBounceLogo.Source = _hueFrames[_hueFrame];
                    ShowBouncePreview();
                }
                else
                {
                    var i = (_rainbowStart + (int)(_rainbowT / RainbowSeconds * n)) % n;
                    _rainbowShown = i;
                    ImgBounceLogo.Source = _hueFrames[i];
                }
            }
        }

        private void BeginCornerRainbow()
        {
            _rainbowStart = _rainbow ? _rainbowShown : _hueFrame;
            _rainbow = true;
            _rainbowT = 0;
            _cornerLine = true;
            ShowBouncePreview();
        }

        private void EnsureHueFrames()
        {
            if (_hueFrames != null)
                return;
            var src = new BitmapImage(new Uri("pack://application:,,,/SSF2M.png", UriKind.Absolute));
            var formatted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var w = formatted.PixelWidth;
            var h = formatted.PixelHeight;
            var original = new byte[w * h * 4];
            formatted.CopyPixels(original, w * 4, 0);
            var count = 360 / HueStep;
            var frames = new ImageSource[count];
            var pixels = new byte[original.Length];
            for (var frame = 0; frame < count; frame++)
            {
                var hue = frame * HueStep;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var shifted = LogoBounceMath.ShiftHue(original[i + 2], original[i + 1], original[i], hue);
                    pixels[i] = shifted.B;
                    pixels[i + 1] = shifted.G;
                    pixels[i + 2] = shifted.R;
                    pixels[i + 3] = original[i + 3];
                }
                var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
                bmp.Freeze();
                frames[frame] = bmp;
            }
            _hueFrames = frames;
        }

        private void Shell_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            NotePointer(e.GetPosition(this));
            if (_logoBounce)
            {
                ShowBouncePreview();
                return;
            }
            var src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src is FrameworkElement fe)
                {
                    var tip = fe.Tag as string;
                    if (string.IsNullOrWhiteSpace(tip) && fe.ToolTip is string fromTip)
                        tip = fromTip;
                    if (!string.IsNullOrWhiteSpace(tip))
                    {
                        if (!string.Equals(TxtStatus.Text, tip, StringComparison.Ordinal))
                            TxtStatus.Text = tip;
                        return;
                    }
                }
                src = VisualTreeHelper.GetParent(src);
            }
            ShowPreviewIdle();
        }

        private void ShowPreviewIdle()
        {
            if (TxtStatus == null) return;
            if (!string.Equals(TxtStatus.Text, _previewBase, StringComparison.Ordinal))
                TxtStatus.Text = _previewBase;
        }

        private void LauncherMiniMarquee_Click(object sender, MouseButtonEventArgs e)
        {
            if (_makeMods != null && _hostedWindows.Contains(_makeMods))
            {
                _makeMods.OpenPanel("music");
                ApplySectionAccent("Make Mods");
                return;
            }
            BtnMakeMods_Click(sender, new RoutedEventArgs());
            _makeMods?.OpenPanel("music");
        }

        private void ApplySectionAccent(string title)
        {
            var hex = title switch
            {
                "Configure" => "#FFE23D4A",
                "Download Mods" => "#FF1F8A4C",
                "Make Mods" => "#FFE85D04",
                "Manage Mods" => "#FF6B2D8B",
                "Docs" => "#FFC4920E",
                "Debug" => "#FF3D4A5C",
                "Settings" => "#FF3D4A5C",
                "News" => "#FF1F4E8A",
                "Credits" => "#FFC4920E",
                _ => "#FF1A1A1A"
            };
            try
            {
                SectionBar.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                SectionBar.Background = Brushes.Black;
            }
        }

        private void SetStatus(string status, string? detail = null, double? progress = null)
        {
            var line = string.IsNullOrWhiteSpace(detail) ? status : status + " — " + detail;
            TxtStatusDetail.Text = string.IsNullOrWhiteSpace(detail) ? line : detail;
            if (TxtStatus != null)
                TxtStatus.ToolTip = string.IsNullOrWhiteSpace(detail) ? null : detail;
            if (progress.HasValue && Progress != null)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = Math.Clamp(progress.Value, 0, 1);
                Progress.Visibility = Visibility.Visible;
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (Progress == null) return;
            if (busy)
            {
                Progress.IsIndeterminate = true;
                Progress.Visibility = Visibility.Visible;
            }
            else
            {
                Progress.IsIndeterminate = false;
                Progress.Visibility = Visibility.Collapsed;
            }
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

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e) =>
            await OfferProductUpdatesAsync(reportCurrent: true);

        private void PauseMusicForLaunch()
        {
            if (_settings.GetSyncMusicWithGame())
            {
                _musicSyncLaunchUtc = DateTime.UtcNow;
                _miniPlayer.HoldPaused = true;
                _miniPlayer.ClearCurrent();
                UpdateLauncherMiniPlayer();
                return;
            }
            if (!_settings.GetPauseMusicOnLaunch() || !_miniPlayer.IsPlaying)
                return;
            _miniPlayer.Pause();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shell = ResolveShellRoot();
                if (shell == null)
                {
                    MessageBox.Show("Could not find SSF2 Shell. Open Settings and set the SSF2 Shell folder.",
                        "Play", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!VanillaHashPrompt.ConfirmPlay(_settings, shell, AskVanilla))
                    return;
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
                    MessageBox.Show("Could not find SSF2 Shell. Open Settings and set the SSF2 Shell folder.",
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
                MessageBox.Show("Could not find SSF2 Shell. Open Settings and set the SSF2 Shell folder.",
                    "Make Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _makeMods = new MakeModsWindow(shell);
            _makeMods.SectionChanged += SyncMakeTrail;
            _makeMods.PlayInManager = (songs, index) =>
            {
                var (queue, at) = LauncherMusicUi.PreviewQueue(_miniPlayer.Queue, _musicCache?.Songs, songs, index);
                if (queue.Count == 0) return;
                _miniPlayer.PlayTrack(queue[at], queue);
            };
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
                    MessageBox.Show("Could not find SSF2 Shell. Open Settings and set the SSF2 Shell folder.",
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
                MessageBox.Show("Could not find SSF2 Shell. Open Settings and set the SSF2 Shell folder.",
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

        private void UpdateLauncherMiniPlayer()
        {
            var cur = _miniPlayer.Current;
            SetLauncherMarqueeText(LauncherMusicUi.TrackLabel(cur));
            // Segoe MDL2: Play E768, Pause E769, Repeat E8EE, Repeat one E8ED, Shuffle E8B1, Forward E72A
            if (BtnLauncherMiniPlay != null)
                BtnLauncherMiniPlay.Content = _miniPlayer.IsPlaying ? "\uE769" : "\uE768";
            if (BtnLauncherMiniLoop != null)
            {
                BtnLauncherMiniLoop.Content = _miniPlayer.RepeatMode switch
                {
                    MusicRepeatMode.One => "\uE8ED",
                    MusicRepeatMode.All => "\uE8EE",
                    MusicRepeatMode.Shuffle => "\uE8B1",
                    _ => "\uE72A"
                };
                BtnLauncherMiniLoop.ToolTip = _miniPlayer.RepeatMode switch
                {
                    MusicRepeatMode.One => "Loop: one track",
                    MusicRepeatMode.All => "Loop: playlist",
                    MusicRepeatMode.Shuffle => "Shuffle",
                    _ => "Loop: off"
                };
            }
            BindLauncherSongCombo();
            UpdateLauncherMiniProgress();
            if (LauncherMiniVisualizer != null)
                LauncherMiniVisualizer.Visibility =
                    (_miniPlayer.IsPlaying || cur != null) ? Visibility.Visible : Visibility.Collapsed;
            UpdateRailMusicVisualizer();
        }

        private void BindLauncherSongCombo(bool forceList = false)
        {
            if (CmbLauncherMiniSongs == null) return;
            _launcherSongComboQuiet = true;
            try
            {
                var songs = LauncherMusicUi.PickerSongs(_miniPlayer.Queue);
                if (forceList || CmbLauncherMiniSongs.ItemsSource == null
                    || CmbLauncherMiniSongs.Items.Count != songs.Count)
                {
                    CmbLauncherMiniSongs.ItemsSource = songs;
                }
                var cur = _miniPlayer.Current;
                if (cur != null && CmbLauncherMiniSongs.ItemsSource is System.Collections.IEnumerable items)
                {
                    CachedSongInfo? match = null;
                    foreach (var obj in items)
                    {
                        if (obj is CachedSongInfo s
                            && s.Id.Equals(cur.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            match = s;
                            break;
                        }
                    }
                    if (match != null)
                        CmbLauncherMiniSongs.SelectedItem = match;
                }
            }
            finally
            {
                _launcherSongComboQuiet = false;
            }
        }

        private void UpdateLauncherMiniProgress()
        {
            var pos = _miniPlayer.Position;
            var dur = _miniPlayer.DisplayDuration;
            if (TxtLauncherMiniDuration != null)
                TxtLauncherMiniDuration.Text = dur > TimeSpan.Zero
                    ? LauncherMusicUi.FormatTrackTime(dur) : "0:00";

            if (_launcherSeekDragging || DateTime.UtcNow < _suppressLauncherProgressUntilUtc)
            {
                if (TxtLauncherMiniTime != null && SldLauncherMiniSeek != null && dur.TotalMilliseconds > 0)
                {
                    var hold = LauncherMusicUi.SeekFromRatio(SldLauncherMiniSeek.Value / 1000.0, dur);
                    TxtLauncherMiniTime.Text = LauncherMusicUi.FormatTrackTime(hold);
                }
                return;
            }

            if (TxtLauncherMiniTime != null)
                TxtLauncherMiniTime.Text = LauncherMusicUi.FormatTrackTime(pos);
            if (SldLauncherMiniSeek != null)
                SldLauncherMiniSeek.Value = LauncherMusicUi.SeekRatio(pos, dur) * 1000.0;
        }

        private void UpdateLauncherMiniVisualizer()
        {
            DrawMiniBars();
            UpdateRailMusicVisualizer();
        }

        private void DrawMiniBars()
        {
            if (LauncherMiniVisualizer == null) return;
            var w = LauncherMiniVisualizer.ActualWidth;
            var h = LauncherMiniVisualizer.ActualHeight;
            if (w <= 1 || h <= 1) return;
            var bars = _miniPlayer.VisualizerBars;
            var n = bars.Length;
            var gap = 2.0;
            var barW = Math.Max(2.5, (w - gap * (n - 1)) / n);
            Brush fill;
            try
            {
                var accent = (Brush)FindResource("AccentBrush");
                fill = accent.Clone();
                fill.Opacity = 0.55;
                if (fill.CanFreeze) fill.Freeze();
            }
            catch { fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x8C, 0x1E, 0x90, 0xFF)); }
            while (LauncherMiniVisualizer.Children.Count < n)
            {
                LauncherMiniVisualizer.Children.Add(new WpfRectangle
                {
                    RadiusX = 1.5,
                    RadiusY = 1.5
                });
            }
            for (var i = 0; i < n; i++)
            {
                if (LauncherMiniVisualizer.Children[i] is not WpfRectangle rect) continue;
                rect.RenderTransform = null;
                rect.Fill = fill;
                var bh = Math.Max(2, bars[i] * h * 0.92);
                rect.Width = barW;
                rect.Height = bh;
                Canvas.SetLeft(rect, i * (barW + gap));
                Canvas.SetTop(rect, h - bh);
            }
        }

        private void UpdateRailMusicVisualizer()
        {
            if (RailMusicViz == null || TxtRailMusicIcon == null)
                return;
            var playing = _miniPlayer.IsPlaying;
            TxtRailMusicIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            RailMusicViz.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            if (!playing)
                return;
            const double w = 18;
            const double h = 16;
            const int n = 4;
            var bars = _miniPlayer.VisualizerBars;
            var gap = 1.0;
            var barW = (w - gap * (n - 1)) / n;
            Brush fill;
            try
            {
                var accent = (Brush)FindResource("AccentBrush");
                fill = accent.Clone();
                fill.Opacity = 0.9;
                if (fill.CanFreeze) fill.Freeze();
            }
            catch { fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x1E, 0x90, 0xFF)); }
            while (RailMusicViz.Children.Count < n)
                RailMusicViz.Children.Add(new WpfRectangle { RadiusX = 1, RadiusY = 1 });
            for (var i = 0; i < n; i++)
            {
                if (RailMusicViz.Children[i] is not WpfRectangle rect) continue;
                var sample = bars.Length == 0 ? 0 : bars[Math.Min(bars.Length - 1, i * bars.Length / n)];
                rect.RenderTransform = null;
                rect.Fill = fill;
                var bh = Math.Max(2, sample * h * 0.92);
                rect.Width = barW;
                rect.Height = bh;
                Canvas.SetLeft(rect, i * (barW + gap));
                Canvas.SetTop(rect, h - bh);
            }
        }

        private void SetLauncherMarqueeText(string text)
        {
            if (TxtLauncherMiniTrack == null || LauncherMiniMarqueeClip == null) return;
            if (!string.Equals(_marqueeText, text, StringComparison.Ordinal))
            {
                _marqueeText = text;
                TxtLauncherMiniTrack.Text = text;
                // Always start at the beginning of the title (fixes mid-string clipping).
                _marqueeOffset = 0;
                _marqueeHoldFrames = 45; // ~1.5s pause so the full start is readable
                Canvas.SetLeft(TxtLauncherMiniTrack, 0);
            }

            TxtLauncherMiniTrack.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var textW = TxtLauncherMiniTrack.DesiredSize.Width;
            var clipW = LauncherMiniMarqueeClip.ActualWidth;
            if (clipW <= 1) clipW = 160;
            if (textW <= clipW + 4)
            {
                if (_marqueeTimer != null)
                {
                    _marqueeTimer.Stop();
                    _marqueeTimer = null;
                }
                Canvas.SetLeft(TxtLauncherMiniTrack, 0);
                return;
            }

            if (_marqueeTimer == null)
            {
                _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _marqueeTimer.Tick += (_, _) => TickLauncherMarquee();
                _marqueeTimer.Start();
            }
        }

        private void TickLauncherMarquee()
        {
            if (TxtLauncherMiniTrack == null || LauncherMiniMarqueeClip == null) return;
            var textW = TxtLauncherMiniTrack.DesiredSize.Width;
            var clipW = Math.Max(1, LauncherMiniMarqueeClip.ActualWidth);
            if (textW <= clipW + 4)
            {
                Canvas.SetLeft(TxtLauncherMiniTrack, 0);
                return;
            }
            if (_marqueeHoldFrames > 0)
            {
                _marqueeHoldFrames--;
                Canvas.SetLeft(TxtLauncherMiniTrack, 0);
                return;
            }
            _marqueeOffset -= 0.85;
            if (_marqueeOffset < -(textW + 24))
            {
                _marqueeOffset = 0;
                _marqueeHoldFrames = 40;
            }
            Canvas.SetLeft(TxtLauncherMiniTrack, _marqueeOffset);
        }

        private async Task<bool> EnsureMiniPreviewAsync(CachedSongInfo song)
        {
            var path = await _musicCacheSvc.EnsurePreviewMp3Async(song, null);
            if (path == null || !File.Exists(path))
                return false;
            song.PreviewMp3Path = path;
            if (_musicCache != null)
                _musicCacheSvc.Save(_musicCache);
            return true;
        }

        /// <summary>Extract next/prev MP3s in the background so Next is not stuck on FFDec.</summary>
        private async Task PrefetchNeighborPreviewsAsync()
        {
            var gen = ++_prefetchGen;
            var queue = _miniPlayer.Queue;
            if (queue.Count == 0) return;
            var idx = _miniPlayer.Index;
            var order = new[] { idx + 1, idx + 2, idx - 1 }
                .Select(i => ((i % queue.Count) + queue.Count) % queue.Count)
                .Distinct()
                .Where(i => i != idx)
                .Take(3)
                .ToList();
            foreach (var i in order)
            {
                if (gen != _prefetchGen) return;
                var song = queue[i];
                if (!string.IsNullOrWhiteSpace(song.PreviewMp3Path) && File.Exists(song.PreviewMp3Path))
                    continue;
                try { await EnsureMiniPreviewAsync(song); }
                catch { /* prefetch best-effort */ }
            }
        }

        private void RememberMiniSong()
        {
            if (!_miniQueueReady || _musicCache == null)
                return;
            var before = _musicCache.LastPlayedSongId;
            LauncherMusicUi.RememberLastSong(_musicCache, _miniPlayer.Current, _miniPlayer.Queue);
            if (!string.Equals(before, _musicCache.LastPlayedSongId, StringComparison.OrdinalIgnoreCase))
                _musicCacheSvc.Save(_musicCache);
        }

        /// <summary>Show the saved song list, then refresh names and new tracks off the window thread.</summary>
        private async Task<bool> EnsureMiniQueueAsync()
        {
            if (_miniPlayer.Queue.Count > 0)
                return true;
            var shell = ResolveShellRoot();
            if (string.IsNullOrWhiteSpace(shell) || !Directory.Exists(shell))
            {
                SetStatus("Music", "Set the SSF2 Shell folder first.");
                return false;
            }

            MusicLibraryCache? cached;
            try
            {
                cached = await Task.Run(() => _musicCacheSvc.Load(shell));
            }
            catch (Exception ex)
            {
                SetStatus("Music", "Could not read Shell music: " + ex.Message);
                return false;
            }

            if (cached?.Songs is { Count: > 0 })
            {
                ShowMiniQueue(cached);
                _ = RefreshMiniLibraryAsync(shell);
                return true;
            }

            try
            {
                var built = await Task.Run(() => _musicCacheSvc.EnsureFastLibrary(shell));
                return ShowMiniQueue(built);
            }
            catch (Exception ex)
            {
                SetStatus("Music", "Could not read Shell music: " + ex.Message);
                return false;
            }
        }

        private bool ShowMiniQueue(MusicLibraryCache cache)
        {
            _musicCache = cache;
            var songs = cache.Songs;
            if (songs == null || songs.Count == 0)
            {
                SetStatus("Music", "No songs in this Shell yet. Add music in Make Mods → Music.");
                return false;
            }

            var ordered = LauncherMusicUi.PickerSongs(songs);
            var start = 0;
            if (!string.IsNullOrWhiteSpace(cache.LastPlayedSongId))
            {
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (ordered[i].Id.Equals(cache.LastPlayedSongId, StringComparison.OrdinalIgnoreCase))
                    {
                        start = i;
                        break;
                    }
                }
            }
            _miniPlayer.SetQueue(ordered, start);
            _miniQueueReady = true;
            BindLauncherSongCombo(forceList: true);
            return true;
        }

        private async Task RefreshMiniLibraryAsync(string shell)
        {
            try
            {
                var refreshed = await Task.Run(() => _musicCacheSvc.EnsureFastLibrary(shell));
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_miniPlayer.IsPlaying)
                    {
                        _musicCache = refreshed;
                        return;
                    }
                    var before = string.Join("|", _miniPlayer.Queue.Select(s => s.Id + "\t" + s.DisplayLabel));
                    var after = string.Join("|", LauncherMusicUi.PickerSongs(refreshed.Songs).Select(s => s.Id + "\t" + s.DisplayLabel));
                    if (before == after)
                    {
                        _musicCache = refreshed;
                        return;
                    }
                    ShowMiniQueue(refreshed);
                });
            }
            catch
            {
                /* keep the list already on screen */
            }
        }

        /// <summary>
        /// Preview scrub on the glass bar. Only commits to MediaPlayer when <paramref name="commit"/>
        /// is true — mid-drag seeks Pause/Play every move and stutter hard.
        /// </summary>
        private void ApplyLauncherSeekFromHit(double x, double width, bool commit)
        {
            if (SldLauncherMiniSeek == null) return;
            var dur = _miniPlayer.DisplayDuration;
            if (dur.TotalMilliseconds <= 0) return;
            var ratio = Math.Clamp(x / Math.Max(1.0, width), 0, 1);
            SldLauncherMiniSeek.Value = ratio * 1000.0;
            var at = LauncherMusicUi.SeekFromRatio(ratio, dur);
            if (TxtLauncherMiniTime != null)
                TxtLauncherMiniTime.Text = LauncherMusicUi.FormatTrackTime(at);
            if (!commit) return;
            _lastLauncherSeekUtc = DateTime.UtcNow;
            _suppressLauncherProgressUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
            _miniPlayer.SeekDisplay(at);
            if (_musicSyncMuted)
                PushSyncedSeek(at.TotalMilliseconds);
        }

        private void EndLauncherSeekDrag(UIElement? hit, bool seek)
        {
            if (seek && SldLauncherMiniSeek != null && LauncherMiniSeekHit != null)
            {
                var ratio = SldLauncherMiniSeek.Value / 1000.0;
                ApplyLauncherSeekFromHit(ratio * Math.Max(1, LauncherMiniSeekHit.ActualWidth),
                    Math.Max(1, LauncherMiniSeekHit.ActualWidth), commit: true);
            }
            if (hit != null && hit.IsMouseCaptured)
                hit.ReleaseMouseCapture();
            _launcherSeekDragging = false;
            _suppressLauncherProgressUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
        }

        private void LauncherMiniSeek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement hit) return;
            _launcherSeekDragging = true;
            var pos = e.GetPosition(hit);
            var width = hit is FrameworkElement fe ? fe.ActualWidth : 0;
            // UI-only while the thumb moves; commit on mouse-up.
            ApplyLauncherSeekFromHit(pos.X, width, commit: false);
            hit.CaptureMouse();
            e.Handled = true;
        }

        private void LauncherMiniSeek_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_launcherSeekDragging || sender is not UIElement hit) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(hit);
            var width = hit is FrameworkElement fe ? fe.ActualWidth : 0;
            ApplyLauncherSeekFromHit(pos.X, width, commit: false);
        }

        private void LauncherMiniSeek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndLauncherSeekDrag(sender as UIElement, seek: true);
            e.Handled = true;
        }

        private void LauncherMiniSeek_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_launcherSeekDragging)
                EndLauncherSeekDrag(sender as UIElement, seek: true);
        }

        private void BtnLauncherMiniVolume_Click(object sender, RoutedEventArgs e)
        {
            if (PopLauncherVolume == null) return;
            PopLauncherVolume.IsOpen = !PopLauncherVolume.IsOpen;
        }

        private void SldLauncherMiniVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            _settings.SetMiniPlayerVolume(e.NewValue);
            if (_musicSyncMuted)
                return;
            _miniPlayer.Volume = e.NewValue;
        }

        private async void CmbLauncherMiniSongs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_launcherSongComboQuiet) return;
            if (CmbLauncherMiniSongs?.SelectedItem is not CachedSongInfo song) return;
            try
            {
                if (!await EnsureMiniQueueAsync()) return;
                if (_miniPlayer.Current?.Id.Equals(song.Id, StringComparison.OrdinalIgnoreCase) == true
                    && _miniPlayer.IsPlaying)
                    return;
                SetLauncherMarqueeText("Loading…");
                _miniPlayer.PlayTrack(song, _miniPlayer.Queue.Count > 0 ? _miniPlayer.Queue : null);
                UpdateLauncherMiniPlayer();
                _ = PrefetchNeighborPreviewsAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Music", ex.Message);
            }
        }

        private async void BtnLauncherMiniPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!await EnsureMiniQueueAsync())
                    return;
                if (_miniPlayer.Current == null)
                    return;
                SetLauncherMarqueeText("Loading…");
                _miniPlayer.TogglePlayPause();
                if (_musicSyncMuted)
                {
                    if (_miniPlayer.IsPlaying)
                        PushSyncedResume();
                    else
                        PushSyncedPause();
                }
                UpdateLauncherMiniPlayer();
                _ = PrefetchNeighborPreviewsAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Music", ex.Message);
                UpdateLauncherMiniPlayer();
            }
        }

        private async void BtnLauncherMiniPrev_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsureMiniQueueAsync()) return;
            _miniPlayer.Previous();
            UpdateLauncherMiniPlayer();
        }

        private async void BtnLauncherMiniNext_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsureMiniQueueAsync()) return;
            _miniPlayer.Next();
            UpdateLauncherMiniPlayer();
        }

        private void BtnLauncherMiniLoop_Click(object sender, RoutedEventArgs e)
        {
            _miniPlayer.ToggleRepeat();
            UpdateLauncherMiniPlayer();
        }

        private void PollMusicSync()
        {
            if (!_settings.GetSyncMusicWithGame())
            {
                EndMusicSync();
                return;
            }
            var shell = ResolveShellRoot();
            if (shell == null || !ShellConfigureLaunchService.IsGameRunning())
            {
                EndMusicSync();
                return;
            }
            if (!_musicSyncMuted)
            {
                _musicSyncWasPlaying = _miniPlayer.IsPlaying;
                _musicSyncPushed = false;
                _musicSyncHoldTicks = 0;
                _musicSyncSentId = null;
                _miniPlayer.Volume = 0;
                _musicSyncMuted = true;
            }
            string? id = null;
            string? name = null;
            JObject? now = null;
            string? nowPath = null;
            try
            {
                nowPath = Path.Combine(ShellWorkspaceService.StateDirectory(shell), "now-playing.json");
                if (File.Exists(nowPath))
                {
                    if (_musicSyncLaunchUtc != DateTime.MinValue
                        && File.GetLastWriteTimeUtc(nowPath) <= _musicSyncLaunchUtc)
                        return;
                    now = JObject.Parse(File.ReadAllText(nowPath));
                    id = now["id"]?.ToString();
                    name = now["name"]?.ToString();
                }
            }
            catch
            {
                return;
            }
            var songs = _musicCache?.Songs;
            if (songs == null || songs.Count == 0)
                songs = _miniPlayer.Queue.ToList();
            var reported = MusicSync.Find(songs, id, name);
            var gamePaused = now?["paused"]?.Value<bool>() ?? false;
            if (reported == null)
                return;
            if (_musicSyncPushed)
            {
                _musicSyncHoldTicks++;
                if (MusicSync.IdMatch(reported.Id, _musicSyncSentId) || _musicSyncHoldTicks > 6)
                {
                    _musicSyncPushed = false;
                    _musicSyncHoldTicks = 0;
                }
                else
                {
                    return;
                }
            }
            if (MusicSync.Same(_miniPlayer.Current, reported))
            {
                MirrorJukeboxPause(gamePaused);
                return;
            }
            _musicSyncApplying = true;
            try
            {
                if (!_miniPlayer.IsPlaying)
                    _musicSyncStartedPlayback = true;
                var queue = _miniPlayer.Queue.Count > 0 ? _miniPlayer.Queue : songs;
                _miniPlayer.PlayTrack(reported, queue);
                _miniPlayer.Volume = 0;
                if (gamePaused)
                    _miniPlayer.Pause();
            }
            finally
            {
                _musicSyncApplying = false;
            }
        }

        private void MirrorJukeboxPause(bool paused)
        {
            if ((DateTime.UtcNow - _musicSyncCommandUtc).TotalMilliseconds < 1500)
                return;
            var play = MusicSync.ShouldPlay(paused, _miniPlayer.IsPlaying);
            if (play == null || (play == true && _miniPlayer.Current == null))
                return;
            _musicSyncApplying = true;
            try
            {
                if (play == true)
                    _miniPlayer.Play();
                else
                    _miniPlayer.Pause();
            }
            finally { _musicSyncApplying = false; }
        }

        private void NoteMusicSyncCommand()
        {
            _musicSyncCommandUtc = DateTime.UtcNow;
        }

        private void PushSyncedSong(string id)
        {
            var shell = ResolveShellRoot();
            if (shell == null || string.IsNullOrWhiteSpace(id))
                return;
            NoteMusicSyncCommand();
            try
            {
                ShellConfigureLaunchService.SendLiveOp(shell, "previewMusic", new JObject
                {
                    ["musicId"] = id,
                    ["id"] = id,
                    ["force"] = true,
                    ["positionMs"] = Math.Max(0, _miniPlayer.Position.TotalMilliseconds)
                });
                _musicSyncSentId = id;
            }
            catch
            {
            }
        }

        private void PushSyncedPause()
        {
            var shell = ResolveShellRoot();
            if (shell == null)
                return;
            NoteMusicSyncCommand();
            try
            {
                ShellConfigureLaunchService.SendLiveOp(shell, "pauseMusic", new JObject());
            }
            catch
            {
            }
        }

        private void PushSyncedResume()
        {
            var shell = ResolveShellRoot();
            if (shell == null || _miniPlayer.Current == null)
                return;
            NoteMusicSyncCommand();
            try
            {
                ShellConfigureLaunchService.SendLiveOp(shell, "resumeMusic", new JObject
                {
                    ["positionMs"] = Math.Max(0, _miniPlayer.Position.TotalMilliseconds)
                });
            }
            catch
            {
            }
        }

        private void PushSyncedSeek(double positionMs)
        {
            var shell = ResolveShellRoot();
            if (shell == null || positionMs < 0)
                return;
            NoteMusicSyncCommand();
            try
            {
                ShellConfigureLaunchService.SendLiveOp(shell, "seekMusic", new JObject
                {
                    ["positionMs"] = positionMs
                });
            }
            catch
            {
            }
        }

        private void EndMusicSync()
        {
            if (!_musicSyncMuted)
                return;
            _musicSyncMuted = false;
            _musicSyncPushed = false;
            _musicSyncHoldTicks = 0;
            _musicSyncSentId = null;
            _miniPlayer.Volume = _settings.GetMiniPlayerVolume();
            _miniPlayer.Pause();
            _musicSyncStartedPlayback = false;
            _musicSyncWasPlaying = false;
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

        private void BtnOpenDocs_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://ssf2-mods-official.github.io/SSF2ModManager/")
            {
                UseShellExecute = true
            });
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
            if (TxtPlayLabel != null)
                TxtPlayLabel.Text = _settings.GetLaunchConfigure().UseOnEveryLaunch ? "Play*" : "Play";
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
            ApplySectionAccent(title);
            ShowPageChrome();
        }

        private void ShowHosted(string title, Window window)
        {
            ReleaseHosted();
            var page = window.Content as UIElement ?? new TextBlock { Text = title };
            window.Content = null;
            if (page is System.Windows.Controls.Panel panel)
                panel.Background = Brushes.Transparent;
            _hostedWindows.Add(window);
            _crumbs.Clear();
            _crumbs.Add(new Crumb("Home", LeavePage));
            _crumbs.Add(new Crumb(title, () => { }));
            PageContent.Content = page;
            ApplySectionAccent(title);
            ShowPageChrome();
        }

        private void SyncMakeTrail()
        {
            if (_makeMods == null || !_hostedWindows.Contains(_makeMods))
                return;
            _crumbs.Clear();
            _crumbs.Add(new Crumb("Home", LeavePage));
            SectionBar.Visibility = _makeMods.HideTrail ? Visibility.Collapsed : Visibility.Visible;
            if (_makeMods.HideTrail)
            {
                Breadcrumb.Children.Clear();
                return;
            }
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
            StopLogoBounce(false);
            _lastActivityUtc = DateTime.UtcNow;
            HomeTiles.Visibility = Visibility.Collapsed;
            PageHost.Visibility = Visibility.Visible;
            if (BtnRailMusic != null)
                BtnRailMusic.Visibility = Visibility.Visible;
            SectionBar.Visibility = Visibility.Visible;
            RenderCrumbs();
        }

        private Grid? _playerHome;

        private void BtnRailMusic_Click(object sender, RoutedEventArgs e)
        {
            if (MiniPlayerPopup.IsOpen)
            {
                MiniPlayerPopup.IsOpen = false;
                return;
            }
            if (PlayerCard.Parent is Grid home)
                _playerHome = home;
            if (PlayerCard.Parent is System.Windows.Controls.Panel panel)
                panel.Children.Remove(PlayerCard);
            MiniPlayerPopupHost.Child = PlayerCard;
            MiniPlayerPopup.IsOpen = true;
        }

        private void MiniPlayerPopup_Closed(object? sender, EventArgs e) => ParkPlayerCard();

        private void CloseMiniPlayerPopup()
        {
            if (MiniPlayerPopup != null && MiniPlayerPopup.IsOpen)
                MiniPlayerPopup.IsOpen = false;
            else
                ParkPlayerCard();
        }

        private void ParkPlayerCard()
        {
            if (PlayerCard == null || _playerHome == null || PlayerCard.Parent == _playerHome)
                return;
            if (MiniPlayerPopupHost != null && MiniPlayerPopupHost.Child == PlayerCard)
                MiniPlayerPopupHost.Child = null;
            if (PlayerCard.Parent is System.Windows.Controls.Panel panel)
                panel.Children.Remove(PlayerCard);
            PlayerCard.SetValue(Grid.RowProperty, 0);
            PlayerCard.Margin = new Thickness(3, 3, 3, 0);
            _playerHome.Children.Add(PlayerCard);
        }

        private void LeavePage()
        {
            StopLogoBounce(false);
            _lastActivityUtc = DateTime.UtcNow;
            PageContent.Content = null;
            ReleaseHosted();
            _crumbs.Clear();
            CloseMiniPlayerPopup();
            PageHost.Visibility = Visibility.Collapsed;
            HomeTiles.Visibility = Visibility.Visible;
            if (BtnRailMusic != null)
                BtnRailMusic.Visibility = Visibility.Collapsed;
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
                        Foreground = Brushes.White
                    });
                }
                var crumb = _crumbs[i];
                var last = i == _crumbs.Count - 1;
                if (last)
                {
                    Breadcrumb.Children.Add(new TextBlock
                    {
                        Text = crumb.Label,
                        FontWeight = FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    });
                    continue;
                }
                var btn = new Button
                {
                    Content = crumb.Label,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
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
