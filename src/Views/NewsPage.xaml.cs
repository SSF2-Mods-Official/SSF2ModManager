using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class NewsPage : System.Windows.Controls.UserControl
    {
        private List<NewsArticle> _articles = new();
        private string _lastPath = "";
        private SettingsService? _settings;

        public event Action? ArticlesChanged;

        public NewsPage()
        {
            InitializeComponent();
        }

        public void BindSettings(SettingsService settings) => _settings = settings;

        public void LoadLocal(string newsFolderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newsFolderPath)) return;
                _lastPath = newsFolderPath;
                _articles = NewsService.LoadLocalArticles(newsFolderPath);
                DisplayArticles();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load news: {ex.Message}");
            }
        }

        public async Task RefreshAsync(bool forceSync = false)
        {
            if (_settings == null)
            {
                LoadLocal(string.IsNullOrWhiteSpace(_lastPath) ? AppPaths.NewsFolder : _lastPath);
                return;
            }

            try
            {
                TxtNewsStatus.Text = forceSync ? "Syncing news from GitHub..." : "Checking for news updates...";
                var sync = await NewsSyncService.SyncAsync(_settings, forceSync);
                UpdateStatusText(sync);
                if (!sync.Success && forceSync)
                {
                    MessageBox.Show(
                        $"Could not sync news:\n{sync.ErrorMessage ?? "Unknown error"}\n\nShowing cached and bundled articles.",
                        "News Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                _articles = NewsService.LoadMergedArticles();
                DisplayArticles();
                ArticlesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to refresh news: {ex.Message}");
                if (forceSync)
                    MessageBox.Show($"Failed to refresh news:\n{ex.Message}", "News Sync",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public IReadOnlyList<NewsArticle> GetArticles() => _articles;

        private void UpdateStatusText(NewsSyncResult sync)
        {
            if (!sync.Success)
            {
                var err = sync.ErrorMessage ?? _settings?.GetLastNewsSyncError() ?? "offline";
                TxtNewsStatus.Text = $"Offline — could not sync ({err}). Showing cached articles.";
                return;
            }

            var last = _settings?.GetLastNewsSyncUtc();
            if (sync.DownloadedCount > 0)
                TxtNewsStatus.Text = $"Synced {sync.DownloadedCount} article(s)" + (last.HasValue ? $" · {last.Value.ToLocalTime():g}" : "");
            else if (last.HasValue)
                TxtNewsStatus.Text = $"Up to date · Last sync {last.Value.ToLocalTime():g}";
            else
                TxtNewsStatus.Text = "Up to date";
        }

        private void DisplayArticles()
        {
            LstArticles.ItemsSource = null;
            LstArticles.ItemsSource = _articles;
            if (_articles.Count > 0)
                LstArticles.SelectedIndex = 0;
            else
            {
                var fdEmpty = new FlowDocument();
                fdEmpty.Blocks.Add(new Paragraph(new Run("No news articles found.")));
                DocViewer.Document = fdEmpty;
            }
        }

        private void ShowError(string message)
        {
            var fdErr = new FlowDocument();
            fdErr.Blocks.Add(new Paragraph(new Run(message)));
            DocViewer.Document = fdErr;
            TxtNewsStatus.Text = message;
        }

        private void LstArticles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstArticles.SelectedItem is NewsArticle a)
            {
                if (_settings != null && !string.IsNullOrWhiteSpace(a.Id))
                {
                    _settings.MarkNewsArticleRead(a.Id);
                    ArticlesChanged?.Invoke();
                }

                try
                {
                    try
                    {
                        var md = a.RawMarkdown ?? a.Html ?? "";
                        var doc = ConvertMarkdownToFlowDocument(md, a.SourceFolder);
                        var parentBorder = DocViewer.Parent as System.Windows.Controls.Border;
                        double avail = parentBorder?.ActualWidth ?? DocViewer.ActualWidth;
                        if (avail > 0)
                        {
                            var width = Math.Max(320, avail - (parentBorder?.Padding.Left ?? 12) - (parentBorder?.Padding.Right ?? 12) - 24);
                            doc.PageWidth = width;
                            doc.ColumnWidth = width;
                            doc.PagePadding = new Thickness(12);
                            DocViewer.ClipToBounds = true;
                        }
                        else
                        {
                            DocViewer.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var pb = DocViewer.Parent as System.Windows.Controls.Border;
                                var w = pb?.ActualWidth ?? DocViewer.ActualWidth;
                                if (w > 0)
                                {
                                    var width = Math.Max(320, w - (pb?.Padding.Left ?? 12) - (pb?.Padding.Right ?? 12) - 24);
                                    doc.PageWidth = width;
                                    doc.ColumnWidth = width;
                                    doc.PagePadding = new Thickness(12);
                                    DocViewer.ClipToBounds = true;
                                }
                            }), System.Windows.Threading.DispatcherPriority.Loaded);
                        }

                        var bgBrush = System.Windows.Application.Current.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush;
                        var fgBrush = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush;
                        if (bgBrush != null) DocViewer.Background = bgBrush;
                        if (fgBrush != null) DocViewer.Foreground = fgBrush;
                        DocViewer.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
                        DocViewer.Document = doc;
                    }
                    catch { }
                }
                catch { }
            }
        }

        private FlowDocument ConvertMarkdownToFlowDocument(string markdown, string baseFolder)
        {
            var fd = new FlowDocument();
            fd.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            fd.FontSize = 14;
            fd.PagePadding = new Thickness(12);

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var md = Markdig.Markdown.Parse(markdown ?? "", pipeline);

            foreach (var block in md)
            {
                if (block is HeadingBlock hb)
                {
                    var p = new Paragraph();
                    var size = hb.Level switch { 1 => 22.0, 2 => 18.0, 3 => 16.0, _ => 14.0 };
                    p.FontSize = size;
                    p.FontWeight = FontWeights.SemiBold;
                    AddInlineChildren(p.Inlines, hb.Inline);
                    fd.Blocks.Add(p);
                }
                else if (block is ThematicBreakBlock)
                {
                    var hr = new BlockUIContainer();
                    var border = new System.Windows.Controls.Border
                    {
                        Height = 1,
                        Margin = new Thickness(0, 8, 0, 8),
                        Background = System.Windows.Application.Current.TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray
                    };
                    hr.Child = border;
                    fd.Blocks.Add(hr);
                }
                else if (block is Markdig.Syntax.LinkReferenceDefinitionGroup) { }
                else if (block is ParagraphBlock pb)
                {
                    var p = new Paragraph();
                    AddInlineChildren(p.Inlines, pb.Inline);
                    fd.Blocks.Add(p);
                }
                else if (block is ListBlock lb)
                {
                    var list = new List();
                    list.MarkerStyle = lb.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
                    foreach (ListItemBlock item in lb)
                    {
                        var li = new ListItem();
                        foreach (var sub in item)
                        {
                            if (sub is ParagraphBlock sp)
                            {
                                var p = new Paragraph();
                                AddInlineChildren(p.Inlines, sp.Inline);
                                li.Blocks.Add(p);
                            }
                        }
                        list.ListItems.Add(li);
                    }
                    fd.Blocks.Add(list);
                }
                else if (block is QuoteBlock qb)
                {
                    var sec = new Section();
                    sec.BorderThickness = new Thickness(2,0,0,0);
                    sec.BorderBrush = System.Windows.Application.Current.TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
                    sec.Padding = new Thickness(10,0,0,0);
                    foreach (var sub in qb)
                    {
                        if (sub is ParagraphBlock sp)
                        {
                            var p = new Paragraph();
                            p.FontStyle = FontStyles.Italic;
                            AddInlineChildren(p.Inlines, sp.Inline);
                            sec.Blocks.Add(p);
                        }
                    }
                    fd.Blocks.Add(sec);
                }
                else if (block is FencedCodeBlock cb)
                {
                    var p = new Paragraph();
                    p.FontFamily = new System.Windows.Media.FontFamily("Consolas");
                    p.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0, 0, 0));
                    p.Inlines.Add(new Run(string.Join('\n', cb.Lines)));
                    fd.Blocks.Add(p);
                }
                else if (block is Markdig.Syntax.LeafBlock leaf && leaf.Inline != null)
                {
                    var p = new Paragraph();
                    AddInlineChildren(p.Inlines, leaf.Inline);
                    fd.Blocks.Add(p);
                }
            }

            return fd;
        }

        private void AddInlineChildren(InlineCollection inlines, Markdig.Syntax.Inlines.ContainerInline? container)
        {
            if (container == null) return;
            foreach (var inline in container)
            {
                if (inline is LiteralInline li)
                    inlines.Add(new Run(li.Content.ToString()));
                else if (inline is CodeInline ci)
                {
                    var run = new Run(ci.Content) { FontFamily = new System.Windows.Media.FontFamily("Consolas") };
                    var span = new Span(run)
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x30, 0x30, 0x30))
                    };
                    inlines.Add(span);
                }
                else if (inline is AutolinkInline al)
                {
                    var hl = new Hyperlink(new Run(al.Url ?? al.ToString()));
                    if (!string.IsNullOrEmpty(al.Url))
                    {
                        try { hl.NavigateUri = new Uri(al.Url); } catch { }
                    }
                    inlines.Add(hl);
                }
                else if (inline is EmphasisInline ei)
                {
                    var span = new Span();
                    if (ei.DelimiterChar == '*') span.FontWeight = FontWeights.Bold;
                    if (ei.DelimiterChar == '_') span.FontStyle = FontStyles.Italic;
                    AddInlineChildren(span.Inlines, ei);
                    inlines.Add(span);
                }
                else if (inline is LineBreakInline)
                    inlines.Add(new LineBreak());
                else if (inline is LinkInline link)
                {
                    if (link.IsImage)
                    {
                        var url = link.Url ?? string.Empty;
                        var imgPath = ResolvePath(url, null);
                        try
                        {
                            var bi = new BitmapImage(new Uri(imgPath));
                            var img = new System.Windows.Controls.Image { Source = bi, MaxWidth = 640 };
                            inlines.Add(new InlineUIContainer(img));
                        }
                        catch { }
                    }
                    else
                    {
                        var hl = new Hyperlink();
                        hl.NavigateUri = !string.IsNullOrEmpty(link.Url) ? new Uri(link.Url, UriKind.RelativeOrAbsolute) : null;
                        AddInlineChildren(hl.Inlines, link);
                        inlines.Add(hl);
                    }
                }
                else
                    inlines.Add(new Run(inline.ToString()));
            }
        }

        private string ResolvePath(string url, string? baseFolder)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (Uri.IsWellFormedUriString(url, UriKind.Absolute)) return url;
            try
            {
                var folder = baseFolder ?? _lastPath ?? "";
                if (string.IsNullOrWhiteSpace(folder)) return url;
                var path = Path.Combine(folder, url.Replace('/', Path.DirectorySeparatorChar));
                return new Uri(path).AbsoluteUri;
            }
            catch { return url; }
        }

        private async void BtnRefreshNews_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            BtnRefreshNews.IsEnabled = false;
            try
            {
                await RefreshAsync(forceSync: true);
            }
            finally
            {
                BtnRefreshNews.IsEnabled = true;
            }
        }

        private void BtnMarkAllRead_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null || _articles.Count == 0) return;
            _settings.MarkAllNewsRead(_articles.Select(a => a.Id));
            ArticlesChanged?.Invoke();
            MessageBox.Show("All news articles marked as read.", "News",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
