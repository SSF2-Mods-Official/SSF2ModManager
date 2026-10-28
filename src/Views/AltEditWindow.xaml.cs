using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SSF2ModManager.Views
{
    public partial class AltEditWindow : Window
    {
        private readonly string _shell;
        private readonly CharacterAltsService.AltEntry _entry;
        private DispatcherTimer? _pickTimer;
        private long _lastPickAt;
        private MediaPlayer? _player;
        private MediaPlayer? _playerB;
        private List<AltSoundEditService.SoundItem> _sounds = new();

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
            Loaded += (_, _) =>
            {
                try
                {
                    CharacterAltsService.MigrateToSoundsMode(_shell, _entry.Folder);
                    ReloadSounds();
                }
                catch (Exception ex)
                {
                    TxtSoundStatus.Text = "Could not load sounds: " + ex.Message;
                }
            };
            Closed += (_, _) => StopPlayers();
        }

        private void Scope_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelCostume == null) return;
            PanelCostume.Visibility = RadCostume?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ReloadSounds()
        {
            _sounds = AltSoundEditService.ListSounds(_entry.Folder);
            var selected = (CmbSounds.SelectedItem as AltSoundEditService.SoundItem)?.RelativePath;
            CmbSounds.ItemsSource = null;
            CmbSounds.ItemsSource = _sounds;
            TxtReplacedCount.Text = "Replaced: " + AltSoundEditService.ReplacedCount(_entry.Folder);
            if (_sounds.Count == 0)
            {
                TxtSoundStatus.Text = "No sounds folder — reinstall from Make Mods to export editable sounds.";
                UpdateCompare(null);
                return;
            }
            TxtSoundStatus.Text = _sounds.Count + " sound file(s). Replaced files show *.";
            var pick = _sounds.FirstOrDefault(s =>
                string.Equals(s.RelativePath, selected, StringComparison.OrdinalIgnoreCase)) ?? _sounds[0];
            CmbSounds.SelectedItem = pick;
            UpdateCompare(pick);
        }

        private AltSoundEditService.SoundItem? SelectedSound() =>
            CmbSounds.SelectedItem as AltSoundEditService.SoundItem;

        private void CmbSounds_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateCompare(SelectedSound());

        private async void UpdateCompare(AltSoundEditService.SoundItem? item)
        {
            if (item == null)
            {
                TxtOrigInfo.Text = "—";
                TxtCurrInfo.Text = "—";
                return;
            }
            TxtCurrInfo.Text = FormatInfo(item.FullPath, item.CurrentBytes, null);
            if (item.OriginalPath != null && File.Exists(item.OriginalPath))
                TxtOrigInfo.Text = FormatInfo(item.OriginalPath, item.OriginalBytes ?? 0, null);
            else
                TxtOrigInfo.Text = item.Replaced ? "(original missing)" : "(same as current until replaced)";

            try
            {
                var currPlay = await AltSoundEditService.EnsurePlayableAsync(item.FullPath);
                var currDur = AltSoundEditService.TryReadDurationSeconds(currPlay);
                TxtCurrInfo.Text = FormatInfo(item.FullPath, item.CurrentBytes, currDur);
                if (item.OriginalPath != null && File.Exists(item.OriginalPath))
                {
                    var origPlay = await AltSoundEditService.EnsurePlayableAsync(item.OriginalPath);
                    var origDur = AltSoundEditService.TryReadDurationSeconds(origPlay);
                    TxtOrigInfo.Text = FormatInfo(item.OriginalPath, item.OriginalBytes ?? 0, origDur);
                }
            }
            catch { }
        }

        private static string FormatInfo(string path, long bytes, double? seconds)
        {
            var size = bytes >= 1024 * 1024
                ? (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MB"
                : (bytes / 1024.0).ToString("0.0") + " KB";
            var dur = seconds.HasValue ? seconds.Value.ToString("0.00") + " s" : "duration ?";
            return Path.GetFileName(path) + "\n" + size + " · " + dur;
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
                TxtPickStatus.Text = "Costume grid open — click costumes; Name fills from the costume profile when available.";
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
                        var pickName = ((string?)jo["name"] ?? "").Trim();
                        if (pickName.Length > 0)
                            TxtName.Text = pickName;
                        TxtPickStatus.Text = "Costume " + TxtCostume.Text
                            + (pickName.Length > 0 ? " (" + pickName + ")" : "")
                            + " — keep clicking to change; then Save.";
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

        private async void BtnPreview_Click(object sender, RoutedEventArgs e) =>
            await PlayPathAsync(SelectedSound()?.FullPath, useB: false);

        private async void BtnPlayOrig_Click(object sender, RoutedEventArgs e)
        {
            var s = SelectedSound();
            await PlayPathAsync(s?.OriginalPath ?? s?.FullPath, useB: false);
        }

        private async void BtnPlayCurr_Click(object sender, RoutedEventArgs e) =>
            await PlayPathAsync(SelectedSound()?.FullPath, useB: false);

        private async void BtnPlayBoth_Click(object sender, RoutedEventArgs e)
        {
            var s = SelectedSound();
            if (s == null) return;
            var orig = s.OriginalPath ?? s.FullPath;
            await PlayPathAsync(orig, useB: false);
            await System.Threading.Tasks.Task.Delay(80);
            await PlayPathAsync(s.FullPath, useB: true);
        }

        private async System.Threading.Tasks.Task PlayPathAsync(string? path, bool useB)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                TxtSoundStatus.Text = "Nothing to play.";
                return;
            }
            try
            {
                var playable = await AltSoundEditService.EnsurePlayableAsync(path);
                if (playable == null)
                {
                    TxtSoundStatus.Text = "Could not decode " + Path.GetFileName(path) + " for preview.";
                    return;
                }
                if (useB)
                {
                    _playerB ??= new MediaPlayer();
                    _playerB.Stop();
                    _playerB.Open(new Uri(playable));
                    _playerB.Play();
                }
                else
                {
                    _player ??= new MediaPlayer();
                    _player.Stop();
                    _player.Open(new Uri(playable));
                    _player.Play();
                }
                TxtSoundStatus.Text = "Playing " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                TxtSoundStatus.Text = ex.Message;
            }
        }

        private async void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            var s = SelectedSound();
            if (s == null)
            {
                MessageBox.Show("Pick a sound first.", "Replace", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new OpenFileDialog
            {
                Title = "Replace " + s.FileName,
                Filter = "Audio|*.wav;*.mp3;*.flv;*.ogg|All files|*.*"
            };
            if (dlg.ShowDialog(this) != true)
                return;
            try
            {
                StopPlayers();
                TxtSoundStatus.Text = "Replacing (matching volume to original)…";
                await AltSoundEditService.ReplaceAsync(_entry.Folder, s.FileName, dlg.FileName, _entry.CharacterId);
                ReloadSounds();
                SelectSoundNamed(s.FileName);
                TxtSoundStatus.Text = "Replaced " + s.FileName + " (volume matched; sounds folder only).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Replace", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSoundStatus.Text = ex.Message;
            }
        }

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            var s = SelectedSound();
            if (s == null || !s.Replaced)
            {
                MessageBox.Show("Select a replaced sound (*).", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                StopPlayers();
                TxtSoundStatus.Text = "Restoring…";
                await AltSoundEditService.RestoreAsync(_entry.Folder, s.FileName, _entry.CharacterId);
                ReloadSounds();
                SelectSoundNamed(s.FileName);
                TxtSoundStatus.Text = "Restored " + s.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSoundStatus.Text = ex.Message;
            }
        }

        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            var n = AltSoundEditService.ReplacedCount(_entry.Folder);
            if (n == 0)
            {
                MessageBox.Show("No replaced sounds.", "Restore all", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show("Restore all " + n + " replaced sound(s)?", "Restore all",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                StopPlayers();
                TxtSoundStatus.Text = "Restoring all…";
                await AltSoundEditService.RestoreAllAsync(_entry.Folder, _entry.CharacterId);
                ReloadSounds();
                TxtSoundStatus.Text = "Restored all sounds.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Restore all", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSoundStatus.Text = ex.Message;
            }
        }

        private void SelectSoundNamed(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || _sounds.Count == 0) return;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var pick = _sounds.FirstOrDefault(x =>
                string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(x.FileName), stem, StringComparison.OrdinalIgnoreCase));
            if (pick != null)
                CmbSounds.SelectedItem = pick;
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
                if (scope == "costume")
                {
                    var warn = MessageBox.Show(
                        "This alt is tied to costume " + costume
                        + ".\n\nJump still cycles to it and selects that costume. Selecting the costume alone also applies the voice.\n\nSave?",
                        "Costume-tied alt",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    if (warn != MessageBoxResult.Yes)
                        return;
                }
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
            StopPlayers();
            base.OnClosed(e);
        }

        private void StopPlayers()
        {
            try { _player?.Stop(); } catch { }
            try { _playerB?.Stop(); } catch { }
            try { _player?.Close(); } catch { }
            try { _playerB?.Close(); } catch { }
            _player = null;
            _playerB = null;
            // Let MediaPlayer release file handles before overwrite.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
        }
    }
}
