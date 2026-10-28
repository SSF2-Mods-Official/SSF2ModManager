using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SSF2ModManager.Services
{
    public class GamePrepareFileEntry
    {
        public string DatFileName { get; set; } = "";
        public string DatRelativePath { get; set; } = "";
        public string SwfRelativePath { get; set; } = "";
        public string? Category { get; set; }
        public string FriendlyFileName { get; set; } = "";
    }

    public class GamePrepareState
    {
        public string InstallRoot { get; set; } = "";
        public string MainSwfBackupPath { get; set; } = "";
        public string PreparedAtUtc { get; set; } = "";
        public int ManifestCharacterId { get; set; }
        public string? ManifestKey { get; set; }
        public List<GamePrepareFileEntry> Files { get; set; } = new();
    }

    public class GamePrepareResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int FilesConverted { get; set; }
        public int FilesFailed { get; set; }
        public List<string> Errors { get; set; } = new();
        public GamePrepareState? State { get; set; }
    }

    /// <summary>
    /// Prepares an SSF2 install for modding: patch engine for friendly/decompressed paths,
    /// decompress all DAT files to category/*.swf, with full revert support.
    /// </summary>
    public class GamePrepareService
    {
        public const string StateFileName = ".ssf2mm-pressor-state.json";
        public const string MainSwfBackupSuffix = ".ssf2mm.bak";

        public const string ScriptNameConfig = "com.mcleodgaming.ssf2.Config";
        public const string ScriptNameMain = "com.mcleodgaming.ssf2.Main";
        public const string ScriptNameResource = "com.mcleodgaming.ssf2.util.Resource";
        public const string ScriptNameCharacterSelectMenu = "com.mcleodgaming.ssf2.menus.CharacterSelectMenu";
        public const string ScriptNameStageSelectMenu = "com.mcleodgaming.ssf2.menus.StageSelectMenu";
        public const string ScriptNameGamepadManager = "com.mcleodgaming.ssf2.input.GamepadManager";
        /// <summary>Marker in GamepadManager for ADL-safe NativeJoystick fallback.</summary>
        public const string GamepadAdlSafeMarker = "SSF2MM_GAMEPAD_ADL_SAFE";

        /// <summary>Marker so re-patch is idempotent (bump when injection body changes).</summary>
        public const string SelectScaleMarker = "SSF2MM_SELECT_SCALE_V2";
        public const string CssRosterHashMarker = "SSF2MM_CSS_HASH_BY_ROSTER";
        public const string ContentPacksMarker = "SSF2MM_CONTENT_PACKS";
        /// <summary>Live pack PNG/label bind on CSS chips (Milestone 1).</summary>
        public const string ContentPackArtMarker = "SSF2MM_PACK_ART";
        /// <summary>Inject-once live UI helpers (real chip chrome + masked select.png + EditText name).</summary>
        public const string ContentPackLiveUiMarker = "SSF2MM_LIVE_UI_V14";

        /// <summary>Debug console bridge marker in SSF2API.print / Main uncaught handler.</summary>
        public const string DebugConsoleMarker = "SSF2MM_DEBUG_CONSOLE_V1";

        /// <summary>
        /// Durable V2 marker (FFDec strips // and /* */ comments on replaceAS3).
        /// Present in the hello line / loadPolicyFile URL path.
        /// </summary>
        public const string DebugConsoleMarkerV2 = "SSF2MM_CONNECTED";

        /// <summary>Localhost port the Mod Manager debug console listens on.</summary>
        public const int DebugConsolePort = 39333;

        private readonly JpexsCliService _jpexs;
        private readonly DatMappingService _datMapping;
        private readonly PressorService _pressor;

        public GamePrepareService(
            JpexsCliService? jpexs = null,
            DatMappingService? datMapping = null,
            PressorService? pressor = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _datMapping = datMapping ?? new DatMappingService(_jpexs);
            _pressor = pressor ?? new PressorService();
        }

        public static string GetStatePath(string installRoot) =>
            Path.Combine(installRoot, StateFileName);

        public static bool IsPrepared(string installRoot) =>
            File.Exists(GetStatePath(installRoot));

        public GamePrepareState? LoadState(string installRoot)
        {
            var path = GetStatePath(installRoot);
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<GamePrepareState>(File.ReadAllText(path));
        }

        public async Task<GamePrepareResult> PrepareAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 install folder not found.");

            if (IsPrepared(installRoot))
                throw new InvalidOperationException(
                    "This install is already prepared. Use Revert to DAT first, or re-click Prepare to repair the engine patch.");

            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");
            var dataDir = Path.Combine(installRoot, "data");
            if (!Directory.Exists(dataDir))
                throw new DirectoryNotFoundException("Install is missing a data/ folder.");

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-prepare-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                // One shared FFDec export for mappings + engine patches (avoids a second full SSF2 dump).
                progress?.Report("Exporting SSF2.swf (scripts + manifest)…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(
                    jpexsPath ?? "", mainSwf, exportDir, "script,binaryData", cancellationToken);

                progress?.Report("Loading DAT mappings…");
                var entries = await _datMapping.LoadMappingsFromMainExportAsync(
                    installRoot, exportDir, jpexsPath, progress, cancellationToken);
                if (entries.Count == 0)
                    throw new InvalidOperationException("No DAT mappings were found.");

                var backupPath = mainSwf + MainSwfBackupSuffix;
                progress?.Report("Backing up SSF2.swf…");
                File.Copy(mainSwf, backupPath, overwrite: true);

                var scriptsDir = Path.Combine(exportDir, "scripts");
                if (!Directory.Exists(scriptsDir))
                    scriptsDir = exportDir;

                progress?.Report("Patching engine (encrypt_files=false, safe raw SWF loads)…");
                var patchedScriptsSwf = Path.Combine(work, "ssf2-scripts.swf");
                await ApplyEnginePatchesSurgicallyAsync(
                    jpexsPath,
                    sourceSwf: backupPath,
                    scriptsDir,
                    patchedScriptsSwf,
                    progress,
                    cancellationToken);

                var (manifestId, manifestKey, rewrittenBin) = RewriteManifestBinary(exportDir, progress);
                var patchedFinal = Path.Combine(work, "ssf2-final.swf");
                if (manifestId > 0 && rewrittenBin != null)
                {
                    progress?.Report($"Updating embedded manifest (id {manifestId})…");
                    var newBinPath = Path.Combine(work, "manifest-rewritten.bin");
                    await File.WriteAllBytesAsync(newBinPath, rewrittenBin, cancellationToken);
                    await _jpexs.ReplaceAsync(
                        jpexsPath ?? "",
                        patchedScriptsSwf,
                        patchedFinal,
                        manifestId.ToString(),
                        newBinPath,
                        cancellationToken);
                }
                else
                {
                    progress?.Report("Warning: could not rewrite manifest binary; copying script-patched SWF.");
                    File.Copy(patchedScriptsSwf, patchedFinal, overwrite: true);
                }

                File.Copy(patchedFinal, mainSwf, overwrite: true);

                var state = new GamePrepareState
                {
                    InstallRoot = installRoot,
                    MainSwfBackupPath = backupPath,
                    PreparedAtUtc = DateTime.UtcNow.ToString("o"),
                    ManifestCharacterId = manifestId,
                    ManifestKey = manifestKey
                };

                progress?.Report($"Decompressing {entries.Count} mapped DAT file(s) in parallel…");
                var preview = _datMapping.BuildRenamePreview(installRoot, entries);
                var jobs = new List<(DatRenamePreview Preview, DatMappingEntry Mapping)>();
                var byDat = entries
                    .GroupBy(e => e.DatFileName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var p in preview)
                {
                    if (!p.DatExists) continue;
                    var datName = Path.GetFileName(p.DatFullPath);
                    if (!byDat.TryGetValue(datName, out var mapping)) continue;
                    jobs.Add((p, mapping));
                }

                var converted = new ConcurrentBag<GamePrepareFileEntry>();
                var errors = new ConcurrentBag<string>();
                var done = 0;
                var total = jobs.Count;
                var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);

                await Parallel.ForEachAsync(
                    jobs,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = parallelism,
                        CancellationToken = cancellationToken
                    },
                    async (job, ct) =>
                    {
                        var p = job.Preview;
                        var mapping = job.Mapping;
                        var datName = Path.GetFileName(p.DatFullPath);
                        var swfRelative = DatMappingService.FriendlySwfRelativePath(mapping);
                        var swfFull = Path.Combine(dataDir, swfRelative);
                        try
                        {
                            var wrapped = await File.ReadAllBytesAsync(p.DatFullPath, ct);
                            var swfBytes = _pressor.Decompress(wrapped, out _);
                            var dir = Path.GetDirectoryName(swfFull);
                            if (!string.IsNullOrEmpty(dir))
                                Directory.CreateDirectory(dir);
                            await File.WriteAllBytesAsync(swfFull, swfBytes, ct);
                            File.Delete(p.DatFullPath);

                            converted.Add(new GamePrepareFileEntry
                            {
                                DatFileName = datName,
                                DatRelativePath = Path.GetRelativePath(installRoot, p.DatFullPath),
                                SwfRelativePath = Path.GetRelativePath(installRoot, swfFull),
                                Category = mapping.Category,
                                FriendlyFileName = Path.GetFileName(swfRelative)
                            });

                            var n = Interlocked.Increment(ref done);
                            if (n == total || n % 25 == 0)
                                progress?.Report($"  Decompressed {n}/{total}…");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{datName}: {ex.Message}");
                        }
                    });

                // Leftover DATs not in mappings (e.g. official DAT223.ssf) — decompress in place to .swf.
                var leftoverDats = Directory.GetFiles(dataDir, "DAT*.ssf", SearchOption.TopDirectoryOnly);
                if (leftoverDats.Length > 0)
                {
                    progress?.Report($"Decompressing {leftoverDats.Length} unmapped leftover DAT(s)…");
                    foreach (var datPath in leftoverDats)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var datName = Path.GetFileName(datPath);
                        var swfFull = Path.ChangeExtension(datPath, ".swf")!;
                        try
                        {
                            var wrapped = await File.ReadAllBytesAsync(datPath, cancellationToken);
                            var swfBytes = _pressor.Decompress(wrapped, out _);
                            await File.WriteAllBytesAsync(swfFull, swfBytes, cancellationToken);
                            File.Delete(datPath);
                            converted.Add(new GamePrepareFileEntry
                            {
                                DatFileName = datName,
                                DatRelativePath = Path.GetRelativePath(installRoot, datPath),
                                SwfRelativePath = Path.GetRelativePath(installRoot, swfFull),
                                Category = "unmapped",
                                FriendlyFileName = Path.GetFileName(swfFull)
                            });
                            progress?.Report($"  {datName} → {Path.GetFileName(swfFull)} (unmapped leftover)");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{datName}: {ex.Message}");
                        }
                    }
                }

                // Repair any orphan DAT###.swf that still has a Pressor header stuck on it.
                RepairOrphanDatSwfs(dataDir, progress);

                state.Files = converted.OrderBy(f => f.DatFileName, StringComparer.OrdinalIgnoreCase).ToList();
                result.FilesConverted = state.Files.Count;
                result.FilesFailed = errors.Count;
                result.Errors = errors.ToList();

                await File.WriteAllTextAsync(
                    GetStatePath(installRoot),
                    JsonConvert.SerializeObject(state, Formatting.Indented),
                    cancellationToken);

                var mappingsEntry = state.Files.FirstOrDefault(f =>
                    f.FriendlyFileName.Equals("mappings.swf", StringComparison.OrdinalIgnoreCase) ||
                    (f.Category?.Equals("misc", StringComparison.OrdinalIgnoreCase) == true &&
                     f.FriendlyFileName.Contains("mapping", StringComparison.OrdinalIgnoreCase)));
                if (mappingsEntry == null)
                    throw new InvalidOperationException(
                        "mappings.swf was not produced; character/stage paths would stay broken.");

                var mappingsSwf = Path.Combine(installRoot, mappingsEntry.SwfRelativePath);
                progress?.Report("Updating on-disk mappings metadata to .swf…");
                await RewriteMappingsSwfFileFieldsAsync(
                    mappingsSwf, jpexsPath, work, progress, cancellationToken);

                try
                {
                    progress?.Report("Exporting editable mappings.json…");
                    var mapEdit = new MappingsEditService(_jpexs);
                    var jsonPath = await mapEdit.ExportPlaintextAsync(
                        installRoot, jpexsPath, progress, cancellationToken);
                    progress?.Report($"Mappings editor file: {jsonPath}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: could not export mappings.json: {ex.Message}");
                }

                try
                {
                    var slots = new MenuSlotService(_jpexs);
                    await slots.ClearBtnGameModeTextsAsync(installRoot, jpexsPath, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: could not clear btn_gameMode text: {ex.Message}");
                }

                try
                {
                    // Character slot pool only — stage pool deferred until character live-art is solid.
                    // Also deploys debug host + Main.DEBUG + SSF2API CMD spawn.
                    await EnsureContentPackRuntimeAsync(
                        installRoot, jpexsPath, progress, cancellationToken,
                        characterSlots: SlotPoolService.DefaultCharacterSlots,
                        stageSlots: 0);
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: content pack runtime setup: {ex.Message}");
                }

                result.State = state;
                result.Success = result.FilesFailed == 0;
                result.Message = result.Success
                    ? $"Prepared {result.FilesConverted} file(s). Engine patched for decompressed .swf paths."
                    : $"Prepared with {result.FilesFailed} failure(s); {result.FilesConverted} ok.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Restores SSF2.swf from the prepare backup (original ABC), then surgically replaces
        /// Config / Main / Resource / CharacterSelectMenu / StageSelectMenu.
        /// Avoids FFDec bulk -importScript which rewrites Character/StageData.
        /// </summary>
        public async Task<GamePrepareResult> RepairEngineAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            if (!IsPrepared(installRoot))
                throw new InvalidOperationException("Install is not prepared.");

            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            var state = LoadState(installRoot);
            var backupPath = state?.MainSwfBackupPath;
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                backupPath = mainSwf + MainSwfBackupSuffix;
            if (!File.Exists(backupPath))
                throw new FileNotFoundException(
                    "SSF2.swf backup (.ssf2mm.bak) not found. Cannot restore original engine ABC.",
                    backupPath);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-repair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Restoring SSF2.swf from backup (original Character/StageData ABC)…");
                var restored = Path.Combine(work, "ssf2-restored.swf");
                File.Copy(backupPath, restored, overwrite: true);

                progress?.Report("Exporting scripts from backup…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(
                    jpexsPath ?? "", restored, exportDir, "script,binaryData", cancellationToken);

                var scriptsDir = Path.Combine(exportDir, "scripts");
                if (!Directory.Exists(scriptsDir))
                    scriptsDir = exportDir;

                var patchedScripts = Path.Combine(work, "ssf2-scripts.swf");
                await ApplyEnginePatchesSurgicallyAsync(
                    jpexsPath, restored, scriptsDir, patchedScripts, progress, cancellationToken);

                // Re-apply .swf manifest rewrite if we can (prepared installs expect .swf file fields).
                var (manifestId, _, rewrittenBin) = RewriteManifestBinary(exportDir, progress);
                var patchedFinal = Path.Combine(work, "ssf2-final.swf");
                if (manifestId > 0 && rewrittenBin != null)
                {
                    progress?.Report($"Updating embedded manifest (id {manifestId})…");
                    var newBinPath = Path.Combine(work, "manifest-rewritten.bin");
                    await File.WriteAllBytesAsync(newBinPath, rewrittenBin, cancellationToken);
                    await _jpexs.ReplaceAsync(
                        jpexsPath ?? "",
                        patchedScripts,
                        patchedFinal,
                        manifestId.ToString(),
                        newBinPath,
                        cancellationToken);
                }
                else
                {
                    File.Copy(patchedScripts, patchedFinal, overwrite: true);
                }

                File.Copy(patchedFinal, mainSwf, overwrite: true);
                RepairOrphanDatSwfs(Path.Combine(installRoot, "data"), progress);

                try
                {
                    var slots = new MenuSlotService(_jpexs);
                    await slots.ClearBtnGameModeTextsAsync(installRoot, jpexsPath, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: could not clear btn_gameMode text: {ex.Message}");
                }

                try
                {
                    // Force rebuild of pack patch after restore-from-backup.
                    var ready = SlotPoolService.ReadyPath(installRoot);
                    // Keep existing slot pool; only refresh engine pack patch + debug console.
                    await PatchContentPackRuntimeOnInstallAsync(installRoot, jpexsPath, progress, cancellationToken);
                    if (!SlotPoolService.IsReady(installRoot))
                    {
                        var pool = new SlotPoolService(new MenuSlotService(_jpexs));
                        await pool.EnsureSlotPoolAsync(installRoot, jpexsPath, progress, cancellationToken);
                    }
                    if (GetDebugConsolePatchStatus(installRoot) == DebugConsolePatchStatus.Missing)
                        await InstallDebugConsoleAsync(installRoot, jpexsPath, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: content pack runtime after repair: {ex.Message}");
                }

                result.Success = true;
                result.Message =
                    "Engine restored from backup and surgically patched (Config/Main/Resource/select scale/CSS roster hash/content packs). Try CSS/SSS.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Restore stock menu/misc hosts from first-write <c>.ssf2mm.bak</c> backups when present.
        /// Slot pool must be rebuilt afterward via <see cref="EnsureContentPackRuntimeAsync"/>.
        /// </summary>
        public Task<GamePrepareResult> HealStockHostsFromBackupAsync(
            string installRoot,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            var healed = 0;
            var missing = 0;
            string[] hosts =
            {
                Path.Combine(installRoot, "data", "menu", "menu_characters.swf"),
                Path.Combine(installRoot, "data", "menu", "menu_stages.swf"),
                Path.Combine(installRoot, "data", "misc", "misc2.swf"),
                Path.Combine(installRoot, "data", "misc", "misc.swf")
            };
            foreach (var host in hosts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bak = host + MainSwfBackupSuffix;
                if (!File.Exists(bak))
                {
                    missing++;
                    progress?.Report($"No backup for {Path.GetFileName(host)}");
                    continue;
                }
                File.Copy(bak, host, overwrite: true);
                healed++;
                progress?.Report($"Restored {Path.GetFileName(host)} from .ssf2mm.bak");
            }

            // Slot pool marker is invalid after host restore — clear so Ensure rebuilds.
            try
            {
                var ready = SlotPoolService.ReadyPath(installRoot);
                if (File.Exists(ready))
                    File.Delete(ready);
            }
            catch { /* ignore */ }

            result.Success = healed > 0;
            result.Message = healed > 0
                ? $"Healed {healed} host SWF(s) from backup. Re-run content-pack runtime to rebuild the slot pool."
                : $"No host backups restored ({missing} missing).";
            progress?.Report(result.Message);
            return Task.FromResult(result);
        }

        public const string DebugHostExeName = "ssf2mm-debug-host.exe";
        public const string DebugLogFileName = "ssf2mm_debuglog.txt";
        public const string DebugHostSpawnMarker = "ssf2mmEnsureDebugHost";

        /// <summary>
        /// For an already-prepared install: ensure slot pool, content-pack engine patch, and debug console.
        /// </summary>
        public async Task EnsureContentPackRuntimeAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            int characterSlots = SlotPoolService.DefaultCharacterSlots,
            int stageSlots = SlotPoolService.DefaultStageSlots)
        {
            if (!IsPrepared(installRoot))
                throw new InvalidOperationException("Prepare the install before enabling content packs.");

            ContentPackService.EnsureRoot(installRoot);

            await EnsureDebugSidecarAsync(installRoot, jpexsPath, progress, cancellationToken);

            // Engine pack-boot patch (idempotent).
            progress?.Report("Ensuring content-pack engine patch…");
            await PatchContentPackRuntimeOnInstallAsync(installRoot, jpexsPath, progress, cancellationToken);

            try
            {
                progress?.Report("Ensuring Resource.as pack path fix (data/ssf2mm/…)…");
                await EnsureResourcePackPathsOnInstallAsync(installRoot, jpexsPath, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning: Resource pack-path patch: {ex.Message}");
            }

            try
            {
                progress?.Report("Ensuring live pack API (SSF2API UI remap + costumes.json)…");
                await new LivePackRuntimeService(_jpexs).EnsureLivePackApiAsync(
                    installRoot, jpexsPath, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning: live pack API patch: {ex.Message}");
            }

            // One-time CSS + PA/stock (+ optional SSS) slot pool.
            var pool = new SlotPoolService(new MenuSlotService(_jpexs));
            await pool.EnsureSlotPoolAsync(
                installRoot, jpexsPath, progress, cancellationToken,
                characterSlots, stageSlots);

            progress?.Report("Content pack runtime ready.");
        }

        /// <summary>
        /// Deploy debug host exe, enable Main.DEBUG, and patch SSF2API to spawn the CMD console + log file.
        /// </summary>
        public async Task EnsureDebugSidecarAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report("Deploying ssf2mm-debug-host.exe…");
            DeployDebugHost(installRoot, progress);

            try
            {
                var status = GetDebugConsolePatchStatus(installRoot);
                var hasSpawn = SwfContainsAscii(FindMainSwf(installRoot), DebugHostSpawnMarker);
                if (status == DebugConsolePatchStatus.Missing || !hasSpawn)
                {
                    progress?.Report("Installing / upgrading debug console bridge (CMD spawn + traces)…");
                    await InstallDebugConsoleAsync(installRoot, jpexsPath, progress, cancellationToken, force: true);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning: debug console patch: {ex.Message}");
            }

            try
            {
                progress?.Report("Enabling Main.DEBUG (ADL-style error visibility)…");
                await EnableInGameDebugFlagAsync(installRoot, jpexsPath, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning: Main.DEBUG enable: {ex.Message}");
            }

            try
            {
                progress?.Report("Patching GamepadManager for ADL (NativeJoystick optional)…");
                await PatchGamepadManagerForAdlAsync(installRoot, jpexsPath, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning: GamepadManager ADL patch: {ex.Message}");
            }

            WriteDebugLaunchers(installRoot, progress);
        }

        public static void DeployDebugHost(string installRoot, IProgress<string>? progress = null)
        {
            // Only the single self-contained host exe — never a publish folder / Mod Manager tree.
            RemoveInstallJunk(installRoot, progress);

            var dest = Path.Combine(installRoot, DebugHostExeName);
            var src = ResolveDebugHostSource();
            if (src == null)
            {
                progress?.Report(
                    "Warning: ssf2mm-debug-host.exe not found next to Mod Manager — " +
                    "build tools/Ssf2DebugHost and re-Prepare, or copy the exe into the install folder.");
                return;
            }

            try
            {
                if (!File.Exists(src) || Directory.Exists(src))
                {
                    progress?.Report("Warning: debug host source is not a single file — skipped.");
                    return;
                }
                File.Copy(src, dest, overwrite: true);
                progress?.Report("Deployed " + DebugHostExeName + " only");
            }
            catch (Exception ex)
            {
                progress?.Report("Warning: could not deploy debug host: " + ex.Message);
            }
        }

        /// <summary>
        /// Removes accidental Mod Manager / tooling debris from an install root
        /// (never delete game files, packs, or the debug host itself).
        /// </summary>
        public static void RemoveInstallJunk(string installRoot, IProgress<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                return;

            string[] junkFiles =
            {
                "SSF2ModManager.exe", "SSF2ModManager.dll", "SSF2ModManager.pdb",
                "SSF2ModManager.deps.json", "SSF2ModManager.runtimeconfig.json",
                "Markdig.dll", "Newtonsoft.Json.dll", "SharpCompress.dll", "ZstdSharp.dll",
                "Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll"
            };
            string[] junkDirs = { "ffdec", "ffmpeg", "jre", "Languages", "News", "Themes" };

            foreach (var name in junkFiles)
            {
                var path = Path.Combine(installRoot, name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        progress?.Report("Removed install junk: " + name);
                    }
                }
                catch { /* ignore */ }
            }
            foreach (var name in junkDirs)
            {
                var path = Path.Combine(installRoot, name);
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                        progress?.Report("Removed install junk folder: " + name);
                    }
                }
                catch { /* ignore */ }
            }
        }

        public static string? ResolveDebugHostSource()
        {
            var candidates = new List<string>();
            try
            {
                foreach (var baseDir in new[] { AppPaths.ExeDirectory, AppContext.BaseDirectory })
                {
                    candidates.Add(Path.Combine(baseDir, DebugHostExeName));
                    candidates.Add(Path.Combine(baseDir, "tools", "Ssf2DebugHost", DebugHostExeName));
                }
            }
            catch { /* ignore */ }

            // Dev tree: tools/Ssf2DebugHost/bin/...
            try
            {
                var dir = AppPaths.ExeDirectory;
                for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Release", "net8.0-windows", "win-x64", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Debug", "net8.0-windows", "win-x64", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Release", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Debug", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Debug", "net8.0-windows10.0.19041.0", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Release", "net8.0-windows10.0.19041.0", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "tools", "Ssf2DebugHost", "bin", "Debug", "net8.0-windows", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "src", "tools", "Ssf2DebugHost", "bin", "Release", "net8.0-windows", "win-x64", "publish", DebugHostExeName));
                    candidates.Add(Path.Combine(dir, "src", "tools", "Ssf2DebugHost", "bin", "Debug", "net8.0-windows", "win-x64", "publish", DebugHostExeName));
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { /* ignore */ }

            foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        public static string? ResolveAdlPath()
        {
            var roots = new List<string>();
            var envSdk = Environment.GetEnvironmentVariable("SSF2_AIR_SDK");
            if (!string.IsNullOrWhiteSpace(envSdk)) roots.Add(envSdk);
            // Bundled aflex_lite under runtime\ (legacy: next to Mod Manager)
            try
            {
                var lite = AppPaths.AflexLiteDirectory;
                if (lite != null) roots.Add(lite);
                foreach (var root in AppPaths.EnumerateSidecarRoots())
                    roots.Add(Path.Combine(root, "aflex_lite"));
            }
            catch { /* ignore */ }
            roots.Add(@"C:\aflex_sdk");
            var flexHome = Environment.GetEnvironmentVariable("FLEX_HOME");
            if (!string.IsNullOrWhiteSpace(flexHome)) roots.Add(flexHome);
            var airHome = Environment.GetEnvironmentVariable("AIR_HOME");
            if (!string.IsNullOrWhiteSpace(airHome)) roots.Add(airHome);

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var adl = Path.Combine(root, "bin", "adl.exe");
                if (File.Exists(adl)) return adl;
            }
            return null;
        }

        private static void WriteDebugLaunchers(string installRoot, IProgress<string>? progress)
        {
            var bat = Path.Combine(installRoot, "SSF2-Debug.bat");
            var adl = ResolveAdlPath();
            var lines = new List<string>
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                "start \"\" \"%~dp0" + DebugHostExeName + "\" --install \"%~dp0\"",
                "timeout /t 1 /nobreak >nul",
                "start \"\" \"%~dp0SSF2.exe\"",
                "exit /b 0"
            };
            File.WriteAllText(bat, string.Join("\r\n", lines));
            progress?.Report("Wrote SSF2-Debug.bat (host + game).");

            var appXml = Path.Combine(installRoot, "META-INF", "AIR", "application.xml");
            if (adl == null || !File.Exists(appXml))
                return;

            // Stock SSF2 ships a 64-bit-only joystick ANE, but Flex's adl.exe is 32-bit and
            // loads the win (x86) runtime → "extension … not supported for Windows-x86".
            // Write a debug descriptor without <extensions> so ADL can actually boot the SWF
            // and surface uncaught errors / Main.DEBUG dialogs.
            string? debugDescRel = null;
            try
            {
                debugDescRel = EnsureAdlDebugApplicationDescriptor(installRoot);
            }
            catch (Exception ex)
            {
                progress?.Report("Warning: ADL debug descriptor: " + ex.Message);
            }

            var adlBat = Path.Combine(installRoot, "SSF2-ADL-Debug.bat");
            var sdkRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(adl)!, ".."));
            var runtimeWin = Path.Combine(sdkRoot, "runtimes", "air", "win");
            var desc = debugDescRel ?? @"META-INF\AIR\application.xml";
            var adlLines = new List<string>
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                "echo SSF2 ADL debug launch",
                "echo Descriptor: " + desc,
                "echo (Joystick ANE disabled under ADL - required so 32-bit adl can boot.)",
                "start \"\" \"%~dp0" + DebugHostExeName + "\" --install \"%~dp0\"",
                "timeout /t 1 /nobreak >nul",
                "REM ADL launches with debugging enabled (omit -nodebug).",
                "set ADL=\"" + adl + "\"",
            };
            if (Directory.Exists(runtimeWin))
            {
                adlLines.Add(
                    "%ADL% -runtime \"" + runtimeWin + "\" -profile extendedDesktop " +
                    "\"" + desc + "\" \".\"");
            }
            else
            {
                adlLines.Add(
                    "%ADL% -profile extendedDesktop \"" + desc + "\" \".\"");
            }
            adlLines.Add("set ERR=%ERRORLEVEL%");
            adlLines.Add("if not %ERR%==0 (");
            adlLines.Add("  echo.");
            adlLines.Add("  echo ADL exited with code %ERR%.");
            adlLines.Add("  echo If you see a nativejoystick / Windows-x86 error, re-Prepare to refresh application.adl-debug.xml.");
            adlLines.Add("  pause");
            adlLines.Add(")");
            File.WriteAllText(adlBat, string.Join("\r\n", adlLines) + "\r\n");
            progress?.Report("Wrote SSF2-ADL-Debug.bat using " + adl +
                             (debugDescRel != null ? " + extension-free descriptor" : ""));
        }

        /// <summary>
        /// Copies <c>application.xml</c> to <c>application.adl-debug.xml</c> without
        /// <c>&lt;extensions&gt;</c> so 32-bit ADL can launch the 64-bit packaged game.
        /// </summary>
        public static string EnsureAdlDebugApplicationDescriptor(string installRoot)
        {
            var src = Path.Combine(installRoot, "META-INF", "AIR", "application.xml");
            if (!File.Exists(src))
                throw new FileNotFoundException("AIR application.xml not found.", src);

            var dst = Path.Combine(installRoot, "META-INF", "AIR", "application.adl-debug.xml");
            var text = File.ReadAllText(src);
            // Drop the whole extensions block (nativejoystick ANE is Windows-x86-64 only).
            text = Regex.Replace(
                text,
                @"<extensions\b[^>]*>[\s\S]*?</extensions\s*>",
                "<!-- ssf2mm: extensions omitted for 32-bit ADL -->",
                RegexOptions.IgnoreCase);
            // Optional: architecture tag is fine to leave; ADL win32 still boots with it.
            File.WriteAllText(dst, text);
            return @"META-INF\AIR\application.adl-debug.xml";
        }

        public static bool SwfContainsAscii(string? swfPath, string ascii)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath) || string.IsNullOrEmpty(ascii))
                return false;
            try
            {
                // CWS/ZWS bodies are compressed — scan inflated payload, not raw file bytes.
                var found = false;
                SwfScanAscii(swfPath, (buf, len) =>
                {
                    if (!ContainsAscii(buf, len, ascii)) return false;
                    found = true;
                    return true;
                });
                if (found) return true;
                // FWS / tiny / scan miss: also try raw (covers uncompressed and header-only cases).
                var bytes = File.ReadAllBytes(swfPath);
                return ContainsAscii(bytes, bytes.Length, ascii);
            }
            catch
            {
                return false;
            }
        }

        public async Task<GamePrepareResult> PatchContentPackRuntimeOnInstallAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-pack-rt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Exporting menus for content-pack patch…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(jpexsPath ?? "", mainSwf, exportDir, "script", cancellationToken);
                var scriptsDir = Path.Combine(exportDir, "scripts");
                if (!Directory.Exists(scriptsDir))
                    scriptsDir = exportDir;

                var cssMenuAs = FindScriptFile(scriptsDir, "CharacterSelectMenu.as", preferParentName: "menus")
                    ?? throw new FileNotFoundException("CharacterSelectMenu.as not found.");
                var cssText = File.ReadAllText(cssMenuAs);
                var liveArtOk = cssText.Contains(ContentPackLiveUiMarker, StringComparison.Ordinal)
                                && cssText.Contains("function ssf2mmGetLivePacks", StringComparison.Ordinal)
                                && cssText.Contains("function ssf2mmLoadChipArt", StringComparison.Ordinal)
                                && cssText.Contains("function ssf2mmCapturePortraitHole", StringComparison.Ordinal)
                                && cssText.Contains("function ssf2mmFitChipLabel", StringComparison.Ordinal)
                                && cssText.Contains("ssf2mmBarW", StringComparison.Ordinal)
                                && cssText.Contains("ssf2mmFitScale", StringComparison.Ordinal)
                                && cssText.Contains("artAt", StringComparison.Ordinal)
                                && cssText.Contains("nameBand", StringComparison.Ordinal)
                                && cssText.Contains("cacheAsBitmap", StringComparison.Ordinal)
                                && cssText.Contains("backgroundColor = 0", StringComparison.Ordinal)
                                && !cssText.Contains("scrollRect", StringComparison.Ordinal)
                                && !cssText.Contains("function ssf2mmChipFrameBounds", StringComparison.Ordinal)
                                && cssText.Contains("File.applicationDirectory", StringComparison.Ordinal)
                                && cssText.Contains("ssf2mmArtMask", StringComparison.Ordinal)
                                && !cssText.Contains(".name = \"ssf2mmNameplate\"", StringComparison.Ordinal)
                                && cssText.Contains("loadBytes", StringComparison.Ordinal)
                                && cssText.Contains("import flash.filesystem.File", StringComparison.Ordinal)
                                && cssText.Contains("import com.mcleodgaming.ssf2.api.SSF2API", StringComparison.Ordinal)
                                && cssText.Contains("[SSF2MM_PACK] art OK", StringComparison.Ordinal)
                                && cssText.Contains("this.ssf2mmBindCharacterChip(child", StringComparison.Ordinal);
                var dupHelpers = Regex.Matches(cssText, @"function ssf2mmBindCharacterChip").Count > 1;

                if (liveArtOk && !dupHelpers)
                {
                    var before = cssText;
                    PatchContentPackInCharacterSelect(cssMenuAs); // dedupe call sites if needed
                    if (File.ReadAllText(cssMenuAs).Equals(before, StringComparison.Ordinal))
                    {
                        result.Success = true;
                        result.Message = "Content-pack engine patch already present (live UI).";
                        progress?.Report(result.Message);
                        return result;
                    }
                }
                else if (dupHelpers)
                {
                    progress?.Report("Repairing duplicated content-pack helpers…");
                    var cleaned = StripInjectedPackHelpers(cssText);
                    cleaned = DedupeConsecutiveCalls(cleaned, "this.ssf2mmBindCharacterChip(child,allCharacters[i],mappings);");
                    cleaned = DedupeConsecutiveCalls(cleaned, "this.ssf2mmBindCharacterChip(child, allCharacters[i], mappings);");
                    cleaned = DedupeConsecutiveCalls(cleaned, "this.ssf2mmPackBoot(mappings);");
                    File.WriteAllText(cssMenuAs, cleaned);
                    if (!PatchContentPackInCharacterSelect(cssMenuAs))
                        throw new InvalidOperationException("Content-pack repair patch failed.");
                }
                else if (!PatchContentPackRuntime(scriptsDir))
                {
                    throw new InvalidOperationException("Content-pack patch failed.");
                }

                var sssMenuAs = FindScriptFile(scriptsDir, "StageSelectMenu.as", preferParentName: "menus")
                    ?? throw new FileNotFoundException("StageSelectMenu.as not found.");

                progress?.Report("Replacing CSS/SSS scripts in SSF2.swf…");
                var patched = Path.Combine(work, "ssf2-pack.swf");
                await _jpexs.ReplaceManyAsync(
                    jpexsPath ?? "",
                    mainSwf,
                    patched,
                    new (string, string)[]
                    {
                        (ScriptNameCharacterSelectMenu, cssMenuAs),
                        (ScriptNameStageSelectMenu, sssMenuAs)
                    },
                    cancellationToken);
                if (!File.Exists(patched))
                    throw new InvalidOperationException("FFDec -replace did not produce patched SSF2.swf.");

                var bak = mainSwf + MainSwfBackupSuffix;
                if (!File.Exists(bak))
                    File.Copy(mainSwf, bak, overwrite: false);
                File.Copy(patched, mainSwf, overwrite: true);

                result.Success = true;
                result.Message = "Content-pack engine patch applied.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Patches live <c>SSF2.swf</c> so CSS chips are hashed by roster/row id, allowing
        /// many chips to share one playable <c>characterID</c> (Crazy CSS).
        /// </summary>
        public async Task<GamePrepareResult> PatchCssRosterHashOnInstallAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-css-hash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Exporting CharacterSelectMenu for CSS roster-hash patch…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(jpexsPath ?? "", mainSwf, exportDir, "script", cancellationToken);
                var scriptsDir = Path.Combine(exportDir, "scripts");
                if (!Directory.Exists(scriptsDir))
                    scriptsDir = exportDir;

                var cssMenuAs = FindScriptFile(scriptsDir, "CharacterSelectMenu.as", preferParentName: "menus")
                    ?? throw new FileNotFoundException("CharacterSelectMenu.as not found in FFDec export.");

                if (File.ReadAllText(cssMenuAs).Contains(CssRosterHashMarker, StringComparison.Ordinal))
                {
                    result.Success = true;
                    result.Message = "CSS roster-hash patch already present in SSF2.swf.";
                    progress?.Report(result.Message);
                    return result;
                }

                if (!PatchCharacterSelectRosterHashFile(cssMenuAs))
                    throw new InvalidOperationException(
                        "Could not patch CharacterSelectMenu to hash CSS chips by roster id.");

                progress?.Report("Replacing CharacterSelectMenu in SSF2.swf…");
                var patched = Path.Combine(work, "ssf2-css-hash.swf");
                await _jpexs.ReplaceManyAsync(
                    jpexsPath ?? "",
                    mainSwf,
                    patched,
                    new (string, string)[]
                    {
                        (ScriptNameCharacterSelectMenu, cssMenuAs)
                    },
                    cancellationToken);
                if (!File.Exists(patched))
                    throw new InvalidOperationException("FFDec -replace did not produce patched SSF2.swf.");

                var bak = mainSwf + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(mainSwf, bak, overwrite: false);
                File.Copy(patched, mainSwf, overwrite: true);

                result.Success = true;
                result.Message = "CSS roster-hash patch applied to SSF2.swf.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Patches Config/Main/Resource/select menus in an exported scripts tree, then FFDec -replace
        /// those scripts only into <paramref name="sourceSwf"/>.
        /// </summary>
        public async Task ApplyEnginePatchesSurgicallyAsync(
            string? jpexsPath,
            string sourceSwf,
            string scriptsDir,
            string outfile,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report("Patching Config / Main / Resource / select scale…");
            PatchEncryptFiles(scriptsDir);
            PatchMainEncrypted(scriptsDir);
            if (!PatchResourceAllowDecompressed(scriptsDir))
                throw new InvalidOperationException(
                    "Could not patch Resource.as to safely load decompressed SWFs.");
            if (!PatchResourcePackPaths(scriptsDir))
                throw new InvalidOperationException(
                    "Could not patch Resource.as for content-pack paths under data/ssf2mm/.");
            if (!PatchSelectIconScale(scriptsDir))
                throw new InvalidOperationException(
                    "Could not patch CharacterSelectMenu/StageSelectMenu for icon scaleAll.");
            if (!PatchCharacterSelectRosterHash(scriptsDir))
                throw new InvalidOperationException(
                    "Could not patch CharacterSelectMenu to hash CSS chips by roster id.");
            if (!PatchContentPackRuntime(scriptsDir))
                throw new InvalidOperationException(
                    "Could not patch CharacterSelectMenu/StageSelectMenu for content-pack boot traces.");

            var configAs = FindScriptFile(scriptsDir, "Config.as", preferParentName: "ssf2")
                ?? throw new FileNotFoundException("Config.as not found in FFDec export.");
            var mainAs = FindScriptFile(scriptsDir, "Main.as", preferParentName: "ssf2")
                ?? throw new FileNotFoundException("Main.as not found in FFDec export.");
            var resourceAs = FindScriptFile(scriptsDir, "Resource.as", preferParentName: "util")
                ?? throw new FileNotFoundException("Resource.as not found in FFDec export.");
            var cssMenuAs = FindScriptFile(scriptsDir, "CharacterSelectMenu.as", preferParentName: "menus")
                ?? throw new FileNotFoundException("CharacterSelectMenu.as not found in FFDec export.");
            var sssMenuAs = FindScriptFile(scriptsDir, "StageSelectMenu.as", preferParentName: "menus")
                ?? throw new FileNotFoundException("StageSelectMenu.as not found in FFDec export.");

            progress?.Report("Surgically replacing engine scripts (leaving Character/StageData intact)…");
            await _jpexs.ReplaceManyAsync(
                jpexsPath ?? "",
                sourceSwf,
                outfile,
                new (string, string)[]
                {
                    (ScriptNameConfig, configAs),
                    (ScriptNameMain, mainAs),
                    (ScriptNameResource, resourceAs),
                    (ScriptNameCharacterSelectMenu, cssMenuAs),
                    (ScriptNameStageSelectMenu, sssMenuAs)
                },
                cancellationToken);
        }

        /// <summary>
        /// Injects <c>[SSF2MM_PACK]</c> boot verification + slot playableId bind into CSS/SSS menus.
        /// Reads <c>mappings.ssf2mm</c> written by <see cref="ContentPackService.ApplyPacksToMappings"/>.
        /// </summary>
        public static bool PatchContentPackRuntime(string scriptsRoot)
        {
            var css = FindScriptFile(scriptsRoot, "CharacterSelectMenu.as", preferParentName: "menus");
            var sss = FindScriptFile(scriptsRoot, "StageSelectMenu.as", preferParentName: "menus");
            if (css == null || sss == null) return false;
            var cssOk = PatchContentPackInCharacterSelect(css);
            var sssOk = PatchContentPackInStageSelect(sss);
            return cssOk && sssOk;
        }

        public static bool PatchContentPackInCharacterSelect(string characterSelectMenuAsPath)
        {
            var text = File.ReadAllText(characterSelectMenuAsPath);
            // Prefer durable markers (survive FFDec comment stripping): live.json packs + art OK trace.
            var hasFakeChrome = text.Contains(".name = \"ssf2mmNameplate\"", StringComparison.Ordinal)
                                || text.Contains(".name = \"ssf2mmArtBg\"", StringComparison.Ordinal)
                                || text.Contains(".name = \"ssf2mmLabel\"", StringComparison.Ordinal);
            var hasBrokenClip = text.Contains("scrollRect", StringComparison.Ordinal)
                                || text.Contains("function ssf2mmChipFrameBounds", StringComparison.Ordinal);
            var hasLiveUi = text.Contains(ContentPackLiveUiMarker, StringComparison.Ordinal)
                            && text.Contains("function ssf2mmGetLivePacks", StringComparison.Ordinal)
                            && text.Contains("function ssf2mmLoadChipArt", StringComparison.Ordinal)
                            && text.Contains("function ssf2mmCapturePortraitHole", StringComparison.Ordinal)
                            && text.Contains("function ssf2mmFitChipLabel", StringComparison.Ordinal)
                            && text.Contains("ssf2mmBarW", StringComparison.Ordinal)
                            && text.Contains("ssf2mmFitScale", StringComparison.Ordinal)
                            && text.Contains("artAt", StringComparison.Ordinal)
                            && text.Contains("nameBand", StringComparison.Ordinal)
                            && text.Contains("cacheAsBitmap", StringComparison.Ordinal)
                            && text.Contains("backgroundColor = 0", StringComparison.Ordinal)
                            && text.Contains("[SSF2MM_PACK] art OK", StringComparison.Ordinal)
                            && text.Contains("File.applicationDirectory", StringComparison.Ordinal)
                            && text.Contains("ssf2mmArtMask", StringComparison.Ordinal)
                            && text.Contains("loadBytes", StringComparison.Ordinal)
                            && !hasFakeChrome
                            && !hasBrokenClip;
            var hasApiImport = text.Contains("import com.mcleodgaming.ssf2.api.SSF2API", StringComparison.Ordinal)
                               && text.Contains("import flash.filesystem.File", StringComparison.Ordinal);
            var hasBindCall = text.Contains("this.ssf2mmBindCharacterChip(child", StringComparison.Ordinal);
            var hasBootCall = text.Contains("this.ssf2mmPackBoot(mappings)", StringComparison.Ordinal);
            // Old messy TextField overlay — rebuild helpers without it.
            var hasBadLabelOverlay = text.Contains("function ssf2mmApplyChipLabel", StringComparison.Ordinal)
                                     && !text.Contains("function ssf2mmApplyNativeChipLabel", StringComparison.Ordinal);

            if (hasLiveUi && hasBindCall && hasBootCall && hasApiImport && !hasBadLabelOverlay)
            {
                // Dedupe accidental double call sites from older comment-marker races.
                text = DedupeConsecutiveCalls(text, "this.ssf2mmBindCharacterChip(child,allCharacters[i],mappings);");
                text = DedupeConsecutiveCalls(text, "this.ssf2mmBindCharacterChip(child, allCharacters[i], mappings);");
                text = DedupeConsecutiveCalls(text, "this.ssf2mmPackBoot(mappings);");
                File.WriteAllText(characterSelectMenuAsPath, text);
                return true;
            }

            if (hasBadLabelOverlay || hasFakeChrome || hasBrokenClip || !hasLiveUi)
            {
                text = StripInjectedPackHelpers(text);
                hasBindCall = text.Contains("this.ssf2mmBindCharacterChip(child", StringComparison.Ordinal);
                hasBootCall = text.Contains("this.ssf2mmPackBoot(mappings)", StringComparison.Ordinal);
            }

            // Upgrade / partial: strip old helpers if present without live UI marker, then re-inject.
            if (text.Contains("function ssf2mmBindCharacterChip", StringComparison.Ordinal) && !hasLiveUi)
            {
                text = StripInjectedPackHelpers(text);
                hasBindCall = text.Contains("this.ssf2mmBindCharacterChip(child", StringComparison.Ordinal);
                hasBootCall = text.Contains("this.ssf2mmPackBoot(mappings)", StringComparison.Ordinal);
            }

            if (!hasBindCall)
            {
                var bindPattern = new Regex(
                    @"this\.m_charMCHash\[allCharacters\[i\]\]\s*=\s*child\s*;",
                    RegexOptions.IgnoreCase);
                if (!bindPattern.IsMatch(text))
                    return false;

                text = bindPattern.Replace(
                    text,
                    "this.m_charMCHash[allCharacters[i]] = child;\r\n" +
                    "                  this.ssf2mmBindCharacterChip(child, allCharacters[i], mappings);",
                    1);
            }

            if (!hasBootCall)
            {
                var bootAnchor = new Regex(
                    @"this\.displayObjectTable\s*=\s*new\s*DisplayObjectTable\s*\(",
                    RegexOptions.IgnoreCase);
                if (!bootAnchor.IsMatch(text))
                    return false;

                text = bootAnchor.Replace(
                    text,
                    "/* " + ContentPacksMarker + " */\r\n" +
                    "         this.ssf2mmPackBoot(mappings);\r\n" +
                    "         this.displayObjectTable = new DisplayObjectTable(",
                    1);
            }

            if (!text.Contains(ContentPackLiveUiMarker, StringComparison.Ordinal)
                || !text.Contains("function ssf2mmGetLivePacks", StringComparison.Ordinal)
                || !text.Contains("function ssf2mmLoadChipArt", StringComparison.Ordinal)
                || !text.Contains("function ssf2mmCapturePortraitHole", StringComparison.Ordinal)
                || !text.Contains("function ssf2mmFitChipLabel", StringComparison.Ordinal)
                || !text.Contains("ssf2mmBarW", StringComparison.Ordinal)
                || !text.Contains("ssf2mmFitScale", StringComparison.Ordinal)
                || !text.Contains("artAt", StringComparison.Ordinal)
                || !text.Contains("nameBand", StringComparison.Ordinal)
                || !text.Contains("cacheAsBitmap", StringComparison.Ordinal)
                || !text.Contains("backgroundColor = 0", StringComparison.Ordinal)
                || text.Contains("scrollRect", StringComparison.Ordinal)
                || text.Contains("function ssf2mmChipFrameBounds", StringComparison.Ordinal)
                || !text.Contains("File.applicationDirectory", StringComparison.Ordinal)
                || !text.Contains("ssf2mmArtMask", StringComparison.Ordinal)
                || text.Contains(".name = \"ssf2mmNameplate\"", StringComparison.Ordinal)
                || !text.Contains("loadBytes", StringComparison.Ordinal)
                || !text.Contains("[SSF2MM_PACK] art OK", StringComparison.Ordinal))
            {
                text = StripInjectedPackHelpers(text);
                var helpers = BuildContentPackCharacterSelectHelpers();
                var classClose = text.LastIndexOf("\n   }", StringComparison.Ordinal);
                if (classClose < 0)
                {
                    var lastBrace = text.LastIndexOf('}');
                    if (lastBrace < 0) return false;
                    classClose = lastBrace;
                }
                text = text.Insert(classClose, helpers);
            }

            text = EnsureContentPackCssImports(text);

            File.WriteAllText(characterSelectMenuAsPath, text);
            var next = File.ReadAllText(characterSelectMenuAsPath);
            return next.Contains(ContentPackLiveUiMarker, StringComparison.Ordinal)
                   && next.Contains("function ssf2mmGetLivePacks", StringComparison.Ordinal)
                   && next.Contains("function ssf2mmLoadChipArt", StringComparison.Ordinal)
                   && next.Contains("function ssf2mmCapturePortraitHole", StringComparison.Ordinal)
                   && next.Contains("function ssf2mmFitChipLabel", StringComparison.Ordinal)
                   && next.Contains("ssf2mmBarW", StringComparison.Ordinal)
                   && next.Contains("ssf2mmFitScale", StringComparison.Ordinal)
                   && next.Contains("artAt", StringComparison.Ordinal)
                   && next.Contains("nameBand", StringComparison.Ordinal)
                   && next.Contains("cacheAsBitmap", StringComparison.Ordinal)
                   && next.Contains("backgroundColor = 0", StringComparison.Ordinal)
                   && !next.Contains("scrollRect", StringComparison.Ordinal)
                   && !next.Contains("function ssf2mmChipFrameBounds", StringComparison.Ordinal)
                   && next.Contains("File.applicationDirectory", StringComparison.Ordinal)
                   && next.Contains("ssf2mmArtMask", StringComparison.Ordinal)
                   && !next.Contains(".name = \"ssf2mmNameplate\"", StringComparison.Ordinal)
                   && next.Contains("[SSF2MM_PACK] art OK", StringComparison.Ordinal)
                   && next.Contains("this.ssf2mmBindCharacterChip(child", StringComparison.Ordinal)
                   && next.Contains("import flash.filesystem.File", StringComparison.Ordinal)
                   && next.Contains("com.mcleodgaming.ssf2.api.SSF2API", StringComparison.Ordinal);
        }

        private static string EnsureContentPackCssImports(string text)
        {
            var needed = new[]
            {
                "import com.mcleodgaming.ssf2.api.SSF2API;",
                "import flash.filesystem.File;",
                "import flash.filesystem.FileMode;",
                "import flash.filesystem.FileStream;",
                "import flash.text.StaticText;",
                "import flash.text.TextFormat;",
                "import flash.text.TextFormatAlign;",
                "import flash.utils.ByteArray;",
            };
            foreach (var line in needed)
            {
                if (text.Contains(line, StringComparison.Ordinal))
                    continue;
                text = InsertImportLine(text, "   " + line);
            }
            return text;
        }

        private static string InsertImportLine(string text, string importLine)
        {
            var importAnchor = new Regex(
                @"^(?<pre>package\s+[\w.]+\s*\{\s*\r?\n)(?<imports>(?:\s*import\s+[^\r\n]+;\s*\r?\n)*)",
                RegexOptions.Multiline);
            var m = importAnchor.Match(text);
            if (!m.Success)
                return importLine + "\r\n" + text;

            return text.Substring(0, m.Index)
                   + m.Groups["pre"].Value
                   + importLine + "\r\n"
                   + m.Groups["imports"].Value
                   + text.Substring(m.Index + m.Length);
        }

        private static string DedupeConsecutiveCalls(string text, string call)
        {
            if (string.IsNullOrEmpty(call) || !text.Contains(call, StringComparison.Ordinal))
                return text;
            var doubled = call + "\r\n" + call;
            while (text.Contains(doubled, StringComparison.Ordinal))
                text = text.Replace(doubled, call, StringComparison.Ordinal);
            doubled = call + "\n" + call;
            while (text.Contains(doubled, StringComparison.Ordinal))
                text = text.Replace(doubled, call, StringComparison.Ordinal);
            // Also handle whitespace variants between duplicated lines.
            var rx = new Regex(
                Regex.Escape(call) + @"\s*" + Regex.Escape(call),
                RegexOptions.Multiline);
            return rx.Replace(text, call);
        }

        private static string StripInjectedPackHelpers(string text)
        {
            var names = new[]
            {
                "ssf2mmGetLivePacks",
                "ssf2mmBindCharacterChip",
                "ssf2mmPackBoot",
                "ssf2mmApplyChipLabel",
                "ssf2mmApplyNativeChipLabel",
                "ssf2mmFitChipLabel",
                "ssf2mmIsChipText",
                "ssf2mmChipFrameBounds",
                "ssf2mmHidePortraitChildren",
                "ssf2mmCapturePortraitHole",
                "ssf2mmLoadChipArt",
                "ssf2mmOnChipArtComplete",
                "ssf2mmOnChipArtFail"
            };
            foreach (var name in names)
            {
                while (true)
                {
                    var marker = "function " + name;
                    var idx = text.IndexOf(marker, StringComparison.Ordinal);
                    if (idx < 0) break;

                    // Back up to "private " / start of line.
                    var start = idx;
                    while (start > 0 && text[start - 1] != '\n')
                        start--;

                    var brace = text.IndexOf('{', idx);
                    if (brace < 0) break;
                    var depth = 0;
                    var end = -1;
                    for (var i = brace; i < text.Length; i++)
                    {
                        var c = text[i];
                        if (c == '{') depth++;
                        else if (c == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                end = i + 1;
                                break;
                            }
                        }
                    }
                    if (end < 0) break;
                    // Swallow trailing newlines.
                    while (end < text.Length && (text[end] == '\r' || text[end] == '\n'))
                        end++;
                    text = text.Remove(start, end - start);
                }
            }
            return text;
        }

        private static string BuildContentPackCharacterSelectHelpers() =>
            "\r\n" +
            "      /* " + ContentPackArtMarker + " " + ContentPackLiveUiMarker + " */\r\n" +
            "      private function ssf2mmGetLivePacks(mappings:Object) : *\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var f:File = File.applicationDirectory.resolvePath(\"data/ssf2mm/live.json\");\r\n" +
            "            if(f.exists)\r\n" +
            "            {\r\n" +
            "               var fs:FileStream = new FileStream();\r\n" +
            "               fs.open(f, FileMode.READ);\r\n" +
            "               var txt:String = fs.readUTFBytes(fs.bytesAvailable);\r\n" +
            "               fs.close();\r\n" +
            "               var live:Object = JSON.parse(txt);\r\n" +
            "               if(live != null && live.packs != null)\r\n" +
            "               {\r\n" +
            "                  return live.packs;\r\n" +
            "               };\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eLive:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "         if(mappings != null && mappings.ssf2mm != null && mappings.ssf2mm.packs != null)\r\n" +
            "         {\r\n" +
            "            return mappings.ssf2mm.packs;\r\n" +
            "         };\r\n" +
            "         return null;\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmBindCharacterChip(chip:*, rosterId:String, mappings:Object) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(chip == null)\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] bind skip roster=\" + rosterId + \" reason=no_chip\");\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var packs:* = this.ssf2mmGetLivePacks(mappings);\r\n" +
            "            if(packs == null)\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] bind skip roster=\" + rosterId + \" reason=no_live_index\");\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var pi:int = 0;\r\n" +
            "            while(pi < packs.length)\r\n" +
            "            {\r\n" +
            "               var pk:Object = packs[pi];\r\n" +
            "               if(pk && pk.type == \"character\" && pk.rosterId == rosterId && pk.playableId)\r\n" +
            "               {\r\n" +
            "                  chip.characterID = pk.playableId;\r\n" +
            "                  var label:String = pk.name != null ? String(pk.name) : String(pk.id);\r\n" +
            "                  chip.ssf2mmChipLabel = label;\r\n" +
            "                  this.ssf2mmApplyNativeChipLabel(chip, label);\r\n" +
            "                  this.ssf2mmLoadChipArt(chip, String(pk.id));\r\n" +
            "                  SSF2API.print(\"[SSF2MM_PACK] bind ok roster=\" + rosterId + \" playable=\" + pk.playableId);\r\n" +
            "                  return;\r\n" +
            "               };\r\n" +
            "               pi++;\r\n" +
            "            };\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] bind skip roster=\" + rosterId + \" reason=no_pack_for_roster\");\r\n" +
            "         }\r\n" +
            "         catch(eBind:*)\r\n" +
            "         {\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] bind FAIL roster=\" + rosterId + \" reason=\" + eBind);\r\n" +
            "            }\r\n" +
            "            catch(eBind2:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmIsChipText(ch:*) : Boolean\r\n" +
            "      {\r\n" +
            "         if(ch == null)\r\n" +
            "         {\r\n" +
            "            return false;\r\n" +
            "         };\r\n" +
            "         if(ch is TextField)\r\n" +
            "         {\r\n" +
            "            return true;\r\n" +
            "         };\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(ch is StaticText)\r\n" +
            "            {\r\n" +
            "               return true;\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eSt:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "         return false;\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmApplyNativeChipLabel(chip:*, label:String) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(chip == null)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var upper:String = label != null ? String(label).toUpperCase() : \"\";\r\n" +
            "            var hi:int = 0;\r\n" +
            "            while(hi < chip.numChildren)\r\n" +
            "            {\r\n" +
            "               var ch:* = chip.getChildAt(hi);\r\n" +
            "               if(ch is TextField)\r\n" +
            "               {\r\n" +
            "                  var tf:TextField = TextField(ch);\r\n" +
            "                  // Native TextField plate (not a drawn Shape) — stock chips read as black bar + white name.\r\n" +
            "                  tf.background = true;\r\n" +
            "                  tf.backgroundColor = 0;\r\n" +
            "                  tf.border = false;\r\n" +
            "                  tf.scaleX = 1;\r\n" +
            "                  tf.scaleY = 1;\r\n" +
            "                  tf.text = upper;\r\n" +
            "                  this.ssf2mmFitChipLabel(tf);\r\n" +
            "               }\r\n" +
            "               else if(ch != null && ch.hasOwnProperty(\"text\"))\r\n" +
            "               {\r\n" +
            "                  try\r\n" +
            "                  {\r\n" +
            "                     ch.text = upper;\r\n" +
            "                  }\r\n" +
            "                  catch(eTxt:*)\r\n" +
            "                  {\r\n" +
            "                  };\r\n" +
            "               };\r\n" +
            "               hi++;\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eLab:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmFitChipLabel(tf:TextField) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(tf == null)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            // Lock bar slot once (chip-local).\r\n" +
            "            if(!(Number(tf.ssf2mmBarW) > 4))\r\n" +
            "            {\r\n" +
            "               tf.ssf2mmBarW = tf.width;\r\n" +
            "               tf.ssf2mmBarX = tf.x;\r\n" +
            "               tf.ssf2mmBarY = tf.y;\r\n" +
            "            };\r\n" +
            "            var barW:Number = Number(tf.ssf2mmBarW);\r\n" +
            "            var barX:Number = Number(tf.ssf2mmBarX);\r\n" +
            "            var barY:Number = Number(tf.ssf2mmBarY);\r\n" +
            "            if(!(barW > 4))\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            // Outline fonts ignore size/letterSpacing. Fit like stock: widen unscaled\r\n" +
            "            // field so glyphs are not clipped, then scaleX so painted width == barW.\r\n" +
            "            // (Never set width AFTER scaleX — that shifts the plate to the right.)\r\n" +
            "            tf.scaleX = 1;\r\n" +
            "            tf.scaleY = 1;\r\n" +
            "            tf.x = barX;\r\n" +
            "            tf.y = barY;\r\n" +
            "            tf.border = false;\r\n" +
            "            var fmt:TextFormat = tf.defaultTextFormat;\r\n" +
            "            if(fmt == null)\r\n" +
            "            {\r\n" +
            "               fmt = new TextFormat();\r\n" +
            "            };\r\n" +
            "            fmt.align = TextFormatAlign.CENTER;\r\n" +
            "            tf.defaultTextFormat = fmt;\r\n" +
            "            tf.setTextFormat(fmt);\r\n" +
            "            var pad:Number = 2;\r\n" +
            "            var contentW:Number = tf.textWidth + pad;\r\n" +
            "            if(contentW < barW)\r\n" +
            "            {\r\n" +
            "               contentW = barW;\r\n" +
            "            };\r\n" +
            "            tf.width = contentW;\r\n" +
            "            var s:Number = barW / contentW;\r\n" +
            "            if(s > 1)\r\n" +
            "            {\r\n" +
            "               s = 1;\r\n" +
            "            };\r\n" +
            "            if(s < 0.42)\r\n" +
            "            {\r\n" +
            "               s = 0.42;\r\n" +
            "            };\r\n" +
            "            tf.ssf2mmFitScale = s;\r\n" +
            "            tf.scaleX = s;\r\n" +
            "            tf.scaleY = 1;\r\n" +
            "            // contentW * scaleX == barW when s == barW/contentW — pin left edge.\r\n" +
            "            tf.x = barX;\r\n" +
            "            tf.y = barY;\r\n" +
            "         }\r\n" +
            "         catch(eFit:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmCapturePortraitHole(chip:*) : Rectangle\r\n" +
            "      {\r\n" +
            "         var hole:Rectangle = null;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var seenChrome:Boolean = false;\r\n" +
            "            var i:int = 0;\r\n" +
            "            while(i < chip.numChildren)\r\n" +
            "            {\r\n" +
            "               var ch:* = chip.getChildAt(i);\r\n" +
            "               i++;\r\n" +
            "               if(ch == null || this.ssf2mmIsChipText(ch))\r\n" +
            "               {\r\n" +
            "                  continue;\r\n" +
            "               };\r\n" +
            "               var nm:String = ch.name != null ? String(ch.name) : \"\";\r\n" +
            "               if(nm.indexOf(\"ssf2mm\") == 0)\r\n" +
            "               {\r\n" +
            "                  continue;\r\n" +
            "               };\r\n" +
            "               if(!seenChrome)\r\n" +
            "               {\r\n" +
            "                  seenChrome = true;\r\n" +
            "                  continue;\r\n" +
            "               };\r\n" +
            "               var cb:Rectangle = ch.getBounds(chip);\r\n" +
            "               if(cb.width >= 4 && cb.height >= 4)\r\n" +
            "               {\r\n" +
            "                  if(hole == null)\r\n" +
            "                  {\r\n" +
            "                     hole = cb.clone();\r\n" +
            "                  }\r\n" +
            "                  else\r\n" +
            "                  {\r\n" +
            "                     hole = hole.union(cb);\r\n" +
            "                  };\r\n" +
            "               };\r\n" +
            "               ch.visible = false;\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eHole:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "         return hole;\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmLoadChipArt(chip:*, packId:String) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(chip == null || packId == null || packId.length == 0)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var rel:String = \"expansions/packs/\" + packId + \"/assets/select.png\";\r\n" +
            "            var png:File = File.applicationDirectory.resolvePath(rel);\r\n" +
            "            if(!png.exists)\r\n" +
            "            {\r\n" +
            "               png = File.applicationDirectory.resolvePath(\"expansions/packs/\" + packId + \"/assets/select.jpg\");\r\n" +
            "            };\r\n" +
            "            if(!png.exists)\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] art FAIL id=\" + packId + \" reason=missing_select\");\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var oldArt:* = chip.getChildByName(\"ssf2mmArt\");\r\n" +
            "            if(oldArt != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldArt);\r\n" +
            "            };\r\n" +
            "            var oldMask:* = chip.getChildByName(\"ssf2mmArtMask\");\r\n" +
            "            if(oldMask != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldMask);\r\n" +
            "            };\r\n" +
            "            var oldBg:* = chip.getChildByName(\"ssf2mmArtBg\");\r\n" +
            "            if(oldBg != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldBg);\r\n" +
            "            };\r\n" +
            "            var oldFrame:* = chip.getChildByName(\"ssf2mmFrame\");\r\n" +
            "            if(oldFrame != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldFrame);\r\n" +
            "            };\r\n" +
            "            var oldPlate:* = chip.getChildByName(\"ssf2mmNameplate\");\r\n" +
            "            if(oldPlate != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldPlate);\r\n" +
            "            };\r\n" +
            "            var oldLbl:* = chip.getChildByName(\"ssf2mmLabel\");\r\n" +
            "            if(oldLbl != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldLbl);\r\n" +
            "            };\r\n" +
            "            // Hide donor portrait; keep first graphic as chrome. Art only below nameplate band.\r\n" +
            "            var hole:Rectangle = this.ssf2mmCapturePortraitHole(chip);\r\n" +
            "            if(hole == null || hole.width < 4 || hole.height < 4)\r\n" +
            "            {\r\n" +
            "               hole = new Rectangle(-22,-22,44,44);\r\n" +
            "            };\r\n" +
            "            // Leave top nameplate strip uncovered so chrome/name plate stay visible.\r\n" +
            "            var nameBand:Number = Math.max(10, Math.min(14, hole.height * 0.24));\r\n" +
            "            hole.y += nameBand;\r\n" +
            "            hole.height = Math.max(4, hole.height - nameBand);\r\n" +
            "            var inset:Number = 1;\r\n" +
            "            hole.x += inset;\r\n" +
            "            hole.y += inset;\r\n" +
            "            hole.width = Math.max(4, hole.width - inset * 2);\r\n" +
            "            hole.height = Math.max(4, hole.height - inset * 2);\r\n" +
            "            var loader:Loader = new Loader();\r\n" +
            "            loader.name = \"ssf2mmArt\";\r\n" +
            "            chip.ssf2mmArtPackId = packId;\r\n" +
            "            chip.ssf2mmArtBoxX = hole.x;\r\n" +
            "            chip.ssf2mmArtBoxY = hole.y;\r\n" +
            "            chip.ssf2mmArtBoxW = hole.width;\r\n" +
            "            chip.ssf2mmArtBoxH = hole.height;\r\n" +
            "            loader.contentLoaderInfo.addEventListener(Event.COMPLETE, this.ssf2mmOnChipArtComplete);\r\n" +
            "            loader.contentLoaderInfo.addEventListener(IOErrorEvent.IO_ERROR, this.ssf2mmOnChipArtFail);\r\n" +
            "            // Stock depth: chrome backing, then art, then nameplate text.\r\n" +
            "            var artAt:int = 0;\r\n" +
            "            var gi:int = 0;\r\n" +
            "            while(gi < chip.numChildren)\r\n" +
            "            {\r\n" +
            "               var gch:* = chip.getChildAt(gi);\r\n" +
            "               if(gch != null && !this.ssf2mmIsChipText(gch))\r\n" +
            "               {\r\n" +
            "                  var gnm:String = gch.name != null ? String(gch.name) : \"\";\r\n" +
            "                  if(gnm.indexOf(\"ssf2mm\") != 0)\r\n" +
            "                  {\r\n" +
            "                     artAt = gi + 1;\r\n" +
            "                     break;\r\n" +
            "                  };\r\n" +
            "               };\r\n" +
            "               gi++;\r\n" +
            "            };\r\n" +
            "            chip.addChildAt(loader, artAt);\r\n" +
            "            var bytes:ByteArray = new ByteArray();\r\n" +
            "            var fsArt:FileStream = new FileStream();\r\n" +
            "            fsArt.open(png, FileMode.READ);\r\n" +
            "            fsArt.readBytes(bytes);\r\n" +
            "            fsArt.close();\r\n" +
            "            loader.loadBytes(bytes);\r\n" +
            "         }\r\n" +
            "         catch(eArt:*)\r\n" +
            "         {\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] art FAIL id=\" + packId + \" reason=\" + eArt);\r\n" +
            "            }\r\n" +
            "            catch(eArt2:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmOnChipArtComplete(e:Event) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var li:LoaderInfo = e.target as LoaderInfo;\r\n" +
            "            if(li == null || li.loader == null)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var loader:Loader = li.loader;\r\n" +
            "            var chip:* = loader.parent;\r\n" +
            "            if(chip == null)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var boxW:Number = Number(chip.ssf2mmArtBoxW);\r\n" +
            "            var boxH:Number = Number(chip.ssf2mmArtBoxH);\r\n" +
            "            var boxX:Number = Number(chip.ssf2mmArtBoxX);\r\n" +
            "            var boxY:Number = Number(chip.ssf2mmArtBoxY);\r\n" +
            "            if(!(boxW > 1) || !(boxH > 1))\r\n" +
            "            {\r\n" +
            "               boxW = 44;\r\n" +
            "               boxH = 44;\r\n" +
            "               boxX = -22;\r\n" +
            "               boxY = -22;\r\n" +
            "            };\r\n" +
            "            var cw:Number = loader.content.width;\r\n" +
            "            var ch:Number = loader.content.height;\r\n" +
            "            if(!(cw > 0) || !(ch > 0))\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] art FAIL id=\" + chip.ssf2mmArtPackId + \" reason=empty_bitmap\");\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            // Cover-fit inside hole; mask clips overflow so art cannot paint over the grid.\r\n" +
            "            var scale:Number = Math.max(boxW / cw, boxH / ch);\r\n" +
            "            loader.scaleX = scale;\r\n" +
            "            loader.scaleY = scale;\r\n" +
            "            loader.x = boxX + (boxW - cw * scale) * 0.5;\r\n" +
            "            loader.y = boxY + (boxH - ch * scale) * 0.5;\r\n" +
            "            var oldMask:* = chip.getChildByName(\"ssf2mmArtMask\");\r\n" +
            "            if(oldMask != null)\r\n" +
            "            {\r\n" +
            "               chip.removeChild(oldMask);\r\n" +
            "            };\r\n" +
            "            var mask:Shape = new Shape();\r\n" +
            "            mask.name = \"ssf2mmArtMask\";\r\n" +
            "            mask.graphics.beginFill(0xFFFFFF);\r\n" +
            "            mask.graphics.drawRect(boxX, boxY, boxW, boxH);\r\n" +
            "            mask.graphics.endFill();\r\n" +
            "            var maskAt:int = chip.getChildIndex(loader) + 1;\r\n" +
            "            chip.addChildAt(mask, maskAt);\r\n" +
            "            // AIR often needs cacheAsBitmap for DisplayObject.mask to clip reliably.\r\n" +
            "            loader.cacheAsBitmap = true;\r\n" +
            "            mask.cacheAsBitmap = true;\r\n" +
            "            loader.mask = mask;\r\n" +
            "            // Nameplate TextField above art.\r\n" +
            "            var hi:int = 0;\r\n" +
            "            while(hi < chip.numChildren)\r\n" +
            "            {\r\n" +
            "               var kid:* = chip.getChildAt(hi);\r\n" +
            "               hi++;\r\n" +
            "               if(!this.ssf2mmIsChipText(kid))\r\n" +
            "               {\r\n" +
            "                  continue;\r\n" +
            "               };\r\n" +
            "               try\r\n" +
            "               {\r\n" +
            "                  chip.setChildIndex(kid, chip.numChildren - 1);\r\n" +
            "               }\r\n" +
            "               catch(eIdx:*)\r\n" +
            "               {\r\n" +
            "               };\r\n" +
            "            };\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] art OK id=\" + chip.ssf2mmArtPackId + \" file=select.png\");\r\n" +
            "         }\r\n" +
            "         catch(eDone:*)\r\n" +
            "         {\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] art FAIL reason=\" + eDone);\r\n" +
            "            }\r\n" +
            "            catch(eDone2:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmOnChipArtFail(e:Event) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var li:LoaderInfo = e.target as LoaderInfo;\r\n" +
            "            var packId:String = \"?\";\r\n" +
            "            if(li != null && li.loader != null && li.loader.parent != null)\r\n" +
            "            {\r\n" +
            "               packId = String(li.loader.parent.ssf2mmArtPackId);\r\n" +
            "            };\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] art FAIL id=\" + packId + \" reason=io_error\");\r\n" +
            "         }\r\n" +
            "         catch(eFail:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private function ssf2mmPackBoot(mappings:Object) : void\r\n" +
            "      {\r\n" +
            "         var ok:int = 0;\r\n" +
            "         var fail:int = 0;\r\n" +
            "         var nChar:int = 0;\r\n" +
            "         var nStage:int = 0;\r\n" +
            "         var nMusic:int = 0;\r\n" +
            "         var i:int = 0;\r\n" +
            "         var p:Object = null;\r\n" +
            "         var packs:* = null;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] boot start " + ContentPackLiveUiMarker + "\");\r\n" +
            "            packs = this.ssf2mmGetLivePacks(mappings);\r\n" +
            "            if(packs != null)\r\n" +
            "            {\r\n" +
            "               i = 0;\r\n" +
            "               while(i < packs.length)\r\n" +
            "               {\r\n" +
            "                  p = packs[i];\r\n" +
            "                  if(p)\r\n" +
            "                  {\r\n" +
            "                     if(p.type == \"character\")\r\n" +
            "                     {\r\n" +
            "                        nChar++;\r\n" +
            "                        if(p.exists != false && Boolean(p.rosterId))\r\n" +
            "                        {\r\n" +
            "                           SSF2API.print(\"[SSF2MM_PACK] char OK id=\" + p.id + \" slot=\" + p.slot + \" file=\" + p.file);\r\n" +
            "                           ok++;\r\n" +
            "                        }\r\n" +
            "                        else\r\n" +
            "                        {\r\n" +
            "                           SSF2API.print(\"[SSF2MM_PACK] char FAIL id=\" + p.id + \" reason=missing_file\");\r\n" +
            "                           fail++;\r\n" +
            "                        };\r\n" +
            "                     }\r\n" +
            "                     else if(p.type == \"stage\")\r\n" +
            "                     {\r\n" +
            "                        nStage++;\r\n" +
            "                        if(p.exists != false && Boolean(p.rosterId))\r\n" +
            "                        {\r\n" +
            "                           SSF2API.print(\"[SSF2MM_PACK] stage OK id=\" + p.id + \" slot=\" + p.slot + \" file=\" + p.file);\r\n" +
            "                           ok++;\r\n" +
            "                        }\r\n" +
            "                        else\r\n" +
            "                        {\r\n" +
            "                           SSF2API.print(\"[SSF2MM_PACK] stage FAIL id=\" + p.id + \" reason=missing_file\");\r\n" +
            "                           fail++;\r\n" +
            "                        };\r\n" +
            "                     }\r\n" +
            "                     else if(p.type == \"music\")\r\n" +
            "                     {\r\n" +
            "                        nMusic++;\r\n" +
            "                        if(p.exists != false)\r\n" +
            "                        {\r\n" +
            "                           SSF2API.print(\"[SSF2MM_PACK] music OK id=\" + p.id + \" file=\" + p.file);\r\n" +
            "                           ok++;\r\n" +
            "                        }\r\n" +
            "                        else\r\n" +
            "                        {\r\n" +
            "                           SSF2API.print(\"[SSF2MM_PACK] music FAIL id=\" + p.id + \" reason=missing_file\");\r\n" +
            "                           fail++;\r\n" +
            "                        };\r\n" +
            "                     };\r\n" +
            "                  };\r\n" +
            "                  i++;\r\n" +
            "               };\r\n" +
            "            }\r\n" +
            "            else\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] boot skip reason=no_live_index\");\r\n" +
            "            };\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] index ok packs=\" + (nChar + nStage + nMusic) + \" charSlots=\" + nChar + \" stageSlots=\" + nStage + \" music=\" + nMusic);\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] ui_lib ok\");\r\n" +
            "            SSF2API.print(\"[SSF2MM_PACK] boot done ok=\" + ok + \" fail=\" + fail);\r\n" +
            "         }\r\n" +
            "         catch(eBoot:*)\r\n" +
            "         {\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               SSF2API.print(\"[SSF2MM_PACK] boot FAIL reason=\" + eBoot);\r\n" +
            "            }\r\n" +
            "            catch(eBoot2:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "         };\r\n" +
            "      }\r\n";

        public static bool PatchContentPackInStageSelect(string stageSelectMenuAsPath)
        {
            var text = File.ReadAllText(stageSelectMenuAsPath);
            if (text.Contains(ContentPacksMarker, StringComparison.Ordinal))
                return true;

            // Marker + lightweight stageID rebind helper call if hash assign is found.
            var stageBind = new Regex(
                @"(child\.stageID\s*=\s*child\.stageID\s*\|\|\s*[^;]+;)",
                RegexOptions.IgnoreCase);
            if (stageBind.IsMatch(text))
            {
                text = stageBind.Replace(
                    text,
                    "$1\r\n" +
                    "                  /* " + ContentPacksMarker + " */\r\n" +
                    "                  this.ssf2mmBindStageChip(child);",
                    1);
            }
            else
            {
                text = "/* " + ContentPacksMarker + " */\r\n" + text;
            }

            if (!text.Contains("function ssf2mmBindStageChip", StringComparison.Ordinal))
            {
                var helper =
                    "\r\n" +
                    "      private function ssf2mmBindStageChip(chip:*) : void\r\n" +
                    "      {\r\n" +
                    "         try\r\n" +
                    "         {\r\n" +
                    "            var meta:Object = ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\");\r\n" +
                    "            if(chip == null || meta == null || meta.ssf2mm == null || meta.ssf2mm.packs == null)\r\n" +
                    "            {\r\n" +
                    "               return;\r\n" +
                    "            };\r\n" +
                    "            var sj:int = 0;\r\n" +
                    "            while(sj < meta.ssf2mm.packs.length)\r\n" +
                    "            {\r\n" +
                    "               var sp:Object = meta.ssf2mm.packs[sj];\r\n" +
                    "               if(sp && sp.type == \"stage\" && chip.stageID == sp.rosterId && sp.playableId)\r\n" +
                    "               {\r\n" +
                    "                  chip.stageID = sp.playableId;\r\n" +
                    "               };\r\n" +
                    "               sj++;\r\n" +
                    "            };\r\n" +
                    "         }\r\n" +
                    "         catch(eStage:*)\r\n" +
                    "         {\r\n" +
                    "         };\r\n" +
                    "      }\r\n";
                var classClose = text.LastIndexOf("\n   }", StringComparison.Ordinal);
                if (classClose > 0)
                    text = text.Insert(classClose, helper);
            }

            File.WriteAllText(stageSelectMenuAsPath, text);
            return File.ReadAllText(stageSelectMenuAsPath).Contains(ContentPacksMarker, StringComparison.Ordinal);
        }

        public static string? FindScriptFile(string scriptsRoot, string fileName, string? preferParentName = null)
        {
            var matches = Directory.GetFiles(scriptsRoot, fileName, SearchOption.AllDirectories);
            if (matches.Length == 0) return null;
            if (!string.IsNullOrEmpty(preferParentName))
            {
                var preferred = matches.FirstOrDefault(f =>
                    Path.GetFileName(Path.GetDirectoryName(f))
                        ?.Equals(preferParentName, StringComparison.OrdinalIgnoreCase) == true);
                if (preferred != null) return preferred;
            }
            return matches[0];
        }

        public async Task<GamePrepareResult> RevertAsync(
            string installRoot,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            var state = LoadState(installRoot)
                ?? throw new InvalidOperationException(
                    "No prepare state found. This install does not look prepared by SSF2 Mod Manager.");

            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            if (File.Exists(state.MainSwfBackupPath))
            {
                progress?.Report("Restoring SSF2.swf from backup…");
                File.Copy(state.MainSwfBackupPath, mainSwf, overwrite: true);
            }
            else
            {
                progress?.Report("Warning: SSF2.swf backup missing; engine patch cannot be fully reverted.");
            }

            progress?.Report($"Recompressing {state.Files.Count} file(s) to DAT…");
            var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
            var errors = new ConcurrentBag<string>();
            var ok = 0;

            await Parallel.ForEachAsync(
                state.Files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                },
                async (entry, ct) =>
                {
                    var swfFull = Path.Combine(installRoot, entry.SwfRelativePath);
                    var datFull = Path.Combine(installRoot, entry.DatRelativePath);
                    try
                    {
                        if (!File.Exists(swfFull))
                        {
                            errors.Add($"{entry.FriendlyFileName}: missing .swf");
                            return;
                        }

                        var swfBytes = await File.ReadAllBytesAsync(swfFull, ct);
                        var ssf = _pressor.Compress(swfBytes, PressorMode.WithGarbageHeader);
                        var datDir = Path.GetDirectoryName(datFull);
                        if (!string.IsNullOrEmpty(datDir))
                            Directory.CreateDirectory(datDir);
                        await File.WriteAllBytesAsync(datFull, ssf, ct);
                        File.Delete(swfFull);
                        Interlocked.Increment(ref ok);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{entry.DatFileName}: {ex.Message}");
                    }
                });

            result.FilesConverted = ok;
            result.FilesFailed = errors.Count;
            result.Errors = errors.ToList();

            try { File.Delete(GetStatePath(installRoot)); } catch { /* ignore */ }
            try
            {
                if (File.Exists(state.MainSwfBackupPath))
                    File.Delete(state.MainSwfBackupPath);
            }
            catch { /* ignore */ }

            result.Success = result.FilesFailed == 0;
            result.Message = result.Success
                ? $"Reverted {result.FilesConverted} file(s) to DAT layout."
                : $"Revert finished with {result.FilesFailed} failure(s); {result.FilesConverted} ok.";
            progress?.Report(result.Message);
            result.State = state;
            return result;
        }

        public async Task<PressorFileResult> ConvertFileToDatAlongsideAsync(
            string installRoot,
            string swfPath,
            bool useGarbageHeader = true,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(swfPath))
                throw new FileNotFoundException("SWF not found.", swfPath);

            string? datName = null;
            var state = LoadState(installRoot);
            if (state != null)
            {
                var rel = Path.GetRelativePath(installRoot, swfPath);
                var match = state.Files.FirstOrDefault(f =>
                    f.SwfRelativePath.Equals(rel, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(f.SwfRelativePath).Equals(
                        Path.GetFileName(swfPath), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    datName = match.DatFileName;
            }

            if (datName == null)
            {
                progress?.Report("Looking up DAT name from mappings…");
                var entries = await _datMapping.LoadMappingsAsync(
                    installRoot, null, progress, cancellationToken);
                var swfName = Path.GetFileNameWithoutExtension(swfPath);
                var match = entries.FirstOrDefault(e =>
                {
                    var friendly = Path.GetFileNameWithoutExtension(
                        DatMappingService.FriendlySwfRelativePath(e));
                    return friendly.Equals(swfName, StringComparison.OrdinalIgnoreCase);
                });
                datName = match?.DatFileName ?? Path.ChangeExtension(Path.GetFileName(swfPath), ".ssf");
            }

            var outPath = Path.Combine(Path.GetDirectoryName(swfPath)!, datName);
            return await _pressor.ConvertToDatAlongsideAsync(
                swfPath, outPath, useGarbageHeader, progress, cancellationToken);
        }

        public static string? FindMainSwf(string installRoot) =>
            Directory.GetFiles(installRoot, "*.swf", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("ssf2.swf", StringComparison.OrdinalIgnoreCase));

        public enum DebugConsolePatchStatus
        {
            Missing = 0,
            /// <summary>Socket bridge for <c>SSF2API.print</c> only.</summary>
            PrintOnly = 1,
            /// <summary>Print bridge + uncaught error capture.</summary>
            Full = 2
        }

        /// <summary>
        /// Fast check: whether <c>SSF2.swf</c> already contains the debug console bridge
        /// (<c>ssf2mmDebugSend</c> / <c>ssf2mmOnUncaught</c> in the ABC constant pool).
        /// Streams inflated SWF bytes and exits early — no FFDec required.
        /// </summary>
        public static bool IsDebugConsolePatchInstalled(string installRoot) =>
            GetDebugConsolePatchStatus(installRoot) != DebugConsolePatchStatus.Missing;

        public static DebugConsolePatchStatus GetDebugConsolePatchStatus(string installRoot)
        {
            var mainSwf = FindMainSwf(installRoot);
            return mainSwf == null
                ? DebugConsolePatchStatus.Missing
                : GetDebugConsolePatchStatusInSwf(mainSwf);
        }

        public static bool IsDebugConsolePatchInstalledInSwf(string swfPath) =>
            GetDebugConsolePatchStatusInSwf(swfPath) != DebugConsolePatchStatus.Missing;

        public static DebugConsolePatchStatus GetDebugConsolePatchStatusInSwf(string swfPath)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
                return DebugConsolePatchStatus.Missing;
            try
            {
                var hasSend = false;
                var hasSock = false;
                var hasUncaught = false;
                var hasV2 = false;
                SwfScanAscii(swfPath, (haystack, len) =>
                {
                    if (!hasSend && ContainsAscii(haystack, len, "ssf2mmDebugSend"))
                        hasSend = true;
                    if (!hasSock && ContainsAscii(haystack, len, "_ssf2mmSock"))
                        hasSock = true;
                    if (!hasUncaught && ContainsAscii(haystack, len, "ssf2mmOnUncaught"))
                        hasUncaught = true;
                    if (!hasV2 && ContainsAscii(haystack, len, DebugConsoleMarkerV2))
                        hasV2 = true;
                    // Early-out once we know Full V2.
                    return hasV2 && hasUncaught && (hasSend || hasSock);
                });
                // Treat pre-V2 uncaught installs as PrintOnly so Install upgrades the socket bridge.
                if (hasUncaught && hasV2 && (hasSend || hasSock))
                    return DebugConsolePatchStatus.Full;
                if (hasSend || hasSock)
                    return DebugConsolePatchStatus.PrintOnly;
                return DebugConsolePatchStatus.Missing;
            }
            catch
            {
                return DebugConsolePatchStatus.Missing;
            }
        }

        /// <summary>
        /// Inflates CWS/FWS SWF and invokes <paramref name="onChunk"/> with a rolling buffer.
        /// Return true from the callback to stop early.
        /// </summary>
        private static void SwfScanAscii(string swfPath, Func<byte[], int, bool> onChunk)
        {
            using var fs = File.OpenRead(swfPath);
            if (fs.Length < 8) return;
            var hdr = new byte[8];
            if (fs.Read(hdr, 0, 8) != 8) return;
            if (hdr[1] != (byte)'W' || hdr[2] != (byte)'S') return;

            Stream payload;
            if (hdr[0] == (byte)'F')
            {
                payload = fs;
            }
            else if (hdr[0] == (byte)'C')
            {
                if (fs.ReadByte() < 0 || fs.ReadByte() < 0) return;
                payload = new DeflateStream(fs, CompressionMode.Decompress, leaveOpen: true);
            }
            else
            {
                return; // ZWS not supported
            }

            const int chunkSize = 64 * 1024;
            const int overlap = 64; // longer than any marker we search
            var buf = new byte[chunkSize + overlap];
            var carry = 0;
            try
            {
                while (true)
                {
                    var read = payload.Read(buf, carry, chunkSize);
                    if (read <= 0) break;
                    var len = carry + read;
                    if (onChunk(buf, len)) return;
                    if (len > overlap)
                    {
                        Buffer.BlockCopy(buf, len - overlap, buf, 0, overlap);
                        carry = overlap;
                    }
                    else
                    {
                        carry = len;
                    }
                }
                if (carry > 0)
                    onChunk(buf, carry);
            }
            finally
            {
                if (payload is DeflateStream)
                    payload.Dispose();
            }
        }

        private static bool ContainsAscii(byte[] haystack, int length, string asciiNeedle)
        {
            var needle = Encoding.ASCII.GetBytes(asciiNeedle);
            if (needle.Length == 0 || length < needle.Length) return false;
            var last = length - needle.Length;
            for (var i = 0; i <= last; i++)
            {
                var ok = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }

        /// <summary>
        /// Hash CSS chips by roster / row id (<c>allCharacters[i]</c>), not <c>characterID</c>.
        /// Crazy CSS aliases share a playable characterID (e.g. donkeykong) but need unique
        /// hash keys so every chip appears; selection still uses the MC's characterID.
        /// </summary>
        public static bool PatchCharacterSelectRosterHash(string scriptsRoot)
        {
            var css = FindScriptFile(scriptsRoot, "CharacterSelectMenu.as", preferParentName: "menus");
            if (css == null) return false;
            return PatchCharacterSelectRosterHashFile(css);
        }

        public static bool PatchCharacterSelectRosterHashFile(string characterSelectMenuAsPath)
        {
            var text = File.ReadAllText(characterSelectMenuAsPath);
            if (text.Contains(CssRosterHashMarker, StringComparison.Ordinal))
                return true;

            // Stock:
            //   if(Boolean(child.characterID)) { this.m_charMCHash[child.characterID] = child; }
            //   else { this.m_charMCHash[allCharacters[i]] = child; }
            var pattern = new Regex(
                @"if\s*\(\s*Boolean\s*\(\s*child\.characterID\s*\)\s*\)\s*\{\s*" +
                @"this\.m_charMCHash\s*\[\s*child\.characterID\s*\]\s*=\s*child\s*;\s*" +
                @"\}\s*else\s*\{\s*" +
                @"this\.m_charMCHash\s*\[\s*allCharacters\s*\[\s*i\s*\]\s*\]\s*=\s*child\s*;\s*" +
                @"\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!pattern.IsMatch(text))
            {
                // Already roster-keyed without marker, or different formatting — try loose form.
                var loose = new Regex(
                    @"this\.m_charMCHash\s*\[\s*child\.characterID\s*\]\s*=\s*child\s*;",
                    RegexOptions.IgnoreCase);
                if (!loose.IsMatch(text))
                    return text.Contains("m_charMCHash[allCharacters[i]]", StringComparison.Ordinal);
                text = loose.Replace(
                    text,
                    "/* " + CssRosterHashMarker + " */\r\n" +
                    "                  this.m_charMCHash[allCharacters[i]] = child;",
                    1);
            }
            else
            {
                text = pattern.Replace(
                    text,
                    "/* " + CssRosterHashMarker + " */\r\n" +
                    "                  this.m_charMCHash[allCharacters[i]] = child;",
                    1);
            }

            File.WriteAllText(characterSelectMenuAsPath, text);
            return File.ReadAllText(characterSelectMenuAsPath)
                .Contains(CssRosterHashMarker, StringComparison.Ordinal);
        }

        /// <summary>
        /// After spaceObjects(), scale select chips to icon_size / vanilla pitch so art matches cell size.
        /// Idempotent via <see cref="SelectScaleMarker"/>.
        /// </summary>
        public static bool PatchSelectIconScale(string scriptsRoot)
        {
            var css = FindScriptFile(scriptsRoot, "CharacterSelectMenu.as", preferParentName: "menus");
            var sss = FindScriptFile(scriptsRoot, "StageSelectMenu.as", preferParentName: "menus");
            if (css == null || sss == null) return false;

            var cssOk = PatchCharacterSelectScale(css);
            var sssOk = PatchStageSelectScale(sss);
            return cssOk && sssOk;
        }

        public static bool PatchCharacterSelectScale(string characterSelectMenuAsPath)
        {
            var text = File.ReadAllText(characterSelectMenuAsPath);
            // Icon scale + hide stray btn_game* labels. Hands and chips are scaled
            // per page in the css editor; a one-shot here ran before the live chips
            // existed and could freeze the hand at a dense page's size.
            if (text.Contains(SelectScaleMarker, StringComparison.Ordinal) &&
                text.Contains("indexOf(\"btn_game\")", StringComparison.Ordinal) &&
                !text.Contains("HandMC.scaleX", StringComparison.Ordinal))
                return true;

            // Strip any prior SSF2MM select-scale injection (V1 or the hand/chip loop).
            text = Regex.Replace(
                text,
                @"\r?\n[ \t]*/\* SSF2MM_SELECT_SCALE(?:_V2)? \*/[\s\S]*?while\s*\(\s*i\s*<\s*(?:this\.)?m_playerChips\.length\s*\)\s*\{[\s\S]*?i\+\+\s*;\s*\}\s*;",
                "",
                RegexOptions.None);
            text = Regex.Replace(
                text,
                @"\r?\n[ \t]*(?:this\.)?m_playerChips\[i\]\.(?:MC|HandMC)\.scale[XY]\s*=\s*\([^;]+;",
                "");
            text = Regex.Replace(
                text,
                @"\r?\n[ \t]*/\* SSF2MM_SELECT_SCALE(?:_V2)? \*/\r?\n[ \t]*(?:this\.)?displayObjectTable\.scaleAll\([^;]+;",
                "");

            // No mid-block var decls (FFDec-safe). Reuse updateIcons' i/j after spaceObjects.
            var replacement =
                "this.displayObjectTable.spaceObjects();\r\n" +
                "            /* " + SelectScaleMarker + " */\r\n" +
                "            this.displayObjectTable.scaleAll((mappings.character_select_screen.icon_size.width / 42), (mappings.character_select_screen.icon_size.height / 42));\r\n" +
                "            j = 0;\r\n" +
                "            while(j < m_subMenu.numChildren)\r\n" +
                "            {\r\n" +
                "               if(m_subMenu.getChildAt(j).name == \"btn_gameMode\")\r\n" +
                "               {\r\n" +
                "                  m_subMenu.getChildAt(j).visible = false;\r\n" +
                "               };\r\n" +
                "               if(((m_subMenu.getChildAt(j) is TextField) && (TextField(m_subMenu.getChildAt(j)).text.indexOf(\"btn_game\") == 0)))\r\n" +
                "               {\r\n" +
                "                  m_subMenu.getChildAt(j).visible = false;\r\n" +
                "               };\r\n" +
                "               j++;\r\n" +
                "            };\r\n" +
                "            if(((this.m_charSelectionMC) && (this.m_charSelectionMC.charSelect)))\r\n" +
                "            {\r\n" +
                "               j = 0;\r\n" +
                "               while(j < this.m_charSelectionMC.charSelect.numChildren)\r\n" +
                "               {\r\n" +
                "                  if(this.m_charSelectionMC.charSelect.getChildAt(j).name == \"btn_gameMode\")\r\n" +
                "                  {\r\n" +
                "                     this.m_charSelectionMC.charSelect.getChildAt(j).visible = false;\r\n" +
                "                  };\r\n" +
                "                  if(((this.m_charSelectionMC.charSelect.getChildAt(j) is TextField) && (TextField(this.m_charSelectionMC.charSelect.getChildAt(j)).text.indexOf(\"btn_game\") == 0)))\r\n" +
                "                  {\r\n" +
                "                     this.m_charSelectionMC.charSelect.getChildAt(j).visible = false;\r\n" +
                "                  };\r\n" +
                "                  j++;\r\n" +
                "               };\r\n" +
                "            };";

            var patched = Regex.Replace(
                text,
                @"this\.displayObjectTable\.spaceObjects\s*\(\s*\)\s*;",
                replacement,
                RegexOptions.None);
            if (patched == text)
            {
                patched = Regex.Replace(
                    text,
                    @"displayObjectTable\.spaceObjects\s*\(\s*\)\s*;",
                    replacement.Replace("this.displayObjectTable", "displayObjectTable")
                        .Replace("this.m_playerChips", "m_playerChips")
                        .Replace("this.m_charSelectionMC", "m_charSelectionMC"),
                    RegexOptions.None);
            }

            if (patched == text ||
                !patched.Contains(SelectScaleMarker, StringComparison.Ordinal) ||
                patched.Contains("HandMC.scaleX", StringComparison.Ordinal))
                return false;

            File.WriteAllText(characterSelectMenuAsPath, patched);
            return true;
        }

        public static bool PatchStageSelectScale(string stageSelectMenuAsPath)
        {
            var text = File.ReadAllText(stageSelectMenuAsPath);
            if (text.Contains(SelectScaleMarker, StringComparison.Ordinal))
                return true;

            // show() has no mappings local — inline ResourceManager lookup (no mid-block vars).
            var replacement =
                "this.normalStageTable.spaceObjects();\r\n" +
                "            /* " + SelectScaleMarker + " */\r\n" +
                "            this.normalStageTable.scaleAll((ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.normal.icon_size.width / 48), (ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.normal.icon_size.height / 26));\r\n" +
                "            this.pastStageTable.scaleAll((ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.past.icon_size.width / 48), (ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.past.icon_size.height / 26));";

            var patched = Regex.Replace(
                text,
                @"this\.normalStageTable\.spaceObjects\s*\(\s*\)\s*;",
                replacement,
                RegexOptions.None);
            if (patched == text)
            {
                patched = Regex.Replace(
                    text,
                    @"normalStageTable\.spaceObjects\s*\(\s*\)\s*;",
                    "normalStageTable.spaceObjects();\r\n" +
                    "            /* " + SelectScaleMarker + " */\r\n" +
                    "            normalStageTable.scaleAll((ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.normal.icon_size.width / 48), (ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.normal.icon_size.height / 26));\r\n" +
                    "            pastStageTable.scaleAll((ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.past.icon_size.width / 48), (ResourceManager.getResourceByID(\"mappings\").getProp(\"metadata\").stage_select_screen.past.icon_size.height / 26));",
                    RegexOptions.None);
            }

            if (patched == text || !patched.Contains(SelectScaleMarker, StringComparison.Ordinal))
                return false;

            File.WriteAllText(stageSelectMenuAsPath, patched);
            return true;
        }

        public static bool PatchEncryptFiles(string scriptsRoot)
        {
            var config = Directory.GetFiles(scriptsRoot, "Config.as", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (config == null) return false;
            var text = File.ReadAllText(config);
            var patched = Regex.Replace(
                text,
                @"(encrypt_files\s*:\s*Boolean\s*=\s*)true\b",
                "$1false",
                RegexOptions.IgnoreCase);
            if (patched == text)
            {
                if (Regex.IsMatch(text, @"encrypt_files\s*:\s*Boolean\s*=\s*false\b", RegexOptions.IgnoreCase))
                    return true;
                return false;
            }
            File.WriteAllText(config, patched);
            return true;
        }

        public static bool PatchMainEncrypted(string scriptsRoot)
        {
            var main = Directory.GetFiles(scriptsRoot, "Main.as", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(Path.GetDirectoryName(f))
                    ?.Equals("ssf2", StringComparison.OrdinalIgnoreCase) == true)
                ?? Directory.GetFiles(scriptsRoot, "Main.as", SearchOption.AllDirectories).FirstOrDefault();
            if (main == null) return false;
            var text = File.ReadAllText(main);
            var patched = Regex.Replace(
                text,
                @"ENCRYPTED\s*:\s*Boolean\s*=\s*Config\.encrypt_files",
                "ENCRYPTED:Boolean = false",
                RegexOptions.IgnoreCase);
            if (patched == text)
            {
                patched = Regex.Replace(
                    text,
                    @"(ENCRYPTED\s*:\s*Boolean\s*=\s*)true\b",
                    "$1false",
                    RegexOptions.IgnoreCase);
            }
            if (patched == text) return false;
            File.WriteAllText(main, patched);
            return true;
        }

        public const string ResourcePackPathMarker = "SSF2MM_PACK_PATH";

        /// <summary>
        /// Legacy pack content lived under <c>data/ssf2mm/packs/…</c>, but stock Resource.Location always
        /// prefixes characters with <c>data/character/</c>. Rewrite so <c>ssf2mm/…</c> file fields
        /// resolve to <c>data/ssf2mm/…</c> (same for stages).
        /// </summary>
        public static bool PatchResourcePackPaths(string scriptsRoot)
        {
            if (!PatchResourceSsf2mmPaths(scriptsRoot))
                return false;
            foreach (var resource in Directory.GetFiles(scriptsRoot, "Resource.as", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(resource);
                var patched = AddExpansionPacksBranch(text);
                if (!patched.Equals(text, StringComparison.Ordinal))
                    File.WriteAllText(resource, patched);
            }
            return true;
        }

        private static readonly Regex Ssf2mmFileBranch = new(
            @"(?<indent>[ \t]*)if\s*\(\s*this\.m_fileName\s*!=\s*null\s*&&\s*this\.m_fileName\.indexOf\s*\(\s*""ssf2mm/""\s*\)\s*==\s*0\s*\)\s*\{\s*return\s+(?<v>\w+)\s*\+\s*""data/""\s*\+\s*this\.m_fileName\s*;\s*\}",
            RegexOptions.Singleline);

        /// <summary>
        /// Content packs live in <c>expansions/packs/&lt;id&gt;</c>. A mappings <c>file</c> that starts
        /// with <c>expansions/packs/</c> resolves from the game folder, ahead of the
        /// <c>expansions/</c> → <c>expansions/character/</c> rule.
        /// </summary>
        public static string AddExpansionPacksBranch(string resourceAs)
        {
            if (resourceAs.Contains("indexOf(\"" + ContentPackService.PacksFileFieldPrefix + "\")", StringComparison.Ordinal))
                return resourceAs;
            return Ssf2mmFileBranch.Replace(resourceAs, m =>
            {
                var i = m.Groups["indent"].Value;
                var v = m.Groups["v"].Value;
                return i + "if(this.m_fileName != null && this.m_fileName.indexOf(\"" + ContentPackService.PacksFileFieldPrefix + "\") == 0)\r\n" +
                       i + "{\r\n" +
                       i + "   return " + v + " + this.m_fileName;\r\n" +
                       i + "}\r\n" +
                       m.Value;
            });
        }

        private static bool PatchResourceSsf2mmPaths(string scriptsRoot)
        {
            var resource = Directory.GetFiles(scriptsRoot, "Resource.as", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains($"{Path.DirectorySeparatorChar}util{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                    || f.Contains("/util/", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(Path.GetDirectoryName(f))?.Equals("util", StringComparison.OrdinalIgnoreCase) == true);
            resource ??= Directory.GetFiles(scriptsRoot, "Resource.as", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (resource == null) return false;

            var text = File.ReadAllText(resource);
            text = text.Replace(
                "prefix + \"data/\" + this.m_fileName",
                "prefix + \"data/\" + this.m_fileName",
                StringComparison.Ordinal);

            var hasPackPath = text.Contains(ResourcePackPathMarker, StringComparison.Ordinal)
                || text.Contains("prefix + \"data/\" + this.m_fileName", StringComparison.Ordinal);
            var hasExpChar = text.Contains("expansions/character/", StringComparison.Ordinal)
                && text.Contains("indexOf(\"expansions/\")", StringComparison.Ordinal);

            if (hasPackPath && hasExpChar)
            {
                if (!text.Equals(File.ReadAllText(resource), StringComparison.Ordinal))
                    File.WriteAllText(resource, text);
                return true;
            }

            if (hasPackPath && !hasExpChar)
            {
                // Insert expansions/character remapping after the ssf2mm branch in CHARACTER block.
                var insertPat = new Regex(
                    @"(if\s*\(\s*this\.m_fileName\s*!=\s*null\s*&&\s*this\.m_fileName\.indexOf\s*\(\s*""ssf2mm/""\s*\)\s*==\s*0\s*\)\s*\{\s*" +
                    @"return\s+prefix\s*\+\s*""data/""\s*\+\s*this\.m_fileName\s*;\s*" +
                    @"\})",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (insertPat.IsMatch(text))
                {
                    text = insertPat.Replace(text,
                        "if(this.m_fileName != null && this.m_fileName.indexOf(\"ssf2mm/\") == 0)\r\n" +
                        "            {\r\n" +
                        "               return prefix + \"data/\" + this.m_fileName;\r\n" +
                        "            }\r\n" +
                        "            if(this.m_fileName != null && this.m_fileName.indexOf(\"expansions/\") == 0)\r\n" +
                        "            {\r\n" +
                        "               return prefix + \"expansions/character/\" + this.m_fileName.substring(\"expansions/\".length);\r\n" +
                        "            }",
                        1);
                    File.WriteAllText(resource, text);
                    return true;
                }
            }

            if (text.Contains(ResourcePackPathMarker, StringComparison.Ordinal)
                || text.Contains("indexOf(\"ssf2mm/\")", StringComparison.Ordinal)
                || text.Contains("indexOf('ssf2mm/')", StringComparison.Ordinal))
            {
                File.WriteAllText(resource, text);
                return text.Contains("prefix + \"data/\" + this.m_fileName", StringComparison.Ordinal);
            }

            var charPat = new Regex(
                @"(if\s*\(\s*this\.m_type\s*==\s*Resource\.CHARACTER\s*\|\|\s*this\.m_type\s*==\s*Resource\.CHARACTER_EXPANSION\s*\)\s*\{\s*" +
                @"if\s*\(\s*Main\.ENCRYPTED\s*\)\s*\{\s*" +
                @"return\s+prefix\s*\+\s*Resource\.DIRECTORY_CHAR_ENCRYPTED\s*\+\s*this\.m_encryptedFileName\s*;\s*" +
                @"\}\s*" +
                @"return\s+prefix\s*\+\s*Resource\.DIRECTORY_CHAR\s*\+\s*this\.m_fileName\s*;\s*" +
                @"\})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!charPat.IsMatch(text))
                return false;

            text = charPat.Replace(text,
                "if(this.m_type == Resource.CHARACTER || this.m_type == Resource.CHARACTER_EXPANSION)\r\n" +
                "         {\r\n" +
                "            if(Main.ENCRYPTED)\r\n" +
                "            {\r\n" +
                "               return prefix + Resource.DIRECTORY_CHAR_ENCRYPTED + this.m_encryptedFileName;\r\n" +
                "            }\r\n" +
                "            /* " + ResourcePackPathMarker + " */\r\n" +
                "            if(this.m_fileName != null && this.m_fileName.indexOf(\"ssf2mm/\") == 0)\r\n" +
                "            {\r\n" +
                "               return prefix + \"data/\" + this.m_fileName;\r\n" +
                "            }\r\n" +
                "            if(this.m_fileName != null && this.m_fileName.indexOf(\"expansions/\") == 0)\r\n" +
                "            {\r\n" +
                "               return prefix + \"expansions/character/\" + this.m_fileName.substring(\"expansions/\".length);\r\n" +
                "            }\r\n" +
                "            return prefix + Resource.DIRECTORY_CHAR + this.m_fileName;\r\n" +
                "         }",
                1);

            var stagePat = new Regex(
                @"(if\s*\(\s*this\.m_type\s*==\s*Resource\.STAGE\s*\|\|\s*this\.m_type\s*==\s*Resource\.STAGE_EXPANSION\s*\)\s*\{\s*" +
                @"if\s*\(\s*Main\.ENCRYPTED\s*\)\s*\{\s*" +
                @"return\s+prefix\s*\+\s*Resource\.DIRECTORY_STAGE_ENCRYPTED\s*\+\s*this\.m_encryptedFileName\s*;\s*" +
                @"\}\s*" +
                @"return\s+prefix\s*\+\s*Resource\.DIRECTORY_STAGE\s*\+\s*this\.m_fileName\s*;\s*" +
                @"\})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (stagePat.IsMatch(text))
            {
                text = stagePat.Replace(text,
                    "if(this.m_type == Resource.STAGE || this.m_type == Resource.STAGE_EXPANSION)\r\n" +
                    "         {\r\n" +
                    "            if(Main.ENCRYPTED)\r\n" +
                    "            {\r\n" +
                    "               return prefix + Resource.DIRECTORY_STAGE_ENCRYPTED + this.m_encryptedFileName;\r\n" +
                    "            }\r\n" +
                    "            /* " + ResourcePackPathMarker + " */\r\n" +
                    "            if(this.m_fileName != null && this.m_fileName.indexOf(\"ssf2mm/\") == 0)\r\n" +
                    "            {\r\n" +
                    "               return prefix + \"data/\" + this.m_fileName;\r\n" +
                    "            }\r\n" +
                    "            return prefix + Resource.DIRECTORY_STAGE + this.m_fileName;\r\n" +
                    "         }",
                    1);
            }

            File.WriteAllText(resource, text);
            return File.ReadAllText(resource).Contains("prefix + \"data/\" + this.m_fileName", StringComparison.Ordinal);
        }

        public async Task EnsureResourcePackPathsOnInstallAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");
            if (SwfContainsAscii(mainSwf, "expansions/character/")
                && SwfContainsAscii(mainSwf, ResourcePackPathMarker))
            {
                progress?.Report("Resource pack-path patch already present.");
                return;
            }

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-respath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.RunFfdecAsync(
                    jpexsPath ?? "",
                    $"-selectclass {ScriptNameResource} -export script \"{exportDir}\" \"{mainSwf}\"",
                    cancellationToken);
                var scriptsDir = Directory.Exists(Path.Combine(exportDir, "scripts"))
                    ? Path.Combine(exportDir, "scripts")
                    : exportDir;
                if (!PatchResourcePackPaths(scriptsDir))
                    throw new InvalidOperationException("Resource pack-path patch failed.");

                var resourceAs = FindScriptFile(scriptsDir, "Resource.as", preferParentName: "util")
                    ?? throw new FileNotFoundException("Resource.as not found.");
                var outfile = Path.Combine(work, "ssf2-respath.swf");
                progress?.Report("Replacing Resource.as (expansions pack paths)…");
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", mainSwf, outfile, ScriptNameResource, resourceAs, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("FFDec did not produce patched SSF2.swf.");
                File.Copy(outfile, mainSwf, overwrite: true);
                progress?.Report("Resource.as now loads data/ssf2mm/ and expansions/character/ paths.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Patches Resource.initialLoadComplete so raw FWS/CWS/ZWS bytes are never passed through
        /// ByteArray.uncompress (which can corrupt the buffer on failure and black-screen matches).
        /// Must stay valid AS3 for FFDec -importScript (no mid-block var decls, no unicode punctuation).
        /// </summary>
        public static bool PatchResourceAllowDecompressed(string scriptsRoot)
        {
            var resource = Directory.GetFiles(scriptsRoot, "Resource.as", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains($"{Path.DirectorySeparatorChar}util{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                    || f.Contains("/util/", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(Path.GetDirectoryName(f))?.Equals("util", StringComparison.OrdinalIgnoreCase) == true);
            resource ??= Directory.GetFiles(scriptsRoot, "Resource.as", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (resource == null) return false;

            var text = File.ReadAllText(resource);
            // Good form: locals at top, no mid-block `var isRawSwf:Boolean = b.length` (that breaks FFDec).
            if (text.Contains("SSF2MM_RAW_SWF", StringComparison.Ordinal) &&
                text.Contains("var rawCopy:ByteArray = null", StringComparison.Ordinal) &&
                !Regex.IsMatch(text, @"var isRawSwf:Boolean\s*=\s*b\.length", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // One-shot replace of the whole local+uncompress block (stock or prior SSF2MM attempts).
            // Catch body must be matched explicitly: a non-greedy `[\s\S]*?` stops at the nested
            // `if (m_id !== "menu_news") { return; }` brace and leaves stray `}` for FFDec.
            const string replacement =
                "var c:ByteArray = null;\r\n" +
                "            var l:int = 0;\r\n" +
                "            var n:int = 0;\r\n" +
                "            var x:int = 0;\r\n" +
                "            var rawCopy:ByteArray = null;\r\n" +
                "            var isRawSwf:Boolean = false;\r\n" +
                "            var b:ByteArray = m_urlLoader.data as ByteArray;\r\n" +
                "            rawCopy = new ByteArray();\r\n" +
                "            rawCopy.writeBytes(b);\r\n" +
                "            c = new ByteArray();\r\n" +
                "            isRawSwf = b.length >= 3 && (b[0] == 70 || b[0] == 67 || b[0] == 90) && b[1] == 87 && b[2] == 83;\r\n" +
                "            if(!isRawSwf)\r\n" +
                "            {\r\n" +
                "               try\r\n" +
                "               {\r\n" +
                "                  b.uncompress(CompressionAlgorithm.ZLIB);\r\n" +
                "                  l = b.readInt();\r\n" +
                "                  n = b.readInt();\r\n" +
                "                  for(x = 0; x < n; x++)\r\n" +
                "                  {\r\n" +
                "                     b.readInt();\r\n" +
                "                  }\r\n" +
                "                  c.writeBytes(b,b.position,l);\r\n" +
                "                  b = c;\r\n" +
                "                  c = null;\r\n" +
                "               }\r\n" +
                "               catch(err:*)\r\n" +
                "               {\r\n" +
                "                  b = rawCopy;\r\n" +
                "               }\r\n" +
                "            }\r\n" +
                "            // SSF2MM_RAW_SWF\r\n";

            const string catchBody =
                @"(?:" +
                // Stock public Beta
                @"\s*if\s*\(\s*m_id\s*!==\s*""menu_news""\s*\)\s*\{\s*return\s*;\s*\}\s*" +
                @"|" +
                // Prior SSF2MM / empty catch
                @"\s*(?:b\s*=\s*rawCopy\s*;\s*|//[^\r\n]*\s*)?" +
                @")";

            var patched = Regex.Replace(
                text,
                @"var c:ByteArray\s*=\s*null\s*;\s*" +
                @"(?:var (?:rawCopy:ByteArray|isRawSwf:Boolean|l:int|n:int|x:int)\s*=\s*[^;]+;\s*)*" +
                @"var b:ByteArray\s*=\s*m_urlLoader\.data as ByteArray\s*;\s*" +
                @"(?:rawCopy\s*=\s*new ByteArray\(\)\s*;\s*rawCopy\.writeBytes\(b\)\s*;\s*)?" +
                @"c\s*=\s*new ByteArray\(\)\s*;\s*" +
                @"(?://[^\r\n]*\r?\n\s*)?" +
                @"(?:isRawSwf\s*=\s*b\.length[\s\S]*?if\s*\(\s*!isRawSwf\s*\)\s*\{\s*)?" +
                @"(?:var isRawSwf:Boolean\s*=\s*b\.length[\s\S]*?if\s*\(\s*!isRawSwf\s*\)\s*\{\s*)?" +
                @"try\s*\{\s*" +
                @"b\.uncompress\(CompressionAlgorithm\.ZLIB\)\s*;\s*" +
                @"l\s*=\s*b\.readInt\(\)\s*;\s*" +
                @"n\s*=\s*b\.readInt\(\)\s*;\s*" +
                @"(?:for\s*\(\s*x\s*=\s*0\s*;\s*x\s*<\s*n\s*;\s*x\s*\+\+\s*\)\s*\{\s*b\.readInt\(\)\s*;\s*\}|" +
                @"x\s*=\s*0\s*;\s*while\s*\(\s*x\s*<\s*n\s*\)\s*\{\s*b\.readInt\(\)\s*;\s*x\s*\+\+\s*;\s*\})" +
                @"\s*" +
                @"c\.writeBytes\s*\(\s*b\s*,\s*b\.position\s*,\s*l\s*\)\s*;\s*" +
                @"b\s*=\s*c\s*;\s*" +
                @"c\s*=\s*null\s*;\s*" +
                @"\}\s*" +
                @"catch\s*\(\s*err\s*:\s*\*\s*\)\s*\{" + catchBody + @"\}" +
                @"(?:\s*//\s*SSF2MM_RAW_SWF\s*)?" +
                @"(?:\s*\})?", // close leftover if(!isRawSwf) from a prior partial patch
                replacement,
                RegexOptions.IgnoreCase);

            if (!patched.Contains("SSF2MM_RAW_SWF", StringComparison.Ordinal) ||
                !patched.Contains("var rawCopy:ByteArray = null", StringComparison.Ordinal) ||
                Regex.IsMatch(patched, @"var isRawSwf:Boolean\s*=\s*b\.length", RegexOptions.IgnoreCase) ||
                patched == text)
            {
                return false;
            }

            // Defensive: remove any orphan braces left immediately after our marker.
            patched = Regex.Replace(
                patched,
                @"// SSF2MM_RAW_SWF\s*(?:\r?\n\s*\})+",
                "// SSF2MM_RAW_SWF\r\n",
                RegexOptions.IgnoreCase);

            File.WriteAllText(resource, patched);
            return true;
        }

        private void RepairOrphanDatSwfs(string dataDir, IProgress<string>? progress)
        {
            if (!Directory.Exists(dataDir)) return;
            foreach (var path in Directory.GetFiles(dataDir, "DAT*.swf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (PressorService.LooksLikeSwf(bytes)) continue;
                    if (!PressorService.TryParseGarbageHeader(bytes, out var swf)) continue;
                    File.WriteAllBytes(path, swf);
                    progress?.Report($"  Repaired orphan header on {Path.GetFileName(path)}");
                }
                catch
                {
                    // ignore per-file repair failures
                }
            }
        }

        private (int characterId, string? key, byte[]? rewritten) RewriteManifestBinary(
            string exportDir,
            IProgress<string>? progress)
        {
            var resourceManager = Directory.GetFiles(exportDir, "ResourceManager.as", SearchOption.AllDirectories)
                .FirstOrDefault();
            var manifestBin = Directory.GetFiles(exportDir, "*.bin", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Contains(
                    "ResourceManager_manifestJSON", StringComparison.OrdinalIgnoreCase));
            if (resourceManager == null || manifestBin == null)
            {
                progress?.Report("Manifest binary or ResourceManager.as missing from export.");
                return (0, null, null);
            }

            var key = DatMappingService.ExtractManifestKey(File.ReadAllText(resourceManager));
            var blob = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(manifestBin))
                .Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
            var json = DatMappingService.DecodeJsonBlob(blob, key);
            var rewrittenJson = DatMappingService.RewriteFileExtensionsToSwf(json);
            var encoded = DatMappingService.EncodeJsonBlob(rewrittenJson, string.IsNullOrWhiteSpace(key) ? null : key);

            var idMatch = Regex.Match(Path.GetFileName(manifestBin), @"^(\d+)_");
            var id = idMatch.Success ? int.Parse(idMatch.Groups[1].Value) : 0;
            return (id, key, encoded);
        }

        private async Task RewriteMappingsSwfFileFieldsAsync(
            string mappingsSwfPath,
            string? jpexsPath,
            string workRoot,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(mappingsSwfPath))
                throw new FileNotFoundException("mappings.swf not found.", mappingsSwfPath);

            var exportDir = Path.Combine(workRoot, "mappings-meta");
            Directory.CreateDirectory(exportDir);
            await _jpexs.ExportAsync(
                jpexsPath ?? "", mappingsSwfPath, exportDir, "binaryData", cancellationToken);

            var metadataBinary = Directory.GetFiles(exportDir, "*.bin", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault(f => Path.GetFileName(f).Contains(
                    "mappingsJSON", StringComparison.OrdinalIgnoreCase))
                ?? Directory.GetFiles(exportDir, "*.bin", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
            if (metadataBinary == null)
                throw new InvalidOperationException("No mappingsJSON binary found in mappings.swf.");

            const string mappingsKey = "b7d60849-fa99-4514-b41e-16693ce7acc0";
            var blob = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(metadataBinary))
                .Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
            var json = DatMappingService.DecodeJsonBlob(blob, mappingsKey);
            var rewrittenJson = DatMappingService.RewriteFileExtensionsToSwf(json);
            if (Regex.IsMatch(rewrittenJson, "\"file\"\\s*:\\s*\"[^\"]+\\.ssf\"", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("mappings rewrite left .ssf file fields behind.");

            var encoded = DatMappingService.EncodeJsonBlob(rewrittenJson, mappingsKey);
            var newBin = Path.Combine(workRoot, "mappings-rewritten.bin");
            await File.WriteAllBytesAsync(newBin, encoded, cancellationToken);

            var idMatch = Regex.Match(Path.GetFileName(metadataBinary), @"^(\d+)_");
            if (!idMatch.Success)
                throw new InvalidOperationException("Could not parse mappings binary character id.");

            var outSwf = Path.Combine(workRoot, "mappings-patched.swf");
            await _jpexs.ReplaceAsync(
                jpexsPath ?? "",
                mappingsSwfPath,
                outSwf,
                idMatch.Groups[1].Value,
                newBin,
                cancellationToken);
            File.Copy(outSwf, mappingsSwfPath, overwrite: true);
            progress?.Report("mappings.swf metadata updated to .swf file names.");
        }

        /// <summary>
        /// Optional patch: bridge <c>SSF2API.print</c> + uncaught errors to
        /// <c>127.0.0.1:DebugConsolePort</c> for the Mod Manager debug window.
        /// Idempotent via <see cref="DebugConsoleMarker"/>.
        /// </summary>
        public async Task<GamePrepareResult> InstallDebugConsoleAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool force = false)
        {
            var result = new GamePrepareResult();
            if (!IsPrepared(installRoot))
                throw new InvalidOperationException(
                    "Prepare the install first, then install the debug console patch.");

            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            var status = GetDebugConsolePatchStatusInSwf(mainSwf);
            var hasSpawn = SwfContainsAscii(mainSwf, DebugHostSpawnMarker);
            if (status == DebugConsolePatchStatus.Full && hasSpawn && !force)
            {
                result.Success = true;
                result.Message = "Debug console patch already present in SSF2.swf (prints + uncaught + CMD host spawn).";
                progress?.Report(result.Message);
                return result;
            }

            if (status == DebugConsolePatchStatus.PrintOnly || (status == DebugConsolePatchStatus.Full && !hasSpawn))
                progress?.Report("Upgrading debug patch (uncaught errors + CMD host spawn)…");

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-debug-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Exporting SSF2.swf scripts for debug console patch…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(jpexsPath ?? "", mainSwf, exportDir, "script", cancellationToken);
                var scriptsDir = Path.Combine(exportDir, "scripts");
                if (!Directory.Exists(scriptsDir))
                    scriptsDir = exportDir;

                // Only patch SSF2API (print + uncaught via LoaderInfo). Do NOT touch Main.as —
                // FFDec replaceAS3 on Main has crashed the Beta projector at ~95% load.
                var apiPatched = PatchSsf2ApiDebugConsole(scriptsDir);
                if (!apiPatched)
                {
                    result.Success = true;
                    result.Message = "Debug console patch already present.";
                    progress?.Report(result.Message);
                    return result;
                }

                var apiAs = FindScriptFile(scriptsDir, "SSF2API.as")
                    ?? throw new InvalidOperationException("SSF2API.as not found after patch.");
                var scriptName = ResolveScriptReplaceName(apiAs, "com.mcleodgaming.ssf2.api.SSF2API");

                progress?.Report("Replacing SSF2API.print with debug console bridge…");
                var outfile = Path.Combine(work, "ssf2-debug.swf");
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", mainSwf, outfile, scriptName, apiAs, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("FFDec did not produce a patched SSF2.swf.");
                File.Copy(outfile, mainSwf, overwrite: true);

                result.Success = true;
                result.Message =
                    $"Debug console patch installed (SSF2API.print → port {DebugConsolePort}). " +
                    "Open Game Debug Console, then launch the game.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Turns on the game's compiled-off <c>Main.DEBUG</c> flag by flipping
        /// <c>DEBUGCONST</c> / <c>FORCEDEBUGOFF</c> only (no other Main.as changes).
        /// Enables the in-game debug console that <c>SSF2API.print</c> writes to when DEBUG is on.
        /// </summary>
        public async Task<GamePrepareResult> EnableInGameDebugFlagAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            if (!IsPrepared(installRoot))
                throw new InvalidOperationException("Prepare the install first.");

            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-debugflag-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Exporting Main.as to enable DEBUG flag…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.RunFfdecAsync(
                    jpexsPath ?? "",
                    $"-selectclass {ScriptNameMain} -export script \"{exportDir}\" \"{mainSwf}\"",
                    cancellationToken);
                var mainAs = FindScriptFile(exportDir, "Main.as", preferParentName: "ssf2")
                             ?? FindScriptFile(exportDir, "Main.as")
                             ?? throw new InvalidOperationException("Main.as not found.");

                var text = await File.ReadAllTextAsync(mainAs, cancellationToken);
                var next = text;
                next = Regex.Replace(
                    next,
                    @"DEBUGCONST\s*:\s*Boolean\s*=\s*false\s*&&\s*false",
                    "DEBUGCONST:Boolean = true",
                    RegexOptions.IgnoreCase);
                next = Regex.Replace(
                    next,
                    @"FORCEDEBUGOFF\s*:\s*Boolean\s*=\s*true\s*\|\|\s*true",
                    "FORCEDEBUGOFF:Boolean = false",
                    RegexOptions.IgnoreCase);
                next = Regex.Replace(
                    next,
                    @"DEBUGAUTHED\s*:\s*Boolean\s*=\s*false\s*;",
                    "DEBUGAUTHED:Boolean = true;",
                    RegexOptions.IgnoreCase);

                if (string.Equals(next, text, StringComparison.Ordinal))
                {
                    result.Success = true;
                    result.Message = "Main.DEBUG constants already look enabled (or pattern not found).";
                    progress?.Report(result.Message);
                    return result;
                }

                await File.WriteAllTextAsync(mainAs, next, cancellationToken);
                var outfile = Path.Combine(work, "ssf2-debugflag.swf");
                progress?.Report("Replacing Main.DEBUG constants in SSF2.swf…");
                var bak = mainSwf + ".ssf2mm.debugflag.bak";
                if (!File.Exists(bak)) File.Copy(mainSwf, bak, overwrite: false);
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", mainSwf, outfile, ScriptNameMain, mainAs, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("FFDec did not produce patched SSF2.swf.");
                File.Copy(outfile, mainSwf, overwrite: true);

                result.Success = true;
                result.Message =
                    "Main.DEBUG enabled (DEBUGCONST=true, FORCEDEBUGOFF=false). " +
                    "If load crashes at ~95%, restore from SSF2.swf.ssf2mm.debugflag.bak.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// ADL cannot load the 64-bit nativejoystick ANE (32-bit adl). Without the ANE,
        /// <c>NativeJoystickMgr.detectIntervalMillis</c> NPEs in Main(). Wrap native setup
        /// and fall back to Flash <c>GameInput</c>.
        /// </summary>
        public async Task<GamePrepareResult> PatchGamepadManagerForAdlAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GamePrepareResult();
            if (!IsPrepared(installRoot))
                throw new InvalidOperationException("Prepare the install first.");

            var mainSwf = FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");

            if (SwfContainsAscii(mainSwf, GamepadAdlSafeMarker))
            {
                result.Success = true;
                result.Message = "GamepadManager ADL-safe patch already present.";
                progress?.Report(result.Message);
                return result;
            }

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-gamepad-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.RunFfdecAsync(
                    jpexsPath ?? "",
                    $"-selectclass {ScriptNameGamepadManager} -export script \"{exportDir}\" \"{mainSwf}\"",
                    cancellationToken);
                var asPath = FindScriptFile(exportDir, "GamepadManager.as", preferParentName: "input")
                             ?? FindScriptFile(exportDir, "GamepadManager.as")
                             ?? throw new InvalidOperationException("GamepadManager.as not found.");

                if (!PatchGamepadManagerAdlSafe(asPath))
                {
                    result.Success = false;
                    result.Message = "Could not patch GamepadManager.setupGameInputNative for ADL.";
                    progress?.Report(result.Message);
                    return result;
                }

                var outfile = Path.Combine(work, "ssf2-gamepad.swf");
                progress?.Report("Replacing GamepadManager in SSF2.swf…");
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", mainSwf, outfile, ScriptNameGamepadManager, asPath, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("FFDec did not produce patched SSF2.swf.");
                File.Copy(outfile, mainSwf, overwrite: true);

                result.Success = true;
                result.Message = "GamepadManager falls back to GameInput when NativeJoystick ANE is missing (ADL).";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public static bool PatchGamepadManagerAdlSafe(string gamepadManagerAsPath)
        {
            var text = File.ReadAllText(gamepadManagerAsPath);
            if (text.Contains(GamepadAdlSafeMarker, StringComparison.Ordinal))
                return true;

            // Replace setupGameInputNative so a null ExtensionContext (ADL without ANE)
            // falls back to setupGameInput() instead of Error #1009 in Main().
            var pattern = new Regex(
                @"protected\s+static\s+function\s+setupGameInputNative\s*\(\s*\)\s*:\s*void\s*\{",
                RegexOptions.IgnoreCase);
            var match = pattern.Match(text);
            if (!match.Success) return false;
            var brace = text.IndexOf('{', match.Index);
            if (brace < 0) return false;
            var depth = 0;
            var end = -1;
            for (var i = brace; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i + 1;
                        break;
                    }
                }
            }
            if (end < 0) return false;

            var wrapped =
                "protected static function setupGameInputNative() : void\r\n" +
                "      {\r\n" +
                "         try\r\n" +
                "         {\r\n" +
                "            GamepadManager.m_nativeJoystick = new NativeJoystickMgr();\r\n" +
                "            GamepadManager.m_nativeJoystick.pollInterval = 20;\r\n" +
                "            GamepadManager.m_nativeJoystick.detectIntervalMillis = 500;\r\n" +
                "            GamepadManager.m_nativeJoystick.addEventListener(NativeJoystickEvent.JOY_PLUGGED,onDeviceAttachedNative);\r\n" +
                "            GamepadManager.m_nativeJoystick.addEventListener(NativeJoystickEvent.JOY_UNPLUGGED,onDeviceRemovedNative);\r\n" +
                "         }\r\n" +
                "         catch(eNative:*)\r\n" +
                "         {\r\n" +
                "            try\r\n" +
                "            {\r\n" +
                "               trace(\"[" + GamepadAdlSafeMarker + "] NativeJoystick unavailable; using GameInput. \" + eNative);\r\n" +
                "            }\r\n" +
                "            catch(eTrace:*)\r\n" +
                "            {\r\n" +
                "            };\r\n" +
                "            GamepadManager.m_nativeJoystick = null;\r\n" +
                "            GamepadManager.setupGameInput();\r\n" +
                "         };\r\n" +
                "      }";

            text = text.Substring(0, match.Index) + wrapped + text.Substring(end);
            File.WriteAllText(gamepadManagerAsPath, text);
            return File.ReadAllText(gamepadManagerAsPath).Contains(GamepadAdlSafeMarker, StringComparison.Ordinal);
        }

        private static string ResolveScriptReplaceName(string asPath, string fallback)
        {
            try
            {
                var text = File.ReadAllText(asPath);
                var pkg = Regex.Match(text, @"^\s*package\s+([\w\.]+)\s*\{", RegexOptions.Multiline);
                var cls = Regex.Match(text, @"\b(?:public\s+)?(?:dynamic\s+)?class\s+(\w+)", RegexOptions.Multiline);
                if (pkg.Success && cls.Success)
                    return pkg.Groups[1].Value + "." + cls.Groups[1].Value;
                if (cls.Success)
                    return cls.Groups[1].Value;
            }
            catch { /* ignore */ }
            return fallback;
        }

        /// <summary>
        /// Rewrites <c>SSF2API.print</c> to also <c>trace</c> and send lines to the debug socket,
        /// and registers an uncaught-error hook via <c>LoaderInfo.getLoaderInfoByDefinition</c>
        /// (avoids patching <c>Main.as</c>, which has crashed Beta load when replaced).
        /// </summary>
        public static bool PatchSsf2ApiDebugConsole(string scriptsRoot)
        {
            var api = FindScriptFile(scriptsRoot, "SSF2API.as");
            if (api == null) return false;
            var text = File.ReadAllText(api);
            // Full V2 + host spawn already present.
            if (text.Contains(DebugConsoleMarkerV2, StringComparison.Ordinal)
                && text.Contains("ssf2mmOnUncaught", StringComparison.Ordinal)
                && text.Contains("ssf2mmDebugSend", StringComparison.Ordinal)
                && text.Contains(DebugHostSpawnMarker, StringComparison.Ordinal))
                return false;

            // Upgrade / rewrite older bridge (V1, print-only, or V2 without host spawn).
            if (text.Contains("ssf2mmDebugSend", StringComparison.Ordinal)
                || text.Contains("_ssf2mmSock", StringComparison.Ordinal)
                || text.Contains(DebugConsoleMarkerV2, StringComparison.Ordinal))
            {
                return UpgradeSsf2ApiDebugBridge(api, text);
            }

            var printMatch = Regex.Match(
                text,
                @"public\s+static\s+function\s+print\s*\(\s*(?<p>\w+)\s*:\s*String\s*\)\s*:\s*void\s*\{",
                RegexOptions.IgnoreCase);
            if (!printMatch.Success)
                return false;

            var param = printMatch.Groups["p"].Value;
            var bodyOpen = printMatch.Index + printMatch.Length - 1; // index of '{'
            var bodyClose = FindMatchingBrace(text, bodyOpen);
            if (bodyClose < 0)
                return false;

            // Static fields for socket bridge — insert after class opening fields.
            var fieldAnchor = Regex.Match(
                text,
                @"private\s+static\s+var\s+_api\s*:\s*\w+\s*;",
                RegexOptions.IgnoreCase);
            if (!fieldAnchor.Success)
            {
                fieldAnchor = Regex.Match(
                    text,
                    @"private\s+static\s+var\s+m_api\s*:\s*\*\s*;",
                    RegexOptions.IgnoreCase);
            }
            if (fieldAnchor.Success)
            {
                var insertAt = fieldAnchor.Index + fieldAnchor.Length;
                var fields =
                    "\r\n      private static var _ssf2mmSock:* = null;\r\n" +
                    "      private static var _ssf2mmBuf:Array = null;\r\n" +
                    "      private static var _ssf2mmConnecting:Boolean = false;\r\n" +
                    "      private static var _ssf2mmHooked:Boolean = false;\r\n" +
                    "      private static var _ssf2mmHostProc:* = null;\r\n" +
                    "      private static var _ssf2mmHostStarted:Boolean = false;";
                text = text.Substring(0, insertAt) + fields + text.Substring(insertAt);
                // Re-find print after edit.
                printMatch = Regex.Match(
                    text,
                    @"public\s+static\s+function\s+print\s*\(\s*(?<p>\w+)\s*:\s*String\s*\)\s*:\s*void\s*\{",
                    RegexOptions.IgnoreCase);
                if (!printMatch.Success) return false;
                param = printMatch.Groups["p"].Value;
                bodyOpen = printMatch.Index + printMatch.Length - 1;
                bodyClose = FindMatchingBrace(text, bodyOpen);
                if (bodyClose < 0) return false;
            }

            var helpers = BuildSsf2ApiDebugHelpers();

            // Keep original print body (in-game debug console) and also bridge to Mod Manager.
            // First printReplacement in PatchSsf2ApiDebugConsole (fresh install)
            var printReplacement =
                "public static function print(" + param + ":String) : void\r\n" +
                "      {\r\n" +
                "         /* " + DebugConsoleMarker + " */\r\n" +
                "         try\r\n" +
                "         {\r\n" +
                "            ssf2mmEnsureDebugHost();\r\n" +
                "            ssf2mmEnsureUncaughtHook();\r\n" +
                "            trace(" + param + ");\r\n" +
                "            ssf2mmDebugSend(" + param + ");\r\n" +
                "         }\r\n" +
                "         catch(e0:*)\r\n" +
                "         {\r\n" +
                "         }\r\n" +
                "         try\r\n" +
                "         {\r\n" +
                "            if(Main.DEBUG && Boolean(MenuController.debugConsole))\r\n" +
                "            {\r\n" +
                "               MenuController.debugConsole.writeTextData(" + param + ");\r\n" +
                "            }\r\n" +
                "         }\r\n" +
                "         catch(eOrig:*)\r\n" +
                "         {\r\n" +
                "         }\r\n" +
                "      }" +
                helpers;

            var next = text.Substring(0, printMatch.Index) + printReplacement + text.Substring(bodyClose + 1);
            if (!next.Contains("ssf2mmDebugSend", StringComparison.Ordinal)
                || !next.Contains("ssf2mmOnUncaught", StringComparison.Ordinal)
                || !next.Contains(DebugConsoleMarkerV2, StringComparison.Ordinal)
                || !next.Contains(DebugHostSpawnMarker, StringComparison.Ordinal))
                return false;
            File.WriteAllText(api, next);
            return true;
        }

        /// <summary>
        /// Upgrades an older socket bridge to V2 (uncaught + policy-friendly reconnect + hello).
        /// Rewrites print() and replaces all ssf2mm* helpers.
        /// </summary>
        private static bool UpgradeSsf2ApiDebugBridge(string apiPath, string text)
        {
            // Already fully upgraded (V2 + host spawn).
            if (text.Contains(DebugConsoleMarkerV2, StringComparison.Ordinal)
                && text.Contains("ssf2mmOnUncaught", StringComparison.Ordinal)
                && text.Contains(DebugHostSpawnMarker, StringComparison.Ordinal))
                return false;

            if (!text.Contains("_ssf2mmHostStarted", StringComparison.Ordinal))
            {
                var hostFieldAnchor = Regex.Match(
                    text,
                    @"private\s+static\s+var\s+_ssf2mmHooked\s*:\s*Boolean\s*=\s*false\s*;",
                    RegexOptions.IgnoreCase);
                if (hostFieldAnchor.Success)
                {
                    var insertAt = hostFieldAnchor.Index + hostFieldAnchor.Length;
                    text = text.Substring(0, insertAt) +
                           "\r\n      private static var _ssf2mmHostProc:* = null;" +
                           "\r\n      private static var _ssf2mmHostStarted:Boolean = false;" +
                           text.Substring(insertAt);
                }
            }

            if (!text.Contains("_ssf2mmHooked", StringComparison.Ordinal))
            {
                var fieldAnchor = Regex.Match(
                    text,
                    @"private\s+static\s+var\s+_ssf2mmConnecting\s*:\s*Boolean\s*=\s*false\s*;",
                    RegexOptions.IgnoreCase);
                if (fieldAnchor.Success)
                {
                    var insertAt = fieldAnchor.Index + fieldAnchor.Length;
                    text = text.Substring(0, insertAt) +
                           "\r\n      private static var _ssf2mmHooked:Boolean = false;" +
                           "\r\n      private static var _ssf2mmHostProc:* = null;" +
                           "\r\n      private static var _ssf2mmHostStarted:Boolean = false;" +
                           text.Substring(insertAt);
                }
            }

            var printMatch = Regex.Match(
                text,
                @"public\s+static\s+function\s+print\s*\(\s*(?<p>\w+)\s*:\s*String\s*\)\s*:\s*void\s*\{",
                RegexOptions.IgnoreCase);
            if (!printMatch.Success) return false;
            var param = printMatch.Groups["p"].Value;
            var bodyOpen = printMatch.Index + printMatch.Length - 1;
            var bodyClose = FindMatchingBrace(text, bodyOpen);
            if (bodyClose < 0) return false;

            // Strip old helper block after print (from first ssf2mm* function through last).
            var afterPrint = text.Substring(bodyClose + 1);
            var helpersStart = Regex.Match(
                afterPrint,
                @"\r?\n\s*private\s+static\s+function\s+ssf2mm",
                RegexOptions.IgnoreCase);
            if (helpersStart.Success)
            {
                // Remove contiguous ssf2mm* helper functions after print.
                var cursor = helpersStart.Index;
                while (true)
                {
                    var m = Regex.Match(
                        afterPrint.Substring(cursor),
                        @"^\s*private\s+static\s+function\s+ssf2mm\w+\s*\([^)]*\)\s*:\s*\w+\s*\{",
                        RegexOptions.IgnoreCase);
                    if (!m.Success) break;
                    var open = cursor + m.Index + m.Length - 1;
                    var close = FindMatchingBrace(afterPrint, open);
                    if (close < 0) break;
                    cursor = close + 1;
                }
                afterPrint = afterPrint.Substring(0, helpersStart.Index) + afterPrint.Substring(cursor);
            }

            var printReplacement =
                "public static function print(" + param + ":String) : void\r\n" +
                "      {\r\n" +
                "         /* " + DebugConsoleMarker + " */\r\n" +
                "         try\r\n" +
                "         {\r\n" +
                "            ssf2mmEnsureDebugHost();\r\n" +
                "            ssf2mmEnsureUncaughtHook();\r\n" +
                "            trace(" + param + ");\r\n" +
                "            ssf2mmDebugSend(" + param + ");\r\n" +
                "         }\r\n" +
                "         catch(e0:*)\r\n" +
                "         {\r\n" +
                "         }\r\n" +
                "         try\r\n" +
                "         {\r\n" +
                "            if(Main.DEBUG && Boolean(MenuController.debugConsole))\r\n" +
                "            {\r\n" +
                "               MenuController.debugConsole.writeTextData(" + param + ");\r\n" +
                "            }\r\n" +
                "         }\r\n" +
                "         catch(eOrig:*)\r\n" +
                "         {\r\n" +
                "         }\r\n" +
                "      }" +
                BuildSsf2ApiDebugHelpers();

            var next = text.Substring(0, printMatch.Index) + printReplacement + afterPrint;
            if (!next.Contains(DebugConsoleMarkerV2, StringComparison.Ordinal)
                || !next.Contains("ssf2mmOnUncaught", StringComparison.Ordinal)
                || !next.Contains("loadPolicyFile", StringComparison.Ordinal)
                || !next.Contains(DebugHostSpawnMarker, StringComparison.Ordinal))
                return false;
            File.WriteAllText(apiPath, next);
            return true;
        }

        private static string BuildSsf2ApiDebugHelpers() =>
            "\r\n      \r\n" +
            "      private static function ssf2mmEnsureDebugHost() : void\r\n" +
            "      {\r\n" +
            "         if(_ssf2mmHostStarted)\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         }\r\n" +
            "         _ssf2mmHostStarted = true;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var FileCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.File\") as Class;\r\n" +
            "            var InfoCls:Class = flash.utils.getDefinitionByName(\"flash.desktop.NativeProcessStartupInfo\") as Class;\r\n" +
            "            var NpCls:Class = flash.utils.getDefinitionByName(\"flash.desktop.NativeProcess\") as Class;\r\n" +
            "            var VecCls:Class = flash.utils.getDefinitionByName(\"__AS3__.vec.Vector.<String>\") as Class;\r\n" +
            "            var appDir:* = FileCls[\"applicationDirectory\"];\r\n" +
            "            var exe:* = appDir.resolvePath(\"" + DebugHostExeName + "\");\r\n" +
            "            if(!exe.exists)\r\n" +
            "            {\r\n" +
            "               trace(\"[SSF2MM] debug host missing: \" + exe.nativePath);\r\n" +
            "               return;\r\n" +
            "            }\r\n" +
            "            var info:* = new InfoCls();\r\n" +
            "            info.executable = exe;\r\n" +
            "            var args:* = new VecCls();\r\n" +
            "            args.push(\"--install\");\r\n" +
            "            args.push(appDir.nativePath);\r\n" +
            "            info.arguments = args;\r\n" +
            "            info.workingDirectory = appDir;\r\n" +
            "            _ssf2mmHostProc = new NpCls();\r\n" +
            "            _ssf2mmHostProc.start(info);\r\n" +
            "            trace(\"[SSF2MM] debug host started\");\r\n" +
            "         }\r\n" +
            "         catch(eHost:*)\r\n" +
            "         {\r\n" +
            "            trace(\"[SSF2MM] debug host spawn failed: \" + eHost);\r\n" +
            "         }\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmEnsureUncaughtHook() : void\r\n" +
            "      {\r\n" +
            "         if(_ssf2mmHooked)\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         }\r\n" +
            "         _ssf2mmHooked = true;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var li:* = flash.display.LoaderInfo.getLoaderInfoByDefinition(SSF2API);\r\n" +
            "            if(li && li.uncaughtErrorEvents)\r\n" +
            "            {\r\n" +
            "               li.uncaughtErrorEvents.addEventListener(" +
            "flash.events.UncaughtErrorEvent.UNCAUGHT_ERROR, ssf2mmOnUncaught);\r\n" +
            "            }\r\n" +
            "         }\r\n" +
            "         catch(eH:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmOnUncaught(e:*) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var msg:String = \"UNCAUGHT: \";\r\n" +
            "            if(e && e.error)\r\n" +
            "            {\r\n" +
            "               msg = msg + String(e.error);\r\n" +
            "               try\r\n" +
            "               {\r\n" +
            "                  if(e.error.getStackTrace)\r\n" +
            "                  {\r\n" +
            "                     msg = msg + \"\\n\" + String(e.error.getStackTrace());\r\n" +
            "                  }\r\n" +
            "               }\r\n" +
            "               catch(eSt:*)\r\n" +
            "               {\r\n" +
            "               }\r\n" +
            "            }\r\n" +
            "            else\r\n" +
            "            {\r\n" +
            "               msg = msg + String(e);\r\n" +
            "            }\r\n" +
            "            ssf2mmDebugSend(msg);\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               if(e && e.preventDefault)\r\n" +
            "               {\r\n" +
            "                  e.preventDefault();\r\n" +
            "               }\r\n" +
            "            }\r\n" +
            "            catch(ePd:*)\r\n" +
            "            {\r\n" +
            "            }\r\n" +
            "         }\r\n" +
            "         catch(eU:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmDebugSend(msg:String) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            ssf2mmEnsureDebugHost();\r\n" +
            "            ssf2mmEnsureUncaughtHook();\r\n" +
            "            if(!_ssf2mmSock)\r\n" +
            "            {\r\n" +
            "               try\r\n" +
            "               {\r\n" +
            "                  flash.system.Security.loadPolicyFile(\"xmlsocket://127.0.0.1:" +
                                   DebugConsolePort + "\");\r\n" +
            "               }\r\n" +
            "               catch(ePol:*)\r\n" +
            "               {\r\n" +
            "               }\r\n" +
            "               _ssf2mmSock = new flash.net.Socket();\r\n" +
            "               _ssf2mmBuf = [];\r\n" +
            "               _ssf2mmSock.addEventListener(flash.events.Event.CONNECT, ssf2mmOnConnect);\r\n" +
            "               _ssf2mmSock.addEventListener(flash.events.IOErrorEvent.IO_ERROR, ssf2mmOnSockErr);\r\n" +
            "               _ssf2mmSock.addEventListener(flash.events.SecurityErrorEvent.SECURITY_ERROR, ssf2mmOnSockErr);\r\n" +
            "               _ssf2mmSock.addEventListener(flash.events.Event.CLOSE, ssf2mmOnSockErr);\r\n" +
            "            }\r\n" +
            "            if(_ssf2mmSock.connected)\r\n" +
            "            {\r\n" +
            "               _ssf2mmSock.writeUTFBytes(msg + \"\\n\");\r\n" +
            "               _ssf2mmSock.flush();\r\n" +
            "            }\r\n" +
            "            else\r\n" +
            "            {\r\n" +
            "               if(_ssf2mmBuf == null)\r\n" +
            "               {\r\n" +
            "                  _ssf2mmBuf = [];\r\n" +
            "               }\r\n" +
            "               _ssf2mmBuf.push(msg);\r\n" +
            "               if(!_ssf2mmConnecting)\r\n" +
            "               {\r\n" +
            "                  _ssf2mmConnecting = true;\r\n" +
            "                  _ssf2mmSock.connect(\"127.0.0.1\"," + DebugConsolePort + ");\r\n" +
            "               }\r\n" +
            "            }\r\n" +
            "         }\r\n" +
            "         catch(e1:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmOnConnect(e:*) : void\r\n" +
            "      {\r\n" +
            "         _ssf2mmConnecting = false;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            _ssf2mmSock.writeUTFBytes(\"SSF2MM_CONNECTED\\n\");\r\n" +
            "            if(_ssf2mmBuf)\r\n" +
            "            {\r\n" +
            "               var i:int = 0;\r\n" +
            "               while(i < _ssf2mmBuf.length)\r\n" +
            "               {\r\n" +
            "                  _ssf2mmSock.writeUTFBytes(String(_ssf2mmBuf[i]) + \"\\n\");\r\n" +
            "                  i++;\r\n" +
            "               }\r\n" +
            "               _ssf2mmBuf = [];\r\n" +
            "            }\r\n" +
            "            _ssf2mmSock.flush();\r\n" +
            "         }\r\n" +
            "         catch(e2:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmOnSockErr(e:*) : void\r\n" +
            "      {\r\n" +
            "         _ssf2mmConnecting = false;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(_ssf2mmSock)\r\n" +
            "            {\r\n" +
            "               try\r\n" +
            "               {\r\n" +
            "                  _ssf2mmSock.close();\r\n" +
            "               }\r\n" +
            "               catch(eCl:*)\r\n" +
            "               {\r\n" +
            "               }\r\n" +
            "            }\r\n" +
            "         }\r\n" +
            "         catch(e3:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "         _ssf2mmSock = null;\r\n" +
            "      }\r\n";

        private static string BuildSsf2ApiUncaughtHelpersOnly() =>
            "\r\n      private static function ssf2mmEnsureUncaughtHook() : void\r\n" +
            "      {\r\n" +
            "         if(_ssf2mmHooked)\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         }\r\n" +
            "         _ssf2mmHooked = true;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var li:* = flash.display.LoaderInfo.getLoaderInfoByDefinition(SSF2API);\r\n" +
            "            if(li && li.uncaughtErrorEvents)\r\n" +
            "            {\r\n" +
            "               li.uncaughtErrorEvents.addEventListener(" +
            "flash.events.UncaughtErrorEvent.UNCAUGHT_ERROR, ssf2mmOnUncaught);\r\n" +
            "            }\r\n" +
            "         }\r\n" +
            "         catch(eH:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmOnUncaught(e:*) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var msg:String = \"UNCAUGHT: \";\r\n" +
            "            if(e && e.error)\r\n" +
            "            {\r\n" +
            "               msg = msg + String(e.error);\r\n" +
            "               try\r\n" +
            "               {\r\n" +
            "                  if(e.error.getStackTrace)\r\n" +
            "                  {\r\n" +
            "                     msg = msg + \"\\n\" + String(e.error.getStackTrace());\r\n" +
            "                  }\r\n" +
            "               }\r\n" +
            "               catch(eSt:*)\r\n" +
            "               {\r\n" +
            "               }\r\n" +
            "            }\r\n" +
            "            else\r\n" +
            "            {\r\n" +
            "               msg = msg + String(e);\r\n" +
            "            }\r\n" +
            "            ssf2mmDebugSend(msg);\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               if(e && e.preventDefault)\r\n" +
            "               {\r\n" +
            "                  e.preventDefault();\r\n" +
            "               }\r\n" +
            "            }\r\n" +
            "            catch(ePd:*)\r\n" +
            "            {\r\n" +
            "            }\r\n" +
            "         }\r\n" +
            "         catch(eU:*)\r\n" +
            "         {\r\n" +
            "         }\r\n" +
            "      }\r\n";

        /// <summary>Index of the closing <c>}</c> matching the <c>{</c> at <paramref name="openBraceIndex"/>.</summary>
        public static int FindMatchingBrace(string text, int openBraceIndex)
        {
            if (openBraceIndex < 0 || openBraceIndex >= text.Length || text[openBraceIndex] != '{')
                return -1;
            var depth = 0;
            for (var i = openBraceIndex; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>Registers a global uncaught-error handler that forwards to the debug socket.</summary>
        public static bool PatchMainUncaughtDebugConsole(string scriptsRoot)
        {
            var main = FindScriptFile(scriptsRoot, "Main.as", preferParentName: "ssf2")
                       ?? FindScriptFile(scriptsRoot, "Main.as");
            if (main == null) return false;
            var text = File.ReadAllText(main);
            if (text.Contains("ssf2mmOnUncaught", StringComparison.Ordinal))
                return false;

            const string inject =
                "\r\n         /* " + DebugConsoleMarker + " */\r\n" +
                "         try\r\n" +
                "         {\r\n" +
                "            if(this.loaderInfo && this.loaderInfo.uncaughtErrorEvents)\r\n" +
                "            {\r\n" +
                "               this.loaderInfo.uncaughtErrorEvents.addEventListener(" +
                "flash.events.UncaughtErrorEvent.UNCAUGHT_ERROR, this.ssf2mmOnUncaught);\r\n" +
                "            }\r\n" +
                "         }\r\n" +
                "         catch(eDbg:*)\r\n" +
                "         {\r\n" +
                "         }\r\n";

            // Prefer after super() in Main constructor.
            var next = Regex.Replace(
                text,
                @"(function\s+Main\s*\([^)]*\)\s*\{[\s\S]*?super\s*\(\s*\)\s*;)",
                "$1" + inject,
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(5));
            if (string.Equals(next, text, StringComparison.Ordinal))
            {
                next = Regex.Replace(
                    text,
                    @"(public\s+function\s+Main\s*\([^)]*\)\s*\{)",
                    "$1" + inject,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(5));
            }
            if (string.Equals(next, text, StringComparison.Ordinal))
                return false;

            if (!next.Contains("ssf2mmOnUncaught", StringComparison.Ordinal))
            {
                const string handler =
                    "\r\n      private function ssf2mmOnUncaught(e:*) : void\r\n" +
                    "      {\r\n" +
                    "         try\r\n" +
                    "         {\r\n" +
                    "            var msg:String = \"UNCAUGHT: \";\r\n" +
                    "            if(e && e.error)\r\n" +
                    "            {\r\n" +
                    "               msg = msg + String(e.error);\r\n" +
                    "               if(e.error.getStackTrace)\r\n" +
                    "               {\r\n" +
                    "                  msg = msg + \"\\n\" + String(e.error.getStackTrace());\r\n" +
                    "               }\r\n" +
                    "            }\r\n" +
                    "            else\r\n" +
                    "            {\r\n" +
                    "               msg = msg + String(e);\r\n" +
                    "            }\r\n" +
                    "            SSF2API.print(msg);\r\n" +
                    "         }\r\n" +
                    "         catch(e2:*)\r\n" +
                    "         {\r\n" +
                    "         }\r\n" +
                    "      }\r\n";
                var brace = next.LastIndexOf('}');
                if (brace > 0)
                    next = next.Insert(brace, handler);
            }

            File.WriteAllText(main, next);
            return true;
        }
    }
}
