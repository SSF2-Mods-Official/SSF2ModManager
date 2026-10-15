using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SSF2ModManager.Services;

namespace SSF2ModManager.Views
{
    public partial class GameDebugConsoleWindow : Window
    {
        private readonly GameDebugConsoleHost _host = new();
        private readonly StringBuilder _buffer = new();
        private readonly Func<string?> _getInstallRoot;
        private readonly Func<string, Task>? _installPatchAsync;

        public GameDebugConsoleWindow(
            Func<string?> getInstallRoot,
            Func<string, Task>? installPatchAsync = null)
        {
            InitializeComponent();
            _getInstallRoot = getInstallRoot;
            _installPatchAsync = installPatchAsync;
            _host.LineReceived += OnLine;
            _host.StatusChanged += OnStatus;
            Loaded += (_, _) =>
            {
                // Listen first so the UI is useful even if SWF scan is slow.
                TryStartListen();
                _ = RefreshPatchStatusAsync();
            };
            Closed += (_, _) => _host.Dispose();
        }

        private void OnStatus(string status) =>
            Dispatcher.BeginInvoke(() =>
            {
                TxtStatus.Text = status;
                // Keep Retry available whenever we are not listening.
                BtnRetryListen.IsEnabled = !_host.IsListening;
            });

        private void OnLine(string line) =>
            Dispatcher.BeginInvoke(() =>
            {
                var stamp = DateTime.Now.ToString("HH:mm:ss");
                _buffer.Append('[').Append(stamp).Append("] ").AppendLine(line);
                TxtLog.AppendText("[" + stamp + "] " + line + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });

        private void TryStartListen()
        {
            if (_host.IsListening)
            {
                TxtStatus.Text = $"Listening on 127.0.0.1:{_host.Port}";
                BtnRetryListen.IsEnabled = true; // allow forced rebind
                return;
            }

            try
            {
                _host.Start();
                BtnRetryListen.IsEnabled = true;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Failed to listen: " + ex.Message;
                BtnRetryListen.IsEnabled = true;
            }
        }

        private Task RefreshPatchStatusAsync()
        {
            return Task.Run(() =>
            {
                var root = _getInstallRoot()?.Trim();
                string text;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    text = "Patch: unknown (select a prepared SSF2 install)";
                }
                else if (!GamePrepareService.IsPrepared(root))
                {
                    text = "Patch: install not prepared yet";
                }
                else
                {
                    text = GamePrepareService.GetDebugConsolePatchStatus(root) switch
                    {
                        GamePrepareService.DebugConsolePatchStatus.Full =>
                            "Patch: installed (prints + uncaught errors)",
                        GamePrepareService.DebugConsolePatchStatus.PrintOnly =>
                            "Patch: prints only — upgrade for crash/uncaught capture",
                        _ =>
                            "Patch: not installed — use Install / upgrade patch…"
                    };
                }

                Dispatcher.BeginInvoke(() => TxtPatchStatus.Text = text);
            });
        }

        private void BtnRetryListen_Click(object sender, RoutedEventArgs e)
        {
            _host.Stop();
            TryStartListen();
        }

        private void BtnLaunchCmd_Click(object sender, RoutedEventArgs e)
        {
            var root = _getInstallRoot()?.Trim();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this, "Select a prepared SSF2 install first (Pressor).",
                    "Debug console", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Free the in-app port so the CMD host can bind.
            _host.Stop();
            TxtStatus.Text = "In-app listen stopped — use the CMD window.";
            BtnRetryListen.IsEnabled = true;

            try
            {
                GameDebugCmdHost.StartDetached(root, launchGame: true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Launch debug CMD",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _buffer.Clear();
            TxtLog.Clear();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Log|*.log;*.txt|All|*.*",
                FileName = "ssf2-debug-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"
            };
            if (dlg.ShowDialog(this) == true)
                File.WriteAllText(dlg.FileName, _buffer.ToString());
        }

        private async void BtnInstallPatch_Click(object sender, RoutedEventArgs e)
        {
            var root = _getInstallRoot()?.Trim();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this, "Select a prepared SSF2 install first (Pressor / Mappings).",
                    "Debug console", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnInstallPatch.IsEnabled = false;
            try
            {
                if (_installPatchAsync != null)
                {
                    await _installPatchAsync(root);
                    await RefreshPatchStatusAsync();
                    return;
                }

                var svc = new GamePrepareService();
                var result = await svc.InstallDebugConsoleAsync(
                    root, null, new Progress<string>(s => OnStatus(s)));
                await RefreshPatchStatusAsync();
                System.Windows.MessageBox.Show(this, result.Message, "Debug console patch",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Debug console patch",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnInstallPatch.IsEnabled = true;
            }
        }

        private async void BtnEnableDebugFlag_Click(object sender, RoutedEventArgs e)
        {
            var root = _getInstallRoot()?.Trim();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                System.Windows.MessageBox.Show(this, "Select a prepared SSF2 install first.",
                    "Enable Main.DEBUG", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var warn = System.Windows.MessageBox.Show(this,
                "This replaces Main.as constants only so Main.DEBUG becomes true.\n" +
                "A previous fuller Main.as patch crashed load at ~95% — if that happens again, " +
                "restore SSF2.swf.ssf2mm.debugflag.bak.\n\nContinue?",
                "Enable Main.DEBUG", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (warn != MessageBoxResult.Yes) return;

            BtnEnableDebugFlag.IsEnabled = false;
            try
            {
                var svc = new GamePrepareService();
                var result = await svc.EnableInGameDebugFlagAsync(
                    root, null, new Progress<string>(s => OnStatus(s)));
                System.Windows.MessageBox.Show(this, result.Message, "Enable Main.DEBUG",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Enable Main.DEBUG",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnEnableDebugFlag.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
