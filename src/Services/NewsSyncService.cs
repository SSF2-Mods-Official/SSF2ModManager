using Newtonsoft.Json;
using SSF2ModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    public static class NewsSyncService
    {
        public static async Task<NewsSyncResult> SyncAsync(SettingsService settings, bool force = false, CancellationToken ct = default)
        {
            var result = new NewsSyncResult();
            try
            {
                using var http = CreateClient();
                var json = await http.GetStringAsync(AppInfo.NewsIndexUrl, ct);
                var index = JsonConvert.DeserializeObject<NewsIndex>(json);
                if (index?.Articles == null || index.Articles.Count == 0)
                {
                    result.Success = true;
                    settings.SetLastNewsSync(DateTime.UtcNow, null);
                    return result;
                }

                Directory.CreateDirectory(AppPaths.NewsCacheFolder);

                foreach (var entry in index.Articles.Where(a => !string.IsNullOrWhiteSpace(a.Id) && !a.Draft))
                {
                    ct.ThrowIfCancellationRequested();
                    var cacheDir = Path.Combine(AppPaths.NewsCacheFolder, entry.Id);
                    var articlePath = Path.Combine(cacheDir, "article.md");

                    if (!force && File.Exists(articlePath) && IsCacheFresh(cacheDir, entry))
                        continue;

                    Directory.CreateDirectory(cacheDir);
                    var assets = entry.Assets?.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                                 ?? new List<string> { "article.md" };
                    if (!assets.Contains("article.md", StringComparer.OrdinalIgnoreCase))
                        assets.Insert(0, "article.md");

                    foreach (var asset in assets)
                    {
                        var url = $"{AppInfo.NewsRawBaseUrl}/{entry.Id}/{asset.Replace('\\', '/')}";
                        var dest = Path.Combine(cacheDir, asset);
                        var destDir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        var bytes = await http.GetByteArrayAsync(url, ct);
                        await File.WriteAllBytesAsync(dest, bytes, ct);
                    }

                    File.WriteAllText(Path.Combine(cacheDir, ".sync-index-date"), entry.Date ?? "");
                    result.DownloadedCount++;
                }

                result.Success = true;
                settings.SetLastNewsSync(DateTime.UtcNow, null);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("News sync failed", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                settings.SetLastNewsSync(settings.GetLastNewsSyncUtc(), ex.Message);
            }

            return result;
        }

        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(AppPaths.NewsCacheFolder))
                    Directory.Delete(AppPaths.NewsCacheFolder, recursive: true);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Failed to clear news cache", ex);
            }
        }

        private static bool IsCacheFresh(string cacheDir, NewsIndexEntry entry)
        {
            var marker = Path.Combine(cacheDir, ".sync-index-date");
            if (!File.Exists(marker)) return false;
            var cachedDate = File.ReadAllText(marker).Trim();
            return string.Equals(cachedDate, entry.Date?.Trim(), StringComparison.Ordinal);
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            return http;
        }
    }
}
