using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Compile targets for the Next2Flash page: the decompiled engine2 folder,
    /// the built-in packs under src/mods, and each mod under expansions/mods that has a src folder.
    /// Linked-SWF libraries are written once beside the decompiled scripts.
    /// </summary>
    public static class FolderCompileService
    {
        public sealed class CompileTarget
        {
            public string Label { get; init; } = "";
            public string Id { get; init; } = "";
            public bool Engine { get; init; }
            public string Folder { get; init; } = "";
        }

        public static IReadOnlyList<string> ListDataFolders(string shell)
        {
            return JpexsCliService.ListDataFolderSwfs(shell)
                .Select(swf =>
                {
                    var slash = swf.Relative.IndexOf('/');
                    return slash < 0 ? "." : swf.Relative[..slash];
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<(string Relative, string FullPath)> ListSwfsInFolder(string shell, string folder)
        {
            return JpexsCliService.ListDataFolderSwfs(shell)
                .Where(swf => folder == "."
                    ? !swf.Relative.Contains('/')
                    : swf.Relative.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static bool IsEngineFolder(string folder) =>
            string.Equals(folder, "engine", StringComparison.OrdinalIgnoreCase);

        public static string EngineDecompileDirectory(string shell, string swfPath)
        {
            var stem = Path.GetFileNameWithoutExtension(swfPath);
            if (string.IsNullOrWhiteSpace(stem))
                stem = "engine";
            return Path.Combine(ShellWorkspaceService.DecompiledExpansionsDirectory(shell), stem);
        }

        public static async Task<string> DecompileSwfAsync(
            string swfPath, string dest, IProgress<string>? progress = null)
        {
            var held = HoldExterns(dest);
            if (Directory.Exists(dest))
            {
                try { Directory.Delete(dest, recursive: true); }
                catch { /* overwrite what we can */ }
            }
            Directory.CreateDirectory(dest);
            progress?.Report("Decompiling " + Path.GetFileName(swfPath) + "…");
            try
            {
                await RunN2fAsync("decompile \"" + swfPath + "\" --all -d \"" + dest + "\"", progress);
            }
            finally
            {
                RestoreExterns(dest, held);
            }
            return dest;
        }

        private static string? HoldExterns(string dest)
        {
            var externs = Path.Combine(dest, "externs");
            if (!Directory.Exists(externs))
                return null;
            var held = Path.Combine(Path.GetTempPath(), "ssf2mm-externs-" + Guid.NewGuid().ToString("N"));
            Directory.Move(externs, held);
            return held;
        }

        private static void RestoreExterns(string dest, string? held)
        {
            if (held == null)
                return;
            var externs = Path.Combine(dest, "externs");
            if (Directory.Exists(externs))
                Directory.Delete(externs, recursive: true);
            Directory.Move(held, externs);
        }

        public static IReadOnlyList<CompileTarget> ListCompileTargets(string shell)
        {
            var list = new List<CompileTarget>();
            var engine2 = Path.Combine(ShellWorkspaceService.DecompiledExpansionsDirectory(shell), "engine2");
            if (Directory.Exists(engine2))
            {
                list.Add(new CompileTarget
                {
                    Label = "engine2",
                    Id = "engine2",
                    Engine = true,
                    Folder = engine2
                });
            }

            AddModTargets(list, ShellWorkspaceService.BuiltInModSourceDirectory(shell ?? ""), "Built-in: ");
            AddModTargets(list, Path.Combine(shell ?? "", "expansions", "mods"), "");
            return list;
        }

        private static void AddModTargets(List<CompileTarget> list, string root, string labelPrefix)
        {
            if (!Directory.Exists(root))
                return;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("template", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith('_') || name.StartsWith('.'))
                    continue;
                if (!Directory.Exists(Path.Combine(dir, "src")) || !File.Exists(Path.Combine(dir, "asconfig.json")))
                    continue;
                list.Add(new CompileTarget
                {
                    Label = labelPrefix + name,
                    Id = name,
                    Engine = false,
                    Folder = dir
                });
            }
        }

        public static async Task<string> CompileAsync(
            string shell, CompileTarget target, IProgress<string>? progress = null)
        {
            if (target.Engine)
                return await CompileEngineAsync(shell, target.Folder, progress);

            var project = Path.Combine(target.Folder, "asconfig.json");
            if (!File.Exists(project))
                throw new FileNotFoundException("This mod has no asconfig.json.", project);
            var (resolved, swf) = ResolveModProject(project);
            progress?.Report("Compiling " + target.Label + " → " + swf);
            await RunN2fAsync("asconfig --project \"" + resolved + "\" --debug", progress);
            var outDir = Path.GetDirectoryName(swf);
            foreach (var dir in new[] { target.Folder, outDir })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    continue;
                foreach (var extra in Directory.EnumerateFiles(dir, "*-app.xml"))
                {
                    try { File.Delete(extra); }
                    catch { /* the SWF is the output */ }
                }
            }
            return File.Exists(swf) ? swf : target.Folder;
        }

        /// <summary>
        /// Writes a temp copy of a mod's asconfig.json with every path made absolute
        /// (relative paths are relative to the mod folder, not the temp file),
        /// engine/resources names pointed at the decompiled externs, and the output resolved.
        /// </summary>
        public static (string Project, string Output) ResolveModProject(string projectPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var json = JObject.Parse(File.ReadAllText(projectPath));
            if (json["compilerOptions"] is not JObject opts)
            {
                opts = new JObject();
                json["compilerOptions"] = opts;
            }
            var text = opts.ToString();
            var usesEngine = text.Contains("engine/resources") || text.Contains("engine\\resources");
            string? externs = null;
            if (usesEngine)
            {
                externs = FindExterns(dir)
                    ?? throw new InvalidOperationException(
                        "Decompile the SWF first. Linked libraries are written once next to the scripts.");
                EnsureEngine2LinkReport(Path.GetDirectoryName(externs)!);
            }

            foreach (var key in new[] { "source-path", "external-library-path", "load-externs", "library-path" })
            {
                if (opts[key] is not JArray entries)
                    continue;
                var kept = new JArray();
                foreach (var entry in entries.Select(e => e.ToString()))
                {
                    var full = ResolveEntry(entry, dir, externs);
                    if (full != null)
                        kept.Add(full.Replace('\\', '/'));
                }
                opts[key] = kept;
            }

            if (usesEngine && externs != null && opts["source-path"] is JArray sources)
            {
                var engine2 = Path.GetDirectoryName(externs)!;
                var insertAt = 0;
                foreach (var engineSrc in EngineSourcePaths(engine2))
                    sources.Insert(insertAt++, engineSrc.Replace('\\', '/'));
            }

            var output = opts["output"]?.ToString();
            if (string.IsNullOrWhiteSpace(output))
                output = "mod.swf";
            var swf = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(dir, output));
            opts["output"] = swf.Replace('\\', '/');
            Directory.CreateDirectory(Path.GetDirectoryName(swf)!);

            var tmp = Path.Combine(Path.GetTempPath(), "ssf2mm-" + Path.GetFileName(dir) + "-asconfig.json");
            File.WriteAllText(tmp, json.ToString());
            return (tmp, swf);
        }

        private static string? ResolveEntry(string entry, string dir, string? externs)
        {
            if (externs != null)
            {
                foreach (var (name, generated) in ExternMap)
                {
                    if (Regex.IsMatch(entry, @"(?:(?:\.\./)|(?:\.\.\\))*engine[/\\]resources[/\\]" + Regex.Escape(name) + "$"))
                    {
                        var abs = Path.Combine(externs, generated);
                        return File.Exists(abs) ? abs : null;
                    }
                }
            }
            var full = Path.GetFullPath(Path.IsPathRooted(entry) ? entry : Path.Combine(dir, entry));
            if (File.Exists(full) || Directory.Exists(full))
                return full;
            if (entry.EndsWith(".ane", StringComparison.OrdinalIgnoreCase))
            {
                var dev = ShellWorkspaceService.FindDevWorkspace();
                var devAne = dev == null ? null : Path.Combine(dev, Path.GetFileName(entry));
                if (devAne != null && File.Exists(devAne))
                    return devAne;
            }
            return null;
        }

        private static readonly (string Name, string Generated)[] ExternMap =
        {
            ("engine1-code.swc", "engine1.swc"),
            ("engine1-link-report.xml", "link-report.xml"),
            ("engine2-link-report.xml", "engine2-link-report.xml"),
            ("SSF2.swc", "SSF2.swc"),
        };

        /// <summary>
        /// Lists every decompiled engine2 class so mods load them as externs
        /// instead of compiling a second copy of the engine into mod.swf.
        /// </summary>
        public static string EnsureEngine2LinkReport(string engine2Dir)
        {
            var report = Path.Combine(engine2Dir, "externs", "engine2-link-report.xml");
            if (File.Exists(report))
                return report;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<report><scripts>");
            foreach (var name in ScriptClassNames(EngineSourcePaths(engine2Dir)))
            {
                var dot = name.LastIndexOf('.');
                var id = dot < 0 ? name : name[..dot] + ":" + name[(dot + 1)..];
                sb.AppendLine("<script name=\"" + id + "\"><def id=\"" + id + "\" /></script>");
            }
            sb.AppendLine("</scripts></report>");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, sb.ToString());
            return report;
        }

        private static async Task<string> CompileEngineAsync(
            string shell, string engine2Dir, IProgress<string>? progress)
        {
            var project = WriteEngineProject(shell, engine2Dir);
            var output = FindOutputSwf(shell, Path.GetFileName(engine2Dir));
            progress?.Report("Compiling " + Path.GetFileName(engine2Dir) + "…");
            await RunN2fAsync("asconfig --project \"" + project + "\"", progress);
            var outDir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(outDir) && Directory.Exists(outDir))
            {
                foreach (var extra in Directory.EnumerateFiles(outDir, "*-app.xml"))
                {
                    try { File.Delete(extra); }
                    catch { /* the SWF is the output */ }
                }
            }
            return output;
        }

        private static string WriteEngineProject(string shell, string engine2Dir)
        {
            var externs = FindExterns(engine2Dir)
                ?? throw new InvalidOperationException(
                    "Decompile the SWF first. Linked libraries are written once next to the scripts.");
            var sources = EngineSourcePaths(engine2Dir).ToArray();
            var stem = Path.GetFileName(engine2Dir);
            var classes = ScriptClassNames(sources);
            var swcs = Directory.GetFiles(externs, "*.swc").OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            var opts = new JObject
            {
                ["source-path"] = new JArray(sources),
                ["includes"] = new JArray(classes),
                ["external-library-path"] = new JArray(swcs),
                ["target-player"] = "32.0",
                ["optimize"] = true,
                ["strict"] = false,
                ["warnings"] = false,
                ["static-link-runtime-shared-libraries"] = true,
                ["load-externs"] = new JArray(Path.Combine(externs, "link-report.xml")),
                ["link-report"] = Path.Combine(externs, stem + "-link-report.xml"),
                ["output"] = FindOutputSwf(shell, stem),
            };
            foreach (var sdkSwc in SdkLibraries())
                ((JArray)opts["external-library-path"]!).Add(sdkSwc);
            var json = new JObject
            {
                ["mainClass"] = MainClassName(classes),
                ["compilerOptions"] = opts,
            };
            var tmp = Path.Combine(Path.GetTempPath(), "ssf2mm-asconfig-" + stem + ".json");
            File.WriteAllText(tmp, json.ToString());
            return tmp;
        }

        public static string? FindExterns(string start)
        {
            var dir = start;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var local = Path.Combine(dir, "externs", "link-report.xml");
                if (File.Exists(local))
                    return Path.GetDirectoryName(local);
                var engine = Path.Combine(dir, "expansions", "decompiled", "engine2", "externs", "link-report.xml");
                if (File.Exists(engine))
                    return Path.GetDirectoryName(engine);
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir)
                    break;
                dir = parent;
            }
            return null;
        }

        private static string FindOutputSwf(string shell, string stem)
        {
            var fallback = Path.Combine(shell, "data", "engine", stem + ".swf");
            if (!Directory.Exists(shell))
                return fallback;
            var matches = Directory.EnumerateFiles(shell, stem + ".swf", SearchOption.AllDirectories).ToList();
            var underData = matches.FirstOrDefault(path =>
                path.Contains(Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            return underData ?? matches.FirstOrDefault() ?? fallback;
        }

        private static IEnumerable<string> SdkLibraries()
        {
            var root = AppPaths.Next2FlashRoot;
            if (string.IsNullOrWhiteSpace(root))
                yield break;
            var libs = new[]
            {
                Path.Combine(root, "toolchain", "aflex_lite", "frameworks", "libs"),
                Path.Combine(root, "runtime", "resources", "toolchain", "aflex_lite", "frameworks", "libs"),
            };
            foreach (var dir in libs)
            {
                var air = Path.Combine(dir, "air", "airglobal.swc");
                if (File.Exists(air))
                {
                    yield return air;
                    yield break;
                }
            }
        }

        private static List<string> ScriptClassNames(IEnumerable<string> sourceRoots)
        {
            var names = new List<string>();
            foreach (var root in sourceRoots)
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.as", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file);
                    if (rel.IndexOfAny(new[] { '$', ' ' }) >= 0)
                        continue;
                    var name = Path.ChangeExtension(rel, null)!
                        .Replace(Path.DirectorySeparatorChar, '.')
                        .Replace(Path.AltDirectorySeparatorChar, '.');
                    if (name.Length > 0)
                        names.Add(name);
                }
            }
            return names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        private static string MainClassName(IReadOnlyList<string> classes)
        {
            var main = classes.FirstOrDefault(name =>
                name.Equals("Main", StringComparison.Ordinal) || name.EndsWith(".Main", StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(main))
                return main;
            return classes.Count > 0 ? classes[0] : "Main";
        }

        private static IEnumerable<string> EngineSourcePaths(string engine2Dir)
        {
            var unchanged = Path.Combine(engine2Dir, "unchanged");
            var changed = Path.Combine(engine2Dir, "changed");
            if (Directory.Exists(unchanged) || Directory.Exists(changed))
            {
                if (Directory.Exists(unchanged))
                    yield return unchanged;
                if (Directory.Exists(changed))
                    yield return changed;
            }
            else
            {
                var scripts = Path.Combine(engine2Dir, "scripts");
                yield return Directory.Exists(Path.Combine(scripts, "com")) ? scripts : engine2Dir;
            }
        }

        public static async Task RunN2fAsync(string argumentTail, IProgress<string>? progress)
        {
            await Next2FlashSetup.EnsureAsync();
            var psi = Next2FlashSetup.CreateRaw(argumentTail, redirectInput: false);
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start n2f.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());
            var combined = stdoutTask.Result + "\n" + stderrTask.Result;
            foreach (var line in combined.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    progress?.Report(trimmed);
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("n2f failed (exit " + process.ExitCode + ").");
        }
    }
}
