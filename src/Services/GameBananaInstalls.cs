using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Which GameBanana mods were imported into one Shell build: <c>expansions/user/gamebanana-installs.json</c>.
    /// </summary>
    public static class GameBananaInstalls
    {
        public const string FileName = "gamebanana-installs.json";

        public static string PathFor(string shellRoot) =>
            Path.Combine(ShellWorkspaceService.UserStateDirectory(shellRoot), FileName);

        public static void Record(string shellRoot, int modId, int fileId, string? name, IEnumerable<(OneClickKind Kind, string Id)> items)
        {
            if (modId <= 0 || string.IsNullOrWhiteSpace(shellRoot))
                return;
            var doc = Load(shellRoot);
            if (doc["mods"] is not JObject mods)
            {
                mods = new JObject();
                doc["mods"] = mods;
            }
            var list = items.Where(i => !string.IsNullOrWhiteSpace(i.Id)).ToList();
            mods[modId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JObject
            {
                ["fileId"] = fileId,
                ["name"] = name ?? "",
                ["at"] = DateTime.UtcNow.ToString("o"),
                ["items"] = new JArray(list.Select(i => new JObject { ["kind"] = i.Kind.ToString(), ["id"] = i.Id }))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor(shellRoot))!);
            File.WriteAllText(PathFor(shellRoot), doc.ToString(Formatting.Indented));
            DebugLogger.Log($"[gbinstalls] record mod={modId} items={list.Count}");
        }

        /// <summary>Mod ids whose recorded items still exist in this build.</summary>
        public static HashSet<int> InstalledModIds(string? shellRoot)
        {
            var ids = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(shellRoot) || !Directory.Exists(shellRoot))
                return ids;
            if (Load(shellRoot)["mods"] is not JObject mods)
                return ids;
            foreach (var prop in mods.Properties())
            {
                if (!int.TryParse(prop.Name, out var modId))
                    continue;
                var items = (prop.Value["items"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                if (items.Count == 0)
                {
                    ids.Add(modId);
                    continue;
                }
                if (items.Any(i => ItemExists(shellRoot, (string?)i["kind"], (string?)i["id"])))
                    ids.Add(modId);
            }
            DebugLogger.Log($"[gbinstalls] installed={ids.Count} shell={shellRoot}");
            return ids;
        }

        public static bool ItemExists(string shellRoot, string? kind, string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
            return kind switch
            {
                nameof(OneClickKind.Stage) => Directory.Exists(Path.Combine(ShellWorkspaceService.StageExpansionsDirectory(shellRoot), id)),
                nameof(OneClickKind.Character) => Directory.Exists(Path.Combine(ShellWorkspaceService.CharacterExpansionsDirectory(shellRoot), id)),
                nameof(OneClickKind.Song) => File.Exists(Path.Combine(shellRoot, "data", "sound", id + ".swf")),
                _ => Directory.Exists(Path.Combine(ShellWorkspaceService.ExpansionsRoot(shellRoot), id))
            };
        }

        private static JObject Load(string shellRoot)
        {
            var path = PathFor(shellRoot);
            try
            {
                if (File.Exists(path))
                    return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[gbinstalls] read failed " + ex.Message);
            }
            return new JObject();
        }
    }
}
