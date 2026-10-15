using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        /// <summary>Stand-in until the GameBanana category id is known. Zero reads the game feed instead.</summary>
        public const int PlaceholderCategory = 0;
        private const int ShowPerLoad = 200;

        private readonly GameBananaApiClient _api = new();
        private readonly List<BrowseRow> _rows = new();
        private readonly SemaphoreSlim _detailGate = new(8);
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
        /// <summary>Every SSF2 mod. Categories without their own id filter this feed by root category.</summary>
        private readonly CategoryFeed _gameFeed = new("all", "All", "Mod", 0);
        private bool _loading;
        private bool _ready;

        private readonly string _shell;

        public DownloadModsPage(string shell)
        {
            _shell = shell;
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
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible && _rows.Count > 0)
                    ApplyFilter();
            };
        }

        private async void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready)
                return;
            ApplyFilter();
            BtnMore.IsEnabled = !_loading && HasMoreForFilter();
            if (LstMods.Items.Count == 0 && HasMoreForFilter())
                await LoadMoreAsync();
        }

        private async void BtnMore_Click(object sender, RoutedEventArgs e) => await LoadMoreAsync();

        private void BtnPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: BrowseRow row } && !string.IsNullOrWhiteSpace(row.Mod.ProfileUrl))
                Process.Start(new ProcessStartInfo(row.Mod.ProfileUrl) { UseShellExecute = true });
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: BrowseRow row })
                return;
            if (Window.GetWindow(this) is not LauncherWindow launcher)
                return;
            try
            {
                var files = row.Mod.Files ?? await _api.GetModFilesAsync(row.Mod.Id);
                var file = files.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                if (file == null)
                {
                    MessageBox.Show(row.Name + " has no file to download on GameBanana.", "Download Mods",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                launcher.ReceiveOneClick($"ssf2mm:https://gamebanana.com/mmdl/{file.Id},Mod,{row.Mod.Id}", row.Name);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Download Mods file lookup failed", ex);
                MessageBox.Show("Could not reach GameBanana for " + row.Name + ".\n\n" + ex.Message, "Download Mods",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Reads pages until <see cref="ShowPerLoad"/> more mods are shown or the feeds end.</summary>
        private async Task LoadMoreAsync()
        {
            if (_loading)
                return;
            _loading = true;
            BtnMore.IsEnabled = false;
            var target = ShownCount() + ShowPerLoad;
            var pages = 0;
            try
            {
                while (ShownCount() < target && HasMoreForFilter())
                {
                    var added = new List<BrowseRow>();
                    foreach (var feed in FeedsForFilter())
                    {
                        if (feed.More)
                            added.AddRange(await PullAsync(feed));
                    }
                    pages++;
                    TxtStatus.Text = $"Loading… {ShownCount()} mod(s) so far, checking {added.Count} more";
                    await LoadDetailsAsync(added);
                    ApplyFilter();
                }
                DebugLogger.Log($"[browse] load pages={pages} shown={ShownCount()} more={(HasMoreForFilter() ? 1 : 0)}");
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Could not load mods from GameBanana: " + ex.Message;
            }
            finally
            {
                _loading = false;
                BtnMore.IsEnabled = HasMoreForFilter();
            }
        }

        private int ShownCount() => _rows.Count(r => r.Visible(SelectedFilter(), "", ChkNineB.IsChecked == true));

        private async Task<List<BrowseRow>> PullAsync(CategoryFeed feed)
        {
            feed.Page++;
            var fromGame = feed == _gameFeed;
            var (mods, more, _) = fromGame
                ? await _api.BrowseIndexAsync(feed.Page)
                : await _api.SearchCategoryAsync(feed.CategoryId, feed.Page, 50);
            feed.More = more;
            var added = new List<BrowseRow>();
            foreach (var mod in mods)
            {
                var owner = fromGame ? FeedFor(BrowseModFilter.KindFor(mod.RootCategory?.Name)) ?? FeedFor("mod")! : feed;
                var existing = _rows.FirstOrDefault(r => r.Mod.Id == mod.Id);
                if (existing != null)
                {
                    if (!fromGame)
                    {
                        existing.Kind = owner.Kind;
                        existing.CategoryName = owner.CategoryName;
                    }
                    continue;
                }
                var row = new BrowseRow
                {
                    Mod = mod,
                    CategoryName = owner.CategoryName,
                    Kind = owner.Kind,
                    CategoryLabel = string.IsNullOrWhiteSpace(mod.RootCategory?.Name) ? owner.Label : mod.RootCategory!.Name
                };
                _rows.Add(row);
                added.Add(row);
            }
            return added;
        }

        /// <summary>The list feed has no description; the 9B check and the card text need it.</summary>
        private async Task LoadDetailsAsync(List<BrowseRow> rows)
        {
            var failed = 0;
            await Task.WhenAll(rows.Select(async row =>
            {
                await _detailGate.WaitAsync();
                try
                {
                    var details = await _api.GetModDetailsAsync(row.Mod.Id);
                    if (details == null)
                        return;
                    row.Mod.FullText = details.FullText;
                    row.Mod.Downloads = details.Downloads;
                    row.Mod.Files = details.Files;
                    row.Refresh();
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    row.Checked = true;
                    _detailGate.Release();
                }
            }));
            DebugLogger.Log($"[browse] details mods={rows.Count} failed={failed}");
        }

        private void ApplyFilter()
        {
            var filter = SelectedFilter();
            var q = (TxtSearch.Text ?? "").Trim();
            var showNineB = ChkNineB.IsChecked == true;
            var shown = _rows.Where(r => r.Visible(filter, q, showNineB)).ToList();
            var hidden = _rows.Count(r => r.Visible(filter, q, true)) - shown.Count;
            var installed = GameBananaInstalls.InstalledModIds(_shell);
            foreach (var row in _rows)
                row.Installed = installed.Contains(row.Mod.Id);
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

        private bool HasMoreForFilter() => FeedsForFilter().Any(f => f.More);

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

        private sealed class BrowseRow : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public GameBananaMod Mod { get; set; } = new();
            public string CategoryLabel { get; set; } = "";
            public string CategoryName { get; set; } = "";
            public string Kind { get; set; } = "";

            public string Name => string.IsNullOrWhiteSpace(Mod.Name) ? "Mod " + Mod.Id : Mod.Name;
            private bool _installed;
            /// <summary>Recorded in the loaded build's gamebanana-installs.json and its items still exist.</summary>
            public bool Installed
            {
                get => _installed;
                set
                {
                    if (_installed == value) return;
                    _installed = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstalledVisibility)));
                }
            }
            public Visibility InstalledVisibility => Installed ? Visibility.Visible : Visibility.Collapsed;
            /// <summary>Details loaded or failed; until then the description has not been checked for 9B.</summary>
            public bool Checked { get; set; }
            public bool NineB => BrowseModFilter.MentionsNineB(Mod);

            public bool Visible(string filter, string query, bool showNineB) =>
                Checked
                && (filter == "all" || Kind == filter)
                && (query.Length == 0
                    || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || Byline.Contains(query, StringComparison.OrdinalIgnoreCase))
                && (showNineB || !NineB);
            public string? PreviewUrl => Mod.PreviewMedia?.Images?.FirstOrDefault()?.ThumbnailUrl;

            public string Byline
            {
                get
                {
                    var parts = new List<string> { CategoryLabel };
                    if (!string.IsNullOrWhiteSpace(Mod.Submitter?.Name))
                        parts.Add("by " + Mod.Submitter!.Name);
                    if (!string.IsNullOrWhiteSpace(Mod.Version))
                        parts.Add("version " + Mod.Version);
                    return string.Join(" · ", parts);
                }
            }

            public string Stats
            {
                get
                {
                    var parts = new List<string> { Count(Mod.Likes) + " likes", Count(Mod.Views) + " views" };
                    if (Mod.Downloads > 0)
                        parts.Add(Count(Mod.Downloads) + " downloads");
                    var ts = Mod.DateModifiedTimestamp > 0 ? Mod.DateModifiedTimestamp : Mod.DateAddedTimestamp;
                    if (ts > 0)
                        parts.Add("updated " + DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToString("MMM d, yyyy"));
                    return string.Join(" · ", parts);
                }
            }

            public string Snippet
            {
                get
                {
                    var text = BrowseModFilter.PlainText(Mod.FullText);
                    return text.Length > 220 ? text[..220] + "…" : text;
                }
            }

            public string FileSummary
            {
                get
                {
                    var file = Mod.Files?.OrderByDescending(f => f.DateAdded).FirstOrDefault();
                    return file == null ? "" : file.FileName + " · " + file.FileSizeFormatted;
                }
            }

            public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

            private static string Count(int n) =>
                n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#") + "M"
                : n >= 1_000 ? (n / 1_000.0).ToString("0.#") + "k"
                : n.ToString();
        }
    }
}
