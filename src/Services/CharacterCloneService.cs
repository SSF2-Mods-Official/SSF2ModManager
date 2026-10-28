using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    public class CharacterCloneRequest
    {
        public string SourcePath { get; set; } = "";
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        /// <summary>Optional FFDec override path; when empty the bundled FFDec is used.</summary>
        public string? JpexsPath { get; set; }
        /// <summary>Optional new in-game display name (the "displayName" field in Main.as). Empty leaves it unchanged.</summary>
        public string? DisplayName { get; set; }
        /// <summary>
        /// Manager-allocated resource GUID written into register("guid", …).
        /// When null/empty, a fresh Guid is generated (callers with mappings should use
        /// <see cref="MappingsEditService.AllocateUniqueGuid"/> first).
        /// </summary>
        public string? Guid { get; set; }
        /// <summary>
        /// Stage clones keep stock <c>bgm_*</c> playlist ids and still rewrite
        /// <c>stage_{old}</c> / <c>{old}_bg</c>.
        /// </summary>
        public bool IsStageClone { get; set; }
        /// <summary>
        /// Symbol-class names already exported by a fighter that stays loaded.
        /// A class in this SWF is renamed when its name is in this set and does not start with the old id.
        /// </summary>
        public IReadOnlySet<string>? TakenNames { get; set; }
    }

    public class CharacterCloneResult
    {
        public string OutputSwfPath { get; set; } = "";
        public int XmlReplacements { get; set; }
        public int SymbolClassReplacements { get; set; }
        public int ScriptFileTouches { get; set; }
        public int ScriptTextReplacements { get; set; }
        public int DisplayNameReplacements { get; set; }
        public int GuidReplacements { get; set; }
        public string? AppliedGuid { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>
    /// Clones a character SWF/SSF by rewriting identifiers in-memory via FFDec
    /// (SymbolClass + ABC strings). Aborts if the clone exceeds
    /// <see cref="CloneBudget"/>.
    /// </summary>
    public class CharacterCloneService
    {
        public static readonly TimeSpan CloneBudget = TimeSpan.FromSeconds(30);

        private readonly JpexsCliService _jpexs;

        public CharacterCloneService(JpexsCliService? jpexs = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
        }

        public async Task<CharacterCloneResult> CloneAsync(
            CharacterCloneRequest request,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
                throw new FileNotFoundException("Source character file not found.", request.SourcePath);
            if (string.IsNullOrWhiteSpace(request.OldName))
                throw new ArgumentException("Old name is required.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.NewName))
                throw new ArgumentException("New name is required.", nameof(request));
            if (string.Equals(request.OldName.Trim(), request.NewName.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("New name must differ from old name.");
            if (string.IsNullOrWhiteSpace(request.OutputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(request));

            var oldName = request.OldName.Trim();
            var newName = SanitizeFileToken(request.NewName.Trim());
            var jpexsPath = request.JpexsPath ?? "";
            Directory.CreateDirectory(request.OutputDirectory);

            var workRoot = Path.Combine(Path.GetTempPath(), "ssf2mm-clone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CloneBudget);
            var ct = timeoutCts.Token;

            try
            {
                progress?.Report("Preparing work copy…");
                var sourceExt = Path.GetExtension(request.SourcePath);
                var workSwf = Path.Combine(workRoot, "work.swf");
                var sourceBytes = File.ReadAllBytes(request.SourcePath);
                if (sourceBytes.Length < 3
                    || sourceBytes[1] != (byte)'W'
                    || sourceBytes[2] != (byte)'S'
                    || (sourceBytes[0] != (byte)'F' && sourceBytes[0] != (byte)'C' && sourceBytes[0] != (byte)'Z'))
                    sourceBytes = SwfPackage.ToSwf(sourceBytes);
                File.WriteAllBytes(workSwf, sourceBytes);

                var xmlPath = Path.Combine(workRoot, "work.xml");
                var rebuiltSwf = Path.Combine(workRoot, "rebuilt.swf");
                var phase = Stopwatch.StartNew();
                var usedInMemory = false;
                var xmlHits = 0;
                var displayNameHits = 0;
                var guidHits = 0;
                IReadOnlyList<(string From, string To)> plan = Array.Empty<(string, string)>();
                List<string> extras = new();
                IReadOnlyList<string> identityRoots = Array.Empty<string>();
                var appliedGuid = string.IsNullOrWhiteSpace(request.Guid)
                    ? System.Guid.NewGuid().ToString()
                    : request.Guid!.Trim();
                CharacterIdentityNames identity;

                try
                {
                    progress?.Report("Dumping identifiers via FFDec (in-memory)…");
                    await _jpexs.IdentDumpAsync(jpexsPath, workSwf, xmlPath, ct);
                    if (!File.Exists(xmlPath) || new FileInfo(xmlPath).Length == 0)
                        throw new InvalidOperationException("FFDec ident dump was empty.");

                    var planXml = File.ReadAllText(xmlPath);
                    progress?.Report($"Plan dumped ({phase.Elapsed.TotalSeconds:0.0}s, {new FileInfo(xmlPath).Length / 1024.0:0} KB)…");
                    phase.Restart();

                    identity = ParseIdentityFieldsFromXml(planXml);
                    identityRoots = CollectIdentityOldNames(planXml, oldName);
                    extras = identityRoots
                        .Where(r => !r.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    ct.ThrowIfCancellationRequested();
                    progress?.Report("Building replacement plan…");
                    plan = BuildCloneReplacementPlan(
                        oldName, newName, identity, identityRoots, planXml, request.IsStageClone, request.TakenNames);
                    extras = extras.Where(r => plan.Any(p => p.From.Equals(r, StringComparison.Ordinal))).ToList();
                    if (extras.Count > 0)
                        progress?.Report("Also renaming identity roots: " + string.Join(", ", extras));
                    if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    {
                        var literalSeen = new HashSet<string>(plan.Select(p => p.From), StringComparer.Ordinal);
                        var planList = plan.ToList();
                        MergeDonorLiteralPairs(
                            planXml, oldName, newName, identity, planList, literalSeen, request.DisplayName.Trim());
                        plan = planList;
                        VerifyEngineTimelineIdsNotRewritten(plan);
                    }

                    progress?.Report($"Replacement plan ready ({plan.Count} pairs, {phase.Elapsed.TotalSeconds:0.0}s)…");
                    phase.Restart();
                    ct.ThrowIfCancellationRequested();

                    var shortLeaves = plan.Where(p => p.From.Length <= 3 && p.From != p.To).ToList();
                    if (shortLeaves.Count > 0)
                        progress?.Report("Short rewrite pairs: " + string.Join(", ", shortLeaves.Select(p => p.From + "→" + p.To)));

                    var pairsPath = Path.Combine(workRoot, "pairs.txt");
                    var oldDisplay = identity.DisplayName;
                    var oldGuid = ReadIdentityFieldValue(planXml, "guid");
                    xmlHits = WriteIdentPairsFile(
                        pairsPath, plan, oldDisplay, request.DisplayName?.Trim(), oldGuid, appliedGuid);

                    progress?.Report("Rewriting SWF identifiers in memory…");
                    await _jpexs.IdentRewriteAsync(jpexsPath, workSwf, rebuiltSwf, pairsPath, ct);
                    if (!File.Exists(rebuiltSwf) || new FileInfo(rebuiltSwf).Length == 0)
                        throw new InvalidOperationException("FFDec ident rewrite did not produce a SWF.");

                    displayNameHits = !string.IsNullOrWhiteSpace(request.DisplayName) && !string.IsNullOrWhiteSpace(oldDisplay) ? 1 : 0;
                    guidHits = string.IsNullOrWhiteSpace(oldGuid) ? 0 : 1;
                    usedInMemory = true;
                    progress?.Report($"In-memory rewrite done ({phase.Elapsed.TotalSeconds:0.0}s)…");
                    // #region agent log
                    DebugAgentLog.Write("B", "CharacterCloneService.cs:ident-rewrite", "SWF after in-memory ident rewrite", DebugAgentLog.ScanSwf(rebuiltSwf));
                    // #endregion
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException and not OutOfMemoryException)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report("In-memory rewrite unavailable (" + ex.Message + "); using XML round-trip…");
                    phase.Restart();
                    progress?.Report("Converting SWF → XML via FFDec…");
                    await _jpexs.SwfToXmlAsync(jpexsPath, workSwf, xmlPath, ct);
                    if (!File.Exists(xmlPath) || new FileInfo(xmlPath).Length == 0)
                        throw new InvalidOperationException("FFDec did not produce an XML file.");

                    var xmlMb = new FileInfo(xmlPath).Length / (1024.0 * 1024.0);
                    progress?.Report($"Rewriting identifiers in SWF XML ({xmlMb:0.0} MB, streamed)…");
                    var planXml = CharacterCloneXmlStream.BuildPlanXml(xmlPath);
                    progress?.Report($"Plan built ({phase.Elapsed.TotalSeconds:0.0}s)…");
                    phase.Restart();

                    identity = ParseIdentityFieldsFromXml(planXml);
                    identityRoots = CollectIdentityOldNames(planXml, oldName);
                    extras = identityRoots
                        .Where(r => !r.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    progress?.Report("Building replacement plan…");
                    plan = BuildCloneReplacementPlan(
                        oldName, newName, identity, identityRoots, planXml, request.IsStageClone, request.TakenNames);
                    extras = extras.Where(r => plan.Any(p => p.From.Equals(r, StringComparison.Ordinal))).ToList();
                    if (extras.Count > 0)
                        progress?.Report("Also renaming identity roots: " + string.Join(", ", extras));
                    if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    {
                        var literalSeen = new HashSet<string>(plan.Select(p => p.From), StringComparer.Ordinal);
                        var planList = plan.ToList();
                        MergeDonorLiteralPairs(
                            planXml, oldName, newName, identity, planList, literalSeen, request.DisplayName.Trim());
                        plan = planList;
                        VerifyEngineTimelineIdsNotRewritten(plan);
                    }

                    var rewrittenPath = xmlPath + ".rewritten";
                    xmlHits = CharacterCloneXmlStream.RewriteIdentifierPairsToFile(xmlPath, rewrittenPath, plan);
                    progress?.Report($"Identifier rewrite done ({phase.Elapsed.TotalSeconds:0.0}s)…");
                    phase.Restart();

                    (displayNameHits, guidHits) = CharacterCloneXmlStream.ApplyCloneMetadataInFile(
                        rewrittenPath,
                        request.DisplayName?.Trim(),
                        appliedGuid);
                    if (!string.IsNullOrWhiteSpace(request.DisplayName) && displayNameHits == 0)
                        progress?.Report("Warning: could not locate a display name field to update; leaving it unchanged.");
                    if (guidHits == 0)
                        progress?.Report("Warning: could not locate register(\"guid\") / guid pool entry; SWF may keep donor GUID.");

                    if (xmlHits == 0)
                    {
                        throw new InvalidOperationException(
                            $"No occurrences of identity roots ({string.Join(", ", identityRoots)}) were found in the SWF XML. " +
                            "Check that the old linkage name matches the character class in FFDec.");
                    }

                    var introducedNewName = plan.Any(p =>
                        !string.IsNullOrEmpty(p.To) &&
                        p.To.IndexOf(newName, StringComparison.Ordinal) >= 0);
                    if (!introducedNewName && !CharacterCloneXmlStream.FileContains(rewrittenPath, newName))
                    {
                        throw new InvalidOperationException(
                            $"Rewrite did not introduce \"{newName}\" into the SWF XML.");
                    }

                    progress?.Report("Converting XML → SWF via FFDec…");
                    await _jpexs.XmlToSwfAsync(jpexsPath, rewrittenPath, rebuiltSwf, ct);
                    if (!File.Exists(rebuiltSwf) || new FileInfo(rebuiltSwf).Length == 0)
                        throw new InvalidOperationException("FFDec did not produce a rebuilt SWF.");
                    // #region agent log
                    DebugAgentLog.Write("B", "CharacterCloneService.cs:xml-rebuild", "SWF after XML round-trip", DebugAgentLog.ScanSwf(rebuiltSwf));
                    // #endregion
                }

                await UntypeFlaInstanceSlotsOnCloneAsync(jpexsPath, rebuiltSwf, workRoot, progress, ct);
                // #region agent log
                DebugAgentLog.Write("C", "CharacterCloneService.cs:after-untype", "SWF after untype stance slots", DebugAgentLog.ScanSwf(rebuiltSwf));
                // #endregion

                var rewrittenSymbols = ParseSymbolClassNamesFromXml(
                        File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : "")
                    .Select(n => RewriteTextWithPairs(n, plan))
                    .ToList();
                VerifyIdentityRootsRewritten(rewrittenSymbols, identityRoots, newName, plan);

                // XML already checked for newName above — skip FFDec -export symbolClass (~1–2s).

                var outExt = string.IsNullOrWhiteSpace(sourceExt) ? ".swf" : sourceExt;
                var outputPath = Path.Combine(request.OutputDirectory, newName + outExt);
                File.Copy(rebuiltSwf, outputPath, overwrite: true);

                progress?.Report("Done.");

                var result = new CharacterCloneResult
                {
                    OutputSwfPath = outputPath,
                    XmlReplacements = xmlHits,
                    SymbolClassReplacements = xmlHits,
                    ScriptTextReplacements = xmlHits,
                    DisplayNameReplacements = displayNameHits,
                    GuidReplacements = guidHits,
                    AppliedGuid = appliedGuid
                };

                if (extras.Count > 0)
                {
                    result.Notes.Add("Also renamed identity roots: " + string.Join(", ", extras));
                }

                if (!string.IsNullOrWhiteSpace(request.DisplayName))
                {
                    result.Notes.Add(displayNameHits > 0
                        ? $"Display name set to \"{request.DisplayName!.Trim()}\" ({displayNameHits} field(s))."
                        : "Display name was requested but no display name field could be located; left unchanged.");
                }

                result.Notes.Add(guidHits > 0
                    ? $"Resource GUID set to {appliedGuid} ({guidHits} touch(es))."
                    : $"Resource GUID {appliedGuid} was requested but no guid field could be located.");

                result.Notes.Add(usedInMemory
                    ? "Cloned via FFDec in-memory identifier rewrite (SymbolClass + ABC strings)."
                    : "Cloned via FFDec SWF XML round-trip (SymbolClass + ABC identifiers).");
                result.Notes.Add("Untyped *_fla host instance slots (stance) to MovieClip.");
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Character clone did not finish within 30 seconds and was cancelled. " +
                    "Retry; if this keeps happening the character SWF may need a faster in-memory rewrite.");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workRoot))
                        Directory.Delete(workRoot, recursive: true);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Clone] Temp cleanup failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Rewrites identifiers in text using identifier-safe matching (not raw substring).
        /// Protects names that appear inside longer identifiers (e.g. link vs linkage_id)
        /// while still renaming prefixes like link_fla and LinkExt.
        /// </summary>
        public static string RewriteText(string content, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(oldName))
                return content;

            return RewriteTextWithPairs(content, BuildReplacementPairs(oldName, newName));
        }

        public static int WriteIdentPairsFile(
            string path,
            IEnumerable<(string From, string To)> identPairs,
            string? oldDisplayName,
            string? newDisplayName,
            string? oldGuid,
            string? newGuid)
        {
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (from, to) in identPairs)
            {
                if (string.IsNullOrEmpty(from) || from == to) continue;
                if (!seen.Add("I\t" + from)) continue;
                lines.Add("I\t" + from.Replace('\t', ' ') + "\t" + to.Replace('\t', ' '));
            }

            void Exact(string? from, string? to)
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return;
                if (!seen.Add("E\t" + from)) return;
                lines.Add("E\t" + from.Replace('\t', ' ') + "\t" + to.Replace('\t', ' '));
            }

            Exact(oldDisplayName, newDisplayName);
            Exact(oldGuid, newGuid);
            File.WriteAllLines(path, lines);
            return lines.Count;
        }

        /// <summary>
        /// Rewrites the given identifier pairs in a SWF and writes the result under
        /// <paramref name="outputDirectory"/>. Used when a stage's song id has to move.
        /// </summary>
        public async Task<string> RewritePairsAsync(
            string sourcePath,
            string outputDirectory,
            IReadOnlyList<(string From, string To)> pairs,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source file was not found.", sourcePath);
            if (pairs == null || pairs.Count == 0)
                throw new ArgumentException("At least one pair is required.", nameof(pairs));
            Directory.CreateDirectory(outputDirectory);

            var workSwf = Path.Combine(outputDirectory, "work.swf");
            var sourceBytes = File.ReadAllBytes(sourcePath);
            if (sourceBytes.Length < 3
                || sourceBytes[1] != (byte)'W'
                || sourceBytes[2] != (byte)'S'
                || (sourceBytes[0] != (byte)'F' && sourceBytes[0] != (byte)'C' && sourceBytes[0] != (byte)'Z'))
                sourceBytes = SwfPackage.ToSwf(sourceBytes);
            File.WriteAllBytes(workSwf, sourceBytes);

            var pairsPath = Path.Combine(outputDirectory, "pairs.txt");
            var hits = WriteIdentPairsFile(pairsPath, pairs, null, null, null, null);
            if (hits == 0)
                throw new InvalidOperationException("No song ids to rewrite.");

            var rebuilt = Path.Combine(outputDirectory, "rewritten.swf");
            await _jpexs.IdentRewriteAsync("", workSwf, rebuilt, pairsPath, cancellationToken);
            if (!File.Exists(rebuilt) || new FileInfo(rebuilt).Length == 0)
                throw new InvalidOperationException("Could not rewrite the stage's song ids.");
            return rebuilt;
        }

        /// <summary>
        /// Applies an ordered set of identifier replacements (typically from
        /// <see cref="BuildCloneReplacementPlan"/>). Pairs should already be longest-first.
        /// </summary>
        public static string RewriteTextWithPairs(string content, IReadOnlyList<(string From, string To)> pairs)
        {
            if (string.IsNullOrEmpty(content) || pairs == null || pairs.Count == 0)
                return content;

            var usable = pairs
                .Where(p => !string.IsNullOrEmpty(p.From) && p.From != p.To)
                .GroupBy(p => p.From, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderByDescending(p => p.From.Length)
                .ToList();
            if (usable.Count == 0)
                return content;

            // Sequential longest-first (same as the old regex pass): later shorter roots
            // can still rewrite leftovers from earlier longer replacements.
            var result = content;
            foreach (var (from, to) in usable)
                result = ReplaceIdentifierTokenFast(result, from, to);
            return result;
        }

        private static string ReplaceIdentifierTokenFast(string content, string from, string to)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(from))
                return content;

            var sb = new StringBuilder(content.Length + 8);
            var pos = 0;
            var changed = false;
            while (pos < content.Length)
            {
                if (IdentifierMatchesAt(content.AsSpan(), pos, from, null))
                {
                    sb.Append(to);
                    pos += from.Length;
                    changed = true;
                }
                else
                {
                    sb.Append(content[pos]);
                    pos++;
                }
            }

            return changed ? sb.ToString() : content;
        }

        /// <summary>Character identity fields parsed from FFDec SWF XML / ABC string pool.</summary>
        public sealed class CharacterIdentityNames
        {
            public string? StatsName { get; init; }
            public string? DisplayName { get; init; }
            public string? LinkageId { get; init; }
            public string? LinkageId2 { get; init; }
            public string? LinkageIdSpecial { get; init; }
            public string? FinalSmashCutin { get; init; }
            public string? Thumbnail { get; init; }
            public string? SeriesIcon { get; init; }
            public string? ClassApi { get; init; }
        }

        /// <summary>Bare SymbolClass names that must not be renamed (SWF document bootstrap).</summary>
        public static readonly HashSet<string> BareSymbolClassDenylist = new(StringComparer.Ordinal)
        {
            "Main"
        };

        /// <summary>
        /// Fighter timeline / attack ids the engine hardcodes (Attack("b"), playFrame("stand"), …).
        /// A leftover export named "b" must not be globally rewritten or specials break.
        /// </summary>
        public static readonly HashSet<string> EngineTimelineIds = new(StringComparer.Ordinal)
        {
            "stand", "entrance", "revival", "win", "lose", "walk", "run", "skid",
            "jump", "jump_midair", "fall", "helpless", "land", "heavyland", "hover",
            "a", "a_forward", "a_forward_tilt", "a_forwardsmash", "a_up_tilt", "a_up", "a_down",
            "a_air", "a_air_up", "a_air_forward", "a_air_backward", "a_air_down",
            "b", "b_air", "b_up", "b_up_air", "b_forward", "b_forward_air", "b_down", "b_down_air",
            "throw_up", "throw_forward", "throw_down", "throw_back", "pummel",
            "item_pickup", "item_jab", "item_dash", "item_tilt", "item_smash", "item_homerun",
            "item_fan", "item_shoot", "item_heavyshoot", "item_raise", "item_screw",
            "item_firedash", "item_bubblebounce", "item_float", "toss", "toss_air",
            "crouch", "crouch_attack", "uncrouch", "grab", "hurt", "dodgeroll", "airdodge",
            "sidestep", "hang", "climbup", "rollup", "ledge_attack", "defend", "stunned",
            "dizzy", "sleep", "flying", "falling", "crash", "roll", "getup_attack",
            "carry", "swim", "ladder", "edgelean", "wallstick", "frozen", "taunt",
            "egg", "star", "special", "starko", "screenko", "pitfall", "tech_ground",
            "tech_roll", "wait", "wait1", "wait2", "blink", "loop", "idle"
        };

        public static bool IsEngineTimelineId(string? name) =>
            !string.IsNullOrEmpty(name) && EngineTimelineIds.Contains(name);

        /// <summary>Pass B must not globally rewrite these leftover SymbolClass leaves.</summary>
        public static bool ShouldSkipBareSymbolSweep(string? leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf)) return true;
            if (leaf.Length <= 2) return true;
            if (leaf.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase)) return true;
            if (BareSymbolClassDenylist.Contains(leaf)) return true;
            return EngineTimelineIds.Contains(leaf);
        }

        /// <summary>
        /// Reads statsName / linkage_id / linkage_id2 / linkage_id_special / finalSmashCutin /
        /// thumbnail / seriesIcon from SWF XML (constant-pool &lt;item&gt; pairs and quoted AS literals).
        /// </summary>
        public static CharacterIdentityNames ParseIdentityFieldsFromXml(string xml)
        {
            if (string.IsNullOrEmpty(xml))
                return new CharacterIdentityNames();

            return new CharacterIdentityNames
            {
                StatsName = ReadIdentityFieldValue(xml, "statsName"),
                DisplayName = ReadIdentityFieldValue(xml, "displayName"),
                LinkageId = ReadIdentityFieldValue(xml, "linkage_id"),
                LinkageId2 = ReadIdentityFieldValue(xml, "linkage_id2"),
                LinkageIdSpecial = ReadIdentityFieldValue(xml, "linkage_id_special"),
                FinalSmashCutin = ReadIdentityFieldValue(xml, "finalSmashCutin"),
                Thumbnail = ReadIdentityFieldValue(xml, "thumbnail"),
                SeriesIcon = ReadIdentityFieldValue(xml, "seriesIcon"),
                ClassApi = ReadIdentityFieldValue(xml, "classAPI")
            };
        }

        /// <summary>
        /// Linkage and display name stored in the SWF. Linkage is linkage_id, then statsName,
        /// then the _fla symbol that matches the display name.
        /// </summary>
        public static (string? Linkage, string? DisplayName) ReadImportIdentity(byte[] swf)
        {
            var strings = new List<string>();
            var i = 0;
            while (i < swf.Length)
            {
                if (swf[i] >= 32 && swf[i] < 127)
                {
                    var start = i;
                    while (i < swf.Length && swf[i] >= 32 && swf[i] < 127)
                        i++;
                    if (i - start >= 3 && i - start <= 80)
                        strings.Add(Encoding.ASCII.GetString(swf, start, i - start));
                }
                else
                {
                    i++;
                }
            }

            string? display = null;
            string? linkageId = null;
            string? statsName = null;
            var flaRoots = new List<string>();
            for (var n = 0; n < strings.Count; n++)
            {
                var text = strings[n];
                if (text.EndsWith("_fla", StringComparison.Ordinal) && text.Length > 4)
                {
                    var root = text[..^4];
                    if (IsPlainLinkage(root))
                        flaRoots.Add(root);
                }
                if (n + 1 >= strings.Count)
                    continue;
                var next = strings[n + 1];
                if (string.IsNullOrWhiteSpace(next) ||
                    next.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                    DisplayNameGuardKeys.Contains(next))
                    continue;
                if (display == null && text == "displayName")
                    display = next;
                else if (linkageId == null && text == "linkage_id")
                    linkageId = next;
                else if (statsName == null && text == "statsName")
                    statsName = next;
            }

            string? linkage = null;
            if (IsPlainLinkage(linkageId))
                linkage = linkageId;
            else if (IsPlainLinkage(statsName))
                linkage = statsName;
            else
            {
                var compact = Regex.Replace(display ?? "", @"[^A-Za-z0-9]", "");
                linkage = flaRoots.FirstOrDefault(root => root.Equals(compact, StringComparison.OrdinalIgnoreCase));
                if (linkage == null)
                {
                    var only = flaRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (only.Count == 1)
                        linkage = only[0];
                }
            }

            var shown = string.IsNullOrWhiteSpace(display) ? linkage : display.Trim();
            return (linkage, shown);
        }

        public static bool IsPlainLinkage(string? id) =>
            !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, @"^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>
        /// Union of UI/filename OldName with identity metadata tokens (deduped).
        /// When ABC pool values are deduplicated (key followed by another key), also detects
        /// SymbolClass document-family roots (e.g. dkong + dkong_charselect) that the
        /// primary rewrite would not touch. Does not invent roots from unrelated symbols.
        /// </summary>
        public static IReadOnlyList<string> CollectIdentityOldNames(string xml, string primaryOldName)
        {
            var roots = new List<string>();
            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var v = value.Trim();
                if (v.Equals("null", StringComparison.OrdinalIgnoreCase)) return;
                if (DisplayNameGuardKeys.Contains(v)) return;
                if (IsEngineTimelineId(v)) return;
                if (roots.Any(r => r.Equals(v, StringComparison.Ordinal))) return;
                roots.Add(v);
            }

            Add(primaryOldName);
            var identity = ParseIdentityFieldsFromXml(xml);
            Add(identity.StatsName);
            Add(identity.LinkageId);
            Add(identity.LinkageId2);
            Add(identity.LinkageIdSpecial);
            Add(identity.FinalSmashCutin);
            Add(identity.Thumbnail);
            Add(identity.SeriesIcon);
            Add(StemFromExtClass(identity.ClassApi));

            foreach (var stem in DetectIrregularPascalStems(xml, primaryOldName))
                Add(stem);

            foreach (var root in DetectSymbolClassFamilyRoots(xml, primaryOldName))
                Add(root);

            foreach (var name in ParseSymbolClassNamesFromXml(xml))
            {
                if (name.IndexOf('.') >= 0 || name.IndexOf(':') >= 0) continue;
                if (IsBodyClassAlias(name, primaryOldName, identity))
                    Add(name);
            }

            foreach (var abbrev in DetectAbbrevRoots(roots, primaryOldName))
                Add(abbrev);

            return roots;
        }

        /// <summary>
        /// Stems like BandanaDee from BandanaDeeExt / BandanaDeeCut when naive
        /// ToPascal(bandanadee) is Bandanadee and would miss the real class name.
        /// </summary>
        public static IReadOnlyList<string> DetectIrregularPascalStems(string xml, string primaryOldName)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(xml) || string.IsNullOrWhiteSpace(primaryOldName))
                return found;

            void Consider(string? token)
            {
                var stem = TryExtractMatchingPascalStem(token, primaryOldName);
                if (string.IsNullOrEmpty(stem)) return;
                if (found.Any(s => s.Equals(stem, StringComparison.Ordinal))) return;
                found.Add(stem);
            }

            Consider(ReadIdentityFieldValue(xml, "classAPI"));
            foreach (var name in ParseSymbolClassNamesFromXml(xml))
            {
                if (name.Length > 80) continue;
                foreach (Match m in Regex.Matches(name, @"[A-Za-z_][A-Za-z0-9_]*"))
                    Consider(m.Value);
            }

            return found;
        }

        public static string? StemFromExtClass(string? classApi)
        {
            if (string.IsNullOrWhiteSpace(classApi)) return null;
            var v = classApi.Trim();
            if (v.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            if (v.EndsWith("Ext", StringComparison.Ordinal) && v.Length > 3)
                return v[..^3];
            return v;
        }

        public static string? TryExtractMatchingPascalStem(string? identifier, string primaryOldName)
        {
            if (string.IsNullOrEmpty(identifier) || string.IsNullOrWhiteSpace(primaryOldName))
                return null;
            if (identifier.IndexOf('_') >= 0) return null;

            var fold = LettersOnlyLower(primaryOldName);
            if (fold.Length < 4) return null;

            var letterCount = 0;
            for (var i = 0; i < identifier.Length; i++)
            {
                if (!char.IsLetter(identifier[i]))
                {
                    if (letterCount == 0) continue;
                    break;
                }
                letterCount++;
                if (letterCount != fold.Length) continue;

                var stem = identifier[..(i + 1)];
                if (stem.Equals(primaryOldName, StringComparison.Ordinal)) return null;
                if (stem.Equals(ToPascal(primaryOldName), StringComparison.Ordinal)) return null;
                if (!LettersOnlyLower(stem).Equals(fold, StringComparison.Ordinal)) return null;
                if (!stem.Any(char.IsUpper) || !stem.Any(char.IsLower)) return null;
                return stem;
            }

            return null;
        }

        public static string LettersOnlyLower(string s) =>
            new string((s ?? "").Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());

        /// <summary>
        /// Short prefixes from body linkage roots (e.g. dkong → dk) so leaf exports like
        /// dk_fs_cutin rename when underscore-bounded.
        /// </summary>
        public static IReadOnlyList<string> DetectAbbrevRoots(
            IReadOnlyList<string> identityRoots,
            string primaryOldName)
        {
            var found = new List<string>();
            if (identityRoots == null || identityRoots.Count == 0)
                return found;

            foreach (var root in identityRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (root.Equals(primaryOldName, StringComparison.OrdinalIgnoreCase)) continue;
                // Prefer short body-style ids (dkong), not long metadata (dk_fs_cutin).
                if (root.Length < 4 || root.Length > 8) continue;
                if (root.Contains('_')) continue;
                if (!root.All(c => char.IsLetterOrDigit(c))) continue;
                // Pascal symbols (MKSymbol) are not body ids. Their first two letters
                // would rewrite unrelated timelines (mk_ → clone).
                if (root.Any(char.IsUpper)) continue;
                // Abbrev only when root is shorter than the filename/stats id (dkong vs donkeykong).
                if (root.Length >= primaryOldName.Length) continue;

                var abbrev = root.Substring(0, 2).ToLowerInvariant();
                if (abbrev.Length < 2) continue;
                if (!abbrev.All(char.IsLetter)) continue;
                if (abbrev.Equals(primaryOldName, StringComparison.OrdinalIgnoreCase)) continue;
                if (found.Any(a => a.Equals(abbrev, StringComparison.OrdinalIgnoreCase))) continue;
                // Only keep abbrev if it is a real prefix of this root (dkong starts with dk).
                if (!root.StartsWith(abbrev, StringComparison.OrdinalIgnoreCase)) continue;
                found.Add(abbrev);
            }

            return found;
        }

        /// <summary>
        /// Finds bare SymbolClass names that look like a fighter document family
        /// (root + root_charselect / _hud / …) and would survive a primary-only rewrite.
        /// </summary>
        public static IReadOnlyList<string> DetectSymbolClassFamilyRoots(string xml, string primaryOldName)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(xml) || string.IsNullOrWhiteSpace(primaryOldName))
                return found;

            var bare = ParseSymbolClassNamesFromXml(xml)
                .Where(n => n.IndexOf('.') < 0 && n.IndexOf(':') < 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (bare.Count == 0) return found;

            var primaryFold = LettersOnlyLower(primaryOldName);
            foreach (var name in bare.OrderBy(n => n.Length))
            {
                if (name.Equals(primaryOldName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.Length < 4 || name.Length > 32)
                    continue;
                if (CountIdentifierOccurrences(name, primaryOldName) > 0)
                    continue;
                if (!LooksLikeDonorFamilyRoot(name, primaryOldName, primaryFold))
                    continue;
                var prefix = name + "_";
                var hasChild = false;
                foreach (var n in bare)
                {
                    if (n.Length <= prefix.Length || !n.StartsWith(prefix, StringComparison.Ordinal))
                        continue;
                    if (IsDocumentFamilyChildSuffix(n[prefix.Length..]))
                    {
                        hasChild = true;
                        break;
                    }
                }
                if (hasChild)
                    found.Add(name);
            }

            return found;
        }

        /// <summary>
        /// Suffixes that indicate a SymbolClass child belongs to a fighter UI/document family
        /// (dkong_charselect), not an arbitrary move clip (bm_taunt3_metal).
        /// </summary>
        private static bool IsDocumentFamilyChildSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return false;
            if (suffix.Equals("charselect", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("hud", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("select", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("head", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("thumbnail", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("series", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("icon", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return suffix.StartsWith("charselect", StringComparison.OrdinalIgnoreCase) ||
                   suffix.EndsWith("_hud", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeDonorFamilyRoot(string name, string primaryOldName, string primaryFold)
        {
            if (name.StartsWith(primaryOldName, StringComparison.OrdinalIgnoreCase))
                return true;

            var fold = LettersOnlyLower(name);
            if (fold.Length >= 2 && IsLetterSubsequence(fold, primaryFold))
                return true;

            var stem = name;
            var us = name.IndexOf('_');
            if (us > 0)
                stem = name[..us];
            var stemFold = LettersOnlyLower(stem);
            return stemFold.Length >= 2 && stemFold.Length <= 8 && IsLetterSubsequence(stemFold, primaryFold);
        }

        private static bool IsLetterSubsequence(string needle, string haystack)
        {
            if (needle.Length == 0 || needle.Length > haystack.Length)
                return false;
            var j = 0;
            for (var i = 0; i < haystack.Length && j < needle.Length; i++)
            {
                if (haystack[i] == needle[j])
                    j++;
            }
            return j == needle.Length;
        }

        /// <summary>
        /// Builds identifier replacement pairs for the fighter id and exports that would
        /// collide with a name that stays loaded. Stock lookups that are not symbol classes
        /// in this SWF, and in-file classes that do not start with the id, stay unless
        /// <paramref name="takenNames"/> already contains them.
        /// </summary>
        public static IReadOnlyList<(string From, string To)> BuildCloneReplacementPlan(
            string primaryOld,
            string newName,
            CharacterIdentityNames identity,
            IReadOnlyList<string>? allIdentityRoots = null,
            string? swfXmlForBareSweep = null,
            bool isStageClone = false,
            IReadOnlySet<string>? takenNames = null)
        {
            if (string.IsNullOrWhiteSpace(primaryOld) || string.IsNullOrWhiteSpace(newName))
                return Array.Empty<(string, string)>();

            var p = primaryOld.Trim();
            var n = newName.Trim();
            var rootMap = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string>? definedInSwf = null;
            if (!string.IsNullOrEmpty(swfXmlForBareSweep))
            {
                definedInSwf = new HashSet<string>(
                    ParseSymbolClassNamesFromXml(swfXmlForBareSweep),
                    StringComparer.Ordinal);
            }

            void MapRoot(string? root, string? explicitTo = null)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var r = root.Trim();
                if (r.Equals("null", StringComparison.OrdinalIgnoreCase)) return;
                if (DisplayNameGuardKeys.Contains(r)) return;
                if (BareSymbolClassDenylist.Contains(r)) return;
                if (IsEngineTimelineId(r)) return;
                if (isStageClone && r.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase)) return;
                if (rootMap.ContainsKey(r)) return;
                rootMap[r] = explicitTo ?? MapIdentityRootToNewName(r, p, n, identity, allIdentityRoots, definedInSwf);
            }

            MapRoot(p);
            MapRoot(identity.StatsName);
            MapRoot(identity.LinkageId);
            MapRoot(identity.LinkageId2);
            MapRoot(identity.LinkageIdSpecial);
            MapRoot(identity.FinalSmashCutin);
            MapRoot(identity.Thumbnail);
            MapRoot(identity.SeriesIcon);
            if (allIdentityRoots != null)
            {
                foreach (var root in allIdentityRoots)
                    MapRoot(root);
            }

            // Abbrev roots (dk → newName) so dk_fs_cutin becomes newName_fs_cutin.
            foreach (var abbrev in DetectAbbrevRoots(
                         allIdentityRoots ?? rootMap.Keys.ToList(), p))
            {
                MapRoot(abbrev, n);
            }

            var pairs = new List<(string From, string To)>();
            var seenFrom = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (fromRoot, toRoot) in rootMap)
            {
                foreach (var (from, to) in BuildReplacementPairs(fromRoot, toRoot))
                {
                    if (from.Length == 0 || from == to) continue;
                    if (!seenFrom.Add(from)) continue;
                    pairs.Add((from, to));
                }
            }

            if (isStageClone)
            {
                foreach (var (from, to) in BuildReplacementPairs("stage_" + p, "stage_" + n)
                    .Concat(BuildReplacementPairs(p + "_bg", n + "_bg")))
                {
                    if (from.Length == 0 || from == to) continue;
                    if (!seenFrom.Add(from)) continue;
                    pairs.Add((from, to));
                }
            }

            pairs = pairs.OrderByDescending(pair => pair.From.Length).ToList();

            MergeExtClassPairs(swfXmlForBareSweep, p, n, identity, allIdentityRoots, pairs, seenFrom);
            pairs = pairs.OrderByDescending(pair => pair.From.Length).ToList();

            if (!string.IsNullOrEmpty(swfXmlForBareSweep))
            {
                MergeBareSymbolClassSweep(swfXmlForBareSweep, p, n, pairs, seenFrom, identity, takenNames);
                MergeDonorSoundAliasPairs(swfXmlForBareSweep, pairs, seenFrom);
                pairs = pairs.OrderByDescending(pair => pair.From.Length).ToList();
            }

            VerifyEngineTimelineIdsNotRewritten(pairs);
            return pairs;
        }

        /// <summary>
        /// Black Mage (and similar) expose compressed sound ids in Main (bmtaunt2) alongside
        /// underscored symbols (bm_taunt2). Mirror the target when the compressed id is still in ABC.
        /// </summary>
        public static void MergeDonorSoundAliasPairs(
            string xml,
            List<(string From, string To)> pairs,
            HashSet<string>? seenFrom = null)
        {
            if (string.IsNullOrEmpty(xml) || pairs == null) return;
            seenFrom ??= new HashSet<string>(pairs.Select(p => p.From), StringComparer.Ordinal);

            foreach (var (from, to) in pairs.ToList())
            {
                if (from.Length < 4 || !from.StartsWith("bm_", StringComparison.Ordinal))
                    continue;
                var compressed = "bm" + from[3..];
                if (compressed.Equals(from, StringComparison.Ordinal))
                    continue;
                if (xml.IndexOf(compressed, StringComparison.Ordinal) < 0)
                    continue;
                if (!seenFrom.Add(compressed))
                    continue;
                pairs.Add((compressed, to));
            }
        }

        /// <summary>
        /// Global identifier rewrite of engine attack/timeline ids (b, b_up, stand, …)
        /// makes the engine call Attack("b") against a SWF that only has Clone_Dee_b.
        /// </summary>
        public static void VerifyEngineTimelineIdsNotRewritten(IReadOnlyList<(string From, string To)> pairs)
        {
            if (pairs == null) return;
            foreach (var (from, to) in pairs)
            {
                if (string.IsNullOrEmpty(from) || from == to) continue;
                if (!IsEngineTimelineId(from)) continue;
                throw new InvalidOperationException(
                    $"Clone plan would rewrite engine timeline id \"{from}\" → \"{to}\". " +
                    "Specials/attacks use these labels as-is (Attack(\"b\")). Leave them unchanged.");
            }
        }

        /// <summary>
        /// Rewrites donor-specific literals left in ABC pools (init traces, getDonorStats helpers).
        /// </summary>
        public static void MergeDonorLiteralPairs(
            string xml,
            string primaryOld,
            string newName,
            CharacterIdentityNames identity,
            List<(string From, string To)> pairs,
            HashSet<string>? seenFrom = null,
            string? newDisplayName = null)
        {
            if (string.IsNullOrEmpty(xml) || pairs == null) return;
            seenFrom ??= new HashSet<string>(pairs.Select(p => p.From), StringComparer.Ordinal);
            var ordered = pairs.OrderByDescending(p => p.From.Length).ToList();

            void AddPair(string from, string to)
            {
                if (string.IsNullOrEmpty(from) || from == to) return;
                if (!seenFrom.Add(from)) return;
                pairs.Add((from, to));
            }

            var oldDisplay = ReadIdentityFieldValue(xml, "displayName");
            if (string.IsNullOrWhiteSpace(oldDisplay) && !string.IsNullOrWhiteSpace(identity.DisplayName))
                oldDisplay = identity.DisplayName;
            if (!string.IsNullOrWhiteSpace(oldDisplay))
            {
                var plannedDisplay = !string.IsNullOrWhiteSpace(newDisplayName)
                    ? newDisplayName.Trim()
                    : RewriteTextWithPairs(oldDisplay, ordered);
                if (string.Equals(plannedDisplay, oldDisplay, StringComparison.Ordinal))
                    plannedDisplay = newName.Replace('_', ' ');
                AddPair(oldDisplay + " initialized.", plannedDisplay + " initialized.");
            }

            var getterTarget = "get" + ToPascal(newName);
            AddPair("get" + ToPascal(primaryOld), getterTarget);
            var extStem = StemFromExtClass(identity.ClassApi);
            if (!string.IsNullOrWhiteSpace(extStem))
                AddPair("get" + extStem, getterTarget);
            foreach (var stem in DetectIrregularPascalStems(xml, primaryOld))
                AddPair("get" + stem, getterTarget);
        }

        /// <summary>
        /// Pass B: a leftover bare SymbolClass is renamed only when its name starts with the
        /// fighter id, or when <paramref name="takenNames"/> already contains that exact name.
        /// </summary>
        public static void MergeBareSymbolClassSweep(
            string xml,
            string primaryOld,
            string newName,
            List<(string From, string To)> pairs,
            HashSet<string>? seenFrom = null,
            CharacterIdentityNames? identity = null,
            IReadOnlySet<string>? takenNames = null)
        {
            if (string.IsNullOrEmpty(xml) || pairs == null) return;
            seenFrom ??= new HashSet<string>(pairs.Select(p => p.From), StringComparer.Ordinal);
            identity ??= new CharacterIdentityNames();

            var identityPairs = pairs.ToList();
            var bare = ParseSymbolClassNamesFromXml(xml)
                .Where(nm => nm.IndexOf('.') < 0 && nm.IndexOf(':') < 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var leaf in bare)
            {
                if (ShouldSkipBareSymbolSweep(leaf)) continue;
                if (leaf.Equals(newName, StringComparison.OrdinalIgnoreCase)) continue;
                if (leaf.IndexOf(newName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !IsBodyClassAlias(leaf, primaryOld, identity))
                    continue;
                if (seenFrom.Contains(leaf)) continue;
                if (IsCoveredByIdentityPairs(leaf, identityPairs) &&
                    !IsBodyClassAlias(leaf, primaryOld, identity))
                    continue;

                string to;
                if (IsBodyClassAlias(leaf, primaryOld, identity))
                    to = MapIdentityRootToNewName(leaf, primaryOld, newName, identity);
                else if (TryRewriteIdPrefix(leaf, primaryOld, newName, out var prefixed))
                    to = prefixed;
                else if (takenNames != null && takenNames.Contains(leaf))
                    to = ApplyCloneSuffix(leaf, primaryOld, newName);
                else
                    continue;

                if (string.IsNullOrEmpty(to) || to.Equals(leaf, StringComparison.Ordinal)) continue;
                if (!seenFrom.Add(leaf)) continue;
                pairs.Add((leaf, to));
            }
        }

        /// <summary>
        /// Replaces a leading fighter id, including a Pascal export such as ZamusDizzy_0.
        /// A following letter is still part of the export name.
        /// </summary>
        public static bool TryRewriteIdPrefix(string leaf, string primaryOld, string newName, out string rewritten)
        {
            rewritten = leaf;
            if (string.IsNullOrEmpty(leaf) || string.IsNullOrEmpty(primaryOld) || string.IsNullOrEmpty(newName))
                return false;

            if (leaf.StartsWith(primaryOld, StringComparison.Ordinal) && leaf.Length > primaryOld.Length)
            {
                rewritten = newName + leaf[primaryOld.Length..];
                return !rewritten.Equals(leaf, StringComparison.Ordinal);
            }

            var pascalOld = ToPascal(primaryOld);
            if (pascalOld.Equals(primaryOld, StringComparison.Ordinal))
                return false;
            if (!leaf.StartsWith(pascalOld, StringComparison.Ordinal) || leaf.Length <= pascalOld.Length)
                return false;

            rewritten = ToPascal(newName) + leaf[pascalOld.Length..];
            return !rewritten.Equals(leaf, StringComparison.Ordinal);
        }

        private static bool IsCoveredByIdentityPairs(string leaf, List<(string From, string To)> identityPairs)
        {
            foreach (var p in identityPairs)
            {
                if (p.From.Length == 0) continue;
                var idx = 0;
                while ((idx = leaf.IndexOf(p.From, idx, StringComparison.Ordinal)) >= 0)
                {
                    if (IdentifierMatchesAt(leaf.AsSpan(), idx, p.From, null))
                        return true;
                    idx++;
                }
            }

            return false;
        }

        public static string MapIdentityRootToNewName(
            string root,
            string primaryOld,
            string newName,
            CharacterIdentityNames identity,
            IReadOnlyList<string>? allIdentityRoots = null,
            IReadOnlySet<string>? definedInSwf = null)
        {
            if (root.Equals(primaryOld, StringComparison.OrdinalIgnoreCase))
                return newName;

            if (IsBodyClassAlias(root, primaryOld, identity))
                return newName;

            if (!string.IsNullOrWhiteSpace(identity.StatsName) &&
                root.Equals(identity.StatsName, StringComparison.OrdinalIgnoreCase))
            {
                return newName;
            }

            // Cut-in / thumbnail / series icon stay when this SWF does not define that class.
            // A stock lookup such as MetroidSymbolCSS is not this fighter's export.
            if ((!string.IsNullOrWhiteSpace(identity.FinalSmashCutin) &&
                 root.Equals(identity.FinalSmashCutin, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(identity.Thumbnail) &&
                 root.Equals(identity.Thumbnail, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(identity.SeriesIcon) &&
                 root.Equals(identity.SeriesIcon, StringComparison.OrdinalIgnoreCase)))
            {
                var defined = definedInSwf != null && definedInSwf.Contains(root);
                if (!defined)
                    return root;
                if (TryRewriteIdPrefix(root, primaryOld, newName, out var prefixed))
                    return prefixed;
                return root;
            }

            // Second / special forms keep their own id with the same suffix.
            if ((!string.IsNullOrWhiteSpace(identity.LinkageId2) &&
                 root.Equals(identity.LinkageId2, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(identity.LinkageIdSpecial) &&
                 root.Equals(identity.LinkageIdSpecial, StringComparison.OrdinalIgnoreCase)))
            {
                return ApplyCloneSuffix(root, primaryOld, newName);
            }

            // Main body linkage (parsed) → newName.
            if (!string.IsNullOrWhiteSpace(identity.LinkageId) &&
                root.Equals(identity.LinkageId, StringComparison.OrdinalIgnoreCase))
            {
                return newName;
            }

            // SymbolClass-detected extras (e.g. dkong when pool value was deduped):
            // Prefer the UI/document family (has _charselect) → newName; other families get suffix.
            var extras = (allIdentityRoots ?? Array.Empty<string>())
                .Where(r =>
                    !r.Equals(primaryOld, StringComparison.OrdinalIgnoreCase) &&
                    (identity.StatsName == null || !r.Equals(identity.StatsName, StringComparison.OrdinalIgnoreCase)) &&
                    (identity.LinkageId == null || !r.Equals(identity.LinkageId, StringComparison.OrdinalIgnoreCase)) &&
                    (identity.LinkageId2 == null || !r.Equals(identity.LinkageId2, StringComparison.OrdinalIgnoreCase)) &&
                    (identity.LinkageIdSpecial == null || !r.Equals(identity.LinkageIdSpecial, StringComparison.OrdinalIgnoreCase)) &&
                    (identity.FinalSmashCutin == null || !r.Equals(identity.FinalSmashCutin, StringComparison.OrdinalIgnoreCase)) &&
                    (identity.Thumbnail == null || !r.Equals(identity.Thumbnail, StringComparison.OrdinalIgnoreCase)) &&
                    (identity.SeriesIcon == null || !r.Equals(identity.SeriesIcon, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Detected abbrevs (dk from dkong) are mapped to newName in
            // BuildCloneReplacementPlan via MapRoot(abbrev, n). Do not treat
            // leftover 1–3 letter exports (bio, sfx, hit) as the fighter ident.

            if (extras.Any(r => r.Equals(root, StringComparison.OrdinalIgnoreCase)))
            {
                // Move/sound/metadata roots (bm_taunt3, bm_waterSpout) must keep their tail;
                // only bare document-family ids (dkong) become the fighter id.
                if (root.IndexOf('_', StringComparison.Ordinal) >= 0)
                    return ApplyCloneSuffix(root, primaryOld, newName);

                if (extras.Count <= 1)
                    return newName;

                // Multiple bare abbrev roots (dkong + dk): UI body → newName, specials → suffix.
                if (root.IndexOf("_nspecial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.IndexOf("_uspecial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.IndexOf("_sspecial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.IndexOf("_dspecial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.IndexOf("_fspecial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ApplyCloneSuffix(root, primaryOld, newName);
                }

                return newName;
            }

            return ApplyCloneSuffix(root, primaryOld, newName);
        }

        /// <summary>
        /// Renames ABC class names ending in Ext (e.g. BandanaDeeExt → Clone_DeeExt).
        /// </summary>
        public static string MapExtClassName(
            string oldExtClass,
            string primaryOld,
            string newName,
            IReadOnlyList<string>? identityRoots = null)
        {
            if (string.IsNullOrWhiteSpace(oldExtClass) ||
                !oldExtClass.EndsWith("Ext", StringComparison.Ordinal) ||
                oldExtClass.Length <= 3)
            {
                return newName + "Ext";
            }

            var body = oldExtClass[..^3];
            var pascalOld = ToPascal(primaryOld);

            foreach (var stem in identityRoots ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(stem)) continue;
                if (body.Equals(stem, StringComparison.Ordinal))
                    return newName + "Ext";
            }

            if (body.Equals(pascalOld, StringComparison.Ordinal) ||
                body.Equals(primaryOld, StringComparison.OrdinalIgnoreCase))
            {
                return newName + "Ext";
            }

            if (body.StartsWith("Dee", StringComparison.Ordinal) && body.Length > 3 &&
                LettersOnlyLower(primaryOld).Contains("dee"))
            {
                return newName + body[3..] + "Ext";
            }

            return newName + "_" + oldExtClass;
        }

        public static void MergeExtClassPairs(
            string? xml,
            string primaryOld,
            string newName,
            CharacterIdentityNames identity,
            IReadOnlyList<string>? identityRoots,
            List<(string From, string To)> pairs,
            HashSet<string> seenFrom)
        {
            var extNames = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(identity.ClassApi))
                extNames.Add(identity.ClassApi.Trim());

            if (!string.IsNullOrEmpty(xml))
            {
                foreach (Match m in Regex.Matches(xml, @"<item>([A-Za-z][A-Za-z0-9_]*Ext)</item>"))
                {
                    var name = m.Groups[1].Value;
                    if (name.Contains('.', StringComparison.Ordinal)) continue;
                    extNames.Add(name);
                }
            }

            foreach (var oldExt in extNames)
            {
                if (!oldExt.EndsWith("Ext", StringComparison.Ordinal)) continue;
                var neu = MapExtClassName(oldExt, primaryOld, newName, identityRoots);
                if (oldExt == neu || !seenFrom.Add(oldExt)) continue;
                pairs.Add((oldExt, neu));
            }
        }

        /// <summary>
        /// <c>blackmage</c> stats vs SymbolClass <c>black_mage</c> are the same body id.
        /// Both must map to the clone ident, not <c>{ident}_black_mage</c>.
        /// Also treats leftover host names like <c>Bo_Mama_black_mage</c> as the body.
        /// </summary>
        private static bool IsBodyClassAlias(
            string root,
            string primaryOld,
            CharacterIdentityNames identity)
        {
            if (string.IsNullOrEmpty(root))
                return false;
            foreach (var donor in JpexsCliService.DonorBodyClassAliases)
            {
                if (root.Equals(donor, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (root.EndsWith("_" + donor, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var compact = CompactIdent(root);
            if (compact.Length == 0)
                return false;
            if (compact.Equals(CompactIdent(primaryOld), StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(identity.StatsName) &&
                compact.Equals(CompactIdent(identity.StatsName), StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(identity.LinkageId) &&
                compact.Equals(CompactIdent(identity.LinkageId), StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static string CompactIdent(string? value) =>
            Regex.Replace(value ?? "", @"[\s_\-]", "");

        public static string ApplyCloneSuffix(string root, string primaryOld, string newName)
        {
            if (newName.StartsWith(primaryOld, StringComparison.OrdinalIgnoreCase) &&
                newName.Length > primaryOld.Length)
            {
                return root + newName.Substring(primaryOld.Length);
            }

            if (newName.EndsWith("_clone", StringComparison.OrdinalIgnoreCase) &&
                !root.EndsWith("_clone", StringComparison.OrdinalIgnoreCase))
            {
                return root + "_clone";
            }

            // Full rename (bandanadee → Clone_Dee). Prefix leftover exports so
            // dairSparkle / Kirby_Symbol do not all collapse onto the fighter id.
            if (!root.StartsWith(newName + "_", StringComparison.OrdinalIgnoreCase) &&
                !root.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                return newName + "_" + root;
            }

            return newName;
        }

        private static string? ReadIdentityFieldValue(string xml, string key)
        {
            // Constant pool: <item>linkage_id</item><item>dkong</item>
            // Deduped pools put the next key (revivalEffect) in that slot. Skip those
            // and keep the later hit (Starfox_Symbol).
            var pool = new Regex(
                $@"<item>{Regex.Escape(key)}</item>\s*<item>([^<]*)</item>",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            foreach (Match m in pool.Matches(xml))
            {
                var value = XmlUnescape(m.Groups[1].Value.Trim());
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (value.Equals("null", StringComparison.OrdinalIgnoreCase)) continue;
                if (DisplayNameGuardKeys.Contains(value)) continue;
                if (IsDedupedStatsNeighbor(value)) continue;
                return value;
            }

            // Quoted AS / JSON-like literals in scripts embedded in XML.
            var quoted = new Regex(
                $@"[""']{Regex.Escape(key)}[""']\s*:\s*[""']([^""']+)[""']",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var quotedMatch = quoted.Match(xml);
            if (quotedMatch.Success)
            {
                var value = quotedMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Equals("null", StringComparison.OrdinalIgnoreCase) &&
                    !DisplayNameGuardKeys.Contains(value) &&
                    !IsDedupedStatsNeighbor(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string XmlUnescape(string value) =>
            value.Replace("&lt;", "<")
                 .Replace("&gt;", ">")
                 .Replace("&amp;", "&");

        /// <summary>Bare SymbolClass linkage names from FFDec SWF XML.</summary>
        public static IReadOnlyList<string> ParseSymbolClassNamesFromXml(string xml)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(xml)) return names;

            var searchFrom = 0;
            while (true)
            {
                var typeIdx = xml.IndexOf("SymbolClassTag", searchFrom, StringComparison.OrdinalIgnoreCase);
                if (typeIdx < 0) break;
                var itemStart = xml.LastIndexOf("<item", typeIdx, StringComparison.OrdinalIgnoreCase);
                if (itemStart < 0)
                {
                    searchFrom = typeIdx + 1;
                    continue;
                }

                var itemEnd = xml.IndexOf("</item>", itemStart, StringComparison.OrdinalIgnoreCase);
                // Nested structure: find names block inside this SymbolClass tag region.
                // Prefer scanning forward to next SymbolClassTag or a generous window.
                var nextType = xml.IndexOf("SymbolClassTag", typeIdx + 1, StringComparison.OrdinalIgnoreCase);
                var regionEnd = nextType > typeIdx ? nextType : Math.Min(xml.Length, typeIdx + 200000);
                var region = xml.Substring(itemStart, Math.Max(0, regionEnd - itemStart));
                searchFrom = typeIdx + 1;

                var namesOpen = region.IndexOf("<names>", StringComparison.OrdinalIgnoreCase);
                var namesClose = region.IndexOf("</names>", StringComparison.OrdinalIgnoreCase);
                if (namesOpen < 0 || namesClose <= namesOpen) continue;

                foreach (Match nm in Regex.Matches(
                             region.Substring(namesOpen, namesClose - namesOpen),
                             @"<item>([^<]+)</item>",
                             RegexOptions.IgnoreCase))
                {
                    var name = XmlUnescape(nm.Groups[1].Value.Trim());
                    if (name.Length > 0)
                        names.Add(name);
                }

                // silence unused
                _ = itemEnd;
            }

            return names;
        }

        /// <summary>
        /// Fails the clone when a bare SymbolClass name still equals an old identity root.
        /// </summary>
        public static void VerifyIdentityRootsRewritten(
            string rewrittenXml,
            IReadOnlyList<string> oldRoots,
            string newName,
            IReadOnlyList<(string From, string To)>? plan = null)
        {
            if (string.IsNullOrEmpty(rewrittenXml))
                throw new InvalidOperationException("Rewritten XML is empty.");

            VerifyIdentityRootsRewritten(ParseSymbolClassNamesFromXml(rewrittenXml), oldRoots, newName, plan);
        }

        public static void VerifyIdentityRootsRewritten(
            IReadOnlyList<string> symbolNames,
            IReadOnlyList<string> oldRoots,
            string newName,
            IReadOnlyList<(string From, string To)>? plan = null)
        {
            if (symbolNames == null || symbolNames.Count == 0)
            {
                // Some FFDec dumps omit SymbolClassTag structure. CloneAsync already
                // requires the new name to appear in the rewritten XML.
                return;
            }

            foreach (var root in oldRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (root.Equals(newName, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsEngineTimelineId(root)) continue;

                var stillBare = symbolNames.Any(n => n.Equals(root, StringComparison.Ordinal));
                if (stillBare)
                {
                    throw new InvalidOperationException(
                        $"Verification failed: SymbolClass still lists bare identity \"{root}\". " +
                        "Filename/stem rewrite alone is not enough — linkage_id (or related) was not renamed.");
                }
            }

            var aliasedBody = newName + "_black_mage";
            if (symbolNames.Any(n => n.Equals(aliasedBody, StringComparison.Ordinal) ||
                                       n.Equals(newName + "_blackmage", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Verification failed: SymbolClass still lists donor body alias \"{aliasedBody}\". " +
                    "Host linkage must be the ident only, not {ident}_black_mage.");
            }

            var expectedBodies = new HashSet<string>(StringComparer.Ordinal) { newName };
            if (plan != null)
            {
                foreach (var (_, to) in plan)
                {
                    if (!string.IsNullOrWhiteSpace(to))
                        expectedBodies.Add(to);
                }
            }

            if (!symbolNames.Any(n => expectedBodies.Contains(n) ||
                                      expectedBodies.Any(b => n.StartsWith(b + "_", StringComparison.Ordinal))))
            {
                // Soft: newName may only appear inside package paths; CloneAsync already requires newName in XML.
            }
        }

        /// <summary>
        /// Counts identifier-safe occurrences of <paramref name="needle"/> (same rules as rewrite).
        /// </summary>
        public static int CountIdentifierOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return 0;
            var count = 0;
            var pos = 0;
            while (pos < haystack.Length)
            {
                if (IdentifierMatchesAt(haystack.AsSpan(), pos, needle, null))
                {
                    count++;
                    pos += needle.Length;
                }
                else
                    pos++;
            }

            return count;
        }

        /// <summary>
        /// True when <paramref name="token"/> is an identifier-safe match at
        /// <paramref name="index"/> (same rules as the clone rewrite regex).
        /// When <paramref name="index"/> is 0, <paramref name="previousChar"/> is the
        /// already-emitted character before this window (lookbehind).
        /// </summary>
        public static bool IdentifierMatchesAt(
            ReadOnlySpan<char> text,
            int index,
            string token,
            char? previousChar)
        {
            if (index < 0 || string.IsNullOrEmpty(token) || index + token.Length > text.Length)
                return false;
            if (!text.Slice(index, token.Length).Equals(token.AsSpan(), StringComparison.Ordinal))
                return false;

            if (IsInsideProtectedBgmId(text, index, token, previousChar))
                return false;

            char? left = index > 0 ? text[index - 1] : previousChar;
            if (left is char lc && IsAsciiAlnum(lc))
                return false;

            var after = index + token.Length;
            if (after >= text.Length)
                return true;
            return IsIdentifierRightBoundaryOk(token, text[after]);
        }

        public static bool IsDisplayNameGuardKey(string? value) =>
            !string.IsNullOrEmpty(value) && DisplayNameGuardKeys.Contains(value);

        private static bool IsInsideProtectedBgmId(
            ReadOnlySpan<char> text,
            int index,
            string token,
            char? previousChar)
        {
            if (token.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                return false;

            var start = index;
            while (start > 0)
            {
                var c = text[start - 1];
                if (!(IsAsciiAlnum(c) || c == '_'))
                    break;
                start--;
            }

            if (index - start >= 4 &&
                text.Slice(start, 4).Equals("bgm_".AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;

            if (start == 0 && previousChar is char pc && (IsAsciiAlnum(pc) || pc == '_'))
            {
                var take = Math.Min(3, index);
                var seen = pc + (take > 0 ? text.Slice(0, take).ToString() : "");
                if (seen.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsAsciiAlnum(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

        private static bool IsIdentifierRightBoundaryOk(string token, char next)
        {
            var letters = 0;
            var upper = 0;
            var lower = 0;
            for (var i = 0; i < token.Length; i++)
            {
                var c = token[i];
                if (c >= 'A' && c <= 'Z')
                {
                    letters++;
                    upper++;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    letters++;
                    lower++;
                }
            }

            if (letters == 0 || upper == letters || lower == letters || upper == 0)
                return !IsAsciiAlnum(next);
            if (upper > 0 && lower > 0)
                return next is < 'a' or > 'z';
            return !IsAsciiAlnum(next);
        }

        private static string ReplaceIdentifierToken(string content, string from, string to)
        {
            return CreateIdentifierRegex(from).Replace(content, MatchEvaluator);

            string MatchEvaluator(Match _) => to;
        }

        private static Regex CreateIdentifierRegex(string token)
        {
            var escaped = Regex.Escape(token);
            // All-lowercase / non-letter tokens: strict alnum boundaries
            // (link matches link_fla and "link", but not linkage_id).
            // PascalCase / mixed: allow next char to be uppercase (LinkExt) but not lowercase (Linkage).
            // ALL-CAPS: strict alnum boundaries (avoid LINKAGE → LINK_CLONEAGE).
            var letters = 0;
            var upper = 0;
            var lower = 0;
            foreach (var c in token)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    letters++;
                    upper++;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    letters++;
                    lower++;
                }
            }

            var allUpper = letters > 0 && upper == letters;
            var allLower = letters > 0 && lower == letters;
            var hasUpper = upper > 0;
            var hasLower = lower > 0;

            string pattern;
            if (allUpper || allLower || !hasUpper)
            {
                pattern = $@"(?<![A-Za-z0-9]){escaped}(?![A-Za-z0-9])";
            }
            else if (hasUpper && hasLower)
            {
                // Pascal or camel: LinkExt yes, Linkage no
                pattern = $@"(?<![A-Za-z0-9]){escaped}(?![a-z])";
            }
            else
            {
                pattern = $@"(?<![A-Za-z0-9]){escaped}(?![A-Za-z0-9])";
            }

            return new Regex(pattern, RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Sets character id string literals to "expansion" inside SWF XML / AS-like text.
        /// </summary>
        public static string ApplyExpansionIdInXml(string xml, string characterId)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(characterId))
                return xml;

            var pattern = new Regex(
                $@"(\bid\b(?:\s*:\s*[\w.]+)?\s*=\s*)([""']){Regex.Escape(characterId)}\2",
                RegexOptions.CultureInvariant);

            var result = pattern.Replace(xml, m =>
                m.Groups[1].Value + m.Groups[2].Value + "expansion" + m.Groups[2].Value);

            // register("id", "characterId") / register('id', 'characterId')
            var registerPattern = new Regex(
                $@"(register\s*\(\s*[""']id[""']\s*,\s*)([""']){Regex.Escape(characterId)}\2",
                RegexOptions.CultureInvariant);
            result = registerPattern.Replace(result, m =>
                m.Groups[1].Value + m.Groups[2].Value + "expansion" + m.Groups[2].Value);

            // Constant-pool style: <item>id</item> followed by <item>characterId</item>
            // Only replace when the following value exactly matches the character id.
            var poolPattern = new Regex(
                $@"(<item>id</item>\s*<item>){Regex.Escape(characterId)}(</item>)",
                RegexOptions.CultureInvariant);
            result = poolPattern.Replace(result, m => m.Groups[1].Value + "expansion" + m.Groups[2].Value);

            return result;
        }

        /// <summary>
        /// Reserved object-literal keys that must never be treated as a display-name value.
        /// If the constant-pool item after "displayName" is one of these, the value string was
        /// deduplicated elsewhere in the pool and we cannot safely locate it positionally.
        /// </summary>
        private static readonly HashSet<string> DisplayNameGuardKeys = new(StringComparer.Ordinal)
        {
            "classAPI", "statsName", "displayName", "linkage_id", "linkage_id2",
            "linkage_id_special", "finalSmashCutin", "thumbnail", "seriesIcon", "width", "height",
            "guid", "resources", "characters",
            // Adjacent stats keys often appear as the next constant-pool item when values are deduped.
            "persistent", "normalStats_id", "specialStats_id", "shield_scale", "maxShieldSize", "jumpSpeed",
            "jumpSpeedMidair", "jumpStartup", "shortHopSpeed", "max_jumpSpeed", "groundToAirMultiplier",
            "dodgeSpeed", "dodgeDecel", "gravity", "weight1", "power", "norm_xSpeed", "max_xSpeed",
            "max_ySpeed", "fastFallSpeed", "accel_start", "accel_start_dash", "accel_rate",
            "accel_rate_air", "decel_rate", "decel_rate_air", "holdJump", "max_jump",
            "max_projectile", "tetherGrab", "roll_speed", "roll_delay", "roll_decay",
            "getup_roll_delay", "tech_roll_delay", "climb_roll_delay", "itemScaleRatio",
            "special_type", "sounds", "mcName", "e", "glasses", "visible", "scaleX", "scaleY",
            "tiltTossMultiplier", "revivalEffect", "inheritPalette", "getCharacterStat",
            "fs_time_limit"
        };

        /// <summary>
        /// camelCase stats fields (revivalEffect) that sit in the pool after a deduped
        /// identity key. Pascal symbols (Starfox_Symbol, MarioSymbol) are real linkages.
        /// </summary>
        private static bool IsDedupedStatsNeighbor(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 4) return false;
            if (value.IndexOf('_') >= 0) return false;
            if (!char.IsLower(value[0])) return false;
            for (var i = 1; i < value.Length; i++)
            {
                if (char.IsUpper(value[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Rewrites register("guid", …) and the ABC constant-pool value after the "guid" key
        /// so the SWF PassKey matches mappings. Also replaces leftover exact occurrences of the
        /// previous GUID string when it can be detected.
        /// </summary>
        public static (string xml, int hits) ApplyGuidInXml(string xml, string newGuid)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(newGuid))
                return (xml, 0);

            var guid = newGuid.Trim();
            var hits = 0;
            string? oldGuid = null;

            var poolPattern = new Regex(
                @"(<item>guid</item>\s*<item>)([^<]+)(</item>)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var result = poolPattern.Replace(xml, m =>
            {
                var current = m.Groups[2].Value;
                if (DisplayNameGuardKeys.Contains(current))
                    return m.Value;
                if (!LooksLikeGuid(current) && !string.IsNullOrWhiteSpace(current))
                {
                    // Still replace non-key values that sit after the guid key.
                }
                if (string.IsNullOrEmpty(oldGuid) && !current.Equals(guid, StringComparison.OrdinalIgnoreCase))
                    oldGuid = current;
                if (current.Equals(guid, StringComparison.OrdinalIgnoreCase))
                    return m.Value;
                hits++;
                return m.Groups[1].Value + guid + m.Groups[3].Value;
            });

            var registerPattern = new Regex(
                @"register\s*\(\s*[""']guid[""']\s*,\s*[""']([^""']+)[""']\s*\)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            result = registerPattern.Replace(result, m =>
            {
                var current = m.Groups[1].Value;
                if (string.IsNullOrEmpty(oldGuid) && !current.Equals(guid, StringComparison.OrdinalIgnoreCase))
                    oldGuid = current;
                if (current.Equals(guid, StringComparison.OrdinalIgnoreCase))
                    return m.Value;
                hits++;
                return $"register(\"guid\",\"{guid}\")";
            });

            if (!string.IsNullOrEmpty(oldGuid) &&
                !oldGuid.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                var before = result;
                result = result.Replace(oldGuid, guid, StringComparison.OrdinalIgnoreCase);
                if (!ReferenceEquals(before, result) && !before.Equals(result, StringComparison.Ordinal))
                    hits++;
            }

            return (result, hits);
        }

        private static bool LooksLikeGuid(string value) =>
            System.Guid.TryParse(value?.Trim(), out _);

        /// <summary>
        /// Patches an existing character/music SWF so register("guid") matches <paramref name="newGuid"/>.
        /// </summary>
        public async Task PatchGuidInSwfAsync(
            string swfPath,
            string newGuid,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
                throw new FileNotFoundException("SWF not found.", swfPath);
            if (string.IsNullOrWhiteSpace(newGuid))
                throw new ArgumentException("GUID is required.", nameof(newGuid));

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-guid-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var xmlPath = Path.Combine(work, "work.xml");
                progress?.Report("SWF → XML for GUID patch…");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var (next, hits) = ApplyGuidInXml(xml, newGuid.Trim());
                if (hits == 0)
                    throw new InvalidOperationException("Could not locate a guid field to update in " + Path.GetFileName(swfPath));

                await File.WriteAllTextAsync(
                    xmlPath, next, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                var rebuilt = Path.Combine(work, "patched.swf");
                progress?.Report("XML → SWF…");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, rebuilt, cancellationToken);

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(rebuilt, swfPath, overwrite: true);
                progress?.Report($"GUID set to {newGuid.Trim()} ({hits} touch(es)).");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Sets the in-game display name inside the FFDec SWF XML. In the ABC string constant
        /// pool the display-name value string is emitted immediately after the "displayName"
        /// key (see Main.as object literal). Replaces that value item, guarding against cases
        /// where the value was deduplicated and the following item is another known key.
        /// Returns the rewritten XML and the number of display-name fields updated.
        /// </summary>
        public static (string xml, int hits) ApplyDisplayNameInXml(string xml, string newDisplayName)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(newDisplayName))
                return (xml, 0);

            var escaped = XmlEscape(newDisplayName);
            var hits = 0;

            var pattern = new Regex(
                @"(<item>displayName</item>\s*<item>)(.*?)(</item>)",
                RegexOptions.CultureInvariant | RegexOptions.Singleline);

            var result = pattern.Replace(xml, m =>
            {
                var currentValue = m.Groups[2].Value;
                // Skip when the following item is a known key (value was deduplicated elsewhere).
                if (DisplayNameGuardKeys.Contains(currentValue))
                    return m.Value;
                hits++;
                return m.Groups[1].Value + escaped + m.Groups[3].Value;
            });

            return (result, hits);
        }

        private static string XmlEscape(string value) =>
            value.Replace("&", "&amp;")
                 .Replace("<", "&lt;")
                 .Replace(">", "&gt;");

        public static string SanitizeExpansionFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "expansion";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "");
            name = name.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(name) ? "expansion" : name;
        }

        /// <summary>
        /// Extracts the costume array block for <paramref name="oldName"/> from Misc.as text
        /// and rewrites the key to <paramref name="newName"/>.
        /// </summary>
        public static string? ExtractCostumeBlock(string miscAsText, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(miscAsText) || string.IsNullOrEmpty(oldName))
                return null;

            var initPattern = new Regex(
                $@"_loc1_\s*\[\s*[""']{Regex.Escape(oldName)}[""']\s*\]\s*=\s*new\s+Array\s*\(\s*\)\s*;",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var match = initPattern.Match(miscAsText);
            if (!match.Success) return null;

            var start = match.Index;
            var nextInit = new Regex(
                @"_loc1_\s*\[\s*[""']([^""']+)[""']\s*\]\s*=\s*new\s+Array\s*\(\s*\)\s*;",
                RegexOptions.CultureInvariant);

            var end = miscAsText.Length;
            foreach (Match m in nextInit.Matches(miscAsText, start + match.Length))
            {
                if (!string.Equals(m.Groups[1].Value, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    end = m.Index;
                    break;
                }
            }

            var block = miscAsText.Substring(start, end - start).TrimEnd();
            block = initPattern.Replace(block, "", 1).TrimStart();
            // Rewrite key references from oldName to newName (quoted only).
            block = Regex.Replace(
                block,
                $@"(_loc1_\s*\[\s*[""']){Regex.Escape(oldName)}([""'])",
                m => m.Groups[1].Value + newName + m.Groups[2].Value,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            return block + Environment.NewLine;
        }

        /// <summary>
        /// Like <see cref="ExtractCostumeBlock"/> but also rewrites any additional costume
        /// array keys in <paramref name="alsoRewriteKeys"/> to <paramref name="newName"/>.
        /// </summary>
        public static string? ExtractCostumeBlock(
            string miscAsText,
            string oldName,
            string newName,
            IEnumerable<string>? alsoRewriteKeys)
        {
            var block = ExtractCostumeBlock(miscAsText, oldName, newName);
            if (block == null && alsoRewriteKeys != null)
            {
                foreach (var key in alsoRewriteKeys)
                {
                    if (string.IsNullOrWhiteSpace(key) ||
                        key.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    block = ExtractCostumeBlock(miscAsText, key.Trim(), newName);
                    if (block != null) break;
                }
            }

            if (block == null || alsoRewriteKeys == null) return block;

            foreach (var key in alsoRewriteKeys)
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    key.Equals(newName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                block = Regex.Replace(
                    block,
                    $@"(_loc1_\s*\[\s*[""']){Regex.Escape(key.Trim())}([""'])",
                    m => m.Groups[1].Value + newName + m.Groups[2].Value,
                    RegexOptions.CultureInvariant);
            }

            return block;
        }

        public static string CreateStubCostumes(string newName) =>
            $"_loc1_[\"{newName}\"].push({{ \"base\":true, \"paletteSwap\":{{ \"colors\":[], \"replacements\":[] }}, \"paletteSwapPA\":{{ \"colors\":[], \"replacements\":[] }} }});{Environment.NewLine}";

        /// <summary>
        /// Live packs load a JSON array. Convert Misc.as push blocks (or pass-through JSON).
        /// </summary>
        public static string ToCostumesJson(string? asBlockOrJson, string newName)
        {
            var json = LivePackRuntimeService.CostumeBlockToJsonArray(asBlockOrJson ?? "");
            if (json == "[]")
                json = LivePackRuntimeService.CostumeBlockToJsonArray(CreateStubCostumes(newName));
            if (json == "[]")
            {
                json = """
                    [
                      {
                        "base": true,
                        "paletteSwap": { "colors": [], "replacements": [] },
                        "paletteSwapPA": { "colors": [], "replacements": [] }
                      }
                    ]
                    """;
            }
            return json;
        }

        /// <summary>Minimal valid 1×1 transparent PNG.</summary>
        public static byte[] CreatePlaceholderPngBytes() =>
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO5W5aUAAAAASUVORK5CYII=");

        private async Task UntypeFlaInstanceSlotsOnCloneAsync(
            string? jpexsPath,
            string rebuiltSwf,
            string workRoot,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report("Untyping *_fla instance slots to MovieClip…");
            var dest = Path.Combine(workRoot, "untyped.swf");
            await _jpexs.UntypeFlaInstanceSlotsAsync(jpexsPath ?? "", rebuiltSwf, dest, cancellationToken);
            if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
                throw new InvalidOperationException("FFDec untype-stance did not produce a SWF.");
            File.Copy(dest, rebuiltSwf, overwrite: true);
        }


        public static int RewriteSymbolClassFile(string csvPath, string oldName, string newName)
        {
            var original = File.ReadAllText(csvPath);
            var rewritten = RewriteText(original, oldName, newName);
            if (rewritten == original) return 0;

            File.WriteAllText(csvPath, rewritten, Encoding.UTF8);
            return CountIdentifierOccurrences(original, oldName);
        }

        public static (int filesTouched, int replacements) RewriteScriptsFolder(
            string scriptsFolder, string oldName, string newName)
        {
            var filesTouched = 0;
            var replacements = 0;
            var files = Directory.GetFiles(scriptsFolder, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return ext.Equals(".as", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".pcode", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var file in files)
            {
                var original = File.ReadAllText(file);
                var rewritten = RewriteText(original, oldName, newName);
                if (rewritten != original)
                {
                    File.WriteAllText(file, rewritten, Encoding.UTF8);
                    filesTouched++;
                    replacements += CountIdentifierOccurrences(original, oldName);
                }
            }

            // Text-only; do not rename paths (kept for unit tests / helpers).
            return (filesTouched, replacements);
        }

        public static int ApplyExpansionIdInScripts(string scriptsFolder, string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId)) return 0;

            var hits = 0;
            foreach (var file in Directory.GetFiles(scriptsFolder, "*.as", SearchOption.AllDirectories))
            {
                var original = File.ReadAllText(file);
                var rewritten = ApplyExpansionIdInXml(original, characterId);
                if (rewritten == original) continue;
                File.WriteAllText(file, rewritten, Encoding.UTF8);
                hits += Regex.Matches(original,
                    $@"\bid\b(?:\s*:\s*[\w.]+)?\s*=\s*[""']{Regex.Escape(characterId)}[""']").Count;
            }

            return hits;
        }

        public static string SuggestOldNameFromPath(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (name.Equals("data", StringComparison.OrdinalIgnoreCase))
                return "";
            return name;
        }

        public static IReadOnlyList<(string From, string To)> BuildReplacementPairs(string oldName, string newName)
        {
            var pairs = new List<(string, string)>();
            void Add(string from, string to)
            {
                if (!string.IsNullOrEmpty(from) && !pairs.Any(p => p.Item1 == from))
                    pairs.Add((from, to));
            }

            Add(oldName, newName);
            Add(ToPascal(oldName), ToPascal(newName));
            Add(ToCamel(oldName), ToCamel(newName));
            Add(oldName.ToLowerInvariant(), newName.ToLowerInvariant());
            Add(oldName.ToUpperInvariant(), newName.ToUpperInvariant());

            return pairs.OrderByDescending(p => p.Item1.Length).ToList();
        }

        private async Task VerifyOutputContainsClassAsync(
            string jpexsPath,
            string swfPath,
            string workRoot,
            string oldName,
            string newName,
            CancellationToken cancellationToken)
        {
            var verifyDir = Path.Combine(workRoot, "verify");
            Directory.CreateDirectory(verifyDir);

            await _jpexs.ExportAsync(jpexsPath, swfPath, verifyDir, "symbolClass", cancellationToken);

            var csv = FindSymbolClassFile(verifyDir);
            if (csv == null || !File.Exists(csv))
            {
                throw new InvalidOperationException(
                    "Could not re-export SymbolClass from the cloned SWF to verify linkages.");
            }

            var csvText = await File.ReadAllTextAsync(csv, cancellationToken);

            // Document / root class is typically a bare name on its own CSV line: "id;classname"
            var hasNew = csvText.Contains(newName, StringComparison.Ordinal);
            if (!hasNew)
            {
                throw new InvalidOperationException(
                    $"Verification failed: SymbolClass in the cloned SWF does not contain \"{newName}\". " +
                    "The XML rewrite or xml2swf step did not update linkages correctly.");
            }

            // Prefer ensuring the bare document class was renamed (e.g. ";ganondorf" -> ";ganondorf_clone")
            var oldBare = $";{oldName}";
            var newBare = $";{newName}";
            var hasOldBare = csvText.Split('\n', '\r')
                .Any(line =>
                {
                    var parts = line.Split(';');
                    return parts.Length >= 2 &&
                           parts[^1].Trim().Equals(oldName, StringComparison.Ordinal);
                });

            if (hasOldBare && !csvText.Contains(newBare, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Verification failed: document class \"{oldName}\" is still present and \"{newName}\" bare linkage was not found.");
            }

            if (hasOldBare)
            {
                throw new InvalidOperationException(
                    $"Verification failed: SymbolClass still lists bare class \"{oldName}\". " +
                    "ABC/SymbolClass are out of sync — refuse to ship a broken clone.");
            }
        }

        private static string? FindSymbolClassFile(string exportDir)
        {
            var direct = Directory.GetFiles(exportDir, "*.csv", SearchOption.AllDirectories)
                .FirstOrDefault(f =>
                    Path.GetFileName(f).Contains("symbol", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(f).Equals("SymbolClass.csv", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(f).Equals("symbols.csv", StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct;

            return Directory.GetFiles(exportDir, "*.csv", SearchOption.AllDirectories).FirstOrDefault();
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            var count = 0;
            var idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        private static string SanitizeFileToken(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static string ToPascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var parts = Regex.Split(s, @"[^A-Za-z0-9]+")
                .Where(p => p.Length > 0)
                .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : ""))
                .ToArray();
            if (parts.Length == 0)
                return char.ToUpperInvariant(s[0]) + (s.Length > 1 ? s[1..] : "");
            return string.Concat(parts);
        }

        private static string ToCamel(string s)
        {
            var pascal = ToPascal(s);
            if (string.IsNullOrEmpty(pascal)) return pascal;
            return char.ToLowerInvariant(pascal[0]) + pascal[1..];
        }
    }
}
