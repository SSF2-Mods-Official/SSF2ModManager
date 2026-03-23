using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SSF2ModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    public class ModManagerService
    {
        private readonly GameBananaApiClient _apiClient;
        private ModDatabase _database;
        private readonly string _appDataDir;
        private readonly string _databasePath;
        private readonly string _modsBaseDir;
        private readonly string _backupsBaseDir;
        private readonly string _downloadCacheDir;

        public static readonly string[] KnownVersions =
        {
            "SSF2 Beta 1.4.0",
            "SSF2 Beta 1.3.1.2",
            "SSF2 Beta 1.3.1.1",
            "SSF2 Beta 1.3.0.1",
            "SSF2 Beta 1.2.5.1",
            "SSF2 Beta 1.2.4.2",
            "SSF2 Beta 1.2.3.2",
            "SSF2 Beta 1.2.2.1",
            "SSF2 Beta 1.2.1.1",
            "SSF2 Beta v1.2.0.2",
            "SSF2 Beta v1.1.0.1",
            "SSF2 Beta v1.0.3.2",
            "v0.9b (0.9.1.1982)",
            "v0.9a (0.9.0.2011)",
            "v0.8b",
            "v0.8a",
            "v0.7",
            "v0.6",
            "v0.5b",
            "v0.5a",
            "v0.4b",
            "v0.4a",
            "v0.3c",
            "v0.3b",
            "v0.3a",
            "v0.2b",
            "v0.2a",
            "v0.1b",
            "v0.1a",
            "Custom...",
        };

        public List<InstalledMod> InstalledMods => _database.Mods;
        public List<SSF2VersionEntry> Versions => _database.Versions;
        public string ModsBaseDir => _modsBaseDir;

        public string ActiveVersion
        {
            get => _database.ActiveVersion;
            set { _database.ActiveVersion = value; SaveDatabase(); }
        }

        public ModManagerService(GameBananaApiClient apiClient)
        {
            _apiClient = apiClient;
            _database = new ModDatabase();

            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SSF2ModManager");
            Directory.CreateDirectory(_appDataDir);

            _databasePath = Path.Combine(_appDataDir, "mods.json");
            _modsBaseDir = Path.Combine(_appDataDir, "mods");
            _backupsBaseDir = Path.Combine(_appDataDir, "backups");
            _downloadCacheDir = Path.Combine(_appDataDir, "cache");

            Directory.CreateDirectory(_modsBaseDir);
            Directory.CreateDirectory(_backupsBaseDir);
            Directory.CreateDirectory(_downloadCacheDir);

            LoadOrCreateDatabase();
            DebugLogger.Log("ModManagerService initialized");
        }

        private void LoadOrCreateDatabase()
        {
            if (File.Exists(_databasePath))
            {
                try
                {
                    var json = File.ReadAllText(_databasePath);
                    _database = JsonConvert.DeserializeObject<ModDatabase>(json) ?? new ModDatabase();
                    DebugLogger.Log($"Database loaded: {_database.Mods.Count} mods, {_database.Versions.Count} versions");
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("Failed to load database", ex);
                    _database = new ModDatabase();
                }
            }
            else
            {
                _database = new ModDatabase();
            }

            _database.Versions ??= new List<SSF2VersionEntry>();
            _database.Mods ??= new List<InstalledMod>();
        }

        public void SaveDatabase()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_database, Formatting.Indented);
                File.WriteAllText(_databasePath, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Failed to save database", ex);
            }
        }

        // ── Version Management ──────────────────────────────────

        public void AddVersion(string versionName, string folderPath, string nickname = "")
        {
            // Allow multiple entries of same version (different paths)
            _database.Versions.Add(new SSF2VersionEntry
            {
                VersionName = versionName,
                FolderPath = folderPath,
                Nickname = nickname
            });
            DebugLogger.Log($"Added version: {versionName} ({nickname}) at {folderPath}");

            if (string.IsNullOrEmpty(_database.ActiveVersion))
                _database.ActiveVersion = nickname;

            SaveDatabase();
        }

        public void RenameVersion(SSF2VersionEntry entry, string newNickname)
        {
            var oldNickname = entry.Nickname;
            entry.Nickname = newNickname;
            if (_database.ActiveVersion == oldNickname)
                _database.ActiveVersion = newNickname;
            SaveDatabase();
            DebugLogger.Log($"Renamed version {entry.VersionName} to nickname: {newNickname}");
        }

        public void ChangeVersionPath(SSF2VersionEntry entry, string newPath)
        {
            var oldPath = entry.FolderPath;
            entry.FolderPath = newPath;
            SaveDatabase();
            DebugLogger.Log($"Changed path for {entry.DisplayName}: {oldPath} -> {newPath}");
        }

        public void RemoveVersion(SSF2VersionEntry entry)
        {
            _database.Versions.Remove(entry);
            if (_database.ActiveVersion == entry.Nickname)
                _database.ActiveVersion = _database.Versions.FirstOrDefault()?.Nickname ?? "";
            DebugLogger.Log($"Removed version: {entry.DisplayName}");
            SaveDatabase();
        }

        public SSF2VersionEntry? GetVersionEntry(string identifier)
        {
            return _database.Versions.FirstOrDefault(v => v.Nickname == identifier)
                ?? _database.Versions.FirstOrDefault(v => v.VersionName == identifier);
        }

        public string? GetVersionPath(string identifier)
        {
            var entry = _database.Versions.FirstOrDefault(v => v.Nickname == identifier)
                ?? _database.Versions.FirstOrDefault(v => v.VersionName == identifier);
            return entry?.FolderPath;
        }

        public string? GetActiveVersionPath()
        {
            return GetVersionPath(_database.ActiveVersion);
        }

        // ── Mod Installation ────────────────────────────────────

        /// <summary>
        /// Preview the contents of an archive without extracting.
        /// Returns list of (entryPath, size) tuples.
        /// </summary>
        public List<(string Path, long Size)> PreviewArchiveContents(byte[] fileBytes, string fileName)
        {
            var results = new List<(string, long)>();
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension != ".zip" && extension != ".7z" && extension != ".rar")
            {
                results.Add((fileName, fileBytes.Length));
                return results;
            }

            using var stream = new MemoryStream(fileBytes);
            using var archive = ArchiveFactory.Open(stream);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                var key = entry.Key?.Replace('/', Path.DirectorySeparatorChar) ?? "";
                if (!string.IsNullOrEmpty(key))
                    results.Add((key, entry.Size));
            }
            return results;
        }

        public async Task<InstalledMod> InstallModAsync(
            GameBananaMod mod, GameBananaFile file, string targetVersion,
            IProgress<double>? progress = null, HashSet<string>? selectedFiles = null,
            byte[]? preDownloadedBytes = null)
        {
            DebugLogger.Log($"Installing: {mod.Name} (ID: {mod.Id}) for {targetVersion}");

            // Determine category
            var rawCategory = mod.RootCategory?.Name ?? mod.Category?.Name ?? "Other";
            var categoryFolder = SanitizeFileName(rawCategory);
            var isGameFiles = rawCategory.Equals("Game files", StringComparison.OrdinalIgnoreCase);

            DebugLogger.Log($"Category: {rawCategory}, IsGameFiles: {isGameFiles}");

            // Download (or use pre-downloaded bytes)
            byte[] fileBytes;
            if (preDownloadedBytes != null)
            {
                fileBytes = preDownloadedBytes;
                DebugLogger.Log($"Using pre-downloaded bytes: {fileBytes.Length} bytes");
            }
            else
            {
                fileBytes = await _apiClient.DownloadFileAsync(file.DownloadUrl, progress);
                DebugLogger.Log($"Downloaded: {file.FileName} ({fileBytes.Length} bytes)");
            }

            // Save to cache
            var cachePath = Path.Combine(_downloadCacheDir, SanitizeFileName(file.FileName));
            await File.WriteAllBytesAsync(cachePath, fileBytes);

            // Create category/mod folder
            var modFolderName = SanitizeFileName($"{mod.Name}_{mod.Id}");
            var categoryDir = Path.Combine(_modsBaseDir, categoryFolder);
            var modPath = Path.Combine(categoryDir, modFolderName);

            // Remove existing if reinstalling
            var existing = _database.Mods.FirstOrDefault(m => m.GameBananaId == mod.Id);
            if (existing != null)
            {
                DebugLogger.Log($"Reinstalling - removing existing: {existing.Name}");
                await UninstallModAsync(existing);
            }

            Directory.CreateDirectory(modPath);
            var installedFiles = new List<string>();

            // Extract based on file type
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".zip" || extension == ".7z" || extension == ".rar")
            {
                try
                {
                    using var stream = new MemoryStream(fileBytes);
                    using var archive = ArchiveFactory.Open(stream);
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        var entryKey = entry.Key?.Replace('/', Path.DirectorySeparatorChar) ?? "";
                        if (string.IsNullOrEmpty(entryKey)) continue;

                        // Skip files not in the selection (if a selection was provided)
                        if (selectedFiles != null && !selectedFiles.Contains(entryKey))
                            continue;

                        var destPath = Path.Combine(modPath, entryKey);
                        var fullDestPath = Path.GetFullPath(destPath);
                        if (!fullDestPath.StartsWith(Path.GetFullPath(modPath), StringComparison.OrdinalIgnoreCase))
                            continue;

                        var destDir = Path.GetDirectoryName(destPath);
                        if (destDir != null) Directory.CreateDirectory(destDir);
                        entry.WriteToFile(destPath, new ExtractionOptions { Overwrite = true });
                        installedFiles.Add(entryKey);
                    }
                    DebugLogger.Log($"Extracted {installedFiles.Count} files from {extension} to {modPath}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Error($"Archive extraction failed for {extension} file", ex);
                    throw new InvalidOperationException(
                        $"Failed to extract {file.FileName} ({extension}).\n" +
                        $"The archive may be corrupted or in an unsupported format.\n\n" +
                        $"Error: {ex.Message}");
                }
            }
            else
            {
                var destFile = Path.Combine(modPath, file.FileName);
                File.Copy(cachePath, destFile, true);
                installedFiles.Add(file.FileName);
                DebugLogger.Log($"Copied file: {file.FileName}");
            }

            // Create mod record early so metadata (info.json) can be applied to it
            var installedMod = new InstalledMod
            {
                Id = Guid.NewGuid().ToString(),
                GameBananaId = mod.Id,
                Name = mod.Name,
                Author = mod.Submitter?.Name ?? "Unknown",
                Description = mod.Description ?? "",
                Category = categoryFolder,
                FolderName = modFolderName,
                ThumbnailUrl = mod.PreviewMedia?.Images?.FirstOrDefault()?.ThumbnailUrl ?? "",
                ProfileUrl = mod.ProfileUrl,
                Enabled = true,
                InstalledDate = DateTime.Now,
                InstalledFiles = installedFiles,
                TargetVersion = targetVersion,
                IsGameFiles = isGameFiles,
                BackedUpFiles = new List<BackedUpFile>()
            };

            // Look for optional info.json in the extracted mod and apply metadata
            try
            {
                string? infoPath = null;
                // Prefer root-level info.json
                var rootCandidate = Path.Combine(modPath, "info.json");
                if (File.Exists(rootCandidate)) infoPath = rootCandidate;
                else
                {
                    var found = Directory.GetFiles(modPath, "info.json", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) infoPath = found;
                }

                if (!string.IsNullOrEmpty(infoPath))
                {
                    DebugLogger.Log($"Found info.json at {infoPath}, parsing metadata");
                    try
                    {
                        var txt = File.ReadAllText(infoPath);
                        dynamic? info = JsonConvert.DeserializeObject(txt);
                        if (info != null)
                        {
                            if (info.creator != null) installedMod.Creator = (string)info.creator;
                            if (info.mod_type != null) installedMod.ModType = (string)info.mod_type;
                            if (info.ssf2_version != null)
                            {
                                var sv = (string)info.ssf2_version;
                                // If targetVersion was default/empty, adopt info.json version; otherwise keep user selection
                                if (string.IsNullOrEmpty(targetVersion) || targetVersion.StartsWith("Auto", StringComparison.OrdinalIgnoreCase))
                                    installedMod.TargetVersion = sv;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Failed to parse info.json", ex);
                    }
                }
            }
            catch { }

            // Clean up cache
            try { File.Delete(cachePath); } catch { }

            // Deploy .ssf files to data folder (unless Game files category)
            if (!isGameFiles)
            {
                DeployModToData(installedMod, modPath);
            }
            else
            {
                DebugLogger.Log("Game files category - skipping data deployment");
            }

            _database.Mods.Add(installedMod);
            SaveDatabase();

            DebugLogger.Log($"Installation complete: {mod.Name}");
            return installedMod;
        }

        // ── Deploy / Restore ────────────────────────────────────

        private void DeployModToData(InstalledMod mod, string modFolderPath)
        {
            var versionPath = GetVersionPath(mod.TargetVersion);
            if (string.IsNullOrEmpty(versionPath))
            {
                throw new InvalidOperationException($"Version path not found for {mod.TargetVersion}");
            }

            var dataDir = Path.Combine(versionPath, "data");
            if (!Directory.Exists(dataDir))
            {
                DebugLogger.Error($"Data directory not found: {dataDir}");
                return;
            }

            // Find all .ssf files in the mod
            string[] ssfFiles;
            try
            {
                ssfFiles = Directory.GetFiles(modFolderPath, "*.ssf", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Failed to scan mod for .ssf files", ex);
                return;
            }

            if (ssfFiles.Length == 0)
            {
                DebugLogger.Log("No .ssf files found in mod - nothing to deploy to data/");
                return;
            }

            DebugLogger.Log($"Found {ssfFiles.Length} .ssf file(s) to deploy");

            var backupDir = Path.Combine(_backupsBaseDir, mod.TargetVersion, mod.GameBananaId.ToString());

            foreach (var ssfFile in ssfFiles)
            {
                var fileName = Path.GetFileName(ssfFile);

                // Search for matching file in data directory
                string[] matchingFiles;
                try
                {
                    matchingFiles = Directory.GetFiles(dataDir, fileName, SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                if (matchingFiles.Length == 0)
                {
                    DebugLogger.Log($"No matching file in data/ for: {fileName}");
                    continue;
                }

                foreach (var existingFile in matchingFiles)
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(dataDir, existingFile);
                        if (relativePath.Contains("..")) continue;

                        // Back up original
                        Directory.CreateDirectory(backupDir);
                        var backupPath = Path.Combine(backupDir, relativePath);
                        var backupFileDir = Path.GetDirectoryName(backupPath);
                        if (backupFileDir != null) Directory.CreateDirectory(backupFileDir);

                        if (!File.Exists(backupPath))
                        {
                            File.Copy(existingFile, backupPath, false);
                            DebugLogger.Log($"Backed up: {relativePath}");
                        }

                        // Copy modded file
                        File.Copy(ssfFile, existingFile, true);
                        DebugLogger.Log($"Deployed: {fileName} -> {relativePath}");

                        // Track backup
                        if (!mod.BackedUpFiles.Any(b => b.OriginalRelativePath == relativePath))
                        {
                            mod.BackedUpFiles.Add(new BackedUpFile
                            {
                                OriginalRelativePath = relativePath,
                                BackupFullPath = backupPath
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error($"Failed to deploy {fileName}", ex);
                    }
                }
            }
        }

        private void RestoreBackups(InstalledMod mod)
        {
            var versionPath = GetVersionPath(mod.TargetVersion);
            if (string.IsNullOrEmpty(versionPath))
            {
                throw new InvalidOperationException($"Cannot restore - version path not found: {mod.TargetVersion}");
            }

            var dataDir = Path.Combine(versionPath, "data");

            foreach (var backup in mod.BackedUpFiles)
            {
                try
                {
                    var targetPath = Path.Combine(dataDir, backup.OriginalRelativePath);
                    // Validate the restore target is within data dir
                    if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(dataDir), StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (File.Exists(backup.BackupFullPath))
                    {
                        var dir = Path.GetDirectoryName(targetPath);
                        if (dir != null) Directory.CreateDirectory(dir);
                        File.Copy(backup.BackupFullPath, targetPath, true);
                        DebugLogger.Log($"Restored: {backup.OriginalRelativePath}");
                    }
                    else
                    {
                        DebugLogger.Error($"Backup file missing: {backup.BackupFullPath}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Error($"Failed to restore {backup.OriginalRelativePath}", ex);
                }
            }
        }

        // ── Toggle / Uninstall ──────────────────────────────────

        public void ToggleMod(InstalledMod mod)
        {
            if (mod.Enabled)
            {
                // Disabling - restore original files
                DebugLogger.Log($"Disabling: {mod.Name}");
                if (!mod.IsGameFiles)
                {
                    RestoreBackups(mod);
                }
                mod.Enabled = false;
            }
            else
            {
                // Enabling - re-deploy .ssf files
                DebugLogger.Log($"Enabling: {mod.Name}");
                if (!mod.IsGameFiles)
                {
                    var modPath = Path.Combine(_modsBaseDir, mod.Category, mod.FolderName);
                    if (Directory.Exists(modPath))
                    {
                        DeployModToData(mod, modPath);
                    }
                    else
                    {
                        DebugLogger.Error($"Mod folder not found: {modPath}");
                    }
                }
                mod.Enabled = true;
            }

            SaveDatabase();
        }

        public Task UninstallModAsync(InstalledMod mod)
        {
            DebugLogger.Log($"Uninstalling: {mod.Name}");

            // Restore backed up files
            if (!mod.IsGameFiles && mod.Enabled)
            {
                RestoreBackups(mod);
            }

            // Delete mod folder
            var modPath = Path.Combine(_modsBaseDir, mod.Category, mod.FolderName);
            if (Directory.Exists(modPath))
            {
                try
                {
                    Directory.Delete(modPath, true);
                    DebugLogger.Log($"Deleted mod folder: {modPath}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("Failed to delete mod folder", ex);
                }
            }

            // Delete backup folder
            if (!string.IsNullOrEmpty(mod.TargetVersion))
            {
                var backupDir = Path.Combine(_backupsBaseDir, mod.TargetVersion, mod.GameBananaId.ToString());
                if (Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.Delete(backupDir, true);
                        DebugLogger.Log($"Deleted backup folder: {backupDir}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Failed to delete backup folder", ex);
                    }
                }
            }

            _database.Mods.Remove(mod);
            SaveDatabase();
            return Task.CompletedTask;
        }

        // ── Helpers ─────────────────────────────────────────────

        public string? FindSSF2Executable(string versionName)
        {
            var versionPath = GetVersionPath(versionName);
            return FindExeInPath(versionPath);
        }

        public string? FindSSF2Executable(SSF2VersionEntry entry)
        {
            return FindExeInPath(entry.FolderPath);
        }

        private string? FindExeInPath(string? versionPath)
        {
            if (string.IsNullOrEmpty(versionPath) || !Directory.Exists(versionPath))
                return null;

            string[] exeNames = { "SSF2.exe", "Super Smash Flash 2.exe", "ssf2.exe" };

            // Search known names recursively
            foreach (var name in exeNames)
            {
                try
                {
                    var found = Directory.GetFiles(versionPath, name, SearchOption.AllDirectories);
                    if (found.Length > 0)
                        return found[0];
                }
                catch { }
            }

            // Fallback: any .exe in the entire tree
            try
            {
                var exeFiles = Directory.GetFiles(versionPath, "*.exe", SearchOption.AllDirectories);
                if (exeFiles.Length > 0)
                    return exeFiles[0];
            }
            catch { }

            return null;
        }

        public void DisableAllModsForVersion(string versionName)
        {
            var mods = _database.Mods.Where(m => m.TargetVersion == versionName && m.Enabled).ToList();
            foreach (var mod in mods)
            {
                DebugLogger.Log($"Disabling: {mod.Name}");
                if (!mod.IsGameFiles)
                {
                    RestoreBackups(mod);
                }
                mod.Enabled = false;
            }
            SaveDatabase();
            DebugLogger.Log($"Disabled {mods.Count} mods for version {versionName}");
        }

        public void DeployGameFilesMod(InstalledMod mod)
        {
            var modPath = Path.Combine(_modsBaseDir, mod.Category, mod.FolderName);
            if (!Directory.Exists(modPath))
            {
                DebugLogger.Error($"Mod folder not found: {modPath}");
                return;
            }

            var versionPath = GetVersionPath(mod.TargetVersion);
            if (string.IsNullOrEmpty(versionPath) || !Directory.Exists(versionPath))
            {
                throw new InvalidOperationException($"Version path not found for {mod.TargetVersion}");
            }

            var backupDir = Path.Combine(_backupsBaseDir, mod.TargetVersion, mod.GameBananaId.ToString());

            // Check for data/ folder inside the mod
            var modDataDir = FindDataFolder(modPath);
            if (modDataDir != null)
            {
                var targetDataDir = Path.Combine(versionPath, "data");
                if (Directory.Exists(targetDataDir))
                {
                    CopyDirectoryWithBackup(modDataDir, targetDataDir, backupDir, mod, @"data\");
                }
            }

            // Check for ssf2.swf
            var swfFiles = Directory.GetFiles(modPath, "ssf2.swf", SearchOption.AllDirectories);
            if (swfFiles.Length > 0)
            {
                var targetSwf = Path.Combine(versionPath, "ssf2.swf");
                if (File.Exists(targetSwf))
                {
                    Directory.CreateDirectory(backupDir);
                    var backupSwf = Path.Combine(backupDir, "ssf2.swf");
                    if (!File.Exists(backupSwf))
                    {
                        File.Copy(targetSwf, backupSwf, false);
                        DebugLogger.Log("Backed up: ssf2.swf");
                    }
                    mod.BackedUpFiles.Add(new BackedUpFile
                    {
                        OriginalRelativePath = "__root__/ssf2.swf",
                        BackupFullPath = backupSwf
                    });
                }
                File.Copy(swfFiles[0], targetSwf, true);
                DebugLogger.Log("Deployed: ssf2.swf to version root");
            }

            // Also deploy any .ssf files like normal mods
            DeployModToData(mod, modPath);

            mod.IsGameFiles = false;
            SaveDatabase();
        }

        private string? FindDataFolder(string modPath)
        {
            var direct = Path.Combine(modPath, "data");
            if (Directory.Exists(direct)) return direct;

            foreach (var subDir in Directory.GetDirectories(modPath))
            {
                var nested = Path.Combine(subDir, "data");
                if (Directory.Exists(nested)) return nested;
            }
            return null;
        }

        private void CopyDirectoryWithBackup(string sourceDir, string targetDir, string backupDir,
            InstalledMod mod, string relativePrefix)
        {
            foreach (var sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                if (relativePath.Contains("..")) continue;

                var targetFile = Path.Combine(targetDir, relativePath);
                var fullTarget = Path.GetFullPath(targetFile);
                if (!fullTarget.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.Exists(targetFile))
                {
                    Directory.CreateDirectory(backupDir);
                    var backupPath = Path.Combine(backupDir, relativePrefix + relativePath);
                    var backupFileDir = Path.GetDirectoryName(backupPath);
                    if (backupFileDir != null) Directory.CreateDirectory(backupFileDir);
                    if (!File.Exists(backupPath))
                    {
                        File.Copy(targetFile, backupPath, false);
                        DebugLogger.Log($"Backed up: {relativePath}");
                    }
                    mod.BackedUpFiles.Add(new BackedUpFile
                    {
                        OriginalRelativePath = relativePrefix + relativePath,
                        BackupFullPath = backupPath
                    });
                }

                var destDir = Path.GetDirectoryName(targetFile);
                if (destDir != null) Directory.CreateDirectory(destDir);
                File.Copy(sourceFile, targetFile, true);
                DebugLogger.Log($"Deployed: {relativePath}");
            }
        }

        public List<string> GetModFileStructure(InstalledMod mod)
        {
            var modPath = Path.Combine(_modsBaseDir, mod.Category, mod.FolderName);
            if (!Directory.Exists(modPath)) return new List<string>();

            try
            {
                return Directory.GetFiles(modPath, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(modPath, f))
                    .OrderBy(f => f)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public List<InstalledMod> GetModsForVersion(string versionName)
        {
            return _database.Mods.Where(m => m.TargetVersion == versionName).ToList();
        }

        /// <summary>
        /// Get file-level conflicts: which enabled mods have backed up the same relative paths?
        /// Returns a dictionary of conflicting relative paths → list of mods that own those files.
        /// </summary>
        public Dictionary<string, List<InstalledMod>> GetFileConflicts(string targetVersion, IEnumerable<string> ssfFileNames, int? excludeGameBananaId = null)
        {
            var conflicts = new Dictionary<string, List<InstalledMod>>(StringComparer.OrdinalIgnoreCase);
            var enabledMods = _database.Mods
                .Where(m => m.TargetVersion == targetVersion && m.Enabled && m.GameBananaId != (excludeGameBananaId ?? -1))
                .ToList();

            foreach (var mod in enabledMods)
            {
                foreach (var backup in mod.BackedUpFiles)
                {
                    var backupFileName = Path.GetFileName(backup.OriginalRelativePath);
                    if (ssfFileNames.Any(f => f.Equals(backupFileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!conflicts.ContainsKey(backupFileName))
                            conflicts[backupFileName] = new List<InstalledMod>();
                        if (!conflicts[backupFileName].Contains(mod))
                            conflicts[backupFileName].Add(mod);
                    }
                }
            }

            return conflicts;
        }

        /// <summary>
        /// Partially disable a mod by restoring only specific files (those that conflict).
        /// Removes backup entries for the partially restored files.
        /// </summary>
        public void PartialDisableMod(InstalledMod mod, IEnumerable<string> fileNames, bool restoreBackups = false)
        {
            var versionPath = GetVersionPath(mod.TargetVersion);
            if (string.IsNullOrEmpty(versionPath))
                throw new InvalidOperationException($"Version path not found for {mod.TargetVersion}");
            var dataDir = Path.Combine(versionPath, "data");

            var fileSet = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
            var matchingBackups = mod.BackedUpFiles
                .Where(b => fileSet.Contains(Path.GetFileName(b.OriginalRelativePath)))
                .ToList();

            if (restoreBackups)
            {
                foreach (var backup in matchingBackups)
                {
                    try
                    {
                        var targetPath = Path.Combine(dataDir, backup.OriginalRelativePath);
                        if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(dataDir), StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (File.Exists(backup.BackupFullPath))
                        {
                            File.Copy(backup.BackupFullPath, targetPath, true);
                            DebugLogger.Log($"Partially restored: {backup.OriginalRelativePath} for {mod.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error($"Failed to partially restore {backup.OriginalRelativePath}", ex);
                    }
                }
            }

            // Remove backup entries for these files (regardless of whether we restored them)
            mod.BackedUpFiles.RemoveAll(b => fileSet.Contains(Path.GetFileName(b.OriginalRelativePath)));

            // If no backed up files remain, fully disable
            if (mod.BackedUpFiles.Count == 0)
            {
                mod.Enabled = false;
                DebugLogger.Log($"Mod fully disabled (no files remain): {mod.Name}");
            }
            SaveDatabase();
        }

        /// <summary>
        /// Install a local mod (not from GameBanana).
        /// </summary>
        public async Task<InstalledMod> InstallLocalModAsync(
            string modName, string category, string fileName, byte[] fileBytes, string targetVersion)
        {
            DebugLogger.Log($"Installing local mod: {modName} for {targetVersion}");

            var categoryFolder = SanitizeFileName(category);
            var isGameFiles = category.Equals("Game files", StringComparison.OrdinalIgnoreCase);

            var modFolderName = SanitizeFileName($"{modName}_local_{DateTime.Now:yyyyMMddHHmmss}");
            var categoryDir = Path.Combine(_modsBaseDir, categoryFolder);
            var modPath = Path.Combine(categoryDir, modFolderName);
            Directory.CreateDirectory(modPath);

            var installedFiles = new List<string>();
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (extension == ".zip" || extension == ".7z" || extension == ".rar")
            {
                using var stream = new MemoryStream(fileBytes);
                using var archive = ArchiveFactory.Open(stream);
                foreach (var entry in archive.Entries.Where(en => !en.IsDirectory))
                {
                    var entryKey = entry.Key?.Replace('/', Path.DirectorySeparatorChar) ?? "";
                    if (string.IsNullOrEmpty(entryKey)) continue;

                    var destPath = Path.Combine(modPath, entryKey);
                    var fullDestPath = Path.GetFullPath(destPath);
                    if (!fullDestPath.StartsWith(Path.GetFullPath(modPath), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);
                    entry.WriteToFile(destPath, new ExtractionOptions { Overwrite = true });
                    installedFiles.Add(entryKey);
                }
                DebugLogger.Log($"Extracted {installedFiles.Count} files from local {extension}");
            }
            else
            {
                var destFile = Path.Combine(modPath, fileName);
                await File.WriteAllBytesAsync(destFile, fileBytes);
                installedFiles.Add(fileName);
            }

            var installedMod = new InstalledMod
            {
                Id = Guid.NewGuid().ToString(),
                GameBananaId = 0,
                Name = modName,
                Author = "Local",
                Description = $"Locally installed from {fileName}",
                Category = categoryFolder,
                FolderName = modFolderName,
                Enabled = true,
                InstalledDate = DateTime.Now,
                InstalledFiles = installedFiles,
                TargetVersion = targetVersion,
                IsGameFiles = isGameFiles,
                BackedUpFiles = new List<BackedUpFile>()
            };

            if (!isGameFiles)
            {
                DeployModToData(installedMod, modPath);
            }

            _database.Mods.Add(installedMod);
            SaveDatabase();
            DebugLogger.Log($"Local mod installation complete: {modName}");
            return installedMod;
        }

        /// <summary>
        /// Re-install a mod from its local folder (re-deploy files).
        /// </summary>
        public void ReinstallFromFolder(InstalledMod mod)
        {
            var modPath = Path.Combine(_modsBaseDir, mod.Category, mod.FolderName);
            if (!Directory.Exists(modPath))
                throw new DirectoryNotFoundException($"Mod folder not found: {modPath}");

            DebugLogger.Log($"Re-installing from folder: {mod.Name}");

            // Restore backups first
            if (mod.Enabled && !mod.IsGameFiles)
                RestoreBackups(mod);

            // Clear backed up files and re-deploy
            mod.BackedUpFiles.Clear();
            if (!mod.IsGameFiles)
            {
                DeployModToData(mod, modPath);
            }
            mod.Enabled = true;
            SaveDatabase();
            DebugLogger.Log($"Re-install from folder complete: {mod.Name}");
        }

        /// <summary>
        /// Get ssf file names from a mod's folder (for conflict checking before install).
        /// </summary>
        public List<string> GetSsfFileNames(string modPath)
        {
            if (!Directory.Exists(modPath)) return new List<string>();
            try
            {
                return Directory.GetFiles(modPath, "*.ssf", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Select(f => f!)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
