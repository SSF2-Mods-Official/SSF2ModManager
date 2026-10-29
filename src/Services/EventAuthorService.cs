using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public class EventSourceChoice
    {
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public string ClassName { get; set; } = "";
    }

    public class EventInfoDraft
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Creator { get; set; } = "";
        public string ClassName { get; set; } = "";
        public bool ChooseCharacter { get; set; }
        /// <summary>Png to copy to <c>previews/&lt;id&gt;.png</c>. Empty writes a placeholder.</summary>
        public string PreviewPath { get; set; } = "";
        /// <summary>Stage id, or <c>pack:</c> plus a stage-background pack id. Empty keeps the stage picture.</summary>
        public string StageBackground { get; set; } = "";
        /// <summary>Menu portrait when the event picks the character. Empty skips the portrait.</summary>
        public string PreviewCharacter { get; set; } = "";
        /// <summary>Costume on that portrait. <c>-1</c> is the base costume.</summary>
        public int PreviewCostume { get; set; } = -1;
        public List<int> Dependencies { get; set; } = new();
    }

    public class EventIdentity
    {
        public string Id { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Folder { get; set; } = "";
    }

    /// <summary>
    /// Copies an event or the template, writes the pack list, renames the class, and compiles with mxmlc.
    /// </summary>
    public class EventAuthorService
    {
        public static List<EventSourceChoice> ListSources(string shellRoot)
        {
            var list = ListVanillaEvents(shellRoot);
            var eventsRoot = Path.Combine(shellRoot ?? "", "expansions", "events");
            var template = Path.Combine(eventsRoot, "_template", "EventMode_TemplateBout.as");
            if (File.Exists(template))
                list.Add(new EventSourceChoice { Label = "Template", Path = template });
            if (!Directory.Exists(eventsRoot))
                return list;
            foreach (var dir in Directory.GetDirectories(eventsRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('_') || name.StartsWith('.'))
                    continue;
                var src = Path.Combine(dir, "src");
                if (!Directory.Exists(src)) continue;
                foreach (var file in Directory.GetFiles(src, "EventMode_*.as"))
                {
                    list.Add(new EventSourceChoice
                    {
                        Label = name + " / " + Path.GetFileNameWithoutExtension(file),
                        Path = file
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// One row per <c>EventMode&lt;number&gt;_&lt;id&gt;</c> class in <c>data/modes/event_mode.swf</c> or <c>.ssf</c>.
        /// The label is the event menu name from <see cref="ShellConfigureLaunchService.ListEvents"/>.
        /// </summary>
        public static List<EventSourceChoice> ListVanillaEvents(string shellRoot)
        {
            var list = new List<EventSourceChoice>();
            if (string.IsNullOrWhiteSpace(shellRoot))
                return list;
            var path = ShellConfigureLaunchService.ResolveModeFile(shellRoot, "event_mode.swf");
            if (string.IsNullOrWhiteSpace(path))
                return list;

            var labels = ShellConfigureLaunchService.ListEvents(shellRoot)
                .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var text in ReadPrintableStrings(File.ReadAllBytes(path)))
            {
                var match = VanillaClassName.Match(text);
                if (!match.Success)
                    continue;
                byId[match.Groups[3].Value] = text;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in ShellConfigureLaunchService.ListEvents(shellRoot))
            {
                if (!byId.TryGetValue(ev.Id, out var className) || !seen.Add(className))
                    continue;
                list.Add(new EventSourceChoice
                {
                    Label = ev.Label,
                    ClassName = className
                });
            }
            foreach (var pair in byId.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(pair.Value))
                    continue;
                var label = labels.TryGetValue(pair.Key, out var menu) ? menu : pair.Value;
                list.Add(new EventSourceChoice { Label = label, ClassName = pair.Value });
            }
            return list;
        }

        private static readonly Regex VanillaClassName = new(
            @"^EventMode(\d+)(Special)?_([A-Za-z][A-Za-z0-9]*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static List<string> ReadPrintableStrings(byte[] data)
        {
            var strings = new List<string>();
            var i = 0;
            while (i < data.Length - 2)
            {
                var n = data[i];
                if (n is >= 3 and <= 90 && i + 1 + n <= data.Length)
                {
                    var ok = true;
                    for (var j = 0; j < n; j++)
                    {
                        var b = data[i + 1 + j];
                        if (b < 32 || b > 126)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                    {
                        strings.Add(Encoding.ASCII.GetString(data, i + 1, n));
                        i += 1 + n;
                        continue;
                    }
                }
                i++;
            }
            return strings;
        }

        public static List<EventSourceChoice> ListPacks(string shellRoot)
        {
            var list = new List<EventSourceChoice>();
            var eventsRoot = Path.Combine(shellRoot, "expansions", "events");
            if (!Directory.Exists(eventsRoot))
                return list;
            foreach (var dir in Directory.GetDirectories(eventsRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('_') || name.StartsWith('.'))
                    continue;
                var src = Path.Combine(dir, "src");
                if (!Directory.Exists(src))
                    continue;
                var pack = Directory.GetFiles(src, "EventPack_*.as").FirstOrDefault();
                if (pack == null) continue;
                list.Add(new EventSourceChoice { Label = name, Path = dir });
            }
            return list;
        }

        public static string? PackScript(string packDir)
        {
            var src = Path.Combine(packDir ?? "", "src");
            if (!Directory.Exists(src))
                return null;
            return Directory.GetFiles(src, "EventMode_*.as").FirstOrDefault();
        }

        public static string PackEventId(string packDir)
        {
            var infoPath = Path.Combine(packDir ?? "", "info.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    var id = JObject.Parse(File.ReadAllText(infoPath))["id"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }
                catch
                {
                    /* info.json */
                }
            }
            var script = PackScript(packDir ?? "");
            if (script == null)
                return "";
            var match = Regex.Match(File.ReadAllText(script), @"""id""\s*:\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : "";
        }

        public static void UpdatePack(string packDir, string source, EventInfoDraft draft)
        {
            var script = PackScript(packDir) ?? throw new FileNotFoundException("Event script was not found.", packDir);
            File.WriteAllText(script, StripEventInfo(source), Encoding.UTF8);
            var infoPath = Path.Combine(packDir, "info.json");
            var info = File.Exists(infoPath) ? JObject.Parse(File.ReadAllText(infoPath)) : new JObject();
            info["name"] = draft.Name ?? "";
            info["description"] = draft.Description ?? "";
            info["creator"] = draft.Creator ?? "";
            if (!string.IsNullOrWhiteSpace(draft.Id))
                info["id"] = draft.Id.Trim();
            if (string.IsNullOrWhiteSpace(draft.StageBackground))
                info.Remove("stageBackground");
            else
                info["stageBackground"] = draft.StageBackground.Trim();
            var id = info["id"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(id))
                id = Path.GetFileName(packDir);
            var src = Path.GetDirectoryName(script) ?? Path.Combine(packDir, "src");
            Directory.CreateDirectory(src);
            var packFile = Directory.GetFiles(src, "EventPack_*.as")
                .FirstOrDefault(f => !Path.GetFileName(f).Equals("EventPack.as", StringComparison.OrdinalIgnoreCase));
            var packClass = packFile == null
                ? "EventPack_" + Regex.Replace(Path.GetFileName(packDir), @"[^A-Za-z0-9_]", "")
                : Path.GetFileNameWithoutExtension(packFile);
            File.WriteAllText(
                packFile ?? Path.Combine(src, packClass + ".as"),
                PackSource(packClass, Path.GetFileNameWithoutExtension(script), id, draft),
                Encoding.UTF8);
            File.WriteAllText(infoPath, info.ToString(Formatting.Indented));
        }

        public static string SuggestId(string? name)
        {
            var parts = Regex.Split(name ?? "", @"[^A-Za-z0-9]+")
                .Where(p => p.Length > 0)
                .Select(p => char.ToUpperInvariant(p[0]) + p[1..]);
            var body = string.Concat(parts);
            if (string.IsNullOrEmpty(body)) return "";
            if (char.IsDigit(body[0])) body = "E" + body;
            return body;
        }

        public static string SuggestClassName(string eventId)
        {
            var body = SuggestId(eventId);
            if (string.IsNullOrEmpty(body)) body = "NewEvent";
            return "EventMode_" + body;
        }

        /// <summary>
        /// Id, class, and pack folder from a display name. A taken folder uses the next number on all three.
        /// </summary>
        public static EventIdentity IdentityFromName(string shellRoot, string? name)
        {
            var id = SuggestId(name);
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Event name needs letters or numbers.");
            var folder = id;
            var n = 2;
            var root = Path.Combine(shellRoot ?? "", "expansions", "events");
            while (!string.IsNullOrWhiteSpace(shellRoot) && Directory.Exists(Path.Combine(root, folder)))
            {
                folder = id + n;
                n++;
            }
            return new EventIdentity
            {
                Id = folder,
                ClassName = "EventMode_" + folder,
                Folder = folder
            };
        }

        public static string? FindExportedScript(string exportDir, string className)
        {
            if (string.IsNullOrWhiteSpace(exportDir) || string.IsNullOrWhiteSpace(className) || !Directory.Exists(exportDir))
                return null;
            var name = className + ".as";
            var direct = Path.Combine(exportDir, "scripts", name);
            if (File.Exists(direct))
                return direct;
            return Directory.EnumerateFiles(exportDir, name, SearchOption.AllDirectories).FirstOrDefault();
        }

        /// <summary>Test hook. Empty uses AppData <c>event-scripts</c>.</summary>
        public static string? EventScriptCacheRootOverride { get; set; }

        public static string VanillaScriptCacheFile(string swfPath, string className)
        {
            var info = new FileInfo(swfPath);
            var stamp = info.Length + "-" + info.LastWriteTimeUtc.Ticks;
            var root = string.IsNullOrWhiteSpace(EventScriptCacheRootOverride)
                ? Path.Combine(AppPaths.AppDataDir, "event-scripts")
                : EventScriptCacheRootOverride;
            return Path.Combine(root, stamp, className + ".as");
        }

        public static async Task<string> ExportVanillaSourceAsync(string shellRoot, string className, CancellationToken cancellationToken = default)
        {
            var name = className ?? "";
            if (!VanillaClassName.IsMatch(name))
                throw new ArgumentException("That is not a vanilla event class.");
            var swf = ShellConfigureLaunchService.ResolveModeFile(shellRoot ?? "", "event_mode.swf")
                ?? throw new FileNotFoundException("event_mode.swf was not found.", Path.Combine(shellRoot ?? "", "data", "modes", "event_mode.swf"));
            var cached = VanillaScriptCacheFile(swf, name);
            if (File.Exists(cached))
                return CopyScript(cached, name);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-event-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var jpexs = new JpexsCliService();
                await jpexs.RunFfdecAsync(
                    "",
                    $"-selectclass {name} -export script \"{work}\" \"{swf}\"",
                    cancellationToken,
                    TimeSpan.FromSeconds(60));
                var file = FindExportedScript(work, name)
                    ?? throw new FileNotFoundException(name + ".as was not exported.");
                RememberVanillaScript(cached, file);
                return CopyScript(file, name);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* temp export */ }
            }
        }

        private static string CopyScript(string source, string className)
        {
            var copy = Path.Combine(Path.GetTempPath(), className + "-" + Guid.NewGuid().ToString("N") + ".as");
            File.Copy(source, copy, overwrite: true);
            return copy;
        }

        private static void RememberVanillaScript(string cached, string exportedFile)
        {
            var dir = Path.GetDirectoryName(cached);
            if (string.IsNullOrWhiteSpace(dir))
                return;
            Directory.CreateDirectory(dir);
            File.Copy(exportedFile, cached, overwrite: true);
            var root = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;
            foreach (var old in Directory.GetDirectories(root))
            {
                if (string.Equals(old, dir, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(old, true); } catch { /* old stamp */ }
            }
        }

        public static async Task<string> ExportEventScriptsAsync(string shellRoot, CancellationToken cancellationToken = default)
        {
            var swf = ShellConfigureLaunchService.ResolveModeFile(shellRoot ?? "", "event_mode.swf")
                ?? throw new FileNotFoundException("event_mode.swf was not found.", Path.Combine(shellRoot ?? "", "data", "modes", "event_mode.swf"));
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-events-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var jpexs = new JpexsCliService();
            await jpexs.RunFfdecAsync(
                "",
                $"-export script \"{work}\" \"{swf}\"",
                cancellationToken,
                TimeSpan.FromSeconds(120));
            return work;
        }

        public static async Task<string> CreateFromVanillaAsync(string shellRoot, string className, EventInfoDraft draft, CancellationToken cancellationToken = default)
        {
            var source = await ExportVanillaSourceAsync(shellRoot, className, cancellationToken);
            try
            {
                return CreateEvent(shellRoot, source, "", draft);
            }
            finally
            {
                try { File.Delete(source); } catch { /* temp script */ }
            }
        }

        public static string CreateEvent(string shellRoot, string sourceAsPath, string packFolder, EventInfoDraft draft)
        {
            if (!File.Exists(sourceAsPath))
                throw new FileNotFoundException("Event source was not found.", sourceAsPath);
            string id;
            string className;
            string folder;
            if (string.IsNullOrWhiteSpace(draft.Id))
            {
                var ident = IdentityFromName(shellRoot, draft.Name);
                id = ident.Id;
                className = string.IsNullOrWhiteSpace(draft.ClassName) ? ident.ClassName : draft.ClassName.Trim();
                folder = string.IsNullOrWhiteSpace(packFolder) ? ident.Folder : packFolder;
            }
            else
            {
                className = string.IsNullOrWhiteSpace(draft.ClassName)
                    ? SuggestClassName(draft.Id)
                    : draft.ClassName.Trim();
                id = Regex.Replace(draft.Id ?? "", @"[^A-Za-z0-9_]", "");
                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentException("Event id needs letters or numbers.");
                folder = string.IsNullOrWhiteSpace(packFolder) ? draft.Name : packFolder;
            }
            if (!className.StartsWith("EventMode_", StringComparison.Ordinal))
                className = "EventMode_" + className;
            folder = string.Join("_", (folder ?? "Event").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrWhiteSpace(folder)) folder = id;

            var destDir = Path.Combine(shellRoot, "expansions", "events", folder);
            var srcDir = Path.Combine(destDir, "src");
            Directory.CreateDirectory(srcDir);

            var sourceText = File.ReadAllText(sourceAsPath);
            var oldClass = Regex.Match(sourceText, @"class\s+(EventMode[A-Za-z0-9_]*)");
            var rewritten = oldClass.Success
                ? sourceText.Replace(oldClass.Groups[1].Value, className)
                : sourceText;
            rewritten = StripEventInfo(rewritten);
            File.WriteAllText(Path.Combine(srcDir, className + ".as"), rewritten, Encoding.UTF8);

            var packClass = "EventPack_" + Regex.Replace(folder, @"[^A-Za-z0-9_]", "");
            if (char.IsDigit(packClass.Last())) { }
            File.WriteAllText(Path.Combine(srcDir, "EventPack.as"), EventPackBase, Encoding.UTF8);
            File.WriteAllText(Path.Combine(srcDir, packClass + ".as"), PackSource(packClass, className, id, draft), Encoding.UTF8);

            var info = new JObject
            {
                ["id"] = id,
                ["name"] = string.IsNullOrWhiteSpace(draft.Name) ? id : draft.Name.Trim(),
                ["creator"] = draft.Creator ?? "",
                ["description"] = draft.Description ?? "",
                ["type"] = "Event",
                ["enabled"] = true
            };
            if (!string.IsNullOrWhiteSpace(draft.StageBackground))
                info["stageBackground"] = draft.StageBackground.Trim();
            ModDependencies.Write(info, draft.Dependencies?.ToArray() ?? []);
            File.WriteAllText(Path.Combine(destDir, "info.json"), info.ToString(Formatting.Indented));
            EventPreview.Write(destDir, id, draft.PreviewPath);
            return destDir;
        }

        public static async Task<(bool Ok, string Log)> CompileAsync(string packDir)
        {
            var src = Path.Combine(packDir, "src");
            var pack = Directory.GetFiles(src, "EventPack_*.as")
                .FirstOrDefault(f => !Path.GetFileName(f).Equals("EventPack.as", StringComparison.OrdinalIgnoreCase));
            if (pack == null)
                return (false, "No EventPack_*.as in " + src);

            var mxmlc = FindMxmlc();
            if (mxmlc == null)
                return (false, "mxmlc was not found in Next2Flash (toolchain\\aflex_lite\\bin\\mxmlc.bat).");
            var shell = ShellRootFromPack(packDir);
            var swf = shell == null ? null : ShellConfigureLaunchService.ResolveModeFile(shell, "event_mode.swf");
            if (string.IsNullOrWhiteSpace(swf))
                return (false, "event_mode.swf was not found. Build SSF2 Shell first.");
            var api = await VanillaApiDirAsync(shell!, swf);
            if (api == null)
                return (false, "SSF2CustomMatch.as was not in the scripts exported from event_mode.swf.");

            var swfName = Path.GetFileName(packDir).Replace(" ", "") + ".swf";
            var output = Path.Combine(packDir, swfName);
            var args = $"/c \"\"{mxmlc}\" -strict=false -warnings=false -source-path+=\"{api}\" -source-path+=src -output \"{output}\" \"src\\{Path.GetFileName(pack)}\"\"";
            var psi = new ProcessStartInfo("cmd.exe", args)
            {
                WorkingDirectory = packDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start mxmlc.");
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var log = (stdout + "\n" + stderr).Trim();
            if (proc.ExitCode == 0 && File.Exists(output))
                return (true, "Compiled " + output + (log.Length > 0 ? "\n\n" + log : ""));
            return (false, string.IsNullOrWhiteSpace(log) ? "mxmlc exited " + proc.ExitCode : log);
        }

        /// <summary>The object <c>buildEventList</c> returns. The game registers this list.</summary>
        public static string PackList(string className, string id, EventInfoDraft draft) =>
            EventObject(className, id, draft, "\t\t\t\t");

        private static string StripEventInfo(string source)
        {
            return Regex.Replace(
                source,
                @"public\s+var\s+eventinfo\s*:\s*Array\s*=\s*\[[\s\S]*?\];\s*",
                "",
                RegexOptions.CultureInvariant);
        }

        private static string EventObject(string className, string id, EventInfoDraft draft, string indent)
        {
            string Q(string? s) => JsonConvert.SerializeObject(s ?? "");
            var lines = new List<string>
            {
                indent + "\t\"id\": " + Q(id),
                indent + "\t\"classAPI\": " + className,
                indent + "\t\"name\": " + Q(draft.Name),
                indent + "\t\"description\": " + Q(draft.Description),
                indent + "\t\"creator\": " + Q(draft.Creator),
                indent + "\t\"chooseCharacter\": " + (draft.ChooseCharacter ? "true" : "false")
            };
            var bg = draft.StageBackground?.Trim() ?? "";
            if (bg.Length > 0 && !bg.StartsWith("pack:", StringComparison.Ordinal))
                lines.Add(indent + "\t\"stageBackground\": " + Q(bg));
            var preview = draft.PreviewCharacter?.Trim() ?? "";
            if (!draft.ChooseCharacter && preview.Length > 0 && !preview.Equals("(player)", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(indent + "\t\"previewCharacter\": " + Q(preview));
                lines.Add(indent + "\t\"previewCostume\": " + draft.PreviewCostume);
            }
            return indent + "{\r\n" + string.Join(",\r\n", lines) + "\r\n" + indent + "}";
        }

        private static string PackSource(string packClass, string modeClass, string id, EventInfoDraft draft) =>
            "package\r\n{\r\n"
            + "\tpublic class " + packClass + " extends EventPack\r\n"
            + "\t{\r\n"
            + "\t\tpublic function " + packClass + "()\r\n"
            + "\t\t{\r\n\t\t\tsuper();\r\n\t\t}\r\n\r\n"
            + "\t\toverride protected function buildEventList():Array\r\n"
            + "\t\t{\r\n\t\t\treturn [\r\n"
            + EventObject(modeClass, id, draft, "\t\t\t\t") + "\r\n"
            + "\t\t\t];\r\n\t\t}\r\n\t}\r\n}\r\n";

        private const string EventPackBase =
            "package\r\n{\r\n\timport flash.display.MovieClip;\r\n\r\n"
            + "\tpublic class EventPack extends MovieClip\r\n\t{\r\n"
            + "\t\tpublic var eventList2:Array;\r\n\r\n"
            + "\t\tpublic function EventPack()\r\n\t\t{\r\n\t\t\tsuper();\r\n\t\t\teventList2 = buildEventList();\r\n\t\t}\r\n\r\n"
            + "\t\tpublic function initAPI(engineAPI:*):Class\r\n\t\t{\r\n\t\t\treturn SSF2API.init(engineAPI);\r\n\t\t}\r\n\r\n"
            + "\t\tpublic function deinitAPI():void\r\n\t\t{\r\n\t\t\tSSF2API.deinit();\r\n\t\t}\r\n\r\n"
            + "\t\tprotected function buildEventList():Array\r\n\t\t{\r\n\t\t\treturn [];\r\n\t\t}\r\n\t}\r\n}\r\n";

        /// <summary>Shell folder that holds <c>data/modes/event_mode.swf</c> or <c>.ssf</c> above this pack, or null.</summary>
        public static string? ShellRootFromPack(string? packDir)
        {
            var dir = packDir;
            for (var i = 0; i < 6 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                if (!string.IsNullOrWhiteSpace(ShellConfigureLaunchService.ResolveModeFile(dir, "event_mode.swf")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>Directory under an FFDec script export that contains <c>SSF2CustomMatch.as</c>.</summary>
        public static string? FindApi(string? exportRoot)
        {
            if (string.IsNullOrWhiteSpace(exportRoot) || !Directory.Exists(exportRoot))
                return null;
            var hit = Directory.EnumerateFiles(exportRoot, "SSF2CustomMatch.as", SearchOption.AllDirectories).FirstOrDefault();
            return hit == null ? null : Path.GetDirectoryName(hit);
        }

        private static async Task<string?> VanillaApiDirAsync(string shellRoot, string swf)
        {
            var cache = Path.GetDirectoryName(VanillaScriptCacheFile(swf, "_"))!;
            var found = FindApi(cache);
            if (found != null)
                return found;
            var work = await ExportEventScriptsAsync(shellRoot);
            try
            {
                CopyTree(work, cache);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* temp export */ }
            }
            return FindApi(cache);
        }

        private static void CopyTree(string source, string dest)
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dest, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        private static string? FindMxmlc()
        {
            var lite = ModdingToolchainService.ResolveAflexLite();
            if (lite == null)
                return null;
            var bat = Path.Combine(lite, "bin", "mxmlc.bat");
            return File.Exists(bat) ? bat : null;
        }
    }
}
