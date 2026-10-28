using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SSF2ModManager.Services
{
    public static class EventNames
    {
        public static bool IsTaken(IEnumerable<string?> names, string? name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0)
                return false;
            return names.Any(n => string.Equals((n ?? "").Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Event menu art is <c>previews/&lt;id&gt;.png</c> on the pack.</summary>
    public static class EventPreview
    {
        private static readonly byte[] PlaceholderPng =
        {
            137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, 144, 119, 83, 222,
            0, 0, 0, 12, 73, 68, 65, 84, 8, 215, 99, 96, 96, 96, 0, 0, 0, 4, 0, 1, 94, 34, 227, 85, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130
        };

        public static string Write(string packDir, string eventId, string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(packDir))
                throw new ArgumentException("Pack folder is required.", nameof(packDir));
            var id = (eventId ?? "").Trim();
            if (id.Length == 0)
                throw new ArgumentException("Event id is required.", nameof(eventId));
            var dir = Path.Combine(packDir, "previews");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, id + ".png");
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
                File.Copy(sourcePath, dest, overwrite: true);
            else
                File.WriteAllBytes(dest, PlaceholderPng);
            return dest;
        }
    }
}
