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
    }

    public class EventInfoDraft
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Creator { get; set; } = "";
        public string ClassName { get; set; } = "";
        public bool ChooseCharacter { get; set; }
    }

    /// <summary>
    /// Copies an event or the template, writes eventinfo, renames the class, and compiles with mxmlc.
    /// </summary>
    public class EventAuthorService
    {
        public static List<EventSourceChoice> ListSources(string shellRoot)
        {
            var list = new List<EventSourceChoice>();
            var eventsRoot = Path.Combine(shellRoot, "expansions", "events");
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

        public static string SuggestClassName(string eventId)
        {
            var parts = Regex.Split(eventId ?? "", @"[^A-Za-z0-9]+")
                .Where(p => p.Length > 0)
                .Select(p => char.ToUpperInvariant(p[0]) + p[1..]);
            var body = string.Concat(parts);
            if (string.IsNullOrEmpty(body)) body = "NewEvent";
            if (char.IsDigit(body[0])) body = "E" + body;
            return "EventMode_" + body;
        }

        public static string CreateEvent(string shellRoot, string sourceAsPath, string packFolder, EventInfoDraft draft)
        {
            if (!File.Exists(sourceAsPath))
                throw new FileNotFoundException("Event source was not found.", sourceAsPath);
            var className = string.IsNullOrWhiteSpace(draft.ClassName)
                ? SuggestClassName(draft.Id)
                : draft.ClassName.Trim();
            if (!className.StartsWith("EventMode_", StringComparison.Ordinal))
                className = "EventMode_" + className;
            var id = Regex.Replace(draft.Id ?? "", @"[^A-Za-z0-9_]", "");
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Event id needs letters or numbers.");
            var folder = string.IsNullOrWhiteSpace(packFolder) ? draft.Name : packFolder;
            folder = string.Join("_", (folder ?? "Event").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrWhiteSpace(folder)) folder = id;

            var destDir = Path.Combine(shellRoot, "expansions", "events", folder);
            var srcDir = Path.Combine(destDir, "src");
            Directory.CreateDirectory(srcDir);

            var sourceText = File.ReadAllText(sourceAsPath);
            var oldClass = Regex.Match(sourceText, @"class\s+(EventMode_[A-Za-z0-9_]+)");
            var rewritten = oldClass.Success
                ? sourceText.Replace(oldClass.Groups[1].Value, className)
                : sourceText;
            rewritten = InjectEventInfo(rewritten, className, id, draft);
            File.WriteAllText(Path.Combine(srcDir, className + ".as"), rewritten, Encoding.UTF8);

            var packClass = "EventPack_" + Regex.Replace(folder, @"[^A-Za-z0-9_]", "");
            if (char.IsDigit(packClass.Last())) { }
            File.WriteAllText(Path.Combine(srcDir, "EventPack.as"), EventPackBase, Encoding.UTF8);
            File.WriteAllText(Path.Combine(srcDir, packClass + ".as"), PackSource(packClass, className, id, draft), Encoding.UTF8);

            var info = new JObject
            {
                ["name"] = string.IsNullOrWhiteSpace(draft.Name) ? id : draft.Name.Trim(),
                ["creator"] = draft.Creator ?? "",
                ["description"] = draft.Description ?? "",
                ["type"] = "Event",
                ["enabled"] = true
            };
            File.WriteAllText(Path.Combine(destDir, "info.json"), info.ToString(Formatting.Indented));
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
            var api = FindApi();
            if (mxmlc == null)
                return (false, "mxmlc was not found. Expected Next2Flash toolchain\\aflex_lite\\bin\\mxmlc.bat or C:\\aflex_sdk\\bin\\mxmlc.bat.");
            if (api == null)
                return (false, "AS3 API folder was not found next to mxmlc (aflex_lite\\api).");

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

        private static string InjectEventInfo(string source, string className, string id, EventInfoDraft draft)
        {
            var block = "\t\tpublic var eventinfo:Array = [\r\n" + EventObject(className, id, draft, "\t\t\t") + "\r\n\t\t];";
            var replaced = Regex.Replace(
                source,
                @"public\s+var\s+eventinfo\s*:\s*Array\s*=\s*\[[\s\S]*?\];",
                block.TrimStart('\t'));
            if (replaced != source)
                return replaced;
            var ctor = Regex.Match(source, @"public\s+function\s+" + Regex.Escape(className));
            if (!ctor.Success)
                return source;
            return source.Insert(ctor.Index, block + "\r\n\r\n");
        }

        private static string EventObject(string className, string id, EventInfoDraft draft, string indent)
        {
            string Q(string? s) => JsonConvert.SerializeObject(s ?? "");
            return indent + "{\r\n"
                + indent + "\t\"id\": " + Q(id) + ",\r\n"
                + indent + "\t\"classAPI\": " + className + ",\r\n"
                + indent + "\t\"name\": " + Q(draft.Name) + ",\r\n"
                + indent + "\t\"description\": " + Q(draft.Description) + ",\r\n"
                + indent + "\t\"creator\": " + Q(draft.Creator) + ",\r\n"
                + indent + "\t\"chooseCharacter\": " + (draft.ChooseCharacter ? "true" : "false") + "\r\n"
                + indent + "}";
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

        private static string? FindMxmlc()
        {
            var sdk = @"C:\aflex_sdk\bin\mxmlc.bat";
            if (File.Exists(sdk)) return sdk;
            var lite = ModdingToolchainService.ResolveAflexLite();
            if (lite != null)
            {
                var bat = Path.Combine(lite, "bin", "mxmlc.bat");
                if (File.Exists(bat)) return bat;
            }
            return null;
        }

        private static string? FindApi()
        {
            var lite = ModdingToolchainService.ResolveAflexLite();
            if (lite != null)
            {
                var api = Path.Combine(lite, "api");
                if (Directory.Exists(api)) return api;
            }
            var sdk = @"C:\aflex_sdk\frameworks";
            if (Directory.Exists(sdk)) return sdk;
            return null;
        }
    }
}
