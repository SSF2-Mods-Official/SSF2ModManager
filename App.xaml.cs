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
		win.Show();
		File.AppendAllText("ssf2mm-debug.log", $"[OnStartup] MainWindow.Show() complete\n");
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

