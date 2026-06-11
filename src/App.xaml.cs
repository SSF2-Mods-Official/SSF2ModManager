using SSF2ModManager.Models;
using SSF2ModManager.Services;
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

    private static void TryAttachParentConsole()
    {
        try { AttachConsole(AttachParentProcess); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var cli = ParseCliOptions(e.Args);
        var headlessCli = cli.Help || cli.Version ||
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

                System.Windows.MessageBox.Show("SSF2 Mod Manager is already running.", "Already Running",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }
        if (cli.Verbose || cli.Diagnostics || cli.TraceApi)
            DevFileLog.SetEnabled(true);

        DevFileLog.Reset($"[OnStartup] {DateTime.Now}: Entered\n");

        string lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var langArg = e.Args.FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase));
        if (langArg != null)
            lang = langArg.Substring(7);
        DevFileLog.Write($"[OnStartup] Loading language: {lang}\n");
        Localization.Load(lang);

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

        if (cli.Diagnostics)
        {
            Console.WriteLine(ProtocolService.GetRegistrationStatus());
            Console.WriteLine($"Executable: {ProtocolService.GetExePath()}");
            Console.WriteLine($"Version: {AppInfo.DisplayVersion} ({AppInfo.GetAssemblyVersion()})");
            Console.WriteLine($"Protocol registered: {ProtocolService.IsRegistered()}");
            if (!cli.OpenNews && cli.Install.Count == 0)
            {
                Shutdown();
                return;
            }
        }

        if (cli.RegisterProtocol)
            ProtocolService.Register();
        else if (!cli.UnregisterProtocol)
            ProtocolService.Register();

        if (cli.UnregisterProtocol)
            ProtocolService.Unregister();

        base.OnStartup(e);

        var win = new MainWindow();

        if (cli.StartMinimized)
            win.WindowState = WindowState.Minimized;

        if (!cli.StartHidden)
            win.Show();

        try { DispatchArguments(win, e.Args, cli); }
        catch (Exception ex) { DebugLogger.Error("DispatchArguments failed", ex); }
    }

    internal static void DispatchArguments(MainWindow main, string[] args, CliOptions? cli = null)
    {
        cli ??= ParseCliOptions(args);

        var protocolArg = args.FirstOrDefault(a =>
            a.StartsWith(ProtocolService.Scheme + ":", StringComparison.OrdinalIgnoreCase));
        if (protocolArg != null && ProtocolService.TryParse(protocolArg, out var proto))
        {
            DevFileLog.Write($"[DispatchArguments] Protocol URL: {proto.RawUrl}\n");
            _ = main.InstallModFromProtocolAsync(proto.ArchiveUrl, proto.ModType, proto.ModId?.ToString() ?? "");
            return;
        }

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
            if (arg.StartsWith("--screenshot-news=", StringComparison.OrdinalIgnoreCase)) { cli.ScreenshotNews = arg.Substring("--screenshot-news=".Length).Trim('"'); continue; }
            if (arg == "--diagnostics") { cli.Diagnostics = true; continue; }
            if (arg == "--ci-mode") { cli.CiMode = true; continue; }
            if (arg == "--yes" || arg == "-y") { cli.Yes = true; continue; }
            if (arg == "--dry-run") { cli.DryRun = true; continue; }
            if (arg.StartsWith("--output-format=", StringComparison.OrdinalIgnoreCase)) { cli.OutputFormat = arg.Substring("--output-format=".Length).Trim('"'); continue; }
            if (arg == "--register-protocol") { cli.RegisterProtocol = true; continue; }
            if (arg == "--unregister-protocol") { cli.UnregisterProtocol = true; continue; }
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
          --open-page=<page>      Open a page (browse, installed, settings, news, ...)
          --open-news             Open the News page
          --theme=<name>          Apply a theme on startup
        """;
}
