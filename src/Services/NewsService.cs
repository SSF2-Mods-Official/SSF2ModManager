using Markdig;
using SSF2ModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SSF2ModManager.Services
{
    public static class NewsService
    {
        /// <summary>Load articles from bundled News folder and AppData cache, merged by article id.</summary>
        public static List<NewsArticle> LoadMergedArticles(bool includeDrafts = false)
        {
            var byId = new Dictionary<string, NewsArticle>(StringComparer.OrdinalIgnoreCase);

            void MergeFolder(string folder)
            {
                if (!Directory.Exists(folder)) return;
                foreach (var article in LoadLocalArticles(folder))
                {
                    if (string.IsNullOrWhiteSpace(article.Id))
                        article.Id = Path.GetFileName(article.SourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!includeDrafts && article.Draft) continue;
                    byId[article.Id] = article;
                }
            }

            MergeFolder(AppPaths.NewsFolder);
            MergeFolder(AppPaths.NewsCacheFolder);

            return byId.Values.OrderByDescending(a => a.Date).ToList();
        }

        public static int CountUnread(IEnumerable<NewsArticle> articles, SettingsService settings, bool releaseTaggedOnly = false)
            => CountUnread(articles, settings.GetReadNewsArticleIds(), releaseTaggedOnly);

        public static int CountUnread(IEnumerable<NewsArticle> articles, IEnumerable<string> readIds, bool releaseTaggedOnly = false)
        {
            var read = new HashSet<string>(readIds, StringComparer.OrdinalIgnoreCase);
            return articles.Count(a =>
            {
                if (read.Contains(a.Id)) return false;
                if (releaseTaggedOnly && !a.Tags.Any(t => t.Equals("release", StringComparison.OrdinalIgnoreCase)))
                    return false;
                return true;
            });
        }

        // Load local articles from a News folder (each article in its own subfolder with article.md)
        public static List<NewsArticle> LoadLocalArticles(string newsFolder)
        {
            var outList = new List<NewsArticle>();
            try
            {
                if (!Directory.Exists(newsFolder)) return outList;

                var dirs = Directory.GetDirectories(newsFolder).ToList();
                var mdFiles = Directory.GetFiles(newsFolder, "*.md", SearchOption.TopDirectoryOnly).ToList();

                foreach (var d in dirs)
                {
                    var am = Path.Combine(d, "article.md");
                    if (File.Exists(am))
                    {
                        var art = ParseArticleMarkdown(File.ReadAllText(am), d);
                        if (art != null) outList.Add(art);
                    }
                }

                foreach (var f in mdFiles)
                {
                    var folder = Path.GetDirectoryName(f) ?? newsFolder;
                    var art = ParseArticleMarkdown(File.ReadAllText(f), folder);
                    if (art != null) outList.Add(art);
                }

                outList = outList.OrderByDescending(a => a.Date).ToList();
            }
            catch { }
            return outList;
        }

        private static NewsArticle? ParseArticleMarkdown(string raw, string sourceFolder)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string content = raw;
            var article = new NewsArticle();
            article.SourceFolder = sourceFolder;
            article.Id = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            article.RawMarkdown = content;

            var fmMatch = Regex.Match(raw, "^---\\s*([\\s\\S]*?)\\s*---\\s*", RegexOptions.Multiline);
            if (fmMatch.Success)
            {
                var fm = fmMatch.Groups[1].Value;
                content = raw.Substring(fmMatch.Index + fmMatch.Length).Trim();

                using (var sr = new StringReader(fm))
                {
                    string? line;
                    string? lastKey = null;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.TrimStart().StartsWith("- ") && lastKey == "tags")
                        {
                            article.Tags.Add(line.Trim().TrimStart('-').Trim());
                            continue;
                        }
                        var idx = line.IndexOf(':');
                        if (idx > 0)
                        {
                            var k = line[..idx].Trim();
                            var v = line[(idx + 1)..].Trim().Trim('"');
                            lastKey = k;
                            switch (k)
                            {
                                case "title": article.Title = v; break;
                                case "date": DateTime.TryParse(v, out var dt); article.Date = dt; break;
                                case "author": article.Author = v; break;
                                case "excerpt": article.Excerpt = v; break;
                                case "featured_image": article.FeaturedImage = v; break;
                                case "draft": article.Draft = v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                                case "tags": break;
                            }
                        }
                    }
                }
            }

            article.RawMarkdown = content;

            if (string.IsNullOrWhiteSpace(article.Title))
                article.Title = article.Id;
            if (article.Date == default)
                article.Date = Directory.GetCreationTime(sourceFolder);

            try
            {
                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                var body = Markdig.Markdown.ToHtml(content, pipeline);
                var baseHref = new Uri(sourceFolder + Path.DirectorySeparatorChar).AbsoluteUri;
                var css = "body{font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#DDD;padding:18px;margin:0;} img{max-width:100%;height:auto;} h1,h2,h3{color:#FFF;margin:0 0 8px 0;} .excerpt{color:#BBB;margin:8px 0 12px 0;}";
                var html = "<!DOCTYPE html>" +
                           $"<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" /><base href=\"{baseHref}\" /><meta charset=\"utf-8\"/><style>{css}</style></head><body>";
                if (!string.IsNullOrWhiteSpace(article.FeaturedImage))
                {
                    html += $"<img src=\"{article.FeaturedImage}\" alt=\"{System.Net.WebUtility.HtmlEncode(article.Title)}\" style=\"width:100%;max-height:360px;object-fit:cover;margin-bottom:12px;\"/>";
                }
                if (!string.IsNullOrWhiteSpace(article.Excerpt))
                    html += $"<div class=\"excerpt\">{System.Net.WebUtility.HtmlEncode(article.Excerpt)}</div>";
                html += body + "</body></html>";
                article.Html = html;
            }
            catch
            {
                article.Html = "<html><body><pre>Failed to render article</pre></body></html>";
            }

            return article;
        }
    }
}
