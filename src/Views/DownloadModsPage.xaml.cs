using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SSF2ModManager.Models;
using Button = System.Windows.Controls.Button;
using SSF2ModManager.Services;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class DownloadModsPage : System.Windows.Controls.UserControl
    {
        public const int EventsCategory = 49482;
        public const int MusicCategory = 49485;
        /// <summary>Stand-in until the GameBanana category id is known. Zero skips the request.</summary>
        public const int PlaceholderCategory = 0;

        private readonly string _shell;
        private readonly GameBananaApiClient _api = new();
        private readonly ModManagerService _mods;
        private readonly List<BrowseRow> _rows = new();
        private readonly CategoryFeed[] _feeds =
        [
            new("event", "Events", "Events", EventsCategory),
            new("music", "Music", "Music", MusicCategory),
            new("tutorial", "Tutorials", "Tutorial", PlaceholderCategory),
            new("character", "Characters", "Character", PlaceholderCategory),
            new("stage", "Stages", "Stage", PlaceholderCategory),
            new("intro", "Intros", "Intro", PlaceholderCategory),
            new("mod", "General Mods", "Mod", PlaceholderCategory)
        ];
        private bool _loading;
        private bool _ready;

        public DownloadModsPage(string shell)
        {
            _shell = shell;
            _mods = new ModManagerService(_api);
            InitializeComponent();
            CmbCategory.Items.Add("All");
            foreach (var feed in _feeds)
                CmbCategory.Items.Add(feed.Label);
            CmbCategory.SelectedIndex = 0;
            _ready = true;
            Loaded += async (_, _) =>
            {
                if (_rows.Count == 0)
                    await LoadMoreAsync();
            };
        }

        private async void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready)
                return;
            ApplyFilter();
            if (VisibleCount() == 0 && HasMoreForFilter())
                await LoadMoreAsync();
        }

        private async void BtnMore_Click(object sender, RoutedEventArgs e) => await LoadMoreAsync();

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BrowseRow row || _loading)
                return;
            btn.IsEnabled = false;
            TxtStatus.Text = "Downloading " + row.Name + "…";
            try
            {
                var files = await _api.GetModFilesAsync(row.Mod.Id);
                var file = files.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                if (file == null || string.IsNullOrWhiteSpace(file.DownloadUrl))
                {
                    TxtStatus.Text = "No downloadable file for " + row.Name + ".";
                    return;
                }

                row.Mod.RootCategory ??= new GameBananaCategory();
                row.Mod.RootCategory.Name = row.CategoryName;
                var version = EnsureShellVersion();
                await _mods.InstallModAsync(row.Mod, file, version);
                TxtStatus.Text = "Installed " + row.Name + ".";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
                MessageBox.Show(ex.Message, "Download Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        private async Task LoadMoreAsync()
        {
            if (_loading)
                return;
            _loading = true;
            BtnMore.IsEnabled = false;
            TxtStatus.Text = "Loading…";
            try
            {
                var filter = SelectedFilter();
                var selected = FeedFor(filter);
                if (selected != null && selected.CategoryId <= 0)
                {
                    ApplyFilter();
                    TxtStatus.Text = "That category is listed, but its GameBanana id is not set yet.";
                    BtnMore.IsEnabled = false;
                    return;
                }
                foreach (var feed in _feeds)
                {
                    if (filter != "all" && filter != feed.Kind)
                        continue;
                    if (feed.CategoryId <= 0 || !feed.More)
                        continue;
                    await PullAsync(feed);
                }
                ApplyFilter();
                TxtStatus.Text = LstMods.Items.Count + " mod(s)";
                BtnMore.IsEnabled = HasMoreForFilter();
                if (!BtnMore.IsEnabled)
                    TxtStatus.Text += " — end of the list";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
            finally
            {
                _loading = false;
            }
        }

        private async Task PullAsync(CategoryFeed feed)
        {
            feed.Page++;
            var (mods, more, _) = await _api.SearchCategoryAsync(feed.CategoryId, feed.Page, 40);
            feed.More = more;
            foreach (var mod in mods)
            {
                if (_rows.Any(r => r.Mod.Id == mod.Id))
                    continue;
                _rows.Add(new BrowseRow
                {
                    Mod = mod,
                    Name = string.IsNullOrWhiteSpace(mod.Name) ? "Mod " + mod.Id : mod.Name,
                    CategoryLabel = feed.Label,
                    CategoryName = feed.CategoryName,
                    Kind = feed.Kind
                });
            }
        }

        private void ApplyFilter()
        {
            var filter = SelectedFilter();
            var q = (TxtSearch.Text ?? "").Trim();
            var shown = _rows.Where(r =>
                (filter == "all" || r.Kind == filter)
                && (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)));
            LstMods.ItemsSource = shown.ToList();
        }

        private string SelectedFilter()
        {
            var text = CmbCategory.SelectedItem as string ?? "All";
            var feed = _feeds.FirstOrDefault(f => f.Label.Equals(text, StringComparison.OrdinalIgnoreCase));
            return feed?.Kind ?? "all";
        }

        private CategoryFeed? FeedFor(string kind) => _feeds.FirstOrDefault(f => f.Kind == kind);

        private int VisibleCount() => LstMods.Items.Count;

        private bool HasMoreForFilter()
        {
            var filter = SelectedFilter();
            foreach (var feed in _feeds)
            {
                if (filter != "all" && filter != feed.Kind)
                    continue;
                if (feed.CategoryId > 0 && feed.More)
                    return true;
            }
            return false;
        }

        private string EnsureShellVersion()
        {
            var full = Path.GetFullPath(_shell);
            var match = _mods.Versions.FirstOrDefault(v =>
                !string.IsNullOrWhiteSpace(v.FolderPath)
                && string.Equals(Path.GetFullPath(v.FolderPath), full, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                var nick = "Launcher";
                var taken = _mods.GetVersionEntry(nick);
                if (taken != null
                    && !string.IsNullOrWhiteSpace(taken.FolderPath)
                    && !string.Equals(Path.GetFullPath(taken.FolderPath), full, StringComparison.OrdinalIgnoreCase))
                    nick = "Launcher-" + Guid.NewGuid().ToString("N")[..6];
                _mods.AddVersion(ShellWorkspaceService.VersionName, full, nick);
                match = _mods.Versions.LastOrDefault();
            }
            var id = match == null
                ? "Launcher"
                : (string.IsNullOrWhiteSpace(match.Nickname) ? match.VersionName : match.Nickname);
            _mods.ActiveVersion = id;
            return id;
        }

        private sealed class CategoryFeed
        {
            public CategoryFeed(string kind, string label, string categoryName, int categoryId)
            {
                Kind = kind;
                Label = label;
                CategoryName = categoryName;
                CategoryId = categoryId;
            }

            public string Kind { get; }
            public string Label { get; }
            public string CategoryName { get; }
            public int CategoryId { get; }
            public int Page { get; set; }
            public bool More { get; set; } = true;
        }

        private sealed class BrowseRow
        {
            public GameBananaMod Mod { get; set; } = new();
            public string Name { get; set; } = "";
            public string CategoryLabel { get; set; } = "";
            public string CategoryName { get; set; } = "";
            public string Kind { get; set; } = "";
        }
    }
}
