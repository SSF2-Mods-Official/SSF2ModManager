using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class AltEditWindow : Window
    {
        private readonly string _shell;
        private readonly CharacterAltsService.AltEntry _entry;
        private DispatcherTimer? _pickTimer;
        private long _lastPickAt;

        public AltEditWindow(string shellRoot, CharacterAltsService.AltEntry entry)
        {
            InitializeComponent();
            _shell = shellRoot;
            _entry = entry;
            TxtCharacter.Text = "Alt of " + entry.CharacterId;
            TxtName.Text = entry.Name ?? "";
            if (string.Equals(entry.Scope, "costume", StringComparison.OrdinalIgnoreCase))
            {
                RadCostume.IsChecked = true;
                TxtCostume.Text = (entry.Costume ?? -1).ToString();
            }
            else
            {
                RadAll.IsChecked = true;
            }
            Scope_Changed(this, new RoutedEventArgs());
        }

        private void Scope_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelCostume == null) return;
            PanelCostume.Visibility = RadCostume?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            var character = _entry.CharacterId;
            try
            {
                var pickPaths = new[]
                {
                    Path.Combine(ShellWorkspaceService.StateDirectory(_shell), "alt-costume-pick.json"),
                    Path.Combine(ShellWorkspaceService.UserStateDirectory(_shell), "alt-costume-pick.json")
                };
                foreach (var p in pickPaths)
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                _lastPickAt = 0;
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenCss,
                    new Dictionary<string, object?>
                    {
                        ["altCostumePick"] = true,
                        ["altCostumePickChar"] = character,
                        ["match"] = new Dictionary<string, object?>
                        {
                            ["players"] = new object[]
                            {
                                new Dictionary<string, object?> { ["character"] = character }
                            }
                        }
                    });
                TxtPickStatus.Text = "Costume grid open — click costumes; the index updates each time. Save when ready.";
                _pickTimer?.Stop();
                _pickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                var started = DateTime.UtcNow;
                _pickTimer.Tick += (_, _) =>
                {
                    if (DateTime.UtcNow - started > TimeSpan.FromMinutes(15))
                    {
                        _pickTimer.Stop();
                        TxtPickStatus.Text = "Timed out. Type the costume index (−1 = base) yourself.";
                        return;
                    }
                    var pickPath = pickPaths.FirstOrDefault(File.Exists);
                    if (pickPath == null) return;
                    try
                    {
                        var jo = JObject.Parse(File.ReadAllText(pickPath));
                        if (jo["costume"] == null) return;
                        var at = jo["at"]?.Value<long>() ?? 0;
                        if (at != 0 && at == _lastPickAt) return;
                        _lastPickAt = at;
                        TxtCostume.Text = jo["costume"]!.ToString();
                        TxtPickStatus.Text = "Costume " + TxtCostume.Text + " (keep clicking to change; then Save).";
                    }
                    catch { }
                };
                _pickTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pick costume", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var scope = RadCostume?.IsChecked == true ? "costume" : "all";
            int? costume = null;
            if (scope == "costume")
            {
                if (!int.TryParse(TxtCostume.Text.Trim(), out var c))
                {
                    MessageBox.Show("Costume index must be an integer (−1 = base).", "Edit alt",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                costume = c;
            }
            try
            {
                CharacterAltsService.Update(_shell, _entry.Folder, TxtName.Text, scope, costume);
                _pickTimer?.Stop();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Edit alt", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _pickTimer?.Stop();
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _pickTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
