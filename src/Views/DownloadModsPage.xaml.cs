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
        /// <summary>Every SSF2 mod, newest first. Categories without their own id filter this feed by root category.</summary>
        private readonly CategoryFeed _gameFeed = new("all", "All", "Mod", 0);
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
            BtnMore.IsEnabled = !_loading && HasMoreForFilter();
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
                foreach (var feed in FeedsForFilter())
                {
                    if (feed.More)
                        await PullAsync(feed);
                }
                ApplyFilter();
                BtnMore.IsEnabled = HasMoreForFilter();
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
            var fromGame = feed == _gameFeed;
            var (mods, more, _) = fromGame
                ? await _api.SearchModsAsync("", feed.Page, 50, "new")
                : await _api.SearchCategoryAsync(feed.CategoryId, feed.Page, 40);
            feed.More = more;
            foreach (var mod in mods)
            {
                var owner = fromGame ? FeedFor(BrowseModFilter.KindFor(mod.RootCategory?.Name)) ?? FeedFor("mod")! : feed;
                var label = fromGame && !string.IsNullOrWhiteSpace(mod.RootCategory?.Name) ? mod.RootCategory!.Name : owner.Label;
                var existing = _rows.FirstOrDefault(r => r.Mod.Id == mod.Id);
                if (existing != null)
                {
                    if (!fromGame)
                    {
                        existing.Kind = owner.Kind;
                        existing.CategoryLabel = owner.Label;
                        existing.CategoryName = owner.CategoryName;
                    }
                    continue;
                }
                _rows.Add(new BrowseRow
                {
                    Mod = mod,
                    Name = string.IsNullOrWhiteSpace(mod.Name) ? "Mod " + mod.Id : mod.Name,
                    CategoryLabel = label,
                    CategoryName = owner.CategoryName,
                    Kind = owner.Kind,
                    NineBOnly = BrowseModFilter.IsNineBOnly(mod)
                });
            }
        }

        private void ApplyFilter()
        {
            var filter = SelectedFilter();
            var q = (TxtSearch.Text ?? "").Trim();
            var showNineB = ChkNineB.IsChecked == true;
            var matching = _rows.Where(r =>
                (filter == "all" || r.Kind == filter)
                && (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            var shown = matching.Where(r => showNineB || !r.NineBOnly).ToList();
            var hidden = matching.Count - shown.Count;
            LstMods.ItemsSource = shown;
            TxtStatus.Text = shown.Count + " mod(s)"
                + (hidden > 0 ? $", {hidden} 9B mod(s) hidden" : "")
                + (HasMoreForFilter() ? "" : " — end of the list");
            DebugLogger.Log($"[browse] shown={shown.Count} hidden9b={hidden} rows={_rows.Count}");
        }

        /// <summary>All reads the game feed plus every category with its own id; a category without one reads the game feed.</summary>
        private IEnumerable<CategoryFeed> FeedsForFilter()
        {
            var filter = SelectedFilter();
            if (filter == "all")
                return new[] { _gameFeed }.Concat(_feeds.Where(f => f.CategoryId > 0));
            var feed = FeedFor(filter);
            return new[] { feed != null && feed.CategoryId > 0 ? feed : _gameFeed };
        }

        private string SelectedFilter()
        {
            var text = CmbCategory.SelectedItem as string ?? "All";
            var feed = _feeds.FirstOrDefault(f => f.Label.Equals(text, StringComparison.OrdinalIgnoreCase));
            return feed?.Kind ?? "all";
        }

        private CategoryFeed? FeedFor(string kind) => _feeds.FirstOrDefault(f => f.Kind == kind);

        private int VisibleCount() => LstMods.Items.Count;

        private bool HasMoreForFilter() => FeedsForFilter().Any(f => f.More);

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
            public bool NineBOnly { get; set; }
        }
    }
}
