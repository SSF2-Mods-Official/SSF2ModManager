using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FlaStepWizard;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Thin wrapper around the JPEXS Free Flash Decompiler (FFDec) CLI.
    /// Prefers a long-lived <c>ffdec-worker</c> JVM (see <c>src/ffdec-worker</c>) so successive
    /// commands avoid cold-start cost; falls back to one-shot <c>java -jar ffdec.jar</c>.
    /// </summary>
    public class JpexsCliService
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
        private const int WorkerCount = 2;
        private static readonly object WorkerPoolLock = new();
        private static readonly WorkerSlot[] Workers = CreateWorkerSlots();
        private static string? _poolFfdecDir;

        private sealed class WorkerSlot
        {
            // Async-safe exclusive use (never hold Monitor across await).
            public readonly SemaphoreSlim Busy = new(1, 1);
            public Process? Process;
            public StreamWriter? In;
            public StreamReader? Out;
            public int Id;
        }

        private static WorkerSlot[] CreateWorkerSlots()
        {
            var slots = new WorkerSlot[WorkerCount];
            for (var i = 0; i < WorkerCount; i++)
                slots[i] = new WorkerSlot { Id = i };
            return slots;
        }

        /// <summary>
        /// Resolves an FFDec launcher from a configured path (file or folder).
        /// Prefers ffdec.bat / ffdec.exe on Windows, then ffdec.jar.
        /// </summary>
        public string? ResolveExecutable(string? configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return null;

            var path = configuredPath.Trim().Trim('"');

            if (File.Exists(path))
            {
                var name = Path.GetFileName(path);
                if (name.Equals("ffdec.bat", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffdec.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffdec.jar", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffdec-cli.jar", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffdec-cli.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                // Allow pointing at any .jar/.bat/.exe named similarly
                var ext = Path.GetExtension(path);
                if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                return null;
            }

            if (!Directory.Exists(path))
                return null;

            string[] candidates =
            {
                "ffdec.bat",
                "ffdec.exe",
                "ffdec-cli.exe",
                "ffdec.jar",
                "ffdec-cli.jar"
            };

            foreach (var candidate in candidates)
            {
                var full = Path.Combine(path, candidate);
                if (File.Exists(full))
                    return full;
            }

            return null;
        }

        /// <summary>
        /// Resolves the FFDec installed with Next2Flash, then a local runtime\ffdec copy.
        /// </summary>
        public string? ResolveBundledExecutable()
        {
            string[] candidates = { "ffdec.jar", "ffdec-cli.jar", "ffdec.bat" };
            var external = AppPaths.FfdecDirectory;
            if (!string.IsNullOrWhiteSpace(external))
            {
                foreach (var candidate in candidates)
                {
                    var full = Path.Combine(external, candidate);
                    if (File.Exists(full))
                        return full;
                }
            }
            foreach (var root in AppPaths.EnumerateSidecarRoots())
            {
                var bundleDir = Path.Combine(root, "ffdec");
                foreach (var candidate in candidates)
                {
                    var full = Path.Combine(bundleDir, candidate);
                    if (File.Exists(full))
                        return full;
                }
            }
            return null;
        }

        /// <summary>
        /// Resolves a configured FFDec path, falling back to the bundled copy.
        /// </summary>
        public string? ResolveExecutableOrBundled(string? configuredPath) =>
            ResolveExecutable(configuredPath) ?? ResolveBundledExecutable();

        public bool IsAvailable(string? configuredPath) =>
            ResolveExecutableOrBundled(configuredPath) != null;

        /// <summary>
        /// Prefers the JRE shipped with Next2Flash, then a local runtime\jre, then PATH.
        /// </summary>
        public static string? ResolveJavaExecutable(string? nearbyDirectory = null)
        {
            foreach (var bundled in JavaHomeCandidates(nearbyDirectory))
            {
                if (File.Exists(bundled))
                    return bundled;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "java",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "java";
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .FirstOrDefault(File.Exists);
                if (first != null) return first;
            }
            catch { /* ignore */ }

            return null;
        }

        private static IEnumerable<string> JavaHomeCandidates(string? nearbyDirectory)
        {
            var app = AppPaths.JreJavaExe;
            if (app != null)
                yield return app;

            foreach (var root in AppPaths.EnumerateSidecarRoots())
                yield return Path.Combine(root, "jre", "bin", "java.exe");

            var dir = nearbyDirectory ?? AppPaths.ExeDirectory;
            for (var i = 0; i < 5 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                yield return Path.Combine(dir, "runtime", "jre", "bin", "java.exe");
                yield return Path.Combine(dir, "jre", "bin", "java.exe");
                yield return Path.Combine(dir, "bin", "java.exe");
                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        public static bool HasBundledJre() => AppPaths.JreJavaExe != null;

        /// <summary>
        /// Builds a CLI -config fragment. Values containing commas/spaces must be quoted for the process argv.
        /// </summary>
        public static string FormatConfigArgument(string config) =>
            $"-config \"{config.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

        public Task ExportAsync(
            string ffdecPath,
            string swfPath,
            string exportDir,
            string itemTypes = "symbolClass,script",
            CancellationToken cancellationToken = default,
            string? config = null) =>
            RunFfdecAsync(
                ffdecPath,
                $"{(string.IsNullOrWhiteSpace(config) ? "" : $"{FormatConfigArgument(config)} ")}-export {itemTypes} \"{exportDir}\" \"{swfPath}\"",
                cancellationToken);

        /// <summary>
        /// Convert a data-folder SWF to CS6 FLA the same way stage/character export does.
        /// Writes under <c>expansions/decompiled/…</c>. Character pretty-pass
        /// (Ground/Effects/hitbox peel) only runs for <c>data/character</c> SWFs.
        /// </summary>
        public async Task<string> ConvertSwfToFlaAsync(
            string swfPath,
            string outputDirectory,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool characterPretty = false,
            bool stageLibrary = false)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
                throw new FileNotFoundException("SWF was not found.", swfPath);
            var name = Path.GetFileNameWithoutExtension(swfPath);
            if (Directory.Exists(outputDirectory))
            {
                try { Directory.Delete(outputDirectory, recursive: true); }
                catch { /* overwrite what we can */ }
            }
            Directory.CreateDirectory(outputDirectory);
            progress?.Report("Converting " + Path.GetFileName(swfPath) + " to FLA…");
            await ExportFlaAsync(
                "", swfPath, outputDirectory, "cs6", cancellationToken,
                outputBaseName: name, progress: progress, characterPretty: characterPretty,
                stageLibrary: stageLibrary);
            var fla = Path.Combine(outputDirectory, name + ".fla");
            if (!File.Exists(fla) && !Directory.Exists(fla))
                throw new InvalidOperationException("FLA export did not produce " + name + ".fla.");
            progress?.Report("Wrote " + fla);
            return fla;
        }

        public static IReadOnlyList<(string Relative, string FullPath)> ListDataFolderSwfs(string installRoot)
        {
            var data = Path.Combine(installRoot ?? "", "data");
            if (!Directory.Exists(data))
                return Array.Empty<(string, string)>();
            var swf = Directory.GetFiles(data, "*.swf", SearchOption.AllDirectories);
            var ssf = Directory.GetFiles(data, "*.ssf", SearchOption.AllDirectories);
            return swf.Concat(ssf)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(full => (Path.GetRelativePath(data, full).Replace('\\', '/'), full))
                .ToList();
        }

        /// <summary>True when <paramref name="relative"/> is a SWF under <c>data/stage</c>.</summary>
        public static bool IsStageDataSwf(string? relative)
        {
            var path = (relative ?? "").Replace('\\', '/');
            return path.StartsWith("stage/", StringComparison.OrdinalIgnoreCase);
        }

        public static string DecompiledOutputDirectory(string installRoot, string relativeSwf)
        {
            var stem = Path.ChangeExtension((relativeSwf ?? "").Replace('/', Path.DirectorySeparatorChar), null);
            if (string.IsNullOrWhiteSpace(stem))
                stem = "swf";
            return Path.Combine(ShellWorkspaceService.DecompiledExpansionsDirectory(installRoot), stem);
        }

        /// <summary>
        /// Raw FFDec SWF?FLA: one <c>-export fla</c>, flatten nested
        /// <c>{name}.fla/{name}.fla</c>, copy sidecars as JPEXS wrote them.
        /// Empty Sound/MovieClip <c>.as</c> stubs are deleted. Timeline
        /// <c>{id}_fla.*</c> AS3 export linkage is stripped so Animate does not
        /// type <c>stance</c> as <c>Idle_3</c> (Error #1034). Auto-declare is
        /// turned off. Scripts, sounds, and timelines are not rewritten.
        /// </summary>
        public async Task ExportFlaAsync(
            string ffdecPath,
            string swfPath,
            string outputDirectory,
            string flaVersion = "cs6",
            CancellationToken cancellationToken = default,
            string? outputBaseName = null,
            IProgress<string>? progress = null,
            bool characterPretty = true,
            bool stageLibrary = false)
        {
            Directory.CreateDirectory(outputDirectory);
            var ver = string.IsNullOrWhiteSpace(flaVersion) ? "cs6" : flaVersion.Trim();
            var baseName = ResolveFlaOutputBaseName(swfPath, outputBaseName);
            var totalSw = Stopwatch.StartNew();

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-fla-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("FLA: FFDec export starting…");
                var ffdecSw = Stopwatch.StartNew();
                await ExportFlaToWorkAsync(ffdecPath, swfPath, work, baseName, ver, cancellationToken, ident: null);
                var ffdecMs = ffdecSw.ElapsedMilliseconds;
                progress?.Report($"FLA: FFDec done ({ffdecMs / 1000.0:0.0}s)");
                LogFlaTiming("ffdec-export", ffdecMs);

                var packSw = Stopwatch.StartNew();
                var produced = FlattenFlaExport(work, baseName);
                if (!File.Exists(produced))
                    throw new InvalidOperationException("FLA export did not produce a .fla file.");
                LogFlaTiming("pack-flatten", packSw.ElapsedMilliseconds);

                var copySw = Stopwatch.StartNew();
                TryDeleteDirectory(Path.Combine(outputDirectory, "FLA1"));
                TryDeleteDirectory(Path.Combine(outputDirectory, "FLA2"));
                CopyFlattenedFlaOutput(work, outputDirectory);
                PruneEmptyFlaSidecars(outputDirectory);
                RelocateFlaScriptsToCommon(outputDirectory);
                var flaFile = Path.Combine(outputDirectory, baseName + ".fla");
                if (!File.Exists(flaFile))
                    throw new InvalidOperationException("FLA export did not produce a .fla file.");
                LogFlaTiming("pack-copy", copySw.ElapsedMilliseconds);

                var patchSw = Stopwatch.StartNew();
                ApplyFlaVanillaExportPatches(flaFile, baseName);
                LogFlaTiming("pack-patches", patchSw.ElapsedMilliseconds);
                var packMs = packSw.ElapsedMilliseconds;
                LogFlaTiming("flatten-copy-patches", packMs);

                var dumpFlv = Path.Combine(work, "swf-sounds-flv");
                var dumpWav = Path.Combine(work, "swf-sounds-wav");
                progress?.Report("FLA: converting SWF sounds to WAV…");
                var dumpSw = Stopwatch.StartNew();
                await TryDumpSwfSoundsAsync(ffdecPath, swfPath, dumpFlv, "flv", cancellationToken);
                var dumpMs = dumpSw.ElapsedMilliseconds;
                LogFlaTiming("sound-dump", dumpMs);

                var wavSw = Stopwatch.StartNew();
                ConvertSoundDumpFolderToPcmWav(dumpFlv, dumpWav);
                var wavMs = wavSw.ElapsedMilliseconds;
                LogFlaTiming("wav-convert", wavMs);

                var spliceSw = Stopwatch.StartNew();
                SpliceAndConvertFlaSounds(flaFile, dumpWav);
                var spliceMs = spliceSw.ElapsedMilliseconds;
                LogFlaTiming("splice-pcm", spliceMs);

                var prettySw = Stopwatch.StartNew();
                ApplyFlaPrettyPass(flaFile, outputDirectory, baseName, progress, characterPretty, stageLibrary);
                var prettyMs = prettySw.ElapsedMilliseconds;
                LogFlaTiming("pretty-pass", prettyMs);

                DebugLogger.Log(
                    $"[JPEXS] timing total={totalSw.ElapsedMilliseconds}ms " +
                    $"ffdec={ffdecMs}ms pack={packMs}ms soundDump={dumpMs}ms " +
                    $"wav={wavMs}ms splice={spliceMs}ms pretty={prettyMs}ms");
                progress?.Report($"FLA done ({totalSw.Elapsed.TotalSeconds:0.0}s)");
            }
            finally
            {
                TryDeleteDirectory(work);
            }
        }

        /// <summary>
        /// Wizard steps (organize, names, folders, merge) in-process. No <c>tools/</c> folder and no <c>.bak</c> files.
        /// A step that returns <c>Ok == false</c> is logged; exceptions still fail the export.
        /// </summary>
        public static void ApplyFlaPrettyPass(
            string flaFile,
            string outputDirectory,
            string? ident,
            IProgress<string>? progress = null,
            bool characterPretty = true,
            bool stageLibrary = false)
        {
            if (string.IsNullOrWhiteSpace(flaFile) || !File.Exists(flaFile))
                return;

            var tsv = BuildFlaOrganizeBlueprint(flaFile, stageLibrary) ?? "";
            var display = ResolveFlaDisplayName(flaFile, outputDirectory);
            if (string.IsNullOrWhiteSpace(display))
                display = string.IsNullOrWhiteSpace(ident)
                    ? Path.GetFileNameWithoutExtension(flaFile)
                    : ident;

            Run("organize library", () => FlaCleanupSteps.Organize(flaFile, tsv));
            Run("rename layers", () => FlaCleanupSteps.NamesAndOutlines(flaFile, display, characterPretty));
            Run("folders and lock", () => FlaCleanupSteps.FoldersAndLock(flaFile, display, characterPretty));
            Run("merge box layers", () => FlaCleanupSteps.Merge(flaFile, display));
            return;

            void Run(string label, Func<FlaCleanupSteps.Result> step)
            {
                progress?.Report("FLA: " + label + "�");
                var sw = Stopwatch.StartNew();
                var result = step();
                DebugLogger.Log($"[JPEXS] timing pretty {label} {sw.ElapsedMilliseconds}ms — {result.Message}");
                if (!result.Ok)
                    DebugLogger.Log("[JPEXS] pretty " + label + " reported failure; continuing.");
            }
        }

        /// <summary>
        /// FFDec FLA export config pairs (no <c>-config</c> wrapper). Keys must exist as
        /// non-<c>@ConfigurationRemoved</c> fields on the bundled FFDec <c>Configuration</c>
        /// class � a removed key NPEs the CLI instead of warning.
        /// </summary>
        public const string FlaExportConfigPairs = "autoDeobfuscate=0,openFolderAfterFlaExport=0,parallelSpeedUp=1,parallelSpeedUpThreadCount=4";

        private static void LogFlaTiming(string phase, long elapsedMs) =>
            DebugLogger.Log($"[JPEXS] timing {phase} {elapsedMs}ms");

        /// <summary>
        /// FFDec FLA export config: deobfuscate off, do not open Explorer after export.
        /// </summary>
        public static string FormatFlaExportConfig() =>
            FormatConfigArgument(FlaExportConfigPairs);

        /// <summary>
        /// Key names from a <c>k=v,k2=v2</c> list or a formatted <c>-config "�"</c> argument.
        /// </summary>
        public static IReadOnlyList<string> ParseConfigKeys(string configPairsOrArgument)
        {
            if (string.IsNullOrWhiteSpace(configPairsOrArgument))
                return Array.Empty<string>();

            var s = configPairsOrArgument.Trim();
            if (s.StartsWith("-config", StringComparison.OrdinalIgnoreCase))
            {
                s = s["-config".Length..].Trim();
                if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                    s = s[1..^1];
            }

            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    var eq = part.IndexOf('=');
                    return (eq < 0 ? part : part[..eq]).Trim();
                })
                .Where(k => k.Length > 0)
                .ToArray();
        }

        /// <summary>
        /// Live <c>-config</c> names from bundled <c>ffdec_lib.jar</c> (skips
        /// <c>@ConfigurationRemoved</c> fields � those NPE the CLI).
        /// </summary>
        public static HashSet<string> ReadLiveFfdecConfigKeys(string ffdecLibJarPath) =>
            FfdecConfigurationClass.ReadLiveKeys(ffdecLibJarPath);

        /// <summary>
        /// Throws if any FLA-export config key is missing or <c>@ConfigurationRemoved</c>
        /// in the given <c>ffdec_lib.jar</c>.
        /// </summary>
        public static void EnsureFlaExportConfigKeysAreLive(string ffdecLibJarPath)
        {
            var live = ReadLiveFfdecConfigKeys(ffdecLibJarPath);
            var missing = ParseConfigKeys(FlaExportConfigPairs)
                .Where(k => !live.Contains(k))
                .ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "FFDec -config key(s) are not live in " + ffdecLibJarPath + ": " +
                    string.Join(", ", missing) +
                    ". Removed keys NPE the CLI (ConfigurationItem.getItem).");
            }
        }

        private static string? TryFindFfdecLibJar(string ffdecPath)
        {
            var dir = Path.GetDirectoryName(ffdecPath);
            if (string.IsNullOrEmpty(dir))
                return null;
            var lib = Path.Combine(dir, "lib", "ffdec_lib.jar");
            return File.Exists(lib) ? lib : null;
        }

        private async Task ExportFlaToWorkAsync(
            string ffdecPath,
            string swfPath,
            string workDirectory,
            string baseName,
            string flaVersion,
            CancellationToken cancellationToken,
            string? ident = null)
        {
            var libJar = TryFindFfdecLibJar(ffdecPath);
            if (libJar != null)
                EnsureFlaExportConfigKeysAreLive(libJar);

            Directory.CreateDirectory(workDirectory);
            var flaPath = Path.Combine(workDirectory, baseName + ".fla");
            var identProp = string.IsNullOrWhiteSpace(ident)
                ? ""
                : $"-Dssf2mm.ident={ident.Trim()} ";
            await RunFfdecAsync(
                ffdecPath,
                $"{identProp}{FormatFlaExportConfig()} -format fla:{flaVersion} -export fla \"{flaPath}\" \"{swfPath}\"",
                cancellationToken,
                timeout: TimeSpan.FromMinutes(3));
        }

        /// <summary>
        /// Exports SymbolClass names from a SWF via FFDec (fast <c>symbols.csv</c> dump).
        /// </summary>
        public async Task<HashSet<string>> GetSymbolClassNamesAsync(
            string ffdecPath,
            string swfPath,
            CancellationToken cancellationToken = default)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-symclass-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                await ExportAsync(ffdecPath, swfPath, work, "symbolClass", cancellationToken);
                var csv = Directory.GetFiles(work, "*.csv", SearchOption.AllDirectories)
                    .FirstOrDefault(f =>
                        Path.GetFileName(f).Contains("symbol", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.GetFiles(work, "*.csv", SearchOption.AllDirectories).FirstOrDefault();
                return csv != null
                    ? ParseSymbolClassNamesFromCsv(await File.ReadAllTextAsync(csv, cancellationToken))
                    : new HashSet<string>(StringComparer.Ordinal);
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Parses FFDec <c>symbols.csv</c> (id;"ClassName" per line).
        /// </summary>
        public static HashSet<string> ParseSymbolClassNamesFromCsv(string csvText)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(csvText))
                return names;
            foreach (Match m in Regex.Matches(
                         csvText,
                         @"^\s*\d+;""(?<n>[^""]+)""",
                         RegexOptions.Multiline))
                names.Add(m.Groups["n"].Value);
            foreach (Match m in Regex.Matches(
                         csvText,
                         @"^\s*\d+;(?<n>[A-Za-z0-9_\.]+)\s*$",
                         RegexOptions.Multiline))
                names.Add(m.Groups["n"].Value);
            return names;
        }

        /// <summary>
        /// Copies only external API/document scripts and <c>fl/</c> from a flattened FFDec
        /// sidecar export into <paramref name="outputDirectory"/>.
        /// </summary>
        public static void HarvestExternalFlaSidecars(
            string sourceDirectory,
            string outputDirectory,
            string baseName,
            IReadOnlySet<string> symbolClassNames)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                return;
            Directory.CreateDirectory(outputDirectory);
            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();

            var flSrc = Path.Combine(sourceDirectory, "fl");
            if (Directory.Exists(flSrc))
            {
                var flDest = Path.Combine(outputDirectory, "fl");
                if (Directory.Exists(flDest)) Directory.Delete(flDest, recursive: true);
                CopyDirectory(flSrc, flDest);
            }

            foreach (var asFile in Directory.GetFiles(sourceDirectory, "*.as"))
            {
                var className = Path.GetFileNameWithoutExtension(asFile);
                if (!ShouldKeepExternalFlaScript(asFile, className, name, symbolClassNames))
                    continue;
                File.Copy(asFile, Path.Combine(outputDirectory, Path.GetFileName(asFile)), overwrite: true);
                if (className.Equals("Main", StringComparison.Ordinal))
                {
                    var dest = Path.Combine(outputDirectory, Path.GetFileName(asFile));
                    try
                    {
                        var patched = PatchFlaDocumentClassMainAs(File.ReadAllText(dest));
                        File.WriteAllText(dest, patched);
                    }
                    catch { /* locked */ }
                }
                else if (className.Equals("SSF2Asset", StringComparison.Ordinal))
                {
                    var dest = Path.Combine(outputDirectory, Path.GetFileName(asFile));
                    try
                    {
                        var patched = PatchSsf2AssetExtendsMovieClip(File.ReadAllText(dest));
                        File.WriteAllText(dest, patched);
                    }
                    catch { /* locked */ }
                }
            }
        }

        private static readonly Regex As3ClassNameRegex = new(
            @"\bclass\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex As3FunctionNameRegex = new(
            @"(?:public|protected|private|internal)?\s*(?:static\s+)?(?:override\s+)?function\s+(?:get\s+|set\s+)?(?<n>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex JpexsFrameHandlerRegex = new(
            @"^frame\d+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// True when the sidecar class has no methods besides its constructor
        /// (empty Sound / MovieClip / BitmapData wrappers). JPEXS
        /// <c>frame1</c>/<c>frame12</c> handlers from addFrameScript do not count
        /// as extra, so Idle/Taunt clips are still stubs.
        /// </summary>
        public static bool IsEmptyFlaSidecar(string asText, string className)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return true;
            var name = string.IsNullOrWhiteSpace(className)
                ? ParseAs3ClassName(asText)
                : className.Trim();
            if (string.IsNullOrEmpty(name))
                name = "";

            var extra = false;
            foreach (Match m in As3FunctionNameRegex.Matches(asText))
            {
                var fn = m.Groups["n"].Value;
                if (fn.Equals(name, StringComparison.Ordinal))
                    continue;
                if (JpexsFrameHandlerRegex.IsMatch(fn))
                    continue;
                extra = true;
                break;
            }
            return !extra;
        }

        /// <summary>Class name from a decompiled <c>.as</c> sidecar, or null.</summary>
        public static string? ParseAs3ClassName(string asText)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return null;
            var m = As3ClassNameRegex.Match(asText);
            return m.Success ? m.Groups["n"].Value : null;
        }

        /// <summary>
        /// Keep document/API scripts even if they look like empty classes.
        /// </summary>
        public static bool ShouldPreserveFlaSidecar(string filePath)
        {
            var n = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(n))
                return true;
            if (n.Equals("Main", StringComparison.OrdinalIgnoreCase))
                return true;
            if (n.StartsWith("SSF2", StringComparison.Ordinal))
                return true;
            if (n.EndsWith("Ext", StringComparison.Ordinal))
                return true;
            return false;
        }

        /// <summary>
        /// True for scripts that stay at the FLA root: <c>Main.as</c> and <c>*Ext.as</c>.
        /// </summary>
        public static bool ShouldKeepFlaScriptAtFlaRoot(string filePath)
        {
            var n = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(n))
                return true;
            if (n.Equals("Main", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsSsf2CharacterExtName(n))
                return false;
            if (n.EndsWith("Ext", StringComparison.Ordinal))
                return true;
            return false;
        }

        public static bool IsSsf2CharacterExtName(string name) =>
            !string.IsNullOrEmpty(name) &&
            (name.Equals("SSF2CharacterExt", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith("_SSF2CharacterExt", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// JPEXS prefixes the engine class with the clone ident. Restore
        /// <c>SSF2CharacterExt.as</c> and rewrite references.
        /// </summary>
        public static int RestoreSsf2CharacterExtScripts(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return 0;
            var files = Directory.GetFiles(outputDirectory, "*.as", SearchOption.AllDirectories)
                .Where(f => IsSsf2CharacterExtName(Path.GetFileNameWithoutExtension(f)))
                .ToList();
            if (files.Count == 0)
                return 0;

            var oldNames = files
                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                .Where(n => !n.Equals("SSF2CharacterExt", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var common = Path.Combine(outputDirectory, "common");
            Directory.CreateDirectory(common);
            var dest = Path.Combine(common, "SSF2CharacterExt.as");
            var text = File.ReadAllText(files[0]);
            foreach (var old in oldNames)
                text = Regex.Replace(text, @"\b" + Regex.Escape(old) + @"\b", "SSF2CharacterExt");
            File.WriteAllText(dest, text);
            foreach (var file in files)
            {
                if (file.Equals(dest, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(file); } catch { /* locked */ }
            }

            foreach (var asFile in Directory.GetFiles(outputDirectory, "*.as", SearchOption.AllDirectories))
            {
                if (asFile.Equals(dest, StringComparison.OrdinalIgnoreCase))
                    continue;
                var next = File.ReadAllText(asFile);
                var updated = next;
                foreach (var old in oldNames)
                    updated = Regex.Replace(updated, @"\b" + Regex.Escape(old) + @"\b", "SSF2CharacterExt");
                if (updated != next)
                    File.WriteAllText(asFile, updated);
            }

            return 1;
        }

        /// <summary>
        /// Moves non-root <c>.as</c> files and the <c>fl/</c> package into <c>common/</c>.
        /// Leaves <c>{id}_fla</c> at the FLA root for JSFL inject.
        /// </summary>
        public static int RelocateFlaScriptsToCommon(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return 0;
            RestoreSsf2CharacterExtScripts(outputDirectory);
            var common = Path.Combine(outputDirectory, "common");
            Directory.CreateDirectory(common);
            var moved = 0;
            foreach (var asFile in Directory.GetFiles(outputDirectory, "*.as"))
            {
                if (ShouldKeepFlaScriptAtFlaRoot(asFile))
                    continue;
                var dest = Path.Combine(common, Path.GetFileName(asFile));
                try
                {
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(asFile, dest);
                    moved++;
                }
                catch { /* locked */ }
            }
            foreach (var dir in Directory.GetDirectories(outputDirectory))
            {
                var folder = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(folder) ||
                    folder.Equals("common", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!folder.Equals("fl", StringComparison.OrdinalIgnoreCase))
                    continue;
                var destDir = Path.Combine(common, "fl");
                try
                {
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, recursive: true);
                    Directory.Move(dir, destDir);
                    moved++;
                }
                catch { /* locked */ }
            }
            return moved;
        }

        /// <summary>
        /// Converts sound bins already inside the FLA to PCM16 WAV (CloudConvert-style
        /// ffmpeg, no extra filters). Does not dump sounds from JPEXS.
        /// </summary>
        public Task<int> TryExportSwfSoundsAsWavAsync(
            string ffdecPath,
            string swfPath,
            string outputDirectory,
            string? flaPath = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return Task.FromResult(0);
            return Task.FromResult(ConvertFlaSoundBinsToPcm(flaPath));
        }

        public static string? MatchWavForSound(string soundName, IReadOnlyList<string> wavPaths, ISet<string>? used = null)
        {
            if (string.IsNullOrWhiteSpace(soundName) || wavPaths == null || wavPaths.Count == 0)
                return null;
            var stem = Path.GetFileNameWithoutExtension(soundName);
            foreach (var wav in wavPaths)
            {
                if (used != null && used.Contains(wav)) continue;
                var wstem = Path.GetFileNameWithoutExtension(wav);
                if (wstem.Equals(stem, StringComparison.OrdinalIgnoreCase))
                    return wav;
                if (wstem.EndsWith("." + stem, StringComparison.OrdinalIgnoreCase) ||
                    wstem.EndsWith("_" + stem, StringComparison.OrdinalIgnoreCase))
                    return wav;
            }

            var nums = Regex.Matches(stem, @"\d+")
                .Select(m => m.Value)
                .ToList();
            if (nums.Count == 0)
                return null;
            var last = nums[^1];
            foreach (var wav in wavPaths)
            {
                if (used != null && used.Contains(wav)) continue;
                var wstem = Path.GetFileNameWithoutExtension(wav);
                if (wstem.Equals(last, StringComparison.OrdinalIgnoreCase))
                    return wav;
                if (wstem.StartsWith(last + ".", StringComparison.OrdinalIgnoreCase) ||
                    wstem.StartsWith(last + "_", StringComparison.OrdinalIgnoreCase))
                    return wav;
                if (wstem.EndsWith("_" + last, StringComparison.OrdinalIgnoreCase))
                    return wav;
            }
            return null;
        }

        public enum SoundMediaKind
        {
            Unknown,
            Flv,
            RiffWav,
            Mp3
        }

        public static SoundMediaKind SniffSoundMagic(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 3 && data[0] == (byte)'F' && data[1] == (byte)'L' && data[2] == (byte)'V')
                return SoundMediaKind.Flv;
            if (data.Length >= 4 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F')
                return SoundMediaKind.RiffWav;
            if (data.Length >= 3 && data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3')
                return SoundMediaKind.Mp3;
            if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
                return SoundMediaKind.Mp3;
            return SoundMediaKind.Unknown;
        }

        public readonly record struct WavFormatInfo(
            int SampleRate,
            int BitsPerSample,
            int Channels,
            int SampleCount,
            int AudioFormat = 1)
        {
            public string ToFlaFormatAttribute()
            {
                var khz = Math.Max(1, (int)Math.Round(SampleRate / 1000.0));
                var ch = Channels > 1 ? "Stereo" : "Mono";
                return khz + "kHz " + BitsPerSample + "bit " + ch;
            }

            public bool IsPcm44k16 => AudioFormat == 1 && SampleRate == 44100 && BitsPerSample == 16;
            public bool IsPcm16 => AudioFormat == 1 && BitsPerSample == 16;
        }

        public static bool TryReadWavFormat(ReadOnlySpan<byte> wav, out WavFormatInfo info)
        {
            info = default;
            if (SniffSoundMagic(wav) != SoundMediaKind.RiffWav || wav.Length < 44)
                return false;
            var offset = 12;
            var channels = 0;
            var rate = 0;
            var bits = 0;
            var audioFormat = 0;
            var dataBytes = 0;
            while (offset + 8 <= wav.Length)
            {
                var chunkId = Encoding.ASCII.GetString(wav.Slice(offset, 4));
                var chunkSize = BitConverter.ToInt32(wav.Slice(offset + 4, 4));
                var dataStart = offset + 8;
                if (chunkSize < 0 || dataStart + chunkSize > wav.Length)
                    break;
                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    audioFormat = BitConverter.ToInt16(wav.Slice(dataStart, 2));
                    channels = BitConverter.ToInt16(wav.Slice(dataStart + 2, 2));
                    rate = BitConverter.ToInt32(wav.Slice(dataStart + 4, 4));
                    bits = BitConverter.ToInt16(wav.Slice(dataStart + 14, 2));
                }
                else if (chunkId == "data")
                    dataBytes = chunkSize;
                offset = dataStart + chunkSize + (chunkSize & 1);
            }
            if (channels <= 0 || rate <= 0 || bits <= 0)
                return false;
            var frameBytes = Math.Max(1, channels * (bits / 8));
            var samples = dataBytes > 0 ? dataBytes / frameBytes : 0;
            info = new WavFormatInfo(rate, bits, channels, samples, audioFormat);
            return true;
        }

        /// <summary>
        /// Patches <c>DOMSoundItem</c> compression metadata. Does not rename <c>name</c>.
        /// </summary>
        public static string PatchDomSoundItemXml(string itemXml, WavFormatInfo wav)
        {
            if (string.IsNullOrEmpty(itemXml))
                return itemXml;
            var next = SetOrInsertXmlAttribute(itemXml, "format", wav.ToFlaFormatAttribute());
            next = SetOrInsertXmlAttribute(next, "sampleCount", wav.SampleCount.ToString(CultureInfo.InvariantCulture));
            // 0 = Default (document publish). 3 = Raw uncompressed.
            next = SetOrInsertXmlAttribute(next, "exportFormat", "3");
            next = SetOrInsertXmlAttribute(next, "compressionType", "Raw");
            next = RemoveXmlAttribute(next, "soundDataHRef");
            var href = GetXmlAttribute(next, "href") ?? GetXmlAttribute(itemXml, "href") ?? "";
            var leaf = Path.GetFileName(href.Replace('\\', '/'));
            if (!string.IsNullOrEmpty(leaf))
            {
                var wavName = Path.GetFileNameWithoutExtension(leaf) + ".wav";
                next = SetOrInsertXmlAttribute(next, "name", wavName);
                next = SetOrInsertXmlAttribute(next, "href", wavName);
            }
            return next;
        }

        public static string RemoveXmlAttribute(string tag, string attribute)
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrWhiteSpace(attribute))
                return tag;
            return Regex.Replace(
                tag,
                @"\s+" + Regex.Escape(attribute) + @"=""[^""]*""",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static int SpliceFlaSoundMedia(string flaPath, string wavDirectory)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var entries = ReadFlaZipEntries(flaPath);
            var replaced = SpliceSoundMediaIntoEntries(entries, wavDirectory);
            if (replaced > 0)
                WriteFlaZipAtomic(flaPath, entries);
            return replaced;
        }

        /// <summary>
        /// Splices dumped PCM wavs and converts remaining sound bins in one zip read and one zip write.
        /// </summary>
        public static int SpliceAndConvertFlaSounds(string flaPath, string wavDirectory)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var entries = ReadFlaZipEntries(flaPath);
            var spliced = SpliceSoundMediaIntoEntries(entries, wavDirectory);
            var pcm = ConvertSoundBinsInEntries(entries, FindBundledFlvToWav());
            if (spliced > 0 || pcm > 0)
                WriteFlaZipAtomic(flaPath, entries);
            return spliced + pcm;
        }

        private static int SpliceSoundMediaIntoEntries(Dictionary<string, byte[]> entries, string wavDirectory)
        {
            if (entries.Count == 0 ||
                string.IsNullOrWhiteSpace(wavDirectory) || !Directory.Exists(wavDirectory))
                return 0;

            var wavs = Directory.GetFiles(wavDirectory, "*.wav", SearchOption.AllDirectories);
            if (wavs.Length == 0)
                return 0;

            var zipNames = entries.Keys.ToList();
            var usedWavs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var binWrites = new List<(string Entry, byte[] Bytes)>();
            var replaced = 0;

            foreach (var key in zipNames)
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!entries.TryGetValue(key, out var raw))
                    continue;
                var xml = Encoding.UTF8.GetString(raw);
                if (xml.IndexOf("DOMSoundItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var next = PatchSoundItemsInXml(xml, wavs, usedWavs, zipNames, binWrites, out var n);
                replaced += n;
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }

            foreach (var (entry, bytes) in binWrites)
            {
                var dest = entry.Replace('\\', '/');
                entries[dest] = bytes;
            }

            return replaced;
        }

        private static bool TryWritePcmWav(
            string ffmpeg,
            string inputPath,
            string destWav,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var raw = File.ReadAllBytes(inputPath);
                var kind = SniffSoundMagic(raw);
                if (kind == SoundMediaKind.RiffWav &&
                    TryReadWavFormat(raw, out var info) &&
                    info.IsPcm16 &&
                    info.SampleRate == 44100)
                {
                    File.Copy(inputPath, destWav, overwrite: true);
                    return true;
                }

                var source = inputPath;
                if (kind == SoundMediaKind.Flv)
                {
                    var flv = Path.ChangeExtension(inputPath, ".flv");
                    if (!string.Equals(flv, inputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(inputPath, flv, overwrite: true);
                        source = flv;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = BuildFlaPcmFfmpegArgs(source, destWav),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                if (!RunFfmpeg(psi))
                    return false;
                return File.Exists(destWav) && File.ReadAllBytes(destWav).Length > 44;
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[JPEXS] ffmpeg PCM convert skipped: " + ex.Message);
                return false;
            }
        }

        public static string BuildFlaPcmFfmpegArgs(string source, string destWav) =>
            "-hide_banner -y -i \"" + source +
            "\" -vn -ar 44100 -c:a pcm_s16le \"" + destWav + "\"";

        /// <summary>
        /// Decodes each FLA sound bin to PCM16 WAV. Passes through only WAVE format tag 1.
        /// </summary>
        public static int ConvertFlaSoundBinsToPcm(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var ffmpeg = FindBundledFlvToWav();
            var entries = ReadFlaZipEntries(flaPath);
            var n = ConvertSoundBinsInEntries(entries, ffmpeg);
            if (n > 0)
                WriteFlaZipAtomic(flaPath, entries);
            return n;
        }

        /// <summary>
        /// Decodes FFDec sound dumps (FLV/MP3/WAV) to 44.1 kHz PCM WAV.
        /// </summary>
        public static int ConvertSoundDumpFolderToPcmWav(string sourceDirectory, string destDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                return 0;
            Directory.CreateDirectory(destDirectory);
            var ffmpeg = FindBundledFlvToWav();
            var n = 0;
            foreach (var file in Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".flv", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".mpa", StringComparison.OrdinalIgnoreCase))
                    continue;
                var dest = Path.Combine(destDirectory, Path.GetFileNameWithoutExtension(file) + ".wav");
                if (ffmpeg != null && TryWritePcmWav(ffmpeg, file, dest, CancellationToken.None) &&
                    File.Exists(dest) && new FileInfo(dest).Length > 44)
                {
                    n++;
                    continue;
                }
                var raw = File.ReadAllBytes(file);
                if (FlaSoundDecoder.TryDecodeToPcm16Wav(raw, out var wav) && wav.Length > 44)
                {
                    File.WriteAllBytes(dest, wav);
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Bundled ffmpeg only (never PATH). Prefer <c>runtime\ffmpeg\ffmpeg.exe</c>.
        /// </summary>
        public static string? FindBundledFlvToWav()
        {
            return AppPaths.FfmpegExe;
        }

        private static IEnumerable<string> EnumerateBundledToolRoots() => AppPaths.EnumerateSidecarRoots();

        private async Task<bool> TryDumpSwfSoundsAsync(
            string ffdecPath,
            string swfPath,
            string dumpDir,
            string format,
            CancellationToken cancellationToken)
        {
            try
            {
                await RunFfdecAsync(
                    ffdecPath,
                    $"-format sound:{format} -export sound \"{dumpDir}\" \"{swfPath}\"",
                    cancellationToken,
                    timeout: TimeSpan.FromMinutes(2));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[JPEXS] sound:" + format + " dump skipped: " + ex.Message);
                return false;
            }

            return Directory.Exists(dumpDir) &&
                   Directory.GetFiles(dumpDir, "*.*", SearchOption.AllDirectories).Any(f =>
                   {
                       var ext = Path.GetExtension(f);
                       return ext.Equals(".flv", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
                   });
        }

        private static string PatchSoundItemsInXml(
            string xml,
            IReadOnlyList<string> wavPaths,
            HashSet<string> usedWavs,
            IReadOnlyList<string> zipNames,
            List<(string Entry, byte[] Bytes)> binWrites,
            out int replaced)
        {
            var n = 0;
            var next = SoundItemTagRegex.Replace(xml, match =>
            {
                var tag = match.Value;
                var name = GetXmlAttribute(tag, "name") ?? GetXmlAttribute(tag, "href") ?? "";
                var wavPath = MatchWavForSound(name, wavPaths, usedWavs)
                    ?? MatchWavForSound(GetXmlAttribute(tag, "href") ?? "", wavPaths, usedWavs)
                    ?? MatchWavForSound(GetXmlAttribute(tag, "linkageClassName") ?? "", wavPaths, usedWavs);
                if (wavPath == null)
                    return tag;

                byte[] bytes;
                try { bytes = File.ReadAllBytes(wavPath); }
                catch { return tag; }
                if (!TryReadWavFormat(bytes, out var info))
                    return tag;

                usedWavs.Add(wavPath);
                var href = GetXmlAttribute(tag, "href") ?? name;
                var leaf = Path.GetFileNameWithoutExtension((href ?? "").Replace('\\', '/'));
                if (string.IsNullOrEmpty(leaf))
                    leaf = Path.GetFileNameWithoutExtension(wavPath);
                var destEntry = FindZipMediaEntry(zipNames, leaf + ".wav")
                    ?? FindZipMediaEntry(zipNames, leaf + ".flv")
                    ?? ("LIBRARY/" + leaf + ".wav");
                if (!destEntry.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    destEntry = Path.ChangeExtension(destEntry.Replace('\\', '/'), ".wav") ?? destEntry;
                binWrites.Add((destEntry, bytes));
                n++;
                return PatchDomSoundItemXml(tag, info);
            });
            replaced = n;
            return next;
        }

        private static readonly Regex SoundItemTagRegex = new(
            @"<DOMSoundItem\b[^>]*?/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string? GetXmlAttribute(string tag, string attribute)
        {
            var m = Regex.Match(
                tag,
                @"\b" + Regex.Escape(attribute) + @"=""([^""]*)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string SetOrInsertXmlAttribute(string tag, string attribute, string value)
        {
            var pattern = @"\b" + Regex.Escape(attribute) + @"=""[^""]*""";
            if (Regex.IsMatch(tag, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    tag,
                    pattern,
                    attribute + "=\"" + value + "\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            var insert = " " + attribute + "=\"" + value + "\"";
            if (tag.EndsWith("/>", StringComparison.Ordinal))
                return tag[..^2] + insert + "/>";
            if (tag.EndsWith(">", StringComparison.Ordinal))
                return tag[..^1] + insert + ">";
            return tag + insert;
        }

        private static string? FindZipMediaEntry(IReadOnlyList<string> zipNames, string? href)
        {
            if (string.IsNullOrWhiteSpace(href) || zipNames == null)
                return null;
            var file = href.Replace('\\', '/').TrimStart('/');
            return zipNames.FirstOrDefault(n =>
                n.Equals(file, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FindSoundBinEntry(IReadOnlyList<string> zipNames, string? href)
        {
            var found = FindZipMediaEntry(zipNames, href);
            if (found != null)
                return found;
            if (string.IsNullOrWhiteSpace(href))
                return null;
            var wavHref = Path.ChangeExtension(href.Replace('\\', '/'), ".wav");
            return FindZipMediaEntry(zipNames, wavHref);
        }

        /// <summary>
        /// Adds <c>common</c> to the FLA AS3 source/package path so sidecars compile.
        /// </summary>
        public static bool AddFlaCommonSourcePath(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return false;
            var changed = false;
            if (TryPatchFlaZipEntry(flaPath, "PublishSettings.xml", MergeCommonIntoAs3PackagePaths))
                changed = true;
            if (TryPatchFlaZipEntry(flaPath, "DOMDocument.xml", MergeCommonIntoDomSourcePath))
                changed = true;
            return changed;
        }

        /// <summary>
        /// One copy-then-patch after FFDec: <c>common</c> source path, strip
        /// <c>{ident}_fla.*</c> export linkage, declare timeline <c>self</c>/<c>xSpeedDecay</c>,
        /// and drop duplicate XML attributes. Does not empty or delete Scene 1.
        /// </summary>
        public static void ApplyFlaVanillaExportPatches(string flaPath, string ident)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return;

            var name = string.IsNullOrWhiteSpace(ident)
                ? Path.GetFileNameWithoutExtension(flaPath) ?? "character"
                : ident.Trim();
            var dir = Path.GetDirectoryName(flaPath) ?? "";
            // Bitmaps stay compressed in the zip. Loading them just to write them back was the slow part.
            var entries = ReadFlaZipXmlEntries(flaPath);
            // Extra methods like setupTaunt must land before {ident}_fla linkage is stripped.
            var injected = InjectLeftoverFlaMethodsInEntries(entries, dir);
            var n = ApplyFlaLibraryXmlFixes(entries, name);
            if (injected > 0 || n > 0)
                ReplaceFlaZipXmlEntries(flaPath, entries);
            DebugAgentLog.Write("D", "JpexsCliService.ApplyFlaVanillaExportPatches",
                "leftover extra methods inject",
                new
                {
                    flaPath,
                    ident = name,
                    leftoverInjected = injected,
                    xmlFixes = n
                });
        }

        private static int ApplyFlaLibraryXmlFixes(Dictionary<string, byte[]> entries, string ident)
        {
            var n = 0;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = DeduplicateXmlAttributes(xml);
                var file = Path.GetFileName(key.Replace('\\', '/'));
                if (file.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase))
                    next = MergeCommonIntoDomSourcePath(next);
                if (file.Equals("PublishSettings.xml", StringComparison.OrdinalIgnoreCase))
                {
                    next = MergeCommonIntoAs3PackagePaths(next);
                    next = ApplyPublishSoundQuality(next);
                }
                if (key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(ident))
                        next = StripTimelineFlaExportLinkageFromSymbolXml(next, ident);
                    if (next.IndexOf("Actionscript", StringComparison.OrdinalIgnoreCase) >= 0)
                        next = SanitizeSymbolActionsSelfBindings(next);
                }
                if (string.Equals(next, xml, StringComparison.Ordinal))
                    continue;
                entries[key] = Encoding.UTF8.GetBytes(next);
                n++;
            }
            return n;
        }

        private static void PatchZipEntryInPlace(ZipArchive zip, ZipArchiveEntry entry, Func<string, string> transform)
        {
            string xml;
            using (var reader = new StreamReader(entry.Open()))
                xml = reader.ReadToEnd();
            var next = transform(xml);
            if (string.Equals(next, xml, StringComparison.Ordinal))
                return;
            var name = entry.FullName;
            entry.Delete();
            var created = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var writer = new StreamWriter(created.Open());
            writer.Write(next);
        }

        public static string MergeCommonIntoAs3PackagePaths(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return xml;
            if (Regex.IsMatch(xml, @"<AS3PackagePaths>[^<]*common", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return NormalizeCommonSourcePathCase(xml);
            if (Regex.IsMatch(xml, @"<AS3PackagePaths>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    @"<AS3PackagePaths>(.*?)</AS3PackagePaths>",
                    m => "<AS3PackagePaths>" + AppendCommonSourcePath(m.Groups[1].Value) + "</AS3PackagePaths>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            }
            return xml;
        }

        public static string MergeCommonIntoDomSourcePath(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return xml;
            if (Regex.IsMatch(xml, @"as3SourcePath\s*=\s*""[^""]*common", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return NormalizeCommonSourcePathCase(xml);
            if (Regex.IsMatch(xml, @"as3SourcePath\s*=\s*""[^""]*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    @"as3SourcePath\s*=\s*""([^""]*)""",
                    m => "as3SourcePath=\"" + AppendCommonSourcePath(m.Groups[1].Value) + "\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return Regex.Replace(
                xml,
                @"<DOMDocument\b",
                "<DOMDocument as3SourcePath=\".;common\"",
                RegexOptions.CultureInvariant);
        }

        private static string AppendCommonSourcePath(string existing)
        {
            var cur = (existing ?? "").Trim();
            if (string.IsNullOrEmpty(cur) || cur == ".")
                return ".;common";
            if (cur.IndexOf("common", StringComparison.OrdinalIgnoreCase) >= 0)
                return Regex.Replace(cur, "Common", "common", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return cur.TrimEnd(';', '\\', '/') + ";common";
        }

        private static string NormalizeCommonSourcePathCase(string xml) =>
            Regex.Replace(xml ?? "", "Common", "common", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool IsRootOrFlaPackageSidecar(string outputDirectory, string asFile)
        {
            var parent = Path.GetDirectoryName(asFile);
            if (string.IsNullOrEmpty(parent))
                return false;
            if (string.Equals(
                    Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return true;
            var folder = Path.GetFileName(parent);
            return folder.EndsWith("_fla", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Deletes ctor-only linkage stubs under the FLA folder (root and <c>{id}_fla</c>).
        /// Extra methods are injected onto clip timelines. Clip leftover <c>.as</c> files that
        /// mention <c>addEventListener</c> (e.g. jab jump-cancel) still inject.
        /// </summary>
        public static int PruneEmptyFlaSidecars(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return 0;
            var deleted = 0;
            foreach (var asFile in Directory.GetFiles(outputDirectory, "*.as", SearchOption.AllDirectories))
            {
                if (!IsRootOrFlaPackageSidecar(outputDirectory, asFile))
                    continue;
                if (ShouldPreserveFlaSidecar(asFile))
                    continue;
                string text;
                try { text = File.ReadAllText(asFile); }
                catch { continue; }
                var className = ParseAs3ClassName(text) ?? Path.GetFileNameWithoutExtension(asFile) ?? "";
                if (!IsEmptyFlaSidecar(text, className))
                    continue;
                try
                {
                    File.Delete(asFile);
                    deleted++;
                }
                catch { /* locked */ }
            }
            return deleted;
        }

        /// <summary>
        /// Whether a root-level <c>.as</c> sidecar should remain external to the FLA binary.
        /// </summary>
        public static bool ShouldKeepExternalFlaScript(
            string asFilePath,
            string className,
            string baseName,
            IReadOnlySet<string> symbolClassNames)
        {
            if (!symbolClassNames.Contains(className))
                return true;
            return ExtendsSsf2Api(asFilePath, baseName);
        }

        /// <summary>
        /// Removes FFDec sidecar dumps for scripts that belong inside the FLA (SymbolClass-linked
        /// movieclips and timeline action layers). Keeps external API/document scripts and <c>fl/</c>.
        /// </summary>
        public static void PruneFlaSidecars(
            string outputDirectory,
            string baseName,
            IReadOnlySet<string> symbolClassNames)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return;
            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();
            TryDeleteDirectory(Path.Combine(outputDirectory, name + "_fla"));

            foreach (var asFile in Directory.GetFiles(outputDirectory, "*.as"))
            {
                var className = Path.GetFileNameWithoutExtension(asFile);
                if (ShouldKeepExternalFlaScript(asFile, className, name, symbolClassNames))
                    continue;
                try { File.Delete(asFile); } catch { /* locked */ }
            }
        }

        /// <summary>
        /// Copies the Animate JSFL cleanup script next to the exported FLA so it is
        /// easy to run from Commands ? Run Command.
        /// </summary>
        public static void TryCopyFlaCleanupJsfl(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return;
            var tools = Path.Combine(outputDirectory, "tools");
            Directory.CreateDirectory(tools);
            CopyJsflIfFound(tools, "ssf2mm_fla_cleanup.jsfl");
            CopyJsflIfFound(tools, "ssf2mm_fla_merge_boxes.jsfl");
        }

        /// <summary>
        /// Copies <c>FlaStepWizard.exe</c> into the FLA folder (next to the .fla).
        /// </summary>
        public static void TryCopyFlaStepWizard(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return;
            var dest = Path.Combine(outputDirectory, "FlaStepWizard.exe");
            foreach (var src in EnumerateFlaStepWizardPaths())
            {
                if (!File.Exists(src))
                    continue;
                try
                {
                    File.Copy(src, dest, overwrite: true);
                    return;
                }
                catch { /* locked / in use */ }
            }
        }

        private static IEnumerable<string> EnumerateFlaStepWizardPaths()
        {
            const string file = "FlaStepWizard.exe";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in AppPaths.EnumerateSidecarRoots())
            {
                foreach (var path in new[]
                         {
                             Path.Combine(root, file),
                             Path.Combine(root, "tools", file),
                             Path.Combine(root, "tools", "FlaStepWizard", file)
                         })
                {
                    if (seen.Add(path))
                        yield return path;
                }
            }

            foreach (var baseDir in new[] { AppPaths.ExeDirectory, AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(baseDir);
                for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                {
                    foreach (var path in new[]
                             {
                                 Path.Combine(dir.FullName, "src", "tools", "FlaStepWizard", "dist", file),
                                 Path.Combine(dir.FullName, "src", "tools", "FlaStepWizard", "bin", "Release", "win-x64", "publish", file),
                                 Path.Combine(dir.FullName, "src", "tools", "FlaStepWizard", "bin", "Release", file)
                             })
                    {
                        if (seen.Add(path))
                            yield return path;
                    }
                }
            }
        }

        private static void CopyJsflIfFound(string toolsDir, string file)
        {
            var dest = Path.Combine(toolsDir, file);
            foreach (var src in EnumerateJsflPaths(file))
            {
                if (!File.Exists(src))
                    continue;
                try
                {
                    File.Copy(src, dest, overwrite: true);
                    return;
                }
                catch { /* locked / in use */ }
            }
        }

        /// <summary>
        /// Starts Adobe Animate with the cleanup JSFL. The script opens the sibling FLA,
        /// organizes, saves, and leaves the document open.
        /// </summary>
        public static bool TryLaunchFlaCleanupJsfl(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return false;
            var jsfl = Path.Combine(outputDirectory, "tools", "ssf2mm_fla_cleanup.jsfl");
            if (!File.Exists(jsfl))
                return false;
            var animate = FindAnimateExe();
            if (string.IsNullOrEmpty(animate))
                return false;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = animate,
                    ArgumentList = { jsfl },
                    UseShellExecute = false
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string? FindAnimateExe()
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                var adobe = Path.Combine(root, "Adobe");
                if (!Directory.Exists(adobe))
                    continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(adobe, "Adobe Animate*"); }
                catch { continue; }
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (var i = dirs.Length - 1; i >= 0; i--)
                {
                    var exe = Path.Combine(dirs[i], "Animate.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateFlaCleanupJsflPaths() =>
            EnumerateJsflPaths("ssf2mm_fla_cleanup.jsfl");

        private static IEnumerable<string> EnumerateJsflPaths(string file)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in AppPaths.EnumerateSidecarRoots())
            {
                foreach (var path in new[]
                         {
                             Path.Combine(root, "jsfl", file),
                             Path.Combine(root, "tools", "jsfl", file)
                         })
                {
                    if (seen.Add(path))
                        yield return path;
                }
            }

            var dir = new DirectoryInfo(AppPaths.ExeDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                foreach (var path in new[]
                         {
                             Path.Combine(dir.FullName, "src", "tools", "jsfl", file),
                             Path.Combine(dir.FullName, "tools", "jsfl", file)
                         })
                {
                    if (seen.Add(path))
                        yield return path;
                }
            }
        }

        private static bool ExtendsSsf2Api(string asFilePath, string baseName)
        {
            string text;
            try { text = File.ReadAllText(asFilePath); }
            catch { return false; }
            if (Regex.IsMatch(text, @"extends\s+SSF2\w+", RegexOptions.IgnoreCase))
                return true;
            var stubPrefix = Regex.Escape(baseName) + "_SSF2";
            return Regex.IsMatch(text, $@"extends\s+{stubPrefix}\w*", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Promotes FFDec's nested <c>{name}.fla/{name}.fla</c> binary (or XFL children) into
        /// <paramref name="outputDirectory"/>. All sidecar files and folders are moved alongside it;
        /// <see cref="PruneFlaSidecars"/> then drops SymbolClass-linked dumps that belong in the FLA.
        /// </summary>
        public static string FlattenFlaExport(string outputDirectory, string baseName)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();
            var nested = Path.Combine(outputDirectory, name + ".fla");
            if (File.Exists(nested) && !Directory.Exists(nested))
                return nested;
            if (!Directory.Exists(nested))
                throw new InvalidOperationException("FLA export did not produce " + nested);

            var innerFla = Path.Combine(nested, name + ".fla");
            if (!File.Exists(innerFla))
            {
                var found = Directory.GetFiles(nested, "*.fla");
                if (found.Length == 1)
                    innerFla = found[0];
            }
            if (File.Exists(innerFla))
            {
                // Promote every sibling of the inner binary FLA up to outputDirectory,
                // then promote the FLA itself last.
                var tmp = Path.Combine(outputDirectory, name + ".fla.promote");
                if (File.Exists(tmp)) File.Delete(tmp);
                File.Move(innerFla, tmp);
                foreach (var entry in Directory.GetFileSystemEntries(nested))
                {
                    if (string.Equals(entry, innerFla, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var fileName = Path.GetFileName(entry);
                    var finalDest = Path.Combine(outputDirectory, fileName);
                    if (Directory.Exists(finalDest)) Directory.Delete(finalDest, recursive: true);
                    else if (File.Exists(finalDest)) File.Delete(finalDest);
                    if (Directory.Exists(entry))
                        Directory.Move(entry, finalDest);
                    else
                        File.Move(entry, finalDest);
                }
                Directory.Delete(nested, recursive: true);
                var dest = Path.Combine(outputDirectory, name + ".fla");
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return dest;
            }

            foreach (var entry in Directory.GetFileSystemEntries(nested))
            {
                var dest = Path.Combine(outputDirectory, Path.GetFileName(entry));
                if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
                else if (File.Exists(dest)) File.Delete(dest);
                if (Directory.Exists(entry))
                    Directory.Move(entry, dest);
                else
                    File.Move(entry, dest);
            }
            Directory.Delete(nested, recursive: true);
            return outputDirectory;
        }

        private static void CopyFlattenedFlaOutput(string sourceDirectory, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (var entry in Directory.GetFileSystemEntries(sourceDirectory))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(outputDirectory, name);
                if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
                else if (File.Exists(dest)) File.Delete(dest);

                if (Directory.Exists(entry))
                    CopyDirectory(entry, dest);
                else
                    File.Copy(entry, dest, overwrite: true);
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            try { Directory.Delete(path, recursive: true); }
            catch { /* ignore locked */ }
        }

        /// <summary>
        /// Resolves the FLA basename so temp SWF copies (e.g. <c>ssf2mm-fla-�.swf</c>) can still
        /// export as <c>{id}.fla</c>.
        /// </summary>
        public static string ResolveFlaOutputBaseName(string swfPath, string? outputBaseName)
        {
            var baseName = !string.IsNullOrWhiteSpace(outputBaseName)
                ? outputBaseName.Trim()
                : Path.GetFileNameWithoutExtension(swfPath);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "character";
            foreach (var c in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(c, '_');
            return baseName;
        }

        private static readonly Regex StanceInstanceRegex = new(
            @"<DOMSymbolInstance\b[^>]*\bname=""stance""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LinkageClassNameRegex = new(
            @"\blinkageClassName=""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex AddFrameScriptRegex = new(
            @"addFrameScript\s*\((?<args>[^)]*)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex FunctionKeywordRegex = new(
            @"(?:public\s+|private\s+|protected\s+|internal\s+)?(?:override\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ParamNRegex = new(
            @"\bparam(\d+)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Bare <c>self</c> identifier, not <c>this.self</c>.</summary>
        private static readonly Regex BareSelfRegex = new(
            @"(?<![\w.])self\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SelfVarDeclRegex = new(
            @"\b(?:var|const)\s+self\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal const string TimelineSelfBindingDecl =
            "var self:* = this.self != null ? this.self : this;\n";

        internal const string TimelineSelfBindingAssign =
            "self = this.self != null ? this.self : this;\n";

        /// <summary>
        /// Injects timeline Actions from decompiled <c>{base}_fla</c> classes into LIBRARY
        /// symbols, then strips those symbols' AS3 export linkage (reference-style).
        /// </summary>
        /// <returns>Number of LIBRARY symbols rebuilt.</returns>
        public static int RebuildTimelineFlaAnimations(string flaPath, string baseName, string scriptRoot)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath) ||
                string.IsNullOrWhiteSpace(scriptRoot) || !Directory.Exists(scriptRoot))
                return 0;

            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();
            var prefix = name + "_fla.";
            var classFiles = IndexFlaClassScripts(scriptRoot, name);
            if (classFiles.Count == 0)
                return 0;

            var patches = new List<(string EntryName, string Content)>();
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                        !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string xml;
                    using (var reader = new StreamReader(entry.Open()))
                        xml = reader.ReadToEnd();

                    var classMatch = LinkageClassNameRegex.Match(xml);
                    if (!classMatch.Success)
                        continue;
                    var fullClass = classMatch.Groups[1].Value;
                    if (!fullClass.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    var shortName = fullClass.Substring(prefix.Length);
                    if (!classFiles.TryGetValue(shortName, out var asPath))
                        continue;

                    string asText;
                    try { asText = File.ReadAllText(asPath); }
                    catch { continue; }

                    var withActions = InjectActionsLayerFromFlaClass(xml, asText);
                    var stripped = StripTimelineFlaExportLinkageFromSymbolXml(withActions, name);
                    if (!string.Equals(stripped, xml, StringComparison.Ordinal))
                        patches.Add((entry.FullName, stripped));
                }
            }

            foreach (var (entryName, content) in patches)
                ReplaceZipEntry(flaPath, entryName, content);

            return patches.Count;
        }

        /// <summary>
        /// Maps short class names under <c>{baseName}_fla</c> to their exported <c>.as</c> paths.
        /// </summary>
        public static Dictionary<string, string> IndexFlaClassScripts(string scriptRoot, string baseName)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(scriptRoot) || !Directory.Exists(scriptRoot))
                return map;

            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();
            var packageFolder = name + "_fla";
            foreach (var asFile in Directory.GetFiles(scriptRoot, "*.as", SearchOption.AllDirectories))
            {
                var shortName = Path.GetFileNameWithoutExtension(asFile);
                if (string.IsNullOrEmpty(shortName))
                    continue;

                var dir = Path.GetFileName(Path.GetDirectoryName(asFile) ?? "");
                if (string.Equals(dir, packageFolder, StringComparison.OrdinalIgnoreCase) ||
                    asFile.Contains(packageFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    asFile.Contains(packageFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    map[shortName] = asFile;
                    continue;
                }

                // Fallback: file declares package {base}_fla
                try
                {
                    var head = File.ReadAllText(asFile);
                    if (head.Length > 4000) head = head.Substring(0, 4000);
                    if (Regex.IsMatch(head, @"package\s+" + Regex.Escape(packageFolder) + @"\b"))
                        map[shortName] = asFile;
                }
                catch { /* ignore */ }
            }
            return map;
        }

        /// <summary>
        /// Inserts/replaces an Actions layer built from a decompiled <c>_fla</c> class.
        /// </summary>
        public static string InjectActionsLayerFromFlaClass(string libraryXml, string asText)
        {
            var actionsLayer = BuildActionsLayerFromFlaClass(asText);
            if (string.IsNullOrWhiteSpace(actionsLayer))
                return libraryXml;

            XDocument doc;
            try { doc = XDocument.Parse(libraryXml, LoadOptions.PreserveWhitespace); }
            catch { return libraryXml; }

            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var layersEl = doc.Descendants(ns + "layers").FirstOrDefault();
            if (layersEl == null)
                return libraryXml;

            XElement actionsEl;
            try { actionsEl = XElement.Parse(actionsLayer); }
            catch { return libraryXml; }

            // Re-namespace children to match the document.
            if (ns != XNamespace.None)
                actionsEl = RetargetXElementNamespace(actionsEl, ns);

            foreach (var existing in layersEl.Elements(ns + "DOMLayer")
                         .Where(el =>
                         {
                             var n = (string?)el.Attribute("name") ?? "";
                             return string.Equals(n, "Actions", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(n, "Script Layer", StringComparison.OrdinalIgnoreCase);
                         })
                         .ToList())
                existing.Remove();

            var labelLayer = layersEl.Elements(ns + "DOMLayer").FirstOrDefault(el =>
            {
                var n = (string?)el.Attribute("name") ?? "";
                return n.Contains("Label", StringComparison.OrdinalIgnoreCase);
            });
            if (labelLayer != null)
                labelLayer.AddAfterSelf(actionsEl);
            else
                layersEl.AddFirst(actionsEl);

            return doc.Declaration == null
                ? doc.ToString(SaveOptions.DisableFormatting)
                : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
        }

        private static XElement RetargetXElementNamespace(XElement el, XNamespace ns)
        {
            var copy = new XElement(ns + el.Name.LocalName,
                el.Attributes().Where(a => !a.IsNamespaceDeclaration));
            foreach (var node in el.Nodes())
            {
                if (node is XElement child)
                    copy.Add(RetargetXElementNamespace(child, ns));
                else
                    copy.Add(node);
            }
            return copy;
        }

        /// <summary>
        /// Builds an XFL Actions <c>DOMLayer</c> from FFDec decompiled timeline class source.
        /// </summary>
        public static string BuildActionsLayerFromFlaClass(string asText)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return "";

            var frameMap = ParseAddFrameScriptMap(asText);
            var methods = ExtractAs3Methods(asText);
            if (frameMap.Count == 0 && methods.Count == 0)
                return "";

            var helperNames = CollectHelperMethodNames(methods, frameMap);
            var helpers = new StringBuilder();
            foreach (var method in methods.Values)
            {
                if (frameMap.Values.Contains(method.Name, StringComparer.Ordinal))
                    continue;
                helpers.Append(method.FormatTimelineFunction());
            }
            var helperBlock = FixTimelineFunctionParameters(helpers.ToString());

            var frames = new StringBuilder();
            var declaredSelf = false;
            if (frameMap.Count == 0)
            {
                // No addFrameScript � put helpers (if any) on frame 0 only.
                if (helperBlock.Length == 0)
                    return "";
                var script0 = RewriteBareHelperReferences(helperBlock, helperNames);
                script0 = RewriteFrameScriptForTimeline(script0, declareSelf: true);
                script0 = FixTimelineFunctionParameters(script0);
                frames.Append(BuildDomFrameActionsXml(0, script0));
            }
            else
            {
                var first = true;
                foreach (var (frameIndex, methodName) in frameMap.OrderBy(kv => kv.Key))
                {
                    var body = methods.TryGetValue(methodName, out var extracted)
                        ? extracted.Body
                        : "";
                    var script = first && helperBlock.Length > 0
                        ? helperBlock + body
                        : body;
                    first = false;
                    script = RewriteBareHelperReferences(script, helperNames);
                    script = RewriteFrameScriptForTimeline(script, declareSelf: !declaredSelf);
                    script = FixTimelineFunctionParameters(script);
                    if (script.StartsWith(TimelineSelfBindingDecl, StringComparison.Ordinal) ||
                        SelfVarDeclRegex.IsMatch(script))
                        declaredSelf = true;
                    if (string.IsNullOrWhiteSpace(script))
                        continue;
                    frames.Append(BuildDomFrameActionsXml(frameIndex, script));
                }
            }

            if (frames.Length == 0)
                return "";

            return "<DOMLayer name=\"Actions\" color=\"#9933CC\" locked=\"true\" autoNamed=\"false\">" +
                   "<frames>" + frames + "</frames></DOMLayer>";
        }

        private static string BuildDomFrameActionsXml(int frameIndex, string script)
        {
            var escaped = EscapeCData(script);
            return "<DOMFrame index=\"" + frameIndex + "\" keyMode=\"9728\">" +
                   "<Actionscript><script><![CDATA[" + escaped + "]]></script></Actionscript>" +
                   "<elements/></DOMFrame>";
        }

        private static string EscapeCData(string text) =>
            text.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal);

        private static string UnescapeXml(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&', StringComparison.Ordinal) < 0)
                return text;
            return text
                .Replace("&lt;", "<", StringComparison.Ordinal)
                .Replace("&gt;", ">", StringComparison.Ordinal)
                .Replace("&quot;", "\"", StringComparison.Ordinal)
                .Replace("&apos;", "'", StringComparison.Ordinal)
                .Replace("&amp;", "&", StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses <c>addFrameScript(0, this.frame1, 11, frame12, ...)</c> into frame?method map.
        /// </summary>
        public static Dictionary<int, string> ParseAddFrameScriptMap(string asText)
        {
            var map = new Dictionary<int, string>();
            var m = AddFrameScriptRegex.Match(asText ?? "");
            if (!m.Success)
                return map;

            var args = m.Groups["args"].Value;
            var parts = SplitAs3CallArgs(args);
            for (var i = 0; i + 1 < parts.Count; i += 2)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
                    continue;
                var method = parts[i + 1].Trim();
                method = Regex.Replace(method, @"^this\.", "", RegexOptions.CultureInvariant);
                method = method.Trim();
                if (method.Length == 0)
                    continue;
                map[frame] = method;
            }
            return map;
        }

        private static List<string> SplitAs3CallArgs(string args)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            var depth = 0;
            foreach (var ch in args)
            {
                if (ch == '(' || ch == '[' || ch == '{') depth++;
                else if (ch == ')' || ch == ']' || ch == '}') depth = Math.Max(0, depth - 1);
                if (ch == ',' && depth == 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(ch);
            }
            if (sb.Length > 0)
                list.Add(sb.ToString());
            return list;
        }

        /// <summary>
        /// Decompiled AS3 method used when rebuilding timeline Actions.
        /// </summary>
        public readonly struct ExtractedAs3Method
        {
            public ExtractedAs3Method(string name, string parameterList, string returnType, string body)
            {
                Name = name;
                ParameterList = parameterList ?? "";
                ReturnType = string.IsNullOrWhiteSpace(returnType) ? "*" : returnType.Trim();
                Body = body ?? "";
            }

            public string Name { get; }
            public string ParameterList { get; }
            public string ReturnType { get; }
            public string Body { get; }

            /// <summary>
            /// Top-level <c>function</c> declarations � Animate hoists these to timeline
            /// class methods so <c>this.restoreSpecials()</c> / <c>this.uncrouch</c> work.
            /// </summary>
            public string FormatTimelineFunction() =>
                "function " + Name + "(" + ParameterList + "):" + ReturnType + " {\n" +
                (NeedsEventDataGuard(ParameterList, Body) ? "         if(!param1 || !param1.data) return;\n" : "") +
                Body.Trim() + "\n}\n\n";
        }

        private static HashSet<string> CollectHelperMethodNames(
            Dictionary<string, ExtractedAs3Method> methods,
            Dictionary<int, string> frameMap)
        {
            var helpers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods.Values)
            {
                if (!frameMap.Values.Contains(method.Name, StringComparer.Ordinal))
                    helpers.Add(method.Name);
            }
            return helpers;
        }

        /// <summary>
        /// FFDec sometimes emits bare helper ids (e.g. <c>destroyTimer(xSpeedDecay)</c>).
        /// Skips string literals so labels like <c>stancePlayFrame("uncrouch")</c> stay intact.
        /// </summary>
        public static string RewriteBareHelperReferences(string body, IReadOnlyCollection<string> helperNames)
        {
            if (string.IsNullOrWhiteSpace(body) || helperNames == null || helperNames.Count == 0)
                return body ?? "";

            var names = helperNames
                .Where(n => !string.IsNullOrEmpty(n))
                .Where(n => !Regex.IsMatch(body, @"\bfunction\s+" + Regex.Escape(n) + @"\s*\("))
                .OrderByDescending(n => n.Length)
                .ToList();
            if (names.Count == 0)
                return body;

            var sb = new StringBuilder(body.Length + 16);
            for (var i = 0; i < body.Length;)
            {
                var ch = body[i];
                if (ch == '"' || ch == '\'')
                {
                    var end = ReadQuotedLiteralEnd(body, i);
                    sb.Append(body, i, end - i);
                    i = end;
                    continue;
                }

                var replaced = false;
                foreach (var name in names)
                {
                    if (i + name.Length > body.Length ||
                        !body.AsSpan(i, name.Length).SequenceEqual(name.AsSpan()))
                        continue;
                    if (i > 0 && IsIdentChar(body[i - 1]))
                        continue;
                    if (i + name.Length < body.Length && IsIdentChar(body[i + name.Length]))
                        continue;
                    if (IsFollowedByCallParen(body, i + name.Length))
                        continue;

                    sb.Append("this.").Append(name);
                    i += name.Length;
                    replaced = true;
                    break;
                }

                if (!replaced)
                {
                    sb.Append(ch);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static bool IsIdentChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '.';

        private static bool IsFollowedByCallParen(string text, int pos)
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                pos++;
            return pos < text.Length && text[pos] == '(';
        }

        private static int ReadQuotedLiteralEnd(string text, int start)
        {
            var quote = text[start];
            var i = start + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }
                if (text[i] == quote)
                    return i + 1;
                i++;
            }
            return text.Length;
        }

        private static bool NeedsEventDataGuard(string parameterList, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;
            if (!Regex.IsMatch(body, @"\bparam1\.data\b", RegexOptions.CultureInvariant))
                return false;
            if (Regex.IsMatch(parameterList ?? "", @"\bparam1\b", RegexOptions.CultureInvariant))
                return true;
            return Regex.IsMatch(body, @"\bparam1\b", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Converts mistaken <c>this.name = function</c> back to hoisted <c>function name</c>.
        /// </summary>
        public static string ConvertMethodAssignmentsToTimelineFunctions(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return script ?? "";

            return Regex.Replace(
                script,
                @"this\.([A-Za-z_][A-Za-z0-9_]*)\s*=\s*function\s*\(",
                "function $1(",
                RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Timer helpers must stay hoisted <c>function xSpeedDecay()</c>. Converting them
        /// to <c>this.x = function</c> makes IntervalTimer capture undefined (#1006).
        /// </summary>
        public static string ConvertTimelineFunctionsToMethodAssignments(string script) =>
            script ?? "";

        /// <summary>
        /// Extracts <c>function name(...) { body }</c> declarations from AS3 class text.
        /// </summary>
        public static Dictionary<string, ExtractedAs3Method> ExtractAs3Methods(string asText)
        {
            var map = new Dictionary<string, ExtractedAs3Method>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(asText))
                return map;

            var className = Regex.Match(asText, @"\bclass\s+(?<c>[A-Za-z_][A-Za-z0-9_]*)").Groups["c"].Value;

            foreach (Match m in FunctionKeywordRegex.Matches(asText))
            {
                var methodName = m.Groups["name"].Value;
                if (!string.IsNullOrEmpty(className) &&
                    string.Equals(className, methodName, StringComparison.Ordinal))
                    continue; // constructor

                var openParen = m.Index + m.Length - 1;
                var closeParen = FindMatchingDelimiter(asText, openParen, '(', ')');
                if (closeParen < 0)
                    continue;
                var parameterList = asText.Substring(openParen + 1, closeParen - openParen - 1).Trim();

                var i = closeParen + 1;
                while (i < asText.Length && char.IsWhiteSpace(asText[i])) i++;
                var returnType = "*";
                if (i < asText.Length && asText[i] == ':')
                {
                    i++;
                    var typeStart = i;
                    while (i < asText.Length && asText[i] != '{') i++;
                    returnType = asText.Substring(typeStart, i - typeStart).Trim();
                }
                while (i < asText.Length && char.IsWhiteSpace(asText[i])) i++;
                if (i >= asText.Length || asText[i] != '{')
                    continue;

                var body = ExtractBalancedBlockBody(asText, i);
                if (body == null)
                    continue;
                map[methodName] = new ExtractedAs3Method(methodName, parameterList, returnType, body);
            }
            return map;
        }

        private static int FindMatchingDelimiter(string text, int openIndex, char open, char close)
        {
            if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != open)
                return -1;
            var depth = 0;
            for (var i = openIndex; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == open) depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private static readonly Regex TimelineMethodAssignRegex = new(
            @"this\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*function\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Adds missing FFDec <c>paramN</c> arguments to timeline <c>function</c> helpers
        /// (event listeners keep <c>param1</c> after the class wrapper is stripped).
        /// </summary>
        public static string FixTimelineFunctionParameters(string script)
        {
            if (string.IsNullOrWhiteSpace(script) || script.IndexOf("function", StringComparison.Ordinal) < 0)
                return script ?? "";

            var sb = new StringBuilder(script.Length + 32);
            var i = 0;
            while (i < script.Length)
            {
                var assign = TimelineMethodAssignRegex.Match(script, i);
                var named = FunctionKeywordRegex.Match(script, i);
                if (!assign.Success && !named.Success)
                {
                    sb.Append(script, i, script.Length - i);
                    break;
                }

                Match m;
                string name;
                int openParen;
                if (assign.Success && (!named.Success || assign.Index <= named.Index))
                {
                    m = assign;
                    name = m.Groups["name"].Value;
                    openParen = m.Index + m.Length - 1;
                }
                else
                {
                    m = named;
                    name = m.Groups["name"].Value;
                    openParen = m.Index + m.Length - 1;
                }

                sb.Append(script, i, m.Index - i);
                var closeParen = FindMatchingDelimiter(script, openParen, '(', ')');
                if (closeParen < 0)
                {
                    sb.Append(script, m.Index, script.Length - m.Index);
                    break;
                }

                var parameterList = script.Substring(openParen + 1, closeParen - openParen - 1);
                var cursor = closeParen + 1;
                while (cursor < script.Length && char.IsWhiteSpace(script[cursor])) cursor++;
                var returnType = "*";
                if (cursor < script.Length && script[cursor] == ':')
                {
                    cursor++;
                    var typeStart = cursor;
                    while (cursor < script.Length && script[cursor] != '{') cursor++;
                    returnType = script.Substring(typeStart, cursor - typeStart).Trim();
                    if (string.IsNullOrEmpty(returnType))
                        returnType = "*";
                }
                while (cursor < script.Length && char.IsWhiteSpace(script[cursor])) cursor++;
                if (cursor >= script.Length || script[cursor] != '{')
                {
                    sb.Append(script, m.Index, closeParen + 1 - m.Index);
                    i = closeParen + 1;
                    continue;
                }

                var body = ExtractBalancedBlockBody(script, cursor);
                if (body == null)
                {
                    sb.Append(script, m.Index, cursor + 1 - m.Index);
                    i = cursor + 1;
                    continue;
                }

                var closeBrace = cursor + 1 + body.Length;
                var fixedParams = EnsureParamNArguments(parameterList, body);
                if (NeedsEventDataGuard(fixedParams, body) &&
                    !Regex.IsMatch(body, @"if\s*\(\s*!param1\s*\|\|\s*!param1\.data\s*\)", RegexOptions.CultureInvariant))
                {
                    body = "         if(!param1 || !param1.data) return;\n" + body;
                }
                sb.Append("function ").Append(name).Append('(').Append(fixedParams).Append("):")
                    .Append(returnType).Append(" {").Append(body).Append('}');
                i = closeBrace + 1;
            }

            return sb.ToString();
        }

        private static string EnsureParamNArguments(string existingParams, string body)
        {
            var parts = new List<string>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in SplitAs3CallArgs(existingParams ?? ""))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                    continue;
                parts.Add(part);
                var id = Regex.Match(part, @"^[A-Za-z_][A-Za-z0-9_]*").Value;
                if (id.Length > 0)
                    names.Add(id);
            }

            var maxN = 0;
            foreach (Match pm in ParamNRegex.Matches(body ?? ""))
            {
                if (int.TryParse(pm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                    n > maxN)
                    maxN = n;
            }

            for (var n = 1; n <= maxN; n++)
            {
                var id = "param" + n.ToString(CultureInfo.InvariantCulture);
                if (names.Contains(id))
                    continue;
                parts.Add(id + ":*");
                names.Add(id);
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Given index of '{', returns inner body until matching '}'.
        /// </summary>
        private static string? ExtractBalancedBlockBody(string text, int openBraceIndex)
        {
            if (openBraceIndex < 0 || openBraceIndex >= text.Length || text[openBraceIndex] != '{')
                return null;
            var depth = 0;
            for (var i = openBraceIndex; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(openBraceIndex + 1, i - openBraceIndex - 1);
                }
            }
            return null;
        }

        /// <summary>
        /// Makes decompiled frame bodies safe on a bare timeline (no class scope).
        /// AS3 frame scripts share one namespace, so <c>var self</c> may be declared
        /// only once per clip (<paramref name="declareSelf"/>).
        /// </summary>
        public static string RewriteFrameScriptForTimeline(string body, bool declareSelf = true)
        {
            if (string.IsNullOrWhiteSpace(body))
                return body ?? "";

            var text = body.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!BareSelfRegex.IsMatch(text) || SelfVarDeclRegex.IsMatch(text))
                return text;

            return (declareSelf ? TimelineSelfBindingDecl : TimelineSelfBindingAssign) + text;
        }

        /// <summary>
        /// Timeline closures cannot see sibling methods like <c>xSpeedDecay</c>.
        /// Capture them as locals so Animate Error 1120 does not fire.
        /// </summary>
        public static string CaptureClosureTimerHelpers(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return body ?? "";

            foreach (Match fm in Regex.Matches(body, @"\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("))
            {
                var hid = fm.Groups[1].Value;
                body = Regex.Replace(
                    body,
                    @"(?m)^\s*var\s+" + Regex.Escape(hid) + @":\*\s*=\s*this\." + Regex.Escape(hid) + @";\s*\n?",
                    "");
            }

            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in ClosureTimerHelperRegex.Matches(body))
            {
                var id = m.Groups["id"].Value;
                if (string.IsNullOrEmpty(id) ||
                    id is "self" or "null" or "undefined")
                    continue;
                if (Regex.IsMatch(body, @"\b(?:var|function)\s+" + Regex.Escape(id) + @"\b"))
                    continue;
                ids.Add(id);
            }

            if (ids.Count == 0)
                return body;

            var decls = new StringBuilder();
            foreach (var id in ids)
                decls.Append("var ").Append(id).Append(":* = this.").Append(id).Append(";\n");
            return decls + body;
        }

        /// <summary>
        /// Animate Error 5000 wants a MovieClip import on the document class sidecar.
        /// </summary>
        public static string PatchFlaDocumentClassMainAs(string asText)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return asText ?? "";
            if (!Regex.IsMatch(asText, @"\bclass\s+Main\b"))
                return asText;
            if (Regex.IsMatch(asText, @"import\s+flash\.display\.MovieClip\b"))
                return asText;
            return Regex.Replace(
                asText,
                @"package(?:\s+[A-Za-z_][A-Za-z0-9_.]*)?\s*\{",
                m => m.Value + "\n   import flash.display.MovieClip;",
                RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Fully-qualifies <c>extends MovieClip</c> so Animate resolves the document-class lineage.
        /// </summary>
        public static string PatchSsf2AssetExtendsMovieClip(string asText)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return asText ?? "";
            return Regex.Replace(
                asText,
                @"extends\s+MovieClip\b",
                "extends flash.display.MovieClip",
                RegexOptions.CultureInvariant);
        }

        private static readonly Regex ActionsScriptBlockRegex = new(
            @"<Actionscript>\s*<script>\s*(?:<!\[CDATA\[(?<body>.*?)\]\]>|(?<body>.*?))\s*</script>\s*</Actionscript>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DuplicateXmlAttributeTagRegex = new(
            @"<(?<slash>/?)(?<name>[A-Za-z_][\w:.-]*)(?<attrs>(?:\s+\w+\s*=\s*""[^""]*"")+)(?<tail>\s*/?)>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex XmlAttributePairRegex = new(
            @"\s+(?<n>\w+)\s*=\s*""(?<v>[^""]*)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ClosureTimerHelperRegex = new(
            @"\b(?:destroyTimer|removeTimer)\s*\(\s*(?!this\.)(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*\)" +
            @"|\bcreateTimer\s*\([^,]+,[^,]+,\s*(?!this\.)(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static HashSet<string> CollectTimelineHelperNames(string script)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(script))
                return names;
            foreach (Match m in TimelineMethodAssignRegex.Matches(script))
                names.Add(m.Groups["name"].Value);
            foreach (Match m in FunctionKeywordRegex.Matches(script))
                names.Add(m.Groups["name"].Value);
            return names;
        }

        /// <summary>
        /// Removes duplicate <c>var self</c> bindings from an already-injected Actions layer
        /// (Error 1151). Safe to run more than once.
        /// </summary>
        public static string SanitizeSymbolActionsSelfBindings(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<Actionscript", StringComparison.OrdinalIgnoreCase) < 0)
                return xml;

            var combined = new StringBuilder();
            foreach (Match m in ActionsScriptBlockRegex.Matches(xml))
                combined.Append(m.Groups["body"].Value.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');
            var helperNames = CollectTimelineHelperNames(
                ConvertMethodAssignmentsToTimelineFunctions(combined.ToString()));

            var declared = false;
            return ActionsScriptBlockRegex.Replace(xml, m =>
            {
                var body = UnescapeXml(
                    m.Groups["body"].Value.Replace("\r\n", "\n", StringComparison.Ordinal));
                body = ConvertMethodAssignmentsToTimelineFunctions(body);
                body = FixTimelineFunctionParameters(body);
                body = Regex.Replace(
                    body,
                    @"^\s*var\s+self:\*\s*=\s*this\.self\s*!=\s*null\s*\?\s*this\.self\s*:\s*this;\s*\n?",
                    "",
                    RegexOptions.CultureInvariant);
                body = Regex.Replace(
                    body,
                    @"^\s*self\s*=\s*this\.self\s*!=\s*null\s*\?\s*this\.self\s*:\s*this;\s*\n?",
                    "",
                    RegexOptions.CultureInvariant);
                body = RewriteBareHelperReferences(body, helperNames);
                var next = RewriteFrameScriptForTimeline(body, declareSelf: !declared);
                next = CaptureClosureTimerHelpers(next);
                if (next.StartsWith(TimelineSelfBindingDecl, StringComparison.Ordinal) ||
                    SelfVarDeclRegex.IsMatch(next))
                    declared = true;
                return "<Actionscript><script><![CDATA[" + EscapeCData(next) + "]]></script></Actionscript>";
            });
        }

        /// <summary>
        /// Walks LIBRARY symbols and dedupes <c>var self</c> on Actions frames.
        /// Also strips duplicate XML attributes that make Animate refuse Scene 1.
        /// </summary>
        public static int SanitizeFlaTimelineSelfBindings(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;

            var ident = Path.GetFileNameWithoutExtension(flaPath) ?? "";
            var entries = ReadFlaZipEntries(flaPath);
            var n = ApplyFlaLibraryXmlFixes(entries, ident);
            if (n > 0)
                PatchFlaZipEntries(flaPath, entries);
            return n;
        }

        /// <summary>
        /// JPEXS emits duplicate attributes such as <c>locked="true" locked="true"</c>.
        /// Animate then fails with "Could not load scene into memory" when that clip
        /// is on Scene 1. Keep the first value; do not drop Scene 1.
        /// </summary>
        public static string DeduplicateXmlAttributes(string xml)
        {
            if (string.IsNullOrEmpty(xml) || xml.IndexOf("=\"", StringComparison.Ordinal) < 0)
                return xml;

            return DuplicateXmlAttributeTagRegex.Replace(xml, m =>
            {
                var attrs = m.Groups["attrs"].Value;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var rebuilt = new StringBuilder();
                var dup = false;
                foreach (Match a in XmlAttributePairRegex.Matches(attrs))
                {
                    if (!seen.Add(a.Groups["n"].Value))
                    {
                        dup = true;
                        continue;
                    }
                    rebuilt.Append(' ')
                        .Append(a.Groups["n"].Value)
                        .Append("=\"")
                        .Append(a.Groups["v"].Value)
                        .Append('"');
                }
                if (!dup)
                    return m.Value;
                return "<" + m.Groups["slash"].Value + m.Groups["name"].Value + rebuilt +
                       m.Groups["tail"].Value + ">";
            });
        }

        private static readonly Regex StanceInstanceOpenTagRegex = new(
            @"<DOMSymbolInstance\b(?<a>[^>]*)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LibraryItemNameAttrRegex = new(
            @"\blibraryItemName=""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BitmapRefAttrRegex = new(
            @"\b(?:bitmapPath|href|src)=""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ExtraAs3VarRegex = new(
            @"\b(?:public|protected|private|internal)?\s*var\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*(?<t>[A-Za-z_][A-Za-z0-9_.*]*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Library item names placed as <c>name="stance"</c> on the fighter host.
        /// </summary>
        public static HashSet<string> CollectStanceChildLibraryItemNames(string hostXml)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(hostXml))
                return names;
            foreach (Match m in StanceInstanceOpenTagRegex.Matches(hostXml))
            {
                var attrs = m.Groups["a"].Value;
                if (!Regex.IsMatch(attrs, @"\bname=""stance""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    continue;
                var lib = LibraryItemNameAttrRegex.Match(attrs);
                if (lib.Success)
                    names.Add(lib.Groups[1].Value);
            }
            return names;
        }

        /// <summary>
        /// Writes <c>ssf2mm_organize.txt</c> (folder TAB item [TAB newName]).
        /// Always overwrites so JSFL does not reuse a stale list after C# zip organize.
        /// </summary>
        public static int WriteFlaOrganizeBlueprint(string flaPath, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return 0;
            var tsv = string.IsNullOrWhiteSpace(flaPath) ? "" : BuildFlaOrganizeBlueprint(flaPath);
            var tools = Path.Combine(outputDirectory, "tools");
            Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(tools, "ssf2mm_organize.txt"), tsv ?? "");
            try
            {
                File.WriteAllText(Path.Combine(tools, "ssf2mm_displayName.txt"), ResolveFlaDisplayName(flaPath, outputDirectory));
            }
            catch { /* optional sidecar */ }
            if (string.IsNullOrWhiteSpace(tsv))
                return 0;
            return tsv.Split('\n').Count(l => l.Contains('\t', StringComparison.Ordinal));
        }

        public static string ResolveFlaDisplayName(string? flaPath, string? outputDirectory)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    var dir = outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var pack = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(pack))
                    {
                        var json = Path.Combine(pack, "character.json");
                        if (File.Exists(json))
                        {
                            var m = Regex.Match(
                                File.ReadAllText(json),
                                "\"(?:DisplayName|displayName)\"\\s*:\\s*\"([^\"]+)\"",
                                RegexOptions.CultureInvariant);
                            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                                return m.Groups[1].Value.Trim();
                        }
                        var folder = Path.GetFileName(pack);
                        if (!string.IsNullOrWhiteSpace(folder) &&
                            !folder.Equals("FLA", StringComparison.OrdinalIgnoreCase))
                            return folder;
                    }
                }
            }
            catch { /* ignore */ }
            return string.IsNullOrWhiteSpace(flaPath) ? "" : Path.GetFileNameWithoutExtension(flaPath) ?? "";
        }

        /// <summary>
        /// Maps an instance name to a shared layer family (HitBox, AttackBox, Item, �).
        /// </summary>
        public static string? MapBoxLayerFamily(string? instanceName)
        {
            var n = Regex.Replace(instanceName ?? "", @"[\s*_]+", "", RegexOptions.CultureInvariant).ToLowerInvariant();
            n = Regex.Replace(n, @"\d+$", "");
            if (n.Length == 0)
                return null;
            if (n.Contains("itembox", StringComparison.Ordinal))
                return "Item";
            if (n.Contains("hitbox", StringComparison.Ordinal) || n == "hit")
                return "HitBox";
            if (n.Contains("attackbox", StringComparison.Ordinal) || n.Contains("atkbox", StringComparison.Ordinal))
                return "AttackBox";
            if (n.Contains("hurtbox", StringComparison.Ordinal))
                return "HurtBox";
            if (n.Contains("grabbox", StringComparison.Ordinal))
                return "GrabBox";
            if (n.EndsWith("box", StringComparison.Ordinal) && n.Length > 3)
                return char.ToUpperInvariant(n[0]) + n[1..^3] + "Box";
            return null;
        }

        public static bool IsHostSymbolXml(string xml, string ident, string? displayName)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return false;
            var id = (ident ?? "").Trim();
            if (id.Length > 0 && Regex.IsMatch(xml, @"\blinkageClassName=""" + Regex.Escape(id) + @"""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
            if (id.Length > 0 && Regex.IsMatch(xml, @"<DOMSymbolItem\b[^>]*\bname=""" + Regex.Escape(id) + @"""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
            if (!string.IsNullOrWhiteSpace(displayName) &&
                Regex.IsMatch(xml, @"<DOMSymbolItem\b[^>]*\bname=""" + Regex.Escape(displayName) + @"""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
            return Regex.IsMatch(xml, @"\bname=""stance""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// In-memory timeline policy: family names, Actions merge, lock all but displayName.
        /// Does not move zip entries.
        /// </summary>
        public static string ApplyFlaTimelineLayerPolicy(string xml, string? displayName, bool isHost)
        {
            if (string.IsNullOrWhiteSpace(xml) ||
                (xml.IndexOf("DOMLayer", StringComparison.Ordinal) < 0 &&
                 xml.IndexOf("DOMTimeline", StringComparison.Ordinal) < 0))
                return xml;
            XDocument doc;
            try { doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
            catch { return xml; }

            var art = string.IsNullOrWhiteSpace(displayName) ? "stance" : displayName.Trim();
            foreach (var layers in doc.Descendants().Where(e => e.Name.LocalName == "layers").ToList())
            {
                var layerList = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var layer in layerList)
                {
                    var name = (string?)layer.Attribute("name") ?? "";
                    if (IsScriptLayerName(name))
                        layer.SetAttributeValue("name", isHost ? "Scripts" : "Actions");
                    else if (name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
                        layer.SetAttributeValue("name", "Labels Layer");
                    else if (name.Equals("stance", StringComparison.OrdinalIgnoreCase))
                        layer.SetAttributeValue("name", art);
                    else
                    {
                        var inst = FirstInstanceNameInLayer(layer);
                        var fam = MapBoxLayerFamily(inst) ?? MapBoxLayerFamily(name);
                        if (fam != null)
                            layer.SetAttributeValue("name", fam);
                        else
                        {
                            var leaf = FirstPlacedClipLeafInLayer(layer);
                            if (!string.IsNullOrEmpty(leaf) && !IsGenericSymbolLeaf(leaf))
                                layer.SetAttributeValue("name", UniqueLayerName(usedNames, leaf));
                            else if (LayerHasBitmap(layer))
                                layer.SetAttributeValue("name", UniqueLayerName(usedNames, art));
                        }
                    }
                    var assigned = (string?)layer.Attribute("name") ?? "";
                    if (assigned.Length > 0)
                        usedNames.Add(assigned);
                }

                var isDocument = doc.Root?.Name.LocalName == "DOMDocument";
                if (!isDocument)
                    MergeSameNamedLayers(layers, isHost, art);
                TrimBoxLayersToArtLastFrame(layers, art);
                WrapGroundFolder(layers);
                if (isDocument)
                    MoveDisplayNameLayerToTop(layers, art);
                else
                    CollectUnnamedIntoEffects(layers);
                WrapEffectsFolder(layers);
                ApplyFlaOutlinePolicy(layers, art, isHost);
                ReindexParentLayerIndices(layers);

                layerList = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
                foreach (var layer in layerList)
                {
                    var name = (string?)layer.Attribute("name") ?? "";
                    var isArt = name.Equals(art, StringComparison.OrdinalIgnoreCase);
                    var isFolder = string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase);
                    if (!isArt && !isFolder)
                        layer.SetAttributeValue("locked", "true");
                    else if (isArt)
                        layer.SetAttributeValue("locked", "false");
                }
            }

            if (doc.Root?.Name.LocalName == "DOMDocument")
                EnsureSingleScene1(doc);

            return doc.Declaration == null
                ? doc.ToString(SaveOptions.DisableFormatting)
                : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
        }

        public sealed class FlaBoxLayerMergeResult
        {
            public bool Ok { get; init; }
            public string Message { get; init; } = "";
            public int XmlFilesChanged { get; init; }
            public int TimelinesMerged { get; init; }
            public int LayersRemoved { get; init; }
            public string? BackupPath { get; init; }
        }

        /// <summary>
        /// Overlay-merge box-family layers (HitBox, Item, AttackBox, �) in a FLA zip.
        /// Does not clone, rename the library, convert audio, or apply lock/folder policy.
        /// Writes <c>*.fla.bak</c> once if missing. Restores that backup on failure.
        /// </summary>
        public static FlaBoxLayerMergeResult MergeFlaBoxLayers(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return new FlaBoxLayerMergeResult { Ok = false, Message = "FLA not found: " + flaPath };

            string? bak = null;
            try
            {
                bak = flaPath + ".bak";
                if (!File.Exists(bak))
                    File.Copy(flaPath, bak, overwrite: false);

                var entries = ReadFlaZipEntries(flaPath);
                var files = 0;
                var timelines = 0;
                var removed = 0;
                foreach (var key in entries.Keys.ToList())
                {
                    if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var xml = Encoding.UTF8.GetString(entries[key]);
                    if (xml.IndexOf("DOMLayer", StringComparison.Ordinal) < 0)
                        continue;
                    var next = MergeBoxLayersInXml(xml, out var t, out var r);
                    if (t > 0 || r > 0 || !string.Equals(next, xml, StringComparison.Ordinal))
                    {
                        entries[key] = Encoding.UTF8.GetBytes(next);
                        files++;
                    }
                    timelines += t;
                    removed += r;
                }

                if (files == 0)
                    return new FlaBoxLayerMergeResult
                    {
                        Ok = true,
                        Message = "Nothing to merge (already one layer per box family).",
                        BackupPath = bak
                    };

                WriteFlaZipAtomic(flaPath, entries);
                using (var zip = ZipFile.OpenRead(flaPath))
                {
                    if (zip.Entries.Count == 0)
                        throw new InvalidOperationException("Wrote an empty zip.");
                }

                return new FlaBoxLayerMergeResult
                {
                    Ok = true,
                    Message = "Merged box layers in " + files + " XML file(s): "
                        + timelines + " timeline(s), removed " + removed + " layer(s).",
                    XmlFilesChanged = files,
                    TimelinesMerged = timelines,
                    LayersRemoved = removed,
                    BackupPath = bak
                };
            }
            catch (Exception ex)
            {
                try
                {
                    if (bak != null && File.Exists(bak))
                        File.Copy(bak, flaPath, overwrite: true);
                }
                catch { /* keep original error */ }
                return new FlaBoxLayerMergeResult { Ok = false, Message = ex.Message, BackupPath = bak };
            }
        }

        public static string MergeBoxLayersInXml(string xml, out int timelinesMerged, out int layersRemoved)
        {
            timelinesMerged = 0;
            layersRemoved = 0;
            if (string.IsNullOrWhiteSpace(xml))
                return xml;
            XDocument doc;
            try { doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
            catch { return xml; }

            var changed = false;
            foreach (var layers in doc.Descendants().Where(e => e.Name.LocalName == "layers").ToList())
            {
                var n = MergeBoxFamiliesOnLayers(layers);
                if (n <= 0)
                    continue;
                timelinesMerged++;
                layersRemoved += n;
                changed = true;
            }
            if (!changed)
                return xml;
            return doc.Declaration == null
                ? doc.ToString(SaveOptions.DisableFormatting)
                : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
        }

        private static int MergeBoxFamiliesOnLayers(XElement layers)
        {
            var grouped = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer")
                .Select((el, i) => (el, i, fam: BoxFamilyOfLayer(el)))
                .Where(x => x.fam != null)
                .GroupBy(x => x.fam!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            var removed = 0;
            foreach (var g in grouped)
            {
                var list = g.OrderBy(x => x.i).ToList();
                var dest = list[0].el;
                if (LayerHasTween(dest))
                    continue;
                dest.SetAttributeValue("name", g.Key);
                for (var i = list.Count - 1; i >= 1; i--)
                {
                    var src = list[i].el;
                    if (LayerHasTween(src))
                        continue;
                    var srcIndex = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(src);
                    if (LayerHasDrawable(src))
                        MergeDomLayerFrames(dest, src, artEnd: -1);
                    src.Remove();
                    if (srcIndex >= 0)
                        AdjustParentLayerIndexAfterRemoval(layers, srcIndex);
                    removed++;
                }
            }
            return removed;
        }

        private static string? BoxFamilyOfLayer(XElement layer)
        {
            if (LayerIsSpecialType(layer))
                return null;
            var name = (string?)layer.Attribute("name") ?? "";
            if (name.StartsWith("Effects ", StringComparison.Ordinal))
                return null;
            return MapBoxLayerFamily(name) ?? MapBoxLayerFamily(FirstInstanceNameInLayer(layer));
        }

        private static bool LayerIsSpecialType(XElement layer)
        {
            var t = (string?)layer.Attribute("layerType") ?? "";
            return t.Equals("folder", StringComparison.OrdinalIgnoreCase)
                || t.Equals("mask", StringComparison.OrdinalIgnoreCase)
                || t.Equals("guide", StringComparison.OrdinalIgnoreCase)
                || t.Equals("guided", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LayerHasTween(XElement layer)
        {
            foreach (var f in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame"))
            {
                var tw = (string?)f.Attribute("tweenType");
                if (!string.IsNullOrEmpty(tw) && !tw.Equals("None", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void AdjustParentLayerIndexAfterRemoval(XElement layers, int removedIndex)
        {
            foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
            {
                var parent = (string?)layer.Attribute("parentLayerIndex");
                if (string.IsNullOrEmpty(parent))
                    continue;
                if (!int.TryParse(parent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                    continue;
                if (p == removedIndex)
                    layer.Attribute("parentLayerIndex")?.Remove();
                else if (p > removedIndex)
                    layer.SetAttributeValue("parentLayerIndex", p - 1);
            }
        }

        private static bool IsScriptLayerName(string name) =>
            name.Equals("Script Layer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Script Layer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Scripts Layer", StringComparison.OrdinalIgnoreCase);

        private static string? FirstInstanceNameInLayer(XElement layer)
        {
            foreach (var inst in layer.Descendants().Where(e => e.Name.LocalName is "DOMSymbolInstance" or "DOMBitmapInstance"))
            {
                var n = (string?)inst.Attribute("name");
                if (!string.IsNullOrEmpty(n))
                    return n;
            }
            return null;
        }

        private static string? FirstPlacedClipLeafInLayer(XElement layer)
        {
            foreach (var inst in layer.Descendants().Where(e => e.Name.LocalName == "DOMSymbolInstance"))
            {
                var lib = (string?)inst.Attribute("libraryItemName");
                if (string.IsNullOrEmpty(lib))
                    continue;
                var leaf = BaseLibraryItemName(lib);
                if (!string.IsNullOrEmpty(leaf))
                    return leaf;
            }
            return null;
        }

        private static bool IsGenericSymbolLeaf(string leaf) =>
            Regex.IsMatch(leaf ?? "", @"^Symbol\s*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool LayerHasBitmap(XElement layer) =>
            layer.Descendants().Any(e => e.Name.LocalName == "DOMBitmapInstance");

        private static string UniqueLayerName(HashSet<string> used, string wanted)
        {
            var baseName = string.IsNullOrWhiteSpace(wanted) ? "Layer" : wanted.Trim();
            if (used.Add(baseName))
                return baseName;
            for (var n = 2; n < 1000; n++)
            {
                var candidate = baseName + " " + n;
                if (used.Add(candidate))
                    return candidate;
            }
            return baseName;
        }

        private static bool LayerHasDrawable(XElement layer) =>
            layer.Descendants().Any(e => e.Name.LocalName == "DOMFrame" && FrameHasDrawable(e));

        private static bool FrameHasDrawable(XElement frame)
        {
            foreach (var el in frame.Descendants())
            {
                var n = el.Name.LocalName;
                if (n is "DOMSymbolInstance" or "DOMBitmapInstance" or "DOMStaticText" or "DOMDynamicText")
                    return true;
                if (n == "Edge")
                    return true;
            }
            return false;
        }

        private static void MergeSameNamedLayers(XElement layers, bool isHost, string artName)
        {
            var artEnd = ResolveArtLastFrame(layers, artName);
            var grouped = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer")
                .Select((el, i) =>
                {
                    var name = (string?)el.Attribute("name") ?? "";
                    return (el, i, name, fam: MapBoxLayerFamily(name));
                })
                .Where(x => x.fam != null && !x.name.StartsWith("Effects ", StringComparison.Ordinal))
                .GroupBy(x => x.fam!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);
            foreach (var g in grouped)
            {
                if (isHost && (g.Key.Equals("Scripts", StringComparison.OrdinalIgnoreCase) ||
                               g.Key.Equals("Actions", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var list = g.OrderBy(x => x.i).ToList();
                var dest = list[0].el;
                dest.SetAttributeValue("name", g.Key);
                for (var i = 1; i < list.Count; i++)
                {
                    if (!LayerHasDrawable(list[i].el))
                    {
                        list[i].el.Remove();
                        continue;
                    }
                    MergeDomLayerFrames(dest, list[i].el, artEnd);
                    list[i].el.Remove();
                }
            }
        }

        private static int LayerLastFrameEnd(XElement layer)
        {
            var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
            if (frames == null)
                return 0;
            var max = 0;
            foreach (var f in frames.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
            {
                int.TryParse((string?)f.Attribute("index") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx);
                if (!int.TryParse((string?)f.Attribute("duration") ?? "1", NumberStyles.Integer, CultureInfo.InvariantCulture, out var dur) || dur < 1)
                    dur = 1;
                max = Math.Max(max, idx + dur);
            }
            return max;
        }

        private static bool IsBoxLayerName(string name) =>
            name.Equals("HitBox", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AttackBox", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("HurtBox", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GrabBox", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Item", StringComparison.OrdinalIgnoreCase);

        private static void TrimBoxLayersToArtLastFrame(XElement layers, string artName)
        {
            var layerEls = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var artEnd = ResolveArtLastFrame(layers, artName);
            if (artEnd <= 0)
                return;
            foreach (var layer in layerEls)
            {
                var name = (string?)layer.Attribute("name") ?? "";
                if (!IsBoxLayerName(name))
                    continue;
                var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
                if (frames == null)
                    continue;
                foreach (var f in frames.Elements().Where(e => e.Name.LocalName == "DOMFrame").ToList())
                {
                    int.TryParse((string?)f.Attribute("index") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx);
                    if (idx >= artEnd)
                    {
                        f.Remove();
                        continue;
                    }
                    if (!int.TryParse((string?)f.Attribute("duration") ?? "1", NumberStyles.Integer, CultureInfo.InvariantCulture, out var dur) || dur < 1)
                        dur = 1;
                    if (idx + dur > artEnd)
                        f.SetAttributeValue("duration", artEnd - idx);
                }
            }
        }

        private static bool LayerLooksLikeGround(XElement layer)
        {
            var name = (string?)layer.Attribute("name") ?? "";
            if (name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var inst = FirstInstanceNameInLayer(layer) ?? "";
            if (inst.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var leaf = FirstPlacedClipLeafInLayer(layer) ?? "";
            return leaf.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGenericLeftoverLayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            return Regex.IsMatch(name, @"^Layer(\s+\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                   || Regex.IsMatch(name, @"^Layer_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                   || Regex.IsMatch(name, @"^Symbol\s+\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void WrapGroundFolder(XElement layers)
        {
            var ns = layers.Name.Namespace;
            var layerEls = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var ground = layerEls.Where(l =>
                !string.Equals((string?)l.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase) &&
                LayerLooksLikeGround(l)).ToList();
            if (ground.Count == 0)
                return;
            XElement folder;
            var existing = layerEls.FirstOrDefault(l =>
                string.Equals((string?)l.Attribute("name"), "Ground", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)l.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                folder = new XElement(ns + "DOMLayer",
                    new XAttribute("name", "Ground"),
                    new XAttribute("layerType", "folder"),
                    new XAttribute("locked", "true"));
                ground[0].AddBeforeSelf(folder);
            }
            else
                folder = existing;
            var all = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var folderIndex = all.IndexOf(folder);
            for (var i = 0; i < ground.Count; i++)
            {
                ground[i].SetAttributeValue("name", "reference " + (i + 1));
                ground[i].SetAttributeValue("locked", "true");
                if (folderIndex >= 0)
                    ground[i].SetAttributeValue("parentLayerIndex", folderIndex);
            }
        }

        private static void CollectUnnamedIntoEffects(XElement layers)
        {
            var leftovers = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").Where(l =>
            {
                var n = (string?)l.Attribute("name") ?? "";
                var type = (string?)l.Attribute("layerType") ?? "";
                if (type.Equals("folder", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (IsBoxLayerName(n) || LayerLooksLikeGround(l))
                    return false;
                if (n.Equals("Actions", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Scripts", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Labels Layer", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Effects", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Ground", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("reference ", StringComparison.OrdinalIgnoreCase))
                    return false;
                return IsGenericLeftoverLayerName(n);
            }).ToList();
            for (var i = 0; i < leftovers.Count; i++)
                leftovers[i].SetAttributeValue("name", "Effect " + (i + 1));
        }

        private static void MoveDisplayNameLayerToTop(XElement layers, string artName)
        {
            var art = layers.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "DOMLayer" &&
                (string.Equals((string?)e.Attribute("name"), artName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals((string?)e.Attribute("name"), "stance", StringComparison.OrdinalIgnoreCase)));
            if (art == null)
                return;
            art.Remove();
            layers.AddFirst(art);
        }

        private static void WrapEffectsFolder(XElement layers)
        {
            var ns = layers.Name.Namespace;
            var layerEls = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var effects = layerEls.Where(l =>
            {
                var n = (string?)l.Attribute("name") ?? "";
                return n.StartsWith("Effect ", StringComparison.OrdinalIgnoreCase) ||
                       n.StartsWith("Effects ", StringComparison.OrdinalIgnoreCase) ||
                       n.Equals("Effects", StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (effects.Count == 0)
                return;
            if (layerEls.Any(l => string.Equals((string?)l.Attribute("name"), "Effects", StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals((string?)l.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase)))
                return;
            var folder = new XElement(ns + "DOMLayer",
                new XAttribute("name", "Effects"),
                new XAttribute("layerType", "folder"));
            effects[0].AddBeforeSelf(folder);
            var all = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var folderIndex = all.IndexOf(folder);
            if (folderIndex < 0)
                return;
            foreach (var layer in effects)
                layer.SetAttributeValue("parentLayerIndex", folderIndex);
        }

        private static void ApplyFlaOutlinePolicy(XElement layers, string artName, bool isHost)
        {
            var layerEls = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var artIndex = layerEls.FindIndex(l =>
                string.Equals((string?)l.Attribute("name"), artName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)l.Attribute("name"), "stance", StringComparison.OrdinalIgnoreCase));
            for (var i = 0; i < layerEls.Count; i++)
            {
                var layer = layerEls[i];
                var name = (string?)layer.Attribute("name") ?? "";
                if (string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                    continue;
                var box = IsBoxLayerName(name);
                var afterArt = isHost && artIndex >= 0 && i > artIndex &&
                               !name.Equals("Labels Layer", StringComparison.OrdinalIgnoreCase) &&
                               !name.Equals("Scripts", StringComparison.OrdinalIgnoreCase) &&
                               !name.Equals("Actions", StringComparison.OrdinalIgnoreCase);
                if (!box && !afterArt)
                    continue;
                layer.SetAttributeValue("outline", "true");
                if (box && layer.Attribute("color") == null)
                    layer.SetAttributeValue("color", "#FF4FFF");
            }
        }

        private static void ReindexParentLayerIndices(XElement layers)
        {
            var layerEls = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            for (var i = 0; i < layerEls.Count; i++)
            {
                var parent = (string?)layerEls[i].Attribute("parentLayerIndex");
                if (string.IsNullOrEmpty(parent))
                    continue;
                if (!int.TryParse(parent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ||
                    p < 0 || p >= layerEls.Count)
                    layerEls[i].Attribute("parentLayerIndex")?.Remove();
            }
        }

        private static void EnsureSingleScene1(XDocument doc)
        {
            var timelines = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "timelines");
            if (timelines == null)
                return;
            var scenes = timelines.Elements().Where(e => e.Name.LocalName == "DOMTimeline").ToList();
            if (scenes.Count == 0)
                return;
            XElement keep = scenes[0];
            var bestScore = -1;
            foreach (var scene in scenes)
            {
                var score = scene.Descendants().Count(e => e.Name.LocalName == "DOMSymbolInstance");
                if (scene.Descendants().Any(e =>
                        string.Equals((string?)e.Attribute("name"), "stance", StringComparison.OrdinalIgnoreCase)))
                    score += 1000;
                if (score > bestScore)
                {
                    keep = scene;
                    bestScore = score;
                }
            }
            foreach (var scene in scenes)
            {
                if (!ReferenceEquals(scene, keep))
                    scene.Remove();
            }
            keep.SetAttributeValue("name", "Scene 1");
            doc.Root?.SetAttributeValue("currentTimeline", "1");
        }

        private static int ResolveArtLastFrame(XElement layers, string artName)
        {
            var layerEls = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var art = layerEls.FirstOrDefault(l =>
                string.Equals((string?)l.Attribute("name"), artName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)l.Attribute("name"), "stance", StringComparison.OrdinalIgnoreCase));
            var end = art == null ? 0 : LayerLastFrameEnd(art);
            if (end > 0)
                return end;
            foreach (var layer in layerEls)
            {
                var name = (string?)layer.Attribute("name") ?? "";
                if (IsBoxLayerName(name) ||
                    name.StartsWith("Effects", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Labels Layer", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Scripts", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Actions", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                    continue;
                end = LayerLastFrameEnd(layer);
                if (end > 0)
                    return end;
            }
            return 0;
        }

        private static int DomFrameIndex(XElement frame)
        {
            int.TryParse((string?)frame.Attribute("index") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx);
            return idx;
        }

        private static int DomFrameDuration(XElement frame)
        {
            if (!int.TryParse((string?)frame.Attribute("duration") ?? "1", NumberStyles.Integer, CultureInfo.InvariantCulture, out var dur) || dur < 1)
                return 1;
            return dur;
        }

        private static XElement? CoveringDomFrame(XElement frames, int index)
        {
            foreach (var f in frames.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
            {
                var idx = DomFrameIndex(f);
                if (index >= idx && index < idx + DomFrameDuration(f))
                    return f;
            }
            return null;
        }

        /// <summary>
        /// Splits a dest span so a key exists at <paramref name="index"/> (Animate convert-to-keyframes).
        /// </summary>
        private static XElement EnsureDestKeyframeAt(XElement frames, XNamespace ns, int index)
        {
            var exact = frames.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "DOMFrame" && DomFrameIndex(e) == index);
            if (exact != null)
                return exact;
            var cover = CoveringDomFrame(frames, index);
            if (cover == null)
            {
                var created = new XElement(ns + "DOMFrame",
                    new XAttribute("index", index),
                    new XElement(ns + "elements"));
                frames.Add(created);
                return created;
            }
            var start = DomFrameIndex(cover);
            var dur = DomFrameDuration(cover);
            var headLen = index - start;
            var tailLen = start + dur - index;
            cover.SetAttributeValue("duration", headLen);
            if (headLen <= 1)
                cover.Attribute("duration")?.Remove();
            var tail = new XElement(cover);
            tail.SetAttributeValue("index", index);
            if (tailLen > 1)
                tail.SetAttributeValue("duration", tailLen);
            else
                tail.Attribute("duration")?.Remove();
            cover.AddAfterSelf(tail);
            return tail;
        }

        /// <summary>
        /// Overlay src onto dest at the same frame index. Dest is the front layer:
        /// src elements go first (behind), dest elements stay last (front).
        /// </summary>
        private static void MergeDomLayerFrames(XElement dest, XElement src, int artEnd)
        {
            var destFrames = dest.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
            var srcFrames = src.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
            if (srcFrames == null)
                return;
            if (destFrames == null)
            {
                dest.Add(new XElement(srcFrames));
                return;
            }
            int cap;
            if (artEnd < 0)
                cap = 0;
            else
            {
                var destEnd = LayerLastFrameEnd(dest);
                cap = artEnd > 0 ? artEnd : destEnd;
            }
            var ns = dest.Name.Namespace;
            foreach (var sf in srcFrames.Elements().Where(e => e.Name.LocalName == "DOMFrame").ToList())
            {
                if (!FrameHasDrawable(sf))
                    continue;
                var idxNum = DomFrameIndex(sf);
                if (cap > 0 && idxNum >= cap)
                    continue;
                var df = EnsureDestKeyframeAt(destFrames, ns, idxNum);
                if (cap > 0)
                {
                    var dur = DomFrameDuration(df);
                    if (idxNum + dur > cap)
                    {
                        if (cap - idxNum <= 1)
                            df.Attribute("duration")?.Remove();
                        else
                            df.SetAttributeValue("duration", cap - idxNum);
                    }
                }
                var destEls = df.Elements().FirstOrDefault(e => e.Name.LocalName == "elements");
                var srcEls = sf.Elements().FirstOrDefault(e => e.Name.LocalName == "elements");
                if (srcEls == null)
                    continue;
                if (destEls == null)
                    df.AddFirst(new XElement(srcEls));
                else
                    foreach (var child in srcEls.Elements().Reverse().ToList())
                        destEls.AddFirst(new XElement(child));
            }
        }

        public static string BuildFlaOrganizeBlueprint(string flaPath, bool stageLibrary = false)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return "";
            return BuildFlaOrganizeBlueprintFromEntries(
                ReadFlaZipEntries(flaPath),
                CharacterIdentFromFileName(flaPath),
                stageLibrary);
        }

        public static string BuildFlaOrganizeBlueprintFromEntries(
            Dictionary<string, byte[]> entries, string ident, bool stageLibrary = false)
        {
            if (entries == null || entries.Count == 0)
                return "";
            ident ??= "";
            var items = new Dictionary<string, (string Xml, LibraryItemKind Kind)>(StringComparer.Ordinal);
            foreach (var kv in entries)
            {
                if (!kv.Key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                var root = LibraryItemRootRegex.Match(xml);
                if (!root.Success)
                    continue;
                var attrs = root.Groups["a"].Value;
                var nameMatch = Regex.Match(attrs, @"\bname=""([^""]+)""", RegexOptions.CultureInvariant);
                var itemName = nameMatch.Success
                    ? nameMatch.Groups[1].Value
                    : Path.GetFileNameWithoutExtension(kv.Key);
                TryAddOrganizeItem(items, itemName, xml, root.Groups["tag"].Value, attrs);
            }

            if (entries.TryGetValue("DOMDocument.xml", out var domBytes) ||
                entries.Keys.FirstOrDefault(k => k.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase)) is { } domKey &&
                entries.TryGetValue(domKey, out domBytes))
            {
                var domXml = Encoding.UTF8.GetString(domBytes);
                foreach (Match media in LibraryItemRootRegex.Matches(domXml))
                {
                    var tag = media.Groups["tag"].Value;
                    if (!tag.Equals("DOMBitmapItem", StringComparison.OrdinalIgnoreCase) &&
                        !tag.Equals("DOMSoundItem", StringComparison.OrdinalIgnoreCase) &&
                        !tag.Equals("DOMFontItem", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var attrs = media.Groups["a"].Value;
                    var nameMatch = Regex.Match(attrs, @"\bname=""([^""]+)""", RegexOptions.CultureInvariant);
                    if (!nameMatch.Success)
                        continue;
                    TryAddOrganizeItem(items, nameMatch.Groups[1].Value, media.Value, tag, attrs);
                }
            }

            var host = FindStanceHostLibraryXml(entries);
            var hostName = "";
            if (host != null)
            {
                var hostRoot = LibraryItemRootRegex.Match(host.Value.Xml);
                var hostAttrs = hostRoot.Success ? hostRoot.Groups["a"].Value : "";
                var hn = Regex.Match(hostAttrs, @"\bname=""([^""]+)""", RegexOptions.CultureInvariant);
                hostName = hn.Success ? hn.Groups[1].Value : Path.GetFileNameWithoutExtension(host.Value.EntryName);
            }

            var usage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var firstPose = new Dictionary<string, (string Label, int Start)>(StringComparer.Ordinal);
            var poseRoots = new Dictionary<string, List<(string Label, int Start)>>(StringComparer.Ordinal);
            var poseLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (host != null)
            {
                foreach (var span in CollectHostLabelSpans(host.Value.Xml))
                {
                    poseLabelNames.Add(span.Label);
                    foreach (var child in CollectLibraryItemNamesInXml(host.Value.Xml, span.Start, span.End))
                    {
                        var resolved = ResolveOrganizeItemName(child, items) ?? child;
                        WalkOrganizeUsage(
                            resolved, span.Label, span.Start, items, usage, firstPose,
                            new HashSet<string>(StringComparer.Ordinal));
                    }

                    foreach (var stance in CollectStanceLibraryItemNamesInXml(host.Value.Xml, span.Start, span.End))
                    {
                        var resolved = ResolveOrganizeItemName(stance, items) ?? stance;
                        if (!poseRoots.TryGetValue(resolved, out var poseLabels))
                        {
                            poseLabels = new List<(string Label, int Start)>();
                            poseRoots[resolved] = poseLabels;
                        }

                        if (!poseLabels.Any(p => p.Label.Equals(span.Label, StringComparison.Ordinal)))
                            poseLabels.Add((span.Label, span.Start));
                    }
                }
            }

            var allParents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var kv in items)
            {
                if (kv.Value.Kind is LibraryItemKind.Bitmap or LibraryItemKind.Sound or LibraryItemKind.Font)
                    continue;
                var children = new HashSet<string>(StringComparer.Ordinal);
                CollectChildLibraryNames(kv.Value.Xml, children);
                foreach (var child in children)
                {
                    var resolved = ResolveOrganizeItemName(child, items);
                    if (resolved == null || resolved.Equals(kv.Key, StringComparison.Ordinal))
                        continue;
                    if (!allParents.TryGetValue(resolved, out var parents))
                    {
                        parents = new HashSet<string>(StringComparer.Ordinal);
                        allParents[resolved] = parents;
                    }

                    parents.Add(kv.Key);
                }
            }

            var destFolder = new Dictionary<string, string>(StringComparer.Ordinal);
            var renameTo = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedNames = new HashSet<string>(
                items.Keys.Select(BaseLibraryItemName),
                StringComparer.OrdinalIgnoreCase);

            void Assign(string key, string folder, string? preferredName)
            {
                destFolder[key] = folder;
                var leaf = BaseLibraryItemName(key);
                if (string.IsNullOrWhiteSpace(preferredName))
                    return;
                var neu = AllocateUniqueLibraryName(preferredName, leaf, usedNames);
                if (!neu.Equals(leaf, StringComparison.Ordinal))
                    renameTo[key] = neu;
            }

            string? LeftmostLabel(string key)
            {
                if (poseRoots.TryGetValue(key, out var poseLabels) && poseLabels.Count > 0)
                    return poseLabels.OrderBy(p => p.Start).First().Label;
                if (firstPose.TryGetValue(key, out var fp))
                    return fp.Label;
                return null;
            }

            string? ParentDest(string key)
            {
                if (!allParents.TryGetValue(key, out var parents) || parents.Count == 0)
                    return null;
                string? best = null;
                var bestStart = int.MaxValue;
                foreach (var parent in parents)
                {
                    if (!destFolder.TryGetValue(parent, out var folder))
                        continue;
                    var start = firstPose.TryGetValue(parent, out var fp) ? fp.Start : int.MaxValue;
                    if (best == null || start < bestStart)
                    {
                        best = folder;
                        bestStart = start;
                    }
                }

                return best;
            }

            foreach (var kv in items)
            {
                if (kv.Value.Kind == LibraryItemKind.Sound)
                {
                    Assign(kv.Key, "Audio", PreferredSmartName(kv.Value.Xml, ident, poseLabel: null));
                    continue;
                }

                if (kv.Value.Kind == LibraryItemKind.Font)
                {
                    Assign(kv.Key, "Fonts", FontLibraryName(kv.Value.Xml));
                    continue;
                }

                if (!stageLibrary && kv.Key.Equals(hostName, StringComparison.Ordinal))
                {
                    Assign(kv.Key, "", FriendlyCharacterLibraryName(ident));
                    continue;
                }

                if (kv.Value.Kind == LibraryItemKind.Bitmap)
                    continue;

                if (poseRoots.TryGetValue(kv.Key, out var poseLabels) && poseLabels.Count > 0)
                {
                    var home = poseLabels.OrderBy(p => p.Start).First().Label;
                    Assign(kv.Key, "Animations/" + SanitizeOrganizeFolder(home), SanitizeOrganizeFolder(home));
                    continue;
                }

                var typeFolder = kv.Value.Kind == LibraryItemKind.Graphic ? "Graphics" : "Movieclips";
                var smart = PreferredSmartName(kv.Value.Xml, ident, poseLabel: null);
                var homeLabel = LeftmostLabel(kv.Key);
                if (!string.IsNullOrEmpty(homeLabel))
                    Assign(kv.Key, "Animations/" + SanitizeOrganizeFolder(homeLabel) + "/" + typeFolder, smart);
            }

            foreach (var kv in items)
            {
                if (kv.Value.Kind != LibraryItemKind.Bitmap || destFolder.ContainsKey(kv.Key))
                    continue;
                var parentFolder = ParentDest(kv.Key);
                if (string.IsNullOrEmpty(parentFolder))
                    continue;
                var smart = PreferredSmartName(kv.Value.Xml, ident, poseLabel: null);
                Assign(kv.Key, NestedTypeFolder(parentFolder, LibraryItemKind.Bitmap), smart);
            }

            for (var pass = 0; pass < 32; pass++)
            {
                var progress = false;
                foreach (var kv in items)
                {
                    if (destFolder.ContainsKey(kv.Key))
                        continue;
                    var smart = kv.Value.Kind == LibraryItemKind.Font
                        ? FontLibraryName(kv.Value.Xml)
                        : PreferredSmartName(kv.Value.Xml, ident, poseLabel: null);
                    var parentFolder = ParentDest(kv.Key);
                    if (!string.IsNullOrEmpty(parentFolder))
                    {
                        var dest = kv.Value.Kind == LibraryItemKind.Bitmap
                            ? NestedTypeFolder(parentFolder, LibraryItemKind.Bitmap)
                            : NestedTypeFolder(parentFolder, kv.Value.Kind);
                        Assign(kv.Key, dest, smart);
                        progress = true;
                        continue;
                    }

                    var waitingOnParent = allParents.TryGetValue(kv.Key, out var parents) &&
                                          parents.Any(p => items.ContainsKey(p) && !destFolder.ContainsKey(p));
                    if (waitingOnParent)
                        continue;

                    var looseRoot = stageLibrary ? "Animations" : "Shared Animations";
                    if (kv.Value.Kind == LibraryItemKind.Bitmap)
                    {
                        Assign(kv.Key, looseRoot + "/Sprites", smart);
                    }
                    else
                    {
                        var sharedLeaf = SanitizeOrganizeFolder(
                            !string.IsNullOrWhiteSpace(smart) ? smart : BaseLibraryItemName(kv.Key));
                        Assign(kv.Key, looseRoot + "/" + sharedLeaf, smart);
                    }

                    progress = true;
                }

                if (!progress)
                    break;
            }

            var lines = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var kv in destFolder)
            {
                renameTo.TryGetValue(kv.Key, out var neu);
                if (OrganizeRowIsNoOp(kv.Value, kv.Key, neu))
                    continue;
                lines.Add(FormatOrganizeLine(kv.Value, kv.Key, neu));
            }

            if (stageLibrary)
                DebugLogger.Log("[JPEXS] stage library host=" +
                    (string.IsNullOrEmpty(hostName) ? "none" : hostName) +
                    " folder=Animations");

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Human-readable library name from a linkage id: strip <paramref name="ident"/>,
        /// then a lowercase 2�3 letter prefix (<c>bm_</c>), then split underscores / camelCase.
        /// </summary>
        public static string SmartLibraryName(string linkage, string? ident = null)
        {
            var s = (linkage ?? "").Trim();
            if (s.Length == 0)
                return "";
            if (!string.IsNullOrWhiteSpace(ident))
            {
                var prefix = ident.Trim() + "_";
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    s = s[prefix.Length..];
            }

            s = Regex.Replace(s, @"^[a-z]{2,3}_", "");
            s = s.Replace('_', ' ');
            s = Regex.Replace(s, "(?<=[a-z])([A-Z])", " $1");
            s = Regex.Replace(s, @"^([A-Z]{2,3})([a-z])", "$1 $2");
            return SanitizeLibraryItemName(s);
        }

        /// <summary>Library name for the fighter host: ident <c>lol</c> ? <c>Lol</c>, <c>best mage</c> ? <c>Best Mage</c>.</summary>
        public static string CharacterIdentFromFileName(string path)
        {
            var name = Path.GetFileName(path ?? "") ?? "";
            while (true)
            {
                if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                { name = name[..^4]; continue; }
                if (name.EndsWith(".fla", StringComparison.OrdinalIgnoreCase))
                { name = name[..^4]; continue; }
                if (name.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                { name = name[..^4]; continue; }
                break;
            }
            return name.Trim();
        }

        public static string FriendlyCharacterLibraryName(string ident)
        {
            var s = CharacterIdentFromFileName(ident ?? "");
            if (s.Length == 0)
                return "Character";
            var parts = s.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                parts[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : "");
            }

            return string.Join(" ", parts);
        }

        /// <summary>AS3 ident for the body: <c>best mage</c> ? <c>best_mage</c>.</summary>
        public static string CharacterLinkageIdent(string ident) =>
            Regex.Replace((ident ?? "").Trim(), @"[\s\-]+", "_");

        /// <summary><c>Clone_Day</c> ? <c>CloneDay</c> (JPEXS timeline Ext stem).</summary>
        public static string PascalCharacterIdent(string ident)
        {
            var id = CharacterLinkageIdent(ident);
            if (string.IsNullOrEmpty(id))
                return id;
            var parts = Regex.Split(id, @"[^A-Za-z0-9]+")
                .Where(p => p.Length > 0)
                .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : ""))
                .ToArray();
            if (parts.Length == 0)
                return char.ToUpperInvariant(id[0]) + (id.Length > 1 ? id[1..] : "");
            return string.Concat(parts);
        }

        private static string NestedTypeFolder(string parentFolder, LibraryItemKind kind)
        {
            var type = kind == LibraryItemKind.Graphic ? "Graphics"
                : kind == LibraryItemKind.Bitmap ? "Sprites"
                : "Movieclips";
            var pose = PoseFromOrganizeFolder(parentFolder);
            if (!string.IsNullOrEmpty(pose))
                return "Animations/" + pose + "/" + type;
            if (parentFolder.StartsWith("Shared Animations/", StringComparison.Ordinal))
                return SharedRootFolder(parentFolder) + "/" + type;
            return parentFolder;
        }

        private static string SharedRootFolder(string folder)
        {
            const string prefix = "Shared Animations/";
            if (!folder.StartsWith(prefix, StringComparison.Ordinal))
                return folder;
            var rest = folder[prefix.Length..];
            var slash = rest.IndexOf('/');
            var name = slash < 0 ? rest : rest[..slash];
            return prefix + name;
        }

        private static string? PoseFromOrganizeFolder(string? folder)
        {
            if (string.IsNullOrEmpty(folder) ||
                !folder.StartsWith("Animations/", StringComparison.Ordinal))
                return null;
            var rest = folder["Animations/".Length..];
            var slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest[..slash];
        }

        private static readonly (string Prefix, string Hint)[] PoseNameHints =
        {
            ("uspecair", "b_up_air"),
            ("uspec", "b_up"),
            ("utilt", "a_up_tilt"),
            ("sidetilt", "a_forward_tilt"),
            ("ftilt", "a_forward_tilt"),
            ("idlewait", "stand"),
            ("dtilt", "crouch_attack"),
            ("upthrow", "throw_up"),
            ("uthrow", "throw_up"),
            ("dthrow", "throw_down"),
            ("fthrow", "throw_forward"),
            ("bthrow", "throw_back"),
            ("usmash", "upsmash"),
            ("dsmash", "downsmash"),
            ("fsmash", "forwardsmash"),
            ("nair", "a_air"),
            ("fair", "a_air_forward"),
            ("bair", "a_air_backward"),
            ("dair", "a_air_down"),
            ("uair", "a_air_up"),
            ("getup", "getup"),
            ("dizzy", "dizzy")
        };

        private static string? GuessPoseLabel(string name, IReadOnlyCollection<string> labels)
        {
            if (string.IsNullOrWhiteSpace(name) || labels == null || labels.Count == 0)
                return null;
            var compact = Regex.Replace(name, @"[\s_]+", "", RegexOptions.CultureInvariant).ToLowerInvariant();
            compact = Regex.Replace(compact, @"\d+$", "");
            if (compact.Length < 3)
                return null;

            foreach (var (prefix, hint) in PoseNameHints)
            {
                if (!compact.Contains(prefix, StringComparison.Ordinal))
                    continue;
                var hintCompact = hint.Replace("_", "", StringComparison.Ordinal);
                var hit = labels.FirstOrDefault(l =>
                {
                    var lc = Regex.Replace(l, @"[\s_]+", "").ToLowerInvariant();
                    return lc.Equals(hintCompact, StringComparison.OrdinalIgnoreCase) ||
                           lc.Contains(hintCompact, StringComparison.OrdinalIgnoreCase);
                });
                if (!string.IsNullOrEmpty(hit))
                    return hit;
            }

            foreach (var lab in labels.OrderByDescending(l => l.Length))
            {
                var lc = Regex.Replace(lab, @"[\s_]+", "").ToLowerInvariant();
                if (lc.Length >= 3 && (compact.Contains(lc, StringComparison.Ordinal) ||
                                       lc.Contains(compact, StringComparison.Ordinal)))
                    return lab;
            }

            return null;
        }

        /// <summary>Display name from XFL <c>font</c>/<c>family</c> (Animate Family field).</summary>
        public static string FontLibraryName(string xml)
        {
            var m = Regex.Match(xml ?? "", @"\b(?:family|font)=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success)
                return "Font";
            var s = m.Groups[1].Value.Trim().Replace('_', ' ');
            s = Regex.Replace(s, "(?<=[a-z])([A-Z])", " $1");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(s) ? "Font" : SanitizeLibraryItemName(s);
        }

        private static void TryAddOrganizeItem(
            Dictionary<string, (string Xml, LibraryItemKind Kind)> items,
            string itemName,
            string xml,
            string tag,
            string attrs)
        {
            if (string.IsNullOrWhiteSpace(itemName) || items.ContainsKey(itemName))
                return;
            items[itemName] = (xml, ClassifyLibraryItem(tag, attrs, itemName));
        }

        private static HashSet<string> CollectStanceLibraryItemNamesInXml(string xml, int start, int end)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in DomFrameRegex.Matches(xml))
            {
                var attrs = m.Groups["a"].Value;
                var index = 0;
                var dur = 1;
                var idxMatch = Regex.Match(attrs, @"\bindex=""(\d+)""", RegexOptions.CultureInvariant);
                if (idxMatch.Success)
                    int.TryParse(idxMatch.Groups[1].Value, out index);
                var durMatch = Regex.Match(attrs, @"\bduration=""(\d+)""", RegexOptions.CultureInvariant);
                if (durMatch.Success)
                    int.TryParse(durMatch.Groups[1].Value, out dur);
                if (dur < 1) dur = 1;
                if (index + dur <= start || index >= end)
                    continue;
                var body = m.Groups["body"].Success ? m.Groups["body"].Value : attrs;
                foreach (var stance in CollectStanceChildLibraryItemNames(body))
                    names.Add(stance);
            }

            return names;
        }

        private static string? PreferredSmartName(string xml, string ident, string? poseLabel)
        {
            if (!string.IsNullOrWhiteSpace(poseLabel))
                return SanitizeOrganizeFolder(poseLabel);
            var linkage = ReadLinkageClassName(xml);
            if (string.IsNullOrWhiteSpace(linkage))
                return null;
            var smart = SmartLibraryName(linkage, ident);
            return string.IsNullOrWhiteSpace(smart) ? null : smart;
        }

        private static string? ReadLinkageClassName(string xml)
        {
            var m = LinkageClassNameRegex.Match(xml ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string SanitizeLibraryItemName(string name)
        {
            var s = Regex.Replace(name ?? "", @"[\\/:*?""<>|]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static string AllocateUniqueLibraryName(
            string preferred,
            string currentLeaf,
            HashSet<string> used)
        {
            preferred = SanitizeLibraryItemName(preferred);
            if (string.IsNullOrWhiteSpace(preferred))
                return currentLeaf;
            if (preferred.Equals(currentLeaf, StringComparison.OrdinalIgnoreCase))
                return currentLeaf;
            if (used.Add(preferred))
                return preferred;
            for (var n = 2; n < 1000; n++)
            {
                var candidate = preferred + " " + n;
                if (used.Add(candidate))
                    return candidate;
            }

            return currentLeaf;
        }

        private static string FormatOrganizeLine(string folder, string item, string? newName)
        {
            var leaf = BaseLibraryItemName(item);
            if (string.IsNullOrWhiteSpace(newName) || newName.Equals(leaf, StringComparison.Ordinal))
                return folder + "\t" + item;
            return folder + "\t" + item + "\t" + newName;
        }

        /// <summary>
        /// Sets library <c>name="Folder/leaf"</c> from the organize TSV. Does not move zip paths.
        /// Last segment is always a leaf (no slashes). Matches items by basename, ignoring extension.
        /// </summary>
        public static int ApplyFlaOrganizeNamePrefixes(Dictionary<string, byte[]> entries, string? tsv)
        {
            if (entries == null || string.IsNullOrWhiteSpace(tsv))
                return 0;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in tsv.Split('\n'))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length < 2)
                    continue;
                var folder = parts[0].Trim().Replace('\\', '/').Trim('/');
                var item = parts[1].Trim().Replace('\\', '/');
                if (string.IsNullOrEmpty(item))
                    continue;
                var leaf = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
                    ? BaseLibraryItemName(parts[2].Trim())
                    : BaseLibraryItemName(item);
                leaf = BaseLibraryItemName(leaf);
                if (!string.IsNullOrEmpty(folder) &&
                    leaf.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    leaf = BaseLibraryItemName(leaf[(folder.Length + 1)..]);
                var dest = string.IsNullOrEmpty(folder) ? leaf : folder + "/" + leaf;
                AddOrganizeDestKeys(map, item, dest);
                AddOrganizeDestKeys(map, leaf, dest);
            }
            if (map.Count == 0)
                return 0;
            var n = 0;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = RewriteLibraryItemNames(xml, map);
                next = RewriteLibraryItemNameAttributes(next, map);
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                {
                    entries[key] = Encoding.UTF8.GetBytes(next);
                    n++;
                }
            }
            return n;
        }

        private static void AddOrganizeDestKeys(Dictionary<string, string> map, string key, string dest)
        {
            if (string.IsNullOrEmpty(key))
                return;
            map[key] = dest;
            var leaf = BaseLibraryItemName(key);
            map[leaf] = dest;
            var stem = Path.GetFileNameWithoutExtension(leaf);
            if (!string.IsNullOrEmpty(stem))
            {
                map[stem] = dest;
                foreach (var ext in new[] { ".wav", ".flv", ".mp3", ".mpa" })
                    map[stem + ext] = dest;
            }
        }

        private static string ResolveOrganizeDest(Dictionary<string, string> map, string current)
        {
            if (map.TryGetValue(current, out var dest))
                return dest;
            var leaf = BaseLibraryItemName(current);
            if (map.TryGetValue(leaf, out dest))
                return dest;
            var stem = Path.GetFileNameWithoutExtension(leaf);
            if (!string.IsNullOrEmpty(stem) && map.TryGetValue(stem, out dest))
            {
                var ext = Path.GetExtension(leaf);
                if (!string.IsNullOrEmpty(ext) && dest.IndexOf('.', dest.LastIndexOf('/') + 1) < 0)
                    return dest + ext;
                return dest;
            }
            return current;
        }

        private static readonly Regex LibraryItemOpenNameRegex = new(
            @"<(?<tag>DOMSymbolItem|DOMBitmapItem|DOMSoundItem|DOMFontItem|DOMVideoItem)\b(?<rest>[^>]*?)\bname=""(?<n>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string RewriteLibraryItemNames(string xml, Dictionary<string, string> map)
        {
            return LibraryItemOpenNameRegex.Replace(xml, m =>
            {
                var dest = ResolveOrganizeDest(map, m.Groups["n"].Value);
                if (dest.Equals(m.Groups["n"].Value, StringComparison.Ordinal))
                    return m.Value;
                return "<" + m.Groups["tag"].Value + m.Groups["rest"].Value + "name=\"" + dest + "\"";
            });
        }

        private static string RewriteLibraryItemNameAttributes(string xml, Dictionary<string, string> map)
        {
            var next = Regex.Replace(
                xml,
                @"\blibraryItemName=""(?<n>[^""]+)""",
                m =>
                {
                    var dest = ResolveOrganizeDest(map, m.Groups["n"].Value);
                    return "libraryItemName=\"" + dest + "\"";
                },
                RegexOptions.CultureInvariant);
            return Regex.Replace(
                next,
                @"\bsoundName=""(?<n>[^""]+)""",
                m =>
                {
                    var dest = ResolveOrganizeDest(map, m.Groups["n"].Value);
                    return "soundName=\"" + dest + "\"";
                },
                RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Drops unreferenced LIBRARY symbols whose timelines have no drawable elements.
        /// </summary>
        public static int PruneEmptyLibrarySymbols(Dictionary<string, byte[]> entries)
        {
            if (entries == null || entries.Count == 0)
                return 0;
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entries)
            {
                if (!kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                foreach (Match m in Regex.Matches(xml, @"\blibraryItemName=""([^""]+)""", RegexOptions.CultureInvariant))
                    referenced.Add(BaseLibraryItemName(m.Groups[1].Value));
            }

            var removed = 0;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                if (xml.IndexOf("linkageExportForAS=\"true\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                var nameMatch = Regex.Match(xml, @"<DOMSymbolItem\b[^>]*\bname=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!nameMatch.Success)
                    continue;
                var leaf = BaseLibraryItemName(nameMatch.Groups[1].Value);
                if (referenced.Contains(leaf) || referenced.Contains(nameMatch.Groups[1].Value))
                    continue;
                if (LibrarySymbolHasDrawableContent(xml))
                    continue;
                entries.Remove(key);
                removed++;
            }
            return removed;
        }

        private static bool LibrarySymbolHasDrawableContent(string xml)
        {
            if (xml.IndexOf("<DOMSymbolInstance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("<DOMBitmapInstance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("<DOMStaticText", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("<DOMDynamicText", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("<DOMInputText", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("<SoundEnvelope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("soundName=\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (xml.IndexOf("<DOMShape", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return Regex.IsMatch(xml, @"<Edge[\s/>]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void StripInstancesToEmptySymbols(Dictionary<string, byte[]> entries)
        {
            var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entries)
            {
                if (!kv.Key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                if (xml.IndexOf("<DOMSymbolItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (LibrarySymbolHasDrawableContent(xml))
                    continue;
                var name = ReadLibraryItemName(xml);
                if (string.IsNullOrEmpty(name))
                    continue;
                empty.Add(name);
                empty.Add(BaseLibraryItemName(name));
            }
            if (empty.Count == 0)
                return;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                XDocument doc;
                try { doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
                catch { continue; }
                var removed = false;
                foreach (var inst in doc.Descendants().Where(e => e.Name.LocalName == "DOMSymbolInstance").ToList())
                {
                    var lib = (string?)inst.Attribute("libraryItemName") ?? "";
                    if (empty.Contains(lib) || empty.Contains(BaseLibraryItemName(lib)))
                    {
                        inst.Remove();
                        removed = true;
                    }
                }
                if (removed)
                    entries[key] = Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
            }
        }

        /// <summary>
        /// True when the item is already at <c>folder/destLeaf</c> (JSFL would no-op).
        /// </summary>
        public static bool OrganizeRowIsNoOp(string folder, string item, string? newName)
        {
            var leaf = BaseLibraryItemName(item);
            var destLeaf = string.IsNullOrWhiteSpace(newName) ? leaf : newName.Trim();
            var dest = string.IsNullOrEmpty(folder) ? destLeaf : folder + "/" + destLeaf;
            return item.Equals(dest, StringComparison.Ordinal);
        }

        private enum LibraryItemKind
        {
            MovieClip,
            Graphic,
            Bitmap,
            Sound,
            Font
        }

        private static readonly Regex LibraryItemRootRegex = new(
            @"<(?<tag>DOMSymbolItem|DOMBitmapItem|DOMSoundItem|DOMFontItem|DOMVideoItem)\b(?<a>[^>]*)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DomFrameRegex = new(
            @"<DOMFrame\b(?<a>[^>/]*)(?:/>|>(?<body>.*?)</DOMFrame>)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static LibraryItemKind ClassifyLibraryItem(string tag, string attrs, string itemName)
        {
            if (tag.Equals("DOMSoundItem", StringComparison.OrdinalIgnoreCase))
                return LibraryItemKind.Sound;
            if (tag.Equals("DOMFontItem", StringComparison.OrdinalIgnoreCase))
                return LibraryItemKind.Font;
            if (tag.Equals("DOMBitmapItem", StringComparison.OrdinalIgnoreCase))
                return LibraryItemKind.Bitmap;
            if (IsSmashChargeClip(itemName))
                return LibraryItemKind.MovieClip;
            if (Regex.IsMatch(attrs, @"\bsymbolType=""graphic""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return LibraryItemKind.Graphic;
            return LibraryItemKind.MovieClip;
        }

        private static string SanitizeOrganizeFolder(string name)
        {
            var s = Regex.Replace(name ?? "", @"[\\/:*?""<>|]", "_");
            s = s.Trim();
            if (s.Length == 0) s = "Unnamed";
            return char.ToUpperInvariant(s[0]) + s[1..];
        }

        private static string BaseLibraryItemName(string name)
        {
            var n = name ?? "";
            var slash = n.LastIndexOf('/');
            return slash >= 0 ? n[(slash + 1)..] : n;
        }

        private static List<(string Label, int Start, int End)> CollectHostLabelSpans(string hostXml)
        {
            var spans = new List<(string Label, int Start, int End)>();
            foreach (Match m in DomFrameRegex.Matches(hostXml))
            {
                var attrs = m.Groups["a"].Value;
                var labelMatch = Regex.Match(attrs, @"\bname=""([^""]+)""", RegexOptions.CultureInvariant);
                if (!labelMatch.Success)
                    continue;
                var label = labelMatch.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(label))
                    continue;
                var index = 0;
                var dur = 1;
                var idxMatch = Regex.Match(attrs, @"\bindex=""(\d+)""", RegexOptions.CultureInvariant);
                if (idxMatch.Success)
                    int.TryParse(idxMatch.Groups[1].Value, out index);
                var durMatch = Regex.Match(attrs, @"\bduration=""(\d+)""", RegexOptions.CultureInvariant);
                if (durMatch.Success)
                    int.TryParse(durMatch.Groups[1].Value, out dur);
                if (dur < 1) dur = 1;
                spans.Add((label, index, index + dur));
            }
            return spans;
        }

        private static HashSet<string> CollectLibraryItemNamesInXml(string xml, int start, int end)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in DomFrameRegex.Matches(xml))
            {
                var attrs = m.Groups["a"].Value;
                var index = 0;
                var dur = 1;
                var idxMatch = Regex.Match(attrs, @"\bindex=""(\d+)""", RegexOptions.CultureInvariant);
                if (idxMatch.Success)
                    int.TryParse(idxMatch.Groups[1].Value, out index);
                var durMatch = Regex.Match(attrs, @"\bduration=""(\d+)""", RegexOptions.CultureInvariant);
                if (durMatch.Success)
                    int.TryParse(durMatch.Groups[1].Value, out dur);
                if (dur < 1) dur = 1;
                if (index + dur <= start || index >= end)
                    continue;
                var body = m.Groups["body"].Success ? m.Groups["body"].Value : attrs;
                CollectChildLibraryNames(body, names);
            }
            return names;
        }

        private static void CollectChildLibraryNames(string xml, HashSet<string> names)
        {
            foreach (Match lib in LibraryItemNameAttrRegex.Matches(xml))
                names.Add(lib.Groups[1].Value);
            foreach (Match href in BitmapRefAttrRegex.Matches(xml))
                names.Add(href.Groups[1].Value);
        }

        private static void WalkOrganizeUsage(
            string itemName,
            string label,
            int start,
            Dictionary<string, (string Xml, LibraryItemKind Kind)> items,
            Dictionary<string, HashSet<string>> usage,
            Dictionary<string, (string Label, int Start)> firstPose,
            HashSet<string> stack)
        {
            if (string.IsNullOrWhiteSpace(itemName) || !stack.Add(itemName))
                return;
            if (!usage.TryGetValue(itemName, out var labels))
            {
                labels = new HashSet<string>(StringComparer.Ordinal);
                usage[itemName] = labels;
            }
            labels.Add(label);
            if (!firstPose.TryGetValue(itemName, out var cur) || start < cur.Start)
                firstPose[itemName] = (label, start);
            if (!items.TryGetValue(itemName, out var info))
                return;
            if (info.Kind is LibraryItemKind.Bitmap or LibraryItemKind.Sound or LibraryItemKind.Font)
                return;
            var children = new HashSet<string>(StringComparer.Ordinal);
            CollectChildLibraryNames(info.Xml, children);
            foreach (var child in children)
            {
                var resolved = ResolveOrganizeItemName(child, items);
                if (resolved == null || resolved.Equals(itemName, StringComparison.Ordinal))
                    continue;
                WalkOrganizeUsage(resolved, label, start, items, usage, firstPose, stack);
            }
        }

        private static string? ResolveOrganizeItemName(
            string raw,
            Dictionary<string, (string Xml, LibraryItemKind Kind)> items)
        {
            if (items.ContainsKey(raw))
                return raw;
            var slash = raw.Replace('\\', '/');
            var leaf = slash.Contains('/') ? slash[(slash.LastIndexOf('/') + 1)..] : slash;
            var noExt = Path.GetFileNameWithoutExtension(leaf);
            foreach (var key in items.Keys)
            {
                var baseName = BaseLibraryItemName(key);
                if (baseName.Equals(raw, StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals(leaf, StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals(noExt, StringComparison.OrdinalIgnoreCase))
                    return key;
            }
            return null;
        }

        /// <summary>
        /// Extra methods (not ctor, not JPEXS <c>frameN</c>) formatted for a timeline frame.
        /// </summary>
        public static string ExtractExtraFlaMethodsForTimeline(string asText, string className)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return "";
            var methods = ExtractAs3Methods(asText);
            var sb = new StringBuilder();
            foreach (var method in methods.Values)
            {
                if (!string.IsNullOrEmpty(className) &&
                    method.Name.Equals(className, StringComparison.Ordinal))
                    continue;
                if (JpexsFrameHandlerRegex.IsMatch(method.Name))
                    continue;
                sb.Append(method.FormatTimelineFunction());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Match a leftover <c>{ident}_fla</c> class to a library clip after linkage is gone:
        /// symbol name, file stem, or a timeline call of one of the extra methods (setupTaunt).
        /// </summary>
        public static string? FindLibraryClipForFlaSidecar(
            Dictionary<string, byte[]> entries,
            string sidecarClass,
            string extraMethods)
        {
            if (entries == null || string.IsNullOrWhiteSpace(sidecarClass))
                return null;
            var want = ExpandFlaSymbolMatchKeys(sidecarClass);
            string? callHit = null;
            var bestScore = 0;
            foreach (var key in entries.Keys)
            {
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var have = ExpandFlaSymbolMatchKeys(Path.GetFileNameWithoutExtension(key.Replace('\\', '/')));
                var item = Regex.Match(xml, @"<DOMSymbolItem\b[^>]*\bname=""([^""]+)""", RegexOptions.IgnoreCase);
                if (item.Success)
                    have.UnionWith(ExpandFlaSymbolMatchKeys(item.Groups[1].Value));
                var linkage = LinkageClassNameRegex.Match(xml);
                if (linkage.Success)
                {
                    var full = linkage.Groups[1].Value;
                    var shortName = full;
                    var dot = full.LastIndexOf('.');
                    if (dot >= 0)
                        shortName = full[(dot + 1)..];
                    have.UnionWith(ExpandFlaSymbolMatchKeys(shortName));
                }
                if (want.Overlaps(have))
                    return key;
                var score = CountExtraFlaMethodRefs(xml, extraMethods);
                if (score > bestScore)
                {
                    bestScore = score;
                    callHit = key;
                }
            }
            return callHit;
        }

        public static HashSet<string> ExpandFlaSymbolMatchKeys(string? raw)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw))
                return keys;
            void add(string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return;
                var t = s.Trim();
                keys.Add(t);
                keys.Add(t.Replace(" ", "", StringComparison.Ordinal));
                keys.Add(t.Replace(" ", "_", StringComparison.Ordinal));
                keys.Add(t.Replace("_", " ", StringComparison.Ordinal));
            }
            add(raw);
            add(Regex.Replace(raw, @"_\d+$", ""));
            add(Regex.Replace(raw.Replace(" ", "", StringComparison.Ordinal), @"([A-Za-z])(\d)", "$1 $2"));
            return keys;
        }

        public static bool TimelineCallsExtraFlaMethod(string xml, string extraMethods) =>
            CountExtraFlaMethodRefs(xml, extraMethods) > 0;

        public static int CountExtraFlaMethodRefs(string xml, string extraMethods)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(extraMethods))
                return 0;
            var n = 0;
            foreach (Match m in Regex.Matches(extraMethods, @"function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("))
            {
                var name = m.Groups[1].Value;
                if (name.Length < 4 || Regex.IsMatch(name, @"^frame\d+$", RegexOptions.IgnoreCase))
                    continue;
                if (Regex.IsMatch(xml, @"\b(?:this\.)?" + Regex.Escape(name) + @"\s*\(") ||
                    Regex.IsMatch(xml, @"\bthis\." + Regex.Escape(name) + @"\b"))
                    n++;
            }
            return n;
        }

        /// <summary>
        /// Extra fields that are not display-object instance names (e.g. <c>self</c>, <c>xframe</c>).
        /// </summary>
        public static string ExtractExtraFlaVarsForTimeline(string asText, string className)
        {
            if (string.IsNullOrWhiteSpace(asText))
                return "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (Match m in ExtraAs3VarRegex.Matches(asText))
            {
                var n = m.Groups["n"].Value;
                if (string.IsNullOrEmpty(n) ||
                    n.Equals(className, StringComparison.Ordinal) ||
                    !seen.Add(n))
                    continue;
                if (IsDisplayObjectType(m.Groups["t"].Value))
                    continue;
                sb.Append("var ").Append(n).Append(":*;\n");
            }
            return sb.ToString();
        }

        private static bool IsDisplayObjectType(string typeName)
        {
            var t = (typeName ?? "").Replace(" ", "", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(t) || t == "*" || t.Equals("Object", StringComparison.Ordinal))
                return false;
            var simple = t;
            var dot = t.LastIndexOf('.');
            if (dot >= 0)
                simple = t[(dot + 1)..];
            return simple is "MovieClip" or "Sprite" or "SimpleButton" or "TextField"
                or "Shape" or "DisplayObject" or "DisplayObjectContainer"
                or "Bitmap" or "Video" or "MorphShape" or "StaticText" or "Button";
        }

        public static string PrependFirstTimelineScript(string libraryXml, string extra)
        {
            if (string.IsNullOrWhiteSpace(libraryXml) || string.IsNullOrWhiteSpace(extra))
                return libraryXml;
            var done = false;
            var replaced = ActionsScriptBlockRegex.Replace(libraryXml, m =>
            {
                if (done)
                    return m.Value;
                done = true;
                var body = UnescapeXml(m.Groups["body"].Value.Replace("\r\n", "\n", StringComparison.Ordinal));
                if (body.IndexOf(extra.Trim(), StringComparison.Ordinal) >= 0)
                    return m.Value;
                var next = extra.TrimEnd() + "\n" + body;
                return "<Actionscript><script><![CDATA[" + EscapeCData(next) + "]]></script></Actionscript>";
            }, 1);
            if (done)
                return replaced;

            var inserted = false;
            return new Regex(@"<DOMFrame\b[^>]*?/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Replace(libraryXml, m =>
                {
                    if (inserted)
                        return m.Value;
                    inserted = true;
                    var tag = m.Value;
                    var script = "<Actionscript><script><![CDATA[" + extra.TrimEnd() + "]]></script></Actionscript>";
                    if (tag.EndsWith("/>", StringComparison.Ordinal))
                        return tag[..^2] + ">" + script + "</DOMFrame>";
                    return tag + script;
                }, 1);
        }

        /// <summary>
        /// <c>{id}_fla</c> class names of clips placed as <c>stance</c> on the host.
        /// </summary>
        public static HashSet<string> CollectStanceChildFlaClassNames(string flaPath, string baseName)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return new HashSet<string>(StringComparer.Ordinal);
            return CollectStanceChildFlaClassNames(ReadFlaZipEntries(flaPath), baseName);
        }

        private static HashSet<string> CollectStanceChildFlaClassNames(
            Dictionary<string, byte[]> entries,
            string baseName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var host = FindStanceHostLibraryXml(entries);
            if (host == null)
                return names;
            var items = CollectStanceChildLibraryItemNames(host.Value.Xml);
            if (items.Count == 0)
                return names;
            var prefix = (string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim()) + "_fla.";
            foreach (var kv in entries)
            {
                if (!kv.Key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var itemName = Path.GetFileNameWithoutExtension(kv.Key.Replace('\\', '/'));
                if (string.IsNullOrEmpty(itemName) || !items.Contains(itemName))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                var classMatch = LinkageClassNameRegex.Match(xml);
                if (!classMatch.Success)
                    continue;
                var className = classMatch.Groups[1].Value;
                if (!className.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                names.Add(className);
                var dot = className.LastIndexOf('.');
                if (dot >= 0)
                    names.Add(className[(dot + 1)..]);
            }
            return names;
        }

        /// <summary>
        /// Prepends extra methods/vars from stance-child <c>_fla</c> sidecars onto those
        /// clips' frame 1, then deletes the sidecar files so Idle is not kept for linkage.
        /// Leftover Grab-style classes stay on disk for JSFL.
        /// </summary>
        public static int InjectStanceChildFlaMethods(string flaPath, string outputDirectory, string baseName)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var entries = ReadFlaZipEntries(flaPath);
            var injected = InjectStanceChildFlaMethodsInEntries(entries, outputDirectory, baseName);
            if (injected > 0)
                WriteFlaZipAtomic(flaPath, entries);
            return injected;
        }

        private static int InjectStanceChildFlaMethodsInEntries(
            Dictionary<string, byte[]> entries,
            string outputDirectory,
            string baseName)
        {
            var stanceClasses = CollectStanceChildFlaClassNames(entries, baseName);
            if (stanceClasses.Count == 0)
                return 0;

            var asByShort = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(outputDirectory))
            {
                foreach (var dir in Directory.GetDirectories(outputDirectory, "*_fla", SearchOption.AllDirectories))
                {
                    foreach (var asFile in Directory.GetFiles(dir, "*.as"))
                    {
                        var text = File.ReadAllText(asFile);
                        var shortName = ParseAs3ClassName(text) ?? Path.GetFileNameWithoutExtension(asFile);
                        if (string.IsNullOrEmpty(shortName) || !stanceClasses.Contains(shortName))
                            continue;
                        asByShort[shortName] = asFile;
                    }
                }
            }

            var injected = 0;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var classMatch = LinkageClassNameRegex.Match(xml);
                if (!classMatch.Success)
                    continue;
                var className = classMatch.Groups[1].Value;
                var shortName = className;
                var dot = className.LastIndexOf('.');
                if (dot >= 0)
                    shortName = className[(dot + 1)..];
                if (!stanceClasses.Contains(shortName) && !stanceClasses.Contains(className))
                    continue;
                if (!asByShort.TryGetValue(shortName, out var asPath))
                    continue;
                var asText = File.ReadAllText(asPath);
                var extra = ExtractExtraFlaVarsForTimeline(asText, shortName)
                            + ExtractExtraFlaMethodsForTimeline(asText, shortName);
                if (string.IsNullOrWhiteSpace(extra))
                    continue;
                var next = PrependFirstTimelineScript(xml, extra);
                if (string.Equals(next, xml, StringComparison.Ordinal))
                    continue;
                entries[key] = Encoding.UTF8.GetBytes(next);
                injected++;
                try { File.Delete(asPath); } catch { /* locked */ }
            }
            return injected;
        }

        /// <summary>
        /// Class names from leftover <c>{base}_fla/*.as</c> sidecars (empty stubs already pruned).
        /// Those clips keep AS3 linkage so methods like <c>xSpeedDecay</c> still compile.
        /// </summary>
        public static HashSet<string> CollectRemainingFlaSidecarClassNames(string outputDirectory, string baseName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return names;
            var prefix = (string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim()) + "_fla";
            foreach (var dir in Directory.GetDirectories(outputDirectory, "*_fla", SearchOption.AllDirectories))
            {
                foreach (var asFile in Directory.GetFiles(dir, "*.as"))
                {
                    var shortName = ParseAs3ClassName(File.ReadAllText(asFile))
                                    ?? Path.GetFileNameWithoutExtension(asFile);
                    if (string.IsNullOrEmpty(shortName))
                        continue;
                    names.Add(shortName);
                    names.Add(prefix + "." + shortName);
                    var folder = Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(folder))
                        names.Add(folder + "." + shortName);
                }
            }
            return names;
        }

        /// <summary>
        /// Strips <c>linkageExportForAS</c> / <c>linkageClassName</c> when the symbol class is
        /// <c>{baseName}_fla.*</c>. Other exports stay unchanged. Classes in
        /// <paramref name="keepFlaClasses"/> (e.g. Grab_106) keep linkage.
        /// </summary>
        public static string StripTimelineFlaExportLinkageFromSymbolXml(
            string xml,
            string baseName,
            ISet<string>? keepFlaClasses = null)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return xml;

            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();
            var classMatch = LinkageClassNameRegex.Match(xml);
            if (!classMatch.Success)
                return xml;

            var className = classMatch.Groups[1].Value;
            if (!className.StartsWith(name + "_fla.", StringComparison.Ordinal))
                return xml;

            var shortName = className;
            var dot = className.LastIndexOf('.');
            if (dot >= 0 && dot < className.Length - 1)
                shortName = className[(dot + 1)..];
            if (keepFlaClasses != null &&
                (keepFlaClasses.Contains(className) || keepFlaClasses.Contains(shortName)))
                return xml;

            var stripped = Regex.Replace(
                xml,
                @"\s+linkageExportForAS=""true""",
                "",
                RegexOptions.CultureInvariant);
            stripped = Regex.Replace(
                stripped,
                @"\s+linkageClassName=""[^""]+""",
                "",
                RegexOptions.CultureInvariant);
            return stripped;
        }

        /// <summary>
        /// Strips AS3 export linkage from every LIBRARY symbol whose class is
        /// <c>{baseName}_fla.*</c>. Safe to run after <see cref="RebuildTimelineFlaAnimations"/>
        /// so symbols without a matching decompiled class still become plain MovieClips.
        /// </summary>
        /// <returns>Number of LIBRARY entries rewritten.</returns>
        public static int StripAllTimelineFlaExportLinkage(
            string flaPath,
            string baseName,
            ISet<string>? keepFlaClasses = null)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;

            var name = string.IsNullOrWhiteSpace(baseName) ? "character" : baseName.Trim();
            var patches = new List<(string EntryName, string Content)>();
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                        !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string xml;
                    using (var reader = new StreamReader(entry.Open()))
                        xml = reader.ReadToEnd();

                    var next = StripTimelineFlaExportLinkageFromSymbolXml(xml, name, keepFlaClasses);
                    if (!string.Equals(next, xml, StringComparison.Ordinal))
                        patches.Add((entry.FullName, next));
                }
            }

            foreach (var (entryName, content) in patches)
                ReplaceZipEntry(flaPath, entryName, content);
            return patches.Count;
        }

        /// <summary>
        /// Turns off Animate auto-declare of stage instances.
        /// Warnings mode stays on so real issues still surface once <c>_fla</c> linkage is stripped.
        /// </summary>
        public static bool DisableAutoDeclareStageInstances(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return false;

            var changed = false;
            if (TryPatchFlaZipEntry(flaPath, "DOMDocument.xml", ApplyAutoDeclareStageInstancesOff))
                changed = true;
            if (TryPatchFlaZipEntry(flaPath, "PublishSettings.xml", ApplyAnimateCompilerSettings))
                changed = true;
            return changed;
        }

        /// <summary>
        /// Clone pass on a stock JPEXS FLA: overlay box layers, PCM, Scene 1,
        /// organize leaves, outline, prune empty symbols. Does not run JSFL.
        /// </summary>
        public static bool ApplyCloneFlaPass(string flaPath, string outputDirectory, string ident)
            => RebuildFlaLibrary(flaPath, outputDirectory, ident);

        /// <summary>
        /// One zip rewrite: leftover <c>_fla</c> inject, linkage strip, publish flags,
        /// PCM sound bins, host rename, clone pass. Does not run JSFL.
        /// </summary>
        public static bool RebuildFlaLibrary(string flaPath, string outputDirectory, string ident)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return false;

            var entries = ReadFlaZipEntries(flaPath);
            InjectLeftoverFlaMethodsInEntries(entries, outputDirectory);
            InjectStanceChildFlaMethodsInEntries(entries, outputDirectory, ident);
            var keepFla = CollectRemainingFlaSidecarClassNames(outputDirectory, ident);
            var hasSounds = entries.Values.Any(b =>
            {
                var s = Encoding.UTF8.GetString(b);
                return s.IndexOf("DOMSoundItem", StringComparison.OrdinalIgnoreCase) >= 0;
            });
            var ffmpeg = FindBundledFlvToWav();
            if (hasSounds)
                ConvertSoundBinsInEntries(entries, ffmpeg);
            var displayName = ResolveFlaDisplayName(flaPath, outputDirectory);
            var identLeaf = ident ?? Path.GetFileNameWithoutExtension(flaPath) ?? "";
            var organizeTsv = BuildFlaOrganizeBlueprintFromEntries(entries, identLeaf);

            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = xml;
                if (key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase))
                {
                    next = StripTimelineFlaExportLinkageFromSymbolXml(next, ident ?? "", keepFla);
                    next = ApplyFlaTimelineLayerPolicy(next, displayName, IsHostSymbolXml(next, ident ?? "", displayName));
                }
                next = DisableBitmapSmoothingInXml(next);
                var file = Path.GetFileName(key);
                if (file.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase) ||
                    file.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase))
                {
                    next = ApplyAutoDeclareStageInstancesOff(next);
                    next = MergeCommonIntoDomSourcePath(next);
                    next = ApplyFlaTimelineLayerPolicy(next, displayName, isHost: false);
                }
                if (file.Equals("PublishSettings.xml", StringComparison.OrdinalIgnoreCase) ||
                    file.Equals("PublishSettings.xml", StringComparison.OrdinalIgnoreCase))
                {
                    next = ApplyAnimateCompilerSettings(next);
                    next = MergeCommonIntoAs3PackagePaths(next);
                }
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }

            TryApplyHostRenameInEntries(entries, ident ?? "");
            PromoteSmashChargeInEntries(entries);
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !key.EndsWith(".as", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = RewriteCharacterExtCasts(xml, ident ?? "");
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }
            ApplyFlaOrganizeNamePrefixes(entries, organizeTsv);
            StripInstancesToEmptySymbols(entries);
            PruneEmptyLibrarySymbols(entries);
            StripInstancesToEmptySymbols(entries);
            WriteFlaZipAtomic(flaPath, entries);
            return true;
        }

        /// <summary>
        /// Zip-level checks for FLA2. Empty list means the clone pass is acceptable.
        /// </summary>
        public static IReadOnlyList<string> AuditFlaExport(string flaPath)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
            {
                issues.Add("FLA file missing");
                return issues;
            }
            var entries = ReadFlaZipEntries(flaPath);
            foreach (var key in entries.Keys)
            {
                if (key.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                    issues.Add("leftover FLV: " + key);
            }
            if (entries.TryGetValue("DOMDocument.xml", out var domBytes))
            {
                var xml = Encoding.UTF8.GetString(domBytes);
                var scenes = Regex.Matches(xml, @"<DOMTimeline\b[^>]*\bname=""([^""]+)""", RegexOptions.IgnoreCase);
                if (scenes.Count != 1)
                    issues.Add("expected 1 scene, found " + scenes.Count);
                else if (!string.Equals(scenes[0].Groups[1].Value, "Scene 1", StringComparison.Ordinal))
                    issues.Add("scene name is " + scenes[0].Groups[1].Value);
            }
            var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entries)
            {
                if (!kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                foreach (Match m in Regex.Matches(
                             xml,
                             @"<(?:DOMSymbolItem|DOMBitmapItem|DOMSoundItem|DOMFontItem|DOMVideoItem)\b[^>]*\bname=""([^""]+)""",
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    libraryNames.Add(m.Groups[1].Value);
                    libraryNames.Add(BaseLibraryItemName(m.Groups[1].Value));
                }
            }
            var seenUnresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entries)
            {
                if (!kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                foreach (Match m in Regex.Matches(xml, @"\blibraryItemName=""([^""]+)""", RegexOptions.CultureInvariant))
                {
                    var name = m.Groups[1].Value;
                    if (libraryNames.Contains(name) || libraryNames.Contains(BaseLibraryItemName(name)))
                        continue;
                    if (seenUnresolved.Add(name))
                        issues.Add("unresolved libraryItemName: " + name);
                }
                if (Regex.IsMatch(xml, @"<DOMLayer\b[^>]*\bname=""HitBox""", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(xml, @"<DOMLayer\b[^>]*\bname=""HitBox""[^>]*\boutline=""true""", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(xml, @"<DOMLayer\b[^>]*\boutline=""true""[^>]*\bname=""HitBox""", RegexOptions.IgnoreCase))
                    issues.Add("HitBox layer without outline");
            }
            return issues;
        }

        /// <summary>
        /// Renames the stance host in the FLA zip to the display name (e.g. <c>Bmi</c>)
        /// and sets linkage to the ident (<c>bmi</c>). Updates zip entry, Include href,
        /// and every <c>libraryItemName</c> so Scene 1 matches without JSFL renameItem.
        /// </summary>
        public static bool PatchFlaHostSymbol(string flaPath, string ident)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return false;
            var entries = ReadFlaZipEntries(flaPath);
            if (!TryApplyHostRenameInEntries(entries, ident))
                return false;
            WriteFlaZipAtomic(flaPath, entries);
            return true;
        }

        public static string PatchHostSymbolOpeningTag(string xml, string displayName, string linkageClass)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return xml;
            var done = false;
            return Regex.Replace(
                xml,
                @"<DOMSymbolItem\b(?<a>[^>]*)>",
                m =>
                {
                    if (done)
                        return m.Value;
                    done = true;
                    var attrs = m.Groups["a"].Value;
                    attrs = SetOrInsertXmlAttribute(" " + attrs.Trim(), "name", displayName).Trim();
                    attrs = SetOrInsertXmlAttribute(" " + attrs.Trim(), "linkageClassName", linkageClass).Trim();
                    attrs = SetOrInsertXmlAttribute(" " + attrs.Trim(), "linkageExportForAS", "true").Trim();
                    if (!attrs.StartsWith(" ", StringComparison.Ordinal))
                        attrs = " " + attrs;
                    return "<DOMSymbolItem" + attrs + ">";
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                matchTimeout: TimeSpan.FromSeconds(2));
        }

        private static string? ReadLibraryItemName(string xml)
        {
            var m = Regex.Match(xml ?? "", @"<DOMSymbolItem\b[^>]*\bname=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string RewriteLibraryItemReferences(string xml, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(oldName) || oldName.Equals(newName, StringComparison.Ordinal))
                return xml;
            var next = Regex.Replace(
                xml,
                @"\blibraryItemName=""" + Regex.Escape(oldName) + @"""",
                "libraryItemName=\"" + newName + "\"",
                RegexOptions.CultureInvariant);
            var oldXml = oldName + ".xml";
            var newXml = newName + ".xml";
            next = Regex.Replace(
                next,
                @"\bhref=""" + Regex.Escape(oldXml) + @"""",
                "href=\"" + newXml + "\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            next = Regex.Replace(
                next,
                @"\bhref=""LIBRARY/" + Regex.Escape(oldXml) + @"""",
                "href=\"LIBRARY/" + newXml + "\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            next = Regex.Replace(
                next,
                @"\bname=""" + Regex.Escape(oldName) + @"""",
                "name=\"" + newName + "\"",
                RegexOptions.CultureInvariant);
            var oldExt = Path.GetExtension(oldName);
            if (!string.IsNullOrEmpty(oldExt) &&
                !oldExt.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                var newHref = newName.EndsWith(oldExt, StringComparison.OrdinalIgnoreCase)
                    ? newName
                    : newName + oldExt;
                next = Regex.Replace(
                    next,
                    @"\bhref=""" + Regex.Escape(oldName) + @"""",
                    "href=\"" + newHref + "\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return next;
        }

        /// <summary>
        /// Organize-safe rewrite: <c>libraryItemName</c> only.
        /// Does not touch <c>href</c> or generic <c>name=</c> (Include files stay flat).
        /// </summary>
        public static string RewriteLibraryOrganizeRefs(string xml, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(oldName) ||
                oldName.Equals(newName, StringComparison.Ordinal))
                return xml;
            return Regex.Replace(
                xml,
                @"\blibraryItemName=""" + Regex.Escape(oldName) + @"""",
                "libraryItemName=\"" + newName + "\"",
                RegexOptions.CultureInvariant);
        }

        public static string RewriteLibraryItemOpeningName(string xml, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(oldName) ||
                oldName.Equals(newName, StringComparison.Ordinal))
                return xml;
            return Regex.Replace(
                xml,
                @"<(?<tag>DOMSymbolItem|DOMBitmapItem|DOMSoundItem|DOMFontItem|DOMVideoItem)\b(?<a>[^>]*?)\bname=""" +
                Regex.Escape(oldName) + @"""",
                m => "<" + m.Groups["tag"].Value + m.Groups["a"].Value + "name=\"" + newName + "\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                matchTimeout: TimeSpan.FromSeconds(2));
        }

        public static string RewriteZipHref(string xml, string oldEntry, string newEntry)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(oldEntry) ||
                oldEntry.Equals(newEntry, StringComparison.OrdinalIgnoreCase))
                return xml;
            var oldHref = oldEntry.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase)
                ? oldEntry["LIBRARY/".Length..]
                : oldEntry;
            var newHref = newEntry.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase)
                ? newEntry["LIBRARY/".Length..]
                : newEntry;
            if (oldHref.Equals(newHref, StringComparison.OrdinalIgnoreCase))
                return xml;
            var next = Regex.Replace(
                xml,
                @"\bhref=""" + Regex.Escape(oldHref) + @"""",
                "href=\"" + newHref + "\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            next = Regex.Replace(
                next,
                @"\bhref=""LIBRARY/" + Regex.Escape(oldHref) + @"""",
                "href=\"LIBRARY/" + newHref + "\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return next;
        }

        private static bool RewriteFlaHostReferences(
            string flaPath,
            string oldName,
            string newName,
            string oldEntry,
            string newEntry,
            string hostXml)
        {
            var changed = false;
            var xmlPatches = new List<(string Entry, string Xml)>();
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (entry.FullName.Equals(oldEntry, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string xml;
                    using (var reader = new StreamReader(entry.Open()))
                        xml = reader.ReadToEnd();
                    var next = RewriteLibraryItemReferences(xml, oldName, newName);
                    if (!string.Equals(next, xml, StringComparison.Ordinal))
                        xmlPatches.Add((entry.FullName, next));
                }
            }

            foreach (var (entry, xml) in xmlPatches)
            {
                ReplaceZipEntry(flaPath, entry, xml);
                changed = true;
            }

            ReplaceZipEntry(flaPath, newEntry, hostXml);
            if (!oldEntry.Equals(newEntry, StringComparison.OrdinalIgnoreCase))
            {
                DeleteZipEntry(flaPath, oldEntry);
                changed = true;
            }

            return changed;
        }

        private static void DeleteZipEntry(string zipPath, string entryName)
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
            var existing = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
            existing?.Delete();
        }

        private static Dictionary<string, byte[]> ReadFlaZipXmlEntries(string flaPath)
        {
            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var zip = ZipFile.OpenRead(flaPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) ||
                    !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                map[entry.FullName.Replace('\\', '/')] = ms.ToArray();
            }
            return map;
        }

        private static void ReplaceFlaZipXmlEntries(string flaPath, Dictionary<string, byte[]> xmlEntries)
        {
            using var zip = ZipFile.Open(flaPath, ZipArchiveMode.Update);
            foreach (var kv in xmlEntries)
            {
                var name = kv.Key.Replace('\\', '/');
                zip.GetEntry(name)?.Delete();
                var created = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var stream = created.Open();
                stream.Write(kv.Value, 0, kv.Value.Length);
            }
        }

        private static Dictionary<string, byte[]> ReadFlaZipEntries(string flaPath)
        {
            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var zip = ZipFile.OpenRead(flaPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                map[entry.FullName.Replace('\\', '/')] = ms.ToArray();
            }
            return map;
        }

        private static void WriteFlaZipAtomic(string flaPath, Dictionary<string, byte[]> entries)
        {
            var tmp = flaPath + ".tmp";
            if (File.Exists(tmp))
                File.Delete(tmp);
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var kv in entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var name = kv.Key.Replace('\\', '/');
                    var created = zip.CreateEntry(name, FlaZipCompression(name));
                    using var stream = created.Open();
                    stream.Write(kv.Value, 0, kv.Value.Length);
                }
            }
            File.Copy(tmp, flaPath, overwrite: true);
            try { File.Delete(tmp); } catch { /* ignore */ }
        }

        /// <summary>
        /// Rewrites the FLA from the in-memory entry map in one pass.
        /// Zip update mode rewrites the archive once per changed entry and was the slow path.
        /// </summary>
        private static void PatchFlaZipEntries(string flaPath, Dictionary<string, byte[]> entries) =>
            WriteFlaZipAtomic(flaPath, entries);

        private static CompressionLevel FlaZipCompression(string entryName)
        {
            var ext = Path.GetExtension(entryName);
            if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".xfl", StringComparison.OrdinalIgnoreCase))
                return CompressionLevel.Fastest;
            return CompressionLevel.NoCompression;
        }

        private static int ConvertSoundBinsInEntries(Dictionary<string, byte[]> entries, string? ffmpeg)
        {
            var names = entries.Keys.ToList();
            var binKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var datKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in names)
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                if (xml.IndexOf("DOMSoundItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                foreach (Match match in SoundItemTagRegex.Matches(xml))
                {
                    var tag = match.Value;
                    var href = GetXmlAttribute(tag, "href");
                    var binKey = FindSoundBinEntry(names, href);
                    if (binKey != null)
                        binKeys.Add(binKey);
                    var dat = FindZipMediaEntry(names, GetXmlAttribute(tag, "soundDataHRef"));
                    if (dat != null)
                        datKeys.Add(dat);
                }
            }

            var cache = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(binKeys, binKey =>
            {
                if (!entries.TryGetValue(binKey, out var raw))
                    return;
                if (!TryConvertBinBytesToPcm(ffmpeg, raw, binKey, out var wav) || wav == null)
                    return;
                cache[binKey] = wav;
            });

            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in cache)
            {
                var dest = kv.Key;
                if (!dest.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    dest = Path.ChangeExtension(kv.Key.Replace('\\', '/'), ".wav") ?? (kv.Key + ".wav");
                    entries.Remove(kv.Key);
                    renamed[kv.Key] = dest;
                }
                entries[dest] = kv.Value;
            }

            foreach (var key in names)
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                if (xml.IndexOf("DOMSoundItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var next = SoundItemTagRegex.Replace(xml, match =>
                {
                    var tag = match.Value;
                    var href = GetXmlAttribute(tag, "href");
                    var binKey = FindSoundBinEntry(names, href) ?? FindSoundBinEntry(entries.Keys.ToList(), href);
                    if (binKey == null || !cache.TryGetValue(binKey, out var wav))
                        return RemoveXmlAttribute(tag, "soundDataHRef");
                    if (!TryReadWavFormat(wav, out var info) || !info.IsPcm16)
                        return RemoveXmlAttribute(tag, "soundDataHRef");
                    var patched = PatchDomSoundItemXml(tag, info);
                    var leaf = Path.GetFileName((href ?? "").Replace('\\', '/'));
                    if (renamed.TryGetValue(binKey, out var newKey))
                    {
                        leaf = Path.GetFileName(newKey.Replace('\\', '/'));
                    }
                    else if (!string.IsNullOrEmpty(leaf))
                        leaf = Path.GetFileNameWithoutExtension(leaf) + ".wav";
                    if (!string.IsNullOrEmpty(leaf))
                    {
                        patched = SetOrInsertXmlAttribute(patched, "href", leaf);
                        patched = SetOrInsertXmlAttribute(patched, "name", leaf);
                    }
                    return patched;
                });
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }

            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = Regex.Replace(
                    xml,
                    @"\bsoundName=""([^""]+)\.(flv|mp3|mpa)""",
                    m => "soundName=\"" + m.Groups[1].Value.Replace('\\', '/') + ".wav\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }

            foreach (var dat in datKeys)
            {
                if (cache.ContainsKey(dat) || renamed.ContainsKey(dat))
                    continue;
                entries.Remove(dat);
            }
            return cache.Count;
        }

        private static bool TryConvertBinBytesToPcm(string? ffmpeg, byte[] raw, string entryName, out byte[] wav)
        {
            wav = raw;
            var kind = SniffSoundMagic(raw);
            if (kind == SoundMediaKind.RiffWav &&
                TryReadWavFormat(raw, out var info) &&
                info.IsPcm16 &&
                info.SampleRate == 44100)
                return true;
            if (!string.IsNullOrEmpty(ffmpeg))
            {
                var dirFfmpeg = Path.Combine(Path.GetTempPath(), "ssf2mm-pcm-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dirFfmpeg);
                try
                {
                    var ext = kind == SoundMediaKind.Flv ? ".flv"
                        : kind == SoundMediaKind.Mp3 ? ".mp3"
                        : kind == SoundMediaKind.RiffWav ? ".wav"
                        : Path.GetExtension(entryName);
                    if (string.IsNullOrEmpty(ext))
                        ext = ".flv";
                    var src = Path.Combine(dirFfmpeg, "in" + ext);
                    var dest = Path.Combine(dirFfmpeg, "out.wav");
                    File.WriteAllBytes(src, raw);
                    if (TryWritePcmWav(ffmpeg, src, dest, CancellationToken.None))
                    {
                        wav = File.ReadAllBytes(dest);
                        if (wav.Length > 44)
                            return true;
                    }
                }
                finally
                {
                    TryDeleteDirectory(dirFfmpeg);
                }
            }
            if (FlaSoundDecoder.TryDecodeToPcm16Wav(raw, out var decoded) && decoded.Length > 44)
            {
                wav = decoded;
                return true;
            }
            return false;
        }

        private static int InjectLeftoverFlaMethodsInEntries(
            Dictionary<string, byte[]> entries,
            string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
                return 0;

            var sidecars = new List<(string ShortName, string Path, string Text)>();
            foreach (var dir in Directory.GetDirectories(outputDirectory, "*_fla", SearchOption.AllDirectories))
            {
                foreach (var asFile in Directory.GetFiles(dir, "*.as"))
                {
                    string text;
                    try { text = File.ReadAllText(asFile); }
                    catch { continue; }
                    var shortName = ParseAs3ClassName(text) ?? Path.GetFileNameWithoutExtension(asFile);
                    if (string.IsNullOrEmpty(shortName))
                        continue;
                    sidecars.Add((shortName, asFile, text));
                }
            }
            if (sidecars.Count == 0)
                return 0;

            var byShort = sidecars
                .GroupBy(s => s.ShortName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var injected = 0;
            foreach (var sidecar in byShort.Values)
            {
                var extra = ExtractExtraFlaVarsForTimeline(sidecar.Text, sidecar.ShortName)
                            + ExtractExtraFlaMethodsForTimeline(sidecar.Text, sidecar.ShortName);
                if (string.IsNullOrWhiteSpace(extra))
                    continue;
                var clipKey = FindLibraryClipForFlaSidecar(entries, sidecar.ShortName, extra);
                if (clipKey == null)
                    continue;
                var xml = Encoding.UTF8.GetString(entries[clipKey]);
                var next = PrependFirstTimelineScript(xml, extra);
                if (string.Equals(next, xml, StringComparison.Ordinal))
                    continue;
                entries[clipKey] = Encoding.UTF8.GetBytes(next);
                used.Add(sidecar.ShortName);
                injected++;
            }

            foreach (var sc in sidecars)
            {
                if (!used.Contains(sc.ShortName))
                    continue;
                try { File.Delete(sc.Path); } catch { /* locked */ }
            }
            DeleteEmptyFlaSidecarFolders(outputDirectory);
            return injected;
        }

        private static void DeleteEmptyFlaSidecarFolders(string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
                return;
            foreach (var dir in Directory.GetDirectories(outputDirectory, "*_fla", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                    TryDeleteDirectory(dir);
            }
        }

        private static bool TryApplyHostRenameInEntries(Dictionary<string, byte[]> entries, string ident)
        {
            var host = FindStanceHostInEntries(entries);
            if (host == null)
                return false;
            var display = SanitizeLibraryItemName(FriendlyCharacterLibraryName(ident));
            var linkage = CharacterLinkageIdent(ident);
            if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(linkage))
                return false;

            var oldName = ReadLibraryItemName(host.Value.Xml)
                          ?? Path.GetFileNameWithoutExtension(host.Value.EntryName);
            var nextXml = PatchHostSymbolOpeningTag(host.Value.Xml, display, linkage);
            var oldEntry = host.Value.EntryName.Replace('\\', '/');
            var slash = oldEntry.LastIndexOf('/');
            var newEntry = (slash >= 0 ? oldEntry[..(slash + 1)] : "LIBRARY/") + display + ".xml";

            foreach (var key in entries.Keys.Where(k => k.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (key.Equals(oldEntry, StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = RewriteLibraryItemReferences(xml, oldName, display);
                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }

            entries[newEntry] = Encoding.UTF8.GetBytes(nextXml);
            if (!oldEntry.Equals(newEntry, StringComparison.OrdinalIgnoreCase))
                entries.Remove(oldEntry);
            return true;
        }

        private static (string EntryName, string Xml, string ClassName)? FindStanceHostInEntries(
            Dictionary<string, byte[]> entries)
        {
            string? bestEntry = null;
            string? bestXml = null;
            string? bestClass = null;
            var bestCount = 0;
            foreach (var kv in entries)
            {
                if (!kv.Key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(kv.Value);
                var stanceCount = StanceInstanceRegex.Matches(xml).Count;
                if (stanceCount <= bestCount)
                    continue;
                var classMatch = LinkageClassNameRegex.Match(xml);
                if (!classMatch.Success)
                    continue;
                bestCount = stanceCount;
                bestEntry = kv.Key;
                bestXml = xml;
                bestClass = classMatch.Groups[1].Value;
            }

            return bestCount > 0 && bestEntry != null && bestXml != null && bestClass != null
                ? (bestEntry, bestXml, bestClass)
                : null;
        }

        public readonly record struct FlaOrganizeMove(string Folder, string OldName, string NewLeaf)
        {
            public string NewLibraryPath =>
                string.IsNullOrEmpty(Folder) ? NewLeaf : Folder.Trim('/') + "/" + NewLeaf;
        }

        public static List<FlaOrganizeMove> ParseFlaOrganizeBlueprint(string tsv)
        {
            var moves = new List<FlaOrganizeMove>();
            if (string.IsNullOrWhiteSpace(tsv))
                return moves;
            foreach (var raw in tsv.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var parts = line.Split('\t');
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    continue;
                var folder = parts[0] ?? "";
                var oldName = parts[1].Trim().Replace('\\', '/');
                var newLeaf = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2])
                    ? parts[2].Trim()
                    : BaseLibraryItemName(oldName);
                moves.Add(new FlaOrganizeMove(folder, oldName, newLeaf));
            }
            return moves;
        }

        public static int ApplyFlaOrganizeTsv(Dictionary<string, byte[]> entries, string tsv)
        {
            var jobs = new List<(string OldName, string NewPath, string OldEntry)>();
            foreach (var move in ParseFlaOrganizeBlueprint(tsv))
            {
                var oldName = move.OldName;
                var newPath = move.NewLibraryPath;
                if (oldName.Equals(newPath, StringComparison.Ordinal))
                    continue;
                var oldEntry = FindLibraryEntry(entries, oldName);
                if (oldEntry == null)
                    continue;
                jobs.Add((oldName, newPath, oldEntry));
            }

            if (jobs.Count == 0)
                return 0;

            var ordered = jobs.OrderByDescending(j => j.OldName.Length).ToList();
            foreach (var key in entries.Keys.Where(k => k.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = xml;
                foreach (var job in ordered)
                {
                    next = RewriteLibraryOrganizeRefs(next, job.OldName, job.NewPath);
                    if (key.Equals(job.OldEntry, StringComparison.OrdinalIgnoreCase))
                        next = RewriteLibraryItemOpeningName(next, job.OldName, job.NewPath);
                }

                if (!string.Equals(next, xml, StringComparison.Ordinal))
                    entries[key] = Encoding.UTF8.GetBytes(next);
            }

            return jobs.Count;
        }

        public static int ApplyFlaOrganize(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var entries = ReadFlaZipEntries(flaPath);
            var n = ApplyFlaOrganizeTsv(entries, BuildFlaOrganizeBlueprint(flaPath));
            if (n > 0)
                WriteFlaZipAtomic(flaPath, entries);
            return n;
        }

        private static string? FindLibraryEntry(Dictionary<string, byte[]> entries, string itemName)
        {
            var target = (itemName ?? "").Replace('\\', '/');
            if (string.IsNullOrEmpty(target))
                return null;
            string? leafHit = null;
            var leafHits = 0;
            foreach (var key in entries.Keys)
            {
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rest = key["LIBRARY/".Length..];
                if (rest.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    rest.Equals(target + ".xml", StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            foreach (var key in entries.Keys)
            {
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var named = ReadAnyLibraryItemName(xml);
                if (named != null && named.Equals(target, StringComparison.Ordinal))
                    return key;
                var leaf = Path.GetFileNameWithoutExtension(key);
                if (leaf.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals(BaseLibraryItemName(target), StringComparison.OrdinalIgnoreCase))
                {
                    leafHit = key;
                    leafHits++;
                }
            }

            return leafHits == 1 ? leafHit : null;
        }

        private static string? ReadAnyLibraryItemName(string xml)
        {
            var m = Regex.Match(
                xml ?? "",
                @"<(?:DOMSymbolItem|DOMBitmapItem|DOMSoundItem|DOMFontItem|DOMVideoItem)\b[^>]*\bname=""([^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string OrganizeTargetEntry(string oldEntry, string newLibraryPath)
        {
            var ext = Path.GetExtension(oldEntry);
            if (string.IsNullOrEmpty(ext))
                ext = ".xml";
            var path = newLibraryPath.Replace('\\', '/').Trim('/');
            if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                path += ext;
            return "LIBRARY/" + path;
        }

        /// <summary>
        /// Donor body class suffixes that must collapse to the clone ident
        /// (<c>black_mage</c> ? <c>Bo_Mama</c>). Projectile suffixes are not in this list.
        /// </summary>
        public static readonly string[] DonorBodyClassAliases = { "black_mage", "blackmage" };

        /// <summary>
        /// Strips donor body aliases (<c>{ident}_black_mage</c>) from FLA <c>.as</c> sidecars.
        /// Does not rewrite projectile ids such as <c>{ident}_bmmeteorprojectile</c>.
        /// Also restores projectile <c>linkage_id</c>s that were previously collapsed onto the ident.
        /// </summary>
        public static bool PatchFlaMainLinkageId(string outputDirectory, string ident)
        {
            var id = CharacterLinkageIdent(ident);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory) ||
                string.IsNullOrWhiteSpace(id))
                return false;

            var asFiles = Directory.GetFiles(outputDirectory, "*.as", SearchOption.AllDirectories);
            if (asFiles.Length == 0)
                return false;

            var changed = false;
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in asFiles)
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }
                texts[path] = text;
            }

            foreach (var path in texts.Keys.ToList())
            {
                var next = StripDonorBodyLinkageAliases(texts[path], id);
                if (!string.Equals(next, texts[path], StringComparison.Ordinal))
                {
                    texts[path] = next;
                    changed = true;
                }
            }

            var sidecars = texts
                .Where(kv => !Path.GetFileName(kv.Key).Equals("Main.as", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .ToList();
            foreach (var path in texts.Keys.ToList())
            {
                if (!Path.GetFileName(path).Equals("Main.as", StringComparison.OrdinalIgnoreCase))
                    continue;
                var next = RestoreCollapsedProjectileLinkageIds(texts[path], sidecars, id);
                if (!string.Equals(next, texts[path], StringComparison.Ordinal))
                {
                    texts[path] = next;
                    changed = true;
                }
            }

            if (!changed)
                return false;
            foreach (var kv in texts)
                File.WriteAllText(kv.Key, kv.Value);
            return true;
        }

        /// <summary>
        /// Revival / pose timeline scripts use JPEXS Pascal Ext (<c>as CloneDayExt</c>)
        /// while the live class is <c>{ident}Ext</c>. Rewrite sidecar <c>.as</c> and
        /// FLA zip <c>DOMFrame</c> Actionscript.
        /// </summary>
        public static bool PatchFlaExtCasts(string outputDirectory, string ident, string? flaPath = null)
        {
            var changed = false;
            if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
            {
                foreach (var path in Directory.GetFiles(outputDirectory, "*.as", SearchOption.AllDirectories))
                {
                    string text;
                    try { text = File.ReadAllText(path); }
                    catch { continue; }
                    var next = RewriteCharacterExtCasts(text, ident);
                    if (string.Equals(next, text, StringComparison.Ordinal))
                        continue;
                    File.WriteAllText(path, next);
                    changed = true;
                }
            }

            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return changed;

            var entries = ReadFlaZipEntries(flaPath);
            var zipChanged = false;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !key.EndsWith(".as", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                var next = RewriteCharacterExtCasts(xml, ident);
                if (string.Equals(next, xml, StringComparison.Ordinal))
                    continue;
                entries[key] = Encoding.UTF8.GetBytes(next);
                zipChanged = true;
            }

            if (zipChanged)
            {
                WriteFlaZipAtomic(flaPath, entries);
                changed = true;
            }

            return changed;
        }

        public static string RewriteCharacterExtCasts(string text, string ident)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(ident))
                return text;
            var id = CharacterLinkageIdent(ident);
            if (string.IsNullOrWhiteSpace(id))
                return text;
            var canonical = id + "Ext";
            var next = text;
            foreach (var alias in CharacterExtCastAliases(id, canonical))
            {
                next = Regex.Replace(
                    next,
                    "(?<![A-Za-z0-9_])" + Regex.Escape(alias) + "(?![A-Za-z0-9_])",
                    canonical,
                    RegexOptions.CultureInvariant);
            }

            return next;
        }

        private static IEnumerable<string> CharacterExtCastAliases(string id, string canonical)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            if (!IsBlackMageIdent(id))
            {
                aliases.Add("BlackMageExt");
                var pascalExt = PascalCharacterIdent(id) + "Ext";
                if (!pascalExt.Equals(canonical, StringComparison.Ordinal))
                    aliases.Add(pascalExt);
            }

            aliases.Remove(canonical);
            return aliases;
        }

        private static bool IsBlackMageIdent(string id) =>
            id.Equals("black_mage", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("blackmage", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Smash-charge FX clips must stay MovieClips. Graphic timelines do not play
        /// while the fighter holds charge.
        /// </summary>
        public static bool IsSmashChargeClip(string? itemName)
        {
            var leaf = BaseLibraryItemName(itemName ?? "");
            var compact = Regex.Replace(leaf, @"[\s_\-]+", "").ToLowerInvariant();
            if (compact.Length == 0)
                return false;
            if (compact.Contains("uncharged"))
                return true;
            if (compact.Contains("usmash") || compact.Contains("upsmash"))
                return true;
            if (compact.Contains("fsmash") || compact.Contains("forwardsmash"))
                return true;
            if (compact.Contains("dsmash") || compact.Contains("downsmash"))
                return true;
            return compact is "ice" or "hands";
        }

        public static bool PromoteSmashChargeClipsToMovieClip(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return false;

            var entries = ReadFlaZipEntries(flaPath);
            var changed = PromoteSmashChargeInEntries(entries);
            if (changed)
                WriteFlaZipAtomic(flaPath, entries);
            return changed;
        }

        private static bool PromoteSmashChargeInEntries(Dictionary<string, byte[]> entries)
        {
            var nameToKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in entries.Keys)
            {
                if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                if (xml.IndexOf("<DOMSymbolItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var name = ReadLibraryItemName(xml) ?? Path.GetFileNameWithoutExtension(key);
                if (!string.IsNullOrEmpty(name) && !nameToKey.ContainsKey(name))
                    nameToKey[name] = key;
                var leaf = BaseLibraryItemName(name);
                if (!string.IsNullOrEmpty(leaf) && !nameToKey.ContainsKey(leaf))
                    nameToKey[leaf] = key;
            }

            var promoteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in nameToKey)
            {
                if (IsSmashChargeClip(kv.Key) || IsSmashChargeClip(Path.GetFileNameWithoutExtension(kv.Value)))
                    promoteKeys.Add(kv.Value);
            }

            var queue = new Queue<string>(promoteKeys);
            while (queue.Count > 0)
            {
                var key = queue.Dequeue();
                if (!entries.TryGetValue(key, out var bytes))
                    continue;
                var xml = Encoding.UTF8.GetString(bytes);
                var children = new HashSet<string>(StringComparer.Ordinal);
                CollectChildLibraryNames(xml, children);
                foreach (var child in children)
                {
                    if (!TryResolveLibraryZipKey(child, nameToKey, out var childKey) ||
                        !promoteKeys.Add(childKey))
                        continue;
                    queue.Enqueue(childKey);
                }
            }

            var changed = false;
            foreach (var key in promoteKeys)
            {
                if (!entries.TryGetValue(key, out var bytes))
                    continue;
                var xml = Encoding.UTF8.GetString(bytes);
                var next = Regex.Replace(
                    xml,
                    @"\bsymbolType=""graphic""",
                    "symbolType=\"movie clip\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (string.Equals(next, xml, StringComparison.Ordinal))
                    continue;
                entries[key] = Encoding.UTF8.GetBytes(next);
                changed = true;
            }

            return changed;
        }

        private static bool TryResolveLibraryZipKey(
            string childName,
            Dictionary<string, string> nameToKey,
            out string key)
        {
            if (nameToKey.TryGetValue(childName, out key!))
                return true;
            var leaf = BaseLibraryItemName(childName);
            if (!string.IsNullOrEmpty(leaf) && nameToKey.TryGetValue(leaf, out key!))
                return true;
            key = "";
            return false;
        }

        public static string StripDonorBodyLinkageAliases(string asText, string ident)
        {
            if (string.IsNullOrEmpty(asText) || string.IsNullOrWhiteSpace(ident))
                return asText;
            var id = CharacterLinkageIdent(ident);
            var next = asText;
            foreach (var alias in DonorBodyClassAliases)
            {
                var from = id + "_" + alias;
                next = Regex.Replace(
                    next,
                    "(?<![A-Za-z0-9_])" + Regex.Escape(from) + "(?![A-Za-z0-9_])",
                    id,
                    RegexOptions.CultureInvariant);
            }
            return next;
        }

        public static string RestoreCollapsedProjectileLinkageIds(
            string mainText,
            IReadOnlyList<string> sidecarTexts,
            string ident)
        {
            if (string.IsNullOrEmpty(mainText) || sidecarTexts == null || sidecarTexts.Count == 0)
                return mainText;
            var id = CharacterLinkageIdent(ident);
            var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var sidecar in sidecarTexts)
            {
                foreach (Match m in ProjectileLinkageIdRegex.Matches(sidecar ?? ""))
                {
                    var key = m.Groups["key"].Value;
                    var lid = m.Groups["lid"].Value;
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(lid))
                        continue;
                    if (lid.Equals(id, StringComparison.Ordinal) ||
                        lid.Equals(id + "_black_mage", StringComparison.Ordinal) ||
                        lid.Equals(id + "_blackmage", StringComparison.Ordinal))
                        continue;
                    byKey[key] = lid;
                }
            }

            if (byKey.Count == 0)
                return mainText;

            return ProjectileLinkageIdRegex.Replace(mainText, m =>
            {
                var key = m.Groups["key"].Value;
                var lid = m.Groups["lid"].Value;
                if (!lid.Equals(id, StringComparison.Ordinal))
                    return m.Value;
                if (!byKey.TryGetValue(key, out var restored))
                    return m.Value;
                return m.Value.Substring(0, m.Groups["lid"].Index - m.Index) +
                       restored +
                       m.Value.Substring(m.Groups["lid"].Index - m.Index + m.Groups["lid"].Length);
            });
        }

        private static readonly Regex ProjectileLinkageIdRegex = new(
            @"""(?<key>[A-Za-z_][A-Za-z0-9_]*)""\s*:\s*\{\s*""classAPI""\s*:\s*SSF2Projectile\s*,\s*""linkage_id""\s*:\s*""(?<lid>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Hits of forbidden donor body identity in FLA scripts and library XML.
        /// Projectile / FX names that merely contain <c>bm_</c> are not reported.
        /// </summary>
        public static List<string> CollectForbiddenFlaBodyIdentityHits(string directory, string ident)
        {
            var hits = new List<string>();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return hits;
            var id = CharacterLinkageIdent(ident);
            var forbidden = new List<string> { "black_mage", "blackmage", id + "_black_mage", id + "_blackmage" };
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return ext.Equals(".as", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".xml", StringComparison.OrdinalIgnoreCase);
                });
            foreach (var path in files)
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }
                foreach (var token in forbidden)
                {
                    foreach (Match m in Regex.Matches(
                                 text,
                                 "(?<![A-Za-z0-9_])" + Regex.Escape(token) + "(?![A-Za-z0-9_])",
                                 RegexOptions.CultureInvariant))
                    {
                        var rel = path.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        hits.Add(rel.Replace('\\', '/') + ":" + token);
                    }
                }
            }

            return hits;
        }

        /// <summary>
        /// Instance names containing <c>box</c> (hurtbox, attackbox, handbox, hurtbox2, �).
        /// </summary>
        public static bool IsFlaBoxInstanceName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var compact = name.Replace("_", "");
            if (compact.IndexOf("itembox", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return name.IndexOf("box", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly Regex DomSymbolInstanceNameRegex = new(
            @"<DOM(?:Symbol|Component)Instance\b[^>]*\bname=""([^""]*)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Layers that have a keyframe instance named <c>*box*</c> get Animate
        /// outline mode. Layer names like <c>Layer 1</c> are ignored.
        /// </summary>
        public static string MarkFlaBoxLayersAsOutlineInXml(string xml)
        {
            if (string.IsNullOrEmpty(xml) ||
                xml.IndexOf("<DOMLayer", StringComparison.OrdinalIgnoreCase) < 0)
                return xml;

            var opens = Regex.Matches(xml, @"<DOMLayer\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (opens.Count == 0)
                return xml;

            var next = xml;
            for (var i = opens.Count - 1; i >= 0; i--)
            {
                var open = opens[i];
                var bodyStart = open.Index + open.Length;
                var close = FindMatchingDomLayerClose(next, bodyStart);
                if (close < 0)
                    continue;
                var ownFrames = StripNestedDomLayers(next[bodyStart..close]);
                if (!DomSymbolInstanceNameRegex.Matches(ownFrames)
                        .Cast<Match>()
                        .Any(im => IsFlaBoxInstanceName(im.Groups[1].Value)))
                    continue;
                var patched = SetOrInsertXmlAttribute(open.Value, "outline", "true");
                if (GetXmlAttribute(patched, "color") == null)
                    patched = SetOrInsertXmlAttribute(patched, "color", "#FF4FFF");
                next = next[..open.Index] + patched + next[(open.Index + open.Length)..];
            }

            return next;
        }

        private static int FindMatchingDomLayerClose(string xml, int bodyStart)
        {
            var depth = 1;
            var i = bodyStart;
            while (i < xml.Length && depth > 0)
            {
                var open = xml.IndexOf("<DOMLayer", i, StringComparison.OrdinalIgnoreCase);
                var close = xml.IndexOf("</DOMLayer>", i, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                    return -1;
                if (open >= 0 && open < close)
                {
                    depth++;
                    i = open + 9;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                        return close;
                    i = close + 11;
                }
            }

            return -1;
        }

        private static string StripNestedDomLayers(string body)
        {
            var next = body;
            while (true)
            {
                var open = next.IndexOf("<DOMLayer", StringComparison.OrdinalIgnoreCase);
                if (open < 0)
                    return next;
                var afterOpen = next.IndexOf('>', open);
                if (afterOpen < 0)
                    return next;
                var close = FindMatchingDomLayerClose(next, afterOpen + 1);
                if (close < 0)
                    return next;
                next = next[..open] + next[(close + "</DOMLayer>".Length)..];
            }
        }

        public static int MarkFlaBoxLayersAsOutline(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var patches = new List<(string Entry, string Xml)>();
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string xml;
                    using (var reader = new StreamReader(entry.Open()))
                        xml = reader.ReadToEnd();
                    if (xml.IndexOf("DOMLayer", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var next = MarkFlaBoxLayersAsOutlineInXml(xml);
                    if (!string.Equals(next, xml, StringComparison.Ordinal))
                        patches.Add((entry.FullName, next));
                }
            }

            foreach (var (entry, xml) in patches)
                ReplaceZipEntry(flaPath, entry, xml);
            return patches.Count;
        }

        /// <summary>Turns off bitmap smoothing on every <c>DOMBitmapItem</c> in the FLA.</summary>
        public static int DisableFlaBitmapSmoothing(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return 0;
            var patches = new List<(string Entry, string Xml)>();
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string xml;
                    using (var reader = new StreamReader(entry.Open()))
                        xml = reader.ReadToEnd();
                    if (xml.IndexOf("DOMBitmapItem", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var next = DisableBitmapSmoothingInXml(xml);
                    if (!string.Equals(next, xml, StringComparison.Ordinal))
                        patches.Add((entry.FullName, next));
                }
            }

            foreach (var (entry, xml) in patches)
                ReplaceZipEntry(flaPath, entry, xml);
            return patches.Count;
        }

        /// <summary>
        /// JPEXS copies the SWF document timeline onto Scene 1 (often empty on frame 0,
        /// with a second host instance on frame 1). Collapse it to one empty frame.
        /// </summary>
        public static bool StripFlaSceneTimeline(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return false;
            return TryPatchFlaZipEntry(flaPath, "DOMDocument.xml", StripDocumentTimelinesToEmptyFrame);
        }

        public static string StripDocumentTimelinesToEmptyFrame(string domXml)
        {
            if (string.IsNullOrWhiteSpace(domXml) ||
                domXml.IndexOf("<DOMTimeline", StringComparison.OrdinalIgnoreCase) < 0)
                return domXml;

            const string emptyLayers = """
                <layers>
                    <DOMLayer name="Layer 1" color="#4FFF4F" current="true" isSelected="true">
                        <frames>
                            <DOMFrame index="0" keyMode="9728">
                                <elements/>
                            </DOMFrame>
                        </frames>
                    </DOMLayer>
                </layers>
            """;

            return Regex.Replace(
                domXml,
                @"<DOMTimeline\b(?<a>[^>]*)>(?<body>.*?)</DOMTimeline>",
                m => "<DOMTimeline" + m.Groups["a"].Value + ">" + emptyLayers + "</DOMTimeline>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }

        public static string DisableBitmapSmoothingInXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return xml;
            var next = Regex.Replace(
                xml,
                @"\ballowSmoothing=""true""",
                "allowSmoothing=\"false\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            next = Regex.Replace(
                next,
                @"<DOMBitmapItem\b(?<a>[^>]*)>",
                m =>
                {
                    var attrs = m.Groups["a"].Value;
                    if (Regex.IsMatch(attrs, @"\ballowSmoothing=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        return m.Value;
                    var slash = attrs.EndsWith("/", StringComparison.Ordinal) ? "/" : "";
                    var core = slash.Length > 0 ? attrs[..^1].TrimEnd() : attrs;
                    return "<DOMBitmapItem" + core + " allowSmoothing=\"false\"" + slash + ">";
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return next;
        }

        /// <summary>
        /// Sets <c>autoDeclareStageInsts="false"</c> on the XFL <c>DOMDocument</c> root.
        /// </summary>
        public static string ApplyAutoDeclareStageInstancesOff(string domDocumentXml)
        {
            if (string.IsNullOrWhiteSpace(domDocumentXml))
                return domDocumentXml;

            if (Regex.IsMatch(
                    domDocumentXml,
                    @"\bautoDeclareStageInsts\s*=\s*""false""",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return domDocumentXml;

            if (Regex.IsMatch(
                    domDocumentXml,
                    @"\bautoDeclareStageInsts\s*=\s*""[^""]*""",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    domDocumentXml,
                    @"\bautoDeclareStageInsts\s*=\s*""[^""]*""",
                    "autoDeclareStageInsts=\"false\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return Regex.Replace(
                domDocumentXml,
                @"<DOMDocument\b",
                "<DOMDocument autoDeclareStageInsts=\"false\"",
                RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Publish Settings: auto-declare off (bit 4096). Warnings mode (AS3Coach / bit 4) stays enabled.
        /// </summary>
        public static string ApplyAnimateCompilerSettings(string publishXml)
        {
            if (string.IsNullOrWhiteSpace(publishXml))
                return publishXml;

            var next = Regex.Replace(
                publishXml,
                @"<AS3AutoDeclare>\s*\d+\s*</AS3AutoDeclare>",
                "<AS3AutoDeclare>0</AS3AutoDeclare>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Restore warnings mode if a prior export hid it.
            next = Regex.Replace(
                next,
                @"<AS3Coach>\s*0\s*</AS3Coach>",
                "<AS3Coach>4</AS3Coach>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!Regex.IsMatch(next, @"<AS3AutoDeclare>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                next = Regex.Replace(
                    next,
                    @"(<AS3Coach>\s*\d+\s*</AS3Coach>)",
                    "$1\n      <AS3AutoDeclare>0</AS3AutoDeclare>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            next = Regex.Replace(
                next,
                @"<AS3Flags>\s*(\d+)\s*</AS3Flags>",
                m =>
                {
                    var flags = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    // Clear auto-declare (4096); ensure warnings bit (4) is set with strict (2).
                    flags = (flags & ~4096) | 4 | 2;
                    return "<AS3Flags>" + flags + "</AS3Flags>";
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            next = ApplyPublishSoundQuality(next);
            return next;
        }

        /// <summary>
        /// Matches Animate's Publish Settings UI: Override sound settings + Raw 44 kHz.
        /// Animate stores numeric <c>StreamFormat</c>/<c>StreamCompress</c>/<c>OverrideSounds</c>,
        /// not UI strings. Default 0/7 is MP3 16 kbps Mono; 7/0 is Raw 44 kHz.
        /// </summary>
        public static string ApplyPublishSoundQuality(string publishXml)
        {
            if (string.IsNullOrWhiteSpace(publishXml))
                return publishXml;

            var next = SetXmlElementValue(publishXml, "StreamFormat", "7");
            next = SetXmlElementValue(next, "StreamCompress", "0");
            next = SetXmlElementValue(next, "EventFormat", "7");
            next = SetXmlElementValue(next, "EventCompress", "0");
            next = SetXmlElementValue(next, "OverrideSounds", "1");
            next = Regex.Replace(
                next,
                @"<OverrideSoundSettings>[\s\S]*?</OverrideSoundSettings>\s*",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return next;
        }

        private static string SetXmlElementValue(string xml, string tagName, string value)
        {
            var pattern = @"<(?<open>" + tagName + @")>(?<inner>[\s\S]*?)</" + tagName + @">";
            if (Regex.IsMatch(xml, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    pattern,
                    m =>
                    {
                        var open = m.Groups["open"].Value;
                        var inner = m.Groups["inner"].Value;
                        var suffix = inner.IndexOf('\n', StringComparison.Ordinal) >= 0 ? "\n" : "";
                        return "<" + open + ">" + value + suffix + "</" + open + ">";
                    },
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            var inner = "<" + tagName + ">" + value + "</" + tagName + ">\n";
            if (Regex.IsMatch(xml, @"</PublishFlashProperties>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    @"</PublishFlashProperties>",
                    inner + "</PublishFlashProperties>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (Regex.IsMatch(xml, @"</flash_profile>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    @"</flash_profile>",
                    inner + "</flash_profile>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return xml + inner;
        }

        private static string UpsertXmlElement(string xml, string tagName, string value)
        {
            var pattern = @"<" + tagName + @">[\s\S]*?</" + tagName + @">";
            if (Regex.IsMatch(xml, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    pattern,
                    "<" + tagName + ">" + value + "</" + tagName + ">",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            var inner = "<" + tagName + ">" + value + "</" + tagName + ">\n";
            if (Regex.IsMatch(xml, @"</PublishFlashProperties>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    @"</PublishFlashProperties>",
                    inner + "</PublishFlashProperties>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (Regex.IsMatch(xml, @"</flash_profile>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(
                    xml,
                    @"</flash_profile>",
                    inner + "</flash_profile>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return xml + inner;
        }

        private static bool RunFfmpeg(ProcessStartInfo psi)
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                return false;
            proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(120_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }
            return proc.ExitCode == 0;
        }

        /// <summary>Obsolete name � use <see cref="ApplyAnimateCompilerSettings"/>.</summary>
        public static string ApplyAs3AutoDeclareOff(string publishXml) =>
            ApplyAnimateCompilerSettings(publishXml);

        private static bool TryPatchFlaZipEntry(string flaPath, string fileName, Func<string, string> transform)
        {
            string? entryName = null;
            string? xml = null;
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.FullName, fileName, StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return false;
                entryName = entry.FullName;
                using var reader = new StreamReader(entry.Open());
                xml = reader.ReadToEnd();
            }

            var next = transform(xml);
            if (string.Equals(next, xml, StringComparison.Ordinal))
                return false;

            ReplaceZipEntry(flaPath, entryName!, next);
            return true;
        }

        /// <summary>
        /// Restores an editable Actions layer on the main character symbol.
        /// FFDec's script-layer-off export embeds frame logic as bytecode (empty Actions panel);
        /// authored FLAs use <c>this.xframe = "label"; stop();</c> from frame names instead.
        /// Does not copy decompiled Script Layer closures (those caused Grab_106 Error 1120).
        /// </summary>
        public static bool InjectXframeActionsOnStanceHostFla(string flaPath)
        {
            var host = FindStanceHostLibraryXml(flaPath);
            if (host == null)
                return false;

            var next = ApplyXframeActionsLayer(host.Value.Xml);
            if (string.Equals(next, host.Value.Xml, StringComparison.Ordinal))
                return false;

            ReplaceZipEntry(flaPath, host.Value.EntryName, next);
            return true;
        }

        /// <summary>
        /// Inserts a locked Actions layer with <c>this.xframe</c> / <c>stop()</c> for each named
        /// frame label. No-op when the symbol already has timeline Actionscript.
        /// </summary>
        public static string ApplyXframeActionsLayer(string libraryXml)
        {
            if (string.IsNullOrWhiteSpace(libraryXml) ||
                libraryXml.Contains("<Actionscript", StringComparison.OrdinalIgnoreCase))
                return libraryXml;

            XDocument doc;
            try { doc = XDocument.Parse(libraryXml, LoadOptions.PreserveWhitespace); }
            catch { return libraryXml; }

            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var layersEl = doc.Descendants(ns + "layers").FirstOrDefault();
            if (layersEl == null)
                return libraryXml;

            var labels = CollectNamedFrameLabels(layersEl, ns);
            if (labels.Count == 0)
                return libraryXml;

            var actions = new XElement(ns + "DOMLayer",
                new XAttribute("name", "Actions"),
                new XAttribute("color", "#4F80FF"),
                new XAttribute("locked", "true"),
                new XAttribute("autoNamed", "false"),
                new XElement(ns + "frames",
                    labels.Select(l => new XElement(ns + "DOMFrame",
                        new XAttribute("index", l.Index.ToString()),
                        new XAttribute("keyMode", "9728"),
                        new XElement(ns + "Actionscript",
                            new XElement(ns + "script",
                                new XCData($"this.xframe = \"{EscapeAs3String(l.Name)}\";\nstop();"))),
                        new XElement(ns + "elements")))));

            var labelLayer = layersEl.Elements(ns + "DOMLayer").FirstOrDefault(el =>
            {
                var n = (string?)el.Attribute("name") ?? "";
                return n.Contains("Label", StringComparison.OrdinalIgnoreCase);
            });
            if (labelLayer != null)
                labelLayer.AddAfterSelf(actions);
            else
                layersEl.AddFirst(actions);

            return doc.Declaration == null
                ? doc.ToString(SaveOptions.DisableFormatting)
                : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
        }

        private static List<(int Index, string Name)> CollectNamedFrameLabels(XElement layersEl, XNamespace ns)
        {
            var labelLayer = layersEl.Elements(ns + "DOMLayer").FirstOrDefault(el =>
            {
                var n = (string?)el.Attribute("name") ?? "";
                return n.Contains("Label", StringComparison.OrdinalIgnoreCase);
            });
            var framesParent = labelLayer?.Element(ns + "frames") ?? layersEl;
            var found = new List<(int Index, string Name)>();
            foreach (var frame in framesParent.Descendants(ns + "DOMFrame"))
            {
                var labelType = (string?)frame.Attribute("labelType");
                var name = (string?)frame.Attribute("name");
                var indexRaw = (string?)frame.Attribute("index");
                if (!string.Equals(labelType, "name", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(name) ||
                    !int.TryParse(indexRaw, out var index))
                    continue;
                found.Add((index, name));
            }
            found.Sort((a, b) => a.Index.CompareTo(b.Index));
            return found;
        }

        private static string EscapeAs3String(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        private static (string EntryName, string Xml, string ClassName)? FindStanceHostLibraryXml(string flaPath)
        {
            if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
                return null;
            return FindStanceHostLibraryXml(ReadFlaZipEntries(flaPath));
        }

        private static (string EntryName, string Xml, string ClassName)? FindStanceHostLibraryXml(
            Dictionary<string, byte[]> entries)
        {
            string? bestEntry = null;
            string? bestXml = null;
            string? bestClass = null;
            var bestCount = 0;
            foreach (var kv in entries)
            {
                if (!kv.Key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                    !kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var xml = Encoding.UTF8.GetString(kv.Value);
                var stanceCount = StanceInstanceRegex.Matches(xml).Count;
                if (stanceCount <= bestCount)
                    continue;

                var classMatch = LinkageClassNameRegex.Match(xml);
                if (!classMatch.Success)
                    continue;

                bestCount = stanceCount;
                bestEntry = kv.Key;
                bestXml = xml;
                bestClass = classMatch.Groups[1].Value;
            }

            return bestCount > 0 && bestEntry != null && bestXml != null && bestClass != null
                ? (bestEntry, bestXml, bestClass)
                : null;
        }

        private static void ReplaceZipEntry(string zipPath, string entryName, string contents)
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
            var existing = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
            existing?.Delete();
            var created = zip.CreateEntry(entryName, CompressionLevel.Fastest);
            using var writer = new StreamWriter(created.Open());
            writer.Write(contents);
        }

        /// <summary>
        /// Export only the given SWF character ids (FFDec <c>-selectid</c>), e.g. a few sprites
        /// instead of dumping an entire misc2.swf.
        /// </summary>
        public Task ExportSelectedAsync(
            string ffdecPath,
            string swfPath,
            string exportDir,
            IEnumerable<int> characterIds,
            string itemTypes = "sprite",
            CancellationToken cancellationToken = default,
            string? config = null)
        {
            var ids = characterIds
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            if (ids.Count == 0)
                throw new ArgumentException("At least one character id is required.", nameof(characterIds));

            var selectArg = string.Join(",", ids);
            var configPrefix = string.IsNullOrWhiteSpace(config)
                ? ""
                : $"{FormatConfigArgument(config)} ";
            return RunFfdecAsync(
                ffdecPath,
                $"{configPrefix}-selectid {selectArg} -export {itemTypes} \"{exportDir}\" \"{swfPath}\"",
                cancellationToken);
        }

        public Task ReplaceAsync(
            string ffdecPath,
            string infile,
            string outfile,
            string characterIdOrScriptName,
            string importDataFile,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-replace \"{infile}\" \"{outfile}\" \"{characterIdOrScriptName}\" \"{importDataFile}\"",
                cancellationToken);

        /// <summary>
        /// Replaces multiple character images/scripts in one FFDec invocation when possible:
        /// <c>-replace infile outfile id1 file1 id2 file2 �</c>
        /// Falls back to chained single replaces if the bulk form fails.
        /// </summary>
        public async Task ReplaceManyAsync(
            string ffdecPath,
            string infile,
            string outfile,
            IReadOnlyList<(string CharacterIdOrScriptName, string ImportDataFile)> replacements,
            CancellationToken cancellationToken = default)
        {
            if (replacements == null || replacements.Count == 0)
                throw new ArgumentException("At least one replacement is required.", nameof(replacements));

            if (replacements.Count == 1)
            {
                await ReplaceAsync(
                    ffdecPath, infile, outfile,
                    replacements[0].CharacterIdOrScriptName, replacements[0].ImportDataFile,
                    cancellationToken);
                return;
            }

            // Prefer argsfile form � one JVM, many replacements (much faster than N -replace calls).
            var argsFile = Path.Combine(
                Path.GetDirectoryName(outfile) ?? Path.GetTempPath(),
                "ssf2mm-replace-args-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                var lines = replacements.SelectMany(r => new[]
                {
                    r.CharacterIdOrScriptName,
                    r.ImportDataFile
                });
                await File.WriteAllLinesAsync(argsFile, lines, cancellationToken);
                await RunFfdecAsync(
                    ffdecPath,
                    $"-replace \"{infile}\" \"{outfile}\" \"{argsFile}\"",
                    cancellationToken);
                if (File.Exists(outfile))
                    return;
            }
            catch
            {
                // Fall through to inline multi-arg, then chained replaces.
            }
            finally
            {
                try { if (File.Exists(argsFile)) File.Delete(argsFile); } catch { /* ignore */ }
            }

            var pairs = string.Join(" ", replacements.Select(r =>
                $"\"{r.CharacterIdOrScriptName}\" \"{r.ImportDataFile}\""));
            try
            {
                await RunFfdecAsync(
                    ffdecPath,
                    $"-replace \"{infile}\" \"{outfile}\" {pairs}",
                    cancellationToken);
                if (File.Exists(outfile))
                    return;
            }
            catch
            {
                // Fall through to chained replaces.
            }

            var workDir = Path.GetDirectoryName(outfile) ?? Path.GetTempPath();
            Directory.CreateDirectory(workDir);
            var currentIn = infile;
            string? lastTemp = null;

            for (var i = 0; i < replacements.Count; i++)
            {
                var (name, dataFile) = replacements[i];
                var isLast = i == replacements.Count - 1;
                var stepOut = isLast
                    ? outfile
                    : Path.Combine(workDir, $"ssf2mm-replace-{i}-{Guid.NewGuid():N}.swf");

                await ReplaceAsync(ffdecPath, currentIn, stepOut, name, dataFile, cancellationToken);

                if (lastTemp != null && File.Exists(lastTemp))
                {
                    try { File.Delete(lastTemp); } catch { /* ignore */ }
                }

                if (!isLast)
                {
                    lastTemp = stepOut;
                    currentIn = stepOut;
                }
            }
        }

        public Task ImportSymbolClassAsync(
            string ffdecPath,
            string infile,
            string outfile,
            string symbolClassFile,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-importSymbolClass \"{infile}\" \"{outfile}\" \"{symbolClassFile}\"",
                cancellationToken);

        public Task ImportScriptAsync(
            string ffdecPath,
            string infile,
            string outfile,
            string scriptsFolder,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-importScript \"{infile}\" \"{outfile}\" \"{scriptsFolder}\"",
                cancellationToken);

        /// <summary>
        /// Adds or repairs an AS3 class via the bundled FFDec-library helper
        /// (<c>ffdec/tools/AddSelectClass</c>). Dual-package <c>importScript</c> cannot create
        /// new ScriptPacks � this creates a class shell then compiles the AS source into it.
        /// </summary>
        public async Task AddAs3ClassAsync(
            string ffdecPath,
            string infile,
            string outfile,
            string donorClassName,
            string newClassName,
            string newIdLiteral,
            CancellationToken cancellationToken = default,
            string idProp = "characterID")
        {
            if (string.IsNullOrWhiteSpace(donorClassName))
                throw new ArgumentException("Donor class is required.", nameof(donorClassName));
            if (string.IsNullOrWhiteSpace(newClassName))
                throw new ArgumentException("New class is required.", nameof(newClassName));
            if (string.IsNullOrWhiteSpace(newIdLiteral))
                throw new ArgumentException("New id is required.", nameof(newIdLiteral));
            if (string.IsNullOrWhiteSpace(idProp)) idProp = "characterID";
            if (!File.Exists(infile))
                throw new FileNotFoundException("SWF not found.", infile);

            var exe = ResolveExecutableOrBundled(ffdecPath)
                ?? throw new InvalidOperationException(
                    "JPEXS FFDec was not found. The bundled copy under 'ffdec\\' is missing.");
            var ffdecDir = Path.GetDirectoryName(exe)
                ?? throw new InvalidOperationException("Could not resolve FFDec directory.");
            var toolsDir = Path.Combine(ffdecDir, "tools");
            var classFile = Path.Combine(toolsDir, "AddSelectClass.class");
            if (!File.Exists(classFile))
            {
                // Dev/fallback: tools under runtime\ffdec (or legacy root ffdec).
                foreach (var root in AppPaths.EnumerateSidecarRoots())
                {
                    var candidate = Path.Combine(root, "ffdec", "tools");
                    if (File.Exists(Path.Combine(candidate, "AddSelectClass.class")))
                    {
                        toolsDir = candidate;
                        classFile = Path.Combine(toolsDir, "AddSelectClass.class");
                        break;
                    }
                }
            }
            if (!File.Exists(classFile))
                throw new FileNotFoundException(
                    "AddSelectClass.class missing. Expected under ffdec/tools/.", classFile);

            var libDir = Path.Combine(ffdecDir, "lib");
            if (!Directory.Exists(libDir))
            {
                foreach (var root in AppPaths.EnumerateSidecarRoots())
                {
                    var candidate = Path.Combine(root, "ffdec", "lib");
                    if (Directory.Exists(candidate))
                    {
                        libDir = candidate;
                        break;
                    }
                }
            }
            if (!Directory.Exists(libDir))
                throw new DirectoryNotFoundException("FFDec lib folder not found: " + libDir);

            var jars = Directory.GetFiles(libDir, "*.jar");
            if (jars.Length == 0)
                throw new InvalidOperationException("No FFDec library jars in " + libDir);

            var java = ResolveJavaExecutable()
                ?? throw new InvalidOperationException(
                    "Java was not found. Install a JRE or use the bundled jre\\ folder.");

            var classpath = toolsDir + Path.PathSeparator + string.Join(Path.PathSeparator, jars);
            Directory.CreateDirectory(Path.GetDirectoryName(outfile)!);

            var psi = new ProcessStartInfo
            {
                FileName = java,
                Arguments =
                    $"-cp \"{classpath}\" AddSelectClass \"{infile}\" \"{outfile}\" " +
                    $"\"{donorClassName}\" \"{newClassName}\" \"{newIdLiteral}\" \"{idProp}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = toolsDir
            };

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start Java for AddSelectClass.");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var timeout = TimeSpan.FromMinutes(3);
            using var reg = cancellationToken.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ } });
            var finished = await Task.Run(() => proc.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
            if (!finished)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw new TimeoutException("AddSelectClass timed out.");
            }

            if (proc.ExitCode != 0 || !File.Exists(outfile))
            {
                var detail = (stdout.ToString() + "\n" + stderr.ToString()).Trim();
                throw new InvalidOperationException(
                    "AddSelectClass failed (exit " + proc.ExitCode + ")." +
                    (detail.Length > 0 ? "\n" + detail : ""));
            }
        }

        /// <summary>
        /// Batch AddSelectClass � one JVM / one SWF load for many new classes.
        /// Each job: (donorClass, newClass, idLiteral, idProp).
        /// </summary>
        public async Task AddAs3ClassesBatchAsync(
            string ffdecPath,
            string infile,
            string outfile,
            IReadOnlyList<(string DonorClass, string NewClass, string IdLiteral, string IdProp)> jobs,
            CancellationToken cancellationToken = default)
        {
            if (jobs == null || jobs.Count == 0)
                throw new ArgumentException("At least one AddSelectClass job is required.", nameof(jobs));
            if (!File.Exists(infile))
                throw new FileNotFoundException("SWF not found.", infile);

            var exe = ResolveExecutableOrBundled(ffdecPath)
                ?? throw new InvalidOperationException(
                    "JPEXS FFDec was not found. The bundled copy under 'ffdec\\' is missing.");
            var ffdecDir = Path.GetDirectoryName(exe)
                ?? throw new InvalidOperationException("Could not resolve FFDec directory.");
            var toolsDir = Path.Combine(ffdecDir, "tools");
            var classFile = Path.Combine(toolsDir, "AddSelectClass.class");
            if (!File.Exists(classFile))
            {
                foreach (var root in AppPaths.EnumerateSidecarRoots())
                {
                    var candidate = Path.Combine(root, "ffdec", "tools");
                    if (File.Exists(Path.Combine(candidate, "AddSelectClass.class")))
                    {
                        toolsDir = candidate;
                        classFile = Path.Combine(toolsDir, "AddSelectClass.class");
                        break;
                    }
                }
            }
            if (!File.Exists(classFile))
                throw new FileNotFoundException(
                    "AddSelectClass.class missing. Expected under ffdec/tools/.", classFile);

            var libDir = Path.Combine(ffdecDir, "lib");
            if (!Directory.Exists(libDir))
            {
                foreach (var root in AppPaths.EnumerateSidecarRoots())
                {
                    var candidate = Path.Combine(root, "ffdec", "lib");
                    if (Directory.Exists(candidate))
                    {
                        libDir = candidate;
                        break;
                    }
                }
            }
            if (!Directory.Exists(libDir))
                throw new DirectoryNotFoundException("FFDec lib folder not found: " + libDir);

            var jars = Directory.GetFiles(libDir, "*.jar");
            if (jars.Length == 0)
                throw new InvalidOperationException("No FFDec library jars in " + libDir);

            var java = ResolveJavaExecutable()
                ?? throw new InvalidOperationException(
                    "Java was not found. Install a JRE or use the bundled jre\\ folder.");

            var jobsFile = Path.Combine(Path.GetTempPath(), "ssf2mm-addclass-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                var lines = new List<string>(jobs.Count);
                foreach (var (donor, neu, id, prop) in jobs)
                {
                    if (string.IsNullOrWhiteSpace(donor) || string.IsNullOrWhiteSpace(neu)
                        || string.IsNullOrWhiteSpace(id))
                        throw new ArgumentException("Donor, new class, and id are required for each job.");
                    var idProp = string.IsNullOrWhiteSpace(prop) ? "characterID" : prop.Trim();
                    lines.Add(donor.Trim() + "\t" + neu.Trim() + "\t" + id.Trim() + "\t" + idProp);
                }
                await File.WriteAllLinesAsync(jobsFile, lines, cancellationToken);

                var classpath = toolsDir + Path.PathSeparator + string.Join(Path.PathSeparator, jars);
                Directory.CreateDirectory(Path.GetDirectoryName(outfile)!);

                var psi = new ProcessStartInfo
                {
                    FileName = java,
                    Arguments =
                        $"-cp \"{classpath}\" AddSelectClass \"{infile}\" \"{outfile}\" " +
                        $"--batch \"{jobsFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = toolsDir
                };

                using var proc = new Process { StartInfo = psi };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start Java for AddSelectClass batch.");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Large Crazy CSS grids compile hundreds of classes in one process.
                var timeout = TimeSpan.FromMinutes(Math.Max(10, 2 + jobs.Count / 20.0));
                using var reg = cancellationToken.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ } });
                var finished = await Task.Run(() => proc.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
                if (!finished)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    throw new TimeoutException("AddSelectClass batch timed out after " + timeout + ".");
                }

                if (proc.ExitCode != 0 || !File.Exists(outfile))
                {
                    var detail = (stdout.ToString() + "\n" + stderr.ToString()).Trim();
                    throw new InvalidOperationException(
                        "AddSelectClass batch failed (exit " + proc.ExitCode + ")." +
                        (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
            finally
            {
                try { File.Delete(jobsFile); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Bulk-import DefineText / DefineEditText from a folder that contains a <c>texts/</c>
        /// subdirectory of <c>{characterId}.txt</c> files (FFDec export layout).
        /// </summary>
        public Task ImportTextAsync(
            string ffdecPath,
            string infile,
            string outfile,
            string textsFolder,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-importText \"{infile}\" \"{outfile}\" \"{textsFolder}\"",
                cancellationToken);

        public Task SwfToXmlAsync(
            string ffdecPath,
            string swfPath,
            string xmlPath,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-swf2xml \"{swfPath}\" \"{xmlPath}\"",
                cancellationToken,
                timeout: TimeSpan.FromMinutes(30));

        public Task XmlToSwfAsync(
            string ffdecPath,
            string xmlPath,
            string swfPath,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-xml2swf \"{xmlPath}\" \"{swfPath}\"",
                cancellationToken,
                timeout: TimeSpan.FromMinutes(30));

        /// <summary>
        /// Dumps ABC string pool + SymbolClass names as a tiny plan XML (no shape records).
        /// Requires the bundled ffdec-worker.
        /// </summary>
        public Task IdentDumpAsync(
            string ffdecPath,
            string swfPath,
            string xmlPath,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-identdump \"{swfPath}\" \"{xmlPath}\"",
                cancellationToken,
                timeout: TimeSpan.FromSeconds(25));

        /// <summary>
        /// Retypes host instance slots typed as <c>{id}_fla.*</c> (e.g. <c>stance:Idle_3</c>)
        /// to <c>flash.display.MovieClip</c> so CSS intro taunt does not throw Error #1034.
        /// Requires the bundled ffdec-worker.
        /// </summary>
        public Task UntypeFlaInstanceSlotsAsync(
            string ffdecPath,
            string swfPath,
            string outSwfPath,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-untypestance \"{swfPath}\" \"{outSwfPath}\"",
                cancellationToken,
                timeout: TimeSpan.FromSeconds(25));

        /// <summary>
        /// Applies identifier rewrite pairs in memory and writes a new SWF (no XML round-trip).
        /// </summary>
        public Task IdentRewriteAsync(
            string ffdecPath,
            string swfPath,
            string outSwfPath,
            string pairsPath,
            CancellationToken cancellationToken = default) =>
            RunFfdecAsync(
                ffdecPath,
                $"-identrewrite \"{swfPath}\" \"{outSwfPath}\" \"{pairsPath}\"",
                cancellationToken,
                timeout: TimeSpan.FromSeconds(25));

        public async Task RunFfdecAsync(
            string ffdecPath,
            string arguments,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            await Next2FlashSetup.EnsureAsync(cancellationToken);
            var exe = ResolveExecutableOrBundled(ffdecPath)
                ?? throw new InvalidOperationException(
                    "JPEXS FFDec was not found. Install Next2Flash so toolchain/ffdec/ffdec.jar exists, " +
                    "or set a JPEXS path in settings.");

            var ffdecDir = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;
            var workerJar = Path.Combine(ffdecDir, "ffdec-worker.jar");
            var ffdecJar = Path.Combine(ffdecDir, "ffdec.jar");
            if (!File.Exists(ffdecJar) && exe.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                ffdecJar = exe;

            // Prefer warm worker when available.
            if (File.Exists(workerJar) && File.Exists(ffdecJar))
            {
                try
                {
                    await RunViaWorkerAsync(ffdecDir, ffdecJar, workerJar, arguments, cancellationToken, timeout);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException)
                {
                    DebugLogger.Log($"[JPEXS worker] fallback to one-shot: {ex.Message}");
                    KillWorker();
                }
            }

            await RunOneShotAsync(exe, arguments, cancellationToken, timeout);
        }

        private async Task RunViaWorkerAsync(
            string ffdecDir,
            string ffdecJar,
            string workerJar,
            string arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout)
        {
            var slot = await AcquireWorkerSlotAsync(cancellationToken);
            try
            {
                await EnsureSlotReadyAsync(slot, ffdecDir, ffdecJar, workerJar, cancellationToken);
                var shortArgs = arguments.Length > 160 ? arguments[..160] + "�" : arguments;
                DebugLogger.Log($"[JPEXS worker#{slot.Id}] {arguments}");
                var sw = Stopwatch.StartNew();

                var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
                var workerIn = slot.In ?? throw new InvalidOperationException("FFDec worker stdin missing.");
                var workerOut = slot.Out ?? throw new InvalidOperationException("FFDec worker stdout missing.");
                var workerProc = slot.Process ?? throw new InvalidOperationException("FFDec worker missing.");

                await workerIn.WriteLineAsync(arguments);
                await workerIn.FlushAsync();

                // Only one ReadLineAsync may be outstanding on a StreamReader � never start
                // a second read while polling for timeout (that caused "stream in use" ? fallback).
                var readTask = workerOut.ReadLineAsync();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline)
                    {
                        KillSlot(slot);
                        throw new TimeoutException("FFDec worker timed out.");
                    }
                    if (workerProc.HasExited)
                        throw new InvalidOperationException("FFDec worker exited unexpectedly.");

                    var delayMs = Math.Max(50, (int)Math.Min(500, (deadline - DateTime.UtcNow).TotalMilliseconds));
                    var completed = await Task.WhenAny(readTask, Task.Delay(delayMs, cancellationToken));
                    if (completed != readTask)
                        continue;

                    var line = await readTask;
                    if (line == null)
                        throw new InvalidOperationException("FFDec worker closed stdout.");
                    if (line.Equals("OK", StringComparison.Ordinal))
                    {
                        sw.Stop();
                        var okMsg = $"[JPEXS worker#{slot.Id} OK {sw.Elapsed.TotalSeconds:F1}s] {shortArgs}";
                        DebugLogger.Log(okMsg);
                        Console.WriteLine("  " + okMsg);
                        return;
                    }
                    if (line.StartsWith("ERR ", StringComparison.Ordinal))
                        throw new InvalidOperationException("FFDec worker: " + line[4..]);
                    // Ignore unexpected chatter (READY already consumed at start).
                    DebugLogger.Log($"[JPEXS worker#{slot.Id}] {line}");
                    readTask = workerOut.ReadLineAsync();
                }
            }
            catch (OperationCanceledException)
            {
                KillSlot(slot);
                throw;
            }
            finally
            {
                slot.Busy.Release();
            }
        }

        private static async Task<WorkerSlot> AcquireWorkerSlotAsync(CancellationToken cancellationToken)
        {
            // Round-robin short waits � avoids Monitor-across-await and WhenAny slot leaks.
            var i = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = Workers[i % WorkerCount];
                i++;
                if (await slot.Busy.WaitAsync(TimeSpan.FromMilliseconds(40), cancellationToken))
                    return slot;
            }
        }

        private static async Task EnsureSlotReadyAsync(
            WorkerSlot slot,
            string ffdecDir,
            string ffdecJar,
            string workerJar,
            CancellationToken cancellationToken)
        {
            lock (WorkerPoolLock)
            {
                if (!string.Equals(_poolFfdecDir, ffdecDir, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var s in Workers)
                        KillSlot(s);
                    _poolFfdecDir = ffdecDir;
                }
            }

            if (slot.Process != null && !slot.Process.HasExited && slot.In != null && slot.Out != null)
                return;

            await StartSlotAsync(slot, ffdecDir, ffdecJar, workerJar, cancellationToken);
        }

        private static async Task StartSlotAsync(
            WorkerSlot slot,
            string ffdecDir,
            string ffdecJar,
            string workerJar,
            CancellationToken cancellationToken)
        {
            KillSlot(slot);

            var psi = Next2FlashSetup.CreateRaw("ffdec -- --worker", redirectInput: true);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("Failed to start FFDec worker.");

            slot.Process = process;
            slot.In = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            slot.Out = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);

            var slotId = slot.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var err = await process.StandardError.ReadLineAsync();
                        if (err == null) break;
                        if (!string.IsNullOrWhiteSpace(err))
                            DebugLogger.Log($"[JPEXS worker#{slotId} stderr] {err}");
                    }
                }
                catch { /* ignore */ }
            }, CancellationToken.None);

            var ready = await slot.Out.ReadLineAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(ready, "READY", StringComparison.Ordinal))
            {
                KillSlot(slot);
                throw new InvalidOperationException(
                    "FFDec worker did not send READY (got: " + (ready ?? "<null>") + ").");
            }
            DebugLogger.Log($"[JPEXS worker#{slot.Id}] READY");
        }

        private static void KillSlot(WorkerSlot slot)
        {
            try { slot.In?.Dispose(); } catch { /* ignore */ }
            try { slot.Out?.Dispose(); } catch { /* ignore */ }
            slot.In = null;
            slot.Out = null;
            if (slot.Process != null)
            {
                try
                {
                    if (!slot.Process.HasExited)
                        slot.Process.Kill(entireProcessTree: true);
                }
                catch { /* ignore */ }
                try { slot.Process.Dispose(); } catch { /* ignore */ }
                slot.Process = null;
            }
        }

        public static void ShutdownWorkers() => KillWorker();

        private static void KillWorker()
        {
            lock (WorkerPoolLock)
            {
                foreach (var slot in Workers)
                    KillSlot(slot);
                _poolFfdecDir = null;
            }
        }

        private async Task RunOneShotAsync(
            string exe,
            string arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout)
        {
            _ = exe;
            SplitSsf2mmJvmProps(arguments, out _, out var ffdecArgs);
            var psi = Next2FlashSetup.CreateRaw("ffdec -- " + ffdecArgs, redirectInput: false);
            DebugLogger.Log($"[JPEXS] n2f {psi.Arguments}");

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) stderr.AppendLine(e.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start FFDec process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("FFDec timed out.");
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            var output = stdout.ToString();
            var errors = stderr.ToString();
            if (!string.IsNullOrWhiteSpace(output))
                DebugLogger.Log($"[JPEXS stdout] {output.Trim()}");
            if (!string.IsNullOrWhiteSpace(errors))
                DebugLogger.Log($"[JPEXS stderr] {errors.Trim()}");

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFDec exited with code {process.ExitCode}.{(string.IsNullOrWhiteSpace(errors) ? "" : "\n" + errors.Trim())}");
            }
        }

        private static void SplitSsf2mmJvmProps(string arguments, out string jvmProps, out string ffdecArgs)
        {
            jvmProps = "";
            ffdecArgs = arguments ?? "";
            if (string.IsNullOrWhiteSpace(arguments))
                return;
            var props = new List<string>();
            var rest = new List<string>();
            foreach (var token in SplitUnquotedArgs(arguments))
            {
                if (token.StartsWith("-Dssf2mm.", StringComparison.Ordinal))
                    props.Add(token);
                else
                    rest.Add(token);
            }
            jvmProps = string.Join(" ", props);
            ffdecArgs = string.Join(" ", rest.Select(QuoteIfNeeded));
        }

        private static IEnumerable<string> SplitUnquotedArgs(string arguments)
        {
            var cur = new StringBuilder();
            var inQuote = false;
            foreach (var c in arguments)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }
                if (!inQuote && char.IsWhiteSpace(c))
                {
                    if (cur.Length > 0)
                    {
                        yield return cur.ToString();
                        cur.Clear();
                    }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0)
                yield return cur.ToString();
        }

        private static string QuoteIfNeeded(string token) =>
            token.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + token + "\"" : token;

        /// <summary>
        /// Reads live <c>-config</c> names from FFDec's <c>Configuration</c> class,
        /// skipping <c>@ConfigurationRemoved</c> fields (those NPE <c>-config</c>).
        /// </summary>
        private static class FfdecConfigurationClass
        {
            private const string ClassPath = "com/jpexs/decompiler/flash/configuration/Configuration.class";

            public static HashSet<string> ReadLiveKeys(string ffdecLibJarPath)
            {
                using var zip = ZipFile.OpenRead(ffdecLibJarPath);
                var entry = zip.GetEntry(ClassPath)
                    ?? throw new FileNotFoundException("Configuration.class missing in " + ffdecLibJarPath);
                using var src = entry.Open();
                using var ms = new MemoryStream();
                src.CopyTo(ms);
                return Parse(ms.ToArray());
            }

            private static HashSet<string> Parse(byte[] data)
            {
                var r = new Be(data);
                if (unchecked((uint)r.U4()) != 0xCAFEBABE)
                    throw new InvalidDataException("Not a Java class file.");
                r.U2();
                r.U2();
                var cpCount = r.U2();
                var cp = new object?[cpCount];
                for (var i = 1; i < cpCount; i++)
                {
                    var tag = r.U1();
                    switch (tag)
                    {
                        case 1:
                            cp[i] = Encoding.UTF8.GetString(r.Bytes(r.U2()));
                            break;
                        case 3:
                        case 4:
                            r.U4();
                            break;
                        case 5:
                        case 6:
                            r.U4();
                            r.U4();
                            i++;
                            break;
                        case 7:
                        case 8:
                        case 16:
                        case 19:
                        case 20:
                            r.U2();
                            break;
                        case 9:
                        case 10:
                        case 11:
                        case 12:
                        case 17:
                        case 18:
                            r.U2();
                            r.U2();
                            break;
                        case 15:
                            r.U1();
                            r.U2();
                            break;
                        default:
                            throw new InvalidDataException("Unknown constant pool tag " + tag);
                    }
                }

                string Utf8(int idx) => idx > 0 && idx < cp.Length ? cp[idx] as string ?? "" : "";

                r.U2();
                r.U2();
                r.U2();
                r.Skip(r.U2() * 2);
                var fieldCount = r.U2();
                var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var f = 0; f < fieldCount; f++)
                {
                    r.U2();
                    var name = Utf8(r.U2());
                    var desc = Utf8(r.U2());
                    var attrCount = r.U2();
                    var removed = false;
                    string? named = null;
                    for (var a = 0; a < attrCount; a++)
                    {
                        var attrName = Utf8(r.U2());
                        var body = r.Bytes(r.U4());
                        if (attrName != "RuntimeVisibleAnnotations")
                            continue;
                        ParseAnns(body, Utf8, ref removed, ref named);
                    }

                    if (removed)
                        continue;
                    if (desc.IndexOf("ConfigurationItem", StringComparison.Ordinal) < 0)
                        continue;
                    live.Add(named ?? name);
                }

                return live;
            }

            private static void ParseAnns(byte[] body, Func<int, string> utf8, ref bool removed, ref string? named)
            {
                var r = new Be(body);
                var n = r.U2();
                for (var i = 0; i < n; i++)
                    ReadAnn(r, utf8, ref removed, ref named);
            }

            private static void ReadAnn(Be r, Func<int, string> utf8, ref bool removed, ref string? named)
            {
                var type = utf8(r.U2());
                var pairs = r.U2();
                string? value = null;
                for (var i = 0; i < pairs; i++)
                {
                    var pairName = utf8(r.U2());
                    var v = ReadElement(r, utf8);
                    if (pairName == "value" && v is string s)
                        value = s;
                }

                if (type.Contains("ConfigurationRemoved", StringComparison.Ordinal))
                    removed = true;
                if (type.Contains("ConfigurationName", StringComparison.Ordinal) && !string.IsNullOrEmpty(value))
                    named = value;
            }

            private static object? ReadElement(Be r, Func<int, string> utf8)
            {
                var tag = (char)r.U1();
                switch (tag)
                {
                    case 'B':
                    case 'C':
                    case 'D':
                    case 'F':
                    case 'I':
                    case 'J':
                    case 'S':
                    case 'Z':
                        r.U2();
                        return null;
                    case 's':
                        return utf8(r.U2());
                    case 'e':
                        r.U2();
                        r.U2();
                        return null;
                    case 'c':
                        r.U2();
                        return null;
                    case '@':
                        var dummy = false;
                        string? nested = null;
                        ReadAnn(r, utf8, ref dummy, ref nested);
                        return null;
                    case '[':
                        var count = r.U2();
                        for (var i = 0; i < count; i++)
                            ReadElement(r, utf8);
                        return null;
                    default:
                        throw new InvalidDataException("Unknown annotation element tag " + tag);
                }
            }

            private sealed class Be
            {
                private readonly byte[] _b;
                private int _p;
                public Be(byte[] b) => _b = b;
                public int U1() => _b[_p++];
                public int U2()
                {
                    var v = (_b[_p] << 8) | _b[_p + 1];
                    _p += 2;
                    return v;
                }
                public int U4()
                {
                    var v = (_b[_p] << 24) | (_b[_p + 1] << 16) | (_b[_p + 2] << 8) | _b[_p + 3];
                    _p += 4;
                    return v;
                }
                public byte[] Bytes(int n)
                {
                    var slice = new byte[n];
                    Buffer.BlockCopy(_b, _p, slice, 0, n);
                    _p += n;
                    return slice;
                }
                public void Skip(int n) => _p += n;
            }
        }
    }
}
