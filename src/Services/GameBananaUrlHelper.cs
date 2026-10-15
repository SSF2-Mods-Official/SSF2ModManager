using SSF2ModManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Parses GameBanana /mmdl/ download URLs and matches files by ID.
    /// </summary>
    public static class GameBananaUrlHelper
    {
        private static readonly Regex FileIdRegex = new(
            @"/mmdl/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Fixes broken schemes like https// → https:// from some 1-click launchers.</summary>
        public static string NormalizeSchemeTypos(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var s = url.Trim();
            s = Regex.Replace(s, @"^https//", "https://", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^http//", "http://", RegexOptions.IgnoreCase);
            return s;
        }

        public static bool TryExtractFileId(string? url, out int fileId)
        {
            fileId = 0;
            if (string.IsNullOrWhiteSpace(url)) return false;
            var match = FileIdRegex.Match(NormalizeSchemeTypos(url));
            return match.Success && int.TryParse(match.Groups[1].Value, out fileId);
        }

        /// <summary>
        /// Pick the mod file referenced by a 1-click archive URL (/mmdl/{id}).
        /// </summary>
        public static GameBananaFile? MatchProtocolFile(string archiveUrl, IList<GameBananaFile>? files)
        {
            if (files == null || files.Count == 0) return null;

            if (TryExtractFileId(archiveUrl, out var fileId))
            {
                var byId = files.FirstOrDefault(f => f.Id == fileId);
                if (byId != null) return byId;
            }

            return files.OrderByDescending(f => f.DateAdded).FirstOrDefault();
        }
    }
}
