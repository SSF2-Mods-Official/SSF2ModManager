using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace SSF2ModManager.Services
{
    public sealed class PackageModEntry
    {
        public string FolderPath { get; init; } = "";
        public string Section { get; init; } = "";
        public string Label { get; set; } = "";
        public string Name { get; set; } = "";
        public string Creator { get; set; } = "";
        public string Type { get; set; } = "Mod";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "";
        public bool Include { get; set; }
    }

    public sealed class DeployedPack
    {
        public string Kind { get; init; } = "";
        public string Dest { get; init; } = "";
        public string Name { get; init; } = "";
    }

    /// <summary>
    /// Lists local packs the user can zip for upload. Downloaded packs (info.json downloaded:true) are skipped.
    /// </summary>
    public static class ModPackageService
    {
        public static readonly string[] Types = ["Mod", "Event", "Character", "Stage", "Music Pack", "Tutorial"];

        private static readonly (string Folder, string Section, string Type)[] Roots =
        [
            ("mods", "Mods", "Mod"),
            ("events", "Events", "Event"),
            ("character", "Characters", "Character"),
            ("stages", "Stages", "Stage"),
            ("songs\\packs", "Music", "Music Pack")
        ];

        public static List<PackageModEntry> List(string shellRoot)
        {
            var list = new List<PackageModEntry>();
            var expansions = Path.Combine(shellRoot, "expansions");
            if (!Directory.Exists(expansions))
                return list;

            foreach (var root in Roots)
            {
                var dir = Path.Combine(expansions, root.Folder);
                if (!Directory.Exists(dir))
                    continue;
                foreach (var folder in Directory.GetDirectories(dir).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(folder);
                    if (name.StartsWith('_') || name.StartsWith('.'))
                        continue;
                    if (IsDownloaded(folder))
                        continue;
                    if (!CanPackage(folder, root.Type))
                        continue;
                    list.Add(Read(folder, root.Section, root.Type));
                }
            }
            list.AddRange(ListUserTutorials());
            return list;
        }

        public static void SaveInfo(PackageModEntry entry)
        {
            var path = Path.Combine(entry.FolderPath, "info.json");
            JObject obj;
            if (File.Exists(path))
            {
                try { obj = JObject.Parse(File.ReadAllText(path)); }
                catch { obj = new JObject(); }
            }
            else obj = new JObject();

            obj["name"] = entry.Name?.Trim() ?? "";
            obj["creator"] = entry.Creator?.Trim() ?? "";
            obj["type"] = string.IsNullOrWhiteSpace(entry.Type) ? "Mod" : entry.Type.Trim();
            obj["description"] = entry.Description?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(entry.Version))
                obj.Remove("version");
            else
                obj["version"] = entry.Version.Trim();
            obj.Remove("downloaded");
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public static void PackageZip(IReadOnlyList<PackageModEntry> entries, string zipPath)
        {
            if (entries.Count == 0)
                throw new InvalidOperationException("Pick at least one mod to package.");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            var multiple = entries.Count > 1;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var manifest = new JArray();
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var entry in entries)
            {
                var folder = UniqueFolder(Path.GetFileName(entry.FolderPath), used);
                manifest.Add(new JObject
                {
                    ["folder"] = folder,
                    ["type"] = multiple ? "multiple" : entry.Type
                });
                foreach (var file in Directory.GetFiles(entry.FolderPath, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(entry.FolderPath, file).Replace('\\', '/');
                    if (SkipInZip(rel))
                        continue;
                    if (multiple && rel.Equals("info.json", StringComparison.OrdinalIgnoreCase))
                    {
                        JObject info;
                        try { info = JObject.Parse(File.ReadAllText(file)); }
                        catch { info = new JObject(); }
                        info["type"] = "multiple";
                        var infoEntry = zip.CreateEntry(folder + "/" + rel, CompressionLevel.Optimal);
                        using var infoWriter = new StreamWriter(infoEntry.Open());
                        infoWriter.Write(info.ToString(Newtonsoft.Json.Formatting.Indented));
                        continue;
                    }
                    zip.CreateEntryFromFile(file, folder + "/" + rel, CompressionLevel.Optimal);
                }
            }
            var manifestJson = new JObject
            {
                ["format"] = "ssf2mm-pack",
                ["mods"] = manifest
            };
            var entryManifest = zip.CreateEntry("ssf2mm-pack.json");
            using var writer = new StreamWriter(entryManifest.Open());
            writer.Write(manifestJson.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>
        /// Extract a zip, rar, or 7z and copy each pack into the matching expansions folder.
        /// </summary>
        public static List<DeployedPack> InstallArchive(string shellRoot, string archivePath)
        {
            var temp = Path.Combine(Path.GetTempPath(), "ssf2mm-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                using (var archive = ArchiveFactory.OpenArchive(archivePath))
                {
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        var key = entry.Key?.Replace('/', Path.DirectorySeparatorChar) ?? "";
                        if (string.IsNullOrWhiteSpace(key))
                            continue;
                        var dest = Path.GetFullPath(Path.Combine(temp, key));
                        if (!dest.StartsWith(Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                            continue;
                        var dir = Path.GetDirectoryName(dest);
                        if (dir != null)
                            Directory.CreateDirectory(dir);
                        entry.WriteToFile(dest, new ExtractionOptions { Overwrite = true });
                    }
                }
                return DeployExtracted(shellRoot, temp, includeSingleGenericMod: true);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>Place every pack found in an extracted archive into the shell.</summary>
        public static List<DeployedPack> DeployExtracted(string shellRoot, string extractedRoot, bool includeSingleGenericMod = false)
        {
            var placed = new List<DeployedPack>();
            if (!Directory.Exists(extractedRoot) || !Directory.Exists(shellRoot))
                return placed;
            var packs = FindPacks(extractedRoot);
            if (!includeSingleGenericMod && packs.Count == 1 && packs[0].Kind == "mod")
                return placed;
            foreach (var pack in packs)
            {
                var parent = DestParent(shellRoot, pack.Kind);
                if (parent == null)
                    continue;
                Directory.CreateDirectory(parent);
                var folder = SanitizeFolder(pack.FolderName);
                var dest = Path.GetFullPath(Path.Combine(parent, folder));
                if (!dest.StartsWith(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Directory.Exists(dest))
                    Directory.Delete(dest, true);
                CopyPack(pack.Source, dest);
                StampDownloaded(dest);
                placed.Add(new DeployedPack { Kind = pack.Kind, Dest = dest, Name = pack.FolderName });
            }
            return placed;
        }

        /// <summary>Mark every info.json under a freshly downloaded pack so Package Mods skips it.</summary>
        public static void StampDownloaded(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;
            foreach (var infoPath in Directory.GetFiles(root, "info.json", SearchOption.AllDirectories))
            {
                try
                {
                    var obj = JObject.Parse(File.ReadAllText(infoPath));
                    obj["downloaded"] = true;
                    File.WriteAllText(infoPath, obj.ToString(Newtonsoft.Json.Formatting.Indented));
                }
                catch
                {
                }
            }
        }

        private static bool IsDownloaded(string folder)
        {
            var path = Path.Combine(folder, "info.json");
            if (!File.Exists(path))
                return false;
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                return obj["downloaded"]?.Value<bool>() == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// A pack is listed when it has a src folder. Characters and stages are also listed when they have a FLA folder.
        /// </summary>
        private static bool CanPackage(string folder, string type)
        {
            if (HasSrc(folder))
                return true;
            return (type == "Character" || type == "Stage") && HasFlaFolder(folder);
        }

        private static bool HasSrc(string folder) => Directory.Exists(Path.Combine(folder, "src"));

        private static bool HasFlaFolder(string folder)
        {
            try
            {
                return Directory.EnumerateDirectories(folder).Any(d =>
                    string.Equals(Path.GetFileName(d), "FLA", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<PackageModEntry> ListUserTutorials()
        {
            var docs = TutorialWikiImporter.FindDocs();
            if (docs == null)
                yield break;
            var root = Path.Combine(docs, "Dev Wiki", "Tutorials");
            if (!Directory.Exists(root))
                yield break;
            foreach (var folder in Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                if (IsDownloaded(folder))
                    continue;
                if (!File.Exists(Path.Combine(folder, "tutorial.md")))
                    continue;
                yield return Read(folder, "Tutorials", "Tutorial");
            }
        }

        private static PackageModEntry Read(string folder, string section, string fallbackType)
        {
            var folderName = Path.GetFileName(folder);
            var entry = new PackageModEntry
            {
                FolderPath = folder,
                Section = section,
                Name = folderName,
                Type = fallbackType,
                Label = section + " / " + folderName
            };
            var path = Path.Combine(folder, "info.json");
            if (!File.Exists(path))
                return entry;
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var name = obj["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    entry.Name = name;
                    entry.Label = section + " / " + name;
                }
                entry.Creator = obj["creator"]?.ToString() ?? "";
                var type = obj["type"]?.ToString();
                if (!string.IsNullOrWhiteSpace(type))
                    entry.Type = type;
                entry.Description = obj["description"]?.ToString() ?? "";
                entry.Version = obj["version"]?.ToString() ?? "";
            }
            catch
            {
            }
            return entry;
        }

        private static bool SkipInZip(string relative)
        {
            var norm = relative.Replace('/', '\\');
            if (norm.Contains("\\src\\", StringComparison.OrdinalIgnoreCase) ||
                norm.StartsWith("src\\", StringComparison.OrdinalIgnoreCase))
                return true;
            if (norm.Contains("\\decompiled\\", StringComparison.OrdinalIgnoreCase))
                return true;
            return Path.GetFileName(norm).Equals("build.bat", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class FoundPack
        {
            public string Source { get; init; } = "";
            public string FolderName { get; init; } = "";
            public string Kind { get; init; } = "";
        }

        private static List<FoundPack> FindPacks(string extractedRoot)
        {
            var found = new List<FoundPack>();
            var manifestPath = Path.Combine(extractedRoot, "ssf2mm-pack.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                    foreach (var row in manifest["mods"] ?? new JArray())
                    {
                        var folder = row?["folder"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(folder))
                            continue;
                        var source = Path.GetFullPath(Path.Combine(extractedRoot, folder));
                        if (!source.StartsWith(Path.GetFullPath(extractedRoot), StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!Directory.Exists(source))
                            continue;
                        found.Add(new FoundPack
                        {
                            Source = source,
                            FolderName = Path.GetFileName(source),
                            Kind = KindFromType(row?["type"]?.ToString(), source, extractedRoot)
                        });
                    }
                }
                catch
                {
                }
                if (found.Count > 0)
                    return found;
            }

            var infos = Directory.GetFiles(extractedRoot, "info.json", SearchOption.AllDirectories)
                .Where(p => !SkipInZip(Path.GetRelativePath(extractedRoot, p)))
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            infos = infos.Where(dir => !infos.Any(other =>
                !string.Equals(other, dir, StringComparison.OrdinalIgnoreCase) &&
                other.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var dir in infos)
            {
                string? type = null;
                string? infoName = null;
                try
                {
                    var info = JObject.Parse(File.ReadAllText(Path.Combine(dir, "info.json")));
                    type = info["type"]?.ToString();
                    infoName = info["name"]?.ToString();
                }
                catch
                {
                }
                var atRoot = string.Equals(Path.GetFullPath(dir), Path.GetFullPath(extractedRoot), StringComparison.OrdinalIgnoreCase);
                found.Add(new FoundPack
                {
                    Source = dir,
                    FolderName = atRoot
                        ? (string.IsNullOrWhiteSpace(infoName) ? "pack" : infoName)
                        : Path.GetFileName(dir),
                    Kind = KindFromType(type, dir, extractedRoot)
                });
            }
            return found;
        }

        private static string KindFromType(string? type, string source, string extractedRoot)
        {
            var rel = Path.GetRelativePath(extractedRoot, source).Replace('/', '\\');
            if (rel.Contains("\\events\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("events\\", StringComparison.OrdinalIgnoreCase))
                return "event";
            if (rel.Contains("\\stages\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("stages\\", StringComparison.OrdinalIgnoreCase))
                return "stage";
            if (rel.Contains("\\character\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("character\\", StringComparison.OrdinalIgnoreCase))
                return "character";
            if (rel.Contains("\\mods\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("mods\\", StringComparison.OrdinalIgnoreCase))
                return "mod";
            if (rel.Contains("songs\\packs", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("\\music\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("music\\", StringComparison.OrdinalIgnoreCase))
                return "music";
            if (rel.Contains("\\intros\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("intros\\", StringComparison.OrdinalIgnoreCase))
                return "intro";
            if (rel.Contains("\\tutorials\\", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("tutorials\\", StringComparison.OrdinalIgnoreCase))
                return "tutorial";

            var t = type ?? "";
            if (t.Equals("Event", StringComparison.OrdinalIgnoreCase))
                return "event";
            if (t.Contains("Music", StringComparison.OrdinalIgnoreCase))
                return "music";
            if (t.Equals("Stage", StringComparison.OrdinalIgnoreCase))
                return "stage";
            if (t.Equals("Character", StringComparison.OrdinalIgnoreCase))
                return "character";
            if (t.Equals("Tutorial", StringComparison.OrdinalIgnoreCase))
                return "tutorial";
            if (t.Equals("Intro", StringComparison.OrdinalIgnoreCase))
                return "intro";
            return "mod";
        }

        public static string? ExpansionFolder(string shellRoot, string kind) => DestParent(shellRoot, kind);

        private static string? DestParent(string shellRoot, string kind)
        {
            var expansions = Path.Combine(shellRoot, "expansions");
            return kind switch
            {
                "event" => Path.Combine(expansions, "events"),
                "music" => Path.Combine(expansions, "songs", "packs"),
                "stage" => Path.Combine(expansions, "stages"),
                "character" => Path.Combine(expansions, "character"),
                "intro" => Path.Combine(expansions, "intros"),
                "tutorial" => Path.Combine(expansions, "tutorials"),
                "mod" => Path.Combine(expansions, "mods"),
                _ => null
            };
        }

        private static void CopyPack(string source, string dest)
        {
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                if (SkipInZip(rel))
                    continue;
                var target = Path.Combine(dest, rel);
                var dir = Path.GetDirectoryName(target);
                if (dir != null)
                    Directory.CreateDirectory(dir);
                File.Copy(file, target, true);
            }
        }

        private static string UniqueFolder(string name, HashSet<string> used)
        {
            var folder = SanitizeFolder(name);
            var baseName = folder;
            var n = 2;
            while (!used.Add(folder))
                folder = baseName + "-" + n++;
            return folder;
        }

        private static string SanitizeFolder(string name)
        {
            var clean = string.Join("_", (name ?? "pack").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "pack" : clean;
        }
    }
}
