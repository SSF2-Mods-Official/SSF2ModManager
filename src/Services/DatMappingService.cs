using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public class DatMappingEntry
    {
        public string FriendlyRelativePath { get; set; } = "";
        public string DatFileName { get; set; } = "";
        public string? Guid { get; set; }
        public string? Category { get; set; }
    }

    public class DatRenamePreview
    {
        public string DatFullPath { get; set; } = "";
        public string TargetFullPath { get; set; } = "";
        public bool DatExists { get; set; }
        public bool TargetExists { get; set; }
    }

    /// <summary>
    /// Reveals DAT###.ssf ↔ friendly path mappings from mappings.json or mappings.ssf (via FFDec).
    /// </summary>
    public class DatMappingService
    {
        private const string MappingsMetadataKey = "b7d60849-fa99-4514-b41e-16693ce7acc0";
        private readonly JpexsCliService _jpexs;

        public DatMappingService(JpexsCliService? jpexs = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
        }

        public async Task<List<DatMappingEntry>> LoadMappingsAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 install folder not found.");

            // Prefer plaintext mappings.json if present (IDK / tools tree).
            var jsonCandidates = new[]
            {
                Path.Combine(installRoot, "misc", "mappings.json"),
                Path.Combine(installRoot, "data", "misc", "mappings.json"),
                Path.Combine(installRoot, "src", "misc", "mappings.json")
            };
            foreach (var json in jsonCandidates)
            {
                if (!File.Exists(json)) continue;
                progress?.Report($"Loading mappings from {json}…");
                return ParseMappingsJson(await File.ReadAllTextAsync(json, cancellationToken));
            }

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-datmap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var mainSwf = Directory.GetFiles(installRoot, "*.swf", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => Path.GetFileName(f).Equals("ssf2.swf", StringComparison.OrdinalIgnoreCase));
                if (mainSwf == null)
                    throw new FileNotFoundException("Could not find SSF2.swf in the selected install root.");

                progress?.Report("Reading resource manifest from SSF2.swf…");
                var mainExport = Path.Combine(work, "main");
                Directory.CreateDirectory(mainExport);
                await _jpexs.ExportAsync(
                    jpexsPath ?? "", mainSwf, mainExport, "binaryData,script", cancellationToken);

                var resourceManagerScript = Directory.GetFiles(
                        mainExport, "ResourceManager.as", SearchOption.AllDirectories)
                    .FirstOrDefault();
                var manifestBinary = Directory.GetFiles(mainExport, "*.bin", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(
                        "ResourceManager_manifestJSON", StringComparison.OrdinalIgnoreCase));
                if (resourceManagerScript == null || manifestBinary == null)
                    throw new InvalidOperationException(
                        "SSF2.swf did not expose ResourceManager manifest data through FFDec.");

                var resourceManagerText = await File.ReadAllTextAsync(resourceManagerScript, cancellationToken);
                var manifestKey = ExtractManifestKey(resourceManagerText);
                var manifestBlob = ReadEmbeddedText(manifestBinary);
                var manifestJson = DecodeJsonBlob(manifestBlob, manifestKey);
                var manifestObject = JObject.Parse(manifestJson);
                var manifestEntries = ParseMappingsJson(manifestJson);

                var mappingsDat = manifestObject["misc"]?["mappings"]?["file_pub"]?.ToString();
                if (string.IsNullOrWhiteSpace(mappingsDat))
                    throw new InvalidOperationException(
                        "The embedded SSF2 manifest does not contain misc.mappings.file_pub.");

                progress?.Report($"Manifest maps mappings.ssf to {mappingsDat}.");
                var dataDir = Path.Combine(installRoot, "data");
                var mappingsPath = Path.Combine(dataDir, Path.GetFileName(mappingsDat));
                if (!File.Exists(mappingsPath))
                    throw new FileNotFoundException(
                        $"The mappings resource {Path.GetFileName(mappingsDat)} was not found in data/.",
                        mappingsPath);

                progress?.Report($"Unwrapping {Path.GetFileName(mappingsPath)}…");
                var wrapped = await File.ReadAllBytesAsync(mappingsPath, cancellationToken);
                var mappingsSwf = new PressorService().Decompress(wrapped, out _);
                var mappingsSwfPath = Path.Combine(work, "mappings.swf");
                await File.WriteAllBytesAsync(mappingsSwfPath, mappingsSwf, cancellationToken);

                progress?.Report("Extracting encrypted mappings metadata…");
                var mappingsExport = Path.Combine(work, "mappings");
                Directory.CreateDirectory(mappingsExport);
                await _jpexs.ExportAsync(
                    jpexsPath ?? "", mappingsSwfPath, mappingsExport, "binaryData", cancellationToken);
                var metadataBinary = Directory.GetFiles(mappingsExport, "*.bin", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(
                        "mappingsJSON", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.GetFiles(mappingsExport, "*.bin", SearchOption.AllDirectories)
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();
                if (metadataBinary == null)
                    throw new InvalidOperationException(
                        "The mappings DAT did not contain an exported binary metadata asset.");

                var metadataBlob = ReadEmbeddedText(metadataBinary);
                var mappingsJson = DecodeJsonBlob(metadataBlob, MappingsMetadataKey);
                var fullEntries = ParseMappingsJson(mappingsJson);
                var entries = manifestEntries.Concat(fullEntries)
                    .GroupBy(e => e.DatFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                if (entries.Count == 0)
                    throw new InvalidOperationException("No file/file_pub pairs were found after decrypting mappings.");

                progress?.Report($"Found {entries.Count} DAT mapping(s).");
                return entries;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        /// <summary>
        /// Loads mappings using an already-exported SSF2.swf script/binaryData folder
        /// (avoids a second full FFDec dump during Prepare).
        /// </summary>
        public async Task<List<DatMappingEntry>> LoadMappingsFromMainExportAsync(
            string installRoot,
            string mainExportDir,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var resourceManagerScript = Directory.GetFiles(
                    mainExportDir, "ResourceManager.as", SearchOption.AllDirectories)
                .FirstOrDefault();
            var manifestBinary = Directory.GetFiles(mainExportDir, "*.bin", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Contains(
                    "ResourceManager_manifestJSON", StringComparison.OrdinalIgnoreCase));
            if (resourceManagerScript == null || manifestBinary == null)
                throw new InvalidOperationException(
                    "SSF2.swf export did not expose ResourceManager manifest data.");

            var resourceManagerText = await File.ReadAllTextAsync(resourceManagerScript, cancellationToken);
            var manifestKey = ExtractManifestKey(resourceManagerText);
            var manifestBlob = ReadEmbeddedText(manifestBinary);
            var manifestJson = DecodeJsonBlob(manifestBlob, manifestKey);
            var manifestObject = JObject.Parse(manifestJson);
            var manifestEntries = ParseMappingsJson(manifestJson);

            var mappingsDat = manifestObject["misc"]?["mappings"]?["file_pub"]?.ToString();
            if (string.IsNullOrWhiteSpace(mappingsDat))
                throw new InvalidOperationException(
                    "The embedded SSF2 manifest does not contain misc.mappings.file_pub.");

            progress?.Report($"Manifest maps mappings.ssf to {mappingsDat}.");
            var dataDir = Path.Combine(installRoot, "data");
            var mappingsPath = Path.Combine(dataDir, Path.GetFileName(mappingsDat));
            if (!File.Exists(mappingsPath))
                throw new FileNotFoundException(
                    $"The mappings resource {Path.GetFileName(mappingsDat)} was not found in data/.",
                    mappingsPath);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-mapfromexport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Unwrapping {Path.GetFileName(mappingsPath)}…");
                var wrapped = await File.ReadAllBytesAsync(mappingsPath, cancellationToken);
                var mappingsSwf = new PressorService().Decompress(wrapped, out _);
                var mappingsSwfPath = Path.Combine(work, "mappings.swf");
                await File.WriteAllBytesAsync(mappingsSwfPath, mappingsSwf, cancellationToken);

                progress?.Report("Extracting encrypted mappings metadata…");
                var mappingsExport = Path.Combine(work, "mappings");
                Directory.CreateDirectory(mappingsExport);
                await _jpexs.ExportAsync(
                    jpexsPath ?? "", mappingsSwfPath, mappingsExport, "binaryData", cancellationToken);
                var metadataBinary = Directory.GetFiles(mappingsExport, "*.bin", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(
                        "mappingsJSON", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.GetFiles(mappingsExport, "*.bin", SearchOption.AllDirectories)
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();
                if (metadataBinary == null)
                    throw new InvalidOperationException(
                        "The mappings DAT did not contain an exported binary metadata asset.");

                var metadataBlob = ReadEmbeddedText(metadataBinary);
                var mappingsJson = DecodeJsonBlob(metadataBlob, MappingsMetadataKey);
                var fullEntries = ParseMappingsJson(mappingsJson);
                var entries = manifestEntries.Concat(fullEntries)
                    .GroupBy(e => e.DatFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                if (entries.Count == 0)
                    throw new InvalidOperationException("No file/file_pub pairs were found after decrypting mappings.");

                progress?.Report($"Found {entries.Count} DAT mapping(s).");
                return entries;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        public static string ExtractManifestKey(string resourceManagerScript)
        {
            var match = Regex.Match(
                resourceManagerScript,
                @"manifestJSONData[\s\S]{0,500}?Utils\.d\([\s\S]{0,300}?,\s*""(?<key>[^""]+)""\)",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                return "";
            return match.Groups["key"].Value;
        }

        /// <summary>
        /// Decrypts <c>mappingsJSON</c> from mappings.swf. Each row is a category, a <c>file</c> name, and a <c>file_pub</c> DAT name.
        /// The category key is the <c>register("id", ...)</c> value.
        /// </summary>
        public static List<DatMappingEntry>? TryReadMappings(byte[] swf, string? gameRoot = null) =>
            TryReadNamedJson(swf, "mappingsJSON", gameRoot);

        /// <summary>
        /// Decrypts <c>manifestJSON</c> from <c>SSF2.swf</c>. Menu, misc, modes, and audio live here.
        /// <c>assists</c> is <c>DAT57.ssf</c>.
        /// </summary>
        public static List<DatMappingEntry>? TryReadManifest(byte[] swf, string? gameRoot = null) =>
            TryReadNamedJson(swf, "manifestJSON", gameRoot);

        private static List<DatMappingEntry>? TryReadNamedJson(byte[] swf, string binaryName, string? gameRoot)
        {
            try
            {
                var blob = SwfPackage.ExportedBinary(swf, binaryName);
                if (blob == null || blob.Length == 0)
                    return null;
                var text = Encoding.UTF8.GetString(blob).Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
                if (LooksLikeJson(text))
                {
                    var plain = ParseMappingsJson(text);
                    if (plain.Count > 0)
                        return plain;
                }
                foreach (var key in MappingKeys(swf, gameRoot))
                {
                    var json = TryDecodeMappings(text, key);
                    if (json == null)
                        continue;
                    var entries = ParseMappingsJson(json);
                    if (entries.Count > 0)
                        return entries;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryDecodeMappings(string text, string key)
        {
            try
            {
                var json = DecodeJsonBlob(text, key);
                if (LooksLikeJson(json))
                    return json;
            }
            catch
            {
                // This build wraps the same cipher in one more Base64 layer.
            }
            try
            {
                var once = Encoding.UTF8.GetString(Convert.FromBase64String(text));
                var twice = Encoding.UTF8.GetString(Convert.FromBase64String(once));
                var xored = XorShift(twice, key);
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(xored));
                return LooksLikeJson(json) ? json : null;
            }
            catch
            {
                return null;
            }
        }

        private static string XorShift(string encrypted, string key)
        {
            var chars = new char[encrypted.Length];
            for (var i = 0; i < encrypted.Length; i++)
            {
                var modulo = i % key.Length;
                var keyIndex = modulo == 0 ? key.Length - 1 : modulo - 1;
                chars[i] = (char)((encrypted[i] - key[keyIndex]) & 0xFFFF);
            }
            return new string(chars);
        }

        private static IEnumerable<string> MappingKeys(byte[] swf, string? gameRoot)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in EnumerateKeys(swf, gameRoot))
            {
                if (seen.Add(key))
                    yield return key;
            }
        }

        private static IEnumerable<string> EnumerateKeys(byte[] swf, string? gameRoot)
        {
            yield return MappingsMetadataKey;
            foreach (var key in Uuids(swf))
                yield return key;
            if (string.IsNullOrWhiteSpace(gameRoot))
                yield break;
            foreach (var name in new[] { "SSF2.swf", "SSF2Portable.swf" })
            {
                var path = Path.Combine(gameRoot, name);
                if (!File.Exists(path))
                    continue;
                byte[] plain;
                try
                {
                    plain = SwfPackage.ToSwf(File.ReadAllBytes(path));
                }
                catch
                {
                    continue;
                }
                foreach (var key in Uuids(plain))
                    yield return key;
            }
        }

        private static IEnumerable<string> Uuids(byte[] swf)
        {
            byte[] plain;
            try
            {
                plain = SwfPackage.ToSwf(swf);
            }
            catch
            {
                plain = swf;
            }
            var text = Encoding.ASCII.GetString(plain);
            foreach (Match match in Regex.Matches(
                         text,
                         "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"))
                yield return match.Value;
        }

        public static string DecodeJsonBlob(string blob, string? key)
        {
            blob = blob.Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
            if (LooksLikeJson(blob))
                return blob;

            if (!string.IsNullOrWhiteSpace(key))
            {
                try
                {
                    var decryptedBase64 = FlashUtilsD(blob, key);
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(decryptedBase64));
                    if (LooksLikeJson(json))
                        return json;
                }
                catch
                {
                    // Try the plain Base64 form below (used by unencrypted/IDK manifests).
                }
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(blob));
                if (LooksLikeJson(json))
                    return json;
            }
            catch
            {
                // Report a single useful error below.
            }

            throw new InvalidOperationException("Embedded mapping data could not be decoded as JSON.");
        }

        public static string FlashUtilsD(string encryptedBase64, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Decrypt key is required.", nameof(key));

            var encrypted = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedBase64));
            var chars = new char[encrypted.Length];
            for (var i = 0; i < encrypted.Length; i++)
            {
                // AS3: key.substr((i % key.length) - 1, 1);
                var modulo = i % key.Length;
                var keyIndex = modulo == 0 ? key.Length - 1 : modulo - 1;
                chars[i] = (char)((encrypted[i] - key[keyIndex]) & 0xFFFF);
            }
            return new string(chars);
        }

        /// <summary>AS3 Utils.e — inverse of <see cref="FlashUtilsD"/>.</summary>
        public static string FlashUtilsE(string plaintext, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Encrypt key is required.", nameof(key));

            var chars = new char[plaintext.Length];
            for (var i = 0; i < plaintext.Length; i++)
            {
                var modulo = i % key.Length;
                var keyIndex = modulo == 0 ? key.Length - 1 : modulo - 1;
                chars[i] = (char)((plaintext[i] + key[keyIndex]) & 0xFFFF);
            }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(chars)));
        }

        /// <summary>
        /// Rewrites every mapping entry's <c>file</c> field from .ssf → .swf (same length).
        /// </summary>
        public static string RewriteFileExtensionsToSwf(string mappingsJson)
        {
            var root = JObject.Parse(mappingsJson);
            foreach (var prop in root.Properties())
            {
                if (prop.Value is not JObject category) continue;
                foreach (var entry in category.Properties())
                {
                    if (entry.Value is not JObject obj) continue;
                    var file = obj["file"]?.ToString();
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    if (file.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                        obj["file"] = file[..^4] + ".swf";
                }
            }
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Encodes JSON the same way the public SSF2 build stores it:
        /// Utils.e(Base64(json), key) when a key is provided, else plain Base64(json).
        /// </summary>
        public static byte[] EncodeJsonBlob(string json, string? key)
        {
            var innerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            if (!string.IsNullOrWhiteSpace(key))
            {
                var outer = FlashUtilsE(innerBase64, key);
                return Encoding.UTF8.GetBytes(outer);
            }
            return Encoding.UTF8.GetBytes(innerBase64);
        }

        public static string FriendlySwfRelativePath(DatMappingEntry entry)
        {
            var relative = entry.FriendlyRelativePath.Replace('/', '\\').TrimStart('\\');
            if (relative.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
                relative = relative["data\\".Length..];
            if (relative.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                relative = relative[..^4] + ".swf";
            else if (!relative.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                relative = Path.ChangeExtension(relative, ".swf") ?? relative + ".swf";
            return relative;
        }

        /// <summary>
        /// Builds data-relative path for a mappings category + file field (e.g. character + mario.swf).
        /// </summary>
        public static string BuildDataRelativeSwfPath(string category, string file)
        {
            var relative = BuildFriendlyRelative(category, file.Replace('/', '\\'));
            if (relative.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
                relative = relative["data\\".Length..];
            if (relative.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                relative = relative[..^4] + ".swf";
            else if (!relative.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                relative = Path.ChangeExtension(relative, ".swf") ?? relative + ".swf";
            return relative;
        }

        private static string ReadEmbeddedText(string path) =>
            Encoding.UTF8.GetString(File.ReadAllBytes(path))
                .Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');

        private static bool LooksLikeJson(string value)
        {
            var trimmed = value.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) ||
                   trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        public static List<DatMappingEntry> ParseMappingsJson(string json)
        {
            var list = new List<DatMappingEntry>();
            var root = JObject.Parse(json);
            foreach (var prop in root.Properties())
            {
                if (prop.Value is not JObject category) continue;
                foreach (var entry in category.Properties())
                {
                    if (entry.Value is not JObject obj) continue;
                    var file = obj["file"]?.ToString();
                    var filePub = obj["file_pub"]?.ToString();
                    if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(filePub)) continue;
                    list.Add(new DatMappingEntry
                    {
                        Category = prop.Name,
                        FriendlyRelativePath = BuildFriendlyRelative(prop.Name, file!),
                        DatFileName = Path.GetFileName(filePub!),
                        Guid = obj["guid"]?.ToString()
                    });
                }
            }
            return list;
        }

        public static List<DatMappingEntry> ParseMappingsFromAs(string text)
        {
            var list = new List<DatMappingEntry>();
            // Match nearby file / file_pub string pairs in decompiled AS or XML constant pools.
            var filePubRx = new Regex(
                @"[""']file_pub[""']\s*[,:]\s*[""'](?<pub>DAT\d+\.ssf)[""']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var fileRx = new Regex(
                @"[""']file[""']\s*[,:]\s*[""'](?<file>[^""']+\.ssf)[""']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Sliding window: for each file_pub, look backward for nearest file=
            foreach (Match pubMatch in filePubRx.Matches(text))
            {
                var start = Math.Max(0, pubMatch.Index - 400);
                var window = text.Substring(start, pubMatch.Index - start);
                var fileMatch = fileRx.Matches(window).Cast<Match>().LastOrDefault();
                if (fileMatch == null) continue;
                var file = fileMatch.Groups["file"].Value;
                var pub = pubMatch.Groups["pub"].Value;
                list.Add(new DatMappingEntry
                {
                    FriendlyRelativePath = file.Replace('/', '\\'),
                    DatFileName = pub
                });
            }

            return list
                .GroupBy(e => e.DatFileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        public List<DatRenamePreview> BuildRenamePreview(string installRoot, IEnumerable<DatMappingEntry> entries)
        {
            var dataDir = Path.Combine(installRoot, "data");
            if (!Directory.Exists(dataDir))
                dataDir = installRoot;

            var list = new List<DatRenamePreview>();
            foreach (var e in entries)
            {
                var datPath = Path.Combine(dataDir, e.DatFileName);
                // Also check flat data root
                if (!File.Exists(datPath))
                {
                    var alt = Directory.GetFiles(dataDir, e.DatFileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (alt != null) datPath = alt;
                }

                var friendly = e.FriendlyRelativePath.Replace('/', '\\').TrimStart('\\');
                if (!friendly.StartsWith("data\\", StringComparison.OrdinalIgnoreCase) &&
                    !friendly.Contains('\\'))
                {
                    // Bare filename like mario.ssf — guess character/
                    friendly = Path.Combine("character", friendly);
                }
                if (friendly.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
                    friendly = friendly["data\\".Length..];

                var target = Path.Combine(dataDir, friendly);
                list.Add(new DatRenamePreview
                {
                    DatFullPath = datPath,
                    TargetFullPath = target,
                    DatExists = File.Exists(datPath),
                    TargetExists = File.Exists(target)
                });
            }
            return list;
        }

        public int ApplyRenames(IEnumerable<DatRenamePreview> preview, bool overwrite = false)
        {
            var count = 0;
            foreach (var p in preview)
            {
                if (!p.DatExists) continue;
                if (p.TargetExists && !overwrite) continue;
                var dir = Path.GetDirectoryName(p.TargetFullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Move(p.DatFullPath, p.TargetFullPath, overwrite);
                count++;
            }
            return count;
        }

        private static string BuildFriendlyRelative(string category, string file)
        {
            file = file.Replace('/', '\\');
            // Content-pack paths live under data/ssf2mm/ — never prefix with character/stage/.
            if (file.StartsWith("ssf2mm\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("expansions\\ssf2mm\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("data\\ssf2mm\\", StringComparison.OrdinalIgnoreCase))
            {
                if (file.StartsWith("expansions\\", StringComparison.OrdinalIgnoreCase))
                    return file["expansions\\".Length..];
                if (file.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
                    return file["data\\".Length..];
                return file;
            }
            // Real-expansion mapping fields: expansions/{id}/fighter.swf → expansions/character/{id}/…
            if (file.StartsWith("expansions\\", StringComparison.OrdinalIgnoreCase))
                return "expansions\\character\\" + file["expansions\\".Length..];
            // Package clones store "Display Name/id.swf" — still under data/character/.
            if (file.Contains('\\'))
            {
                var prefix = category switch
                {
                    "character" => "character\\",
                    "stage" => "stage\\",
                    "mode" or "modes" => "modes\\",
                    "menu" => "menu\\",
                    "misc" or "extra" => "misc\\",
                    "sound" or "audio" or "music" => "sound\\",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(prefix)
                    && !file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && !file.StartsWith("data\\" + prefix, StringComparison.OrdinalIgnoreCase)
                    && !file.StartsWith("expansions\\", StringComparison.OrdinalIgnoreCase))
                    return prefix + file;
                return file;
            }
            return category switch
            {
                "character" => Path.Combine("character", file),
                "stage" => Path.Combine("stage", file),
                "mode" or "modes" => Path.Combine("modes", file),
                "menu" => Path.Combine("menu", file),
                "misc" => Path.Combine("misc", file),
                "sound" or "audio" or "music" => Path.Combine("sound", file),
                "extra" => Path.Combine("misc", file),
                _ => file
            };
        }
    }
}
