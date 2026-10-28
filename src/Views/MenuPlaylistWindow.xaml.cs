using System.Collections.ObjectModel;
using System.Windows;
using SSF2ModManager.Services;

namespace SSF2ModManager.Views
{
    public partial class MenuPlaylistWindow : Window
    {
        private readonly string _installRoot;
        private readonly ObservableCollection<string> _songs = new();
        private bool _enabled = true;

        public MenuPlaylistWindow(string installRoot, IReadOnlyList<string> catalog)
        {
            _installRoot = installRoot;
            InitializeComponent();
            CmbTracks.ItemsSource = catalog;
            if (catalog.Count > 0)
                CmbTracks.SelectedIndex = 0;
            foreach (var id in ShellConfigureLaunchService.ReadMenuPlaylist(installRoot))
                _songs.Add(id);
            _enabled = ShellConfigureLaunchService.ReadMenuPlaylistEnabled(installRoot);
            if (_songs.Count == 0)
                _enabled = false;
            LstSongs.ItemsSource = _songs;
            RefreshEnabledButton();
        }

        public List<string> Playlist => _songs.ToList();

        public bool PlaylistEnabled => _enabled;

        private void RefreshEnabledButton()
        {
            BtnToggleEnabled.Content = _enabled ? "Disable" : "Enable";
            TxtStatus.Text = _enabled
                ? (_songs.Count == 0 ? "Playlist enabled (add tracks)." : "Playlist enabled.")
                : "Playlist disabled — stock menu music plays; tracks are kept.";
        }

        private void BtnToggleEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (!_enabled && _songs.Count == 0)
            {
                TxtStatus.Text = "Add at least one track before enabling.";
                return;
            }
            _enabled = !_enabled;
            RefreshEnabledButton();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var id = CmbTracks.SelectedItem as string ?? CmbTracks.Text.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                TxtStatus.Text = "Pick a track first.";
                return;
            }
            if (_songs.Any(s => s.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                TxtStatus.Text = id + " is already in the playlist.";
                return;
            }
            _songs.Add(id);
            LstSongs.SelectedItem = id;
            if (!_enabled)
            {
                _enabled = true;
                RefreshEnabledButton();
            }
            TxtStatus.Text = "Added " + id + ".";
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (LstSongs.SelectedItem is not string id)
            {
                TxtStatus.Text = "Select a song to remove.";
                return;
            }
            _songs.Remove(id);
            if (_songs.Count == 0)
            {
                _enabled = false;
                RefreshEnabledButton();
            }
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            var id = LstSongs.SelectedItem as string ?? CmbTracks.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
            {
                TxtStatus.Text = "Select a song to preview.";
                return;
            }
            try
            {
                TxtStatus.Text = BgmPreviewService.PlayLocal(_installRoot, id);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            BgmPreviewService.Stop();
            TxtStatus.Text = "Stopped.";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            BgmPreviewService.Stop();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            BgmPreviewService.Stop();
            DialogResult = false;
        }
    }
}
