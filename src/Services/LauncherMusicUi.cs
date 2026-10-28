using System;
using System.Collections.Generic;
using System.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>Pure helpers for the launcher footer music strip (testable without WPF).</summary>
    public static class LauncherMusicUi
    {
        public static string FormatTrackTime(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        public static string TrackLabel(CachedSongInfo? song) =>
            song == null ? "No track" : song.DisplayLabel;

        /// <summary>Preview bar when the pointer is not on a control that has hover text.</summary>
        public static string PreviewIdleText(string productName, string version) =>
            string.IsNullOrWhiteSpace(version) ? productName : productName + " " + version;

        /// <summary>Writes the song that should be current the next time the mini player opens.</summary>
        public static void RememberLastSong(MusicLibraryCache cache, CachedSongInfo? current, IEnumerable<CachedSongInfo>? queue)
        {
            if (current == null || string.IsNullOrWhiteSpace(current.Id))
                return;
            cache.LastPlayedSongId = current.Id;
            cache.LastPlayedQueueIds = (queue ?? Array.Empty<CachedSongInfo>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id))
                .Select(s => s.Id)
                .ToList();
        }

        public static double SeekRatio(TimeSpan position, TimeSpan duration)
        {
            if (duration.TotalMilliseconds <= 0) return 0;
            return Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        }

        public static TimeSpan SeekFromRatio(double ratio, TimeSpan duration)
        {
            if (duration.TotalMilliseconds <= 0) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(
                Math.Clamp(ratio, 0, 1) * duration.TotalMilliseconds);
        }

        /// <summary>Songs for the picker, sorted by display label.</summary>
        public static IReadOnlyList<CachedSongInfo> PickerSongs(IEnumerable<CachedSongInfo>? songs) =>
            (songs ?? Array.Empty<CachedSongInfo>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id))
                .OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// One requested song plays inside the list already on the player.
        /// A longer request, such as a playlist, becomes the queue.
        /// </summary>
        public static (IReadOnlyList<CachedSongInfo> Queue, int Index) PreviewQueue(
            IReadOnlyList<CachedSongInfo>? current,
            IReadOnlyList<CachedSongInfo>? library,
            IReadOnlyList<CachedSongInfo>? requested,
            int index)
        {
            var asked = (requested ?? Array.Empty<CachedSongInfo>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id))
                .ToList();
            if (asked.Count == 0)
                return (current ?? Array.Empty<CachedSongInfo>(), 0);
            if (asked.Count > 1)
                return (asked, Math.Clamp(index, 0, asked.Count - 1));

            var song = asked[0];
            var queue = (current ?? Array.Empty<CachedSongInfo>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id))
                .ToList();
            if (queue.Count <= 1 && library != null)
            {
                var full = PickerSongs(library).ToList();
                if (full.Count > 1)
                    queue = full;
            }
            var found = queue.FindIndex(s => s.Id.Equals(song.Id, StringComparison.OrdinalIgnoreCase));
            if (found >= 0)
                return (queue, found);
            queue.Add(song);
            return (queue, queue.Count - 1);
        }
    }
}
