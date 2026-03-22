using System.Configuration;
using System.Data;
using System.Windows;

using Microsoft.Win32;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace SSF2ModManager;

public partial class App : System.Windows.Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		// Register protocol if not present
		TryRegisterProtocol();

		// Handle ssf2mm: URL if present
		if (e.Args.Length > 0 && e.Args[0].StartsWith("ssf2mm:", StringComparison.OrdinalIgnoreCase))
		{
			string url = e.Args[0];
			// Example: ssf2mm:https://...zip,Mod,12345
			var match = Regex.Match(url, @"ssf2mm:(?<archive>https?://[^,]+)(,(?<type>[^,]+))?(,(?<id>\d+))?");
			if (match.Success)
			{
				string archiveUrl = match.Groups["archive"].Value;
				string modType = match.Groups["type"].Value;
				string modId = match.Groups["id"].Value;
				// Show a minimal window and trigger install
				var main = new MainWindow();
				main.Show();
				main.InstallModFromProtocol(archiveUrl, modType, modId);
				return;
			}
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

