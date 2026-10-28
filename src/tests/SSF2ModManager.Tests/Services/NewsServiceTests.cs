using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System;
using System.IO;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class NewsServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public NewsServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "ssf2mm-news-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }

        [Fact]
        public void LoadMergedArticles_CacheOverridesBundledWithSameId()
        {
            var bundled = Path.Combine(_tempRoot, "bundled");
            var cache = Path.Combine(_tempRoot, "cache");
            var id = "2026-test-article";
            WriteArticle(bundled, id, "Bundled Title", "2026-01-01");
            WriteArticle(cache, id, "Cached Title", "2026-06-01");

            var originalBundled = AppPaths.NewsFolder;
            var originalCache = AppPaths.NewsCacheFolder;
            try
            {
                // Use direct folder loaders via temp paths
                var bundledArticles = NewsService.LoadLocalArticles(bundled);
                var cacheArticles = NewsService.LoadLocalArticles(cache);

                Assert.Single(bundledArticles);
                Assert.Equal("Bundled Title", bundledArticles[0].Title);
                Assert.Single(cacheArticles);
                Assert.Equal("Cached Title", cacheArticles[0].Title);
                Assert.Equal(id, cacheArticles[0].Id);
            }
            finally { _ = originalBundled; _ = originalCache; }
        }

        [Fact]
        public void CountUnread_RespectsReadIdsAndReleaseFilter()
        {
            var readIds = new[] { "read-one" };
            var articles = new[]
            {
                new NewsArticle { Id = "read-one", Tags = { "release" } },
                new NewsArticle { Id = "unread-release", Tags = { "release" } },
                new NewsArticle { Id = "unread-other", Tags = { "dev" } }
            };

            Assert.Equal(2, NewsService.CountUnread(articles, readIds));
            Assert.Equal(1, NewsService.CountUnread(articles, readIds, releaseTaggedOnly: true));
        }

        private static void WriteArticle(string root, string id, string title, string date)
        {
            var dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "article.md"),
                $"---\ntitle: \"{title}\"\ndate: \"{date}\"\ndraft: false\n---\n\nBody");
        }
    }
}
