using Newtonsoft.Json;
using SSF2ModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SSF2ModManager.Services
{
    public class GameBananaApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;

        // SSF2 GameBanana Game ID
        private const int SSF2_GAME_ID = 5789;
        private const string API_BASE = "https://gamebanana.com/apiv11";

        public GameBananaApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Search for mods on GameBanana for SSF2
        /// </summary>
        public async Task<(List<GameBananaMod> Mods, bool HasMore, int TotalCount)> SearchModsAsync(string query = "", int page = 1, int perPage = 15, string sort = "default")
        {
            string url;

            // "likes" and "downloads" sorts require the Mod/Index endpoint
            if (string.IsNullOrWhiteSpace(query) && (sort == "likes" || sort == "downloads"))
            {
                var indexSort = sort == "likes" ? "Generic_MostLiked" : "Generic_MostDownloaded";
                url = $"{API_BASE}/Mod/Index" +
                      $"?_nPage={page}&_nPerpage={perPage}" +
                      $"&_aFilters[Generic_Game]={SSF2_GAME_ID}" +
                      $"&_sSort={indexSort}";
            }
            else
            {
                string sortParam = sort switch
                {
                    "new" => "&_sSort=new",
                    "updated" => "&_sSort=updated",
                    _ => ""
                };

                if (!string.IsNullOrWhiteSpace(query))
                {
                    url = $"{API_BASE}/Util/Search/Results" +
                          $"?_sSearchString={HttpUtility.UrlEncode(query)}" +
                          $"&_nPage={page}&_nPerpage={perPage}" +
                          $"&_idGameRow={SSF2_GAME_ID}" +
                          $"&_sModelName=Mod" +
                          sortParam;
                }
                else
                {
                    url = $"{API_BASE}/Game/{SSF2_GAME_ID}/Subfeed" +
                          $"?_nPage={page}&_nPerpage={perPage}" +
                          $"&_sModelName=Mod" +
                          sortParam;
                }
            }

            var response = await _httpClient.GetStringAsync(url);
            return ParseRecordPage(response, requireModModel: true);
        }

        /// <summary>
        /// Mods in one GameBanana category. Events is 49482 and Music is 49485. Other Download Mods categories use 0 until their ids are known.
        /// Generic_MostRecent is not used; that sort returns HTTP 400.
        /// </summary>
        public async Task<(List<GameBananaMod> Mods, bool HasMore, int TotalCount)> SearchCategoryAsync(int categoryId, int page = 1, int perPage = 40)
        {
            var url = $"{API_BASE}/Mod/Index?_nPage={page}&_nPerpage={perPage}&_aFilters%5BGeneric_Category%5D={categoryId}";
            var response = await _httpClient.GetStringAsync(url);
            return ParseRecordPage(response, requireModModel: false);
        }

        /// <summary>Every SSF2 mod, 50 a page. Records carry preview, submitter, likes and views but no description.</summary>
        public async Task<(List<GameBananaMod> Mods, bool HasMore, int TotalCount)> BrowseIndexAsync(int page, int perPage = 50)
        {
            var url = $"{API_BASE}/Mod/Index?_nPage={page}&_nPerpage={perPage}&_aFilters%5BGeneric_Game%5D={SSF2_GAME_ID}";
            var response = await _httpClient.GetStringAsync(url);
            return ParseRecordPage(response, requireModModel: false);
        }

        /// <summary>Description (<c>_sText</c>), download count and files for one mod.</summary>
        public async Task<GameBananaMod?> GetModDetailsAsync(int modId)
        {
            var url = $"{API_BASE}/Mod/{modId}?_csvProperties=_sName,_sText,_nDownloadCount,_aFiles,_aPreviewMedia";
            var response = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<GameBananaMod>(response);
        }

        /// <summary>The paths inside a GameBanana file, from <c>apiv13/File/&lt;id&gt;/RawFileList</c> (plain text, one per line).</summary>
        public async Task<List<string>> GetRawFileListAsync(int fileId, CancellationToken cancellationToken = default)
        {
            var url = $"https://gamebanana.com/apiv13/File/{fileId}/RawFileList";
            var text = await _httpClient.GetStringAsync(url, cancellationToken);
            return text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("{") && !l.StartsWith("<"))
                .ToList();
        }

        public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default) =>
            await _httpClient.GetByteArrayAsync(url, cancellationToken);

        private static (List<GameBananaMod> Mods, bool HasMore, int TotalCount) ParseRecordPage(string response, bool requireModModel)
        {
            var wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
            if (wrapper != null && wrapper.TryGetValue("_aRecords", out var recordsObj))
            {
                var allRecords = JsonConvert.DeserializeObject<List<GameBananaMod>>(recordsObj.ToString()!);
                var mods = allRecords ?? new List<GameBananaMod>();
                if (requireModModel)
                    mods = mods.Where(m => m.ModelName == "Mod").ToList();

                bool hasMore = false;
                int totalCount = 0;
                if (wrapper.TryGetValue("_aMetadata", out var metaObj))
                {
                    var meta = JsonConvert.DeserializeObject<Dictionary<string, object>>(metaObj.ToString()!);
                    if (meta != null)
                    {
                        if (meta.TryGetValue("_bIsComplete", out var isComplete))
                            hasMore = !Convert.ToBoolean(isComplete);
                        if (meta.TryGetValue("_nRecordCount", out var count))
                            totalCount = Convert.ToInt32(count);
                    }
                }

                return (mods, hasMore, totalCount);
            }
            return (new List<GameBananaMod>(), false, 0);
        }

        /// <summary>
        /// Get all mods for SSF2 (fetches all pages). Used for client-side filtering by author/category.
        /// Results are cached for the session.
        /// </summary>
        private List<GameBananaMod>? _allModsCache;
        private DateTime _allModsCacheTime;

        public async Task<List<GameBananaMod>> GetAllModsAsync()
        {
            // Cache for 5 minutes
            if (_allModsCache != null && (DateTime.Now - _allModsCacheTime).TotalMinutes < 5)
                return _allModsCache;

            var allMods = new List<GameBananaMod>();
            int page = 1;
            bool hasMore = true;

            while (hasMore)
            {
                var (mods, more, _) = await SearchModsAsync("", page, 50);
                allMods.AddRange(mods);
                hasMore = more;
                page++;
                if (page > 20) break; // Safety limit
            }

            _allModsCache = allMods;
            _allModsCacheTime = DateTime.Now;
            return allMods;
        }

        /// <summary>
        /// Get a specific mod by ID
        /// </summary>
        public async Task<GameBananaMod?> GetModAsync(int modId)
        {
            var url = $"{API_BASE}/Mod/{modId}/ProfilePage";
            var response = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<GameBananaMod>(response);
        }

        /// <summary>
        /// Fetch only the _sText (description HTML) for a mod. Lightweight single-property fetch.
        /// </summary>
        public async Task<string> GetModTextAsync(int modId)
        {
            var url = $"{API_BASE}/Mod/{modId}?_csvProperties=_sText";
            var response = await _httpClient.GetStringAsync(url);
            var mod = JsonConvert.DeserializeObject<GameBananaMod>(response);
            return mod?.FullText ?? "";
        }

        /// <summary>
        /// Get files for a specific mod
        /// </summary>
        public async Task<List<GameBananaFile>> GetModFilesAsync(int modId)
        {
            var url = $"{API_BASE}/Mod/{modId}?_csvProperties=_aFiles";
            var response = await _httpClient.GetStringAsync(url);
            var mod = JsonConvert.DeserializeObject<GameBananaMod>(response);
            return mod?.Files ?? new List<GameBananaFile>();
        }

        /// <summary>
        /// Download a file from GameBanana
        /// </summary>
        public async Task<byte[]> DownloadFileAsync(string downloadUrl, IProgress<double>? progress = null)
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var ms = totalBytes > 0
                ? new MemoryStream((int)Math.Min(totalBytes, int.MaxValue))
                : new MemoryStream();

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes * 100.0);
            }

            return ms.ToArray();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
