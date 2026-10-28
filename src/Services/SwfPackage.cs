using System.IO;
using System.IO.Compression;
using System.Text;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Turns an imported .swf or .ssf into a plain SWF.
    /// A .ssf may be a compressed SWF, a zlib package with the 8-byte length header, or both.
    /// </summary>
    public static class SwfPackage
    {
        public static byte[] ToSwf(byte[] raw)
        {
            if (raw == null || raw.Length < 8)
                throw new InvalidDataException("That file is not a SWF or an SSF2 .ssf package.");
            if (IsSwf(raw))
                return UnpackSwf(raw);

            var packaged = DataFolderCompression.TryUnwrap(raw);
            if (packaged != null)
                return ToSwf(packaged);

            try
            {
                using var input = new MemoryStream(raw);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
                using var inflated = new MemoryStream();
                zlib.CopyTo(inflated);
                var body = inflated.ToArray();
                if (IsSwf(body) || DataFolderCompression.TryUnwrap(body) != null)
                    return ToSwf(body);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch
            {
            }

            throw new InvalidDataException("That file is not a SWF or an SSF2 .ssf package.");
        }

        private static byte[] UnpackSwf(byte[] raw)
        {
            if (raw[0] == (byte)'F')
                return raw;
            if (raw[0] == (byte)'C')
                return Inflate(raw, zlib: true);
            if (raw[0] == (byte)'Z')
                throw new InvalidDataException("That SWF is LZMA-compressed (ZWS). Save it as FWS or CWS, then import it.");
            return raw;
        }

        private static byte[] Inflate(byte[] raw, bool zlib)
        {
            using var payload = new MemoryStream(raw, 8, raw.Length - 8, writable: false);
            using Stream inflater = zlib
                ? new ZLibStream(payload, CompressionMode.Decompress, leaveOpen: true)
                : new DeflateStream(payload, CompressionMode.Decompress, leaveOpen: true);
            using var body = new MemoryStream();
            inflater.CopyTo(body);
            var data = body.ToArray();
            var swf = new byte[8 + data.Length];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            swf[3] = raw[3];
            var length = swf.Length;
            swf[4] = (byte)length;
            swf[5] = (byte)(length >> 8);
            swf[6] = (byte)(length >> 16);
            swf[7] = (byte)(length >> 24);
            Buffer.BlockCopy(data, 0, swf, 8, data.Length);
            return swf;
        }

        /// <summary>
        /// FFDec only opens FWS, CWS, and ZWS. An .ssf package is written as a plain SWF in <paramref name="workDirectory"/>.
        /// </summary>
        public static string EnsurePlainSwf(string sourcePath, string workDirectory)
        {
            var raw = File.ReadAllBytes(sourcePath);
            if (IsSwf(raw))
                return sourcePath;
            Directory.CreateDirectory(workDirectory);
            var dest = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".swf");
            File.WriteAllBytes(dest, ToSwf(raw));
            return dest;
        }

        private static bool IsSwf(byte[] raw)
        {
            return raw.Length >= 8
                && (raw[0] == (byte)'F' || raw[0] == (byte)'C' || raw[0] == (byte)'Z')
                && raw[1] == (byte)'W'
                && raw[2] == (byte)'S';
        }

        /// <summary>A <c>backgrounds</c> list is a stage. <c>statsName</c> or <c>jumpSpeed</c> in that file does not make it a character.</summary>
        public static bool IsStage(byte[] swf) =>
            IndexOf(swf, "backgrounds"u8) >= 0;

        public static bool IsCharacter(byte[] swf) =>
            !IsStage(swf) && (IndexOf(swf, "jumpSpeed"u8) >= 0 || IndexOf(swf, "statsName"u8) >= 0);

        /// <summary>
        /// SymbolClass name stored for character id 0. That is the document class.
        /// <c>Main</c> is a beta file. A name ending in <c>MainTimeline</c> is a Flash timeline export.
        /// </summary>
        public static string? DocumentClass(byte[] raw)
        {
            byte[] swf;
            try
            {
                swf = ToSwf(raw);
            }
            catch
            {
                return null;
            }
            if (swf.Length < 12 || swf[0] != (byte)'F')
                return null;

            var pos = 8;
            if (!SkipRect(swf, ref pos))
                return null;
            pos += 4;
            if (pos > swf.Length)
                return null;

            string? found = null;
            while (pos + 2 <= swf.Length)
            {
                var code = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var type = code >> 6;
                var length = code & 0x3F;
                if (length == 0x3F)
                {
                    if (pos + 4 > swf.Length)
                        return found;
                    length = swf[pos]
                        | (swf[pos + 1] << 8)
                        | (swf[pos + 2] << 16)
                        | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (length < 0 || pos + length > swf.Length)
                    return found;
                if (type == 0)
                    break;
                if (type == 76)
                    found = SymbolClassDocument(swf, pos, length) ?? found;
                pos += length;
            }
            return found;
        }

        /// <summary>
        /// DefineBinaryData bytes for the SymbolClass name that contains <paramref name="namePart"/>.
        /// That is the <c>mappingsJSON</c> blob inside the mappings SWF.
        /// </summary>
        public static byte[]? ExportedBinary(byte[] raw, string namePart)
        {
            byte[] swf;
            try
            {
                swf = ToSwf(raw);
            }
            catch
            {
                return null;
            }
            if (swf.Length < 12 || swf[0] != (byte)'F')
                return null;

            var pos = 8;
            if (!SkipRect(swf, ref pos))
                return null;
            pos += 4;
            if (pos > swf.Length)
                return null;

            var blobs = new Dictionary<int, byte[]>();
            var names = new Dictionary<int, string>();
            while (pos + 2 <= swf.Length)
            {
                var code = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var type = code >> 6;
                var length = code & 0x3F;
                if (length == 0x3F)
                {
                    if (pos + 4 > swf.Length)
                        return null;
                    length = swf[pos]
                        | (swf[pos + 1] << 8)
                        | (swf[pos + 2] << 16)
                        | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (length < 0 || pos + length > swf.Length)
                    return null;
                if (type == 0)
                    break;
                if (type == 87 && length >= 6)
                {
                    var id = swf[pos] | (swf[pos + 1] << 8);
                    var data = new byte[length - 6];
                    Buffer.BlockCopy(swf, pos + 6, data, 0, data.Length);
                    blobs[id] = data;
                }
                else if (type == 76)
                {
                    foreach (var pair in ReadSymbolClassPairs(swf, pos, length))
                        names[pair.Id] = pair.Name;
                }
                pos += length;
            }

            foreach (var pair in names)
            {
                if (pair.Value.Contains(namePart, StringComparison.OrdinalIgnoreCase)
                    && blobs.TryGetValue(pair.Key, out var data))
                    return data;
            }
            return null;
        }

        /// <summary>Every class name in SymbolClass tags. These are the names a loaded SWF exports.</summary>
        public static IReadOnlyList<string> SymbolClassNames(byte[] raw)
        {
            var names = new List<string>();
            byte[] swf;
            try
            {
                swf = ToSwf(raw);
            }
            catch
            {
                return names;
            }
            if (swf.Length < 12 || swf[0] != (byte)'F')
                return names;

            var pos = 8;
            if (!SkipRect(swf, ref pos))
                return names;
            pos += 4;
            if (pos > swf.Length)
                return names;

            while (pos + 2 <= swf.Length)
            {
                var code = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var type = code >> 6;
                var length = code & 0x3F;
                if (length == 0x3F)
                {
                    if (pos + 4 > swf.Length)
                        return names;
                    length = swf[pos]
                        | (swf[pos + 1] << 8)
                        | (swf[pos + 2] << 16)
                        | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (length < 0 || pos + length > swf.Length)
                    return names;
                if (type == 0)
                    break;
                if (type == 76)
                    names.AddRange(ReadSymbolClassNames(swf, pos, length));
                pos += length;
            }
            return names;
        }

        /// <summary>
        /// Each <c>register("key", "value")</c> call in the SWF's ABC, in code order. Value is null when the second
        /// argument is not a string literal (<c>register("stage", draculascastle)</c>).
        /// </summary>
        public static IReadOnlyList<(string Key, string? Value)> RegisterCalls(byte[] raw)
        {
            var calls = new List<(string, string?)>();
            byte[] swf;
            try { swf = ToSwf(raw); }
            catch { return calls; }
            if (swf.Length < 12 || swf[0] != (byte)'F')
                return calls;
            var pos = 8;
            if (!SkipRect(swf, ref pos))
                return calls;
            pos += 4;
            while (pos + 2 <= swf.Length)
            {
                var code = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var type = code >> 6;
                var length = code & 0x3F;
                if (length == 0x3F)
                {
                    if (pos + 4 > swf.Length)
                        break;
                    length = swf[pos] | (swf[pos + 1] << 8) | (swf[pos + 2] << 16) | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (length < 0 || pos + length > swf.Length || type == 0)
                    break;
                if (type == 82 || type == 72)
                {
                    var abc = pos;
                    var end = pos + length;
                    if (type == 82)
                    {
                        abc += 4;
                        while (abc < end && swf[abc] != 0)
                            abc++;
                        abc++;
                    }
                    try { ReadRegisterCalls(swf, abc, end, calls); }
                    catch (IndexOutOfRangeException) { }
                }
                pos += length;
            }
            return calls;
        }

        private static void ReadRegisterCalls(byte[] b, int p, int end, List<(string, string?)> calls)
        {
            int U30()
            {
                int result = 0, shift = 0;
                for (var i = 0; i < 5; i++)
                {
                    var v = b[p++];
                    result |= (v & 0x7F) << shift;
                    if ((v & 0x80) == 0)
                        break;
                    shift += 7;
                }
                return result;
            }

            p += 4;
            for (int i = 1, n = U30(); i < n; i++) U30();
            for (int i = 1, n = U30(); i < n; i++) U30();
            var doubles = U30();
            p += Math.Max(0, doubles - 1) * 8;
            var stringCount = U30();
            var strings = new string[Math.Max(1, stringCount)];
            for (var i = 1; i < stringCount; i++)
            {
                var len = U30();
                strings[i] = System.Text.Encoding.UTF8.GetString(b, p, len);
                p += len;
            }
            var nsCount = U30();
            for (var i = 1; i < nsCount; i++) { p++; U30(); }
            var setCount = U30();
            for (var i = 1; i < setCount; i++) { for (int j = 0, c = U30(); j < c; j++) U30(); }
            var register = new HashSet<int>();
            var nameCount = U30();
            for (var i = 1; i < nameCount; i++)
            {
                var kind = b[p++];
                var name = 0;
                switch (kind)
                {
                    case 0x07: case 0x0D: U30(); name = U30(); break;
                    case 0x0F: case 0x10: name = U30(); break;
                    case 0x11: case 0x12: break;
                    case 0x09: case 0x0E: name = U30(); U30(); break;
                    case 0x1B: case 0x1C: U30(); break;
                    case 0x1D: U30(); for (int j = 0, c = U30(); j < c; j++) U30(); break;
                    default: return;
                }
                if (name > 0 && name < strings.Length && strings[name] == "register")
                    register.Add(i);
            }
            if (register.Count == 0)
                return;

            string? Str(int index) => index > 0 && index < strings.Length ? strings[index] : null;
            while (p < end - 2)
            {
                if (b[p] != 0x5D)
                {
                    p++;
                    continue;
                }
                var at = p;
                p++;
                if (!register.Contains(U30()) || p >= end || b[p] != 0x2C)
                {
                    p = at + 1;
                    continue;
                }
                p++;
                var key = Str(U30());
                string? value = null;
                if (p < end && b[p] == 0x2C)
                {
                    p++;
                    value = Str(U30());
                }
                if (key != null)
                    calls.Add((key, value));
            }
        }

        public static bool IsBetaDocument(string? documentClass) =>
            !string.IsNullOrEmpty(documentClass)
            && (documentClass.Equals("Main", StringComparison.Ordinal)
                || documentClass.EndsWith(".Main", StringComparison.Ordinal));

        public static bool IsTimelineDocument(string? documentClass) =>
            !string.IsNullOrEmpty(documentClass)
            && (documentClass.Equals("MainTimeline", StringComparison.Ordinal)
                || documentClass.EndsWith(".MainTimeline", StringComparison.Ordinal));

        private static bool SkipRect(byte[] swf, ref int pos)
        {
            if (pos >= swf.Length)
                return false;
            var nbits = swf[pos] >> 3;
            var totalBits = 5 + nbits * 4;
            pos += (totalBits + 7) / 8;
            return pos <= swf.Length;
        }

        private static List<(int Id, string Name)> ReadSymbolClassPairs(byte[] swf, int offset, int length)
        {
            var pairs = new List<(int, string)>();
            if (length < 2)
                return pairs;
            var end = offset + length;
            var count = swf[offset] | (swf[offset + 1] << 8);
            var p = offset + 2;
            for (var i = 0; i < count; i++)
            {
                if (p + 2 > end)
                    break;
                var id = swf[p] | (swf[p + 1] << 8);
                p += 2;
                var nameStart = p;
                while (p < end && swf[p] != 0)
                    p++;
                if (p >= end)
                    break;
                if (p > nameStart)
                    pairs.Add((id, Encoding.UTF8.GetString(swf, nameStart, p - nameStart)));
                p++;
            }
            return pairs;
        }

        private static List<string> ReadSymbolClassNames(byte[] swf, int offset, int length)
        {
            var names = new List<string>();
            if (length < 2)
                return names;
            var end = offset + length;
            var count = swf[offset] | (swf[offset + 1] << 8);
            var p = offset + 2;
            for (var i = 0; i < count; i++)
            {
                if (p + 2 > end)
                    break;
                p += 2;
                var nameStart = p;
                while (p < end && swf[p] != 0)
                    p++;
                if (p >= end)
                    break;
                if (p > nameStart)
                    names.Add(Encoding.UTF8.GetString(swf, nameStart, p - nameStart));
                p++;
            }
            return names;
        }

        private static string? SymbolClassDocument(byte[] swf, int offset, int length)
        {
            if (length < 2)
                return null;
            var end = offset + length;
            var count = swf[offset] | (swf[offset + 1] << 8);
            var p = offset + 2;
            string? found = null;
            for (var i = 0; i < count; i++)
            {
                if (p + 2 > end)
                    break;
                var charId = swf[p] | (swf[p + 1] << 8);
                p += 2;
                var nameStart = p;
                while (p < end && swf[p] != 0)
                    p++;
                if (p >= end)
                    break;
                if (charId == 0)
                    found = Encoding.UTF8.GetString(swf, nameStart, p - nameStart);
                p++;
            }
            return found;
        }

        private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
        {
            if (haystack == null || needle.Length == 0 || haystack.Length < needle.Length)
                return -1;
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                    return i;
            }
            return -1;
        }
    }
}
