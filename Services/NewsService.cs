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
        // Load local articles from a News folder (each article in its own subfolder with article.md)
        public static List<NewsArticle> LoadLocalArticles(string newsFolder)
        {
            var outList = new List<NewsArticle>();
            try
            {
                if (!Directory.Exists(newsFolder)) return outList;

                // Accept both directories and direct article.md files in News root
                var dirs = Directory.GetDirectories(newsFolder).ToList();
                var mdFiles = Directory.GetFiles(newsFolder, "*.md", SearchOption.TopDirectoryOnly).ToList();

                foreach (var d in dirs)
                {
                    // Prefer article.md inside each folder
                    var am = Path.Combine(d, "article.md");
                    if (File.Exists(am))
                    {
                        var art = ParseArticleMarkdown(File.ReadAllText(am), d);
                        if (art != null) outList.Add(art);
                    }
                }

                // Also parse top-level markdown files if any
                foreach (var f in mdFiles)
                {
                    var folder = Path.GetDirectoryName(f) ?? newsFolder;
                    var art = ParseArticleMarkdown(File.ReadAllText(f), folder);
                    if (art != null) outList.Add(art);
                }

                // Order by date desc
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
            // Store cleaned markdown (without YAML frontmatter) so renderers don't show the frontmatter
            article.RawMarkdown = content;

            // Parse YAML frontmatter (robust regex parser)
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
                                case "tags": /* tags may follow on next lines */ break;
                            }
                        }
                    }
                }
            }

            // Store cleaned markdown (without YAML frontmatter) so renderers don't show the frontmatter
            article.RawMarkdown = content;

            // Fallback title/date
            if (string.IsNullOrWhiteSpace(article.Title))
            {
                article.Title = Path.GetFileName(sourceFolder);
            }
            if (article.Date == default)
                article.Date = Directory.GetCreationTime(sourceFolder);

            // Convert markdown -> HTML with base href so images resolve
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

/*
 // Future: remote fetcher (GitHub) - example placeholder
 public static List<NewsArticle> LoadRemoteArticles(string repoOwner, string repoName, string branch = "main")
 {
     // This method will use GitHub Contents API or raw.githubusercontent to list folders under /News
     // and fetch article.md and image files. For now the app uses local folder only.
     throw new NotImplementedException();
 }
*/
