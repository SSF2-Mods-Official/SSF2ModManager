using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Turns data-folder SWFs into the package <c>Resource.initialLoadComplete</c> already uncompresses.
    /// The file is zlib of a big-endian length, a big-endian skip count of 0, then the SWF bytes.
    /// Compressed files use the .ssf name. Plain files use .swf.
    /// engine and ssf2mm stay as they are.
    /// </summary>
    public static class DataFolderCompression
    {
        public static bool IsCompressed(string shell)
        {
            var character = Path.Combine(shell, "data", "character");
            if (!Directory.Exists(character))
                return false;
            var ssf = Directory.GetFiles(character, "*.ssf").Length;
            var swf = Directory.GetFiles(character, "*.swf").Length;
            return ssf > 0 && ssf >= swf;
        }

        public static int Apply(string shell, bool compress)
        {
            var data = Path.Combine(shell, "data");
            if (!Directory.Exists(data))
                return 0;
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(data, compress ? "*.swf" : "*.ssf", SearchOption.AllDirectories))
            {
                if (Skip(data, file))
                    continue;
                var name = Path.GetFileName(file);
                var destExt = compress ? ".ssf" : ".swf";
                var dest = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + destExt);
                var bytes = File.ReadAllBytes(file);
                byte[] next;
                if (compress)
                {
                    if (LooksLikePackage(bytes))
                        next = bytes;
                    else
                        next = Wrap(bytes);
                }
                else
                {
                    next = TryUnwrap(bytes) ?? bytes;
                }
                File.WriteAllBytes(dest, next);
                if (!string.Equals(file, dest, StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
                changed.Add(name);
            }
            RewriteMappings(shell, compress ? ".swf" : ".ssf", compress ? ".ssf" : ".swf", changed);
            return changed.Count;
        }

        private static bool Skip(string data, string file)
        {
            var rel = Path.GetRelativePath(data, file);
            return rel.StartsWith("engine" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("ssf2mm" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static byte[] Wrap(byte[] swf)
        {
            var payload = new byte[8 + swf.Length];
            WriteBe(payload, 0, swf.Length);
            WriteBe(payload, 4, 0);
            Buffer.BlockCopy(swf, 0, payload, 8, swf.Length);
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(payload, 0, payload.Length);
            return output.ToArray();
        }

        public static byte[]? TryUnwrap(byte[] packed)
        {
            try
            {
                using var input = new MemoryStream(packed);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
                using var raw = new MemoryStream();
                zlib.CopyTo(raw);
                var body = raw.ToArray();
                if (body.Length < 8)
                    return null;
                var length = ReadBe(body, 0);
                var skip = ReadBe(body, 4);
                if (length < 0 || skip < 0)
                    return null;
                var start = 8 + skip * 4;
                if (start < 8 || start + length > body.Length)
                    return null;
                var swf = new byte[length];
                Buffer.BlockCopy(body, start, swf, 0, length);
                if (swf.Length < 3)
                    return null;
                if (swf[0] != (byte)'F' && swf[0] != (byte)'C' && swf[0] != (byte)'Z')
                    return null;
                if (swf[1] != (byte)'W' || swf[2] != (byte)'S')
                    return null;
                return swf;
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikePackage(byte[] bytes) => TryUnwrap(bytes) != null;

        private static void RewriteMappings(string shell, string fromExt, string toExt, HashSet<string> names)
        {
            var path = Path.Combine(shell, "data", "misc", "mappings.json");
            if (!File.Exists(path) || names.Count == 0)
                return;
            var root = JToken.Parse(File.ReadAllText(path));
            foreach (var prop in root.Children().SelectMany(c => new[] { c }.Concat(c is JContainer box ? box.Descendants() : Enumerable.Empty<JToken>())).OfType<JProperty>())
            {
                if (!prop.Name.Equals("file", StringComparison.Ordinal))
                    continue;
                var value = prop.Value.Type == JTokenType.String ? prop.Value.ToString() : "";
                if (!value.EndsWith(fromExt, StringComparison.OrdinalIgnoreCase))
                    continue;
                var fileName = Path.GetFileName(value);
                if (!names.Contains(fileName))
                    continue;
                prop.Value = value[..^fromExt.Length] + toExt;
            }
            File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static void WriteBe(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static int ReadBe(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
        }
    }
}
