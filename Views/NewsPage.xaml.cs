using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SSF2ModManager.Views
{
    public partial class NewsPage : System.Windows.Controls.UserControl
    {
        private List<NewsArticle> _articles = new();
        private string _lastPath = "";

        public NewsPage()
        {
            InitializeComponent();
        }

        public void LoadLocal(string newsFolderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newsFolderPath)) return;
                _lastPath = newsFolderPath;
                _articles = NewsService.LoadLocalArticles(newsFolderPath);
                LstArticles.ItemsSource = _articles;
                if (_articles.Count > 0)
                {
                    LstArticles.SelectedIndex = 0;
                }
                else
                {
                    // show empty message in FlowDocument
                    var fdEmpty = new FlowDocument();
                    fdEmpty.Blocks.Add(new Paragraph(new Run("No news articles found.")));
                    DocViewer.Document = fdEmpty;
                }
            }
            catch (Exception ex)
            {
                var fdErr = new FlowDocument();
                fdErr.Blocks.Add(new Paragraph(new Run($"Failed to load news: {ex.Message}")));
                DocViewer.Document = fdErr;
            }
        }

        private void LstArticles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstArticles.SelectedItem is NewsArticle a)
            {
                try
                {
                    // Render FlowDocument from raw markdown for native WPF viewing
                    try
                    {
                        var md = a.RawMarkdown ?? a.Html ?? "";
                        var doc = ConvertMarkdownToFlowDocument(md, a.SourceFolder);
                        // Constrain document width to viewer width so it doesn't overlap the left pane
                        // Prefer using the parent Border's width (accounts for padding/margins)
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

                        // Apply theme brushes
                        var bgBrush = System.Windows.Application.Current.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush;
                        var fgBrush = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush;
                        if (bgBrush != null) DocViewer.Background = bgBrush;
                        if (fgBrush != null) DocViewer.Foreground = fgBrush;
                        DocViewer.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
                        DocViewer.Document = doc;
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }
            }
        }

        private string GetBrushHex(string resourceKey, string fallback)
        {
            try
            {
                var res = System.Windows.Application.Current.TryFindResource(resourceKey);
                if (res is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                }
                if (res is System.Windows.Media.Color c2)
                    return $"#{c2.A:X2}{c2.R:X2}{c2.G:X2}{c2.B:X2}";
            }
            catch { }
            return fallback;
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
                    // horizontal rule
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
                else if (block is Markdig.Syntax.LinkReferenceDefinitionGroup)
                {
                    // ignore reference definitions
                }
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
                else
                {
                    // fallback: skip unknown block types silently
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
                {
                    inlines.Add(new Run(li.Content.ToString()));
                }
                else if (inline is CodeInline ci)
                {
                    var run = new Run(ci.Content)
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Consolas")
                    };
                    var span = new Span(run)
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x30, 0x30, 0x30))
                    };
                    inlines.Add(span);
                }
                else if (inline is AutolinkInline al)
                {
                    var hl = new Hyperlink(new Run(al.Url ?? al.ToString()));
                    try { hl.NavigateUri = new Uri(al.Url); } catch { }
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
                else if (inline is LineBreakInline lb)
                {
                    inlines.Add(new LineBreak());
                }
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
                {
                    // unknown inline, add raw text
                    inlines.Add(new Run(inline.ToString()));
                }
            }
        }

        private string ResolvePath(string url, string? baseFolder)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            // If url is absolute, return as-is; if relative, combine with baseFolder
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

        private void BtnRefreshNews_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Directory.Exists(_lastPath))
                LoadLocal(_lastPath);
        }
    }
}
