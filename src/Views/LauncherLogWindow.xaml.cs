using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SSF2ModManager.Services;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace SSF2ModManager.Views
{
    public partial class LauncherLogWindow : Window
    {
        private static readonly Regex HasLeadingTimestamp = new(
            @"^\[?\d{1,4}[-/]\d{1,2}[-/]\d{1,4}[ T]\d{1,2}:\d{2}",
            RegexOptions.Compiled);

        private static readonly Regex BracketTime = new(
            @"^\[(\d{1,2}:\d{2}:\d{2})\]\s*",
            RegexOptions.Compiled);

        private static readonly Regex StackFrameLine = new(
            @"^\s+at\s+",
            RegexOptions.Compiled);

        private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0xC8, 0xC8, 0xD4));
        private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
        private static readonly SolidColorBrush ErrorBgBrush = new(Color.FromArgb(0x40, 0xFF, 0x3B, 0x3B));

        private readonly string? _installRoot;
        private readonly DispatcherTimer _timer;
        private string _lastText = "";
		private bool _stickToEnd = true;
        private bool _rebuildingDoc;
        private bool _pinningEnd;

        public LauncherLogWindow(string? installRoot, bool ssf2ErrorsOnly = false)
        {
            _installRoot = installRoot;
            InitializeComponent();
            NormalBrush.Freeze();
            ErrorBrush.Freeze();
            ErrorBgBrush.Freeze();
            if (ssf2ErrorsOnly && ChkSsf2ErrorsOnly != null)
            {
                ChkSsf2ErrorsOnly.IsChecked = true;
                ApplyErrorsOnlySourceFilter();
            }
            TxtLog.PreviewMouseWheel += (_, e) =>
            {
                if (e.Delta > 0)
                {
                    _stickToEnd = false;
                    _pinningEnd = false;
                }
            };
            TxtLog.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent,
                new System.Windows.Controls.ScrollChangedEventHandler((_, e) =>
                {
                    // Replacing Document resets offset to 0. Ignore that while pinning to the end.
                    if (_rebuildingDoc || _pinningEnd || e.ExtentHeight <= 0)
                        return;
                    if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && e.VerticalChange == 0)
                        return;
                    var atBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 8;
                    _stickToEnd = atBottom;
                }), true);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => Reload();
            _timer.Start();
            Reload();
        }


        public event EventHandler<bool>? FollowChanged;
        private bool _suppressFollow;

        public void SetFollow(bool on)
        {
            _suppressFollow = true;
            if (ChkFollowGame != null)
                ChkFollowGame.IsChecked = on;
            _suppressFollow = false;
        }

        private void Follow_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressFollow || ChkFollowGame == null)
                return;
            FollowChanged?.Invoke(this, ChkFollowGame.IsChecked == true);
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _lastText = "";
            Reload();
        }

        private void ErrorsOnly_Changed(object sender, RoutedEventArgs e)
        {
            ApplyErrorsOnlySourceFilter();
            _lastText = "";
            Reload();
        }

        private void ApplyErrorsOnlySourceFilter()
        {
            if (ChkSsf2ErrorsOnly?.IsChecked != true)
                return;
            if (ChkDebug != null) ChkDebug.IsChecked = false;
            if (ChkManager != null) ChkManager.IsChecked = false;
            if (ChkGame != null) ChkGame.IsChecked = true;
        }

        /// <summary>Turn on SSF2 Errors only (used when opening from a game crash alert).</summary>
        public void ShowSsf2ErrorsOnly()
        {
            if (ChkSsf2ErrorsOnly != null)
                ChkSsf2ErrorsOnly.IsChecked = true;
            ApplyErrorsOnlySourceFilter();
            _stickToEnd = true;
            _lastText = "";
            Reload();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _stickToEnd = true;
            Reload();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = TxtLog.Selection?.Text;
                System.Windows.Clipboard.SetText(
                    string.IsNullOrEmpty(selected) ? PlainTextFromDoc() : selected);
            }
            catch
            {
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (ChkDebug.IsChecked == true)
            {
                try { File.WriteAllText(AppPaths.DebugLogFile, ""); } catch { }
            }
            if (ChkManager.IsChecked == true)
                DebugLogger.Clear();
            if (ChkGame.IsChecked == true)
            {
                foreach (var path in GameLogPaths())
                {
                    try { if (File.Exists(path)) File.WriteAllText(path, ""); } catch { }
                }
            }
            _stickToEnd = true;
            Reload();
        }

        private void Reload()
        {
            if (TxtLog == null)
                return;
            var errorsOnly = ChkSsf2ErrorsOnly?.IsChecked == true;
            var latest = ChkLatestRun?.IsChecked == true;
            var sb = new StringBuilder();
            if (!errorsOnly && ChkDebug?.IsChecked == true)
                Append(sb, "Debug", AppPaths.DebugLogFile, false, latest);
            if (!errorsOnly && ChkManager?.IsChecked == true)
                Append(sb, "Mod Manager", Path.Combine(AppPaths.AppDataDir, "debug.log"), false, latest);
            if (ChkGame?.IsChecked == true || errorsOnly)
            {
                var any = false;
                foreach (var path in GameLogPaths())
                {
                    if (!File.Exists(path))
                        continue;
                    any = true;
                    Append(sb, "SSF2", path, errorsOnly, latest);
                }
                if (!any)
                    sb.AppendLine(StampNow() + " [SSF2] No game log yet. Launch SSF2 from the manager to start one.");
                else if (errorsOnly && sb.Length == 0)
                    sb.AppendLine(StampNow() + " [SSF2] No SSF2 errors in the current log.");
            }
            var text = sb.Length == 0 ? StampNow() + " No log lines for the selected sources." : sb.ToString().TrimEnd();
            if (text == _lastText)
                return;
            _lastText = text;
            var wasEnd = _stickToEnd;
            RenderHighlighted(text);
            if (wasEnd)
            {
                _stickToEnd = true;
                _pinningEnd = true;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, PinLogToEnd);
            }
        }

        private void PinLogToEnd()
        {
            if (!_stickToEnd)
            {
                _pinningEnd = false;
                return;
            }
            try
            {
                var doc = TxtLog.Document;
                if (doc != null)
                    TxtLog.CaretPosition = doc.ContentEnd;
                TxtLog.ScrollToEnd();
            }
            catch
            {
            }
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                if (!_stickToEnd)
                {
                    _pinningEnd = false;
                    return;
                }
                try
                {
                    var doc = TxtLog.Document;
                    if (doc != null)
                        TxtLog.CaretPosition = doc.ContentEnd;
                    TxtLog.ScrollToEnd();
                }
                catch
                {
                }
                _pinningEnd = false;
            });
        }

        private void RenderHighlighted(string text)
        {
            _rebuildingDoc = true;
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = Brushes.Transparent
            };
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var p = new Paragraph { Margin = new Thickness(0) };
                var run = new Run(line.Length == 0 ? " " : line);
                if (line.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    run.Foreground = ErrorBrush;
                    run.FontWeight = FontWeights.Normal;
                    p.Background = ErrorBgBrush;
                }
                else
                {
                    run.Foreground = NormalBrush;
                }
                p.Inlines.Add(run);
                doc.Blocks.Add(p);
            }
            TxtLog.Document = doc;
            _rebuildingDoc = false;
        }

        private string PlainTextFromDoc()
        {
            try
            {
                var range = new TextRange(TxtLog.Document.ContentStart, TxtLog.Document.ContentEnd);
                return range.Text ?? "";
            }
            catch
            {
                return _lastText;
            }
        }

        private IEnumerable<string> GameLogPaths()
        {
            if (string.IsNullOrWhiteSpace(_installRoot))
                yield break;
            yield return ShellWorkspaceService.GameLogPath(_installRoot);
            yield return Path.Combine(_installRoot, GamePrepareService.DebugLogFileName);
        }

        private static void Append(StringBuilder sb, string source, string path, bool errorsOnly = false, bool latestRun = false)
        {
            var body = Tail(path);
            if (latestRun)
                body = LogRunSlice.Latest(body, RunMarker(source));
            if (string.IsNullOrWhiteSpace(body))
                return;
            DateTime? fileTime = null;
            try { fileTime = File.GetLastWriteTime(path); } catch { }
            var inStack = false;
            foreach (var line in body.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length == 0)
                {
                    inStack = false;
                    continue;
                }
                var content = StripExistingBracketTime(trimmed);
                if (errorsOnly)
                {
                    if (IsSsf2ErrorLine(content))
                        inStack = true;
                    else if (inStack && IsStackContinuation(content))
                    { /* keep */ }
                    else
                    {
                        inStack = false;
                        continue;
                    }
                }
                sb.Append(StampForLine(trimmed, fileTime));
                sb.Append(" [").Append(source).Append("] ");
                sb.AppendLine(content);
            }
        }

        private static string RunMarker(string source)
        {
            if (source == "SSF2")
                return "[Bootstrap] loading engine1";
            if (source == "Mod Manager")
                return "ModManagerService initialized";
            return "[OnStartup]";
        }

        private static bool IsSsf2ErrorLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            if (line.Contains("UNCAUGHT", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("TypeError", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("ReferenceError", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("Error #", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("errorID=", StringComparison.OrdinalIgnoreCase))
                return true;
            // Highlight-style catch-all, but skip routine "no error" noise.
            if (line.Contains("Error", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("No error", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static bool IsStackContinuation(string line)
        {
            if (StackFrameLine.IsMatch(line))
                return true;
            if (line.StartsWith("TypeError", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("UNCAUGHT stack", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("(stack unavailable", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static string StampNow() => DateTime.Now.ToString("HH:mm:ss");

        private static string StampForLine(string line, DateTime? fallback)
        {
            if (HasLeadingTimestamp.IsMatch(line))
                return (fallback ?? DateTime.Now).ToString("HH:mm:ss");
            var m = BracketTime.Match(line);
            if (m.Success)
                return m.Groups[1].Value;
            return (fallback ?? DateTime.Now).ToString("HH:mm:ss");
        }

        private static string StripExistingBracketTime(string line)
        {
            return BracketTime.Replace(line, "", 1);
        }

        private static string Tail(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return "";
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                const int max = 240_000;
                if (stream.Length > max)
                    stream.Seek(-max, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                if (stream.Length > max)
                {
                    var nl = text.IndexOf('\n');
                    if (nl >= 0)
                        text = text[(nl + 1)..];
                }
                return text;
            }
            catch
            {
                return "";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
