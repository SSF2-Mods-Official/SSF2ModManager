using SSF2ModManager.Models;
using SSF2ModManager.Services;
using SSF2ModManager.Views;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace SSF2ModManager;

public partial class App : System.Windows.Application
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private static System.Threading.Mutex? _mutex;
    private static LauncherWindow? _launcher;
    private static MainWindow? _manager;
    private static bool _exiting;

    private static void TryAttachParentConsole()
    {
        try { AttachConsole(AttachParentProcess); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var cli = ParseCliOptions(e.Args);
        var debugHost = !string.IsNullOrWhiteSpace(cli.DebugLaunchRoot)
                        || !string.IsNullOrWhiteSpace(cli.DebugListenRoot);
        var headlessCli = cli.Help || cli.Version || debugHost ||
            IsHeadlessNewsRefresh(cli) ||
            (cli.Diagnostics && !cli.OpenNews && cli.Install.Count == 0 && !cli.RegisterProtocol && !cli.UnregisterProtocol);

        if (!headlessCli)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                DebugLogger.Error("UnhandledException", ex.ExceptionObject as Exception);
                DevFileLog.Write($"[FATAL] UnhandledException: {ex.ExceptionObject}\n");
            };
            DispatcherUnhandledException += (s, ex) =>
            {
                DebugLogger.Error("DispatcherUnhandledException", ex.Exception);
                DevFileLog.Write($"[FATAL] DispatcherUnhandledException: {ex.Exception}\n");
                ex.Handled = true;
            };

            _mutex = new System.Threading.Mutex(true, SingleInstanceService.MutexName, out var createdNew);
            if (!createdNew)
            {
                if (SingleInstanceService.TryForwardArguments(e.Args))
                {
                    Shutdown();
                    return;
                }

                // No args to forward — activate existing instance by sending a no-op open
                if (SingleInstanceService.TryForwardArguments(new[] { "--activate" }))
                {
                    Shutdown();
                    return;
                }

                System.Windows.MessageBox.Show(Services.AppInfo.ProductName + " is already running.", "Already Running",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }
        if (cli.Verbose || cli.Diagnostics || cli.TraceApi)
            DevFileLog.SetEnabled(true);

        DevFileLog.Reset($"[OnStartup] {DateTime.Now}: Entered\n");

        if (headlessCli || cli.Diagnostics)
            TryAttachParentConsole();

        if (cli.Help)
        {
            Console.WriteLine(GetHelpText());
            Shutdown();
            return;
        }

        if (cli.Version)
        {
            Console.WriteLine($"{AppInfo.ProductName} {AppInfo.DisplayVersion}");
            Shutdown();
            return;
        }

        if (debugHost)
        {
            var root = !string.IsNullOrWhiteSpace(cli.DebugLaunchRoot)
                ? cli.DebugLaunchRoot!
                : cli.DebugListenRoot!;
            var launch = !string.IsNullOrWhiteSpace(cli.DebugLaunchRoot);
            Environment.ExitCode = GameDebugCmdHost.Run(root, launch);
            Shutdown();
            return;
        }

        if (cli.Diagnostics)
        {
            Console.WriteLine(ProtocolService.GetRegistrationStatus());
            Console.WriteLine($"Executable: {ProtocolService.GetExePath()}");
            Console.WriteLine($"Version: {AppInfo.DisplayVersion} ({AppInfo.GetAssemblyVersion()})");
            Console.WriteLine($"Protocol registered: {ProtocolService.IsRegistered()}");
            if (!cli.OpenNews && cli.Install.Count == 0 && !IsHeadlessNewsRefresh(cli))
            {
                Shutdown();
                return;
            }
        }

        if (IsHeadlessNewsRefresh(cli))
        {
            if (cli.NewsCacheClear)
                NewsSyncService.ClearCache();
            var settings = new SettingsService();
            var sync = NewsSyncService.SyncAsync(settings, force: true).GetAwaiter().GetResult();
            if (sync.Success)
                Console.WriteLine($"News sync OK — {sync.DownloadedCount} article(s) updated");
            else
            {
                Console.Error.WriteLine($"News sync failed: {sync.ErrorMessage}");
                Environment.ExitCode = 1;
            }
            Shutdown();
            return;
        }

        if (cli.UnregisterProtocol)
            ProtocolService.Unregister();

        var savedLang = new SettingsService().GetLanguage();
        var lang = string.IsNullOrWhiteSpace(savedLang)
            ? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : savedLang;
        var langArg = e.Args.FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase));
        if (langArg != null)
            lang = langArg.Substring(7);

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        SingleInstanceService.StartListening(args =>
        {
            Dispatcher.Invoke(() => HandleForwardedArguments(args));
        });

        var protocolArg = ProtocolArgument(e.Args);
        var oneClick = ProtocolService.IsOneClickLaunch(protocolArg);
        if (!oneClick && NeedsFullManager(cli, e.Args))
        {
            Localization.Load(lang);
            ShowManager(cli, e.Args, startMinimized: cli.StartMinimized, startHidden: cli.StartHidden);
            if (!cli.UnregisterProtocol)
            {
                try { ProtocolService.Register(); } catch { }
            }
        }
        else
        {
            _launcher = new LauncherWindow();
            MainWindow = _launcher;
            if (cli.StartMinimized)
                _launcher.WindowState = WindowState.Minimized;
            if (!cli.StartHidden)
                _launcher.Show();

            var clickUrl = oneClick ? protocolArg : cli.Install.FirstOrDefault(ProtocolService.IsOneClickLaunch);
            if (!string.IsNullOrWhiteSpace(clickUrl))
                Dispatcher.BeginInvoke(() => _launcher.ReceiveOneClick(clickUrl));

            Dispatcher.BeginInvoke(() =>
            {
                try { Localization.Load(lang); } catch { }
                try
                {
                    if (!cli.UnregisterProtocol)
                        ProtocolService.Register();
                }
                catch { }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static bool NeedsFullManager(CliOptions cli, string[] args)
    {
        if (!string.IsNullOrWhiteSpace(cli.OpenPage)) return true;
        if (cli.OpenNews) return true;
        if (cli.Install.Any(t => !ProtocolService.IsOneClickLaunch(t))) return true;
        if (cli.Uninstall.Count > 0 || cli.Enable.Count > 0 || cli.Disable.Count > 0 || cli.Update.Count > 0)
            return true;
        return false;
    }

    private static string? ProtocolArgument(string[] args) =>
        args.FirstOrDefault(a => a.StartsWith(ProtocolService.Scheme + ":", StringComparison.OrdinalIgnoreCase));

    internal static LauncherWindow EnsureLauncher()
    {
        if (_launcher == null || !_launcher.IsLoaded)
        {
            _launcher = new LauncherWindow();
            if (_manager == null && Current != null)
                Current.MainWindow = _launcher;
            _launcher.Show();
        }
        else
        {
            _launcher.Activate();
        }
        return _launcher;
    }

    private static void HandleForwardedArguments(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0].Equals("--activate", StringComparison.OrdinalIgnoreCase))
            {
                _launcher?.Activate();
                _manager?.Activate();
                return;
            }

            var cli = ParseCliOptions(args);
            var forwardedProtocol = ProtocolArgument(args);
            if (ProtocolService.IsOneClickLaunch(forwardedProtocol))
            {
                EnsureLauncher().ReceiveOneClick(forwardedProtocol!);
                return;
            }
            if (NeedsFullManager(cli, args))
            {
                ShowManager(cli, args, startMinimized: false, startHidden: false);
                return;
            }

            _launcher?.Activate();
        }
        catch (Exception ex)
        {
            DebugLogger.Error("HandleForwardedArguments failed", ex);
        }
    }

    private static void ShowManager(CliOptions cli, string[] args, bool startMinimized, bool startHidden)
    {
        if (_manager == null || !_manager.IsLoaded)
        {
            _manager = new MainWindow();
            _manager.Closed += OnManagerClosed;
            if (_launcher == null)
                Current.MainWindow = _manager;
            if (startMinimized)
                _manager.WindowState = WindowState.Minimized;
            if (!startHidden)
                _manager.Show();
        }
        else
        {
            _manager.Activate();
        }

        try { DispatchArguments(_manager, args, cli); }
        catch (Exception ex) { DebugLogger.Error("DispatchArguments failed", ex); }
    }

    /// <summary>Used by the launcher hub to open a manager page.</summary>
    internal static MainWindow EnsureManagerWindow()
    {
        if (_manager == null || !_manager.IsLoaded)
        {
            _manager = new MainWindow();
            _manager.Closed += OnManagerClosed;
            if (_launcher == null)
                Current.MainWindow = _manager;
            _manager.Show();
        }
        return _manager;
    }

    private static void OnManagerClosed(object? sender, EventArgs e)
    {
        _manager = null;
        if (_exiting) return;
        if (_launcher != null && _launcher.IsVisible)
            return;
        RequestFullExit();
    }

    /// <summary>
    /// Close leftover windows (hosted Make Mods / Settings, JPEXS workers, pipe)
    /// so the process actually exits and the single-instance mutex is released.
    /// </summary>
    internal static void RequestFullExit()
    {
        if (_exiting) return;
        _exiting = true;
        CleanupRuntime();
        try
        {
            if (Current != null)
            {
                foreach (Window w in Current.Windows.Cast<Window>().ToArray())
                {
                    try { w.Close(); } catch { }
                }
                try { Current.Shutdown(); } catch { }
            }
        }
        catch { }
        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupRuntime();
        base.OnExit(e);
    }

    private static void CleanupRuntime()
    {
        try { SingleInstanceService.StopListening(); } catch { }
        try { JpexsCliService.ShutdownWorkers(); } catch { }
        try { LauncherWindow.DisposeThemes(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }

    internal static void DispatchArguments(MainWindow main, string[] args, CliOptions? cli = null)
    {
        cli ??= ParseCliOptions(args);
        main.ApplyCliOptions(cli);
    }

    private static CliOptions ParseCliOptions(string[] args)
    {
        var cli = new CliOptions();
        foreach (var a in args)
        {
            if (string.IsNullOrWhiteSpace(a)) continue;
            var arg = a.Trim();
            if (arg == "--help" || arg == "-h") { cli.Help = true; continue; }
            if (arg == "--version" || arg == "-v") { cli.Version = true; continue; }
            if (arg == "--verbose" || arg == "-V") { cli.Verbose = true; continue; }
            if (arg.StartsWith("--log-file=", StringComparison.OrdinalIgnoreCase)) { cli.LogFile = arg.Substring("--log-file=".Length).Trim('"'); continue; }
            if (arg == "--start-minimized" || arg == "--minimized") { cli.StartMinimized = true; continue; }
            if (arg == "--start-hidden") { cli.StartHidden = true; continue; }
            if (arg.StartsWith("--open-page=", StringComparison.OrdinalIgnoreCase)) { cli.OpenPage = arg.Substring("--open-page=".Length).Trim('"'); continue; }
            if (arg == "--open-news" || arg == "--news") { cli.OpenNews = true; continue; }
            if (arg.StartsWith("--news-path=", StringComparison.OrdinalIgnoreCase)) { cli.NewsPath = arg.Substring("--news-path=".Length).Trim('"'); continue; }
            if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase)) { cli.Theme = arg.Substring("--theme=".Length).Trim('"'); continue; }
            if (arg == "--refresh-news") { cli.RefreshNews = true; continue; }
            if (arg == "--news-cache-clear") { cli.NewsCacheClear = true; continue; }
            if (arg == "--news-preview-only") { cli.NewsPreviewOnly = true; continue; }
            if (arg.StartsWith("--install=", StringComparison.OrdinalIgnoreCase)) { cli.Install.Add(arg.Substring("--install=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--install-from=", StringComparison.OrdinalIgnoreCase)) { cli.Install.Add(arg.Substring("--install-from=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--uninstall=", StringComparison.OrdinalIgnoreCase)) { cli.Uninstall.Add(arg.Substring("--uninstall=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--enable=", StringComparison.OrdinalIgnoreCase)) { cli.Enable.Add(arg.Substring("--enable=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--disable=", StringComparison.OrdinalIgnoreCase)) { cli.Disable.Add(arg.Substring("--disable=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--update=", StringComparison.OrdinalIgnoreCase)) { cli.Update.Add(arg.Substring("--update=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--import-gb=", StringComparison.OrdinalIgnoreCase)) { cli.ImportGb.Add(arg.Substring("--import-gb=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--install-batch=", StringComparison.OrdinalIgnoreCase)) { cli.InstallBatch = arg.Substring("--install-batch=".Length).Trim('"'); continue; }
            if (arg.StartsWith("--search=", StringComparison.OrdinalIgnoreCase)) { cli.SearchQuery = arg.Substring("--search=".Length).Trim('"'); continue; }
            if (arg.StartsWith("--download-result=", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(arg.Substring("--download-result=".Length), out var idx)) cli.DownloadResultIndex = idx; continue; }
            if (arg.StartsWith("--browse-limit=", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(arg.Substring("--browse-limit=".Length), out var l)) cli.BrowseLimit = l; continue; }
            if (arg.StartsWith("--add-build=", StringComparison.OrdinalIgnoreCase)) { cli.AddBuild.Add(arg.Substring("--add-build=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--remove-build=", StringComparison.OrdinalIgnoreCase)) { cli.RemoveBuild.Add(arg.Substring("--remove-build=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--launch-build=", StringComparison.OrdinalIgnoreCase)) { cli.LaunchBuild.Add(arg.Substring("--launch-build=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase)) { cli.ConfigPath = arg.Substring("--config=".Length).Trim('"'); continue; }
            if (arg.StartsWith("--export-config=", StringComparison.OrdinalIgnoreCase)) { cli.ExportConfig = arg.Substring("--export-config=".Length).Trim('"'); continue; }
            if (arg.StartsWith("--import-config=", StringComparison.OrdinalIgnoreCase)) { cli.ImportConfig = arg.Substring("--import-config=".Length).Trim('"'); continue; }
            if (arg.StartsWith("--set-pref=", StringComparison.OrdinalIgnoreCase)) { cli.SetPrefs.Add(arg.Substring("--set-pref=".Length).Trim('"')); continue; }
            if (arg.StartsWith("--proxy=", StringComparison.OrdinalIgnoreCase)) { cli.Proxy = arg.Substring("--proxy=".Length).Trim('"'); continue; }
            if (arg == "--no-network") { cli.NoNetwork = true; continue; }
            if (arg == "--check-updates") { cli.CheckUpdates = true; continue; }
            if (arg == "--apply-update") { cli.ApplyUpdate = true; continue; }
            if (arg == "--self-install") { cli.SelfInstall = true; continue; }
            if (arg.StartsWith("--dump-log=", StringComparison.OrdinalIgnoreCase)) { cli.DumpLog = arg.Substring("--dump-log=".Length).Trim('"'); continue; }
            if (arg == "--trace-api") { cli.TraceApi = true; continue; }
            if (arg.StartsWith("--debug-launch=", StringComparison.OrdinalIgnoreCase))
            {
                cli.DebugLaunchRoot = arg.Substring("--debug-launch=".Length).Trim('"');
                continue;
            }
            if (arg.StartsWith("--debug-listen=", StringComparison.OrdinalIgnoreCase))
            {
                cli.DebugListenRoot = arg.Substring("--debug-listen=".Length).Trim('"');
                continue;
            }
            if (arg.StartsWith("--screenshot-news=", StringComparison.OrdinalIgnoreCase)) { cli.ScreenshotNews = arg.Substring("--screenshot-news=".Length).Trim('"'); continue; }
            if (arg == "--diagnostics") { cli.Diagnostics = true; continue; }
            if (arg == "--ci-mode") { cli.CiMode = true; continue; }
            if (arg == "--yes" || arg == "-y") { cli.Yes = true; continue; }
            if (arg == "--dry-run") { cli.DryRun = true; continue; }
            if (arg.StartsWith("--output-format=", StringComparison.OrdinalIgnoreCase)) { cli.OutputFormat = arg.Substring("--output-format=".Length).Trim('"'); continue; }
            if (arg == "--register-protocol") { cli.RegisterProtocol = true; continue; }
            if (arg == "--unregister-protocol") { cli.UnregisterProtocol = true; continue; }
            if (arg == "--activate") continue;
            if (ProtocolService.IsOneClickLaunch(arg)) continue;
            if (!arg.StartsWith("-")) { cli.Install.Add(arg); continue; }
        }
        return cli;
    }

    private static string GetHelpText() =>
        $"""
        {AppInfo.ProductName} {AppInfo.DisplayVersion}

        Usage:
          SSF2ModManager.exe [options] [ssf2mm:URL]

        General:
          --help, -h              Show this help
          --version, -v           Print version
          --verbose, -V           Enable verbose file logging
          --diagnostics           Print protocol registration status

        Mod operations:
          --install=<url|file>    Install from GameBanana URL, ssf2mm: link, or local archive
          --uninstall=<name|id>   Uninstall an installed mod
          --enable=<name|id>      Enable a mod
          --disable=<name|id>     Disable a mod
          --update=<id|name|url>  Update or install a mod

        Protocol (ssf2mm:downloadUrl,Mod,modId):
          --register-protocol     Register ssf2mm: handler
          --unregister-protocol   Remove ssf2mm: handler

        Updates:
          --check-updates         Check for a newer app version

        UI:
          --open-page=<page>      Open a page (browse, installed, clonecharacter, make, ...)
          --open-news             Open the News page
          --refresh-news          Sync news from GitHub (headless if no other UI flags)
          --news-cache-clear      Clear cached remote news before sync
          --theme=<name>          Apply a theme on startup

        Game debug:
          --debug-launch=<folder> Open a CMD debug console, listen, and launch SSF2.exe
          --debug-listen=<folder> Open a CMD debug console and listen only
        """;

    private static bool IsHeadlessNewsRefresh(CliOptions cli) =>
        cli.RefreshNews && !cli.OpenNews && cli.Install.Count == 0 &&
        !cli.RegisterProtocol && !cli.UnregisterProtocol &&
        string.IsNullOrWhiteSpace(cli.OpenPage) && !cli.StartHidden;
}
