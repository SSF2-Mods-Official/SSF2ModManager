using System.Net.Http;
using System.Text.Json;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Picks a GitHub release for the stable channel (<c>/releases/latest</c>) or the experimental
    /// channel (newest non-draft from the releases list, including prereleases).
    /// </summary>
    public static class GitHubReleaseChannel
    {
        public static string LatestUrl(string ownerRepo) =>
            $"https://api.github.com/repos/{ownerRepo}/releases/latest";

        public static string ListUrl(string ownerRepo, int perPage = 20) =>
            $"https://api.github.com/repos/{ownerRepo}/releases?per_page={perPage}";

        /// <summary>
        /// Newest non-draft release. When <paramref name="includePrereleases"/> is false, uses
        /// <c>/releases/latest</c>. Caller owns the returned document.
        /// </summary>
        public static async Task<JsonDocument?> FetchNewestDocumentAsync(
            HttpClient http,
            string ownerRepo,
            bool includePrereleases,
            CancellationToken ct = default)
        {
            if (!includePrereleases)
            {
                using var resp = await http.GetAsync(LatestUrl(ownerRepo), ct);
                if (!resp.IsSuccessStatusCode)
                    return null;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }

            using var listResp = await http.GetAsync(ListUrl(ownerRepo), ct);
            if (!listResp.IsSuccessStatusCode)
                return null;
            await using var listStream = await listResp.Content.ReadAsStreamAsync(ct);
            using var listDoc = await JsonDocument.ParseAsync(listStream, cancellationToken: ct);
            var picked = PickNewestRaw(listDoc.RootElement);
            return picked == null ? null : JsonDocument.Parse(picked);
        }

        /// <summary>
        /// From a GitHub releases JSON array (newest first), return the raw JSON of the first
        /// non-draft release, or null.
        /// </summary>
        public static string? PickNewestRaw(JsonElement releasesArray)
        {
            if (releasesArray.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in releasesArray.EnumerateArray())
            {
                if (item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;
                return item.GetRawText();
            }
            return null;
        }

        /// <summary>Tag name of the release picked by <see cref="PickNewestRaw"/>, or empty.</summary>
        public static string PickNewestTag(JsonElement releasesArray)
        {
            var raw = PickNewestRaw(releasesArray);
            if (raw == null)
                return "";
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        }
    }
}
