using System;
using System.Collections.Generic;
using System.Linq;

namespace SSF2ModManager.Services
{
    public static class MusicSync
    {
        public static CachedSongInfo? Find(IEnumerable<CachedSongInfo>? songs, string? id, string? name)
        {
            var list = songs?.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id)).ToList();
            if (list == null || list.Count == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var byId = list.FirstOrDefault(s => IdMatch(s.Id, id));
                if (byId != null)
                    return byId;
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                return list.FirstOrDefault(s =>
                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.DisplayLabel, name, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        public static bool IdMatch(string? songId, string? reported)
        {
            if (string.IsNullOrWhiteSpace(songId) || string.IsNullOrWhiteSpace(reported))
                return false;
            if (string.Equals(songId, reported, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(songId, "bgm_" + reported, StringComparison.OrdinalIgnoreCase))
                return true;
            return songId.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase)
                && string.Equals(songId[4..], reported, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Same(CachedSongInfo? current, CachedSongInfo? reported)
        {
            if (current == null || reported == null)
                return false;
            return IdMatch(current.Id, reported.Id);
        }
    }
}
