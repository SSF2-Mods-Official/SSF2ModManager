using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;

using Microsoft.Win32;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace SSF2ModManager;


public partial class App : System.Windows.Application
{
	private static System.Threading.Mutex? _mutex;
	protected override void OnStartup(StartupEventArgs e)
	{
		// Global exception logging
		AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
		{
			try
			{
				File.AppendAllText("ssf2mm-debug.log", $"[FATAL] UnhandledException: {ex.ExceptionObject}\n");
			}
			catch { }
		};
		this.DispatcherUnhandledException += (s, ex) =>
		{
			try
			{
				File.AppendAllText("ssf2mm-debug.log", $"[FATAL] DispatcherUnhandledException: {ex.Exception}\n");
			}
			catch { }
		};
	{
		const string mutexName = "SSF2ModManager_SINGLE_INSTANCE_MUTEX";
		bool createdNew;
		_mutex = new System.Threading.Mutex(true, mutexName, out createdNew);
		if (!createdNew)
		{
			System.Windows.MessageBox.Show("SSF2 Mod Manager is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Warning);
			Shutdown();
			return;
		}

		File.WriteAllText("ssf2mm-debug.log", $"[OnStartup] {DateTime.Now}: Entered\n");
		// Language setup
		string lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
		if (e.Args.Length > 0)
		{
			var langArg = e.Args.FirstOrDefault(a => a.StartsWith("--lang="));
			if (langArg != null)
				lang = langArg.Substring(7);
		}
		File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] Loading language: {lang}\n");
		Localization.Load(lang);
		File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] Language loaded\n");

		base.OnStartup(e);
		File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] base.OnStartup complete\n");

		// Register protocol if not present
		TryRegisterProtocol();
		File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] Protocol registered\n");

		// Handle ssf2mm: URL if present
		if (e.Args.Length > 0 && e.Args[0].StartsWith("ssf2mm:", StringComparison.OrdinalIgnoreCase))
		{
			string url = e.Args[0];
			File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] Protocol URL detected: {url}\n");
			var match = Regex.Match(url, @"ssf2mm:(?<archive>https?://[^,]+)(,(?<type>[^,]+))?(,(?<id>\d+))?");
			if (match.Success)
			{
				string archiveUrl = match.Groups["archive"].Value;
				string modType = match.Groups["type"].Value;
				string modId = match.Groups["id"].Value;
				File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] Launching MainWindow for protocol\n");
				var main = new MainWindow();
				main.Show();
				main.InstallModFromProtocol(archiveUrl, modType, modId);
				return;
			}
		}
		File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] Launching MainWindow (normal)\n");
		var win = new MainWindow();

		// Parse CLI into CliOptions and apply
		var cli = new SSF2ModManager.Models.CliOptions();
		foreach (var a in e.Args)
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

			// Fallback: treat bare args as install targets
			if (!arg.StartsWith("-")) { cli.Install.Add(arg); continue; }
		}

		// Pre-show actions: help/version
		if (cli.Help)
		{
			var helpText = "SSF2 Mod Manager CLI\n" +
				"--help, -h: Show help\n" +
				"--version, -v: Print version\n" +
				"--open-news --news-path=<path> --install=<url|file> ...\n";
			System.Console.WriteLine(helpText);
			Shutdown();
			return;
		}
		if (cli.Version)
		{
			System.Console.WriteLine("SSF2 Mod Manager version: 1.0 (dev)");
			Shutdown();
			return;
		}

		if (cli.StartMinimized)
			win.WindowState = System.Windows.WindowState.Minimized;

		if (cli.StartHidden)
		{
			// Show hidden (don't call Show)
		}
		else
		{
			win.Show();
			File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] MainWindow.Show() complete\n");
		}

		// Apply CLI options to main window
		try
		{
			win.ApplyCliOptions(cli);
		}
		catch { }
		}
	}

	private void TryRegisterProtocol()
	{
		try
		{
			const string protocol = "ssf2mm";
			string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
			using var key = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{protocol}");
			if (key == null) return;
			key.SetValue("", "URL:SSF2 Mod Manager Protocol");
			key.SetValue("URL Protocol", "");
			using var defaultIcon = key.CreateSubKey("DefaultIcon");
			defaultIcon?.SetValue("", exePath + ",1");
			using var command = key.CreateSubKey("shell\\open\\command");
			command?.SetValue("", $"\"{exePath}\" \"%1\"");
		}
		catch { /* ignore errors */ }
	}


}

