using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Copies a downloaded pack into the Dev Wiki when it is marked Tutorial or contains tutorial.md.
    /// Only markdown and images are copied.
    /// </summary>
    public static class TutorialWikiImporter
    {
        private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"
        };

        public static void TryImport(string modPath, bool force = false)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
                return;
            var docs = FindDocs();
            if (docs == null)
                return;

            var type = ReadType(modPath);
            var hasTutorial = Directory.GetFiles(modPath, "tutorial.md", SearchOption.AllDirectories).Length > 0;
            if (!force && !hasTutorial && !string.Equals(type, "Tutorial", StringComparison.OrdinalIgnoreCase))
                return;

            var copied = new List<string>();
            foreach (var file in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
            {
                if (!DocExt.Contains(Path.GetExtension(file)))
                    continue;
                var rel = Path.GetRelativePath(modPath, file);
                if (rel.Contains("..", StringComparison.Ordinal))
                    continue;
                copied.Add(file);
            }
            if (copied.Count == 0)
                return;

            var dest = Path.Combine(docs, "Dev Wiki", "Tutorials", SafeName(Path.GetFileName(modPath)));
            Directory.CreateDirectory(dest);
            foreach (var file in copied)
            {
                var rel = Path.GetRelativePath(modPath, file);
                var target = Path.Combine(dest, rel);
                var parent = Path.GetDirectoryName(target);
                if (parent != null)
                    Directory.CreateDirectory(parent);
                File.Copy(file, target, true);
            }
            StampDownloaded(dest);
            Rebuild(docs);
        }

        private static void StampDownloaded(string dest)
        {
            var path = Path.Combine(dest, "info.json");
            JObject obj;
            if (File.Exists(path))
            {
                try { obj = JObject.Parse(File.ReadAllText(path)); }
                catch { obj = new JObject(); }
            }
            else obj = new JObject();
            obj["type"] = "Tutorial";
            obj["downloaded"] = true;
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static void Rebuild(string docs)
        {
            var script = Path.Combine(docs, "_static", "build-docs.ps1");
            if (!File.Exists(script))
                return;
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                    WorkingDirectory = docs,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(start);
                process?.WaitForExit(120000);
            }
            catch
            {
            }
        }

        private static string? ReadType(string modPath)
        {
            var info = Path.Combine(modPath, "info.json");
            if (!File.Exists(info))
            {
                var found = Directory.GetFiles(modPath, "info.json", SearchOption.AllDirectories).FirstOrDefault();
                if (found == null)
                    return null;
                info = found;
            }
            try
            {
                var obj = JObject.Parse(File.ReadAllText(info));
                return obj["type"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static string? FindDocs()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var wiki = Path.Combine(dir.FullName, "wiki");
                if (File.Exists(Path.Combine(wiki, "_static", "index.html")))
                    return wiki;
            }
            return null;
        }

        private static string SafeName(string name)
        {
            var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
            var cleaned = new string(chars).Trim();
            return string.IsNullOrEmpty(cleaned) ? "tutorial" : cleaned;
        }
    }
}
