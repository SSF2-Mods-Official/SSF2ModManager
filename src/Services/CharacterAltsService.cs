using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Alternate files for a character the build already has: <c>expansions/alts/alts.json</c>.
    /// The engine (<c>CharacterAlts.as</c>) loads them in place of the stock files when jump picks them on Character Select.
    /// </summary>
    public static class CharacterAltsService
    {
        public static string AltsRoot(string shellRoot) =>
            Path.Combine(ShellWorkspaceService.ExpansionsRoot(shellRoot), "alts");

        public static string IndexPath(string shellRoot) => Path.Combine(AltsRoot(shellRoot), "alts.json");

        /// <summary>
        /// Copies the alt's files to <c>expansions/alts/&lt;gb id&gt;/&lt;character&gt;/&lt;name&gt;/</c> and lists it in alts.json.
        /// Returns the folder relative to <c>expansions</c>.
        /// </summary>
        public static string Install(string shellRoot, int gameBananaId, string characterId, string? name,
            IReadOnlyList<(string ResourceId, string Path)> files, string? sourceFolderName = null)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("An alt needs a character id.", nameof(characterId));
            if (files.Count == 0)
                throw new ArgumentException("An alt needs at least one file.", nameof(files));
            var group = gameBananaId > 0 ? gameBananaId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "local";
            var folderName = SafeFolder(!string.IsNullOrWhiteSpace(sourceFolderName) ? sourceFolderName!
                : !string.IsNullOrWhiteSpace(name) ? name! : characterId + " alt");
            var dest = Path.Combine(AltsRoot(shellRoot), group, SafeFolder(characterId), folderName);
            Directory.CreateDirectory(dest);

            var map = new JObject();
            foreach (var (resourceId, source) in files)
            {
                var target = Path.Combine(dest, Path.GetFileName(source));
                File.Copy(source, target, overwrite: true);
                map[resourceId] = Path.GetRelativePath(shellRoot, target).Replace('\\', '/');
            }

            var doc = Load(shellRoot);
            if (doc["characters"] is not JObject characters)
            {
                characters = new JObject();
                doc["characters"] = characters;
            }
            if (characters[characterId] is not JArray list)
            {
                list = new JArray();
                characters[characterId] = list;
            }
            foreach (var old in list.OfType<JObject>()
                         .Where(e => (string?)e["folder"] == RelativeToExpansions(shellRoot, dest))
                         .ToList())
                list.Remove(old);
            var entry = new JObject
            {
                ["folder"] = RelativeToExpansions(shellRoot, dest),
                ["gamebanana"] = gameBananaId,
                ["files"] = map
            };
            if (!string.IsNullOrWhiteSpace(name))
                entry["name"] = name;
            list.Add(entry);
            Directory.CreateDirectory(AltsRoot(shellRoot));
            File.WriteAllText(IndexPath(shellRoot), doc.ToString(Formatting.Indented));
            DebugLogger.Log($"[alts] install character={characterId} alt={name} files={files.Count}");
            return RelativeToExpansions(shellRoot, dest);
        }

        /// <summary>The alt's name, or null when the folder only repeats the character (the engine shows "X Alt N").</summary>
        public static string? NameFromFolder(string folderName, string? characterName)
        {
            var name = (folderName ?? "").Trim();
            if (name.Length == 0 || Regex.IsMatch(name, @"^DAT\d+$", RegexOptions.IgnoreCase))
                return null;
            if (!string.IsNullOrWhiteSpace(characterName) && name.Equals(characterName.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;
            return name;
        }

        public static int Count(string shellRoot, string characterId) =>
            (Load(shellRoot)["characters"]?[characterId] as JArray)?.Count ?? 0;

        private static string RelativeToExpansions(string shellRoot, string folder) =>
            Path.GetRelativePath(ShellWorkspaceService.ExpansionsRoot(shellRoot), folder).Replace('\\', '/');

        private static string SafeFolder(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return clean.Length == 0 ? "alt" : clean;
        }

        private static JObject Load(string shellRoot)
        {
            var path = IndexPath(shellRoot);
            try
            {
                if (File.Exists(path))
                    return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[alts] read failed " + ex.Message);
            }
            return new JObject();
        }
    }
}
