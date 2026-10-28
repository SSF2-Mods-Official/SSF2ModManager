using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    public enum PressorMode
    {
        /// <summary>Primary Pressor/SSF2 format: zlib wrapping [l][n][garbage×n][swf].</summary>
        WithGarbageHeader,
        /// <summary>Fallback: zlib only, no l/n garbage header.</summary>
        ZlibOnly
    }

    public class PressorFileResult
    {
        public string SourcePath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public bool Success { get; set; }
        public string? ModeUsed { get; set; }
        public string? Error { get; set; }
    }

    public class PressorBatchResult
    {
        public List<PressorFileResult> Files { get; set; } = new();
        public int SuccessCount => Files.Count(f => f.Success);
        public int FailCount => Files.Count(f => !f.Success);
        public double ElapsedSeconds { get; set; }
    }

    public class PressorAssetSort
    {
        public string Id { get; set; } = "";
        public string Folder { get; set; } = "";
    }

    public class PressorModdableResult
    {
        public string OutputRoot { get; set; } = "";
        public string DataFolder { get; set; } = "";
        public PressorBatchResult Files { get; set; } = new();
    }

    /// <summary>
    /// C# reimplementation of Pressor 2.0 SSF↔SWF compress/decompress
    /// (zlib + optional l/n garbage header, matching game Resource.as / Pressor Main.as).
    /// </summary>
    public class PressorService
    {
        public byte[] Compress(byte[] swfBytes, PressorMode mode = PressorMode.WithGarbageHeader)
        {
            if (swfBytes == null || swfBytes.Length == 0)
                throw new ArgumentException("SWF data is empty.", nameof(swfBytes));

            byte[] payload;
            if (mode == PressorMode.WithGarbageHeader)
            {
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms))
                {
                    WriteInt32BigEndian(bw, swfBytes.Length); // l
                    WriteInt32BigEndian(bw, 0);               // n = 0, same as Pressor
                    bw.Write(swfBytes);
                }
                payload = ms.ToArray();
            }
            else
            {
                payload = swfBytes;
            }

            return ZlibCompress(payload);
        }

        public byte[] Decompress(byte[] ssfBytes, out PressorMode modeUsed)
        {
            if (ssfBytes == null || ssfBytes.Length == 0)
                throw new ArgumentException("SSF data is empty.", nameof(ssfBytes));

            // Already a raw SWF?
            if (LooksLikeSwf(ssfBytes))
            {
                modeUsed = PressorMode.ZlibOnly;
                return ssfBytes;
            }

            // Already-inflated Pressor payload (l/n header + SWF) without outer zlib.
            if (TryParseGarbageHeader(ssfBytes, out var nakedSwf))
            {
                modeUsed = PressorMode.WithGarbageHeader;
                return nakedSwf;
            }

            // Primary: zlib + garbage header
            try
            {
                var inflated = ZlibDecompress(ssfBytes);
                if (TryParseGarbageHeader(inflated, out var swf))
                {
                    modeUsed = PressorMode.WithGarbageHeader;
                    return swf;
                }

                // Inflated but no valid header — treat whole buffer as SWF (zlib-only fallback)
                if (LooksLikeSwf(inflated))
                {
                    modeUsed = PressorMode.ZlibOnly;
                    return inflated;
                }

            }
            catch
            {
                // fall through
            }

            throw new InvalidOperationException(
                "Could not decompress file (zlib+header and zlib-only fallback both failed).");
        }

        /// <summary>
        /// Checked garbage header: inflate, then drop the length and the skipped ints.
        /// Unchecked: inflate and return those bytes.
        /// </summary>
        public byte[] Decompress(byte[] ssfBytes, PressorMode mode)
        {
            if (ssfBytes == null || ssfBytes.Length == 0)
                throw new ArgumentException("SSF data is empty.", nameof(ssfBytes));

            if (LooksLikeSwf(ssfBytes))
                return ssfBytes;

            if (mode == PressorMode.ZlibOnly)
                return ZlibDecompress(ssfBytes);

            if (TryParseGarbageHeader(ssfBytes, out var naked))
                return naked;

            var inflated = ZlibDecompress(ssfBytes);
            if (!TryParseGarbageHeader(inflated, out var swf))
                throw new InvalidOperationException("Could not read the garbage header.");
            return swf;
        }

        public static bool TryParseGarbageHeader(byte[] inflated, out byte[] swf)
        {
            swf = Array.Empty<byte>();
            if (inflated.Length < 8) return false;

            try
            {
                using var ms = new MemoryStream(inflated);
                using var br = new BinaryReader(ms);
                var l = ReadInt32BigEndian(br);
                var n = ReadInt32BigEndian(br);
                if (l <= 0 || l > inflated.Length) return false;
                if (n < 0 || n > 1_000_000) return false;
                var skipBytes = (long)n * 4;
                if (ms.Position + skipBytes + l > inflated.Length) return false;
                for (var i = 0; i < n; i++)
                    ReadInt32BigEndian(br);
                swf = br.ReadBytes(l);
                return swf.Length == l && LooksLikeSwf(swf);
            }
            catch
            {
                return false;
            }
        }

        public static bool LooksLikeSwf(byte[] data) =>
            data.Length >= 3 &&
            ((data[0] == (byte)'F' || data[0] == (byte)'C' || data[0] == (byte)'Z') &&
             data[1] == (byte)'W' && data[2] == (byte)'S');

        public static void WriteInt32BigEndian(BinaryWriter writer, int value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        public static int ReadInt32BigEndian(BinaryReader reader)
        {
            var b0 = reader.ReadByte();
            var b1 = reader.ReadByte();
            var b2 = reader.ReadByte();
            var b3 = reader.ReadByte();
            return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        public async Task<PressorBatchResult> UncompressFilesAsync(
            IEnumerable<string> paths,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool useGarbageHeader = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new PressorBatchResult();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Files.Add(await UncompressOneAsync(path, useGarbageHeader, progress, cancellationToken));
            }
            sw.Stop();
            result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        public async Task<PressorBatchResult> CompressFilesAsync(
            IEnumerable<string> paths,
            bool useGarbageHeader = true,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new PressorBatchResult();
            var mode = useGarbageHeader ? PressorMode.WithGarbageHeader : PressorMode.ZlibOnly;
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Files.Add(await CompressOneAsync(path, mode, progress, cancellationToken));
            }
            sw.Stop();
            result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        public Task<PressorBatchResult> UncompressFolderAsync(
            string folder,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool useGarbageHeader = true)
        {
            var files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return ext.Equals(".ssf", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".ssfrec", StringComparison.OrdinalIgnoreCase);
                });
            return UncompressFilesAsync(files, progress, cancellationToken, useGarbageHeader);
        }

        public Task<PressorBatchResult> CompressFolderAsync(
            string folder,
            bool useGarbageHeader = true,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(folder, "*.swf", SearchOption.TopDirectoryOnly);
            return CompressFilesAsync(files, useGarbageHeader, progress, cancellationToken);
        }

        /// <summary>
        /// Compresses a .swf to a DAT/.ssf path without deleting or replacing the source .swf.
        /// </summary>
        public async Task<PressorFileResult> ConvertToDatAlongsideAsync(
            string swfPath,
            string outputDatPath,
            bool useGarbageHeader = true,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var r = new PressorFileResult { SourcePath = swfPath };
            try
            {
                if (!File.Exists(swfPath))
                    throw new FileNotFoundException("SWF not found.", swfPath);

                progress?.Report($"Converting {Path.GetFileName(swfPath)} → {Path.GetFileName(outputDatPath)}…");
                var data = await File.ReadAllBytesAsync(swfPath, cancellationToken);
                var mode = useGarbageHeader ? PressorMode.WithGarbageHeader : PressorMode.ZlibOnly;
                byte[] ssf;
                string modeUsed;
                try
                {
                    ssf = Compress(data, mode);
                    modeUsed = mode.ToString();
                }
                catch (Exception) when (mode == PressorMode.WithGarbageHeader)
                {
                    ssf = Compress(data, PressorMode.ZlibOnly);
                    modeUsed = PressorMode.ZlibOnly.ToString();
                }

                var dir = Path.GetDirectoryName(outputDatPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                await File.WriteAllBytesAsync(outputDatPath, ssf, cancellationToken);
                r.OutputPath = outputDatPath;
                r.Success = true;
                r.ModeUsed = modeUsed;
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Error = ex.Message;
                progress?.Report($"Failed {Path.GetFileName(swfPath)}: {ex.Message}");
            }
            return r;
        }

        private async Task<PressorFileResult> UncompressOneAsync(
            string path, bool useGarbageHeader, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var r = new PressorFileResult { SourcePath = path };
            try
            {
                progress?.Report($"Uncompressing {Path.GetFileName(path)}…");
                var data = await File.ReadAllBytesAsync(path, cancellationToken);
                var mode = useGarbageHeader ? PressorMode.WithGarbageHeader : PressorMode.ZlibOnly;
                var swf = Decompress(data, mode);
                var outPath = Path.ChangeExtension(path, ".swf");
                // Pressor replaces .ssf with .swf in-place (same basename)
                await File.WriteAllBytesAsync(outPath, swf, cancellationToken);
                if (!string.Equals(outPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    try { File.Delete(path); } catch { /* keep both if delete fails */ }
                }
                r.OutputPath = outPath;
                r.Success = true;
                r.ModeUsed = mode.ToString();
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Error = ex.Message;
                progress?.Report($"Failed {Path.GetFileName(path)}: {ex.Message}");
            }
            return r;
        }

        private async Task<PressorFileResult> CompressOneAsync(
            string path, PressorMode mode, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var r = new PressorFileResult { SourcePath = path };
            try
            {
                progress?.Report($"Compressing {Path.GetFileName(path)}…");
                var data = await File.ReadAllBytesAsync(path, cancellationToken);
                byte[] ssf;
                string modeUsed;
                try
                {
                    ssf = Compress(data, mode);
                    modeUsed = mode.ToString();
                }
                catch (Exception) when (mode == PressorMode.WithGarbageHeader)
                {
                    // Fallback: zlib-only
                    ssf = Compress(data, PressorMode.ZlibOnly);
                    modeUsed = PressorMode.ZlibOnly.ToString();
                }

                var outPath = CompressOutputPath(path);
                var outDir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);
                await File.WriteAllBytesAsync(outPath, ssf, cancellationToken);
                if (!string.Equals(outPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
                r.OutputPath = outPath;
                r.Success = true;
                r.ModeUsed = modeUsed;
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Error = ex.Message;
                progress?.Report($"Failed {Path.GetFileName(path)}: {ex.Message}");
            }
            return r;
        }

        /// <summary>
        /// A file named <c>tune-bgm_test.swf</c> is written to the parent folder as <c>tune.ssf</c>.
        /// A name with no dash stays in its folder and changes to <c>.ssf</c>.
        /// </summary>
        public static string CompressOutputPath(string swfPath)
        {
            var name = Path.GetFileNameWithoutExtension(swfPath);
            var dash = name.LastIndexOf('-');
            if (dash <= 0)
                return Path.ChangeExtension(swfPath, ".ssf");

            var stem = name[..dash];
            var parent = Path.GetDirectoryName(swfPath);
            var data = string.IsNullOrEmpty(parent) ? null : Path.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(data))
                data = parent ?? "";
            return Path.Combine(data, stem + ".ssf");
        }

        /// <summary>
        /// Folder for a DAT that mappings do not name. The id is the string after <c>id</c> when the next string starts with <c>guid</c>.
        /// </summary>
        public static PressorAssetSort ReadAssetSort(byte[] swf)
        {
            var strings = AsciiStrings(swf ?? Array.Empty<byte>());
            var id = RegisteredId(strings) ?? "DunderMuffin";
            string folder;
            if (id.IndexOf("bgm_", StringComparison.Ordinal) >= 0)
                folder = "music";
            else if (id.IndexOf("menu", StringComparison.Ordinal) >= 0)
                folder = "menu";
            else if (id.IndexOf("mode", StringComparison.Ordinal) >= 0)
                folder = "mode";
            else
                folder = "misc";

            return new PressorAssetSort { Id = id, Folder = folder };
        }

        private static string? RegisteredId(List<string> strings)
        {
            for (var n = 0; n < strings.Count - 2; n++)
            {
                if (strings[n] != "id" || !IsAssetId(strings[n + 1]))
                    continue;
                if (strings[n + 2].StartsWith("guid", StringComparison.Ordinal))
                    return strings[n + 1];
            }

            var flaRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in strings)
            {
                var cut = text.IndexOf("_fla", StringComparison.Ordinal);
                if (cut > 0)
                    flaRoots.Add(text[..cut]);
            }
            for (var n = 0; n < strings.Count - 1; n++)
            {
                if (strings[n] != "id" || !strings[n + 1].StartsWith("guid", StringComparison.Ordinal))
                    continue;
                var start = Math.Max(0, n - 2500);
                for (var i = n - 1; i >= start; i--)
                {
                    if (flaRoots.Contains(strings[i]) && IsAssetId(strings[i]))
                        return strings[i];
                }
            }
            if (flaRoots.Count == 1)
            {
                var only = flaRoots.First();
                if (IsAssetId(only))
                    return only;
            }

            for (var n = strings.Count - 2; n >= 0; n--)
            {
                if (strings[n] == "id" && IsAssetId(strings[n + 1]) && strings[n + 1].Length >= 4)
                    return strings[n + 1];
            }
            return null;
        }

        /// <summary>
        /// Folder is the mappings category. The file name is the <c>file</c> field with a <c>.swf</c> extension.
        /// The category <c>extra</c> is misc.
        /// </summary>
        public static string PlacementRelative(DatMappingEntry entry)
        {
            var name = Path.GetFileName(entry.FriendlyRelativePath.Replace('/', '\\'));
            if (name.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                name = name[..^4] + ".swf";
            else if (!name.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                name = Path.ChangeExtension(name, ".swf") ?? name + ".swf";
            var folder = string.IsNullOrWhiteSpace(entry.Category) ? "misc" : entry.Category;
            if (folder.Equals("extra", StringComparison.OrdinalIgnoreCase))
                folder = "misc";
            return Path.Combine(folder, name);
        }

        public async Task<PressorModdableResult> CreateModdableBuildAsync(
            string sourceFolder,
            bool useGarbageHeader = true,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? outputParent = null)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException("That game folder was not found.");

            var parent = string.IsNullOrWhiteSpace(outputParent)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : outputParent;
            Directory.CreateDirectory(parent);
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var dest = Path.Combine(parent, "SSF2 Beta (Moddable) Build " + stamp);
            progress?.Report("Copying " + sourceFolder + "…");
            await Task.Run(() => CopyDirectory(sourceFolder, dest), cancellationToken);

            var data = Path.Combine(dest, "data");
            if (!Directory.Exists(data))
                throw new DirectoryNotFoundException("That folder has no data folder.");

            foreach (var bucket in new[] { "music", "stage", "character", "menu", "mode", "misc" })
                Directory.CreateDirectory(Path.Combine(data, bucket));

            var files = Directory.GetFiles(data)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return ext.Equals(".ssf", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".ssfrec", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            var batch = new PressorBatchResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mode = useGarbageHeader ? PressorMode.WithGarbageHeader : PressorMode.ZlibOnly;
            var loaded = new List<(string Path, byte[] Swf)>();
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    progress?.Report($"Uncompressing {Path.GetFileName(path)}…");
                    var raw = await File.ReadAllBytesAsync(path, cancellationToken);
                    loaded.Add((path, Decompress(raw, mode)));
                }
                catch (Exception ex)
                {
                    batch.Files.Add(new PressorFileResult
                    {
                        SourcePath = path,
                        Success = false,
                        Error = ex.Message
                    });
                    progress?.Report($"Failed {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            var combined = new List<DatMappingEntry>();
            foreach (var item in loaded)
            {
                var part = DatMappingService.TryReadMappings(item.Swf, dest);
                if (part != null)
                    combined.AddRange(part);
            }
            foreach (var mainName in new[] { "SSF2.swf", "SSF2Portable.swf" })
            {
                var mainPath = Path.Combine(dest, mainName);
                if (!File.Exists(mainPath))
                    continue;
                var manifest = DatMappingService.TryReadManifest(await File.ReadAllBytesAsync(mainPath, cancellationToken), dest);
                if (manifest != null)
                    combined.AddRange(manifest);
                break;
            }
            List<DatMappingEntry>? mappings = combined.Count == 0
                ? null
                : combined
                    .GroupBy(e => e.DatFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            progress?.Report("mappings " + (mappings?.Count ?? 0));

            foreach (var item in loaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var one = new PressorFileResult { SourcePath = item.Path };
                try
                {
                    var fileName = Path.GetFileName(item.Path);
                    var entry = mappings?.FirstOrDefault(e =>
                        string.Equals(e.DatFileName, fileName, StringComparison.OrdinalIgnoreCase));
                    string relative;
                    if (entry != null)
                        relative = PlacementRelative(entry);
                    else
                    {
                        var sort = ReadAssetSort(item.Swf);
                        relative = Path.Combine(sort.Folder, sort.Id + ".swf");
                    }
                    var outPath = Path.Combine(data, relative);
                    var outDir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(outDir))
                        Directory.CreateDirectory(outDir);
                    await File.WriteAllBytesAsync(outPath, item.Swf, cancellationToken);
                    if (File.Exists(item.Path))
                        File.Delete(item.Path);
                    one.OutputPath = outPath;
                    one.Success = true;
                    one.ModeUsed = mode.ToString();
                    progress?.Report(Path.GetFileName(outPath) + " → " + Path.GetDirectoryName(relative));
                }
                catch (Exception ex)
                {
                    one.Success = false;
                    one.Error = ex.Message;
                    progress?.Report($"Failed {Path.GetFileName(item.Path)}: {ex.Message}");
                }
                batch.Files.Add(one);
            }
            sw.Stop();
            batch.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            progress?.Report("Moddable build " + dest);
            return new PressorModdableResult
            {
                OutputRoot = dest,
                DataFolder = data,
                Files = batch
            };
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private static List<string> AsciiStrings(byte[] bytes)
        {
            var strings = new List<string>();
            var i = 0;
            while (i < bytes.Length)
            {
                if (bytes[i] >= 32 && bytes[i] < 127)
                {
                    var start = i;
                    while (i < bytes.Length && bytes[i] >= 32 && bytes[i] < 127)
                        i++;
                    if (i - start >= 2 && i - start <= 80)
                        strings.Add(System.Text.Encoding.ASCII.GetString(bytes, start, i - start));
                }
                else
                {
                    i++;
                }
            }
            return strings;
        }

        private static bool IsAssetId(string text)
        {
            if (text.Length < 1 || text.Length > 64)
                return false;
            if (text is "String" or "Boolean" or "int" or "uint" or "Number" or "Array" or "Object" or "Class" or "void")
                return false;
            if (!char.IsLetter(text[0]))
                return false;
            foreach (var c in text)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }
            return true;
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        private static byte[] ZlibDecompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
    }
}
